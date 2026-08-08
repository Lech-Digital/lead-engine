using Coldstart.Application.Ports;

namespace Coldstart.Application.Harvest;

// Phase 1 of the pipeline, extracted verbatim from HarvestLeads.RunPipelineAsync: harvest
// + veto + rank + dedup, before any LLM spend. HarvestLeads still owns the per-run setup
// (suppression list, already-held companies) and the funnel; this class only runs the loop
// and hands back the vetoed, ranked candidate pool for phase 2 (triage) to consume.
internal sealed class CandidateCollector(IEnumerable<ILeadSource> sources, ILeadRepository leads)
{
    public async Task<List<(RawItem item, SignalResult signal, double rank)>> CollectAsync(
        NicheOptions runNiche,
        IReadOnlyList<string> disqualifiers,
        Func<string, bool> allowsSource,
        ISet<string> alreadyHeldCompanies,
        IReadOnlyCollection<string> suppressed,
        HarvestFunnel funnel,
        CancellationToken ct)
    {
        // Phase 1: harvest + veto + rank + dedup (no LLM spend yet). MinScore is no longer a
        // drop-threshold — every candidate that survives the veto is ranked and queued; the
        // spend cap (MaxEnrichPerRun below) decides who actually gets enriched this run.
        var candidates = new List<(RawItem item, SignalResult signal, double rank)>();
        var seen = new HashSet<string>();   // fingerprints already taken this run
        foreach (var source in sources.Where(s => s.Enabled && allowsSource(s.Name)))
        {
            IReadOnlyList<RawItem> items;
            try { items = await source.FetchAsync(runNiche, ct); }
            catch { continue; }   // a failing source must not abort the run
            foreach (var item in items)
            {
                funnel.CountHarvested();
                if (SuppressionMatcher.IsMatch(suppressed, item.Title, item.Author)) { funnel.CountDropped("suppressed"); continue; }
                var vetoReason = CandidateVeto.Check(item, disqualifiers, alreadyHeldCompanies, DateTimeOffset.UtcNow, TimeSpan.FromDays(14));
                if (vetoReason is not null) { funnel.CountDropped(vetoReason); continue; }
                var signal = SignalScorer.Score(item, runNiche, source.BaseScore, source.ScoreWeight);
                var rank = CandidatePriority.Rank(signal, source.BaseScore, ParsedDate(item), DateTimeOffset.UtcNow);
                funnel.CountPrioritised();
                if (await leads.ExistsByFingerprintAsync(item.Fingerprint, ct)) { funnel.CountDuplicateFingerprint(); continue; }
                // The same item can surface twice in one run (e.g. an HN comment matching several
                // queries). Dedup within the run too, or the second save collides on the unique
                // fingerprint index. Empty fingerprints are exempt (mirrors the partial DB index).
                if (item.Fingerprint.Length != 0 && !seen.Add(item.Fingerprint)) { funnel.CountDuplicateFingerprint(); continue; }
                candidates.Add((item, signal, rank));
            }
        }

        return candidates.OrderByDescending(c => c.rank).ToList();
    }

    // Several sources emit no usable CreatedAt at all — null here (not "now", not "epoch")
    // is what lets CandidatePriority give it the neutral recency value instead of a penalty.
    private static DateTimeOffset? ParsedDate(RawItem item) =>
        DateTimeOffset.TryParse(item.CreatedAt, out var parsed) ? parsed : null;
}

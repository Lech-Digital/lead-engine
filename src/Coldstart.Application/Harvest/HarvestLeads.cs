using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

internal sealed class HarvestLeads(
    IEnumerable<ILeadSource> sources,
    ILeadEnricher enricher,
    ILeadRepository leads,
    ICustomerRepository customers,
    NicheOptions niche,
    ITenantScope tenantScope) : IHarvestLeads
{
    public async Task<HarvestReport> RunAsync(CancellationToken ct)
    {
        // Console path: resolve the configured target tenant, build sender from ITS profile (unchanged).
        var target = await ResolveTargetCustomerAsync(ct);
        tenantScope.SetCurrent(target.Id.Value);
        var sender = new SenderContext(target.Profile.Brief, target.Profile.MentionSourceTool);
        return await RunPipelineAsync(target.Id, HarvestTargeting.FromNiche(niche), sender, ct);
    }

    public async Task<HarvestReport> RunForCustomerAsync(Guid customerId, HarvestTargeting targeting, CancellationToken ct)
    {
        var cid = new CustomerId(customerId);
        tenantScope.SetCurrent(customerId);
        var customer = await customers.GetAsync(cid, ct);
        var sender = customer is null
            ? SenderContext.None
            : new SenderContext(customer.Profile.Brief, customer.Profile.MentionSourceTool,
                customer.Profile.PitchLine, customer.Profile.CtaLine);
        return await RunPipelineAsync(cid, targeting, sender, ct);
    }

    private async Task<HarvestReport> RunPipelineAsync(CustomerId cid, HarvestTargeting targeting, SenderContext sender, CancellationToken ct)
    {
        var runNiche = BuildRunNiche(targeting);
        int harvested = 0, scoredIn = 0, deduped = 0, enriched = 0, saved = 0, rejected = 0;

        // Phase 1: harvest + cheap score gate + dedup (no LLM spend yet).
        var candidates = new List<(RawItem item, SignalResult signal)>();
        var seen = new HashSet<string>();   // fingerprints already taken this run
        foreach (var source in sources.Where(s => s.Enabled))
        {
            IReadOnlyList<RawItem> items;
            try { items = await source.FetchAsync(ct); }
            catch { continue; }   // a failing source must not abort the run
            foreach (var item in items)
            {
                harvested++;
                var signal = SignalScorer.Score(item, runNiche, source.BaseScore, source.ScoreWeight);
                if (signal.Score < runNiche.MinScore) continue;
                scoredIn++;
                if (await leads.ExistsByFingerprintAsync(item.Fingerprint, ct)) { deduped++; continue; }
                // The same item can surface twice in one run (e.g. an HN comment matching several
                // queries). Dedup within the run too, or the second save collides on the unique
                // fingerprint index. Empty fingerprints are exempt (mirrors the partial DB index).
                if (item.Fingerprint.Length != 0 && !seen.Add(item.Fingerprint)) { deduped++; continue; }
                candidates.Add((item, signal));
            }
        }

        // Phase 2: enrich only the top-N by score (spend cap), save real fits.
        foreach (var (item, signal) in candidates.OrderByDescending(c => c.signal.Score).Take(runNiche.MaxEnrichPerRun))
        {
            var result = await enricher.EnrichAsync(item, runNiche, signal, sender, ct);
            if (result is null) continue;         // LLM failed/empty → retried next run
            enriched++;
            var company = string.IsNullOrWhiteSpace(result.Company) || result.Company == "-" ? item.Author : result.Company;
            // Real fits get the sender's hardcoded pitch/CTA appended; rejected drafts are "-".
            var draft = result.Fit == Fit.No ? result.Draft : DraftComposer.Compose(result.Draft, sender, company, result.ProspectNiche, result.ProspectSells);
            var lead = Lead.CreateHarvested(cid, company, result.Fit, result.Subject, draft,
                item.Source, item.Url, item.Fingerprint, signal.Score, string.Join(",", signal.Matched), DateTimeOffset.UtcNow);
            if (result.Fit == Fit.No)
            {
                // Persist NO as Rejected so its fingerprint is remembered. Without this the same
                // lead is re-harvested and re-sent to the LLM (re-paid) every run. The ListLeads
                // read model keeps Rejected/Shelved out of the customer inbox.
                lead.Reject();
                await leads.AddAsync(lead, ct);
                rejected++;
                continue;
            }
            await leads.AddAsync(lead, ct);
            saved++;
        }

        return new HarvestReport(harvested, scoredIn, deduped, enriched, saved, rejected);
    }

    // Per-run options: per-customer targeting + the injected global source config.
    private NicheOptions BuildRunNiche(HarvestTargeting t) => new()
    {
        Offer = t.Offer,
        SenderName = t.SenderName,
        SenderTitle = t.SenderTitle,
        TargetKeywords = t.TargetKeywords.ToList(),
        BuyingSignals = t.BuyingSignals.ToList(),
        Disqualifiers = t.Disqualifiers.ToList(),
        MinScore = t.MinScore,
        MaxEnrichPerRun = t.MaxEnrichPerRun,
        HackerNews = niche.HackerNews,
        Funding = niche.Funding,
    };

    private async Task<Customer> ResolveTargetCustomerAsync(CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(niche.TargetCustomerName, ct);
        if (existing is not null) return existing;
        var created = Customer.Create(niche.TargetCustomerName);
        await customers.AddAsync(created, ct);
        return created;
    }
}

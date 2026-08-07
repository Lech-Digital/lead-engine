using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

internal sealed class HarvestLeads(
    IEnumerable<ILeadSource> sources,
    ILeadEnricher enricher,
    ISecondaryContextLookup secondaryContextLookup,
    ILeadRepository leads,
    ICustomerRepository customers,
    ISuppressionRepository suppressions,
    NicheOptions niche,
    ITenantScope tenantScope) : IHarvestLeads
{
    public async Task<HarvestReport> RunAsync(CancellationToken ct)
    {
        // Console path: resolve the configured target tenant, build sender from ITS profile (unchanged).
        var target = await ResolveTargetCustomerAsync(ct);
        tenantScope.SetCurrent(target.Id.Value);
        var sender = new SenderContext(target.Profile.Brief, target.Profile.MentionSourceTool);
        return await RunPipelineAsync(target.Id, HarvestTargeting.FromNiche(niche), sender, target.AllowsSource, ct);
    }

    public async Task<HarvestReport> RunForCustomerAsync(Guid customerId, HarvestTargeting targeting, CancellationToken ct)
    {
        var cid = new CustomerId(customerId);
        tenantScope.SetCurrent(customerId);
        var customer = await customers.GetAsync(cid, ct);
        var sender = customer is null
            ? SenderContext.None
            : new SenderContext(customer.Profile.Brief, customer.Profile.MentionSourceTool,
                customer.Profile.PitchLine, customer.Profile.CtaLine, customer.Profile.CtaButtonUrl,
                customer.Profile.IntroLine, customer.Profile.SubjectTemplate,
                SkipFeedback: await BuildSkipFeedbackAsync(cid, ct));
        return await RunPipelineAsync(cid, targeting, sender, customer is null ? _ => true : customer.AllowsSource, ct);
    }

    private async Task<HarvestReport> RunPipelineAsync(CustomerId cid, HarvestTargeting targeting, SenderContext sender, Func<string, bool> allowsSource, CancellationToken ct)
    {
        var runNiche = BuildRunNiche(targeting);
        var funnel = new HarvestFunnel();
        var savedLeads = new List<SavedLead>();
        // GDPR objection list (privacy policy §5): matching items never enter the pipeline.
        var suppressed = await suppressions.ValuesAsync(ct);

        // Phase 1: harvest + cheap score gate + dedup (no LLM spend yet).
        var candidates = new List<(RawItem item, SignalResult signal)>();
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
                var signal = SignalScorer.Score(item, runNiche, source.BaseScore, source.ScoreWeight);
                if (signal.Score < runNiche.MinScore) { funnel.CountDropped("below-score"); continue; }
                funnel.CountPrioritised();
                if (await leads.ExistsByFingerprintAsync(item.Fingerprint, ct)) { funnel.CountDuplicateFingerprint(); continue; }
                // The same item can surface twice in one run (e.g. an HN comment matching several
                // queries). Dedup within the run too, or the second save collides on the unique
                // fingerprint index. Empty fingerprints are exempt (mirrors the partial DB index).
                if (item.Fingerprint.Length != 0 && !seen.Add(item.Fingerprint)) { funnel.CountDuplicateFingerprint(); continue; }
                funnel.CountTriaged();
                candidates.Add((item, signal));
            }
        }

        // Phase 2: enrich only the top-N by score (spend cap), save real fits.
        foreach (var (item, signal) in candidates.OrderByDescending(c => c.signal.Score).Take(runNiche.MaxEnrichPerRun))
        {
            // Best-effort: one extra public snippet about the company (its own announcement, a
            // founder quote) gives paragraph one something beyond the funding blurb to draw on.
            // Any failure here must never abort the batch — enrichment proceeds without it.
            // Author alone isn't a sound key (it's a feed's URL host for FundingRssSource /
            // DutchBusinessNewsSource, an HN username for HackerNewsSource, etc.) — see
            // SecondaryContextKey. A null key means neither field yields anything usable, so
            // skip the HTTP call entirely rather than look up known junk.
            string? secondaryContext = null;
            var secondaryKey = SecondaryContextKey.For(item);
            if (secondaryKey is not null)
            {
                try { secondaryContext = await secondaryContextLookup.LookupAsync(secondaryKey, ct); }
                catch (Exception) when (!ct.IsCancellationRequested) { /* best-effort by contract */ }
            }
            var result = await enricher.EnrichAsync(item, runNiche, signal, sender, secondaryContext, ct);
            if (result is null) continue;         // LLM failed/empty → retried next run
            funnel.CountEnriched();
            var company = string.IsNullOrWhiteSpace(result.Company) || result.Company == "-" ? item.Author : result.Company;
            // Second gate: the true company name is often only known after enrichment.
            if (SuppressionMatcher.IsMatch(suppressed, company)) { funnel.CountDropped("suppressed-after-enrichment"); continue; }
            // Real fits get the sender's hardcoded pitch/CTA appended; rejected drafts are "-".
            var draft = result.Fit == Fit.No ? result.Draft : DraftComposer.Compose(result.Draft, sender, company, result.ProspectNiche, result.ProspectSells);
            var subject = result.Fit == Fit.No ? result.Subject : DraftComposer.ComposeSubject(result.Subject, sender, company, result.ProspectNiche, result.ProspectSells);
            var lead = Lead.CreateHarvested(cid, company, result.Fit, subject, draft,
                item.Source, item.Url, item.Fingerprint, signal.Score, string.Join(",", signal.Matched), DateTimeOffset.UtcNow);
            if (result.Fit == Fit.No)
            {
                // Persist NO as Rejected so its fingerprint is remembered. Without this the same
                // lead is re-harvested and re-sent to the LLM (re-paid) every run. The ListLeads
                // read model keeps Rejected/Shelved out of the customer inbox.
                lead.Reject();
                await leads.AddAsync(lead, ct);
                funnel.CountRejected();
                continue;
            }
            await leads.AddAsync(lead, ct);
            funnel.CountSavedFrom(item.Source);
            savedLeads.Add(new SavedLead(lead.Id.Value, company, subject));
        }

        return funnel.ToReport(savedLeads);
    }

    // Per-run options: per-customer targeting + the injected global source config.
    // The flywheel: the customer's recent skip reasons, formatted for the enricher prompt.
    // Null when there's nothing to learn from (no reasons given yet).
    private async Task<string?> BuildSkipFeedbackAsync(CustomerId cid, CancellationToken ct)
    {
        var all = await leads.ForCustomerAsync(cid, ct);
        var lines = all
            .Where(l => l.Status == LeadStatus.Shelved && !string.IsNullOrWhiteSpace(l.SkipReason))
            .OrderByDescending(l => l.CreatedAt)
            .Take(12)
            .Select(l => $"- {l.Company}: {l.SkipReason}")
            .ToList();
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

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
        FundingNl = niche.FundingNl,
        Adzuna = niche.Adzuna,
        WebSearch = niche.WebSearch,
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

using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

internal sealed class HarvestLeads(
    IEnumerable<ILeadSource> sources,
    ILeadEnricher enricher,
    ILeadTriage triage,
    ISecondaryContextLookup secondaryContextLookup,
    ILeadRepository leads,
    ICustomerRepository customers,
    ISuppressionRepository suppressions,
    NicheOptions niche,
    ITenantScope tenantScope) : IHarvestLeads
{
    // The smallest triage batch an "every call came back null" observation is allowed to be
    // read as a provider outage rather than as isolated bad luck — see the comment at its
    // one use site in RunPipelineAsync.
    private const int MinBatchForOutageDetection = 3;

    public async Task<HarvestReport> RunAsync(CancellationToken ct)
    {
        // Console path: resolve the configured target tenant, build sender from ITS profile (unchanged).
        var target = await ResolveTargetCustomerAsync(ct);
        tenantScope.SetCurrent(target.Id.Value);
        var sender = new SenderContext(target.Profile.Brief, target.Profile.MentionSourceTool);
        return await RunPipelineAsync(target.Id, HarvestTargeting.FromNiche(niche), target.Targeting, sender, target.AllowsSource, ct);
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
        return await RunPipelineAsync(cid, targeting, customer?.Targeting ?? Targeting.Empty, sender, customer is null ? _ => true : customer.AllowsSource, ct);
    }

    private async Task<HarvestReport> RunPipelineAsync(CustomerId cid, HarvestTargeting targeting, Targeting structuredTargeting, SenderContext sender, Func<string, bool> allowsSource, CancellationToken ct)
    {
        var runNiche = BuildRunNiche(targeting);
        var criteria = TriageCriteria.Describe(structuredTargeting, runNiche.Offer);
        var funnel = new HarvestFunnel();
        var savedLeads = new List<SavedLead>();
        // GDPR objection list (privacy policy §5): matching items never enter the pipeline.
        var suppressed = await suppressions.ValuesAsync(ct);

        // Companies this customer already has a lead for, built once per run (not per
        // candidate) so the veto's duplicate-company check is a cheap in-memory lookup.
        var existingLeads = await leads.ForCustomerAsync(cid, ct);
        var alreadyHeldCompanies = new HashSet<string>(
            existingLeads.Select(l => l.Company), StringComparer.OrdinalIgnoreCase);

        // Phase 1: harvest + veto + rank + dedup (no LLM spend yet). MinScore is no longer a
        // drop-threshold — every candidate that survives the veto is ranked and queued; the
        // spend cap (MaxEnrichPerRun below) decides who actually gets enriched this run.
        var ranked = await new CandidateCollector(sources, leads).CollectAsync(
            runNiche, targeting.Disqualifiers, allowsSource, alreadyHeldCompanies, suppressed, funnel, ct);

        // Phase 2: triage — a cheap LLM verdict gates the (far more expensive) enrichment
        // call. Only the top MaxTriagePerRun ranked candidates are judged at all; that cap is
        // a COUNT OF CALLS, never a computed price (see NicheOptions.MaxTriagePerRun).
        var judged = new List<(RawItem item, SignalResult signal, TriageVerdict? verdict)>();
        foreach (var (item, signal, _) in ranked.Take(runNiche.MaxTriagePerRun))
        {
            TriageVerdict? verdict;
            try { verdict = await triage.JudgeAsync(item, criteria, ct); }
            catch { verdict = null; }   // a failing triage call must never abort the batch
            funnel.CountTriaged();
            judged.Add((item, signal, verdict));
        }

        // "Unavailable entirely" (no API key configured, etc.) is detected purely from the
        // shape of the results — every single call this run came back null — never by
        // inspecting configuration, so the Application layer stays free of provider
        // knowledge. Degrade to the pre-triage behaviour rather than produce zero leads.
        //
        // Below MinBatchForOutageDetection, "every call came back null" cannot be told apart
        // from ordinary bad luck on one or two independent candidates — at batch size 1 a
        // single unlucky failure would otherwise be indistinguishable from an outage and
        // would enrich an unqualified candidate unconditionally, the exact expensive,
        // unqualified direction this project exists to avoid. Below the threshold, nulls
        // stay per-candidate Unsure, exactly as an isolated null does today: this is a
        // deliberate trade — if triage really is down on a quiet night with fewer than
        // MinBatchForOutageDetection candidates, that run produces no leads. That is fine:
        // nothing is persisted for those candidates, so they are re-fetched and reconsidered
        // next run. A quiet night with no leads is recoverable; a wrong lead put in front of
        // a paying customer is not.
        var triageUnavailable = judged.Count >= MinBatchForOutageDetection && judged.All(j => j.verdict is null);

        var toEnrich = new List<(RawItem item, SignalResult signal, string? triageCompany)>();
        if (triageUnavailable)
        {
            toEnrich.AddRange(ranked.Take(runNiche.MaxEnrichPerRun).Select(c => (c.item, c.signal, (string?)null)));
        }
        else
        {
            foreach (var (item, signal, verdict) in judged)
            {
                // A null verdict here means triage FAILED for this one candidate (transport
                // error, unparseable answer, …) — treated as Unsure, not as a rejection, so a
                // good prospect is never silently discarded for an LLM hiccup. It simply does
                // not proceed this run and can be picked up on a later pass.
                if (verdict is null) { funnel.CountDropped("triage-unsure"); continue; }
                switch (verdict.Decision)
                {
                    case TriageDecision.Fit:
                        toEnrich.Add((item, signal, verdict.Company));
                        break;
                    case TriageDecision.NotFit:
                        funnel.CountDropped("triage-not-fit");   // the whole point: no draft, no enrichment call
                        break;
                    default:
                        funnel.CountDropped("triage-unsure");
                        break;
                }
            }
            toEnrich = toEnrich.Take(runNiche.MaxEnrichPerRun).ToList();
        }

        // Phase 3: enrich only what triage passed (or, in the fallback path, the top-N by
        // rank), save real fits. Nothing here is dropped for ranking low — an item that
        // misses the cap this run simply queues behind better candidates and may be picked
        // up on a later run.
        foreach (var (item, signal, triageCompany) in toEnrich)
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
            funnel.CountAttempted();               // an actual enricher call, win or lose — distinct from the candidate pool size
            var result = await enricher.EnrichAsync(item, runNiche, signal, sender, secondaryContext, ct);
            if (result is null) continue;         // LLM failed/empty → retried next run
            funnel.CountEnriched();
            // Entity-extraction fix: when triage already worked out the real company, prefer
            // it over item.Author — production stored "Menlo Ventures" and
            // "news.crunchbase.com" as prospects because the author field was trusted blindly.
            var fallbackCompany = triageCompany ?? item.Author;
            var company = string.IsNullOrWhiteSpace(result.Company) || result.Company == "-" ? fallbackCompany : result.Company;
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
        // Not part of HarvestTargeting (a per-customer concept elsewhere) — the triage budget
        // is a global operator knob, so it always comes from the injected settings.
        MaxTriagePerRun = niche.MaxTriagePerRun,
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

using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

internal sealed class HarvestLeads(
    IEnumerable<ILeadSource> sources,
    ILeadEnricher enricher,
    ILeadRepository leads,
    ICustomerRepository customers,
    NicheOptions niche) : IHarvestLeads
{
    public async Task<HarvestReport> RunAsync(CancellationToken ct)
    {
        int harvested = 0, scoredIn = 0, deduped = 0, enriched = 0, saved = 0, rejected = 0;

        // Phase 1: harvest + cheap score gate + dedup (no LLM spend yet).
        var candidates = new List<(RawItem item, SignalResult signal)>();
        foreach (var source in sources.Where(s => s.Enabled))
        {
            IReadOnlyList<RawItem> items;
            try { items = await source.FetchAsync(ct); }
            catch { continue; }   // a failing source must not abort the run
            foreach (var item in items)
            {
                harvested++;
                var signal = SignalScorer.Score(item, niche, source.BaseScore, source.ScoreWeight);
                if (signal.Score < niche.MinScore) continue;
                scoredIn++;
                if (await leads.ExistsByFingerprintAsync(item.Fingerprint, ct)) { deduped++; continue; }
                candidates.Add((item, signal));
            }
        }

        // Phase 2: enrich only the top-N by score (spend cap), save real fits.
        var target = await ResolveTargetCustomerAsync(ct);
        foreach (var (item, signal) in candidates.OrderByDescending(c => c.signal.Score).Take(niche.MaxEnrichPerRun))
        {
            var result = await enricher.EnrichAsync(item, niche, signal, ct);
            if (result is null) continue;         // LLM failed/empty → retried next run
            enriched++;
            var company = string.IsNullOrWhiteSpace(result.Company) || result.Company == "-" ? item.Author : result.Company;
            var lead = Lead.CreateHarvested(target.Id, company, result.Fit, result.Subject, result.Draft,
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

    private async Task<Customer> ResolveTargetCustomerAsync(CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(niche.TargetCustomerName, ct);
        if (existing is not null) return existing;
        var created = Customer.Create(niche.TargetCustomerName);
        await customers.AddAsync(created, ct);
        return created;
    }
}

using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Post-run operator alert decision, kept pure so the diagnosis is testable. A run that
// enriched nothing has two very different causes and the subject line must not lie:
// either real candidates reached the enricher and every call failed (LLM quota/keys),
// or dedup matched everything and the sources simply produced nothing new (content
// starvation — the 2026-08-02/03 incident, misdiagnosed as "LLM quota?" at the time).
internal static class HarvestRunAlert
{
    public static (string Subject, string Body)? For(HarvestReport report, string customerName, Guid customerId)
    {
        if (report.Enriched > 0 || report.ScoredIn == 0) return null;
        var candidates = report.ScoredIn - report.Deduped;
        if (candidates > 0)
            return ($"Harvest enriched 0 of {candidates} for {customerName} — LLM quota?",
                $"The run for {customerName} ({customerId}) had {candidates} candidate(s) left after dedup " +
                "but every enrichment call failed. Check the LLM provider quota/keys.");
        return ($"Harvest found nothing new for {customerName} — sources stale?",
            $"The run for {customerName} ({customerId}) scored in {report.ScoredIn} item(s), but dedup matched " +
            "every one against already-harvested leads — the sources produced nothing new. Check the search " +
            "sources (web-search quota/keys, feed health) or widen the profile keywords.");
    }
}

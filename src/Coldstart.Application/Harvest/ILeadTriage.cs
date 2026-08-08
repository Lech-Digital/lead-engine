namespace Coldstart.Application.Harvest;

public enum TriageDecision { Fit, NotFit, Unsure }

// Tiny, cheap-to-compute verdict on whether a harvested candidate is worth a full,
// expensive enrichment call. Company is the LLM's best guess at the REAL company/org
// name behind the item (never a publisher or an investor) — null when it could not
// tell. Met/Failed carry the ICP criteria the model matched or missed, for the
// briefing/inbox to explain the verdict rather than just asserting it.
public sealed record TriageVerdict(TriageDecision Decision, string? Company, IReadOnlyList<string> Met, IReadOnlyList<string> Failed);

// Judges a single harvested item against a customer's ICP criteria before the (much
// more expensive) enrichment call is made. Returns null on ANY failure — missing API
// key, transport error, non-success HTTP status, or an unparseable answer — never a
// synthetic rejection. A null result means "triage did not run", to be retried on a
// later pass, not "this candidate failed triage".
public interface ILeadTriage
{
    Task<TriageVerdict?> JudgeAsync(RawItem item, string criteria, CancellationToken ct);
}

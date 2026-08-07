namespace Coldstart.Application.Customers;

public sealed record TargetingProposalRequest(string CompanyName, string Niche, string Brief, string Offer);

// Proposes a structured targeting profile from the customer's own prose. Best-effort:
// returns null rather than throwing, and the caller never persists a proposal without a
// human approving it.
public interface ITargetingGenerator
{
    Task<TargetingInput?> ProposeAsync(TargetingProposalRequest request, CancellationToken ct);
}

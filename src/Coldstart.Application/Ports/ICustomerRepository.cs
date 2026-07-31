using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct);
    Task AddAsync(Customer customer, CancellationToken ct);
    Task<Customer?> GetByNameAsync(string name, CancellationToken ct);
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);
    Task UpdateAsync(Customer customer, CancellationToken ct);
    Task DeleteAsync(CustomerId id, CancellationToken ct);

    // System-level (webhook, no tenant context): find by the stored Stripe customer id.
    Task<Customer?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct);

    // System-level (no tenant context): ids of Full-plan customers that are not mid-harvest and have
    // fewer than `threshold` unfinished (PendingReview/Approved) leads — i.e. have room for a top-up.
    Task<IReadOnlyList<Guid>> IdsDueForHarvestAsync(int threshold, CancellationToken ct);

    // System-level (no tenant context): per-customer lead totals for the admin overview.
    // Customers with zero leads are absent from the dictionary.
    Task<IReadOnlyDictionary<Guid, LeadCounts>> LeadCountsAsync(CancellationToken ct);

    // System-level (no tenant context): Free-plan customers eligible for a trial follow-up —
    // harvested at least once (on or before `harvestedBefore`), not opted out, not fully nudged.
    Task<IReadOnlyList<TrialFollowUpCandidate>> TrialFollowUpCandidatesAsync(DateTimeOffset harvestedBefore, CancellationToken ct);
}

public sealed record LeadCounts(int Total, int PendingReview);

public sealed record TrialFollowUpCandidate(Guid Id, int FollowUpStage, DateTimeOffset LastHarvestAt);

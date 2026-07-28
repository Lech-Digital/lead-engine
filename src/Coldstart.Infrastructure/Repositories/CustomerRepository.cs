using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class CustomerRepository(ColdstartDbContext db) : ICustomerRepository
{
    public async Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct) =>
        await db.Customers.IgnoreQueryFilters().OrderBy(c => c.Name).ToListAsync(ct);

    public async Task AddAsync(Customer customer, CancellationToken ct)
    {
        await db.Customers.AddAsync(customer, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Customer?> GetByNameAsync(string name, CancellationToken ct) =>
        await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Name == name, ct);

    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Customer?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct) =>
        await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.StripeCustomerId == stripeCustomerId, ct);

    public async Task UpdateAsync(Customer customer, CancellationToken ct)
    {
        db.Customers.Update(customer);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(CustomerId id, CancellationToken ct)
    {
        // Admin operation: the caller (e.g. a SuperAdmin removing/revoking another tenant) is not
        // scoped to the target tenant, so these reads must bypass the fail-closed filter.
        var leads = await db.Leads.IgnoreQueryFilters().Where(l => l.CustomerId == id).ToListAsync(ct);
        db.Leads.RemoveRange(leads);
        var customer = await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is not null) db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> IdsDueForHarvestAsync(int threshold, CancellationToken ct)
    {
        // Cross-customer system read → bypass the fail-closed tenant filter (like AllAsync/DeleteAsync).
        // Skip both Running AND Pending: Pending means a harvest is already queued (e.g. the initial
        // invite job), so re-enqueuing would run it twice (dedup keeps data safe, but wastes Groq).
        var due = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.Plan == CustomerPlan.Full
                        && c.HarvestStatus != HarvestStatus.Running
                        && c.HarvestStatus != HarvestStatus.Pending)
            .Where(c => db.Leads.IgnoreQueryFilters()
                .Count(l => l.CustomerId == c.Id
                            && (l.Status == LeadStatus.PendingReview || l.Status == LeadStatus.Approved)) < threshold)
            .ToListAsync(ct);   // materialize, then map the value-object id in memory (avoids .Id.Value translation)
        return due.Select(c => c.Id.Value).ToList();
    }
}

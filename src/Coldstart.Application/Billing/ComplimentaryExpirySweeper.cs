using Coldstart.Application.Ports;

namespace Coldstart.Application.Billing;

// Downgrades lapsed complimentary trials. endedAt: null on purpose — a comp trial
// never paid, so it must not start the privacy-policy §7 retention-deletion clock.
public sealed class ComplimentaryExpirySweeper(ICustomerRepository customers)
{
    public async Task<int> SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        var downgraded = 0;
        foreach (var customer in await customers.AllAsync(ct))
        {
            if (customer.ComplimentaryUntil is not { } until || until >= now) continue;
            if (customer.StripeSubscriptionId is not null) continue;   // converted mid-trial
            try
            {
                customer.DowngradeToFree(endedAt: null);
                await customers.UpdateAsync(customer, ct);
                downgraded++;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // One bad row must not stop the sweep; the next nightly run retries it.
            }
        }
        return downgraded;
    }
}

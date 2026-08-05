using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Billing;

public sealed record ComplimentaryGrantResult(bool Succeeded, string Message, string? CustomerName = null, DateTimeOffset? Until = null);

public interface IGrantComplimentaryAccess
{
    Task<ComplimentaryGrantResult> ExecuteAsync(Guid customerId, int days, CancellationToken ct);
}

internal sealed class GrantComplimentaryAccess(ICustomerRepository customers) : IGrantComplimentaryAccess
{
    public async Task<ComplimentaryGrantResult> ExecuteAsync(Guid customerId, int days, CancellationToken ct)
    {
        if (days < 1)
            return new(false, "A complimentary grant needs at least 1 day.");

        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null)
            return new(false, "No such customer.");

        var now = DateTimeOffset.UtcNow;
        try
        {
            customer.GrantComplimentary(now.AddDays(days), now);
        }
        catch (DomainException ex)
        {
            return new(false, ex.Message);
        }

        await customers.UpdateAsync(customer, ct);
        return new(true, $"Granted the full plan to {customer.Name} until {customer.ComplimentaryUntil:yyyy-MM-dd}.",
            customer.Name, customer.ComplimentaryUntil);
    }
}

using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Billing;

public sealed record ComplimentaryCancelResult(bool Succeeded, string Message, string? CustomerName = null);

public interface ICancelComplimentaryAccess
{
    Task<ComplimentaryCancelResult> ExecuteAsync(Guid customerId, CancellationToken ct);
}

// Revokes an active complimentary grant immediately. Never emails the customer — only the
// grant (a thank-you) notifies; a cancellation is an operator correction, not news to deliver.
internal sealed class CancelComplimentaryAccess(ICustomerRepository customers) : ICancelComplimentaryAccess
{
    public async Task<ComplimentaryCancelResult> ExecuteAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await customers.GetForSystemAsync(new CustomerId(customerId), ct);
        if (customer is null)
            return new(false, "No such customer.");

        try
        {
            customer.CancelComplimentary();
        }
        catch (DomainException ex)
        {
            return new(false, ex.Message);
        }

        await customers.UpdateAsync(customer, ct);
        return new(true, $"Complimentary access for {customer.Name} has been cancelled — they're back on the free plan.", customer.Name);
    }
}

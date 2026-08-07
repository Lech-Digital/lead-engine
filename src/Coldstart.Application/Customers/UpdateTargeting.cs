using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

// The one write path shared by the customer settings page and the admin editor, so the
// two surfaces cannot drift apart in validation or behaviour.
internal sealed class UpdateTargeting(ICustomerRepository customers) : IUpdateTargeting
{
    public async Task<TargetingResult> UpdateAsync(Guid customerId, TargetingInput input, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return new(false, "No such customer.");

        Targeting targeting;
        try
        {
            targeting = Targeting.Create(
                Lines(input.Industries), Lines(input.Geographies), Lines(input.BuyerRoles),
                input.SizeMin, input.SizeMax, Lines(input.TriggerEvents), Lines(input.Exclusions));
        }
        catch (DomainException ex)
        {
            return new(false, ex.Message);
        }

        customer.UpdateTargeting(targeting);
        await customers.UpdateAsync(customer, ct);
        return new(true);
    }

    private static IEnumerable<string> Lines(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

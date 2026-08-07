using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class GetTargeting(ICustomerRepository customers) : IGetTargeting
{
    public async Task<TargetingView?> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return null;
        var t = customer.Targeting;
        return new TargetingView(t.Industries ?? "", t.Geographies ?? "", t.BuyerRoles ?? "",
            t.SizeMin, t.SizeMax, t.TriggerEvents ?? "", t.Exclusions ?? "");
    }
}

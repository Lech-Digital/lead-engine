namespace Coldstart.Application.Customers;

public interface IGetTargeting
{
    Task<TargetingView?> ForCustomerAsync(Guid customerId, CancellationToken ct);
}

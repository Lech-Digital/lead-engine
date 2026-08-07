namespace Coldstart.Application.Customers;

public interface IUpdateTargeting
{
    Task<TargetingResult> UpdateAsync(Guid customerId, TargetingInput input, CancellationToken ct);
}

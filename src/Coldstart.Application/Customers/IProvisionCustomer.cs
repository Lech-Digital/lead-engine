namespace Coldstart.Application.Customers;

public interface IProvisionCustomer
{
    Task<Guid> ProvisionAsync(string customerName, CancellationToken ct);
}

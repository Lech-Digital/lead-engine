namespace Coldstart.Application.Customers;

public interface IProvisionCustomer
{
    Task<Guid> ProvisionAsync(string customerName, CancellationToken ct);
    Task<Guid> ProvisionAsync(string customerName, CompanyProfileSeed seed, CancellationToken ct);
}

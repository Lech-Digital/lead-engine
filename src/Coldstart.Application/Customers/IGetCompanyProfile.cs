namespace Coldstart.Application.Customers;

public interface IGetCompanyProfile
{
    Task<CompanyProfileView> ForCustomerAsync(Guid customerId, CancellationToken ct);
}

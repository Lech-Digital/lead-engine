namespace Coldstart.Application.Customers;

public interface IUpdateCompanyProfile
{
    Task UpdateAsync(Guid customerId, CompanyProfileInput input, CancellationToken ct);
}

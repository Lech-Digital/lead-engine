using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class UpdateCompanyProfile(ICustomerRepository customers) : IUpdateCompanyProfile
{
    public async Task UpdateAsync(Guid customerId, CompanyProfileInput input, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return;
        customer.UpdateProfile(new CompanyProfile(input.CompanyName, input.Address, input.VatNumber,
            input.KvkNumber, input.Niche, input.Website, input.ContactEmail));
        await customers.UpdateAsync(customer, ct);
    }
}

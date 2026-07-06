using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class ProvisionCustomer(ICustomerRepository customers) : IProvisionCustomer
{
    public async Task<Guid> ProvisionAsync(string customerName, CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(customerName, ct);
        if (existing is not null) return existing.Id.Value;

        var customer = Customer.Create(customerName);   // no mock leads — real leads come from the harvester
        await customers.AddAsync(customer, ct);
        return customer.Id.Value;
    }
}

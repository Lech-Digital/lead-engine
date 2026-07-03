using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class ProvisionCustomer(ICustomerRepository customers) : IProvisionCustomer
{
    public async Task<Guid> ProvisionAsync(string customerName, CancellationToken ct)
    {
        var customer = Customer.Create(customerName);
        var now = DateTimeOffset.UtcNow;
        // Mock leads until the harvest pipeline lands.
        customer.AddLead("Patronus AI", Fit.Yes, "Anand — a thought after the $50M", "…", now);
        customer.AddLead("Lago", Fit.Yes, "hiring GTM at Lago", "…", now);
        customer.AddLead("Visiblie", Fit.Maybe, "congrats on the €500K", "…", now);
        await customers.AddAsync(customer, ct);
        return customer.Id.Value;
    }
}

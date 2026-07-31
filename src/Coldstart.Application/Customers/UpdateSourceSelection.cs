using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

public interface IUpdateSourceSelection
{
    // names: the sources the customer keeps ON. Empty/null = all sources.
    Task UpdateAsync(Guid customerId, IReadOnlyCollection<string>? names, CancellationToken ct);
}

internal sealed class UpdateSourceSelection(ICustomerRepository customers) : IUpdateSourceSelection
{
    public async Task UpdateAsync(Guid customerId, IReadOnlyCollection<string>? names, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return;
        customer.ConfigureSources(names);
        await customers.UpdateAsync(customer, ct);
    }
}

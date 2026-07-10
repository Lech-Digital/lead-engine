using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class DeleteCustomer(ICustomerRepository customers) : IDeleteCustomer
{
    public Task DeleteAsync(Guid customerId, CancellationToken ct) =>
        customers.DeleteAsync(new CustomerId(customerId), ct);
}

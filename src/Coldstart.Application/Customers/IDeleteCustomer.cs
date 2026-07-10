namespace Coldstart.Application.Customers;

public interface IDeleteCustomer
{
    Task DeleteAsync(Guid customerId, CancellationToken ct);
}

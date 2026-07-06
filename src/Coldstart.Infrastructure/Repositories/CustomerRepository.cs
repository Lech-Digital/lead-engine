using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class CustomerRepository(ColdstartDbContext db) : ICustomerRepository
{
    public async Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct) =>
        await db.Customers.OrderBy(c => c.Name).ToListAsync(ct);

    public async Task AddAsync(Customer customer, CancellationToken ct)
    {
        await db.Customers.AddAsync(customer, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Customer?> GetByNameAsync(string name, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Name == name, ct);

    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task UpdateAsync(Customer customer, CancellationToken ct)
    {
        db.Customers.Update(customer);
        await db.SaveChangesAsync(ct);
    }
}

using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure;

public sealed class ColdstartDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext _tenant;

    public ColdstartDbContext(DbContextOptions<ColdstartDbContext> options, ITenantContext tenant)
        : base(options) => _tenant = tenant;

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();

    // Current tenant as the domain key type, for query filters. Null => no rows match (fail closed).
    public CustomerId? CurrentCustomerId => _tenant.CustomerId is { } id ? new CustomerId(id) : null;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity schema
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ColdstartDbContext).Assembly);
        // Query filters are added in Task 4 (after every host resolves the tenant).
    }
}

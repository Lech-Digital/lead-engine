using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    // A settable tenant per context; default null. Existing tests that pass no id keep working
    // until Task 4 adds filters, at which point tenant-scoped reads pass the owning customer's id.
    public ColdstartDbContext NewDbContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ColdstartDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        var tenant = new MutableTenantContext();
        if (tenantId is { } id) ((Coldstart.Application.Ports.ITenantScope)tenant).SetCurrent(id);
        return new ColdstartDbContext(options, tenant);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

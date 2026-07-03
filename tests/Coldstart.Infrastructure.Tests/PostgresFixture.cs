using Coldstart.Infrastructure;
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

    public ColdstartDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<ColdstartDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ColdstartDbContext(options);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

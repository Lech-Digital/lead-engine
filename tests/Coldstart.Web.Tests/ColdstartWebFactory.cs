using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coldstart.Web.Tests;

public sealed class ColdstartWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db =
        new PostgreSqlBuilder("postgres:17").Build();

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _db.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Coldstart", _db.GetConnectionString());
    }
}

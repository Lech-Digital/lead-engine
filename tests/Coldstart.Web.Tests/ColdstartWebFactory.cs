using Coldstart.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coldstart.Web.Tests;

public sealed class ColdstartWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db =
        new PostgreSqlBuilder("postgres:17").Build();

    public FakeEmailSender Email { get; } = new();
    public FakeMailbox Mailbox { get; } = new();

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _db.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Authentication:Google:ClientId", "");
        builder.UseSetting("Authentication:Google:ClientSecret", "");
        builder.UseSetting("ConnectionStrings:Coldstart", _db.GetConnectionString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            services.RemoveAll<Coldstart.Application.Mailbox.IMailbox>();
            services.AddSingleton<Coldstart.Application.Mailbox.IMailbox>(Mailbox);
        });
    }
}

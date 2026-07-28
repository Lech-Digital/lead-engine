using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coldstart.Web.Tests;

public class ColdstartWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db =
        new PostgreSqlBuilder("postgres:17").Build();

    public FakeEmailSender Email { get; } = new();
    public FakeMailbox Mailbox { get; } = new();
    public FakeCompanyBriefPolisher BriefPolisher { get; } = new();
    public FakeOutreachMailer Outreach { get; } = new();
    public FakeHarvestQueue HarvestQueue { get; } = new();
    public FakeSearchProfileGenerator SearchProfiles { get; } = new();
    public FakeBillingGateway Billing { get; } = new();

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _db.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Authentication:Google:ClientId", "");
        builder.UseSetting("Authentication:Google:ClientSecret", "");
        builder.UseSetting("ConnectionStrings:Coldstart", _db.GetConnectionString());
        builder.UseSetting("Quota:FreeLeadCount", "100000");   // effectively no foil for the shared factory
        builder.UseSetting("HarvestSchedule:Enabled", "false"); // hosted scheduler must never fire during the suite
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            services.RemoveAll<Coldstart.Application.Mailbox.IMailbox>();
            services.AddSingleton<Coldstart.Application.Mailbox.IMailbox>(Mailbox);
            services.RemoveAll<ICompanyBriefPolisher>();
            services.AddSingleton<ICompanyBriefPolisher>(BriefPolisher);
            services.RemoveAll<Coldstart.Application.Outreach.IOutreachMailer>();
            services.AddSingleton<Coldstart.Application.Outreach.IOutreachMailer>(Outreach);
            services.RemoveAll<Coldstart.Application.Harvest.IHarvestQueue>();
            services.AddSingleton<Coldstart.Application.Harvest.IHarvestQueue>(HarvestQueue);
            services.RemoveAll<Coldstart.Application.Customers.ISearchProfileGenerator>();
            services.AddSingleton<Coldstart.Application.Customers.ISearchProfileGenerator>(SearchProfiles);
            services.RemoveAll<Coldstart.Application.Billing.IBillingGateway>();
            services.AddSingleton<Coldstart.Application.Billing.IBillingGateway>(Billing);
        });
    }
}

// Fake IOutreachMailer: records the last sent email; can be told to throw to simulate SMTP failure.
public sealed class FakeOutreachMailer : Coldstart.Application.Outreach.IOutreachMailer
{
    public Coldstart.Application.Outreach.OutreachEmail? LastSent { get; set; }
    public bool Throw { get; set; }

    public Task SendAsync(Coldstart.Application.Outreach.OutreachEmail mail, CancellationToken ct)
    {
        if (Throw) throw new InvalidOperationException("simulated smtp failure");
        LastSent = mail;
        return Task.CompletedTask;
    }
}

// Fake ICompanyBriefPolisher: settable return value, defaults to a fixed polished string.
public sealed class FakeCompanyBriefPolisher : ICompanyBriefPolisher
{
    public string? Result { get; set; } = "POLISHED BRIEF";

    public Task<string?> PolishAsync(string rawBrief, CancellationToken ct) => Task.FromResult(Result);
}

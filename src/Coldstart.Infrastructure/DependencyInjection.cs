using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coldstart.Infrastructure;

public static class DependencyInjection
{
    // Named client for the per-lead secondary-context lookup (bounded timeout, see below).
    public const string SecondaryContextClient = "secondary-context";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<Tenancy.MutableTenantContext>();
        services.TryAddScoped<Application.Ports.ITenantContext>(sp => sp.GetRequiredService<Tenancy.MutableTenantContext>());
        services.TryAddScoped<Application.Ports.ITenantScope>(sp => sp.GetRequiredService<Tenancy.MutableTenantContext>());
        services.AddDbContext<ColdstartDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IInquiryRepository, Repositories.InquiryRepository>();
        services.AddScoped<ISuppressionRepository, Repositories.SuppressionRepository>();
        services.AddScoped<Retention.RetentionSweeper>();
        services.AddScoped<IEmailSender, Email.SmtpEmailSender>();
        services.AddScoped<Application.Outreach.IOutreachMailer, Outreach.SmtpOutreachMailer>();
        services.AddScoped<Application.Mailbox.IMailbox, Mailbox.ImapMailbox>();
        services.AddHttpClient();
        services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient());
        // Placeholder default so the container resolves/validates without config; the
        // composition root (Task 7) overrides this with the real, config-bound instance.
        services.TryAddSingleton<Application.Harvest.NicheOptions>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.HackerNewsSource>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.FundingRssSource>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.AdzunaSource>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.WebSearchSource>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.LangSearchSource>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.DutchBusinessNewsSource>();
        services.AddScoped<Application.Harvest.ILeadEnricher, Harvest.GroqLeadEnricher>();
        // The secondary-context lookup runs once per enriched lead INSIDE the nightly loop, so a
        // hung endpoint on the shared default client (~100s framework timeout) would cost up to
        // MaxEnrichPerRun × 100s sequentially. Its own named client keeps that bounded at 10s per
        // lead; the timeout surfaces as TaskCanceledException, which the lookup's best-effort
        // catch swallows into a null context. Other consumers of the default client are untouched.
        services.AddHttpClient(SecondaryContextClient, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<Application.Harvest.ISecondaryContextLookup>(sp =>
            new Harvest.LangSearchSecondaryLookup(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(SecondaryContextClient),
                sp.GetRequiredService<Application.Harvest.NicheOptions>()));
        services.AddScoped<Application.Notifications.IBriefingChannel, Notifications.SlackBriefingChannel>();
        services.AddScoped<Application.Notifications.IBriefingChannel, Notifications.TwilioWhatsAppChannel>();
        services.AddScoped<Application.Customers.ICompanyBriefPolisher, Harvest.GroqBriefPolisher>();
        services.AddScoped<Application.Customers.ISearchProfileGenerator, Harvest.GroqSearchProfileGenerator>();
        services.AddScoped<Application.Customers.ITargetingGenerator, Harvest.GroqTargetingGenerator>();
        services.AddScoped<Application.Billing.IBillingGateway, Billing.StripeBillingGateway>();
        services.AddSingleton<Application.Billing.IStripeEventParser, Billing.StripeEventParser>();
        return services;
    }

    // Web-only: resolve the tenant from the signed-in principal's claim (overrides the default context).
    public static IServiceCollection AddHttpTenantResolution(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<Application.Ports.ITenantContext, Tenancy.HttpTenantContext>();
        return services;
    }

    // Web-only: the in-process harvest queue + its background worker.
    public static IServiceCollection AddHarvestQueue(this IServiceCollection services)
    {
        services.AddSingleton<Harvest.HarvestQueue>();
        services.AddSingleton<Application.Harvest.IHarvestQueue>(sp => sp.GetRequiredService<Harvest.HarvestQueue>());
        services.AddHostedService<Harvest.HarvestQueueService>();
        services.AddHostedService<Harvest.NightlyHarvestScheduler>();
        services.AddHostedService<FollowUps.TrialFollowUpScheduler>();
        services.AddHostedService<Retention.RetentionCleanupService>();
        services.AddHostedService<Billing.ComplimentaryExpiryService>();
        return services;
    }
}

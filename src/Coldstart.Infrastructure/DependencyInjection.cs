using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coldstart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<Tenancy.MutableTenantContext>();
        services.TryAddScoped<Application.Ports.ITenantContext>(sp => sp.GetRequiredService<Tenancy.MutableTenantContext>());
        services.TryAddScoped<Application.Ports.ITenantScope>(sp => sp.GetRequiredService<Tenancy.MutableTenantContext>());
        services.AddDbContext<ColdstartDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IInquiryRepository, Repositories.InquiryRepository>();
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
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.DutchBusinessNewsSource>();
        services.AddScoped<Application.Harvest.ILeadEnricher, Harvest.GroqLeadEnricher>();
        services.AddScoped<Application.Notifications.IBriefingChannel, Notifications.SlackBriefingChannel>();
        services.AddScoped<Application.Notifications.IBriefingChannel, Notifications.TwilioWhatsAppChannel>();
        services.AddScoped<Application.Customers.ICompanyBriefPolisher, Harvest.GroqBriefPolisher>();
        services.AddScoped<Application.Customers.ISearchProfileGenerator, Harvest.GroqSearchProfileGenerator>();
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
        return services;
    }
}

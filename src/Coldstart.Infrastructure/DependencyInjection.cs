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
        services.AddDbContext<ColdstartDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IInquiryRepository, Repositories.InquiryRepository>();
        services.AddScoped<IEmailSender, Email.SmtpEmailSender>();
        services.AddHttpClient();
        services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient());
        // Placeholder default so the container resolves/validates without config; the
        // composition root (Task 7) overrides this with the real, config-bound instance.
        services.TryAddSingleton<Application.Harvest.NicheOptions>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.HackerNewsSource>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.FundingRssSource>();
        services.AddScoped<Application.Harvest.ILeadEnricher, Harvest.GroqLeadEnricher>();
        return services;
    }
}

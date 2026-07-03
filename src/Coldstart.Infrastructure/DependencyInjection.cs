using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ColdstartDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IInquiryRepository, Repositories.InquiryRepository>();
        services.Configure<Email.SmtpOptions>(o => { }); // bound from config in Web (Task 6)
        services.AddScoped<IEmailSender, Email.SmtpEmailSender>();
        return services;
    }
}

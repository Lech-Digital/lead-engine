using Coldstart.Application.Leads;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IListLeads, ListLeads>();
        services.AddScoped<IGetLeadInbox, GetLeadInbox>();
        services.AddScoped<Contact.ISubmitInquiry, Contact.SubmitInquiry>();
        return services;
    }
}

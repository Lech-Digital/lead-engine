using Coldstart.Application.Leads;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IListLeads, ListLeads>();
        services.AddScoped<IGetLeadInbox, GetLeadInbox>();
        services.AddScoped<IGetLeadDetail, GetLeadDetail>();
        services.AddScoped<ILeadWorkflow, LeadWorkflow>();
        services.AddScoped<Contact.ISubmitInquiry, Contact.SubmitInquiry>();
        services.AddScoped<Customers.IProvisionCustomer, Customers.ProvisionCustomer>();
        services.AddScoped<Customers.IUpdateCompanyProfile, Customers.UpdateCompanyProfile>();
        services.AddScoped<Customers.IGetCompanyProfile, Customers.GetCompanyProfile>();
        services.AddScoped<Harvest.IHarvestLeads, Harvest.HarvestLeads>();
        return services;
    }
}

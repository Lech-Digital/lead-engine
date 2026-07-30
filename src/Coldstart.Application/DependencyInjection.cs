using Coldstart.Application.Leads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coldstart.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(new Leads.QuotaOptions());
        services.AddScoped<IListLeads, ListLeads>();
        services.AddScoped<IGetLeadInbox, GetLeadInbox>();
        services.AddScoped<IGetLeadDetail, GetLeadDetail>();
        services.AddScoped<ILeadWorkflow, LeadWorkflow>();
        services.AddScoped<Contact.ISubmitInquiry, Contact.SubmitInquiry>();
        services.AddScoped<Customers.IProvisionCustomer, Customers.ProvisionCustomer>();
        services.AddScoped<Customers.IUpdateCompanyProfile, Customers.UpdateCompanyProfile>();
        services.AddScoped<Customers.IGetCompanyProfile, Customers.GetCompanyProfile>();
        services.AddScoped<Customers.IDeleteCustomer, Customers.DeleteCustomer>();
        services.AddScoped<Harvest.IHarvestLeads, Harvest.HarvestLeads>();
        services.AddScoped<Billing.IApplySubscriptionChange, Billing.ApplySubscriptionChange>();
        services.AddScoped<Notifications.INotifyBriefing, Notifications.NotifyBriefing>();
        services.AddScoped<Notifications.IUpdateNotificationSettings, Notifications.UpdateNotificationSettings>();
        return services;
    }
}

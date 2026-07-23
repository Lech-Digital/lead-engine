using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class ProvisionCustomer(ICustomerRepository customers) : IProvisionCustomer
{
    public async Task<Guid> ProvisionAsync(string customerName, CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(customerName, ct);
        if (existing is not null) return existing.Id.Value;

        var customer = Customer.Create(customerName);   // no mock leads — real leads come from the harvester
        await customers.AddAsync(customer, ct);
        return customer.Id.Value;
    }

    public async Task<Guid> ProvisionAsync(string customerName, CompanyProfileSeed seed, CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(customerName, ct);
        if (existing is not null) return existing.Id.Value;

        var customer = Customer.Create(customerName);
        var sp = seed.SearchProfile;
        customer.UpdateProfile(new CompanyProfile(
            CompanyName: customerName, Address: null, VatNumber: null, KvkNumber: null,
            Niche: seed.Niche, Website: null, ContactEmail: null,
            Brief: seed.Info, MentionSourceTool: false,
            Offer: sp?.Offer, TargetKeywords: sp?.TargetKeywords,
            BuyingSignals: sp?.BuyingSignals, Disqualifiers: sp?.Disqualifiers));
        customer.MarkHarvest(HarvestStatus.Pending, null);
        await customers.AddAsync(customer, ct);
        return customer.Id.Value;
    }
}

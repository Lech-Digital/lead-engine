using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class UpdateCompanyProfile(ICustomerRepository customers) : IUpdateCompanyProfile
{
    public async Task UpdateAsync(Guid customerId, CompanyProfileInput input, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return;
        // The form edits only the company-facing fields; the AI-generated search profile
        // (Offer/TargetKeywords/BuyingSignals/Disqualifiers) must survive the save or the
        // next harvest fails with "no keywords".
        var existing = customer.Profile;
        customer.UpdateProfile(new CompanyProfile(input.CompanyName, input.Address, input.VatNumber,
            input.KvkNumber, input.Niche, input.Website, input.ContactEmail, input.Brief, input.MentionSourceTool,
            existing.Offer, existing.TargetKeywords, existing.BuyingSignals, existing.Disqualifiers));
        await customers.UpdateAsync(customer, ct);
    }
}

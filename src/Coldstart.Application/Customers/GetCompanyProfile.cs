using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class GetCompanyProfile(ICustomerRepository customers) : IGetCompanyProfile
{
    public async Task<CompanyProfileView> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        var p = customer?.Profile ?? CompanyProfile.Empty;
        return new CompanyProfileView(p.CompanyName, p.Address, p.VatNumber, p.KvkNumber, p.Niche, p.Website, p.ContactEmail, p.Brief, p.MentionSourceTool, p.PitchLine, p.CtaLine, p.CtaButtonText, p.CtaButtonUrl);
    }
}

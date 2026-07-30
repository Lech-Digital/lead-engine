namespace Coldstart.Application.Customers;

public sealed record CompanyProfileView(
    string? CompanyName, string? Address, string? VatNumber, string? KvkNumber,
    string? Niche, string? Website, string? ContactEmail,
    string? Brief = null, bool MentionSourceTool = false,
    string? PitchLine = null, string? CtaLine = null,
    string? CtaButtonText = null, string? CtaButtonUrl = null)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(CompanyName) && string.IsNullOrWhiteSpace(Address)
        && string.IsNullOrWhiteSpace(VatNumber) && string.IsNullOrWhiteSpace(KvkNumber)
        && string.IsNullOrWhiteSpace(Niche) && string.IsNullOrWhiteSpace(Website)
        && string.IsNullOrWhiteSpace(ContactEmail);
}

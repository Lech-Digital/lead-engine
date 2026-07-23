namespace Coldstart.Domain;

public sealed record CompanyProfile(
    string? CompanyName,
    string? Address,
    string? VatNumber,
    string? KvkNumber,
    string? Niche,
    string? Website,
    string? ContactEmail,
    string? Brief = null,
    bool MentionSourceTool = false,
    string? Offer = null,
    string? TargetKeywords = null,
    string? BuyingSignals = null,
    string? Disqualifiers = null)
{
    public static CompanyProfile Empty { get; } = new(null, null, null, null, null, null, null);
}

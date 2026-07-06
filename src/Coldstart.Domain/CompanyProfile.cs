namespace Coldstart.Domain;

public sealed record CompanyProfile(
    string? CompanyName,
    string? Address,
    string? VatNumber,
    string? KvkNumber,
    string? Niche,
    string? Website,
    string? ContactEmail)
{
    public static CompanyProfile Empty { get; } = new(null, null, null, null, null, null, null);
}

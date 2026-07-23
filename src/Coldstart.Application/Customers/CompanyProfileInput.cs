namespace Coldstart.Application.Customers;

public sealed record CompanyProfileInput(
    string? CompanyName, string? Address, string? VatNumber, string? KvkNumber,
    string? Niche, string? Website, string? ContactEmail,
    string? Brief = null, bool MentionSourceTool = false);

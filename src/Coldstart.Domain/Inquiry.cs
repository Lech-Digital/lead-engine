namespace Coldstart.Domain;

public sealed class Inquiry
{
    public InquiryId Id { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public EmailAddress Email { get; private set; } = null!;
    public string Niche { get; private set; } = string.Empty;
    public string WhatYouSell { get; private set; } = string.Empty;
    public string? Company { get; private set; }
    public string? Message { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Inquiry() { } // EF

    private Inquiry(InquiryId id, string name, EmailAddress email, string niche,
        string whatYouSell, string? message, DateTimeOffset createdAt, string? company)
    {
        Id = id;
        Name = name;
        Company = company;
        Email = email;
        Niche = niche;
        WhatYouSell = whatYouSell;
        Message = message;
        CreatedAt = createdAt;
    }

    public static Inquiry Create(string name, EmailAddress email, string niche,
        string whatYouSell, string? message, DateTimeOffset createdAt, string? company = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Inquiry name is required.");
        if (email is null) throw new DomainException("Inquiry email is required.");
        return new Inquiry(InquiryId.New(), name.Trim(), email, niche?.Trim() ?? "",
            whatYouSell?.Trim() ?? "", string.IsNullOrWhiteSpace(message) ? null : message.Trim(), createdAt,
            string.IsNullOrWhiteSpace(company) ? null : company.Trim());
    }
}

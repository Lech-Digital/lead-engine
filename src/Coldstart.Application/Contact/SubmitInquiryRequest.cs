namespace Coldstart.Application.Contact;

public sealed record SubmitInquiryRequest(string Name, string Email, string Niche, string WhatYouSell, string? Message, string? Company = null);

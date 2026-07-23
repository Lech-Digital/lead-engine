using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Contact;

internal sealed class SubmitInquiry(IInquiryRepository inquiries, IEmailSender email) : ISubmitInquiry
{
    private const string NotifyTo = "info@getcoldstart.nl";

    public async Task SubmitAsync(SubmitInquiryRequest request, CancellationToken ct)
    {
        var inquiry = Inquiry.Create(request.Name, new EmailAddress(request.Email),
            request.Niche, request.WhatYouSell, request.Message, DateTimeOffset.UtcNow);
        await inquiries.AddAsync(inquiry, ct);

        var body = $"New Coldstart inquiry\n\nName: {inquiry.Name}\nEmail: {inquiry.Email}\n" +
                   $"Niche: {inquiry.Niche}\nSells: {inquiry.WhatYouSell}\nMessage: {inquiry.Message ?? "(none)"}";
        try
        {
            await email.SendAsync(NotifyTo, $"New inquiry from {inquiry.Name}", body, null, ct);
        }
        catch
        {
            // Inquiry is already persisted; a failed notification must not fail the request.
        }
    }
}

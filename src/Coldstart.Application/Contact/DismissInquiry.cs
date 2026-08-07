using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Contact;

public sealed record InquiryActionResult(bool Succeeded, string Message);

public interface IDismissInquiry
{
    Task<InquiryActionResult> ExecuteAsync(Guid inquiryId, DateTimeOffset when, CancellationToken ct);
}

// Marks an inquiry as spam/not-a-fit. Never deletes it — see Inquiry.Dismiss — so this only ever
// hides the row from the operator's open queue; Restore (below) reverses it losslessly.
internal sealed class DismissInquiry(IInquiryRepository inquiries) : IDismissInquiry
{
    public async Task<InquiryActionResult> ExecuteAsync(Guid inquiryId, DateTimeOffset when, CancellationToken ct)
    {
        var inquiry = await inquiries.GetAsync(new InquiryId(inquiryId), ct);
        if (inquiry is null) return new(false, "No such inquiry.");

        inquiry.Dismiss(when);
        await inquiries.UpdateAsync(inquiry, ct);
        return new(true, $"Dismissed the inquiry from {inquiry.Name}.");
    }
}

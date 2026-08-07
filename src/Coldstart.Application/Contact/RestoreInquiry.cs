using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Contact;

public interface IRestoreInquiry
{
    Task<InquiryActionResult> ExecuteAsync(Guid inquiryId, CancellationToken ct);
}

// Reverses a dismiss. The row was never deleted in the first place, so this simply clears the
// marker — no data was ever at risk of loss.
internal sealed class RestoreInquiry(IInquiryRepository inquiries) : IRestoreInquiry
{
    public async Task<InquiryActionResult> ExecuteAsync(Guid inquiryId, CancellationToken ct)
    {
        var inquiry = await inquiries.GetAsync(new InquiryId(inquiryId), ct);
        if (inquiry is null) return new(false, "No such inquiry.");

        inquiry.Restore();
        await inquiries.UpdateAsync(inquiry, ct);
        return new(true, $"Restored the inquiry from {inquiry.Name}.");
    }
}

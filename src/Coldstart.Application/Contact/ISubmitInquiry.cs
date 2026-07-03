namespace Coldstart.Application.Contact;

public interface ISubmitInquiry
{
    Task SubmitAsync(SubmitInquiryRequest request, CancellationToken ct);
}

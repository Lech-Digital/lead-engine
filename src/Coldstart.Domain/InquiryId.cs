namespace Coldstart.Domain;

public sealed record InquiryId(Guid Value)
{
    public static InquiryId New() => new(Guid.NewGuid());
}

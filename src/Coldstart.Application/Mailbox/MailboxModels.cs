namespace Coldstart.Application.Mailbox;

public sealed record MailboxPage(IReadOnlyList<MailMessageSummary> Items, int TotalCount, int Page, int PageSize);

public sealed record MailMessageSummary(
    uint Uid, string FromName, string FromAddress, string Subject,
    DateTimeOffset Date, bool Seen, bool HasAttachments);

public sealed record MailMessageDetail(
    uint Uid, string FromName, string FromAddress, string To, string Subject,
    DateTimeOffset Date, bool Seen,
    string? HtmlBody,     // sanitized by the Infrastructure adapter — safe to render
    bool RemoteImagesRemoved,
    string TextBody,
    string? MessageId, string? References,
    IReadOnlyList<MailAttachmentInfo> Attachments);

public sealed record MailAttachmentInfo(int Index, string FileName, string ContentType, long Size);

public sealed record MailAttachmentContent(string FileName, string ContentType, byte[] Content);

public sealed record OutgoingMail(
    string To, string Subject, string TextBody,
    string? InReplyToMessageId = null, string? References = null);

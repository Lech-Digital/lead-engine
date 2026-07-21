namespace Coldstart.Application.Mailbox;

public interface IMailbox
{
    Task<MailboxPage> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<MailMessageDetail?> GetAsync(uint uid, CancellationToken ct);
    Task<MailAttachmentContent?> GetAttachmentAsync(uint uid, int index, CancellationToken ct);
    Task MarkReadAsync(uint uid, bool read, CancellationToken ct);
    Task DeleteAsync(uint uid, CancellationToken ct);
    Task SendAsync(OutgoingMail mail, CancellationToken ct);
}

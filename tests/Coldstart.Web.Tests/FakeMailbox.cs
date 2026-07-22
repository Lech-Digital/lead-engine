using Coldstart.Application.Mailbox;

namespace Coldstart.Web.Tests;

// In-memory IMailbox: Messages is the INBOX (newest = highest Uid), Sent captures outgoing mail.
public sealed class FakeMailbox : IMailbox
{
    public List<MailMessageDetail> Messages { get; } = [];
    public List<OutgoingMail> Sent { get; } = [];
    public bool Unreachable { get; set; }

    private void ThrowIfUnreachable()
    {
        if (Unreachable) throw new System.Net.Sockets.SocketException();
    }

    public Task<MailboxPage> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        ThrowIfUnreachable();
        var ordered = Messages.OrderByDescending(m => m.Uid).ToList();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MailMessageSummary(m.Uid, m.FromName, m.FromAddress, m.Subject, m.Date, m.Seen, m.Attachments.Count > 0))
            .ToList();
        return Task.FromResult(new MailboxPage(items, Messages.Count, page, pageSize));
    }

    public Task<MailMessageDetail?> GetAsync(uint uid, CancellationToken ct)
    {
        ThrowIfUnreachable();
        return Task.FromResult(Messages.FirstOrDefault(m => m.Uid == uid));
    }

    public Task<MailAttachmentContent?> GetAttachmentAsync(uint uid, int index, CancellationToken ct)
    {
        ThrowIfUnreachable();
        var msg = Messages.FirstOrDefault(m => m.Uid == uid);
        var info = msg?.Attachments.ElementAtOrDefault(index);
        return Task.FromResult(info is null
            ? null
            : new MailAttachmentContent(info.FileName, info.ContentType, [1, 2, 3]));
    }

    public Task MarkReadAsync(uint uid, bool read, CancellationToken ct)
    {
        ThrowIfUnreachable();
        var i = Messages.FindIndex(m => m.Uid == uid);
        if (i >= 0) Messages[i] = Messages[i] with { Seen = read };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(uint uid, CancellationToken ct)
    {
        ThrowIfUnreachable();
        Messages.RemoveAll(m => m.Uid == uid);
        return Task.CompletedTask;
    }

    public Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        ThrowIfUnreachable();
        Sent.Add(mail);
        return Task.CompletedTask;
    }
}

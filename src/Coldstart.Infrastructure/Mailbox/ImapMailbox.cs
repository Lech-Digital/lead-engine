using Coldstart.Application.Mailbox;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Coldstart.Infrastructure.Mailbox;

internal sealed class ImapMailbox(IOptions<MailboxOptions> options) : IMailbox
{
    private readonly MailboxOptions _o = options.Value;

    private async Task<ImapClient> OpenAsync(CancellationToken ct)
    {
        var client = new ImapClient();
        try
        {
            // Same rationale as SmtpEmailSender: unreachable CRL endpoints must not kill the connection.
            client.CheckCertificateRevocation = false;
            await client.ConnectAsync(_o.Host, _o.ImapPort,
                _o.ImapUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None, ct);
            await client.AuthenticateAsync(_o.Username, _o.Password, ct);
            return client;
        }
        catch
        {
            // Connect/auth failed before ownership transferred to the caller's using block.
            client.Dispose();
            throw;
        }
    }

    public async Task<MailboxPage> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        var total = inbox.Count;
        // Newest first: highest sequence numbers are the most recent arrivals.
        var startIndex = Math.Max(0, total - page * pageSize);
        var count = Math.Min(pageSize, total - (page - 1) * pageSize);
        if (count <= 0)
            return new MailboxPage([], total, page, pageSize);

        var summaries = await inbox.FetchAsync(startIndex, startIndex + count - 1,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope
            | MessageSummaryItems.Flags | MessageSummaryItems.BodyStructure, ct);

        var items = summaries
            .OrderByDescending(s => s.Index)
            .Select(s => new MailMessageSummary(
                s.UniqueId.Id,
                s.Envelope?.From?.Mailboxes.FirstOrDefault()?.Name ?? "",
                s.Envelope?.From?.Mailboxes.FirstOrDefault()?.Address ?? "",
                s.Envelope?.Subject ?? "(no subject)",
                s.Envelope?.Date ?? DateTimeOffset.MinValue,
                s.Flags?.HasFlag(MessageFlags.Seen) ?? false,
                s.Attachments.Any()))
            .ToList();

        await client.DisconnectAsync(true, ct);
        return new MailboxPage(items, total, page, pageSize);
    }

    public async Task<MailMessageDetail?> GetAsync(uint uid, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        var id = new UniqueId(uid);
        MimeMessage message;
        try { message = await inbox.GetMessageAsync(id, ct); }
        catch (MessageNotFoundException) { return null; }

        var summary = (await inbox.FetchAsync([id],
            MessageSummaryItems.Flags | MessageSummaryItems.UniqueId, ct)).FirstOrDefault();

        string? html = null;
        var remoteImagesRemoved = false;
        if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            var sanitized = MailHtmlSanitizer.Sanitize(message.HtmlBody);
            html = sanitized.Html;
            remoteImagesRemoved = sanitized.RemoteImagesRemoved;
        }

        var attachments = message.Attachments
            .Select((a, i) => new MailAttachmentInfo(
                i,
                a.ContentDisposition?.FileName ?? a.ContentType.Name ?? $"attachment-{i}",
                a.ContentType.MimeType,
                (a as MimePart)?.Content?.Stream?.Length ?? 0))
            .ToList();

        var from = message.From.Mailboxes.FirstOrDefault();
        var detail = new MailMessageDetail(
            uid,
            from?.Name ?? "",
            from?.Address ?? "",
            string.Join(", ", message.To.Mailboxes.Select(m => m.Address)),
            message.Subject ?? "(no subject)",
            message.Date,
            summary?.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            html,
            remoteImagesRemoved,
            message.TextBody ?? "",
            message.MessageId is null ? null : $"<{message.MessageId}>",
            message.References.Count == 0 ? null : string.Join(" ", message.References.Select(r => $"<{r}>")),
            attachments);

        await client.DisconnectAsync(true, ct);
        return detail;
    }

    public async Task<MailAttachmentContent?> GetAttachmentAsync(uint uid, int index, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        MimeMessage message;
        try { message = await inbox.GetMessageAsync(new UniqueId(uid), ct); }
        catch (MessageNotFoundException) { return null; }

        var attachment = message.Attachments.ElementAtOrDefault(index);
        if (attachment is not MimePart part) return null;

        using var ms = new MemoryStream();
        await part.Content.DecodeToAsync(ms, ct);
        await client.DisconnectAsync(true, ct);
        return new MailAttachmentContent(
            part.ContentDisposition?.FileName ?? part.ContentType.Name ?? $"attachment-{index}",
            part.ContentType.MimeType,
            ms.ToArray());
    }

    public async Task MarkReadAsync(uint uid, bool read, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
        if (read) await inbox.AddFlagsAsync([new UniqueId(uid)], MessageFlags.Seen, silent: true, ct);
        else await inbox.RemoveFlagsAsync([new UniqueId(uid)], MessageFlags.Seen, silent: true, ct);
        await client.DisconnectAsync(true, ct);
    }

    public async Task DeleteAsync(uint uid, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

        var trash = client.PersonalNamespaces.Count > 0
            ? await FindTrashAsync(client, ct)
            : null;

        var id = new UniqueId(uid);
        if (trash is not null)
        {
            await inbox.MoveToAsync(id, trash, ct);
        }
        else
        {
            await inbox.AddFlagsAsync([id], MessageFlags.Deleted, silent: true, ct);
            await inbox.ExpungeAsync(ct);
        }
        await client.DisconnectAsync(true, ct);
    }

    private static async Task<IMailFolder?> FindTrashAsync(ImapClient client, CancellationToken ct)
    {
        try
        {
            var trash = client.GetFolder(SpecialFolder.Trash);
            if (trash is not null) return trash;
        }
        catch (NotSupportedException) { /* server lacks SPECIAL-USE; fall through to name lookup */ }
        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        var folders = await personal.GetSubfoldersAsync(false, ct);
        return folders.FirstOrDefault(f => f.Name.Equals("Trash", StringComparison.OrdinalIgnoreCase));
    }

    public async Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.Username));
        msg.To.Add(MailboxAddress.Parse(mail.To));
        msg.Subject = mail.Subject;
        msg.Body = new TextPart("plain") { Text = mail.TextBody };
        if (!string.IsNullOrEmpty(mail.InReplyToMessageId))
            msg.InReplyTo = mail.InReplyToMessageId.Trim('<', '>');
        if (!string.IsNullOrEmpty(mail.References))
            foreach (var r in mail.References.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                msg.References.Add(r.Trim('<', '>'));

        using var client = new MailKit.Net.Smtp.SmtpClient();
        client.CheckCertificateRevocation = false;
        await client.ConnectAsync(_o.Host, _o.SmtpPort,
            _o.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}

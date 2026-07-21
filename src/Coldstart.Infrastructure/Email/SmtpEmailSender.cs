using Coldstart.Application.Ports;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Coldstart.Infrastructure.Email;

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _o = options.Value;

    public async Task SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct)
    {
        var msg = BuildMessage(_o, to, subject, textBody, htmlBody);

        using var client = new SmtpClient();
        // CRL/OCSP endpoints are often unreachable from dev machines (macOS + .NET report
        // "incomplete certificate revocation check"), which MailKit's default policy treats
        // as fatal and every send dies on connect. Chain and hostname are still validated.
        client.CheckCertificateRevocation = false;
        await client.ConnectAsync(_o.Host, _o.Port, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }

    internal static MimeMessage BuildMessage(SmtpOptions o, string to, string subject, string textBody, string? htmlBody)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(o.FromName, string.IsNullOrWhiteSpace(o.FromAddress) ? o.Username : o.FromAddress));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = string.IsNullOrEmpty(htmlBody)
            ? new TextPart("plain") { Text = textBody }
            : new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody();
        return msg;
    }
}

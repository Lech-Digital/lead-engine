using System.Net;
using System.Text;
using Coldstart.Application.Outreach;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Coldstart.Infrastructure.Outreach;

internal sealed class SmtpOutreachMailer(IOptions<OutreachMailboxOptions> options) : IOutreachMailer
{
    private readonly OutreachMailboxOptions _o = options.Value;

    public async Task SendAsync(OutreachEmail mail, CancellationToken ct)
    {
        var msg = BuildMessage(_o, mail);

        using var client = new SmtpClient();
        // CRL/OCSP endpoints are often unreachable (macOS + .NET report "incomplete certificate
        // revocation check") which MailKit treats as fatal; chain + hostname are still validated.
        client.CheckCertificateRevocation = false;
        await client.ConnectAsync(_o.Host, _o.SmtpPort,
            _o.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }

    // Deliverability-safe branding: personal plain body + a CSS-only Coldstart signature. No images.
    internal static MimeMessage BuildMessage(OutreachMailboxOptions o, OutreachEmail mail)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(o.FromName, o.Username));
        msg.To.Add(MailboxAddress.Parse(mail.To));
        msg.Subject = mail.Subject;

        var body = mail.Body ?? "";
        msg.Body = new BodyBuilder
        {
            TextBody = body + "\n\n— Andrzej\nFounder, Coldstart\nhttps://getcoldstart.nl",
            HtmlBody = BuildHtml(body),
        }.ToMessageBody();
        return msg;
    }

    private static string BuildHtml(string body)
    {
        var paragraphs = new StringBuilder();
        foreach (var para in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            paragraphs.Append("<p style=\"margin:0 0 12px\">")
                      .Append(WebUtility.HtmlEncode(para).Replace("\n", "<br>"))
                      .Append("</p>");

        // CSS/text wordmark — the '*' is the brand mark, tinted sunrise; no external assets.
        return $"""
        <div style="font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.5;color:#16192B">
          {paragraphs}
          <div style="margin-top:20px;padding-top:12px;border-top:1px solid #E7E2D8;color:#16192B">
            <div style="font-weight:700;letter-spacing:-0.01em">coldstart<span style="color:#FF6A3D">*</span></div>
            <div style="font-size:13px;color:#5A5E70">Andrzej · Founder, Coldstart · <a href="https://getcoldstart.nl" style="color:#FF6A3D;text-decoration:none">getcoldstart.nl</a></div>
          </div>
        </div>
        """;
    }
}

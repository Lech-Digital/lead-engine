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
        msg.From.Add(new MailboxAddress(o.FromName,
            string.IsNullOrWhiteSpace(o.FromAddress) ? o.Username : o.FromAddress));
        msg.To.Add(MailboxAddress.Parse(mail.To));
        msg.Subject = mail.Subject;

        var body = (mail.Body ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        msg.Body = new BodyBuilder
        {
            TextBody = body + "\n\n— Andrzej\nFounder, Coldstart\nhttps://getcoldstart.nl",
            HtmlBody = BuildHtml(body, mail.CtaButtonText),
        }.ToMessageBody();
        return msg;
    }

    private static string BuildHtml(string body, string? ctaButtonText = null)
    {
        // When a button label is configured and the draft ends with a lone URL line,
        // that URL becomes a branded button instead of raw text (the text part keeps it raw).
        var paras = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        string? buttonUrl = null;
        if (!string.IsNullOrWhiteSpace(ctaButtonText) && paras.Count > 0)
        {
            var last = paras[^1].Trim();
            if (Uri.TryCreate(last, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") && !last.Contains(' '))
            {
                buttonUrl = last;
                paras.RemoveAt(paras.Count - 1);
            }
        }
        var paragraphs = new StringBuilder();
        foreach (var para in paras)
        {
            if (para.TrimStart().StartsWith('>'))
            {
                // "> " paragraphs are the engine's quoted analysis — show them as a branded
                // quote block so the reader sees "this is what it wrote about MY company".
                var quoted = string.Join("<br>", para.Split('\n')
                    .Select(l => WebUtility.HtmlEncode(l.TrimStart().TrimStart('>').TrimStart())));
                paragraphs.Append("<blockquote style=\"margin:0 0 12px;padding:10px 14px;border-left:3px solid #FF6A3D;background:#FBF7F1;border-radius:0 8px 8px 0;color:#3A3E52\">")
                          .Append(quoted)
                          .Append("</blockquote>");
                continue;
            }
            paragraphs.Append("<p style=\"margin:0 0 12px\">")
                      .Append(WebUtility.HtmlEncode(para).Replace("\n", "<br>"))
                      .Append("</p>");
        }

        var button = buttonUrl is null ? "" : $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:18px 0 4px"><tr>
          <td style="background:#FF6A3D;border-radius:9px">
            <a href="{buttonUrl}" style="display:inline-block;padding:12px 22px;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:9px">{WebUtility.HtmlEncode(ctaButtonText)}</a>
          </td>
        </tr></table>
        """;

        // CSS/text wordmark — the '*' is the brand mark, tinted sunrise; no external assets.
        return $"""
        <div style="font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.5;color:#16192B">
          {paragraphs}
          {button}
          <div style="margin-top:20px;padding-top:12px;border-top:1px solid #E7E2D8;color:#16192B">
            <div style="font-weight:700;letter-spacing:-0.01em">coldstart<span style="color:#FF6A3D">*</span></div>
            <div style="font-size:13px;color:#5A5E70">Andrzej · Founder, Coldstart · <a href="https://getcoldstart.nl" style="color:#FF6A3D;text-decoration:none">getcoldstart.nl</a></div>
          </div>
        </div>
        """;
    }
}

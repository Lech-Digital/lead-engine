using System.Net;

namespace Coldstart.Web.Email;

internal sealed record EmailContent(string Subject, string TextBody, string HtmlBody);

// Branded, image-free transactional email shell. Table-based + inline CSS so it
// renders across clients and needs no hosted assets while the site isn't deployed.
internal static class EmailTemplates
{
    private const string Site = "https://getcoldstart.nl";

    internal static EmailContent Invitation(string company, string setPasswordUrl) => new(
        Subject: "You're invited to Coldstart",
        TextBody:
            $"You've been invited to Coldstart as the admin for {company}.\n\n" +
            "Coldstart finds companies that need what you sell — every morning — and writes your first email to them. You approve and send.\n\n" +
            $"Set your password to get started:\n\n{setPasswordUrl}\n\n" +
            "This link expires in 7 days. If you weren't expecting this, ignore this email.",
        HtmlBody: Shell(
            heading: "You're invited to Coldstart",
            introHtml: $"You've been invited to Coldstart as the admin for <strong>{Enc(company)}</strong>. " +
                       "Coldstart finds companies that need what you sell — every morning — and writes your first email to them. You approve and send. " +
                       "Set your password to get started.",
            ctaText: "Set your password",
            ctaUrl: setPasswordUrl,
            footnote: "This link expires in 7 days. If you weren't expecting this, you can ignore this email."));

    internal static EmailContent PasswordReset(string setPasswordUrl) => new(
        Subject: "Reset your Coldstart password",
        TextBody:
            $"Set a new password for your Coldstart account:\n\n{setPasswordUrl}\n\n" +
            "This link expires in 7 days. If you didn't ask for this, ignore this email.",
        HtmlBody: Shell(
            heading: "Reset your password",
            introHtml: "Use the button below to set a new password for your Coldstart account.",
            ctaText: "Set a new password",
            ctaUrl: setPasswordUrl,
            footnote: "This link expires in 7 days. If you didn't ask for this, you can ignore this email."));

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string Shell(string heading, string introHtml, string ctaText, string ctaUrl, string footnote) => $$"""
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
<body style="margin:0;padding:0;background:#F1EEE7;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F1EEE7;">
    <tr><td align="center" style="padding:32px 16px;">
      <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#FDFBF7;border:1px solid #E7E2D7;border-radius:12px;overflow:hidden;">
        <tr><td>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
            <td height="4" style="height:4px;background:#1C1F33;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#54425E;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#C96F4A;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#F5C97B;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#FF6A3D;font-size:0;line-height:0;">&nbsp;</td>
          </tr></table>
        </td></tr>
        <tr><td style="padding:28px 32px 4px;">
          <span style="font-family:Helvetica,Arial,sans-serif;font-weight:800;font-size:20px;color:#16192B;letter-spacing:-0.01em;">coldstart<span style="color:#FF6A3D;">*</span></span>
        </td></tr>
        <tr><td style="padding:8px 32px 0;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <h1 style="margin:12px 0 10px;font-size:22px;color:#16192B;">{{heading}}</h1>
          <p style="margin:0 0 24px;font-size:16px;line-height:1.6;color:#5D5A50;">{{introHtml}}</p>
        </td></tr>
        <tr><td style="padding:0 32px 8px;">
          <a href="{{ctaUrl}}" style="display:inline-block;background:#FF6A3D;color:#ffffff;text-decoration:none;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-weight:700;font-size:16px;padding:14px 26px;border-radius:9px;">{{ctaText}} &rarr;</a>
        </td></tr>
        <tr><td style="padding:16px 32px 0;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:13px;line-height:1.5;color:#5D5A50;">Or paste this link into your browser:<br><a href="{{ctaUrl}}" style="color:#E85A2E;word-break:break-all;">{{ctaUrl}}</a></p>
        </td></tr>
        <tr><td style="padding:20px 32px 28px;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:13px;color:#8B8578;">{{footnote}}</p>
        </td></tr>
        <tr><td style="padding:18px 32px;background:#F1EEE7;border-top:1px solid #E7E2D7;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:12px;color:#5D5A50;">Coldstart &middot; Schiedam, the Netherlands &middot; <a href="{{Site}}" style="color:#E85A2E;text-decoration:none;">getcoldstart.nl</a></p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
""";
}

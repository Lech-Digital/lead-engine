using System.Net;

namespace Coldstart.Web.Email;

internal sealed record EmailContent(string Subject, string TextBody, string HtmlBody);

// Branded, image-free transactional email shell. Table-based + inline CSS so it
// renders across clients and needs no hosted assets while the site isn't deployed.
internal static class EmailTemplates
{
    private const string Site = "https://getcoldstart.nl";

    internal sealed record BriefingLead(string Company, string Subject);

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

    internal static EmailContent Welcome(string appUrl) => new(
        Subject: "Welcome to Coldstart — your mornings just changed",
        TextBody:
            "Your Coldstart account is ready.\n\n" +
            "Here's what happens next: we configure the engine to your niche, and it starts hunting overnight. " +
            "Each morning, any leads it finds — already checked against what you sell, with a drafted opening email — will be waiting in your briefing.\n\n" +
            $"Your briefing lives here:\n\n{appUrl}\n\n" +
            "You review, approve or skip, and get on with your day. Nothing is ever sent without you.",
        HtmlBody: Shell(
            heading: "Welcome to Coldstart",
            introHtml: "Your account is ready. Here's what happens next: we configure the engine to your niche, and it starts hunting overnight. " +
                       "Each morning, any leads it finds — already checked against what you sell, with a drafted opening email — will be waiting in your briefing. " +
                       "You review, approve or skip, and get on with your day. Nothing is ever sent without you.",
            ctaText: "Open your briefing",
            ctaUrl: appUrl,
            footnote: "Questions? Just reply to this email."));

    internal static EmailContent MorningBriefing(int count, IReadOnlyList<BriefingLead> leads, string appUrl)
    {
        var word = count == 1 ? "lead" : "leads";
        var textRows = string.Join("\n", leads.Select(l => $"  • {l.Company} — {l.Subject}"));
        var htmlRows = string.Join("", leads.Select(l =>
            $"<tr><td style=\"padding:10px 0;border-bottom:1px solid #E7E2D7;\">" +
            $"<span style=\"font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-weight:700;font-size:15px;color:#16192B;\">{Enc(l.Company)}</span><br>" +
            $"<span style=\"font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:13px;color:#5D5A50;\">{Enc(l.Subject)}</span>" +
            "</td></tr>"));
        return new(
            Subject: $"Your morning briefing — {count} new {word} waiting",
            TextBody:
                $"Good morning. The engine found {count} new {word} for you overnight:\n\n{textRows}\n\n" +
                $"Each comes with a drafted opening email. Review and approve them here:\n\n{appUrl}\n\n" +
                "Nothing is sent without your approval.",
            HtmlBody: Shell(
                heading: $"Good morning — {count} new {word}",
                introHtml: "The engine found these for you overnight, each with a drafted opening email:" +
                           "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:14px 0 4px;\">" + htmlRows + "</table>",
                ctaText: "Open your briefing",
                ctaUrl: appUrl,
                footnote: "You're receiving this because leads were found for your Coldstart account overnight. Nothing is sent without your approval."));
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    internal static EmailContent WeeklyDigest(string company, int found, int sent, int replies, string appUrl)
    {
        string Row(string label, int n) => $"  {label}: {n}\n";
        var replyLine = replies > 0
            ? $"And the best number of all: {replies} {(replies == 1 ? "outcome" : "outcomes")} reported. That's the engine doing its job."
            : "No outcomes reported yet — when a reply lands, mark it on the lead so your numbers tell the real story.";
        return new(
            Subject: $"Your week with Coldstart — {found} found · {sent} sent" + (replies > 0 ? $" · {replies} replies" : ""),
            TextBody:
                $"Good morning — here's what the engine did for {company} last week:\n\n" +
                Row("Leads found", found) + Row("Emails sent", sent) + Row("Outcomes reported", replies) + "\n" +
                replyLine + $"\n\nYour briefing: {appUrl}",
            HtmlBody: Shell(
                heading: "Your week with Coldstart",
                introHtml: $"Here's what the engine did for <strong>{Enc(company)}</strong> last week:" +
                           "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:14px 0 4px;\">" +
                           $"<tr><td style=\"padding:6px 18px 6px 0;color:#5D5A50;\">Leads found</td><td style=\"font-weight:700;color:#16192B;\">{found}</td></tr>" +
                           $"<tr><td style=\"padding:6px 18px 6px 0;color:#5D5A50;\">Emails sent</td><td style=\"font-weight:700;color:#16192B;\">{sent}</td></tr>" +
                           $"<tr><td style=\"padding:6px 18px 6px 0;color:#5D5A50;\">Outcomes reported</td><td style=\"font-weight:700;color:#16192B;\">{replies}</td></tr>" +
                           "</table>" + Enc(replyLine),
                ctaText: "Open your briefing",
                ctaUrl: appUrl,
                footnote: "You get this once a week, only when there's something to report."));
    }

    internal static EmailContent TrialFollowUpDay2(string company, int lockedCount, string appUrl, string optOutUrl)
    {
        var lockedText = lockedCount > 0
            ? $"And they're not alone: {lockedCount} more matches from that same run are sitting in your queue, locked. They unlock the moment you subscribe — no new setup, they're already yours to claim."
            : "They come from a live run of the engine against your niche — the same run that would land in your inbox every morning as a subscriber.";
        return new(
            Subject: lockedCount > 0
                ? $"{lockedCount} more leads are waiting for {company}"
                : $"Your sample leads are waiting, {company}",
            TextBody:
                $"A couple of days ago the engine ran for {company} and your free sample leads landed — each one a company that plausibly needs what you sell, with a drafted opening email attached.\n\n" +
                $"{lockedText}\n\n" +
                $"Have a look when you have a coffee in hand:\n\n{appUrl}\n\n" +
                $"Nothing is ever sent without your approval.\n\nStop these reminders (one click, no login): {optOutUrl}",
            HtmlBody: Shell(
                heading: "Your leads are waiting",
                introHtml: $"A couple of days ago the engine ran for <strong>{Enc(company)}</strong> and your free sample leads landed — " +
                           "each one a company that plausibly needs what you sell, with a drafted opening email attached. " +
                           Enc(lockedText),
                ctaText: "Open your briefing",
                ctaUrl: appUrl,
                footnote: OptOutFootnote(optOutUrl)));
    }

    internal static EmailContent TrialFollowUpDay5(string company, int lockedCount, string appUrl, string optOutUrl)
    {
        var queueLine = lockedCount > 0
            ? $"The {lockedCount} locked leads in your queue were caught in a moment — a fresh funding round, a new hiring push. "
            : "The leads we find are caught in a moment — a fresh funding round, a new hiring push. ";
        return new(
            Subject: $"Leads cool down fast — an honest note for {company}",
            TextBody:
                $"One thing we've learned doing this: timing beats everything.\n\n" +
                queueLine +
                "That's exactly when a good first email works — and every week that passes, someone else gets there first.\n\n" +
                $"A subscription switches the engine on for {company}: fresh leads every morning, drafts included, €750/month, cancel any month.\n\n" +
                $"Switch it on here:\n\n{appUrl}\n\n" +
                $"This is the last reminder we'll send — promise.\n\nStop these reminders (one click, no login): {optOutUrl}",
            HtmlBody: Shell(
                heading: "Leads cool down fast",
                introHtml: "One thing we've learned doing this: timing beats everything. " +
                           Enc(queueLine) +
                           "That's exactly when a good first email works — and every week that passes, someone else gets there first. " +
                           $"A subscription switches the engine on for <strong>{Enc(company)}</strong>: fresh leads every morning, drafts included, €750/month, cancel any month.",
                ctaText: "Switch the engine on",
                ctaUrl: appUrl,
                footnote: "This is the last reminder we'll send — promise. " + OptOutFootnote(optOutUrl)));
    }

    private static string OptOutFootnote(string optOutUrl) =>
        $"You're getting this because you claimed free sample leads on Coldstart. We send at most two of these, ever. " +
        $"<a href=\"{optOutUrl}\" style=\"color:#8B8578;\">Stop these reminders</a> — one click, no login.";

    private static string Shell(string heading, string introHtml, string ctaText, string ctaUrl, string footnote) => $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <style>
    /* Small-screen tightening; base layout is fluid so this only trims padding. */
    @media only screen and (max-width:600px){
      .cs-card{width:100%!important;border-radius:0!important;}
      .cs-pad{padding-left:20px!important;padding-right:20px!important;}
      .cs-h1{font-size:20px!important;}
      .cs-cta{display:block!important;text-align:center!important;}
    }
  </style>
</head>
<body style="margin:0;padding:0;background:#F1EEE7;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F1EEE7;">
    <tr><td align="center" style="padding:24px 12px;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" class="cs-card" style="max-width:600px;width:100%;background:#FDFBF7;border:1px solid #E7E2D7;border-radius:12px;overflow:hidden;">
        <tr><td>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
            <td height="4" style="height:4px;background:#1C1F33;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#54425E;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#C96F4A;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#F5C97B;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#FF6A3D;font-size:0;line-height:0;">&nbsp;</td>
          </tr></table>
        </td></tr>
        <tr><td class="cs-pad" style="padding:28px 32px 4px;">
          <span style="font-family:Helvetica,Arial,sans-serif;font-weight:800;font-size:20px;color:#16192B;letter-spacing:-0.01em;">coldstart<span style="color:#FF6A3D;">*</span></span>
        </td></tr>
        <tr><td class="cs-pad" style="padding:8px 32px 0;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <h1 class="cs-h1" style="margin:12px 0 10px;font-size:22px;color:#16192B;">{{heading}}</h1>
          <p style="margin:0 0 24px;font-size:16px;line-height:1.6;color:#5D5A50;">{{introHtml}}</p>
        </td></tr>
        <tr><td class="cs-pad" style="padding:0 32px 8px;">
          <a href="{{ctaUrl}}" class="cs-cta" style="display:inline-block;background:#FF6A3D;color:#ffffff;text-decoration:none;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-weight:700;font-size:16px;padding:14px 26px;border-radius:9px;">{{ctaText}} &rarr;</a>
        </td></tr>
        <tr><td class="cs-pad" style="padding:16px 32px 0;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:13px;line-height:1.5;color:#5D5A50;">Or paste this link into your browser:<br><a href="{{ctaUrl}}" style="color:#E85A2E;word-break:break-all;">{{ctaUrl}}</a></p>
        </td></tr>
        <tr><td class="cs-pad" style="padding:20px 32px 28px;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:13px;color:#8B8578;">{{footnote}}</p>
        </td></tr>
        <tr><td class="cs-pad" style="padding:18px 32px;background:#F1EEE7;border-top:1px solid #E7E2D7;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:12px;color:#5D5A50;">Coldstart &middot; Schiedam, the Netherlands &middot; <a href="{{Site}}" style="color:#E85A2E;text-decoration:none;">getcoldstart.nl</a></p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
""";
}

# Briefing Notifications (Slack + WhatsApp) & Settings Page — Design

**Date:** 2026-07-30
**Status:** Accepted

## Purpose

When the engine saves new leads for a customer, push a short summary to the customer's
Slack and/or WhatsApp so the morning briefing is a push, not a pull. Give the customer a
proper settings area (`/app/settings`) where these channels — and everything else about
their account — live in clean sections.

## Notification design

**Trigger:** `HarvestQueueService`, after a job completes with `report.Saved > 0`
(covers manual, invite-time, and nightly harvests). Best-effort: a notification failure
is logged and never fails the harvest.

**Message (summary + deep links):**
```
☀️ Coldstart: 3 new leads this morning
• Cascade — Live demo (and free leads for Cascade)
• Bluecore Energy — …
• Inforcer — …
Review them: https://getcoldstart.nl/app
```
Slack: same text with `<url|Company>` links per lead (link → `/app/leads/{id}`), sent as
`{"text": …}` to the customer's incoming-webhook URL. WhatsApp: plain text (no per-lead
links — one link at the end; WhatsApp mangles link-heavy messages), sent via Twilio's
Messages API from the configured WhatsApp sender to the customer's number.

**Architecture (hexagonal):**
- Application port `IBriefingNotifier` + record
  `BriefingNotification(Guid CustomerId, string CustomerName, IReadOnlyList<NotifiedLead> Leads)`,
  `NotifiedLead(Guid Id, string Company, string Subject)`.
- Application use case `NotifyBriefing : INotifyBriefing` — loads the customer, checks
  which channels are configured, builds the notification, and calls every configured
  `IBriefingChannel` (Slack, WhatsApp). Channels that aren't configured are skipped.
  Channel errors are caught per channel (one failing channel must not stop the other).
- Infrastructure adapters:
  - `SlackBriefingChannel` — `HttpClient` POST of `{"text": …}` to the stored webhook URL.
  - `TwilioWhatsAppChannel` — POST to
    `https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json` (basic auth
    sid:token), form fields `From=whatsapp:{TwilioOptions.WhatsAppFrom}`,
    `To=whatsapp:{customer number}`, `Body=…`.
- Options: `NotificationsOptions { BaseUrl }` (deep-link base, e.g. https://getcoldstart.nl),
  `TwilioOptions { AccountSid, AuthToken, WhatsAppFrom }` — global operator credentials,
  env-configured, soft-degrade (unset → WhatsApp channel reports not configured).

**Per-customer state (on `Customer`):** `SlackWebhookUrl` (string?), `WhatsAppNumber`
(string?, E.164), method `ConfigureNotifications(slackWebhookUrl, whatsAppNumber)`.
Migration `AddCustomerNotificationChannels`. A channel is "on" when its value is present;
clearing the field turns it off. (No separate enabled flag in v1 — YAGNI.)

**Security/privacy:** webhook URLs and phone numbers are customer-supplied secrets;
never logged in full. Slack URL must start with `https://hooks.slack.com/` (validated on
save). WhatsApp number validated as `+` and 8–15 digits.

## Settings page design

`/app/settings` (authorized, tenant-scoped) with anchored sections, each its own form:

1. **Company** — name, website, niche, contact email, address, VAT, KvK.
2. **Outreach voice** — brief (+ Polish with AI), pitch paragraph, intro line;
   operator-only extras (CTA paragraph, button label/link, subject template, mention
   toggle) exactly as on the current profile page.
3. **Notifications** — Slack webhook URL + WhatsApp number, with a **Send test**
   button per channel (posts a test notification through the real channel).
4. **Account** — signed-in email, link to change password (existing Identity flow),
   log out.

`/app/profile` becomes a permanent redirect to `/app/settings#company` (old links and
hints keep working). The app nav's "profile" chip points at `/app/settings`.

The existing `ProfileModel` logic (load/update/polish, operator-only preservation)
moves to `SettingsModel` unchanged; notification fields get their own handlers
(`SaveNotifications`, `TestSlack`, `TestWhatsApp`) that call `IUpdateNotifications` /
`INotifyBriefing` (test = a one-lead fake notification).

Application: `IUpdateNotificationSettings.UpdateAsync(customerId, slackUrl, whatsAppNumber, ct)`
(validates formats, normalizes empty → null).

## Testing

- Application: NotifyBriefing (skips unconfigured channels; per-channel error isolation),
  UpdateNotificationSettings (validation, normalization).
- Infrastructure: Slack channel posts correct JSON to the webhook (stub handler);
  Twilio channel posts correct form + auth (stub handler); message formatting.
- Infrastructure: HarvestQueueService fires INotifyBriefing when Saved>0, not when 0.
- Web: settings page renders sections; profile redirects; save/test handlers;
  operator-only fields still gated; existing profile tests migrated.

## Out of scope (v1)

- WhatsApp production template approval (sandbox/freeform first; template is a Twilio
  console task once the account is verified).
- Per-lead notification (only batch summaries).
- Email digest channel (the template exists; separate feature).
- Quiet hours / scheduling preferences.

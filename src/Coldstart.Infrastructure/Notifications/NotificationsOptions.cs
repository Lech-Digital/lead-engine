namespace Coldstart.Infrastructure.Notifications;

public sealed class NotificationsOptions
{
    // Deep-link base for push messages, e.g. https://getcoldstart.nl
    public string BaseUrl { get; set; } = "https://getcoldstart.nl";
}

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    // The Twilio WhatsApp sender, e.g. +14155238886 (sandbox) or your approved number.
    public string WhatsAppFrom { get; set; } = "";
    // Approved WhatsApp content template SID (HX…). Business-initiated messages REQUIRE
    // a template in production; empty = freeform Body (sandbox / 24h service window only).
    public string ContentSid { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(WhatsAppFrom);
}

namespace Coldstart.Application.Outreach;

// Outbound port: sends a single cold-outreach email from the operator's Coldstart identity.
// The implementation brands the body (deliverability-safe: CSS signature, no images) and throws on failure.
public interface IOutreachMailer
{
    Task SendAsync(OutreachEmail mail, CancellationToken ct);
}

// Body is the approved plain-text draft; the adapter builds the text + branded-HTML alternatives.
public sealed record OutreachEmail(string To, string Subject, string Body);

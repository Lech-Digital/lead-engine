using Coldstart.Application.Ports;

namespace Coldstart.Web.Tests;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string TextBody, string? HtmlBody)> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct)
    {
        Sent.Add((to, subject, textBody, htmlBody));
        return Task.CompletedTask;
    }
}

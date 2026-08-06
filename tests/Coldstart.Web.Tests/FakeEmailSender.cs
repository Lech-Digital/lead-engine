using Coldstart.Application.Ports;

namespace Coldstart.Web.Tests;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string TextBody, string? HtmlBody)> Sent { get; } = new();

    // Off by default so no existing test's behaviour changes; flip on to simulate an SMTP failure.
    public bool Unreachable { get; set; }

    public Task SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct)
    {
        if (Unreachable) throw new System.Net.Sockets.SocketException();
        Sent.Add((to, subject, textBody, htmlBody));
        return Task.CompletedTask;
    }
}

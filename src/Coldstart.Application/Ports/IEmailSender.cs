namespace Coldstart.Application.Ports;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct);
}

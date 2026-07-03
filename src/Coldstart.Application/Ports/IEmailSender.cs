namespace Coldstart.Application.Ports;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

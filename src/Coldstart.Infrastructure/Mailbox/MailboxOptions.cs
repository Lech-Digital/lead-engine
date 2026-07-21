namespace Coldstart.Infrastructure.Mailbox;

public sealed class MailboxOptions
{
    public string Host { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public int SmtpPort { get; set; } = 587;
    public bool ImapUseSsl { get; set; } = true;       // false only for plaintext test servers
    public bool SmtpUseStartTls { get; set; } = true;  // false only for plaintext test servers
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromName { get; set; } = "Coldstart";
}

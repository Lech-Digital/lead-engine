namespace Coldstart.Infrastructure.Outreach;

public sealed class OutreachMailboxOptions
{
    public string Host { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseStartTls { get; set; } = true; // false only for plaintext test servers
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromName { get; set; } = "Andrzej — Coldstart";
}

namespace Coldstart.Infrastructure.Email;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 465;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromName { get; set; } = "Coldstart";
    public string FromAddress { get; set; } = "";
}

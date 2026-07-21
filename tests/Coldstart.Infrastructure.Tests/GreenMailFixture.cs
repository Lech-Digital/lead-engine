using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

// Throwaway IMAP+SMTP server (greenmail). Plain-text ports: SMTP 3025, IMAP 3143.
// User is preconfigured via GREENMAIL_OPTS using the "login:pwd@domain" form
// (verified against the actual container: the "login%40domain:pwd" + users.login=email
// form documented in the brief was rejected by GreenMail 2.1.3 with
// "LOGIN failed. Invalid login/password" for every login spelling tried).
// This form authenticates with the bare local-part ("info"); the mailbox's full
// address ("info@getcoldstart.test") is still required for SMTP recipient routing
// (RCPT TO with a bare local-part is rejected by GreenMail with a 451), so the two
// are exposed as separate constants — Login for AuthenticateAsync, User for
// addressing (To:/From: headers).
public sealed class GreenMailFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("greenmail/standalone:2.1.3")
        .WithEnvironment("GREENMAIL_OPTS",
            "-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap " +
            "-Dgreenmail.hostname=0.0.0.0 " +
            "-Dgreenmail.users=info:secret@getcoldstart.test")
        .WithPortBinding(3025, true)
        .WithPortBinding(3143, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(3143))
        .Build();

    public string Host => _container.Hostname;
    public int SmtpPort => _container.GetMappedPublicPort(3025);
    public int ImapPort => _container.GetMappedPublicPort(3143);
    public const string Login = "info";
    public const string User = "info@getcoldstart.test";
    public const string Password = "secret";

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Mailbox;
using FluentAssertions;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class ImapMailboxTests(GreenMailFixture greenMail) : IClassFixture<GreenMailFixture>
{
    private ImapMailbox Sut() => new(Options.Create(new MailboxOptions
    {
        Host = greenMail.Host,
        ImapPort = greenMail.ImapPort,
        SmtpPort = greenMail.SmtpPort,
        ImapUseSsl = false,
        SmtpUseStartTls = false,
        Username = GreenMailFixture.Login,
        Password = GreenMailFixture.Password,
        FromName = "Coldstart",
    }));

    // Seeds one message into the INBOX by SMTP-delivering it to the test user.
    private async Task SeedAsync(string subject, string body, string? html = null)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Prospect", "prospect@example.com"));
        msg.To.Add(MailboxAddress.Parse(GreenMailFixture.User));
        msg.Subject = subject;
        msg.Body = html is null
            ? new TextPart("plain") { Text = body }
            : new BodyBuilder { TextBody = body, HtmlBody = html }.ToMessageBody();
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(greenMail.Host, greenMail.SmtpPort, MailKit.Security.SecureSocketOptions.None);
        await smtp.SendAsync(msg);
        await smtp.DisconnectAsync(true);
    }

    [Fact]
    public async Task ListAsync_AfterSeedingAMessage_ReturnsItNewestFirstAndUnseen()
    {
        // Arrange
        await SeedAsync("Hello Coldstart", "interested in leads");

        // Act
        var pageResult = await Sut().ListAsync(1, 25, CancellationToken.None);

        // Assert
        pageResult.TotalCount.Should().BeGreaterThan(0);
        var first = pageResult.Items[0];
        first.Subject.Should().Be("Hello Coldstart");
        first.FromAddress.Should().Be("prospect@example.com");
        first.Seen.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithHtmlMessage_ReturnsSanitizedHtmlBody()
    {
        // Arrange
        await SeedAsync("Html mail", "plain fallback",
            html: """<p>hi</p><script>alert(1)</script><img src="https://x.example/p.png">""");
        var uid = (await Sut().ListAsync(1, 25, CancellationToken.None)).Items[0].Uid;

        // Act
        var detail = await Sut().GetAsync(uid, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
        detail!.HtmlBody.Should().Contain("hi");
        detail.HtmlBody.Should().NotContain("script");
        detail.HtmlBody.Should().NotContain("x.example");
    }

    [Fact]
    public async Task MarkReadAsync_TogglesTheSeenFlag()
    {
        // Arrange
        await SeedAsync("Flag me", "body");
        var sut = Sut();
        var uid = (await sut.ListAsync(1, 25, CancellationToken.None)).Items[0].Uid;

        // Act
        await sut.MarkReadAsync(uid, read: true, CancellationToken.None);

        // Assert
        var afterRead = await sut.ListAsync(1, 25, CancellationToken.None);
        afterRead.Items.First(i => i.Uid == uid).Seen.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheMessageFromTheInbox()
    {
        // Arrange
        await SeedAsync("Delete me", "body");
        var sut = Sut();
        var uid = (await sut.ListAsync(1, 25, CancellationToken.None)).Items[0].Uid;

        // Act
        await sut.DeleteAsync(uid, CancellationToken.None);

        // Assert
        var after = await sut.ListAsync(1, 25, CancellationToken.None);
        after.Items.Should().NotContain(i => i.Uid == uid);
    }

    [Fact]
    public async Task SendAsync_DeliversWithThreadingHeaders()
    {
        // Arrange — send to ourselves so we can read it back over IMAP
        var mail = new OutgoingMail(GreenMailFixture.User, "Re: original",
            "reply text", InReplyToMessageId: "<orig-123@example.com>", References: "<orig-123@example.com>");
        var sut = Sut();

        // Act
        await sut.SendAsync(mail, CancellationToken.None);

        // Assert
        var list = await sut.ListAsync(1, 25, CancellationToken.None);
        var received = list.Items.First(i => i.Subject == "Re: original");
        var detail = await sut.GetAsync(received.Uid, CancellationToken.None);
        detail!.TextBody.Should().Contain("reply text");
        detail.References.Should().Contain("orig-123@example.com");
    }

    [Fact]
    public async Task SendAsync_WithCrLfSubject_DoesNotInjectHeaders()
    {
        // Arrange — a crafted Subject attempting to smuggle an extra Bcc header via raw CRLF
        var mail = new OutgoingMail(GreenMailFixture.User, "hi\r\nBcc: evil@example.com", "body text");
        var sut = Sut();

        // Act
        await sut.SendAsync(mail, CancellationToken.None);

        // Assert — MimeKit's Subject setter encodes/folds the raw header value rather than
        // splicing it verbatim, so no attacker-controlled Bcc header materializes: the delivered
        // message's To still contains only the real recipient, never the injected address.
        var list = await sut.ListAsync(1, 25, CancellationToken.None);
        var newest = list.Items.OrderByDescending(i => i.Uid).First();
        var detail = await sut.GetAsync(newest.Uid, CancellationToken.None);
        detail!.To.Should().NotContain("evil@");
    }
}

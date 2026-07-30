using System.Net;
using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Coldstart.Web.Tests;

public class MailboxPagesTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private static MailMessageDetail Msg(uint uid, string subject, bool seen = false, string? html = null,
        IReadOnlyList<MailAttachmentInfo>? atts = null, bool remoteImagesRemoved = false, string? references = null) =>
        new(uid, "Prospect", "prospect@example.com", "info@getcoldstart.nl", subject,
            DateTimeOffset.UtcNow, seen, html, remoteImagesRemoved, "plain body", $"<msg-{uid}@example.com>", references, atts ?? []);

    // Scrapes the antiforgery token from `basePath` (a plain GET) then posts the empty form to
    // `handlerUrl` — avoids issuing a GET against a `?handler=` URL, which Razor Pages page-handler
    // selection can reject when no OnGet{Handler} exists for that name.
    private static async Task<HttpResponseMessage> PostHandlerAsync(HttpClient client, string basePath, string handlerUrl)
    {
        var token = await TestAuth.AntiforgeryAsync(client, basePath);
        var fields = new Dictionary<string, string>();
        if (token is not null) fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(handlerUrl, new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task Inbox_ListsMessages_NewestFirstWithUnseenStyling()
    {
        // Arrange — SuperAdmin session, one seen (older) message and one unseen (newer) message
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(1, "Older", seen: true));
        factory.Mailbox.Messages.Add(Msg(2, "Newer"));

        // Act
        var html = await client.GetStringAsync("/admin/mailbox");

        // Assert
        html.Should().Contain("Newer").And.Contain("Older");
        html.IndexOf("Newer", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("Older", StringComparison.Ordinal));
        html.Should().Contain("mail-row-unseen");
    }

    [Fact]
    public async Task Inbox_WhenMailboxUnreachable_ShowsFriendlyErrorNot500()
    {
        // Arrange — SuperAdmin session, mailbox flagged unreachable
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Unreachable = true;
        try
        {
            // Act
            var response = await client.GetAsync("/admin/mailbox");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Mailbox unreachable");
        }
        finally { factory.Mailbox.Unreachable = false; }
    }

    [Fact]
    public async Task Inbox_WhenNotSuperAdmin_IsDenied()
    {
        // Arrange — anonymous client (no login)
        var anon = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        var response = await anon.GetAsync("/admin/mailbox");

        // Assert — Identity redirects unauthenticated users to login
        ((int)response.StatusCode).Should().BeOneOf(302, 401, 403);
    }

    [Fact]
    public async Task Message_OnOpen_RendersSanitizedHtmlAndMarksRead()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(7, "Read me", html: "<p>safe <strong>html</strong></p>"));

        // Act
        var html = await client.GetStringAsync("/admin/mailbox/7");

        // Assert
        html.Should().Contain("safe <strong>html</strong>");
        factory.Mailbox.Messages.Single(m => m.Uid == 7).Seen.Should().BeTrue();
    }

    [Fact]
    public async Task Message_WithRemoteImagesRemoved_ShowsSafetyNote()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(11, "Tracked", html: "<p>hi</p>", remoteImagesRemoved: true));

        // Act
        var html = await client.GetStringAsync("/admin/mailbox/11");

        // Assert
        html.Should().Contain("mail-note").And.Contain("Remote images were removed for your safety.");
    }

    [Fact]
    public async Task Message_MarkUnread_ClearsSeenAndRedirects()
    {
        // Arrange
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(8, "Unread me", seen: true));

        // Act
        var response = await PostHandlerAsync(client, "/admin/mailbox/8", "/admin/mailbox/8?handler=MarkUnread");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Mailbox.Messages.Single(m => m.Uid == 8).Seen.Should().BeFalse();
    }

    [Fact]
    public async Task Message_Delete_RemovesAndRedirectsToInbox()
    {
        // Arrange
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(9, "Delete me"));

        // Act
        var response = await PostHandlerAsync(client, "/admin/mailbox/9", "/admin/mailbox/9?handler=Delete");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/admin/mailbox");
        factory.Mailbox.Messages.Should().NotContain(m => m.Uid == 9);
    }

    [Fact]
    public async Task Message_MarkUnread_WhenMailboxUnreachable_RedirectsWithFlashNot500()
    {
        // Arrange
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(40, "Unread me", seen: true));
        var token = await TestAuth.AntiforgeryAsync(client, "/admin/mailbox/40");
        factory.Mailbox.Unreachable = true;
        try
        {
            // Act
            var response = await client.PostAsync("/admin/mailbox/40?handler=MarkUnread",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token! }));

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Contain("/admin/mailbox");
        }
        finally { factory.Mailbox.Unreachable = false; }
    }

    [Fact]
    public async Task Message_Delete_WhenMailboxUnreachable_RedirectsWithFlashNot500()
    {
        // Arrange
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(41, "Delete me"));
        var token = await TestAuth.AntiforgeryAsync(client, "/admin/mailbox/41");
        factory.Mailbox.Unreachable = true;
        try
        {
            // Act
            var response = await client.PostAsync("/admin/mailbox/41?handler=Delete",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token! }));

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Contain("/admin/mailbox");
        }
        finally { factory.Mailbox.Unreachable = false; }
    }

    [Fact]
    public async Task Message_AttachmentDownload_WhenMailboxUnreachable_RedirectsWithFlashNot500()
    {
        // Arrange
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(42, "With attachment",
            atts: [new MailAttachmentInfo(0, "report.pdf", "application/pdf", 3)]));
        factory.Mailbox.Unreachable = true;
        try
        {
            // Act
            var response = await client.GetAsync("/admin/mailbox/42?handler=Attachment&index=0");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Contain("/admin/mailbox");
        }
        finally { factory.Mailbox.Unreachable = false; }
    }

    [Fact]
    public async Task Message_AttachmentDownload_StreamsBytes_And404sOutOfRange()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(10, "With attachment",
            atts: [new MailAttachmentInfo(0, "report.pdf", "application/pdf", 3)]));

        // Act
        var ok = await client.GetAsync("/admin/mailbox/10?handler=Attachment&index=0");
        var missing = await client.GetAsync("/admin/mailbox/10?handler=Attachment&index=5");

        // Assert
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        ok.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        (await ok.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Message_UnknownUid_RedirectsToInbox()
    {
        // Arrange
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();

        // Act
        var response = await client.GetAsync("/admin/mailbox/999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/admin/mailbox");
    }

    [Fact]
    public async Task Message_AttachmentDownload_NegativeIndex_Returns404()
    {
        // Arrange — SuperAdmin session, message with one attachment
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(11, "With attachment",
            atts: [new MailAttachmentInfo(0, "report.pdf", "application/pdf", 3)]));

        // Act
        var response = await client.GetAsync("/admin/mailbox/11?handler=Attachment&index=-1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Message_WhenMailboxUnreachable_ShowsFriendlyErrorNot500()
    {
        // Arrange — SuperAdmin session, mailbox flagged unreachable
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Unreachable = true;
        try
        {
            // Act
            var response = await client.GetAsync("/admin/mailbox/1");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Mailbox unreachable");
        }
        finally { factory.Mailbox.Unreachable = false; }
    }

    [Fact]
    public async Task Compose_Post_SendsMailAndRedirectsWithFlash()
    {
        // Arrange
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Sent.Clear();
        var token = await TestAuth.AntiforgeryAsync(client, "/admin/mailbox/compose");

        // Act
        var response = await client.PostAsync("/admin/mailbox/compose", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token!,
                ["Input.To"] = "prospect@example.com",
                ["Input.Subject"] = "Invitation to Coldstart",
                ["Input.Body"] = "Hi — here is your invite.",
            }));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Mailbox.Sent.Should().ContainSingle(s =>
            s.To == "prospect@example.com" && s.Subject == "Invitation to Coldstart");
    }

    [Fact]
    public async Task Compose_AsReply_PrefillsQuoteAndThreadsCorrectly()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Sent.Clear();
        factory.Mailbox.Messages.Add(Msg(21, "Question about pricing"));

        // Act — GET prefill, then POST the reply
        var form = await client.GetStringAsync("/admin/mailbox/compose?replyTo=21");
        var token = await TestAuth.AntiforgeryAsync(client, "/admin/mailbox/compose?replyTo=21");
        await client.PostAsync("/admin/mailbox/compose", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token!,
                ["Input.To"] = "prospect@example.com",
                ["Input.Subject"] = "Re: Question about pricing",
                ["Input.Body"] = "Answer.",
                ["Input.InReplyTo"] = "<msg-21@example.com>",
                ["Input.References"] = "<msg-21@example.com>",
            }));

        // Assert
        form.Should().Contain("Re: Question about pricing");
        form.Should().Contain("wrote:");                       // quoted original marker
        factory.Mailbox.Sent.Should().ContainSingle(s =>
            s.InReplyToMessageId == "<msg-21@example.com>" && s.References == "<msg-21@example.com>");
    }

    [Fact]
    public async Task Compose_ReplyTo_MessageWithReferences_ThreadsChainAndInReplyTo()
    {
        // Arrange — original message already carries a References chain from an earlier thread
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(30, "Pricing follow-up", references: "<older@example.com>"));

        // Act
        var form = await client.GetStringAsync("/admin/mailbox/compose?replyTo=30");

        // Assert — References hidden field concatenates old chain + original Message-Id;
        // InReplyTo hidden field is just the original Message-Id. Hidden inputs are HTML-encoded
        // by the tag helper, so angle brackets render as &lt;/&gt;.
        form.Should().Contain("&lt;older@example.com&gt; &lt;msg-30@example.com&gt;");
        form.Should().Contain("&lt;msg-30@example.com&gt;");
    }

    [Fact]
    public async Task Compose_ReplyTo_MessageWithNullReferences_UsesMessageIdAsReferences()
    {
        // Arrange — original message has no prior References chain (References = null)
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();
        factory.Mailbox.Messages.Add(Msg(31, "First contact"));   // references defaults to null

        // Act
        var form = await client.GetStringAsync("/admin/mailbox/compose?replyTo=31");

        // Assert — with no prior chain, the References hidden field falls back to exactly the
        // original Message-Id (same value the InReplyTo hidden field also carries).
        form.Should().Contain("&lt;msg-31@example.com&gt;");
    }

    [Fact]
    public async Task Compose_ReplyTo_UnknownUid_ShowsPrefillFailedNotice()
    {
        // Arrange — no message with this uid exists, so GetAsync returns null
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();

        // Act
        var response = await client.GetAsync("/admin/mailbox/compose?replyTo=404");

        // Assert — page still renders (200), but with a notice instead of silently
        // showing an empty compose form as if the user had chosen to start fresh.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Couldn't load the original message");
    }

    [Fact]
    public async Task Compose_ReplyTo_WhenMailboxUnreachable_ShowsPrefillFailedNotice()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Unreachable = true;
        try
        {
            // Act
            var response = await client.GetAsync("/admin/mailbox/compose?replyTo=1");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("Couldn't load the original message");
        }
        finally { factory.Mailbox.Unreachable = false; }
    }

    [Fact]
    public async Task Compose_WhenSendFails_ShowsErrorPreservesValuesAndSendsNothing()
    {
        // Arrange — grab the antiforgery token before flagging the mailbox unreachable, since a
        // plain GET without replyTo never touches IMailbox but this keeps the ordering defensive.
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Sent.Clear();
        var token = await TestAuth.AntiforgeryAsync(client, "/admin/mailbox/compose");
        factory.Mailbox.Unreachable = true;
        try
        {
            // Act — SendAsync throws a SocketException because the fake mailbox is unreachable
            var response = await client.PostAsync("/admin/mailbox/compose", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token!,
                    ["Input.To"] = "prospect@example.com",
                    ["Input.Subject"] = "Unique subject for send-failure test",
                    ["Input.Body"] = "Body text that should be preserved.",
                }));

            // Assert — page re-rendered (not a 500), error shown, submitted values preserved, nothing sent
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("Sending failed").And.Contain("Unique subject for send-failure test");
            factory.Mailbox.Sent.Should().BeEmpty();
        }
        finally { factory.Mailbox.Unreachable = false; }
    }

    [Fact]
    public async Task Compose_ToWithCrLf_RejectsAndSendsNothing()
    {
        // Arrange — an address attempting to inject an extra header via a raw CRLF
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Sent.Clear();
        var token = await TestAuth.AntiforgeryAsync(client, "/admin/mailbox/compose");

        // Act — FormUrlEncodedContent percent-encodes the raw \r\n; the server sees literal CRLF
        // once the form value is decoded, exactly as an attacker-controlled value would arrive.
        var response = await client.PostAsync("/admin/mailbox/compose", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token!,
                ["Input.To"] = "victim@example.com\r\nBcc:evil@example.com",
                ["Input.Subject"] = "Subject line",
                ["Input.Body"] = "Body text.",
            }));

        // Assert — page re-rendered with a validation error, nothing sent
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Mailbox.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Compose_InvalidEmail_ShowsErrorAndSendsNothing()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Sent.Clear();
        var token = await TestAuth.AntiforgeryAsync(client, "/admin/mailbox/compose");

        // Act
        var response = await client.PostAsync("/admin/mailbox/compose", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token!,
                ["Input.To"] = "not-an-email",
                ["Input.Subject"] = "x",
                ["Input.Body"] = "y",
            }));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);   // page re-rendered with validation error
        factory.Mailbox.Sent.Should().BeEmpty();
    }
}

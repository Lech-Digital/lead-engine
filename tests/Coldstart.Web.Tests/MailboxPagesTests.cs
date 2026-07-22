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
        IReadOnlyList<MailAttachmentInfo>? atts = null, bool remoteImagesRemoved = false) =>
        new(uid, "Prospect", "prospect@example.com", "info@getcoldstart.nl", subject,
            DateTimeOffset.UtcNow, seen, html, remoteImagesRemoved, "plain body", $"<msg-{uid}@example.com>", null, atts ?? []);

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
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
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
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
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
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
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
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
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
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
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
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
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
    public async Task Message_AttachmentDownload_StreamsBytes_And404sOutOfRange()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
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
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        factory.Mailbox.Messages.Clear();

        // Act
        var response = await client.GetAsync("/admin/mailbox/999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/admin/mailbox");
    }
}

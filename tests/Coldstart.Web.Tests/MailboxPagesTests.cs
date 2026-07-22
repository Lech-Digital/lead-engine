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
        IReadOnlyList<MailAttachmentInfo>? atts = null) =>
        new(uid, "Prospect", "prospect@example.com", "info@getcoldstart.nl", subject,
            DateTimeOffset.UtcNow, seen, html, false, "plain body", $"<msg-{uid}@example.com>", null, atts ?? []);

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
}

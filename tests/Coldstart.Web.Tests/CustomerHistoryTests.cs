using System.Net;
using System.Text.RegularExpressions;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class CustomerHistoryTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task History_All_ShowsSentSkippedAndOlderUnreviewed()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"hist-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadAsync(email, "Sent Co", LeadStatus.Sent);
        await AddLeadAsync(email, "Shelved Co", LeadStatus.Shelved);

        // Act
        var html = await client.GetStringAsync("/app/history");

        // Assert
        html.Should().Contain("Sent Co");
        html.Should().Contain("Shelved Co");
        html.Should().NotContain("app-tabs");
    }

    [Fact]
    public async Task History_SentFilter_ExcludesSkipped()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"hist-sent-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadAsync(email, "Sent Co", LeadStatus.Sent);
        await AddLeadAsync(email, "Shelved Co", LeadStatus.Shelved);

        // Act
        var html = await client.GetStringAsync("/app/history?filter=sent");

        // Assert
        html.Should().Contain("Sent Co");
        html.Should().NotContain("Shelved Co");
    }

    [Fact]
    public async Task History_Search_NarrowsByCompany()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"hist-q-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadAsync(email, "Patronus AI", LeadStatus.Sent);
        await AddLeadAsync(email, "Globex", LeadStatus.Sent);

        // Act
        var html = await client.GetStringAsync("/app/history?q=patronus");

        // Assert
        html.Should().Contain("Patronus AI");
        html.Should().NotContain("Globex");
    }

    [Fact]
    public async Task Reopen_MovesOlderLeadBackToTheQueue()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"hist-reopen-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadAsync(email, "Reopen Co", LeadStatus.Shelved);
        var id = await LeadIdAsync(email, "Reopen Co");
        var form = await AntiforgeryForm(client, "/app/history", new() { ["id"] = id.ToString() });

        // Act
        var post = await client.PostAsync("/app/history?handler=Reopen", new FormUrlEncodedContent(form));
        var briefing = await client.GetStringAsync("/app");

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        briefing.Should().Contain("Reopen Co");   // back in the queue
    }

    [Fact]
    public async Task History_OlderFilter_ShowsOnlyUnfinishedLeadsBeyondTheQueueCap()
    {
        // Arrange — 17 unfinished (PendingReview) leads: newest 15 stay in the briefing queue,
        // the 2 oldest spill into History's "older" set.
        var client = factory.CreateClient();
        var email = $"hist-older-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        var baseTime = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 17; i++)
            await AddLeadAtAsync(email, $"Older{i:D2} Co", baseTime.AddMinutes(i)); // i=0 oldest, i=16 newest

        // Act
        var older = await client.GetStringAsync("/app/history?filter=older");
        var briefing = await client.GetStringAsync("/app");

        // Assert — the 2 oldest are in History/older and NOT in the briefing queue; a newer one is the inverse.
        older.Should().Contain("Older00 Co");
        older.Should().Contain("Older01 Co");
        older.Should().NotContain("Older16 Co");   // newest stays in the queue, not "older"
        briefing.Should().Contain("Older16 Co");
        briefing.Should().NotContain("Older00 Co"); // oldest dropped from the queue
    }

    [Fact]
    public async Task Queue_And_OlderHistory_ArePartitioned_EvenWithTiedTimestamps()
    {
        // Arrange — 16 PendingReview leads sharing the exact same CreatedAt: without a deterministic
        // secondary sort, the queue (Take 15) and History/older (Skip 15) — each a separate re-query/
        // re-order — could disagree on which lead is the 16th, letting it appear in both or neither.
        var client = factory.CreateClient();
        var email = $"hist-tie-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        var tiedAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var names = Enumerable.Range(0, 16).Select(i => $"Tie{i:D2} Co").ToList();
        foreach (var name in names)
            await AddLeadAtAsync(email, name, tiedAt);

        // Act
        var briefing = await client.GetStringAsync("/app");
        var older = await client.GetStringAsync("/app/history?filter=older");

        // Assert — exactly 15 lead rows in the briefing queue, exactly 1 in History/older,
        // and every one of the 16 names appears in exactly one of the two pages (no overlap, none missing).
        var queueRowCount = Regex.Matches(briefing, "class=\"lead-row\"").Count;
        queueRowCount.Should().Be(15);

        var inBriefing = names.Where(n => briefing.Contains(n)).ToList();
        var inOlder = names.Where(n => older.Contains(n)).ToList();

        inBriefing.Should().HaveCount(15);
        inOlder.Should().HaveCount(1);
        inBriefing.Intersect(inOlder).Should().BeEmpty();
        inBriefing.Union(inOlder).Should().BeEquivalentTo(names);
    }

    [Fact]
    public async Task GetHistory_WhenNotAuthenticated_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/app/history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    // Inserts a lead in a specific status for the given registered user's customer. Mirrors
    // LeadReviewTests.AddLeadForUserAsync; Sent needs a recipient before MarkSent, per
    // LeadDetailTests.AddApprovedLeadWithRecipientAsync.
    private async Task AddLeadAsync(string email, string company, LeadStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", "fp-" + Guid.NewGuid().ToString("N"), 0.7, "outbound",
            DateTimeOffset.UtcNow);
        if (status == LeadStatus.Approved) lead.Approve(new EmailAddress("ceo@x.com"));
        else if (status == LeadStatus.Shelved) lead.Discard();
        else if (status == LeadStatus.Rejected) lead.Reject();
        else if (status == LeadStatus.Sent) { lead.Approve(new EmailAddress("ceo@x.com")); lead.MarkSent(DateTimeOffset.UtcNow); }
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
    }

    // Seeds a PendingReview harvested lead with an explicit CreatedAt, so ordering (newest-first,
    // queue cap / "older" spillover) is deterministic. Left PendingReview — no approve/discard.
    private async Task AddLeadAtAsync(string email, string company, DateTimeOffset createdAt)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", "fp-" + Guid.NewGuid().ToString("N"), 0.7, "outbound",
            createdAt);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
    }

    // NB: inside an EF Where, `l.CustomerId.Value == guid` does not translate — compare the
    // CustomerId value object directly, and IgnoreQueryFilters() since this setup scope has no
    // signed-in request/tenant claim (the tenant query filter fails closed outside an HTTP request).
    private async Task<Guid> LeadIdAsync(string email, string company)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        return db.Leads.IgnoreQueryFilters()
            .First(l => l.CustomerId == new CustomerId(user!.CustomerId!.Value) && l.Company == company).Id.Value;
    }

    private static async Task<Dictionary<string, string>> AntiforgeryForm(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(path);
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += marker.Length;
            var end = html.IndexOf('"', start);
            fields["__RequestVerificationToken"] = html[start..end];
        }
        return fields;
    }
}

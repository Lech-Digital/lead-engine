using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadReviewTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Index_RendersBriefingQueue_WithoutTabs()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"rev-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        await AddLeadForUserAsync(email, "Pending Co", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Pending Co");
        html.Should().Contain("pz-lead");
        html.Should().NotContain("app-tabs");
        html.Should().NotContain("?tab=");
        html.Should().NotContain("data-panel");
    }

    [Fact]
    public async Task Index_Rail_HasPersistentHistoryLink_EvenWithNoBacklog()
    {
        // Arrange — a single lead means no "older leads" line, so the only History link
        // is the persistent rail one (how a user reaches their skipped/sent leads).
        var client = factory.CreateClient();
        var email = $"rev-hist-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        await AddLeadForUserAsync(email, "Solo Co", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("rail-item");
        html.Should().Contain("href=\"/app/history\"");
        html.Should().NotContain("older lead");   // no backlog line in this case
    }

    [Fact]
    public async Task Index_SkipControl_HasConfirmationDialog()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"rev-confirm-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        await AddLeadForUserAsync(email, "Confirm Co", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert — the shared confirm dialog exists and the skip button opts into it
        html.Should().Contain("id=\"confirm-dialog\"");
        html.Should().Contain("data-confirm");
        html.Should().Contain("data-confirm-title=\"Skip Confirm Co?\"");
    }

    [Fact]
    public async Task Index_QueueShowsNewest15Unfinished_RestGoToOlderCount()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"rev-cap-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        for (var i = 0; i < 18; i++)
            await AddLeadForUserAsync(email, $"Co{i:D2}", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert — 15 rows shown, backlog line names the remaining 3.
        // NB: match the exact class="pz-lead" (trailing quote) so pz-lead-co/-why/-subj don't inflate the count.
        System.Text.RegularExpressions.Regex.Matches(html, "class=\"pz-lead\"").Count.Should().Be(15);
        html.Should().Contain("3 older leads");
        html.Should().Contain("/app/history");
    }

    [Fact]
    public async Task Index_ApprovedNotSentLead_CountsAsUnfinished_AndShowsInQueue()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"rev-appr-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        await AddLeadForUserAsync(email, "Approved Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.Approved);

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Approved Co");
    }

    [Fact]
    public async Task Skip_MovesLeadOutOfQueue_ToSkipped()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"rev-skip-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        await AddLeadForUserAsync(email, "Move Co", "fp-" + Guid.NewGuid().ToString("N"));
        var id = await LeadIdForAsync(email, "Move Co");
        var form = await AntiforgeryForm(client, "/app", new() { ["id"] = id.ToString() });

        // Act
        var post = await client.PostAsync("/app?handler=Skip", new FormUrlEncodedContent(form));
        var briefing = await client.GetStringAsync("/app");

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        briefing.Should().NotContain("Move Co");   // gone from the queue
    }

    private async Task<Guid> LeadIdForAsync(string email, string company)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        return db.Leads.IgnoreQueryFilters().First(l => l.CustomerId == new CustomerId(user!.CustomerId!.Value) && l.Company == company).Id.Value;
    }

    private static async Task<Dictionary<string,string>> AntiforgeryForm(HttpClient client, string path, Dictionary<string,string> fields)
    {
        var html = await client.GetStringAsync(path);
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start >= 0) { start += marker.Length; var end = html.IndexOf('"', start); fields["__RequestVerificationToken"] = html[start..end]; }
        return fields;
    }

    // Inserts a lead in a specific status for the given registered user's customer.
    private async Task AddLeadForUserAsync(string email, string company, string fingerprint, LeadStatus status = LeadStatus.PendingReview)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        if (status == LeadStatus.Approved) lead.Approve(new EmailAddress("ceo@x.com"));
        else if (status == LeadStatus.Shelved) lead.Discard();
        else if (status == LeadStatus.Rejected) lead.Reject();
        else if (status == LeadStatus.Sent) { lead.Approve(new EmailAddress("ceo@x.com")); lead.MarkSent(DateTimeOffset.UtcNow); }
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
    }
}

using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadReviewTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Index_DefaultTab_ShowsNewLeadsAndTabHeaders()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"review-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        await AddLeadForUserAsync(email, "Pending Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.PendingReview);

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Pending Co");
        html.Should().Contain("app-tabs");
        html.Should().Contain("data-tab=\"new\" href=\"/app?tab=new\">New <span class=\"app-tab-count\">1</span>");
        html.Should().Contain("data-tab=\"accepted\" href=\"/app?tab=accepted\">Accepted <span class=\"app-tab-count\">0</span>");
        html.Should().Contain("data-tab=\"discarded\" href=\"/app?tab=discarded\">Discarded <span class=\"app-tab-count\">0</span>");
    }

    [Fact]
    public async Task Index_DiscardedTab_ShowsShelvedAndAutoSkippedGroups()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"review2-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        await AddLeadForUserAsync(email, "Shelved Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.Shelved);
        await AddLeadForUserAsync(email, "Autoskip Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.Rejected);

        // Act
        var html = await client.GetStringAsync("/app?tab=discarded");

        // Assert
        html.Should().Contain("Shelved Co");
        html.Should().Contain("Autoskip Co");
        html.Should().Contain("Auto-skipped");
        // Tab-count spans come straight from the New/Accepted/Discarded partition logic, so these
        // prove both leads were routed into the Discarded buckets (Shelved + Rejected), not New/Accepted.
        html.Should().Contain("data-tab=\"discarded\" href=\"/app?tab=discarded\">Discarded <span class=\"app-tab-count\">2</span>");
        html.Should().Contain("data-tab=\"new\" href=\"/app?tab=new\">New <span class=\"app-tab-count\">0</span>");
    }

    [Fact]
    public async Task Index_RoutesAcceptedAndSentToAcceptedTab_WithDragFlags()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"review3-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        await AddLeadForUserAsync(email, "Ready Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.Approved);
        await AddLeadForUserAsync(email, "Gone Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.Sent);

        // Act
        var html = await client.GetStringAsync("/app?tab=accepted");

        // Assert
        // Tab-count spans come straight from the New/Accepted/Discarded partition logic, so these
        // prove both leads were routed into the Accepted bucket (Approved + Sent), not New/Discarded.
        html.Should().Contain("data-tab=\"accepted\" href=\"/app?tab=accepted\">Accepted <span class=\"app-tab-count\">2</span>");
        html.Should().Contain("data-tab=\"new\" href=\"/app?tab=new\">New <span class=\"app-tab-count\">0</span>");
        html.Should().Contain("data-tab=\"discarded\" href=\"/app?tab=discarded\">Discarded <span class=\"app-tab-count\">0</span>");
        html.Should().Contain("Ready Co");
        html.Should().Contain("Gone Co");
        // Only Sent cards are non-draggable; Approved (Ready) cards are draggable.
        html.Should().Contain("draggable=\"false\"");
        html.Should().Contain("draggable=\"true\"");
    }

    [Fact]
    public async Task Move_ToDiscarded_ShelvesLeadAndReturnsOk()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"move-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        var id = await AddLeadIdForUserAsync(email, "Move Co", "fp-" + Guid.NewGuid().ToString("N"));
        var token = await AntiforgeryToken(client, "/app");

        // Act
        var req = new HttpRequestMessage(HttpMethod.Post, "/app?handler=Move");
        req.Headers.Add("RequestVerificationToken", token);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = id.ToString(), ["tab"] = "discarded" });
        var resp = await client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("\"ok\":true");
        var discarded = await client.GetStringAsync("/app?tab=discarded");
        discarded.Should().Contain("Move Co");
        // Tab-count spans come straight from the New/Accepted/Discarded partition logic, so these
        // prove the lead actually moved into Discarded (not just that "Move Co" appears somewhere
        // on the page, since Index.cshtml renders all three panels on every request).
        discarded.Should().Contain("data-tab=\"discarded\" href=\"/app?tab=discarded\">Discarded <span class=\"app-tab-count\">1</span>");
        discarded.Should().Contain("data-tab=\"new\" href=\"/app?tab=new\">New <span class=\"app-tab-count\">0</span>");
    }

    [Fact]
    public async Task Move_ToAccepted_ApprovesLeadAndReturnsOk()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"move2-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        var id = await AddLeadIdForUserAsync(email, "Accept Co", "fp-" + Guid.NewGuid().ToString("N"));
        var token = await AntiforgeryToken(client, "/app");

        // Act
        var req = new HttpRequestMessage(HttpMethod.Post, "/app?handler=Move");
        req.Headers.Add("RequestVerificationToken", token);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = id.ToString(), ["tab"] = "accepted" });
        var resp = await client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("\"ok\":true");
        var accepted = await client.GetStringAsync("/app?tab=accepted");
        accepted.Should().Contain("Accept Co");
        // Tab-count spans prove the lead actually moved into Accepted (not just that "Accept Co"
        // appears somewhere on the page, since Index.cshtml renders all three panels on every request).
        accepted.Should().Contain("data-tab=\"accepted\" href=\"/app?tab=accepted\">Accepted <span class=\"app-tab-count\">1</span>");
        accepted.Should().Contain("data-tab=\"new\" href=\"/app?tab=new\">New <span class=\"app-tab-count\">0</span>");
    }

    // Returns the created lead's id (PendingReview).
    private async Task<Guid> AddLeadIdForUserAsync(string email, string company, string fingerprint)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private static async Task<string> AntiforgeryToken(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf('"', start);
        return html[start..end];
    }

    // Inserts a lead in a specific status for the given registered user's customer.
    private async Task AddLeadForUserAsync(string email, string company, string fingerprint, LeadStatus status)
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

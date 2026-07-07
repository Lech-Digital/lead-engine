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

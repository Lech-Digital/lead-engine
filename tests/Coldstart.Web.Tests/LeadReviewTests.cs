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

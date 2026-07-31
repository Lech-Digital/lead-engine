using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadOutcomeWebTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private async Task<Guid> AddLeadAsync(string email, string company, LeadStatus status, DateTimeOffset? sentAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", "fp-" + Guid.NewGuid().ToString("N"), 0.7, "outbound",
            DateTimeOffset.UtcNow);
        if (status == LeadStatus.Sent)
        {
            lead.Approve(new EmailAddress("ceo@x.com"));
            lead.MarkSent(sentAt ?? DateTimeOffset.UtcNow);
        }
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    [Fact]
    public async Task Outcome_PostedOnSentLead_ShowsBadgeInHistory()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"outcome-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var leadId = await AddLeadAsync(email, "Winner BV", LeadStatus.Sent, DateTimeOffset.UtcNow.AddDays(-6));

        // Act
        var post = await TestAuth.PostFormAsync(client, $"/app/leads/{leadId}?handler=Outcome",
            new() { ["outcome"] = "Won" });
        var history = await client.GetStringAsync("/app/history?filter=sent");

        // Assert
        ((int)post.StatusCode).Should().BeInRange(200, 399);
        history.Should().Contain("WON");
    }

    [Fact]
    public async Task Detail_QuietSentLead_OffersFollowUpBump()
    {
        // Arrange — sent 6 days ago, no outcome
        var client = factory.CreateClient();
        var email = $"bump-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var leadId = await AddLeadAsync(email, "Quiet BV", LeadStatus.Sent, DateTimeOffset.UtcNow.AddDays(-6));

        // Act
        var html = await client.GetStringAsync($"/app/leads/{leadId}");

        // Assert — the bump panel with a threaded reply subject
        html.Should().Contain("bump it");
        html.Should().Contain("mail.google.com");
        html.Should().Contain(Uri.EscapeDataString("Re: subj"));
    }

    [Fact]
    public async Task Detail_FreshSentLead_ShowsNoBump()
    {
        // Arrange — sent yesterday: too early to nag
        var client = factory.CreateClient();
        var email = $"fresh-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var leadId = await AddLeadAsync(email, "Fresh BV", LeadStatus.Sent, DateTimeOffset.UtcNow.AddDays(-1));

        // Act
        var html = await client.GetStringAsync($"/app/leads/{leadId}");

        // Assert
        html.Should().NotContain("bump it");
    }

    [Fact]
    public async Task Discard_WithReason_ShowsReasonInHistory()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"skipwhy-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var leadId = await AddLeadAsync(email, "Giant Corp", LeadStatus.PendingReview);

        // Act
        await TestAuth.PostFormAsync(client, $"/app/leads/{leadId}?handler=Discard",
            new() { ["skipReason"] = "Too big" });
        var history = await client.GetStringAsync("/app/history?filter=skipped");

        // Assert
        history.Should().Contain("Too big");
    }
}

using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadFoilTests(FoilWebFactory factory) : IClassFixture<FoilWebFactory>
{
    private async Task AddLeadsAsync(string email, int count)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var cid = new CustomerId((await users.FindByEmailAsync(email))!.CustomerId!.Value);
        for (var i = 0; i < count; i++)
            db.Leads.Add(Lead.CreateHarvested(cid, $"Co{i}", Fit.Yes, $"subj{i}", "body",
                "hackernews", "https://x/1", $"fp-{Guid.NewGuid():N}", 0.9 - i * 0.01, "outbound", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Inbox_ForFreeCustomer_ShowsBlurredRows_AndUnlockCta()
    {
        // Arrange — 5 leads, quota 2 → 3 locked
        var client = factory.CreateClient();
        var email = $"foil-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadsAsync(email, 5);

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Unlock all");
        html.Should().Contain("more");            // "N more … behind your plan"
        html.Should().Contain("lead-row locked"); // at least one blurred row inline
    }

    [Fact]
    public async Task LockedLeadDetail_RedirectsToApp()
    {
        // Arrange — find a locked lead's id (lowest-score of the 5)
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"foil-detail-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadsAsync(email, 5);
        Guid lockedId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
            var cid = (await users.FindByEmailAsync(email))!.CustomerId!.Value;
            // lowest score = last after the ranking = locked
            lockedId = db.Leads.IgnoreQueryFilters().Where(l => l.CustomerId == new CustomerId(cid))
                .OrderBy(l => l.Score).First().Id.Value;
        }

        // Act
        var response = await client.GetAsync($"/app/leads/{lockedId}");

        // Assert — the locked lead is not viewable
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/app");
    }

    [Fact]
    public async Task Inbox_WhileHarvestPending_ShowsFindingState()
    {
        // Arrange — a customer whose harvest is Pending with zero leads
        var client = factory.CreateClient();
        var email = $"pending-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
            var cid = (await users.FindByEmailAsync(email))!.CustomerId!.Value;
            var customer = db.Customers.IgnoreQueryFilters().First(c => c.Id == new CustomerId(cid));
            customer.MarkHarvest(HarvestStatus.Pending, null);
            await db.SaveChangesAsync();
        }

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Finding your first leads");
    }
}

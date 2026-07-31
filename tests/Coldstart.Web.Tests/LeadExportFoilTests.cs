using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

// Export under a REAL free quota (FoilWebFactory: 2) — the paywall must hold in the CSV too.
public class LeadExportFoilTests(FoilWebFactory factory) : IClassFixture<FoilWebFactory>
{
    [Fact]
    public async Task Export_OnFreePlan_ExcludesLockedLeads()
    {
        // Arrange — 5 leads, quota 2: the top two by fit/score are unlocked, three are foil
        var client = factory.CreateClient();
        var email = $"export-foil-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        for (var i = 0; i < 5; i++) await AddLeadAsync(email, $"Foil Co {i}", 0.9 - i * 0.1);

        // Act
        var response = await client.GetAsync("/app/history?handler=Export");
        var body = await response.Content.ReadAsStringAsync();

        // Assert — exactly the two unlocked leads leave the building
        response.EnsureSuccessStatusCode();
        var dataLines = body.TrimEnd().Split('\n').Length - 1;
        dataLines.Should().Be(2);
        body.Should().Contain("Foil Co 0").And.Contain("Foil Co 1");
        body.Should().NotContain("Foil Co 2").And.NotContain("Foil Co 4");
    }

    private async Task AddLeadAsync(string email, string company, double score)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", "fp-" + Guid.NewGuid().ToString("N"), score, "outbound",
            DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
    }
}

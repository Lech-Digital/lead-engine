using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class CustomerZoneTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task App_ShowsRealLeadsForTheLoggedInUsersCustomer()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!"); // signs in; provisions a customer (no leads now)
        await AddLeadForUserAsync(email, "Patronus AI", "fp-zone-1");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Patronus AI");
        html.Should().Contain(email); // customer named from the user's email
        html.Should().Contain("A thought after the $50M"); // subject renders
        html.Should().Contain("app-lead"); // card markup, not the old plain table
    }

    [Fact]
    public async Task App_ShowsAnEmptyState_WhenTheCustomerHasNoLeadsYet()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-empty-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!"); // provisions a customer with no leads

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("No leads yet");
    }

    [Fact]
    public async Task App_IsScopedToTheLoggedInUsersOwnCustomer_NotAnotherUsers()
    {
        // Arrange
        var clientA = factory.CreateClient();
        var emailA = $"zone-a-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(clientA, emailA, "Passw0rd!");
        await AddLeadForUserAsync(emailA, "Acme A", "fp-zone-a");

        var clientB = factory.CreateClient();
        var emailB = $"zone-b-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(clientB, emailB, "Passw0rd!");
        await AddLeadForUserAsync(emailB, "Acme B", "fp-zone-b");

        // Act
        var htmlA = await clientA.GetStringAsync("/app");
        var htmlB = await clientB.GetStringAsync("/app");

        // Assert
        htmlA.Should().Contain(emailA);
        htmlA.Should().Contain("Acme A");
        htmlA.Should().NotContain(emailB);
        htmlA.Should().NotContain("Acme B");
        htmlB.Should().Contain(emailB);
        htmlB.Should().Contain("Acme B");
        htmlB.Should().NotContain(emailA);
        htmlB.Should().NotContain("Acme A");
    }

    // Inserts a real harvested lead into the given registered user's customer via a service scope.
    private async Task AddLeadForUserAsync(string email, string company, string fingerprint)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "A thought after the $50M", "Saw your raise…", "funding", "https://x/1", fingerprint, 0.7, "raised",
            DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
    }
}

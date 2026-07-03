using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class CustomerZoneTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task App_ShowsLoggedInUsersOwnCustomerLeads()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!"); // also signs in

        // Act
        var response = await client.GetAsync("/app");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        html.Should().Contain("Patronus AI"); // seeded mock lead
        html.Should().Contain(email); // customer named from the user's email
    }

    [Fact]
    public async Task App_IsScopedToTheLoggedInUsersOwnCustomer_NotAnotherUsers()
    {
        // Arrange
        var clientA = factory.CreateClient();
        var emailA = $"zone-a-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(clientA, emailA, "Passw0rd!");

        var clientB = factory.CreateClient();
        var emailB = $"zone-b-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(clientB, emailB, "Passw0rd!");

        // Act
        var htmlA = await clientA.GetStringAsync("/app");
        var htmlB = await clientB.GetStringAsync("/app");

        // Assert
        htmlA.Should().Contain(emailA);
        htmlA.Should().NotContain(emailB);
        htmlB.Should().Contain(emailB);
        htmlB.Should().NotContain(emailA);
    }
}

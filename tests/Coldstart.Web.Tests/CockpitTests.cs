using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class CockpitTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Cockpit_AsSuperAdmin_ShowsVitalsAndCustomerTable()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"cockpit-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act
        var html = await client.GetStringAsync("/admin/cockpit");

        // Assert
        html.Should().Contain("Cockpit");
        html.Should().Contain("paying customers");
        html.Should().Contain("Engine health per customer");
    }

    [Fact]
    public async Task Cockpit_AsRegularCustomer_IsDenied()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"cockpit-user-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var response = await client.GetAsync("/admin/cockpit");

        // Assert
        ((int)response.StatusCode).Should().BeOneOf(302, 403, 404);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Engine health");
    }

    [Fact]
    public async Task Cockpit_WhenSignedOut_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/admin/cockpit");

        // Assert
        ((int)response.StatusCode).Should().BeOneOf(302, 401);
    }
}

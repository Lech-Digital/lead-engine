using System.Net;
using Coldstart.Web;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class AuthFlowTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task GetApp_WhenNotAuthenticated_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/app");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Register_IsDisabled_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/Identity/Account/Register");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Register_Post_IsDisabled_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>()));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Login_AsSuperAdmin_RedirectsToAdmin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"sa-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!", ColdstartRoles.SuperAdmin, provisionTenant: false);

        // Act
        var response = await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/admin");
    }

    [Fact]
    public async Task Login_WithoutGoogleConfigured_ShowsEmailPasswordOnly()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/Identity/Account/Login");

        // Assert
        html.Should().Contain("Password");
        html.Should().NotContain("Google");
    }
}

using System.Net;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Coldstart.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Register_ProvisionsCustomerWithSeededLeads()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.com";

        // Act
        await RegisterAsync(client, email, "Passw0rd!");

        // Assert
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        user!.CustomerId.Should().NotBeNull();
        (await db.Leads.CountAsync(l => l.CustomerId == new CustomerId(user.CustomerId!.Value))).Should().BeGreaterThan(0);
    }

    // GETs the register page, extracts the antiforgery token, and posts the registration form.
    private static async Task RegisterAsync(HttpClient client, string email, string password)
    {
        var html = await client.GetStringAsync("/Identity/Account/Register");
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        };
        if (start >= 0)
        {
            start += marker.Length;
            var end = html.IndexOf('"', start);
            fields["__RequestVerificationToken"] = html[start..end];
        }

        var response = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(fields));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

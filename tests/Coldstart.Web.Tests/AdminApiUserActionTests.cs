using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Web.Tests;

public sealed class AdminApiUserActionTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    [Fact]
    public async Task Invite_WithValidBody_ShouldCreateTenantSendEmailAndEnqueueHarvest()
    {
        // Arrange
        using var client = Client();
        var before = factory.HarvestQueue.Jobs.Count;

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-invite@example.com", company = "Api Invite BV", niche = "HR SaaS" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("customerId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("setPasswordLink").GetString().Should().Contain("ResetPassword");
        factory.Email.Sent.Should().Contain(m => m.To == "api-invite@example.com");
        factory.HarvestQueue.Jobs.Count.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Invite_WhenEmailAlreadyExists_ShouldReturn409()
    {
        // Arrange
        using var client = Client();
        await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-dup@example.com", company = "Dup BV", niche = "n" });

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-dup@example.com", company = "Dup BV", niche = "n" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invite_WithMissingCompany_ShouldReturn400()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-invalid@example.com", niche = "n" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Block_ExistingUser_ShouldLockOut()
    {
        // Arrange
        await TestAuth.CreateUserAsync(factory, "api-block@example.com", "Passw0rd!x", role: "CompanyAdmin");
        using var client = Client();

        // Act
        var response = await client.PostAsync("/api/admin/users/api-block@example.com/block", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("api-block@example.com");
        user!.LockoutEnd.Should().NotBeNull();
        user.LockoutEnd!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddYears(100));
    }

    [Fact]
    public async Task Block_SuperAdmin_ShouldReturn403()
    {
        // Arrange — the seeded SuperAdmin must be untouchable via the API.
        using var client = Client();

        // Act
        var response = await client.PostAsync($"/api/admin/users/{ColdstartRoles.SuperAdminEmail}/block", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Remove_UnknownUser_ShouldReturn404()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.DeleteAsync("/api/admin/users/nobody@example.com");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoke_PendingInvite_ShouldDeleteUserAndTenant()
    {
        // Arrange
        using var client = Client();
        await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-revoke@example.com", company = "Revoke BV", niche = "n" });

        // Act
        var response = await client.DeleteAsync("/api/admin/invites/api-revoke@example.com");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var customers = await client.GetFromJsonAsync<JsonElement>("/api/admin/customers");
        customers.EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .Should().NotContain("Revoke BV");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public sealed class AdminApiComplimentaryTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    private async Task<Guid> InviteAsync(HttpClient client, string email, string company)
    {
        var invite = await client.PostAsJsonAsync("/api/admin/invites", new { email, company, niche = "n" });
        return (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();
    }

    // Invited users start unconfirmed; the grant email only goes to a confirmed address.
    private async Task ConfirmAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        user!.EmailConfirmed = true;
        await users.UpdateAsync(user);
    }

    [Fact]
    public async Task PostComplimentary_ConfirmedCustomer_GrantsAndSendsEmail()
    {
        // Arrange
        using var client = Client();
        var email = "comp-happy@example.com";
        var customerId = await InviteAsync(client, email, "Comp Happy BV");
        await ConfirmAsync(email);
        var before = factory.Email.Sent.Count;

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("emailSent").GetBoolean().Should().BeTrue();
        body.GetProperty("until").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddDays(4));
        factory.Email.Sent.Skip(before).Should().Contain(m => m.To == email && m.Subject.Contains("full plan"));
    }

    [Fact]
    public async Task PostComplimentary_UnconfirmedCustomer_StillGrantsButReportsNoEmail()
    {
        // Arrange — invited, never set a password, so no confirmed address to mail
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-pending@example.com", "Comp Pending BV");

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });

        // Assert — the grant stands; only the notification is missing
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("emailSent").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PostComplimentary_UnknownCustomer_Returns404()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{Guid.NewGuid()}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostComplimentary_ZeroDays_Returns400()
    {
        // Arrange
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-zero@example.com", "Comp Zero BV");

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 0 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostComplimentary_WithoutAdminKey_IsRejected()
    {
        // Arrange — no X-Admin-Key header
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{Guid.NewGuid()}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

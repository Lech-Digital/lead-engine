using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public sealed class AdminApiCancelComplimentaryTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
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

    private async Task<Coldstart.Domain.Customer> ReloadCustomerAsync(Guid customerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var all = await db.Customers.IgnoreQueryFilters().ToListAsync();
        return all.Single(c => c.Id.Value == customerId);
    }

    [Fact]
    public async Task DeleteComplimentary_ActiveGrant_CancelsAndNeverEmails()
    {
        // Arrange
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-cancel@example.com", "Comp Cancel BV");
        await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });
        var before = factory.Email.Sent.Count;

        // Act
        var response = await client.DeleteAsync($"/api/admin/customers/{customerId}/complimentary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var customer = await ReloadCustomerAsync(customerId);
        customer.Plan.Should().Be(Coldstart.Domain.CustomerPlan.Free);
        customer.ComplimentaryUntil.Should().BeNull();
        factory.Email.Sent.Count.Should().Be(before);
    }

    [Fact]
    public async Task DeleteComplimentary_UnknownCustomer_Returns404()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.DeleteAsync($"/api/admin/customers/{Guid.NewGuid()}/complimentary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteComplimentary_NoActiveGrant_Returns400()
    {
        // Arrange — invited but never granted complimentary access
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-never@example.com", "Comp Never BV");

        // Act
        var response = await client.DeleteAsync($"/api/admin/customers/{customerId}/complimentary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no active complimentary grant");
    }

    [Fact]
    public async Task DeleteComplimentary_WithoutAdminKey_IsRejected()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.DeleteAsync($"/api/admin/customers/{Guid.NewGuid()}/complimentary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

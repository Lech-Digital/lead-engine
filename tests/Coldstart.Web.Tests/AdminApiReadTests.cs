using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Coldstart.Web.Tests;

public sealed class AdminApiReadTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    [Fact]
    public async Task Customers_WithProvisionedTenant_ShouldListItWithItsUser()
    {
        // Arrange
        await TestAuth.CreateUserAsync(factory, "read-tenant@example.com", "Passw0rd!x", role: "CompanyAdmin");
        using var client = Client();

        // Act
        var response = await client.GetAsync("/api/admin/customers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var match = doc.RootElement.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("name").GetString() == "read-tenant@example.com");
        match.ValueKind.Should().Be(JsonValueKind.Object);
        match.GetProperty("plan").GetString().Should().Be("Free");
        match.GetProperty("leadsTotal").GetInt32().Should().Be(0);
        match.GetProperty("users").EnumerateArray()
            .Select(u => u.GetProperty("email").GetString())
            .Should().Contain("read-tenant@example.com");
    }

    [Fact]
    public async Task Customers_WithoutKey_ShouldReturn401()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/admin/customers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Stats_WithHealthyDb_ShouldReportHealthyAndCounts()
    {
        // Arrange
        await TestAuth.CreateUserAsync(factory, "stats-tenant@example.com", "Passw0rd!x", role: "CompanyAdmin");
        using var client = Client();

        // Act
        var stats = await client.GetFromJsonAsync<JsonElement>("/api/admin/stats");

        // Assert
        stats.GetProperty("database").GetString().Should().Be("Healthy");
        stats.GetProperty("customers").GetInt32().Should().BeGreaterThan(0);
        stats.GetProperty("users").GetInt32().Should().BeGreaterThan(0);
    }
}

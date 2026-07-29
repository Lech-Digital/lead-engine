using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Coldstart.Web.Tests;

public sealed class AdminApiAuthTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Ping_WithCorrectKey_ShouldReturn200()
    {
        // Arrange
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);

        // Act
        var response = await client.GetAsync("/api/admin/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ok");
    }

    [Fact]
    public async Task Ping_WithoutKeyHeader_ShouldReturn401()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/admin/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ping_WithWrongKey_ShouldReturn401()
    {
        // Arrange
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "not-the-key");

        // Act
        var response = await client.GetAsync("/api/admin/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ping_WhenNoKeyConfigured_ShouldReturn404()
    {
        // Arrange — fail closed: an unset key means the whole API is off, not open.
        using var noKeyFactory = factory.WithWebHostBuilder(b => b.UseSetting("AdminApi:Key", ""));
        using var client = noKeyFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);

        // Act
        var response = await client.GetAsync("/api/admin/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

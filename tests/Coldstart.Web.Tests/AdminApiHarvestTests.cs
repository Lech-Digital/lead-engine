using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Coldstart.Web.Tests;

public sealed class AdminApiHarvestTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    [Fact]
    public async Task HarvestCustomer_ExistingCustomer_ShouldEnqueueJobAndReturn202()
    {
        // Arrange
        using var client = Client();
        var invite = await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-harvest@example.com", company = "Harvest BV", niche = "n" });
        var customerId = (await invite.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("customerId").GetGuid();
        var before = factory.HarvestQueue.Jobs.Count;

        // Act
        var response = await client.PostAsync($"/api/admin/harvest/{customerId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.HarvestQueue.Jobs.Skip(before).Should().Contain(j => j.CustomerId == customerId);
    }

    [Fact]
    public async Task HarvestCustomer_UnknownCustomer_ShouldReturn404()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.PostAsync($"/api/admin/harvest/{Guid.NewGuid()}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HarvestDue_WhenCalled_ShouldReturn200WithCount()
    {
        // Arrange — factory tenants are Free-plan except the seeded Coldstart tenant,
        // which has HarvestStatus.None and no profile keywords but IS Full plan; assert
        // only that the endpoint answers with a count.
        using var client = Client();

        // Act
        var response = await client.PostAsync("/api/admin/harvest/due", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("enqueued").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }
}

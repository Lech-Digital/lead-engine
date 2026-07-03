using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadsPageTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task GetLeads_ReturnsOk_AndShowsSeededCompany()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/Leads");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        html.Should().Contain("Patronus AI");
        html.Should().Contain("anand@patronus.ai");
    }
}

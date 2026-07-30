using System.Net;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class TenantClaimTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    // A signed-in user's requests must carry a resolvable tenant. We prove it end-to-end: the
    // signed-in user can load their own briefing (200), which only works if the tenant resolves.
    // (Direct isolation is proven at the persistence layer in Task 4.)
    [Fact]
    public async Task SignedInUser_HasResolvableTenant_AndReachesTheirBriefing()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"tenant-claim-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var res = await client.GetAsync("/app");

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

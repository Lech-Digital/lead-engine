using System.Security.Claims;
using Coldstart.Infrastructure.Identity;
using Coldstart.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

// Directly exercises the tenant read path (the claim → Guid resolution the query filters depend on).
public class HttpTenantContextTests
{
    private static HttpTenantContext WithPrincipal(ClaimsPrincipal? user)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = user is null ? null : new DefaultHttpContext { User = user },
        };
        return new HttpTenantContext(accessor);
    }

    [Fact]
    public void CustomerId_WhenClaimPresent_ReturnsIt()
    {
        // Arrange
        var id = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AppUserClaimsPrincipalFactory.CustomerIdClaim, id.ToString())], "test"));
        var sut = WithPrincipal(user);

        // Act
        var result = sut.CustomerId;

        // Assert
        result.Should().Be(id);
    }

    [Fact]
    public void CustomerId_WhenNoClaim_ReturnsNull()
    {
        // Arrange — an authenticated user without the tenant claim
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "x")], "test"));
        var sut = WithPrincipal(user);

        // Act / Assert
        sut.CustomerId.Should().BeNull();
    }

    [Fact]
    public void CustomerId_WhenNoHttpContext_ReturnsNull()
    {
        // Arrange — background/no request in flight
        var sut = WithPrincipal(null);

        // Act / Assert
        sut.CustomerId.Should().BeNull();
    }

    [Fact]
    public void CustomerId_WhenClaimNotAGuid_ReturnsNull()
    {
        // Arrange — a malformed claim must not throw; fail closed to null
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AppUserClaimsPrincipalFactory.CustomerIdClaim, "not-a-guid")], "test"));
        var sut = WithPrincipal(user);

        // Act / Assert
        sut.CustomerId.Should().BeNull();
    }
}

using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class InvitedAtTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task InvitedAt_PersistsOnApplicationUser()
    {
        // Arrange
        using var _ = factory.CreateClient();
        var email = $"inv-{System.Guid.NewGuid():N}@test.com";
        var when = System.DateTimeOffset.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, InvitedAt = when };
            await users.CreateAsync(user, "Passw0rd!");
        }

        // Assert
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            user!.InvitedAt.Should().BeCloseTo(when, System.TimeSpan.FromSeconds(5));
        }
    }
}

using Coldstart.Web;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class IdentitySeedTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Startup_SeedsThreeRoles()
    {
        // Arrange
        using var _ = factory.CreateClient();   // forces the host (and startup seeding) to build
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Act / Assert
        foreach (var role in ColdstartRoles.All)
            (await roles.RoleExistsAsync(role)).Should().BeTrue($"role {role} should be seeded");
    }

    [Fact]
    public async Task Startup_SeedsSuperAdminUserInSuperAdminRole()
    {
        // Arrange
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Act
        var admin = await users.FindByEmailAsync(ColdstartRoles.SuperAdminEmail);

        // Assert
        admin.Should().NotBeNull();
        admin!.EmailConfirmed.Should().BeTrue();
        admin.CustomerId.Should().BeNull();
        (await users.IsInRoleAsync(admin, ColdstartRoles.SuperAdmin)).Should().BeTrue();
    }
}

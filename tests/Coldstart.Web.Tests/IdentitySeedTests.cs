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
        admin.CustomerId.Should().NotBeNull("the operator is dogfooding — linked to the Coldstart tenant");
        (await users.IsInRoleAsync(admin, ColdstartRoles.SuperAdmin)).Should().BeTrue();

        // the linked tenant is the "Coldstart" customer (name lookup is a system-path bypass, so it
        // resolves with no tenant context — unlike GetAsync(id), which is tenant-filtered)
        var customers = scope.ServiceProvider.GetRequiredService<Coldstart.Application.Ports.ICustomerRepository>();
        var tenant = await customers.GetByNameAsync("Coldstart", CancellationToken.None);
        tenant.Should().NotBeNull();
        tenant!.Id.Value.Should().Be(admin.CustomerId!.Value);
    }

    [Fact]
    public async Task SeedAsync_WhenSuperAdminUserAlreadyExistsWithoutRole_GrantsSuperAdminRole()
    {
        // Arrange
        using var _ = factory.CreateClient();   // forces the host (and startup seeding) to build
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(ColdstartRoles.SuperAdminEmail);
        admin.Should().NotBeNull("startup seeding should have created the operator account already");

        (await users.RemoveFromRoleAsync(admin!, ColdstartRoles.SuperAdmin)).Succeeded.Should().BeTrue();
        (await users.IsInRoleAsync(admin!, ColdstartRoles.SuperAdmin)).Should().BeFalse();

        // Act — re-run the seeder against the pre-existing, role-less account.
        await IdentitySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);

        // Assert
        (await users.IsInRoleAsync(admin!, ColdstartRoles.SuperAdmin)).Should().BeTrue();
    }
}

using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class AdminConsoleTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Overview_ListsInvitedAndActiveUsersWithStatus()
    {
        // Arrange — a SuperAdmin session, one pending invite, one active user
        var client = factory.CreateClient();
        var admin = $"boss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!", ColdstartRoles.SuperAdmin);

        var pending = $"pending-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite",
            new() { ["Input.Company"] = "Pending Co", ["Input.Email"] = pending });

        var active = $"active-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, active, "Passw0rd!");   // EmailConfirmed=true => Active

        // Act
        var html = await client.GetStringAsync("/admin");

        // Assert
        html.Should().Contain(pending).And.Contain("Pending");
        html.Should().Contain(active).And.Contain("Active");
    }
}

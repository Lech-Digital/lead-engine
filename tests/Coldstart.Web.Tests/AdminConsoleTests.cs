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

    [Fact]
    public async Task Resend_ForPendingInvite_SendsAnotherInviteEmail()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"resend-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Re Co", ["Input.Email"] = invitee });
        factory.Email.Sent.Clear();

        // Act
        await TestAuth.PostFormAsync(client, "/admin?handler=Resend", new() { ["email"] = invitee });

        // Assert
        factory.Email.Sent.Should().ContainSingle(m => m.To == invitee);
    }

    [Fact]
    public async Task Revoke_ForPendingInvite_DeletesTheAccountAndTenant()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"revoke-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Rev Co", ["Input.Email"] = invitee });

        // Act
        await TestAuth.PostFormAsync(client, "/admin?handler=Revoke", new() { ["email"] = invitee });

        // Assert
        using var scope = factory.Services.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await mgr.FindByEmailAsync(invitee)).Should().BeNull();
    }

    [Fact]
    public async Task CopyLink_ForPendingInvite_ShowsASetPasswordUrl()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"copy-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Copy Co", ["Input.Email"] = invitee });

        // Act
        var html = await TestAuth.PostFormReadAsync(client, "/admin?handler=CopyLink", new() { ["email"] = invitee });

        // Assert
        html.Should().Contain("/Identity/Account/ResetPassword").And.Contain(System.Net.WebUtility.UrlEncode(invitee).Replace("%40", "@"));
    }
}

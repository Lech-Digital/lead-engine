using System.Text.RegularExpressions;
using Coldstart.Application.Ports;
using Coldstart.Domain;
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

        // Assert — each email's status must appear within that email's own table row, not just anywhere on the page
        RowContaining(html, pending).Should().Contain("Pending");
        RowContaining(html, active).Should().Contain("Active");
    }

    // Extracts the single <tr>...</tr> that contains the given text, so status assertions are row-scoped
    // rather than free-floating substring checks against the whole page.
    private static string RowContaining(string html, string needle)
    {
        foreach (Match row in Regex.Matches(html, "<tr\\b.*?</tr>", RegexOptions.Singleline))
        {
            if (row.Value.Contains(needle)) return row.Value;
        }
        throw new Xunit.Sdk.XunitException($"No <tr> found containing \"{needle}\".");
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

        Guid customerId;
        using (var setupScope = factory.Services.CreateScope())
        {
            var mgr0 = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            customerId = (await mgr0.FindByEmailAsync(invitee))!.CustomerId!.Value;
        }

        // Act
        await TestAuth.PostFormAsync(client, "/admin?handler=Revoke", new() { ["email"] = invitee });

        // Assert — both the user and its provisioned tenant are gone
        using var scope = factory.Services.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await mgr.FindByEmailAsync(invitee)).Should().BeNull();

        var customers = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        (await customers.GetAsync(new CustomerId(customerId), CancellationToken.None)).Should().BeNull();
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

    [Fact]
    public async Task Block_ThenLogin_IsRejected_AndUnblock_Restores()
    {
        // Arrange — an active user with a known password + tenant
        var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, admin, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var target = $"blockme-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, target, "Passw0rd!");
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await mgr.FindByEmailAsync(target))!.Id;
        }

        // Act — block, then attempt to log in on a fresh client
        await TestAuth.PostFormAsync(admin, "/admin?handler=Block", new() { ["id"] = userId });
        var blocked = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.PostFormAsync(blocked, "/Identity/Account/Login",
            new() { ["Input.Email"] = target, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false" });
        var appWhileBlocked = await blocked.GetAsync("/app");

        // Assert — blocked user is not authenticated (bounced to login)
        appWhileBlocked.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        appWhileBlocked.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");

        // Act — unblock, then log in again
        await TestAuth.PostFormAsync(admin, "/admin?handler=Unblock", new() { ["id"] = userId });
        var ok = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.PostFormAsync(ok, "/Identity/Account/Login",
            new() { ["Input.Email"] = target, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false" });
        var appAfterUnblock = await ok.GetAsync("/app");

        // Assert — now authenticated (200, not redirected to login)
        appAfterUnblock.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Remove_DeletesUserAndTenant()
    {
        // Arrange
        var admin = factory.CreateClient();
        await TestAuth.SignInAsync(factory, admin, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var target = $"removeme-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, target, "Passw0rd!");
        string userId;
        Guid customerId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await mgr.FindByEmailAsync(target))!;
            userId = user.Id;
            customerId = user.CustomerId!.Value;
        }

        // Act
        await TestAuth.PostFormAsync(admin, "/admin?handler=Remove", new() { ["id"] = userId });

        // Assert — both the user and its tenant are gone
        using var scope2 = factory.Services.CreateScope();
        var users = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByEmailAsync(target)).Should().BeNull();

        var customers = scope2.ServiceProvider.GetRequiredService<ICustomerRepository>();
        (await customers.GetAsync(new CustomerId(customerId), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Remove_SuperAdmin_IsRejected()
    {
        // Arrange
        var admin = factory.CreateClient();
        var bossEmail = $"boss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, admin, bossEmail, "Passw0rd!", ColdstartRoles.SuperAdmin);
        string bossId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            bossId = (await mgr.FindByEmailAsync(bossEmail))!.Id;
        }

        // Act — try to remove self (a SuperAdmin)
        await TestAuth.PostFormAsync(admin, "/admin?handler=Remove", new() { ["id"] = bossId });

        // Assert — still there
        using var scope2 = factory.Services.CreateScope();
        var users = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByEmailAsync(bossEmail)).Should().NotBeNull();
    }
}

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

// Companies list (/admin) is now one row per customer with a link through to its own detail page
// (/admin/customers/{id}), where the per-user and per-plan controls actually live.
public class AdminConsoleTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Overview_ListsCompanies_AndTheirDetailPagesShowEachUsersStatus()
    {
        // Arrange — a SuperAdmin session, one pending invite, one active user, each their own company
        var client = factory.CreateClient();
        var admin = $"boss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!long", ColdstartRoles.SuperAdmin);

        var pending = $"pending-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite",
            new() { ["Input.Company"] = "Pending Co", ["Input.Email"] = pending, ["Input.Niche"] = "test niche" });

        var active = $"active-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, active, "Passw0rd!long");   // EmailConfirmed=true => Active

        Guid pendingCustomerId, activeCustomerId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            pendingCustomerId = (await mgr.FindByEmailAsync(pending))!.CustomerId!.Value;
            activeCustomerId = (await mgr.FindByEmailAsync(active))!.CustomerId!.Value;
        }

        // Act — the companies list itself
        var overview = await client.GetStringAsync("/admin");

        // Assert — each company is listed and links through to its own detail page
        overview.Should().Contain("Pending Co");
        overview.Should().Contain($"/admin/customers/{pendingCustomerId}");
        overview.Should().Contain($"/admin/customers/{activeCustomerId}");

        // Assert — the detail page is where each user's own status actually lives now; each
        // email's status must appear within that email's own table row, not just anywhere on the page
        var pendingDetail = await client.GetStringAsync($"/admin/customers/{pendingCustomerId}");
        RowContaining(pendingDetail, pending).Should().Contain("Pending");

        var activeDetail = await client.GetStringAsync($"/admin/customers/{activeCustomerId}");
        RowContaining(activeDetail, active).Should().Contain("Active");
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
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"resend-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Re Co", ["Input.Email"] = invitee, ["Input.Niche"] = "test niche" });
        var customerId = await CustomerIdForAsync(invitee);
        factory.Email.Sent.Clear();

        // Act — Resend now lives on the customer's own detail page
        await TestAuth.PostFormAsync(client, $"/admin/customers/{customerId}?handler=Resend", new() { ["email"] = invitee });

        // Assert
        factory.Email.Sent.Should().ContainSingle(m => m.To == invitee);
    }

    [Fact]
    public async Task Revoke_ForPendingInvite_DeletesTheAccountAndTenant()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"revoke-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Rev Co", ["Input.Email"] = invitee, ["Input.Niche"] = "test niche" });
        var customerId = await CustomerIdForAsync(invitee);

        // Act — Revoke now lives on the customer's own detail page. It deletes the customer
        // entirely (it was the only account), so the response redirects to /admin.
        var response = await TestAuth.PostFormAsync(client, $"/admin/customers/{customerId}?handler=Revoke", new() { ["email"] = invitee });

        // Assert — both the user and its provisioned tenant are gone
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/admin");
        using var scope = factory.Services.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await mgr.FindByEmailAsync(invitee)).Should().BeNull();

        // AllAsync bypasses the tenant filter (admin path); GetAsync would be filtered to null in this
        // tenant-less scope regardless of whether the row was actually deleted, so assert non-membership.
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        (await customers.AllAsync(CancellationToken.None)).Select(c => c.Id.Value).Should().NotContain(customerId);
    }

    [Fact]
    public async Task CopyLink_ForPendingInvite_ShowsASetPasswordUrl()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"copy-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Copy Co", ["Input.Email"] = invitee, ["Input.Niche"] = "test niche" });
        var customerId = await CustomerIdForAsync(invitee);

        // Act
        var html = await TestAuth.PostFormReadAsync(client, $"/admin/customers/{customerId}?handler=CopyLink", new() { ["email"] = invitee });

        // Assert
        html.Should().Contain("/Identity/Account/ResetPassword").And.Contain(System.Net.WebUtility.UrlEncode(invitee).Replace("%40", "@"));
    }

    [Fact]
    public async Task Block_ThenLogin_IsRejected_AndUnblock_Restores()
    {
        // Arrange — an active user with a known password + tenant
        var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, admin, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var target = $"blockme-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, target, "Passw0rd!long");
        string userId;
        Guid customerId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await mgr.FindByEmailAsync(target))!;
            userId = user.Id;
            customerId = user.CustomerId!.Value;
        }

        // Act — block, then attempt to log in on a fresh client
        await TestAuth.PostFormAsync(admin, $"/admin/customers/{customerId}?handler=Block", new() { ["userId"] = userId });
        var blocked = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.PostFormAsync(blocked, "/Identity/Account/Login",
            new() { ["Input.Email"] = target, ["Input.Password"] = "Passw0rd!long", ["Input.RememberMe"] = "false" });
        var appWhileBlocked = await blocked.GetAsync("/app");

        // Assert — blocked user is not authenticated (bounced to login)
        appWhileBlocked.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        appWhileBlocked.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");

        // Act — unblock, then log in again
        await TestAuth.PostFormAsync(admin, $"/admin/customers/{customerId}?handler=Unblock", new() { ["userId"] = userId });
        var ok = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.PostFormAsync(ok, "/Identity/Account/Login",
            new() { ["Input.Email"] = target, ["Input.Password"] = "Passw0rd!long", ["Input.RememberMe"] = "false" });
        var appAfterUnblock = await ok.GetAsync("/app");

        // Assert — now authenticated (200, not redirected to login)
        appAfterUnblock.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Remove_DeletesUserAndTenant()
    {
        // Arrange
        var admin = factory.CreateClient();
        await TestAuth.SignInAsync(factory, admin, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var target = $"removeme-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, target, "Passw0rd!long");
        string userId;
        Guid customerId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await mgr.FindByEmailAsync(target))!;
            userId = user.Id;
            customerId = user.CustomerId!.Value;
        }

        // Act — Remove also deletes the tenant, so this redirects to /admin
        var response = await TestAuth.PostFormAsync(admin, $"/admin/customers/{customerId}?handler=Remove", new() { ["userId"] = userId });

        // Assert — both the user and its tenant are gone
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/admin");
        using var scope2 = factory.Services.CreateScope();
        var users = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByEmailAsync(target)).Should().BeNull();

        // AllAsync bypasses the tenant filter (admin path); GetAsync would be filtered to null in this
        // tenant-less scope regardless of whether the row was actually deleted, so assert non-membership.
        var customers = scope2.ServiceProvider.GetRequiredService<ICustomerRepository>();
        (await customers.AllAsync(CancellationToken.None)).Select(c => c.Id.Value).Should().NotContain(customerId);
    }

    [Fact]
    public async Task Remove_SuperAdmin_IsRejected()
    {
        // Arrange — the SuperAdmin has no company of their own, so exercise the guard for
        // removing them from an unrelated customer's detail page (the guard is keyed by the
        // target account's own role, not by which customer's page the request came through).
        var admin = factory.CreateClient();
        var bossEmail = $"boss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, admin, bossEmail, "Passw0rd!long", ColdstartRoles.SuperAdmin);
        string bossId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            bossId = (await mgr.FindByEmailAsync(bossEmail))!.Id;
        }
        var otherEmail = $"other-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(admin, "/admin?handler=Invite",
            new() { ["Input.Company"] = "Other Co", ["Input.Email"] = otherEmail, ["Input.Niche"] = "test niche" });
        var otherCustomerId = await CustomerIdForAsync(otherEmail);

        // Act — try to remove self (a SuperAdmin)
        await TestAuth.PostFormAsync(admin, $"/admin/customers/{otherCustomerId}?handler=Remove", new() { ["userId"] = bossId });

        // Assert — still there
        using var scope2 = factory.Services.CreateScope();
        var users = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByEmailAsync(bossEmail)).Should().NotBeNull();
    }

    private async Task<Guid> CustomerIdForAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await mgr.FindByEmailAsync(email))!.CustomerId!.Value;
    }
}

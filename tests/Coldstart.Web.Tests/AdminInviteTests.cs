using System.Net;
using Coldstart.Web;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class AdminInviteTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Admin_ForNonSuperAdmin_IsDenied()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"plain-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");   // no role

        // Act
        var response = await client.GetAsync("/admin");

        // Assert — not a SuperAdmin => bounced to login/access-denied
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_CreatesCompanyAdminWithTenant_AndEmailsLink()
    {
        // Arrange
        var client = factory.CreateClient();
        var admin = $"boss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"founder-{Guid.NewGuid():N}@test.com";
        factory.Email.Sent.Clear();

        // Act
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new()
        {
            ["Input.Company"] = "Acme Co", ["Input.Email"] = invitee,
        });

        // Assert
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(invitee);
        user.Should().NotBeNull();
        user!.EmailConfirmed.Should().BeFalse();
        user.InvitedAt.Should().NotBeNull();
        user.CustomerId.Should().NotBeNull();
        (await users.IsInRoleAsync(user, ColdstartRoles.CompanyAdmin)).Should().BeTrue();
        // This scope is created outside any signed-in request, so there is no tenant claim to
        // resolve — a system-level check of provisioning state, not a tenant-scoped read.
        (await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == new CustomerId(user.CustomerId!.Value))).Should().BeTrue();
        factory.Email.Sent.Should().ContainSingle(m => m.To == invitee);
        factory.Email.Sent.Single().TextBody.Should().Contain("/Identity/Account/ResetPassword");
        factory.Email.Sent.Single().HtmlBody!.Should().Contain("/Identity/Account/ResetPassword").And.Contain("Acme Co");
    }

    [Fact]
    public async Task Invite_ForExistingEmail_DoesNotCreateSecondAccount()
    {
        // Arrange
        var client = factory.CreateClient();
        var admin = $"boss2-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!", ColdstartRoles.SuperAdmin);
        var existing = $"dup-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, existing, "Passw0rd!");   // already registered
        factory.Email.Sent.Clear();

        // Act
        var html = await TestAuth.PostFormReadAsync(client, "/admin?handler=Invite", new()
        {
            ["Input.Company"] = "Dup Co", ["Input.Email"] = existing,
        });

        // Assert — surfaced as an error; no invite email
        html.Should().Contain("already has an account");
        factory.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Resend_ForPendingInvite_SendsSecondEmailAndReportsSuccess()
    {
        // Arrange
        var client = factory.CreateClient();
        var admin = $"boss3-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"pending-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new()
        {
            ["Input.Company"] = "Resend Co", ["Input.Email"] = invitee,
        });
        factory.Email.Sent.Clear();

        // Act
        var html = await TestAuth.PostFormReadAsync(client, "/admin?handler=Resend", new()
        {
            ["email"] = invitee,
        });

        // Assert
        html.Should().Contain($"Invitation re-sent to {invitee}");
        factory.Email.Sent.Should().ContainSingle(m => m.To == invitee);
        // Regression: Resend must not leak spurious "Input" model-binding validation errors
        // (the [BindProperty] Input model is not part of this handler's own form).
        html.Should().NotContain("validation-summary-errors");
    }

    [Fact]
    public async Task Revoke_ForPendingInvite_RemovesUserAndReportsSuccess()
    {
        // Arrange
        var client = factory.CreateClient();
        var admin = $"boss4-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"revoke-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new()
        {
            ["Input.Company"] = "Revoke Co", ["Input.Email"] = invitee,
        });

        // Act
        var html = await TestAuth.PostFormReadAsync(client, "/admin?handler=Revoke", new()
        {
            ["email"] = invitee,
        });

        // Assert
        html.Should().Contain($"Invitation to {invitee} revoked");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByEmailAsync(invitee)).Should().BeNull();
        // Regression: same spurious-validation-error leak as Resend (see above).
        html.Should().NotContain("validation-summary-errors");
    }
}

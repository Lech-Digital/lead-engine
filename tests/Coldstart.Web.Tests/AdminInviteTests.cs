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
        await TestAuth.PostFormAsync(client, "/admin", new()
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
        (await db.Customers.AnyAsync(c => c.Id == new CustomerId(user.CustomerId!.Value))).Should().BeTrue();
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
        var html = await TestAuth.PostFormReadAsync(client, "/admin", new()
        {
            ["Input.Company"] = "Dup Co", ["Input.Email"] = existing,
        });

        // Assert — surfaced as an error; no invite email
        html.Should().Contain("already has an account");
        factory.Email.Sent.Should().BeEmpty();
    }
}

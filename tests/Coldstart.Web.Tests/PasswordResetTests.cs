using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Coldstart.Web;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class PasswordResetTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task ForgotPassword_ForKnownUser_EmailsAResetLink()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"reset-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!");
        factory.Email.Sent.Clear();

        // Act
        await TestAuth.PostFormAsync(client, "/Identity/Account/ForgotPassword",
            new() { ["Input.Email"] = email });

        // Assert
        factory.Email.Sent.Should().ContainSingle(m => m.To == email);
        factory.Email.Sent.Single().TextBody.Should().Contain("/Identity/Account/ResetPassword");
        factory.Email.Sent.Single().HtmlBody.Should().NotBeNull();
        factory.Email.Sent.Single().HtmlBody!.Should().Contain("/Identity/Account/ResetPassword").And.Contain("getcoldstart.nl");
    }

    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_ShowsNeutralMessageAndSendsNothing()
    {
        // Arrange
        var client = factory.CreateClient();
        factory.Email.Sent.Clear();

        // Act
        var html = await TestAuth.PostFormReadAsync(client, "/Identity/Account/ForgotPassword",
            new() { ["Input.Email"] = $"nobody-{Guid.NewGuid():N}@test.com" });

        // Assert — no enumeration: neutral confirmation, no email sent
        factory.Email.Sent.Should().BeEmpty();
        html.Should().Contain("we've sent");
    }

    [Fact]
    public async Task ResetPassword_ViaEmailedLink_SetsPasswordAndSignsIn()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"reset2-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "OldPassw0rd!");
        factory.Email.Sent.Clear();
        await TestAuth.PostFormAsync(client, "/Identity/Account/ForgotPassword", new() { ["Input.Email"] = email });
        var link = Regex.Match(factory.Email.Sent.Single().TextBody, @"/Identity/Account/ResetPassword\?\S+").Value;
        var code = Regex.Match(link, @"code=([^&\s]+)").Groups[1].Value;

        // Act — post a new password using the code from the link
        var response = await TestAuth.PostFormAsync(client, "/Identity/Account/ResetPassword", new()
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.Password"] = "BrandNewP4ss!",
            ["Input.ConfirmPassword"] = "BrandNewP4ss!",
        });

        // Assert — redirects to /app and the new password now authenticates
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/app");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        (await users.CheckPasswordAsync(user!, "BrandNewP4ss!")).Should().BeTrue();
        user!.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_ForSuperAdmin_RedirectsToAdmin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"reset-admin-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "OldPassw0rd!", ColdstartRoles.SuperAdmin, provisionTenant: false);
        factory.Email.Sent.Clear();
        await TestAuth.PostFormAsync(client, "/Identity/Account/ForgotPassword", new() { ["Input.Email"] = email });
        var link = Regex.Match(factory.Email.Sent.Single().TextBody, @"/Identity/Account/ResetPassword\?\S+").Value;
        var code = Regex.Match(link, @"code=([^&\s]+)").Groups[1].Value;

        // Act — post a new password using the code from the link
        var response = await TestAuth.PostFormAsync(client, "/Identity/Account/ResetPassword", new()
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.Password"] = "BrandNewP4ss!",
            ["Input.ConfirmPassword"] = "BrandNewP4ss!",
        });

        // Assert — SuperAdmins land on /admin
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/admin");
    }

    [Fact]
    public async Task CompleteInvite_SendsWelcomeEmail()
    {
        // Arrange — invited user (EmailConfirmed=false) with a valid reset code, minted the
        // same way the admin-invite handler does (CreateAsync with a password, no confirmation).
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"invite-{Guid.NewGuid():N}@test.com";
        string code;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = false };
            await users.CreateAsync(user, "TempP4ssw0rd!");
            var token = await users.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        }
        factory.Email.Sent.Clear();

        // Act — POST the ResetPassword form with a valid code + new password.
        await TestAuth.PostFormAsync(client, "/Identity/Account/ResetPassword", new()
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.Password"] = "BrandNewP4ss!",
            ["Input.ConfirmPassword"] = "BrandNewP4ss!",
        });

        // Assert
        factory.Email.Sent.Should().Contain(m => m.Subject.Contains("Welcome"));
    }

    [Fact]
    public async Task NormalPasswordReset_DoesNotSendWelcomeEmail()
    {
        // Arrange — user with EmailConfirmed=true resets their password.
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"reset3-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "OldPassw0rd!");
        factory.Email.Sent.Clear();
        await TestAuth.PostFormAsync(client, "/Identity/Account/ForgotPassword", new() { ["Input.Email"] = email });
        var link = Regex.Match(factory.Email.Sent.Single().TextBody, @"/Identity/Account/ResetPassword\?\S+").Value;
        var code = Regex.Match(link, @"code=([^&\s]+)").Groups[1].Value;
        factory.Email.Sent.Clear();

        // Act — same POST as above.
        await TestAuth.PostFormAsync(client, "/Identity/Account/ResetPassword", new()
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.Password"] = "BrandNewP4ss!",
            ["Input.ConfirmPassword"] = "BrandNewP4ss!",
        });

        // Assert
        factory.Email.Sent.Should().NotContain(m => m.Subject.Contains("Welcome"));
    }
}

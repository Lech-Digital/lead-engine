using System.Net;
using System.Text.RegularExpressions;
using Coldstart.Web;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
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
        factory.Email.Sent.Single().Body.Should().Contain("/Identity/Account/ResetPassword");
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
        var client = factory.CreateClient();
        var email = $"reset2-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "OldPassw0rd!");
        factory.Email.Sent.Clear();
        await TestAuth.PostFormAsync(client, "/Identity/Account/ForgotPassword", new() { ["Input.Email"] = email });
        var link = Regex.Match(factory.Email.Sent.Single().Body, @"/Identity/Account/ResetPassword\?\S+").Value;
        var code = Regex.Match(link, @"code=([^&\s]+)").Groups[1].Value;

        // Act — post a new password using the code from the link
        await TestAuth.PostFormAsync(client, "/Identity/Account/ResetPassword", new()
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.Password"] = "BrandNewP4ss!",
            ["Input.ConfirmPassword"] = "BrandNewP4ss!",
        });

        // Assert — the new password now authenticates
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        (await users.CheckPasswordAsync(user!, "BrandNewP4ss!")).Should().BeTrue();
        user!.EmailConfirmed.Should().BeTrue();
    }
}

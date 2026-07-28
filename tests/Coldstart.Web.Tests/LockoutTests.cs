using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LockoutTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Login_AfterFiveFailedAttempts_LocksOut_EvenWithCorrectPassword()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"lockout-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!");

        // Act — burn through the allowed attempts with a wrong password…
        for (var i = 0; i < 5; i++)
            await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
            {
                ["Input.Email"] = email, ["Input.Password"] = "Wrong-1!", ["Input.RememberMe"] = "false",
            });
        // …then try the CORRECT password.
        var html = await TestAuth.PostFormReadAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false",
        });

        // Assert — the account is locked, the right password no longer signs in
        html.Should().Contain("locked");
    }

    [Fact]
    public async Task Login_FewFailedAttempts_ThenCorrectPassword_StillSignsIn()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"lockout-ok-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!");

        // Act — two wrong attempts, then the right password
        for (var i = 0; i < 2; i++)
            await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
            {
                ["Input.Email"] = email, ["Input.Password"] = "Wrong-1!", ["Input.RememberMe"] = "false",
            });
        var response = await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false",
        });

        // Assert — normal sign-in (redirect away from the login page)
        ((int)response.StatusCode).Should().BeInRange(300, 399);
    }
}

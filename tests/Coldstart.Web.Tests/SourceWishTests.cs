using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class SourceWishTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Wish_FromSignedInCustomer_LandsInFounderInbox()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"wish-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var before = factory.Email.Sent.Count;

        // Act
        var response = await TestAuth.PostFormAsync(client, "/app/settings/sources?handler=Wish",
            new() { ["wish"] = "Please add Belgian job boards!" });

        // Assert — PRG redirect and the wish email carries the text and the sender's address
        ((int)response.StatusCode)!.Should().BeInRange(300, 399);
        var mail = factory.Email.Sent.Skip(before).Single(m => m.Subject.Contains("Wish"));
        mail.TextBody.Should().Contain("Belgian job boards").And.Contain(email);
    }

    [Fact]
    public async Task Wish_TooShort_SendsNothing()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"wish-short-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var before = factory.Email.Sent.Count;

        // Act
        await TestAuth.PostFormAsync(client, "/app/settings/sources?handler=Wish", new() { ["wish"] = "x" });

        // Assert
        factory.Email.Sent.Count.Should().Be(before);
    }

    [Fact]
    public async Task Wish_WhenSignedOut_IsRejected()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.PostAsync("/app/settings/sources?handler=Wish", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["wish"] = "anonymous spam" }));

        // Assert — unauthenticated posts bounce at the door (login redirect or antiforgery 400)
        ((int)response.StatusCode).Should().BeOneOf(302, 400, 401);
    }
}

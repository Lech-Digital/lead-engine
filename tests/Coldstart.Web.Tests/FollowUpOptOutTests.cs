using Coldstart.Application.FollowUps;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Web.Email;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class FollowUpOptOutTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private async Task<Guid> ProvisionCustomerAsync(string email)
    {
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!long");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        return user.CustomerId!.Value;
    }

    private string MintToken(Guid customerId) => factory.Services
        .GetRequiredService<IDataProtectionProvider>()
        .CreateProtector(EmailTrialFollowUpSender.OptOutPurpose)
        .Protect(customerId.ToString());

    private async Task<Customer> CustomerAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        return await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == new CustomerId(id));
    }

    [Fact]
    public async Task OptOut_WithValidToken_SetsFlagAndConfirms()
    {
        // Arrange
        var customerId = await ProvisionCustomerAsync($"optout-{Guid.NewGuid():N}@test.com");
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/followups/optout?token={Uri.EscapeDataString(MintToken(customerId))}");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        html.Should().Contain("no more reminders");
        (await CustomerAsync(customerId)).FollowUpsOptOut.Should().BeTrue();
    }

    [Fact]
    public async Task OptOut_WithTamperedToken_ChangesNothing()
    {
        // Arrange
        var customerId = await ProvisionCustomerAsync($"optout-bad-{Guid.NewGuid():N}@test.com");
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/followups/optout?token=not-a-real-token");
        var html = await response.Content.ReadAsStringAsync();

        // Assert — friendly fallback, no state change
        response.EnsureSuccessStatusCode();
        html.Should().Contain("didn't work");
        (await CustomerAsync(customerId)).FollowUpsOptOut.Should().BeFalse();
    }

    [Fact]
    public async Task OptOut_WithoutToken_RendersFallbackWithoutError()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/followups/optout");

        // Assert
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync()).Should().Contain("didn't work");
    }

    [Fact]
    public async Task EmailSender_ForConfirmedUser_SendsStageEmailWithOptOutLink()
    {
        // Arrange
        var email = $"followup-send-{Guid.NewGuid():N}@test.com";
        var customerId = await ProvisionCustomerAsync(email);
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ITrialFollowUpSender>();
        var before = factory.Email.Sent.Count;

        // Act
        var sent = await sender.SendAsync(
            new TrialFollowUp(customerId, TrialFollowUpStage.Day2, "Acme", LockedLeadCount: 4), CancellationToken.None);

        // Assert
        sent.Should().BeTrue();
        var mail = factory.Email.Sent.Skip(before).Single(m => m.To == email);
        mail.Subject.Should().Contain("4 more leads");
        mail.TextBody.Should().Contain("/followups/optout?token=");
    }

    [Fact]
    public async Task EmailSender_WhenNoConfirmedUser_ReturnsFalseAndSendsNothing()
    {
        // Arrange — a customer id no user points at
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ITrialFollowUpSender>();
        var before = factory.Email.Sent.Count;

        // Act
        var sent = await sender.SendAsync(
            new TrialFollowUp(Guid.NewGuid(), TrialFollowUpStage.Day5, "Ghost BV", 0), CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        factory.Email.Sent.Count.Should().Be(before);
    }
}

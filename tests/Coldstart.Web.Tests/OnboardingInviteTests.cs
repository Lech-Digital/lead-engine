using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Coldstart.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class OnboardingInviteTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Invite_WithNicheAndInfo_StoresProfile_SendsEmail_AndEnqueuesHarvest()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"newco-{Guid.NewGuid():N}@test.com";
        var company = $"NewCo-{Guid.NewGuid():N}";
        factory.Email.Sent.Clear();
        factory.HarvestQueue.Jobs.Clear();

        // Act
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new()
        {
            ["Input.Company"] = company,
            ["Input.Email"] = invitee,
            ["Input.Niche"] = "dental clinics in the Benelux",
            ["Input.Info"] = "we sell scheduling software",
        });

        // Assert — invite email sent, harvest enqueued, profile persisted with generated search fields
        factory.Email.Sent.Should().ContainSingle(m => m.To == invitee);

        Guid customerId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            customerId = (await users.FindByEmailAsync(invitee))!.CustomerId!.Value;
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            var customer = await customers.GetByNameAsync(company, CancellationToken.None);
            customer!.Profile.Niche.Should().Be("dental clinics in the Benelux");
            customer.Profile.TargetKeywords.Should().Be("dentist\nclinic");   // from FakeSearchProfileGenerator
            customer.HarvestStatus.Should().Be(HarvestStatus.Pending);
        }
        factory.HarvestQueue.Jobs.Should().ContainSingle(j => j.CustomerId == customerId);
    }

    [Fact]
    public async Task Invite_WithoutNiche_IsRejected()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        var invitee = $"noniche-{Guid.NewGuid():N}@test.com";
        factory.Email.Sent.Clear();

        // Act — omit Input.Niche (required)
        var html = await TestAuth.PostFormReadAsync(client, "/admin?handler=Invite", new()
        {
            ["Input.Company"] = "X Co", ["Input.Email"] = invitee,
        });

        // Assert — no invite sent; the form redisplays
        factory.Email.Sent.Should().NotContain(m => m.To == invitee);
    }

    [Fact]
    public async Task InviteFromInquiry_PrefillsEmailNicheAndInfo()
    {
        // Arrange — persist an inquiry, then open the invite page for it
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!long", ColdstartRoles.SuperAdmin);
        Guid inquiryId;
        var inqEmail = $"lead-{Guid.NewGuid():N}@prospect.com";
        using (var scope = factory.Services.CreateScope())
        {
            var inquiries = scope.ServiceProvider.GetRequiredService<IInquiryRepository>();
            var inq = Inquiry.Create("Jane Prospect", new EmailAddress(inqEmail), "yoga studios", "class-booking app", "please help", DateTimeOffset.UtcNow);
            await inquiries.AddAsync(inq, CancellationToken.None);
            inquiryId = inq.Id.Value;
        }

        // Act
        var html = await client.GetStringAsync($"/admin?useInquiry={inquiryId}");

        // Assert — the invite form is prefilled from the inquiry
        html.Should().Contain(inqEmail);
        html.Should().Contain("yoga studios");
        html.Should().Contain("class-booking app");
    }
}

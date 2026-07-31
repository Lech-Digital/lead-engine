using System.Net;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Coldstart.Infrastructure.Retention;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class ComplianceFeatureTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task ProcessorAgreement_IsPublic_WithArt28Essentials()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/verwerkersovereenkomst");

        // Assert
        html.Should().Contain("Processor agreement");
        html.Should().Contain("Article 28");
        html.Should().Contain("Sub-processors").And.Contain("Hetzner");
        html.Should().Contain("48 hours");
        html.Should().Contain("60 days");
    }

    [Fact]
    public async Task TermsAndPrivacy_LinkToTheProcessorAgreement()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var terms = await client.GetStringAsync("/terms");
        var privacy = await client.GetStringAsync("/privacy");

        // Assert
        terms.Should().Contain("/verwerkersovereenkomst");
        privacy.Should().Contain("/verwerkersovereenkomst");
    }

    [Fact]
    public async Task Exclusions_AsSuperAdmin_CanAddAndRemove()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"admin-excl-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long", ColdstartRoles.SuperAdmin);

        // Act — add
        var post = await TestAuth.PostFormAsync(client, "/admin/exclusions?handler=Add",
            new() { ["value"] = "Objecting Corp", ["note"] = "emailed 31 Jul" });
        var html = await client.GetStringAsync("/admin/exclusions");

        // Assert — stored normalized and listed
        ((int)post.StatusCode).Should().BeInRange(300, 399);
        html.Should().Contain("objecting corp").And.Contain("emailed 31 Jul");

        // Act — remove
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISuppressionRepository>();
        var row = (await repo.AllAsync(CancellationToken.None)).Single(s => s.Value == "objecting corp");
        await TestAuth.PostFormAsync(client, "/admin/exclusions?handler=Remove", new() { ["id"] = row.Id.ToString() });

        // Assert
        (await repo.AllAsync(CancellationToken.None)).Should().NotContain(s => s.Value == "objecting corp");
    }

    [Fact]
    public async Task Exclusions_AsRegularCustomer_IsForbidden()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"user-excl-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var response = await client.GetAsync("/admin/exclusions");

        // Assert — non-admins never see the objection list, only an Identity bounce
        ((int)response.StatusCode).Should().BeOneOf(403, 302, 404);
        if (response.StatusCode == HttpStatusCode.Redirect)
            response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/");
    }

    [Fact]
    public async Task RetentionSweep_DeletesYearOldInquiries_AndKeepsFreshOnes()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Inquiries.Add(Inquiry.Create("Old Prospect", new EmailAddress($"old-{Guid.NewGuid():N}@x.com"), "niche", "sells", null, now.AddMonths(-13), "Oldco", now.AddMonths(-13)));
        db.Inquiries.Add(Inquiry.Create("Fresh Prospect", new EmailAddress($"fresh-{Guid.NewGuid():N}@x.com"), "niche", "sells", null, now.AddDays(-5), "Freshco", now.AddDays(-5)));
        await db.SaveChangesAsync();
        var sweeper = scope.ServiceProvider.GetRequiredService<RetentionSweeper>();

        // Act
        var (deleted, _) = await sweeper.SweepAsync(now, CancellationToken.None);

        // Assert
        deleted.Should().BeGreaterThanOrEqualTo(1);
        (await db.Inquiries.AnyAsync(i => i.Name == "Old Prospect")).Should().BeFalse();
        (await db.Inquiries.AnyAsync(i => i.Name == "Fresh Prospect")).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionSweep_EmailsOperatorOnceWhenDeletionWindowNearsEnd()
    {
        // Arrange — a customer whose subscription ended 54 days ago
        var email = $"retention-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!long");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        var customer = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == new CustomerId(user!.CustomerId!.Value));
        customer.ActivateFull("sub_x", null);
        customer.DowngradeToFree(DateTimeOffset.UtcNow.AddDays(-54));
        await db.SaveChangesAsync();
        var sweeper = scope.ServiceProvider.GetRequiredService<RetentionSweeper>();
        var before = factory.Email.Sent.Count;

        // Act — twice: the reminder must not repeat
        var (_, first) = await sweeper.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var (_, second) = await sweeper.SweepAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert
        first.Should().BeGreaterThanOrEqualTo(1);
        second.Should().Be(0);
        factory.Email.Sent.Skip(before).Should().Contain(m => m.Subject.Contains("Retention") && m.Subject.Contains("delete"));
    }
}

using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class CustomerZoneTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task App_ShowsRealLeadsForTheLoggedInUsersCustomer()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!"); // signs in; provisions a customer (no leads now)
        await AddLeadForUserAsync(email, "Patronus AI", "fp-zone-1");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Patronus AI");
        html.Should().Contain(email); // customer named from the user's email
        html.Should().Contain("A thought after the $50M"); // subject renders
        html.Should().Contain("lead-row"); // compact row markup, not the old plain table
    }

    [Fact]
    public async Task App_ShowsMatchedSignals_AsWhyNowContextOnTheLeadCard()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-why-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadForUserAsync(email, "Patronus AI", "fp-zone-why-1"); // seeded with MatchedSignals "raised"

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("lead-row-why"); // compact why-now indicator (icon + signal, no label text)
        html.Should().Contain("raised");
    }

    [Fact]
    public async Task App_ShowsAnEmptyState_WhenTheCustomerHasNoLeadsYet()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-empty-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!"); // provisions a customer with no leads

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Nothing to review yet");
    }

    [Fact]
    public async Task App_ShowsBriefingHeader_WithOvernightCount()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-brief-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadForUserAsync(email, "Patronus AI", "fp-zone-brief-1");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Good morning");
        html.Should().Contain("found overnight");
    }

    [Fact]
    public async Task App_IsScopedToTheLoggedInUsersOwnCustomer_NotAnotherUsers()
    {
        // Arrange
        var clientA = factory.CreateClient();
        var emailA = $"zone-a-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, clientA, emailA, "Passw0rd!");
        await AddLeadForUserAsync(emailA, "Acme A", "fp-zone-a");

        var clientB = factory.CreateClient();
        var emailB = $"zone-b-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, clientB, emailB, "Passw0rd!");
        await AddLeadForUserAsync(emailB, "Acme B", "fp-zone-b");

        // Act
        var htmlA = await clientA.GetStringAsync("/app");
        var htmlB = await clientB.GetStringAsync("/app");

        // Assert
        htmlA.Should().Contain(emailA);
        htmlA.Should().Contain("Acme A");
        htmlA.Should().NotContain(emailB);
        htmlA.Should().NotContain("Acme B");
        htmlB.Should().Contain(emailB);
        htmlB.Should().Contain("Acme B");
        htmlB.Should().NotContain(emailA);
        htmlB.Should().NotContain("Acme A");
    }

    [Fact]
    public async Task App_ShowsEmptyProfilePrompt_ForFreshCustomer()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof-{Guid.NewGuid():N}@test.com", "Passw0rd!");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Add your company details");   // empty-profile prompt
        html.Should().Contain("app-bar");                     // dashboard shell
    }

    [Fact]
    public async Task ProfilePost_PersistsAndRendersOnZone()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof2-{Guid.NewGuid():N}@test.com", "Passw0rd!");
        var form = await AntiforgeryForm(client, "/app/profile", new()
        {
            ["Input.CompanyName"] = "Acme Ltd", ["Input.VatNumber"] = "NL999",
            ["Input.Niche"] = "B2B SaaS", ["Input.Address"] = "", ["Input.KvkNumber"] = "",
            ["Input.Website"] = "", ["Input.ContactEmail"] = "",
        });

        // Act
        var post = await client.PostAsync("/app/profile", new FormUrlEncodedContent(form));
        var zoneHtml = await client.GetStringAsync("/app");

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        zoneHtml.Should().Contain("Acme Ltd");
        zoneHtml.Should().Contain("NL999");
    }

    [Fact]
    public async Task ProfilePost_PersistsBriefAndToggle()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof-brief-{Guid.NewGuid():N}@test.com", "Passw0rd!");
        var form = await AntiforgeryForm(client, "/app/profile", new()
        {
            ["Input.CompanyName"] = "Acme Ltd", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "We help dentists fill calendars.", ["Input.MentionSourceTool"] = "true",
        });

        // Act
        await client.PostAsync("/app/profile", new FormUrlEncodedContent(form));
        var page = await client.GetStringAsync("/app/profile");

        // Assert
        page.Should().Contain("We help dentists fill calendars.");
        page.Should().Contain("id=\"Input_MentionSourceTool\"");
        page.Should().Contain("checked=\"checked\""); // the toggle persisted as checked
    }

    [Fact]
    public async Task ProfilePolish_ReturnsPolishedText_WithoutPersisting()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof-polish-{Guid.NewGuid():N}@test.com", "Passw0rd!");
        factory.BriefPolisher.Result = "POLISHED BRIEF";
        var saveForm = await AntiforgeryForm(client, "/app/profile", new()
        {
            ["Input.CompanyName"] = "", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "rough notes",
        });
        await client.PostAsync("/app/profile", new FormUrlEncodedContent(saveForm)); // persist the stored brief

        var polishForm = await AntiforgeryForm(client, "/app/profile", new()
        {
            ["Input.CompanyName"] = "", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "rough notes",
        });

        // Act
        var polish = await client.PostAsync("/app/profile?handler=Polish", new FormUrlEncodedContent(polishForm));
        var polishHtml = await polish.Content.ReadAsStringAsync();
        var freshGet = await client.GetStringAsync("/app/profile");

        // Assert
        polish.StatusCode.Should().Be(HttpStatusCode.OK);
        polishHtml.Should().Contain("POLISHED BRIEF");
        freshGet.Should().Contain("rough notes");          // stored brief unchanged
        freshGet.Should().NotContain("POLISHED BRIEF");    // polish did not persist
    }

    [Fact]
    public async Task ProfileGet_HiddenDefaultSubmit_PrecedesThePolishButton_SoEnterKeySaves()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof-enter-{Guid.NewGuid():N}@test.com", "Passw0rd!");

        // Act
        var html = await client.GetStringAsync("/app/profile");

        // Assert
        var hiddenDefaultSubmit = "<button type=\"submit\" hidden aria-hidden=\"true\" tabindex=\"-1\"></button>";
        html.Should().Contain(hiddenDefaultSubmit);
        html.Should().Contain("handler=Polish");
        html.IndexOf(hiddenDefaultSubmit, StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("handler=Polish", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfilePolish_WhenPolisherFails_ShowsFriendlyNotice_AndKeepsTypedText()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof-polish-fail-{Guid.NewGuid():N}@test.com", "Passw0rd!");
        factory.BriefPolisher.Result = null;
        var polishForm = await AntiforgeryForm(client, "/app/profile", new()
        {
            ["Input.CompanyName"] = "", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "my own words",
        });

        // Act
        var polish = await client.PostAsync("/app/profile?handler=Polish", new FormUrlEncodedContent(polishForm));
        var polishHtml = await polish.Content.ReadAsStringAsync();

        // Assert
        polish.StatusCode.Should().Be(HttpStatusCode.OK);
        polishHtml.Should().Contain("polish just now"); // the friendly degradation notice (HTML-encodes the apostrophe in "Couldn't")
        polishHtml.Should().Contain("my own words"); // unchanged
    }

    [Fact]
    public async Task GetProfile_WhenNotAuthenticated_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/app/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    // Inserts a real harvested lead into the given registered user's customer via a service scope.
    private async Task AddLeadForUserAsync(string email, string company, string fingerprint)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "A thought after the $50M", "Saw your raise…", "funding", "https://x/1", fingerprint, 0.7, "raised",
            DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
    }

    // GETs the given path, extracts the antiforgery token, and merges it into the form fields.
    private static async Task<Dictionary<string, string>> AntiforgeryForm(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(path);
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += marker.Length;
            var end = html.IndexOf('"', start);
            fields["__RequestVerificationToken"] = html[start..end];
        }
        return fields;
    }
}

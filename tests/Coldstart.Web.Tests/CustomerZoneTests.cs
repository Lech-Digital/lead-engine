using System.Net;
using Coldstart.Application.Customers;
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
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long"); // signs in; provisions a customer (no leads now)
        await AddLeadForUserAsync(email, "Patronus AI", "fp-zone-1");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Patronus AI");
        html.Should().Contain("A thought after the $50M"); // subject renders
        html.Should().Contain("lead-row"); // compact row markup, not the old plain table
    }

    [Fact]
    public async Task App_ShowsMatchedSignals_AsWhyNowContextOnTheLeadCard()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-why-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
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
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long"); // provisions a customer with no leads

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
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
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
        await TestAuth.SignInAsync(factory, clientA, emailA, "Passw0rd!long");
        await AddLeadForUserAsync(emailA, "Acme A", "fp-zone-a");

        var clientB = factory.CreateClient();
        var emailB = $"zone-b-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, clientB, emailB, "Passw0rd!long");
        await AddLeadForUserAsync(emailB, "Acme B", "fp-zone-b");

        // Act
        var htmlA = await clientA.GetStringAsync("/app");
        var htmlB = await clientB.GetStringAsync("/app");

        // Assert — the rail carries no per-customer identifier, so scoping is proven via
        // each customer's own lead data instead of the (now-removed) header email/company chip.
        htmlA.Should().Contain("Acme A");
        htmlA.Should().NotContain("Acme B");
        htmlB.Should().Contain("Acme B");
        htmlB.Should().NotContain("Acme A");
    }

    [Fact]
    public async Task App_LinksToCompanySettings_ViaRail_NotTheFullProfileCard()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"chip-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert — the rail links to Company settings; the full profile card is NOT on /app
        html.Should().Contain("href=\"/app/settings/company\"");
        html.Should().NotContain("Add your company details");   // moved to the profile page
    }

    [Fact]
    public async Task ProfilePost_PersistsAndRendersOnProfilePage()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof-{Guid.NewGuid():N}@test.com", "Passw0rd!long");
        var form = await AntiforgeryForm(client, "/app/settings/company", new()
        {
            ["Input.CompanyName"] = "Acme Ltd", ["Input.VatNumber"] = "NL999",
            ["Input.Niche"] = "B2B SaaS", ["Input.Address"] = "", ["Input.KvkNumber"] = "",
            ["Input.Website"] = "", ["Input.ContactEmail"] = "",
        });

        // Act
        await client.PostAsync("/app/settings/company", new FormUrlEncodedContent(form));
        var profile = await client.GetStringAsync("/app/settings/company");

        // Assert
        profile.Should().Contain("Acme Ltd");
        profile.Should().Contain("NL999");
    }

    [Fact]
    public async Task ProfilePost_AsCustomer_IgnoresMentionSourceToolAndHidesCheckbox()
    {
        // Arrange — the "found via Coldstart" line only makes sense when the sender IS
        // selling Coldstart (the operator). Customers must not see or set the toggle,
        // even by hand-crafting the form field.
        var client = factory.CreateClient();
        var email = $"prof-brief-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var form = await AntiforgeryForm(client, "/app/settings/outreach", new()
        {
            ["Input.CompanyName"] = "Acme Ltd", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "We help dentists fill calendars.", ["Input.MentionSourceTool"] = "true",
        });

        // Act
        await client.PostAsync("/app/settings/outreach", new FormUrlEncodedContent(form));
        var page = await client.GetStringAsync("/app/settings/outreach");

        // Assert — brief persists, the toggle is neither rendered nor persisted
        page.Should().Contain("We help dentists fill calendars.");
        page.Should().NotContain("id=\"Input_MentionSourceTool\"");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var customerId = (await users.FindByEmailAsync(email))!.CustomerId!.Value;
        var profile = await scope.ServiceProvider.GetRequiredService<IGetCompanyProfile>()
            .ForCustomerAsync(customerId, CancellationToken.None);
        profile.MentionSourceTool.Should().BeFalse();
    }

    [Fact]
    public async Task ProfilePost_AsSuperAdmin_PersistsMentionSourceTool()
    {
        // Arrange — the operator (SuperAdmin with a provisioned tenant, like the seeded
        // Coldstart account) may enable the dogfooding line for their own outreach.
        var client = factory.CreateClient();
        var email = $"prof-admin-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!long", ColdstartRoles.SuperAdmin, provisionTenant: true);
        await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = "Passw0rd!long", ["Input.RememberMe"] = "false",
        });
        var form = await AntiforgeryForm(client, "/app/settings/outreach", new()
        {
            ["Input.CompanyName"] = "Coldstart", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.MentionSourceTool"] = "true",
        });

        // Act
        await client.PostAsync("/app/settings/outreach", new FormUrlEncodedContent(form));
        var page = await client.GetStringAsync("/app/settings/outreach");

        // Assert — the toggle renders for the operator and persisted as checked
        page.Should().Contain("id=\"Input_MentionSourceTool\"");
        page.Should().Contain("checked=\"checked\"");
    }

    [Fact]
    public async Task ProfilePost_RedirectsBackToProfile_WithAnchorAndSavedConfirmation()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"prof-save-redir-{Guid.NewGuid():N}@test.com", "Passw0rd!long");
        var form = await AntiforgeryForm(client, "/app/settings/outreach", new()
        {
            ["Input.CompanyName"] = "Acme Ltd", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "we help B2B founders",
        });

        // Act
        var post = await client.PostAsync("/app/settings/outreach", new FormUrlEncodedContent(form));

        // Assert — PRG back to the profile page itself (not /app), with a fragment so the browser
        // returns to the form instead of jumping to the very top; the confirmation then shows inline.
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.OriginalString.Should().StartWith("/app/settings");
        var page = await client.GetStringAsync("/app/settings/outreach");
        page.Should().Contain("Saved");
    }

    [Fact]
    public async Task ProfilePolish_ReturnsPolishedText_WithoutPersisting()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof-polish-{Guid.NewGuid():N}@test.com", "Passw0rd!long");
        factory.BriefPolisher.Result = "POLISHED BRIEF";
        var saveForm = await AntiforgeryForm(client, "/app/settings/outreach", new()
        {
            ["Input.CompanyName"] = "", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "rough notes",
        });
        await client.PostAsync("/app/settings/outreach", new FormUrlEncodedContent(saveForm)); // persist the stored brief

        var polishForm = await AntiforgeryForm(client, "/app/settings/outreach", new()
        {
            ["Input.CompanyName"] = "", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "rough notes",
        });

        // Act
        var polish = await client.PostAsync("/app/settings/outreach?handler=Polish", new FormUrlEncodedContent(polishForm));
        var polishHtml = await polish.Content.ReadAsStringAsync();
        var freshGet = await client.GetStringAsync("/app/settings/outreach");

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
        await TestAuth.SignInAsync(factory, client, $"prof-enter-{Guid.NewGuid():N}@test.com", "Passw0rd!long");

        // Act
        var html = await client.GetStringAsync("/app/settings/outreach");

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
        await TestAuth.SignInAsync(factory, client, $"prof-polish-fail-{Guid.NewGuid():N}@test.com", "Passw0rd!long");
        factory.BriefPolisher.Result = null;
        var polishForm = await AntiforgeryForm(client, "/app/settings/outreach", new()
        {
            ["Input.CompanyName"] = "", ["Input.VatNumber"] = "", ["Input.Niche"] = "",
            ["Input.Address"] = "", ["Input.KvkNumber"] = "", ["Input.Website"] = "", ["Input.ContactEmail"] = "",
            ["Input.Brief"] = "my own words",
        });

        // Act
        var polish = await client.PostAsync("/app/settings/outreach?handler=Polish", new FormUrlEncodedContent(polishForm));
        var polishHtml = await polish.Content.ReadAsStringAsync();

        // Assert
        polish.StatusCode.Should().Be(HttpStatusCode.OK);
        polishHtml.Should().Contain("polish just now"); // the friendly degradation notice (HTML-encodes the apostrophe in "Couldn't")
        polishHtml.Should().Contain("my own words"); // unchanged
    }

    [Fact]
    public async Task Profile_UsesRailShell_NotTheNarrowAuthCard()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"prof-shell-{Guid.NewGuid():N}@test.com", "Passw0rd!long");

        // Act
        var html = await client.GetStringAsync("/app/settings/outreach");

        // Assert — full-height rail nav (like every other app page), grouped panels, no auth-card shell
        html.Should().Contain("id=\"app-rail\"");
        html.Should().Contain("panel");
        html.Should().NotContain("auth-card");
    }

    [Fact]
    public async Task GetProfile_WhenNotAuthenticated_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/app/settings");

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

    [Fact]
    public async Task SettingsPost_SaveNotifications_PersistsChannelsOnTheCustomer()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"notif-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var form = await AntiforgeryForm(client, "/app/settings/outreach", new()
        {
            ["SlackWebhookUrl"] = "https://hooks.slack.com/services/T0/B0/secret",
            ["WhatsAppNumber"] = "+31 6 1234 5678",
        });

        // Act
        await client.PostAsync("/app/settings/notifications", new FormUrlEncodedContent(form));

        // Assert — persisted (and the number normalized) on the tenant
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var customerId = (await users.FindByEmailAsync(email))!.CustomerId!.Value;
        var customers = scope.ServiceProvider.GetRequiredService<Coldstart.Application.Ports.ICustomerRepository>();
        var customer = (await customers.AllAsync(CancellationToken.None)).Single(c => c.Id.Value == customerId);
        customer.SlackWebhookUrl.Should().Be("https://hooks.slack.com/services/T0/B0/secret");
        // WhatsApp is operator-only until the paid Twilio account exists — a customer's
        // posted number is ignored.
        customer.WhatsAppNumber.Should().BeNull();
    }

    [Fact]
    public async Task Profile_OldUrl_RedirectsPermanentlyToSettings()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, client, $"redir-{Guid.NewGuid():N}@test.com", "Passw0rd!long");

        // Act
        var response = await client.GetAsync("/app/profile");

        // Assert
        ((int)response.StatusCode).Should().Be(301);
        response.Headers.Location!.ToString().Should().StartWith("/app/settings");
    }

    [Fact]
    public async Task PasswordPolicy_AcceptsLongGeneratedPasswordWithoutSpecialCharacters()
    {
        // Arrange — Chrome/Google generators emit long letter+digit passwords; the policy
        // is length-based (NIST), so these must pass.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "gen-pw@test.com", Email = "gen-pw@test.com", EmailConfirmed = true };

        // Act
        var result = await users.CreateAsync(user, "kbT7wq2mNfR4Lx");

        // Assert
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    [Fact]
    public async Task PasswordPolicy_RejectsShortPasswords()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "short-pw@test.com", Email = "short-pw@test.com" };

        // Act
        var result = await users.CreateAsync(user, "abc123");

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task SettingsBilling_ShowsPlanStatus_AndTheRightAction()
    {
        // Arrange — a fresh customer is on the Free plan
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"sub-{Guid.NewGuid():N}@test.com", "Passw0rd!long");

        // Act
        var html = await client.GetStringAsync("/app/settings/billing");

        // Assert — free status with the upgrade action posting to the shared /Billing handlers
        html.Should().Contain("Free plan");
        html.Should().Contain("Upgrade to Full");
        html.Should().Contain("handler=Checkout");
        // rail marks Subscription (not some other fixed item) as the current section
        html.Should().Contain("class=\"rail-item active\" href=\"/app/settings/billing\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/app\"");
        System.Text.RegularExpressions.Regex.Matches(html, "rail-item active").Count.Should().Be(1);
    }

    [Fact]
    public async Task Briefing_EmailChannel_SendsMorningDigestToTheAccountAddress()
    {
        // Arrange — a signed-in tenant; the email channel is always on
        var client = factory.CreateClient();
        var email = $"digest-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var before = factory.Email.Sent.Count;

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var customerId = (await users.FindByEmailAsync(email))!.CustomerId!.Value;
        var notifier = scope.ServiceProvider.GetRequiredService<Coldstart.Application.Notifications.INotifyBriefing>();
        // The customer lookup is tenant-filtered (fail closed); production callers set the
        // tenant first (harvest scope / HTTP claim) — mirror that here.
        scope.ServiceProvider.GetRequiredService<Coldstart.Application.Ports.ITenantScope>().SetCurrent(customerId);

        // Act
        var sent = await notifier.NotifyAsync(customerId,
            [new Coldstart.Application.Harvest.SavedLead(Guid.NewGuid(), "Acme", "Congrats + an idea")],
            channelFilter: "email", CancellationToken.None);

        // Assert
        sent.Should().Contain("email");
        factory.Email.Sent.Skip(before).Should().Contain(m =>
            m.To == email && m.Subject.Contains("morning briefing") && m.TextBody.Contains("Acme"));
    }

    [Fact]
    public async Task Briefing_RendersRailWithNavAndLogoutConfirm()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"rail-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("id=\"app-rail\"");
        html.Should().Contain(">Briefing<");
        html.Should().Contain(">History<");
        html.Should().Contain("data-confirm-title=\"Log out?\"");
        html.Should().NotContain("settings-nav");   // pills are gone
    }

    [Fact]
    public async Task Settings_ActiveRailItem_MarksCurrentSection()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"rail-set-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var html = await client.GetStringAsync("/app/settings/company");

        // Assert — the active class sits on the Company item specifically (its href),
        // not merely somewhere on the page, and there is exactly one active item —
        // a "same item always active" regression (e.g. Briefing stuck active) would
        // fail both the exact-pair check and the not-Briefing check below.
        html.Should().Contain("class=\"rail-item active\" href=\"/app/settings/company\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/app\"");
        System.Text.RegularExpressions.Regex.Matches(html, "rail-item active").Count.Should().Be(1);
    }

    [Fact]
    public async Task History_ActiveRailItem_MarksHistorySection_NotBriefing()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"rail-hist-active-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

        // Act
        var html = await client.GetStringAsync("/app/history");

        // Assert — same shape as the Settings/Company case above, on a different page,
        // so a bug that always marks Briefing (or any fixed item) active is caught
        // regardless of which page it happens to be tested against.
        html.Should().Contain("class=\"rail-item active\" href=\"/app/history\"");
        html.Should().NotContain("class=\"rail-item active\" href=\"/app\"");
        System.Text.RegularExpressions.Regex.Matches(html, "rail-item active").Count.Should().Be(1);
    }
}

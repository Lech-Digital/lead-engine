using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadDetailTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Detail_ForOwnedLead_RendersDraftAndContactLinks()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"detail-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddLeadForUserAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        html.Should().Contain("Acme Co");
        html.Should().Contain("linkedin.com/search");
    }

    [Fact]
    public async Task Detail_ForAnotherCustomersLead_RedirectsToApp()
    {
        // Arrange — owner creates a lead
        var owner = factory.CreateClient();
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, owner, ownerEmail, "Passw0rd!long");
        var id = await AddLeadForUserAsync(ownerEmail, "Owner Co", "fp-" + Guid.NewGuid().ToString("N"));

        // attacker is a different logged-in customer
        var attacker = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, attacker, $"attacker-{Guid.NewGuid():N}@test.com", "Passw0rd!long");

        // Act
        var response = await attacker.GetAsync($"/app/leads/{id}");

        // Assert — never leaks another tenant's lead
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/app");
    }

    [Fact]
    public async Task Detail_ForPendingLead_ShowsOwnInboxSend_WithoutSeparateApprove()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"det-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert — customer sends from their own inbox; no exposed "Send from Coldstart"
        html.Should().Contain("Send from your own inbox");
        html.Should().Contain("handoff-gmail");
        html.Should().NotContain("Send from Coldstart");
    }

    [Fact]
    public async Task Detail_DestructiveActions_RequireConfirmation()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"det-confirm-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert — the shared confirm dialog exists, and Skip + Mark-as-sent opt into it.
        html.Should().Contain("id=\"confirm-dialog\"");
        html.Should().Contain("data-confirm-title=\"Mark as sent?\"");
        html.Should().Contain("data-confirm-title=\"Skip Acme Co?\"");
    }

    [Fact]
    public async Task MarkSent_FoldsApprove_ThenLeadLandsInHistorySent()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"det-sent-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new() {
            ["Input.Subject"] = "Hi", ["Input.Draft"] = "Body", ["Input.Recipient"] = "ceo@acme.com",
        });

        // Act
        var post = await client.PostAsync($"/app/leads/{id}?handler=MarkSent", new FormUrlEncodedContent(form));
        var history = await client.GetStringAsync("/app/history?filter=sent");

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        history.Should().Contain("Acme Co");
    }

    [Fact]
    public async Task Detail_RunMode_ShowsRunBar()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"det-run-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}?run=1");

        // Assert
        html.Should().Contain("Reviewing");   // run bar present
    }

    [Fact]
    public async Task MarkSent_InRunMode_RedirectsToNextUnfinishedLead()
    {
        // Arrange — two PendingReview leads with recipients, deterministic newest-first ordering
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"run-marksent-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var older = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"),
            "ceo@acme.com", DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = await AddPendingLeadWithRecipientAsync(email, "Beta Co", "fp-" + Guid.NewGuid().ToString("N"),
            "ceo@beta.com", DateTimeOffset.UtcNow);
        // The run flag is threaded on POST via the hidden <input name="run" value="1"> inside the
        // email form (Detail.cshtml), not a query-string suffix on the form action — so the
        // antiforgery GET and the POST both carry it as a plain form field, same as the real form.
        var form = await AntiforgeryForm(client, $"/app/leads/{older}?run=1", new() {
            ["run"] = "1", ["Input.Subject"] = "Hi", ["Input.Draft"] = "Body", ["Input.Recipient"] = "ceo@acme.com",
        });

        // Act
        var post = await client.PostAsync($"/app/leads/{older}?handler=MarkSent", new FormUrlEncodedContent(form));

        // Assert — hops straight to the other (still-unfinished) lead, still in run mode
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.OriginalString.Should().Contain(newer.ToString());
        post.Headers.Location!.OriginalString.Should().Contain("run=1");
    }

    [Fact]
    public async Task MarkSent_InRunMode_AdvancesToNewestRemainingLead()
    {
        // Arrange — three PendingReview leads with recipients and distinct CreatedAt, so that after
        // "acted" is sent, two candidates remain ("middle" and "newest"). Only a genuine newest-first
        // ordering picks "newest" here — oldest-first or unordered would wrongly land on "middle".
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"run-marksent-newest-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var acted = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"),
            "ceo@acme.com", DateTimeOffset.UtcNow.AddMinutes(-10));
        var middle = await AddPendingLeadWithRecipientAsync(email, "Beta Co", "fp-" + Guid.NewGuid().ToString("N"),
            "ceo@beta.com", DateTimeOffset.UtcNow.AddMinutes(-5));
        var newest = await AddPendingLeadWithRecipientAsync(email, "Gamma Co", "fp-" + Guid.NewGuid().ToString("N"),
            "ceo@gamma.com", DateTimeOffset.UtcNow);
        var form = await AntiforgeryForm(client, $"/app/leads/{acted}?run=1", new() {
            ["run"] = "1", ["Input.Subject"] = "Hi", ["Input.Draft"] = "Body", ["Input.Recipient"] = "ceo@acme.com",
        });

        // Act
        var post = await client.PostAsync($"/app/leads/{acted}?handler=MarkSent", new FormUrlEncodedContent(form));

        // Assert — hops to the newest of the two remaining unfinished leads, not the merely-older one
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.OriginalString.Should().Contain(newest.ToString());
        post.Headers.Location!.OriginalString.Should().Contain("run=1");
        post.Headers.Location!.OriginalString.Should().NotContain(middle.ToString());
    }

    [Fact]
    public async Task MarkSent_InRunMode_WhenNoMoreLeads_RedirectsToApp()
    {
        // Arrange — a single PendingReview lead, nothing left in the queue once it's sent
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"run-marksent-last-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");
        var form = await AntiforgeryForm(client, $"/app/leads/{id}?run=1", new() {
            ["run"] = "1", ["Input.Subject"] = "Hi", ["Input.Draft"] = "Body", ["Input.Recipient"] = "ceo@acme.com",
        });

        // Act
        var post = await client.PostAsync($"/app/leads/{id}?handler=MarkSent", new FormUrlEncodedContent(form));

        // Assert — falls back to the briefing, not another lead detail page
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.OriginalString.Should().Contain("/app");
        post.Headers.Location!.OriginalString.Should().NotContain("/app/leads/");
    }

    [Fact]
    public async Task Skip_InRunMode_RedirectsToNextUnfinishedLead()
    {
        // Arrange — two PendingReview leads with recipients, deterministic newest-first ordering
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"run-skip-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var older = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"),
            "ceo@acme.com", DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = await AddPendingLeadWithRecipientAsync(email, "Beta Co", "fp-" + Guid.NewGuid().ToString("N"),
            "ceo@beta.com", DateTimeOffset.UtcNow);
        var form = await AntiforgeryForm(client, $"/app/leads/{older}?run=1", new() { ["run"] = "1" });

        // Act
        var post = await client.PostAsync($"/app/leads/{older}?handler=Discard", new FormUrlEncodedContent(form));

        // Assert — skip advances to the other (still-unfinished) lead, still in run mode
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.OriginalString.Should().Contain(newer.ToString());
        post.Headers.Location!.OriginalString.Should().Contain("run=1");
    }

    [Fact]
    public async Task Detail_ForApprovedLeadWithRecipient_ShowsMarkSentAndHandoff()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"detail-approved-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddApprovedLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        html.Should().Contain("handler=MarkSent");
        html.Should().Contain("handoff-gmail");
        html.Should().Contain("Send from your own inbox");
    }

    [Fact]
    public async Task Detail_ForAdmin_ShowsSendFromColdstart_AndSendingMarksSent()
    {
        // Arrange — an account that is BOTH SuperAdmin and has a tenant (role added before sign-in
        // so it lands in the auth cookie), with an approved lead.
        var client = factory.CreateClient();
        var email = $"admin-send-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!long", role: ColdstartRoles.SuperAdmin, provisionTenant: true);
        await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = "Passw0rd!long", ["Input.RememberMe"] = "false",
        });
        var id = await AddApprovedLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");
        factory.Outreach.LastSent = null;

        // Act — the send posts the email form (recipient/subject/draft), which the server saves then sends
        var page = await client.GetStringAsync($"/app/leads/{id}");
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new()
        {
            ["Input.Recipient"] = "ceo@acme.com", ["Input.Subject"] = "subj", ["Input.Draft"] = "body",
        });
        var post = await client.PostAsync($"/app/leads/{id}?handler=SendFromColdstart", new FormUrlEncodedContent(form));

        // Assert — admin sees the button; posting sends via the mailer and marks the lead Sent
        page.Should().Contain("SendFromColdstart");
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        factory.Outreach.LastSent.Should().NotBeNull();
        factory.Outreach.LastSent!.To.Should().Be("ceo@acme.com");
        var sent = await client.GetStringAsync("/app/history?filter=sent");
        sent.Should().Contain("Acme Co");
    }

    [Fact]
    public async Task Detail_SendFromColdstart_UsesTheEditedRecipient_EvenWithoutSaveDraft()
    {
        // Arrange — admin with an approved lead whose STORED recipient is the original address
        var client = factory.CreateClient();
        var email = $"admin-edit-send-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!long", role: ColdstartRoles.SuperAdmin, provisionTenant: true);
        await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = "Passw0rd!long", ["Input.RememberMe"] = "false",
        });
        var id = await AddApprovedLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "old@acme.com");
        factory.Outreach.LastSent = null;

        // Act — the send posts the email FORM with an edited recipient/subject, WITHOUT a prior "Save draft"
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new()
        {
            ["Input.Recipient"] = "new@acme.com", ["Input.Subject"] = "Updated subject", ["Input.Draft"] = "Updated body",
        });
        await client.PostAsync($"/app/leads/{id}?handler=SendFromColdstart", new FormUrlEncodedContent(form));

        // Assert — it sends to what the operator sees (the edited recipient), not the stale stored one
        factory.Outreach.LastSent.Should().NotBeNull();
        factory.Outreach.LastSent!.To.Should().Be("new@acme.com");
        factory.Outreach.LastSent!.Subject.Should().Be("Updated subject");
    }

    [Fact]
    public async Task Detail_ForRegularCustomer_HidesSendFromColdstart_AndForbidsThePost()
    {
        // Arrange — a normal customer (no SuperAdmin role)
        var client = factory.CreateClient();
        var email = $"cust-send-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddApprovedLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");
        factory.Outreach.LastSent = null;

        // Act
        var page = await client.GetStringAsync($"/app/leads/{id}");
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new());
        // Mark the request as AJAX so the cookie auth handler surfaces the handler's Forbid() as a
        // raw 403 instead of a 302 to AccessDeniedPath (which is configured to the same Login page,
        // so a plain browser POST would otherwise land on a 200 login page rather than 403).
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/app/leads/{id}?handler=SendFromColdstart")
        {
            Content = new FormUrlEncodedContent(form),
        };
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var post = await client.SendAsync(req);

        // Assert — no button, and the handler refuses non-admins
        page.Should().NotContain("SendFromColdstart");
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Outreach.LastSent.Should().BeNull();
    }

    [Fact]
    public async Task AddNote_PersistsAndRendersOnTheLead()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"note-add-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddLeadForUserAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"));
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new() { ["Input.NoteText"] = "ring them Tuesday" });

        // Act
        var post = await client.PostAsync($"/app/leads/{id}?handler=AddNote", new FormUrlEncodedContent(form));
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        html.Should().Contain("ring them Tuesday");
    }

    [Fact]
    public async Task RemoveNote_RemovesItFromTheLead()
    {
        // Arrange — add a note, then discover its id from the rendered delete form's hidden NoteId input
        var client = factory.CreateClient();
        var email = $"note-remove-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddLeadForUserAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"));
        var addForm = await AntiforgeryForm(client, $"/app/leads/{id}", new() { ["Input.NoteText"] = "call back Friday" });
        await client.PostAsync($"/app/leads/{id}?handler=AddNote", new FormUrlEncodedContent(addForm));
        var htmlWithNote = await client.GetStringAsync($"/app/leads/{id}");
        var noteId = ExtractNoteId(htmlWithNote);
        var removeForm = await AntiforgeryForm(client, $"/app/leads/{id}", new() { ["Input.NoteId"] = noteId.ToString() });

        // Act
        var post = await client.PostAsync($"/app/leads/{id}?handler=RemoveNote", new FormUrlEncodedContent(removeForm));
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        html.Should().NotContain("call back Friday");
    }

    [Fact]
    public async Task AddNote_WithInvalidNote_DoesNotBlankTheEmailFields()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"note-invalid-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddLeadWithDraftAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"),
            "Distinct-Subject-Marker", "Distinct-Draft-Marker");
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new() { ["Input.NoteText"] = "" });

        // Act
        var post = await client.PostAsync($"/app/leads/{id}?handler=AddNote", new FormUrlEncodedContent(form));
        var html = await post.Content.ReadAsStringAsync();

        // Assert
        post.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Distinct-Subject-Marker");
        html.Should().Contain("Distinct-Draft-Marker");
    }

    [Fact]
    public async Task Detail_ForShelvedLead_ShowsReopenHandler()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"detail-shelved-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddShelvedLeadAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        html.Should().Contain("handler=Reopen");
    }

    [Fact]
    public async Task Detail_NoMatchedSignals_ShowsBaselineScoreLabel()
    {
        // Arrange — a lead whose score came only from the source floor (no keyword hits)
        var client = factory.CreateClient();
        var email = $"detail-base-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddLeadWithNoMatchedSignalsAsync(email, "Floor Co", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        html.Should().Contain("baseline — source signal only");
    }

    [Fact]
    public async Task Detail_WithJavascriptSchemeSourceUrl_SuppressesTheLink()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"detail-jsurl-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
        var id = await AddLeadWithSourceUrlAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "javascript:alert(1)");

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        html.Should().NotContain("href=\"javascript:");
    }

    private async Task<Guid> AddLeadWithDraftAsync(string email, string company, string fingerprint, string subject, string draft)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            subject, draft, "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private async Task<Guid> AddShelvedLeadAsync(string email, string company, string fingerprint)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        lead.Discard();
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private async Task<Guid> AddLeadWithSourceUrlAsync(string email, string company, string fingerprint, string sourceUrl)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", sourceUrl, fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private static Guid ExtractNoteId(string html)
    {
        var marker = html.IndexOf("Input.NoteId", StringComparison.Ordinal);
        marker.Should().BeGreaterThanOrEqualTo(0, "the rendered delete form should carry a hidden NoteId input");
        var tagStart = html.LastIndexOf('<', marker);
        var tagEnd = html.IndexOf('>', marker);
        var tag = html[tagStart..tagEnd];
        var match = System.Text.RegularExpressions.Regex.Match(tag, "value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the NoteId input should render a value attribute");
        return Guid.Parse(match.Groups[1].Value);
    }

    private async Task<Guid> AddApprovedLeadWithRecipientAsync(string email, string company, string fingerprint, string recipient)
    {
        // Builds the lead straight through domain methods (mirrors what ILeadWorkflow.AcceptAsync
        // does) rather than calling the workflow itself: that goes through ILeadRepository.GetAsync,
        // which is tenant-filtered, and this setup scope has no signed-in request/tenant claim.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress(recipient));
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private async Task<Guid> AddPendingLeadWithRecipientAsync(string email, string company, string fingerprint, string recipient,
        DateTimeOffset? createdAt = null)
    {
        // Same rationale as AddApprovedLeadWithRecipientAsync: built straight through the domain so
        // this tenant-less setup scope doesn't need to go through the tenant-filtered repository.
        // createdAt is overridable so run-mode "next unfinished lead" ordering tests can seed a
        // deterministic newest-first order rather than relying on wall-clock timing.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", createdAt ?? DateTimeOffset.UtcNow);
        lead.SetRecipient(new EmailAddress(recipient));
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private async Task<Guid> AddLeadForUserAsync(string email, string company, string fingerprint)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private async Task<Guid> AddLeadWithNoMatchedSignalsAsync(string email, string company, string fingerprint)
    {
        // Mirrors AddLeadForUserAsync but with an empty MatchedSignals, i.e. a lead whose score is
        // the bare source floor with no keyword hits — the "baseline" case this task labels honestly.
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.5, "", DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

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

# Customer Zone Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the tabbed customer zone with a bounded daily briefing queue, a searchable History page, and a focused review flow — so lead volume never presents as a wall.

**Architecture:** ASP.NET Core Razor Pages under `Areas/App`. No backend rewrite — reuse `IGetLeadInbox`, `ILeadWorkflow`, `IGetCompanyProfile`. Changes are in the `/app` page model + views, one new `/app/history` page, the detail page, and front-end assets (`site.css`, `app-leads.js`).

**Tech Stack:** .NET 10, Razor Pages, EF Core/Npgsql, ASP.NET Identity. Tests: xUnit + FluentAssertions against `ColdstartWebFactory` (in-memory host) with `TestAuth` / `FoilWebFactory`.

**Design source of truth:**
- Spec: `docs/superpowers/specs/2026-07-25-customer-zone-redesign-design.md`
- Clickable mockup (exact classes, copy, layout): `docs/superpowers/specs/mockups/2026-07-25-customer-zone.html` — open in a browser; it links the real `site.css`. Treat it as the canonical visual reference for markup class names and copy.

## Global Constraints

- **No tabs anywhere in `/app`.** The `?tab=` query and `app-tabs`/`data-panel`/`data-bucket` markup are removed.
- **Queue = newest 15 "unfinished" leads**, where unfinished = `Status` is `PendingReview` or `Approved` (approved-but-not-sent). Newest first by `CreatedAt`. The 16th+ unfinished leads are "Older unreviewed" and live only in History.
- **Nothing expires, nothing auto-sends, no drag-and-drop.**
- **Sending stays from the customer's own inbox** (Gmail/mailto/copy → Mark as sent). "Send from Coldstart" stays gated behind `ColdstartRoles.SuperAdmin`. The prospect never sees Coldstart.
- **No separate Approve button in the customer flow.** Mark-as-sent (and admin Send-from-Coldstart) fold save+approve into the send action via the existing `AcceptAsync` then the send call. "Save draft" persists without approving (`SaveDraftAsync`).
- **Company profile is not rendered on `/app`.** It lives at `/app/profile`; `/app` shows only a company chip linking there.
- Reuse existing Fit chip rendering (`.fit .fit-@lead.Fit.ToLower()` → e.g. `fit-yes`/`fit-maybe`). Do **not** rename to HIGH/MED — that was mockup shorthand only.
- **C#/.NET standards** (from CLAUDE.md): `internal` over `public` for impl; records for DTOs; `CancellationToken` on IO; async all the way; xUnit + FluentAssertions; AAA with comments; test naming `Method_State_Expected`.
- **Git:** never `git push`; never add a `Co-Authored-By` trailer. Commit after each task.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `Areas/App/Pages/Index.cshtml.cs` | Briefing model: `Queue` (newest-15 unfinished), `OlderUnreviewedCount`, overnight greeting, foil counts; `OnPostSkip` | 1, 2, 3 |
| `Areas/App/Pages/Index.cshtml` | Briefing view: greeting + Review all + queue rows + backlog line + blurred foil + company chip; **no** tabs/buckets/profile card | 1, 2, 3 |
| `Areas/App/Pages/History.cshtml(.cs)` | **New** `/app/history`: filter chips + search + status badges + reopen | 4 |
| `Areas/App/Pages/Leads/Detail.cshtml(.cs)` | Focused review restyle: own-inbox send for all customers, fold approve into send, run mode (`?run=1` → run bar + "→ next") | 5 |
| `wwwroot/js/app-leads.js` | Drop drag-drop; inline Skip via fetch; run-mode "→ next" | 6 |
| `wwwroot/css/site.css` | Briefing/history/review styles (reuse tokens) | 1–6 |
| `tests/Coldstart.Web.Tests/LeadReviewTests.cs` | Rewrite: briefing queue, no tabs, newest-15, backlog | 1 |
| `tests/Coldstart.Web.Tests/LeadFoilTests.cs` | Update: blurred-inline foil + unlock copy | 2 |
| `tests/Coldstart.Web.Tests/CustomerZoneTests.cs` | Update: profile moved off `/app`; briefing assertions | 3 |
| `tests/Coldstart.Web.Tests/CustomerHistoryTests.cs` | **New**: history filters/search/reopen | 4 |
| `tests/Coldstart.Web.Tests/LeadDetailTests.cs` | Update: post-send lands in History, own-inbox send, run mode | 5 |

---

## Task 1: Briefing queue — replace tabs with the newest-15 list

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css`
- Rewrite: `tests/Coldstart.Web.Tests/LeadReviewTests.cs`

**Interfaces:**
- Consumes: `IGetLeadInbox.ForCustomerAsync` → `LeadInbox(CustomerName, IReadOnlyList<LeadSummary> Leads, CustomerPlan Plan, int LockedCount, HarvestStatus HarvestStatus)`. `LeadSummary(Id, Company, Fit, Status, RecipientEmail, EmailConfidence, Subject, Draft, Source, Score, CreatedAt, MatchedSignals, Locked)`.
- Produces: `IndexModel.Queue : IReadOnlyList<LeadSummary>` (newest-15 unfinished), `IndexModel.OlderUnreviewedCount : int`, `IndexModel.OvernightCount : int`, `IndexModel.SentTodayCount : int`, `IndexModel.HistoryCount : int`, `OnPostSkipAsync(Guid id)`.

Constant: `private const int QueueCap = 15;`

- [ ] **Step 1: Write failing tests — rewrite `LeadReviewTests.cs`**

Replace the file's `[Fact]` methods (keep the `AddLeadForUserAsync(email, company, fingerprint, LeadStatus status = LeadStatus.PendingReview)` helper and usings) with:

```csharp
[Fact]
public async Task Index_RendersBriefingQueue_WithoutTabs()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"rev-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
    await AddLeadForUserAsync(email, "Pending Co", "fp-" + Guid.NewGuid().ToString("N"));

    // Act
    var html = await client.GetStringAsync("/app");

    // Assert
    html.Should().Contain("Pending Co");
    html.Should().Contain("lead-row");
    html.Should().NotContain("app-tabs");
    html.Should().NotContain("?tab=");
    html.Should().NotContain("data-panel");
}

[Fact]
public async Task Index_QueueShowsNewest15Unfinished_RestGoToOlderCount()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"rev-cap-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
    for (var i = 0; i < 18; i++)
        await AddLeadForUserAsync(email, $"Co{i:D2}", "fp-" + Guid.NewGuid().ToString("N"));

    // Act
    var html = await client.GetStringAsync("/app");

    // Assert — 15 rows shown, backlog line names the remaining 3.
    // NB: match the exact class="lead-row" (trailing quote) so lead-row-head/-why/-subj don't inflate the count.
    System.Text.RegularExpressions.Regex.Matches(html, "class=\"lead-row\"").Count.Should().Be(15);
    html.Should().Contain("3 older leads");
    html.Should().Contain("/app/history");
}

[Fact]
public async Task Index_ApprovedNotSentLead_CountsAsUnfinished_AndShowsInQueue()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"rev-appr-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
    await AddLeadForUserAsync(email, "Approved Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.Approved);

    // Act
    var html = await client.GetStringAsync("/app");

    // Assert
    html.Should().Contain("Approved Co");
}

[Fact]
public async Task Skip_MovesLeadOutOfQueue_ToSkipped()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"rev-skip-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
    await AddLeadForUserAsync(email, "Move Co", "fp-" + Guid.NewGuid().ToString("N"));
    var id = await LeadIdForAsync(email, "Move Co");
    var form = await AntiforgeryForm(client, "/app", new() { ["id"] = id.ToString() });

    // Act
    var post = await client.PostAsync("/app?handler=Skip", new FormUrlEncodedContent(form));
    var briefing = await client.GetStringAsync("/app");

    // Assert
    post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
    briefing.Should().NotContain("Move Co");   // gone from the queue
}
```

Add helpers to the test class (mirror `CustomerZoneTests`):

```csharp
private async Task<Guid> LeadIdForAsync(string email, string company)
{
    using var scope = factory.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
    var user = await users.FindByEmailAsync(email);
    return db.Leads.First(l => l.CustomerId.Value == user!.CustomerId!.Value && l.Company == company).Id.Value;
}

private static async Task<Dictionary<string,string>> AntiforgeryForm(HttpClient client, string path, Dictionary<string,string> fields)
{
    var html = await client.GetStringAsync(path);
    var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
    var start = html.IndexOf(marker, StringComparison.Ordinal);
    if (start >= 0) { start += marker.Length; var end = html.IndexOf('"', start); fields["__RequestVerificationToken"] = html[start..end]; }
    return fields;
}
```

Ensure `AddLeadForUserAsync` accepts an optional `LeadStatus status = LeadStatus.PendingReview` and applies it (approve/discard/etc. via the domain) — copy the pattern already used in the old `LeadReviewTests`/`LeadDetailTests` for setting status.

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter FullyQualifiedName~LeadReviewTests`
Expected: FAIL (old markup still present; `handler=Skip` missing).

- [ ] **Step 3: Rework `IndexModel`**

In `Index.cshtml.cs`: remove `Tab`, `New/AcceptedReady/Sent/Shelved/AutoSkipped`, `NewCount/AcceptedCount/DiscardedCount` and the `OnPostMoveAsync` tab switch. Add:

```csharp
private const int QueueCap = 15;

public IReadOnlyList<LeadSummary> Queue { get; private set; } = [];
public int OlderUnreviewedCount { get; private set; }
public int OvernightCount { get; private set; }
public int SentTodayCount { get; private set; }
public int HistoryCount { get; private set; }
public DateTimeOffset? LatestFound { get; private set; }
```

In `OnGetAsync` (drop the `tab` parameter), after loading `result`:

```csharp
var unfinished = result.Leads
    .Where(l => l.Status is "PendingReview" or "Approved")
    .OrderByDescending(l => l.CreatedAt).ToList();
Queue = unfinished.Take(QueueCap).ToList();
OlderUnreviewedCount = Math.Max(0, unfinished.Count - QueueCap);

var todayUtc = DateTime.UtcNow.Date;
OvernightCount = unfinished.Count(l => l.CreatedAt.UtcDateTime.Date == todayUtc);
LatestFound = unfinished.Count > 0 ? unfinished.Max(l => l.CreatedAt) : null;
SentTodayCount = result.Leads.Count(l => l.Status == "Sent" && l.CreatedAt.UtcDateTime.Date == todayUtc);
HistoryCount = result.Leads.Count(l => l.Status is "Sent" or "Shelved" or "Rejected") + OlderUnreviewedCount;
```

Add the Skip handler (form-post; JS enhances it in Task 6):

```csharp
public async Task<IActionResult> OnPostSkipAsync(Guid id, CancellationToken ct)
{
    var user = await users.GetUserAsync(User);
    if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
    await workflow.DiscardAsync(user.CustomerId.Value, id, ct);
    return RedirectToPage("/Index", new { area = "App" });
}
```

Keep the hint logic, profile load (used until Task 3), `Plan`, `HarvestStatus`, `LockedCount`, `TotalLeads`.

- [ ] **Step 4: Rewrite `Index.cshtml` (briefing)**

Replace the tabs nav + three `app-panel` buckets + the `Card`/`Bucket` local functions with the briefing per the mockup (`#brief` section). Keep the harvest/empty greeting block and (for now) the profile card + upgrade/move-error dialogs. Structure:

- Greeting `<h1>` from harvest/overnight/empty states (reuse existing conditionals; the overnight branch reads "Good morning — @OvernightCount found overnight").
- Sub-stat line `latest @LatestFound HH:mm UTC · @SentTodayCount sent today · @HistoryCount in history` (omit pieces that are zero).
- `Review all →` button (`href="/app/leads/{firstQueueId}?run=1"`), rendered only when `Queue.Count > 0`.
- `<div class="cz-list">` of rows. Each unlocked lead → `<a class="lead-row" href="/app/leads/@lead.Id">` with `.lead-row-head` (company + `.fit .fit-@lead.Fit.ToLower()` + `.lead-row-why` = `⚡ @lead.MatchedSignals.Split(',')[0].Trim()` when present), `.lead-row-subj` = `@lead.Subject`, and a Skip control: a mini form
  ```html
  <form method="post" asp-page-handler="Skip" class="lead-skip-form"><input type="hidden" name="id" value="@lead.Id"><button class="cz-skip" type="submit" title="Skip">✕</button></form>
  ```
- Backlog line when `OlderUnreviewedCount > 0`: `+ @OlderUnreviewedCount older leads waiting · <a href="/app/history">see them in History →</a>`.

Use the mockup's class names (`cz-list`, `lead-row`, `lead-row-head`, `lead-row-why`, `lead-row-subj`, `cz-skip`) so existing CSS/tests align. (Locked/foil rows come in Task 2; profile removal in Task 3.)

- [ ] **Step 5: Add briefing CSS**

Port the `.cz-*` briefing rules from the mockup's `<style>` into `site.css` (greeting, `.cz-list`, `.lead-row` grid, `.cz-skip`, `.cz-stat`, backlog line), using existing tokens (`--daylight`, `--ink`, `--sunrise`, `--sunrise-deep`). Reuse the existing `.fit`/`.fit-yes` chips already in `site.css`.

- [ ] **Step 6: Run tests — verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter FullyQualifiedName~LeadReviewTests`
Expected: PASS. Then run the whole `Coldstart.Web.Tests` project; some `LeadFoilTests`/`LeadDetailTests`/`CustomerZoneTests` will now fail (they reference `?tab=`) — those are fixed in Tasks 2–5. Note the failing names but proceed.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Index.cshtml src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/LeadReviewTests.cs
git commit -m "feat(app): replace lead tabs with a bounded daily briefing queue"
```

---

## Task 2: Blurred-inline foil + unlock

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml` (+ `.cs` if needed for locked list)
- Modify: `src/Coldstart.Web/wwwroot/css/site.css`
- Update: `tests/Coldstart.Web.Tests/LeadFoilTests.cs`

**Interfaces:**
- Consumes: `LeadSummary.Locked` and `IndexModel.LockedCount` (already present).

- [ ] **Step 1: Update `LeadFoilTests.cs`**

The foil moves inline. Replace the two `?tab=new` GETs with `/app` and update assertions:

```csharp
[Fact]
public async Task Inbox_ForFreeCustomer_ShowsBlurredRows_AndUnlockCta()
{
    // Arrange … (unchanged free-customer setup)
    // Act
    var html = await client.GetStringAsync("/app");
    // Assert
    html.Should().Contain("Unlock all");
    html.Should().Contain("more");            // "N more … behind your plan"
    html.Should().Contain("lead-row locked"); // at least one blurred row inline
}

[Fact]
public async Task Inbox_WhileHarvestPending_ShowsFindingState()
{
    // Arrange … (harvest pending, no leads)
    // Act
    var html = await client.GetStringAsync("/app");
    // Assert
    html.Should().Contain("Finding your first leads");
}
```

Keep the existing redirect test but point any `/app?tab=new` at `/app`.

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test … --filter FullyQualifiedName~LeadFoilTests` → FAIL (`lead-row locked` absent).

- [ ] **Step 3: Render locked rows + CTA in `Index.cshtml`**

For `@lead.Locked` leads in the queue, render a non-link blurred row (mockup `.cz-row.locked` → here `class="lead-row locked"`): the `.lead-row-main` content blurred, plus a `🔒 Unlock` affordance whose click opens the existing `#upgrade` dialog. After the list, when `LockedCount > 0`, render `Unlock all @Model.LockedCount leads` button opening `#upgrade`. Keep the existing `#upgrade` dialog markup/copy.

- [ ] **Step 4: Add foil CSS**

Port `.lead-row.locked .lead-row-main { filter: blur(4.5px); user-select:none; }`, the `.cz-lock` pill, and `.cz-foilcta` from the mockup into `site.css`.

- [ ] **Step 5: Run — verify pass**

Run: `dotnet test … --filter FullyQualifiedName~LeadFoilTests` → PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(app): blurred inline foil rows for locked leads on the briefing"
```

---

## Task 3: Move company profile off the briefing → header chip

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml` (remove profile card; add chip)
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs` (drop profile load if now unused)
- Update: `tests/Coldstart.Web.Tests/CustomerZoneTests.cs`

**Interfaces:**
- Consumes: `IndexModel.CustomerName`, `Profile.CompanyName` (for the chip label).

- [ ] **Step 1: Update `CustomerZoneTests.cs`**

The profile card is no longer on `/app`. Change the two profile-on-zone tests:

```csharp
[Fact]
public async Task App_ShowsCompanyChip_LinkingToProfile_NotTheFullCard()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"chip-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");

    // Act
    var html = await client.GetStringAsync("/app");

    // Assert — a compact chip links to the profile page; the full card is NOT on /app
    html.Should().Contain("href=\"/app/profile\"");
    html.Should().NotContain("Add your company details");   // moved to the profile page
}

[Fact]
public async Task ProfilePost_PersistsAndRendersOnProfilePage()
{
    // Arrange
    var client = factory.CreateClient();
    await TestAuth.SignInAsync(factory, client, $"prof-{Guid.NewGuid():N}@test.com", "Passw0rd!");
    var form = await AntiforgeryForm(client, "/app/profile", new() {
        ["Input.CompanyName"] = "Acme Ltd", ["Input.VatNumber"] = "NL999",
        ["Input.Niche"] = "B2B SaaS", ["Input.Address"] = "", ["Input.KvkNumber"] = "",
        ["Input.Website"] = "", ["Input.ContactEmail"] = "",
    });

    // Act
    await client.PostAsync("/app/profile", new FormUrlEncodedContent(form));
    var profile = await client.GetStringAsync("/app/profile");

    // Assert
    profile.Should().Contain("Acme Ltd");
    profile.Should().Contain("NL999");
}
```

Delete/replace the old `App_ShowsEmptyProfilePrompt_ForFreshCustomer` and `ProfilePost_PersistsAndRendersOnZone` bodies accordingly. Leave `App_ShowsEmptyState_WhenTheCustomerHasNoLeadsYet` but update its expected copy if the empty text changed (keep "Nothing to review" or the new empty-morning copy — assert whatever the view now emits).

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test … --filter FullyQualifiedName~CustomerZoneTests` → FAIL.

- [ ] **Step 3: Edit `Index.cshtml`**

Remove the entire `<section class="app-profile">…</section>` block and the `<p class="app-sub">@Model.CustomerName</p>`. In the `app-bar` nav, add a company chip before the profile/logout: `<a class="cz-id" href="/app/profile">@(string.IsNullOrWhiteSpace(Model.Profile.CompanyName) ? Model.CustomerName : Model.Profile.CompanyName) <span class="cz-chev">▾</span></a>`. Keep the "Company profile" link and Log out (or fold "Company profile" into the chip).

- [ ] **Step 4: Trim the model if needed**

If `Profile` is now only used for the chip label, keep the `profiles.ForCustomerAsync` call (cheap) — no change required. Do not remove `IGetCompanyProfile` injection.

- [ ] **Step 5: Add chip CSS**

Port `.cz-id` / `.cz-chev` from the mockup into `site.css`.

- [ ] **Step 6: Run — verify pass**

Run: `dotnet test … --filter FullyQualifiedName~CustomerZoneTests` → PASS.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(app): move company profile off the briefing into a header chip"
```

---

## Task 4: History page (`/app/history`)

**Files:**
- Create: `src/Coldstart.Web/Areas/App/Pages/History.cshtml`
- Create: `src/Coldstart.Web/Areas/App/Pages/History.cshtml.cs`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css`
- Create: `tests/Coldstart.Web.Tests/CustomerHistoryTests.cs`

**Interfaces:**
- Consumes: `IGetLeadInbox.ForCustomerAsync`, `ILeadWorkflow.ReopenAsync`.
- Produces: page at route `/app/history`, `OnGetAsync(string? filter, string? q)`, `OnPostReopenAsync(Guid id)`. Filters: `all` (default), `sent`, `skipped`, `older`.

- [ ] **Step 1: Write `CustomerHistoryTests.cs`**

```csharp
using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class CustomerHistoryTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task History_All_ShowsSentSkippedAndOlderUnreviewed()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"hist-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadAsync(email, "Sent Co", LeadStatus.Sent);
        await AddLeadAsync(email, "Shelved Co", LeadStatus.Shelved);

        // Act
        var html = await client.GetStringAsync("/app/history");

        // Assert
        html.Should().Contain("Sent Co");
        html.Should().Contain("Shelved Co");
        html.Should().NotContain("app-tabs");
    }

    [Fact]
    public async Task History_SentFilter_ExcludesSkipped()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"hist-sent-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadAsync(email, "Sent Co", LeadStatus.Sent);
        await AddLeadAsync(email, "Shelved Co", LeadStatus.Shelved);

        // Act
        var html = await client.GetStringAsync("/app/history?filter=sent");

        // Assert
        html.Should().Contain("Sent Co");
        html.Should().NotContain("Shelved Co");
    }

    [Fact]
    public async Task History_Search_NarrowsByCompany()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"hist-q-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadAsync(email, "Patronus AI", LeadStatus.Sent);
        await AddLeadAsync(email, "Globex", LeadStatus.Sent);

        // Act
        var html = await client.GetStringAsync("/app/history?q=patronus");

        // Assert
        html.Should().Contain("Patronus AI");
        html.Should().NotContain("Globex");
    }

    [Fact]
    public async Task Reopen_MovesOlderLeadBackToTheQueue()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"hist-reopen-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadAsync(email, "Reopen Co", LeadStatus.Shelved);
        var id = await LeadIdAsync(email, "Reopen Co");
        var form = await AntiforgeryForm(client, "/app/history", new() { ["id"] = id.ToString() });

        // Act
        var post = await client.PostAsync("/app/history?handler=Reopen", new FormUrlEncodedContent(form));
        var briefing = await client.GetStringAsync("/app");

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        briefing.Should().Contain("Reopen Co");   // back in the queue
    }

    [Fact]
    public async Task GetHistory_WhenNotAuthenticated_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/app/history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    // Helpers: AddLeadAsync(email, company, status), LeadIdAsync(email, company), AntiforgeryForm —
    // copy the CreateHarvested + status-transition pattern from CustomerZoneTests/LeadReviewTests.
}
```

- [ ] **Step 2: Run — verify fail** (404/no page): `dotnet test … --filter FullyQualifiedName~CustomerHistoryTests` → FAIL.

- [ ] **Step 3: Create `History.cshtml.cs`**

```csharp
[Authorize]
public sealed class HistoryModel(UserManager<ApplicationUser> users, IGetLeadInbox inbox, ILeadWorkflow workflow) : PageModel
{
    private const int QueueCap = 15;   // must match IndexModel
    public string Filter { get; private set; } = "all";
    public string? Query { get; private set; }
    public IReadOnlyList<LeadSummary> Rows { get; private set; } = [];
    public int AllCount { get; private set; }
    public int SentCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int OlderCount { get; private set; }

    public async Task OnGetAsync(string? filter, string? q, CancellationToken ct)
    {
        Filter = filter is "sent" or "skipped" or "older" ? filter : "all";
        Query = q;
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;
        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);

        var unfinished = result.Leads.Where(l => l.Status is "PendingReview" or "Approved")
            .OrderByDescending(l => l.CreatedAt).ToList();
        var older = unfinished.Skip(QueueCap).ToList();                       // beyond the queue cap
        var sent = result.Leads.Where(l => l.Status == "Sent").ToList();
        var skipped = result.Leads.Where(l => l.Status is "Shelved" or "Rejected").ToList();

        SentCount = sent.Count; SkippedCount = skipped.Count; OlderCount = older.Count;
        AllCount = sent.Count + skipped.Count + older.Count;

        IEnumerable<LeadSummary> rows = Filter switch
        {
            "sent" => sent, "skipped" => skipped, "older" => older,
            _ => sent.Concat(skipped).Concat(older),
        };
        if (!string.IsNullOrWhiteSpace(q))
            rows = rows.Where(l => l.Company.Contains(q, StringComparison.OrdinalIgnoreCase));
        Rows = rows.OrderByDescending(l => l.CreatedAt).ToList();
    }

    public async Task<IActionResult> OnPostReopenAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is not null) await workflow.ReopenAsync(user.CustomerId.Value, id, ct);
        return RedirectToPage("/History", new { area = "App" });
    }
}
```

Page directive: `@page "/app/history"`, `Layout = "/Pages/Shared/_Layout.cshtml"`.

- [ ] **Step 4: Create `History.cshtml`**

Match the mockup `#history` screen: app-bar (wordmark, company chip, log out), back link, `History` heading, filter chips (`All @AllCount` / `Sent @SentCount` / `Skipped @SkippedCount` / `Older unreviewed @OlderCount`) as anchors `href="/app/history?filter=…"` with `.on` on the active one, a search `<form method="get">` with `name="q"`, then `.cz-list` rows. Each row: company + status badge (`SENT · dd MMM` / `SKIPPED · you|not a fit` by `Status` — Shelved="you", Rejected="not a fit" / `OLDER · dd MMM`) + subject. Older rows include a reopen form (`asp-page-handler="Reopen"`, hidden `id`) rendering `reopen →`.

- [ ] **Step 5: Add history CSS** — port `.cz-filters`, `.cz-chip`, `.cz-search`, `.cz-stbadge`, `.st-sent/.st-skip/.st-old` from the mockup.

- [ ] **Step 6: Run — verify pass**: `dotnet test … --filter FullyQualifiedName~CustomerHistoryTests` → PASS.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(app): searchable History page replacing the Approved/Skipped tabs"
```

---

## Task 5: Focused review — own-inbox send for all + fold approve + run mode

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml.cs`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css`
- Update: `tests/Coldstart.Web.Tests/LeadDetailTests.cs`

**Interfaces:**
- Consumes: `ILeadWorkflow.AcceptAsync` (save+approve), `MarkSentAsync`, `SendFromColdstartAsync`, `SaveDraftAsync`, `ReopenAsync`.
- Behaviour: A pending lead's review page must expose the own-inbox send controls directly (no separate "approve first" gate). `Mark as sent` runs `AcceptAsync(subject, draft, recipient)` then `MarkSentAsync`. Run mode (`?run=1`) shows the run bar and, on skip/mark-sent, redirects to the next unfinished queue lead (or `/app` when none remain).

- [ ] **Step 1: Update `LeadDetailTests.cs`**

Change post-send navigation checks from `/app?tab=…` to History, and add own-inbox-for-pending + run-mode expectations:

```csharp
[Fact]
public async Task Detail_ForPendingLead_ShowsOwnInboxSend_WithoutSeparateApprove()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"det-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
    var id = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");

    // Act
    var html = await client.GetStringAsync($"/app/leads/{id}");

    // Assert — customer sends from their own inbox; no exposed "Send from Coldstart"
    html.Should().Contain("Send from your own inbox");
    html.Should().Contain("handoff-gmail");
    html.Should().NotContain("Send from Coldstart");
}

[Fact]
public async Task MarkSent_FoldsApprove_ThenLeadLandsInHistorySent()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"det-sent-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
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
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
    var id = await AddPendingLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");

    // Act
    var html = await client.GetStringAsync($"/app/leads/{id}?run=1");

    // Assert
    html.Should().Contain("Reviewing");   // run bar present
}
```

Keep the SuperAdmin `SendFromColdstart` tests but change any `/app?tab=sent` assertion to `/app/history?filter=sent`. Add an `AddPendingLeadWithRecipientAsync` helper (like `AddApprovedLeadWithRecipientAsync` but leaving status `PendingReview` with a recipient set).

- [ ] **Step 2: Run — verify fail**: `dotnet test … --filter FullyQualifiedName~LeadDetailTests` → FAIL.

- [ ] **Step 3: Edit `Detail.cshtml.cs`**

- Make the own-inbox send section render for pending leads too (not just approved). Add a `Run` bool bound from `?run=1`.
- `OnPostMarkSentAsync`: first call `workflow.AcceptAsync(cid, id, Input.Subject, Input.Draft, Input.Recipient, ct)` (save+approve), then `workflow.MarkSentAsync(cid, id, ct)`. On success in run mode, redirect to the next unfinished queue lead: query the inbox, take the newest-15 unfinished excluding this lead, redirect to `/app/leads/{next}?run=1`, or `/app` when none. Reuse a private `NextQueueLeadAsync(cid, excludeId, ct)` helper.
- `OnPostDiscard`/Skip in run mode likewise advances to the next lead.
- Guard: `SendFromColdstart` handler + button remain `SuperAdmin`-only (unchanged).

- [ ] **Step 4: Edit `Detail.cshtml`**

- Restyle header to the mockup review layout (company + fit + why line with decision-maker link).
- Show the "Send from your own inbox" panel whenever the lead is not already sent (remove the `@if (approved)` gate; keep it hidden only for `sent`). Copy: *"Send it from your own inbox — <Company> sees you, never Coldstart."*
- When `Model.Run`, render the run bar (`Reviewing … from this morning` + `Skip → next`) and label the primary `Mark as sent ✓ → next`.
- Keep notes panel and the SuperAdmin-only Send-from-Coldstart block.

- [ ] **Step 5: Add review CSS** — port `.cz-runbar`, `.cz-review-head`, `.cz-review-why`, `.cz-sendbox`, `.cz-sendhead`, `.cz-sendrow` from the mockup.

- [ ] **Step 6: Run — verify pass**: `dotnet test … --filter FullyQualifiedName~LeadDetailTests` → PASS. Then run the whole `Coldstart.Web.Tests` project — expect green.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(app): focused review — own-inbox send for all, folded approve, run mode"
```

---

## Task 6: Front-end — drop drag-drop, inline Skip via fetch, run-mode next

**Files:**
- Modify: `src/Coldstart.Web/wwwroot/js/app-leads.js`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (final polish)

**Interfaces:** Progressive enhancement only — the Skip form (Task 1) and run-mode links (Task 5) already work without JS.

- [ ] **Step 1: Rewrite `app-leads.js`**

Remove all drag-and-drop and bucket-move code. Add:
- Intercept `.lead-skip-form` submit: `preventDefault`, POST via `fetch` with the antiforgery token, animate the row out (`opacity`/`translateX`) and remove it; on failure, fall back to a normal submit. Respect `prefers-reduced-motion`.
- No client-side tab logic (tabs are gone).

- [ ] **Step 2: Manual verification (no unit test for JS)**

Rebuild and run the app; on `/app`, clicking Skip removes the row without a full reload; with JS disabled the form still posts and redirects. Confirm the `Review all →` and per-row links navigate to `/app/leads/{id}?run=1`.

Run: `dotnet build src/Coldstart.Web/Coldstart.Web.csproj` → succeeds.

- [ ] **Step 3: Full suite + visual pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj` → all green. Screenshot `/app`, `/app/history`, `/app/leads/{id}?run=1` against the mockup for parity.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(app): progressive-enhance briefing skip + retire lead drag-and-drop"
```

---

## Self-review notes

- **Spec coverage:** no-tabs (T1), newest-15 cap + backlog (T1), unfinished=Pending+Approved (T1/T4/T5), blurred-inline foil (T2), profile→chip (T3), History with filters/search/reopen (T4), own-inbox send + fold-approve + run mode + SuperAdmin gate (T5), drop drag-drop (T6), states preserved (T1). All mapped.
- **Cross-task type consistency:** `QueueCap = 15` duplicated in `IndexModel` and `HistoryModel` — keep both in sync (Task 4 Step 3 notes it). Status strings (`"PendingReview"`, `"Approved"`, `"Sent"`, `"Shelved"`, `"Rejected"`) are the `LeadSummary.Status` values used consistently.
- **Known breakage handled:** `LeadReviewTests` (T1), `LeadFoilTests` (T2), `CustomerZoneTests` profile tests (T3), `LeadDetailTests` `?tab=` navigation (T5) are each rewritten in their owning task; after T5 the whole suite is green.

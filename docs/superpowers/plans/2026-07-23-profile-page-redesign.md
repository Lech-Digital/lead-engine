# Profile Page Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle `/app/profile` onto the app-zone shell with grouped two-column section-cards, per `docs/superpowers/specs/2026-07-23-profile-page-redesign-design.md`.

**Architecture:** Presentation-only. Rewrite `Profile.cshtml` markup (app-bar + `.wrap app-zone` + three `.panel` cards in one form); add a `.profile-*` CSS layer. `Profile.cshtml.cs` unchanged. Existing behavior-based tests must stay green.

**Tech Stack:** .NET 10 Razor Pages, existing Morning-Engine `site.css` tokens/`.panel`/`.field`/`.btn`, xUnit + FluentAssertions + WebApplicationFactory.

## Global Constraints

- Presentation only: NO change to `Profile.cshtml.cs`, field `id`/`name` attributes (`Input.CompanyName/Address/VatNumber/KvkNumber/Niche/Website/ContactEmail/Brief/MentionSourceTool`), or handler names (`Save`=default `OnPostAsync`, `Polish`, `ShowHints`).
- Keep the hidden first-submit (`<button type="submit" hidden aria-hidden="true" tabindex="-1"></button>`) as the FIRST submit inside the form so Enter saves; the Polish button keeps `asp-page-handler="Polish"`.
- Profile page stops using `auth-card`/`auth-shell`; other pages' use of those classes is untouched.
- Monospace only for machine metadata (none here); `[Authorize]` + antiforgery unchanged (Razor form tag helper).
- Full-solution `dotnet test` green before committing (report per-project totals).
- Conventional commit, no trailers. Stage only the listed paths — repo has unrelated uncommitted user work; never stage it.

---

### Task 1: Restyle the profile page

**Files:**
- Rewrite: `src/Coldstart.Web/Areas/App/Pages/Profile.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (append a `.profile-*` layer)
- Test: `tests/Coldstart.Web.Tests/CustomerZoneTests.cs` (add one shell-consistency assertion)

**Interfaces:**
- Consumes: existing `ProfileModel` (`Input.*` bound props, `StatusMessage`, handlers Save/Polish/ShowHints), Morning-Engine `.panel`/`.field`/`.btn`/`.wordmark`/`.app-bar`/`.wrap`/`.app-zone` styles.
- Produces: no new server interface.

- [ ] **Step 1: Add the failing shell-consistency test**

In `tests/Coldstart.Web.Tests/CustomerZoneTests.cs`, add (reuse the file's signed-in-client + `TestAuth` pattern):

```csharp
[Fact]
public async Task Profile_UsesAppBarShell_NotTheNarrowAuthCard()
{
    // Arrange
    var client = factory.CreateClient();
    await TestAuth.SignInAsync(factory, client, $"prof-shell-{Guid.NewGuid():N}@test.com", "Passw0rd!");

    // Act
    var html = await client.GetStringAsync("/app/profile");

    // Assert — top app-bar nav (like the lead pages), grouped panels, no auth-card shell
    html.Should().Contain("app-bar");
    html.Should().Contain("profile-grid");
    html.Should().NotContain("auth-card");
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~Profile_UsesAppBarShell"`
Expected: FAIL — page still renders `auth-card`, no `app-bar`/`profile-grid`.

- [ ] **Step 3: Rewrite `Profile.cshtml`**

Full replacement (keep every `asp-for`/`asp-page-handler` exactly; only structure/classes change):

```html
@page "/app/profile"
@model Coldstart.Web.Areas.App.Pages.ProfileModel
@{ ViewData["Title"] = "Company profile"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<header class="app-bar">
  <a class="wordmark" href="/">coldstart<i>*</i></a>
  <nav class="app-nav">
    <a href="/app">Your briefing</a>
    <form method="post" asp-page="/Account/Logout" asp-area="Identity"><button class="btn btn-ghost" type="submit">Log out</button></form>
  </nav>
</header>

<main class="wrap app-zone profile">
  <section class="app-briefing">
    <h1>Company profile</h1>
    <p class="app-briefing-ts">Shown on your account · all fields optional</p>
  </section>

  <form method="post" class="profile-form">
    <button type="submit" hidden aria-hidden="true" tabindex="-1"></button>

    <section class="panel">
      <h2 class="app-group-head">Company</h2>
      <div class="profile-grid">
        <div class="field"><label asp-for="Input.CompanyName">Company name</label><input asp-for="Input.CompanyName"></div>
        <div class="field"><label asp-for="Input.Website">Website</label><input asp-for="Input.Website"></div>
        <div class="field"><label asp-for="Input.Niche">Niche</label><input asp-for="Input.Niche"></div>
        <div class="field"><label asp-for="Input.ContactEmail">Contact email</label><input asp-for="Input.ContactEmail" type="email"></div>
      </div>
    </section>

    <section class="panel">
      <h2 class="app-group-head">Registration</h2>
      <div class="profile-grid">
        <div class="field profile-span-2"><label asp-for="Input.Address">Address</label><input asp-for="Input.Address"></div>
        <div class="field"><label asp-for="Input.VatNumber">VAT number</label><input asp-for="Input.VatNumber"></div>
        <div class="field"><label asp-for="Input.KvkNumber">KvK number</label><input asp-for="Input.KvkNumber"></div>
      </div>
    </section>

    <section class="panel">
      <h2 class="app-group-head">Outreach</h2>
      <div class="field">
        <label asp-for="Input.Brief">Company brief <span class="field-hint">— a short "who we are" used as context in your outreach</span></label>
        <textarea asp-for="Input.Brief" rows="4" placeholder="e.g. Acme helps dental clinics fill last-minute cancellations with automated waitlist texts."></textarea>
        <button class="btn btn-ghost btn-sm profile-polish" type="submit" asp-page-handler="Polish">Polish with AI</button>
      </div>
      <div class="field field-check">
        <label><input asp-for="Input.MentionSourceTool" type="checkbox"> Mention leads were found via Coldstart</label>
      </div>
    </section>

    <div class="profile-actions">
      <button class="btn btn-primary" type="submit">Save →</button>
      @if (Model.StatusMessage is not null) { <span class="form-status">@Model.StatusMessage</span> }
    </div>
  </form>

  <section class="panel profile-hints">
    <h2 class="app-group-head">Onboarding hints</h2>
    <p>Want the getting-started tips again? They'll reappear one per day.</p>
    <form method="post" asp-page-handler="ShowHints">
      <button type="submit" class="btn btn-ghost">Show hints again</button>
    </form>
  </section>
</main>
```

- [ ] **Step 4: Append the `.profile-*` CSS layer**

Add to `site.css`:

```css
/* ===== Company profile ===== */
.profile-form { display: flex; flex-direction: column; gap: 14px; margin-bottom: 14px; }
.profile-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px 16px; }
.profile-span-2 { grid-column: 1 / -1; }
.profile .panel h2 { margin-top: 0; }
.profile-polish { margin-top: 8px; align-self: flex-start; }
.profile-actions { display: flex; align-items: center; gap: 14px; padding: 4px 2px; }
.profile-actions .form-status { margin: 0; color: #2E7D4F; }
.profile-hints { margin-top: 6px; }
.profile-hints p { color: var(--body-muted); font-size: 0.9rem; margin: 0 0 10px; }
@media (max-width: 640px) { .profile-grid { grid-template-columns: 1fr; } }
/* ===== end company profile ===== */
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~Profile|FullyQualifiedName~CustomerZone"`
Expected: the new shell test PASSES and all existing profile/customer-zone tests (persist brief/toggle, polish preview, hidden-default-submit-before-Polish, hints reset, save round-trip, empty-profile prompt) stay green. If any existing test pinned `auth-card` for the profile page specifically, that's a real break — update it to the new shell marker; do NOT weaken behavioral assertions.
Then full `dotnet test` → green.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Profile.cshtml src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/CustomerZoneTests.cs
git commit -m "feat(app): redesign company profile onto the app-zone shell with grouped section cards"
```

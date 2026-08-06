# Morning Engine Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Coldstart's retro-terminal identity with the "Morning Engine" identity across the landing page (keynote narrative), customer zone, footer + legal pages, and a four-template email set, per `docs/superpowers/specs/2026-07-21-morning-engine-redesign-design.md`.

**Architecture:** Pure presentation-layer work in `Coldstart.Web` (Razor Pages, one shared `site.css`, static `EmailTemplates`), plus two tiny Application-layer touches: `CreatedAt` added to the `LeadSummary` read model, and a welcome email sent from `ResetPassword.OnPostAsync` on invite completion. No pipeline, routing, or data-model changes: `?tab=` slugs, `data-tab` values, form handlers, and `app-*` CSS hooks asserted by tests all stay.

**Tech Stack:** .NET 10 Razor Pages, vanilla JS (no new deps), self-hosted Fontshare fonts (Cabinet Grotesk, Switzer), xUnit + FluentAssertions + Testcontainers (existing `ColdstartWebFactory`).

## Global Constraints

- Palette tokens verbatim from spec: `--daylight:#FDFBF7`, `--ink:#16192B`, `--night:#1C1F33`, `--night-raised:#242841` (border `#33385A`, rule `#2C3049`), `--night-text:#E9E7F2` (muted `#9BA1C4`, faint `#7C82A6`), `--sunrise:#FF6A3D` (soft `#FF8A5C`), `--body-muted:#5D5A50` (subtle `#8B8578`); dawn gradient `#1C1F33 → #54425E → #C96F4A → #F5C97B → #FDFBF7`.
- Fonts self-hosted from `wwwroot/fonts/` — no CDN requests at runtime.
- Monospace is used ONLY for clock timestamps and machine metadata (`ui-monospace, "SF Mono", Menlo, Consolas, monospace`).
- Copy is jargon-free: never use "pipeline", "outbound", "SDR", "buying signal", "qualified" without immediate plain-language grounding.
- Unchanged and MUST NOT be renamed: `?tab=new|accepted|discarded` query values, `data-tab`/`data-bucket` attributes, page handler names (`Move`, `Accept`, `Save`, `MarkSent`, `Discard`, `Reopen`), contact form field names (`Name`, `Email`, `Niche`, `WhatYouSell`, `Website` honeypot), CSS classes `app-lead`, `app-bar`, `app-tabs`, `app-tab-count`, `hero-login`.
- Emails: table-based layout, all CSS inline, zero images, every template has a plain-text body, `getcoldstart.nl` stays in the HTML footer.
- Tests: xUnit + FluentAssertions, `MethodName_StateUnderTest_ExpectedBehavior` naming, `// Arrange` `// Act` `// Assert` comments. Full suite (`dotnet test`) green at the end of every task.
- Commits: conventional-commit style (`feat(web): …`), NO `Co-Authored-By` or "Generated with" trailers.
- Prices: €750/month ex VAT, €350 one-time setup ex VAT — exactly these numbers wherever pricing appears.
- Legal pages carry a visible draft banner: "Draft — pending review, not legal advice."

---

### Task 1: Fonts, design tokens, and layout head

**Files:**
- Create: `src/Coldstart.Web/wwwroot/fonts/CabinetGrotesk-Variable.woff2`, `src/Coldstart.Web/wwwroot/fonts/Switzer-Variable.woff2` (downloaded)
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (prepend token layer; keep all existing rules below for now)
- Modify: `src/Coldstart.Web/Pages/Shared/_Layout.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/favicon.svg` (replace content)
- Test: `tests/Coldstart.Web.Tests/BrandAssetsTests.cs` (new)

**Interfaces:**
- Consumes: nothing.
- Produces: CSS custom properties (`--daylight`, `--ink`, `--night`, `--night-raised`, `--night-border`, `--night-rule`, `--night-text`, `--night-muted`, `--night-faint`, `--sunrise`, `--sunrise-soft`, `--body-muted`, `--body-subtle`, `--display`, `--body`, `--mono`, `--maxw`), font families "Cabinet Grotesk" and "Switzer", `.wordmark` component, restyled `.btn/.btn-primary/.btn-ghost/.field`. All later tasks style against these.

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/BrandAssetsTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

[Collection("web")]
public class BrandAssetsTests(ColdstartWebFactory factory)
{
    [Theory]
    [InlineData("/fonts/CabinetGrotesk-Variable.woff2")]
    [InlineData("/fonts/Switzer-Variable.woff2")]
    public async Task GetFont_IsServedAsStaticAsset(string path)
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLanding_ReferencesSelfHostedFonts_NotACdn()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var css = await client.GetStringAsync("/css/site.css");

        // Assert
        css.Should().Contain("/fonts/CabinetGrotesk-Variable.woff2");
        css.Should().Contain("/fonts/Switzer-Variable.woff2");
        css.Should().NotContain("fontshare.com");
        css.Should().NotContain("googleapis.com");
    }
}
```

Note: first check how existing test classes get the factory (open `tests/Coldstart.Web.Tests/PublicPagesTests.cs` and copy its exact class declaration pattern — collection attribute / `IClassFixture<ColdstartWebFactory>` — instead of the `[Collection("web")]` guess above).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~BrandAssets"`
Expected: FAIL — 404 for both fonts, css missing font references.

- [ ] **Step 3: Download and place the fonts**

```bash
SCRATCH=$(mktemp -d)
curl -L -o "$SCRATCH/cg.zip" "https://api.fontshare.com/v2/fonts/download/cabinet-grotesk"
curl -L -o "$SCRATCH/sw.zip" "https://api.fontshare.com/v2/fonts/download/switzer"
unzip -o "$SCRATCH/cg.zip" -d "$SCRATCH/cg" && unzip -o "$SCRATCH/sw.zip" -d "$SCRATCH/sw"
mkdir -p src/Coldstart.Web/wwwroot/fonts
find "$SCRATCH/cg" -name "CabinetGrotesk-Variable.woff2" -exec cp {} src/Coldstart.Web/wwwroot/fonts/ \;
find "$SCRATCH/sw" -name "Switzer-Variable.woff2" -exec cp {} src/Coldstart.Web/wwwroot/fonts/ \;
ls -la src/Coldstart.Web/wwwroot/fonts/
```

Expected: both `.woff2` files present (~40–90 KB each). If a filename differs inside the zip (e.g. lives under `Fonts/WEB/fonts/`), find any `*Variable*.woff2` there and rename to the exact target names above. Fontshare fonts are free for commercial use (ITF Free Font License) — no attribution required in the page.

- [ ] **Step 4: Prepend the token layer to site.css**

At the very top of `src/Coldstart.Web/wwwroot/css/site.css`, BEFORE the existing `:root`, add (existing rules stay untouched below — old pages keep working until their tasks rewrite them; Task 8 deletes the legacy layer):

```css
/* ===== Morning Engine tokens (2026 redesign) ===== */
@font-face {
  font-family: "Cabinet Grotesk";
  src: url("/fonts/CabinetGrotesk-Variable.woff2") format("woff2");
  font-weight: 100 1000;
  font-display: swap;
}
@font-face {
  font-family: "Switzer";
  src: url("/fonts/Switzer-Variable.woff2") format("woff2");
  font-weight: 100 1000;
  font-display: swap;
}
:root {
  --daylight: #FDFBF7;
  --ink: #16192B;
  --night: #1C1F33;
  --night-raised: #242841;
  --night-border: #33385A;
  --night-rule: #2C3049;
  --night-text: #E9E7F2;
  --night-muted: #9BA1C4;
  --night-faint: #7C82A6;
  --sunrise: #FF6A3D;
  --sunrise-deep: #E85A2E;
  --sunrise-soft: #FF8A5C;
  --body-muted: #5D5A50;
  --body-subtle: #8B8578;
  --line-soft: #E7E2D7;
  --display: "Cabinet Grotesk", "Helvetica Neue", -apple-system, sans-serif;
  --body: "Switzer", -apple-system, "Segoe UI", Roboto, Helvetica, sans-serif;
  --mono: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
}
.wordmark { font-family: var(--display); font-weight: 800; font-size: 1.25rem; letter-spacing: -0.01em; color: var(--ink); text-decoration: none; }
.wordmark i { font-style: normal; color: var(--sunrise); }
.wordmark--night { color: var(--night-text); }
.ts { font-family: var(--mono); font-variant-numeric: tabular-nums; }
/* ===== end Morning Engine tokens ===== */
```

Then, in the EXISTING rules further down, make these surgical replacements (find-and-replace, keep everything else):

```css
/* body: switch base look to Morning Engine */
body { margin: 0; background: var(--daylight); color: var(--ink); font-family: var(--body); font-size: 17px; line-height: 1.65; -webkit-font-smoothing: antialiased; }

/* buttons: replace the whole .btn block (lines ~79–86) with */
.btn { display: inline-flex; align-items: center; gap: 8px; font-weight: 600; font-size: 0.95rem; font-family: var(--body); padding: 12px 22px; border-radius: 9px; text-decoration: none; border: 1px solid transparent; cursor: pointer; transition: background .15s ease, border-color .15s ease; }
.btn-primary { background: var(--sunrise); color: #fff; }
.btn-primary:hover { background: var(--sunrise-deep); }
.btn-ghost { background: transparent; color: var(--ink); border-color: var(--line-soft); }
.btn-ghost:hover { border-color: var(--ink); }
.btn:focus-visible { outline: 3px solid var(--sunrise); outline-offset: 2px; }
.btn[disabled] { opacity: 0.5; cursor: not-allowed; }

/* fields: replace label + input styling (lines ~252–257) with */
.field { display: flex; flex-direction: column; gap: 6px; }
.field label { font-family: var(--body); font-size: 0.82rem; font-weight: 600; letter-spacing: 0; text-transform: none; color: var(--body-muted); }
.field input, .field textarea { font-family: var(--body); font-size: 1rem; padding: 11px 13px; border: 1px solid var(--line-soft); border-radius: 8px; background: #fff; color: var(--ink); width: 100%; }
.field textarea { resize: vertical; }
.field input:focus-visible, .field textarea:focus-visible { outline: 3px solid var(--sunrise); outline-offset: 1px; border-color: var(--sunrise); }
.field input[disabled], .field textarea[disabled] { opacity: 0.65; cursor: not-allowed; }
```

- [ ] **Step 5: Update `_Layout.cshtml` head and favicon**

Replace the `<head>` metadata in `src/Coldstart.Web/Pages/Shared/_Layout.cshtml` (keep the stylesheet link and `contact.js` script):

```html
<meta name="description" content="Coldstart finds companies that need what you sell — the morning it becomes true — and writes your first email to them. You read it over coffee and hit approve. Done-for-you, €750/month.">
<link rel="icon" href="~/favicon.svg" type="image/svg+xml">
<link rel="manifest" href="~/site.webmanifest">
<meta name="theme-color" content="#16192B">
<meta name="apple-mobile-web-app-title" content="Coldstart">
<meta property="og:title" content="Coldstart — wake up to your next client, already found">
<meta property="og:description" content="We watch the market overnight, pick the few companies that need you right now, and draft your opening email. You approve.">
<meta property="og:type" content="website">
<meta name="twitter:card" content="summary">
<link rel="preload" href="~/fonts/CabinetGrotesk-Variable.woff2" as="font" type="font/woff2" crossorigin>
<link rel="preload" href="~/fonts/Switzer-Variable.woff2" as="font" type="font/woff2" crossorigin>
```

Replace the content of `src/Coldstart.Web/wwwroot/favicon.svg` with:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" rx="14" fill="#16192B"/><text x="32" y="47" font-size="46" text-anchor="middle" fill="#FF6A3D" font-family="Helvetica, Arial, sans-serif" font-weight="700">*</text></svg>
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~BrandAssets"` → PASS.
Run: `dotnet test` → all green (nothing existing asserts on fonts/meta; `GetLanding_ReturnsOk_WithBrandAndCta` still passes because the landing page is unchanged so far).

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(web): Morning Engine design tokens, self-hosted fonts, new favicon and meta"
```

---

### Task 2: Landing page — the keynote arc

**Files:**
- Rewrite: `src/Coldstart.Web/Pages/Index.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (append landing layer; delete legacy landing blocks)
- Rewrite: `src/Coldstart.Web/wwwroot/js/contact.js`
- Modify: `tests/Coldstart.Web.Tests/PublicPagesTests.cs`

**Interfaces:**
- Consumes: Task 1 tokens, `.wordmark`, `.btn*`, `.field`, `.ts`.
- Produces: the inline sample-leads form posting to the existing `POST /Contact` handler (`ContactModel`: `Name`, `Email`, `Niche`, `WhatYouSell`, `Website` honeypot). `ContactTests` scrape the antiforgery token from `GET /` — the form MUST remain a `<form method="post" asp-page="/Contact">` on the landing page. Section id `#get-leads` is the CTA scroll target; `#morning` is the night-act anchor.

- [ ] **Step 1: Update the failing tests first**

In `tests/Coldstart.Web.Tests/PublicPagesTests.cs`, change `GetLanding_ReturnsOk_WithBrandAndCta` to assert the new copy, and add acts coverage:

```csharp
[Fact]
public async Task GetLanding_ReturnsOk_WithBrandAndCta()
{
    // Arrange
    var client = factory.CreateClient();

    // Act
    var html = await client.GetStringAsync("/");

    // Assert
    html.Should().Contain("coldstart");                        // wordmark
    html.Should().Contain("Get 3 sample leads");               // primary CTA
    html.Should().Contain("Wake up to your next client");      // hero promise
}

[Fact]
public async Task GetLanding_TellsTheMorningStory_AndPricing()
{
    // Arrange
    var client = factory.CreateClient();

    // Act
    var html = await client.GetStringAsync("/");

    // Assert
    html.Should().Contain("While you sleep");                  // night act
    html.Should().Contain("06:58");                            // timestamped story
    html.Should().Contain("€750");                             // transparent pricing
    html.Should().Contain("€350");                             // setup fee
    html.Should().Contain("doing the part AI is actually good at");   // AI section (static markup is emitted verbatim — avoid asserting on apostrophes)
}
```

Keep `GetLanding_HasHeroLoginLink_NotOnlyInStatusbar` as is (the new hero keeps `class="… hero-login"` on the login link and the `/Identity/Account/Login` href).

- [ ] **Step 2: Run to verify the changed tests fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~PublicPages"`
Expected: the two landing tests FAIL (old page has old copy); privacy test still passes.

- [ ] **Step 3: Rewrite `Pages/Index.cshtml`**

Full replacement (complete file):

```html
@page
@model IndexModel
@{
    ViewData["Title"] = "Coldstart — wake up to your next client, already found";
    Layout = "_Layout";
}

<main class="lp">

<!-- Act 0 · Hero (daylight) -->
<header class="lp-hero">
  <div class="wrap">
    <nav class="lp-nav">
      <a class="wordmark" href="/" aria-label="Coldstart home">coldstart<i>*</i></a>
      @if (User.Identity?.IsAuthenticated == true)
      {
          <a class="lp-login hero-login" href="/app">Customer zone →</a>
      }
      else
      {
          <a class="lp-login hero-login" href="/Identity/Account/Login">Log in</a>
      }
    </nav>
    <h1>Wake up to your next client, already found — with the first email already written.</h1>
    <p class="lp-lede">For companies that sell to other businesses. We watch the market overnight, pick the few companies that need you right now, and draft your opening email. You read it over coffee and hit approve.</p>
    <div class="lp-cta-row">
      <a class="btn btn-primary" href="#get-leads">Get 3 sample leads — free</a>
      <a class="lp-quiet" href="#enemy">See one morning ↓</a>
    </div>
  </div>
</header>

<!-- Act 1 · The enemy (daylight) -->
<section class="lp-act" id="enemy">
  <div class="wrap wrap-narrow" data-reveal>
    <h2>You already know what bad outreach looks like.<br>It's in your inbox right now.</h2>
    <blockquote class="lp-spam">
      <p>"I hope this email finds you well. I came across your company and was impressed…"</p>
      <footer>— sent to 5,000 people from a bought list. Answered by none.</footer>
    </blockquote>
    <p>You delete these every day. So does everyone you'd ever want as a client. That's why "finding new clients" feels dirty — and why it keeps sliding to the bottom of your list while somewhere out there, right now, a company needs exactly what you sell.</p>
  </div>
</section>

<!-- Act 2 · The belief (daylight) -->
<section class="lp-act lp-act-belief">
  <div class="wrap wrap-narrow" data-reveal>
    <h2>There is a right way to do this.</h2>
    <ul class="lp-beliefs">
      <li><strong>Write to a few companies, not thousands.</strong> Three good conversations beat three thousand deletions.</li>
      <li><strong>Write the morning something happens to them.</strong> New funding, a first sales hire, a tool they've outgrown — the moment their need is real and their inbox isn't full of pitches yet.</li>
      <li><strong>Write about them, not about you.</strong> The first sentence should prove you actually read their news.</li>
      <li><strong>Say no to almost everyone.</strong> Out of 77 companies we looked at one recent morning, we rejected 74.</li>
    </ul>
  </div>
</section>

<!-- Act 3 · While you sleep (night) -->
<section class="lp-night" id="morning">
  <div class="wrap wrap-narrow">
    <p class="lp-night-label ts" data-reveal>WHILE YOU SLEEP</p>
    <div class="lp-timeline">
      <div class="lp-moment" data-reveal>
        <p class="lp-time ts">06:58</p>
        <p>Patronus AI announces a $50M round amid "nearly insatiable demand." Their news is minutes old.</p>
      </div>
      <div class="lp-moment" data-reveal>
        <p class="lp-time ts">06:59</p>
        <p>We check them against what <em>you</em> sell. Most companies fail this check — a competitor, a job seeker, plain noise — and are rejected. This one fits.</p>
      </div>
      <div class="lp-moment" data-reveal>
        <p class="lp-time ts">07:04</p>
        <p>Your opening email is drafted — four sentences, starting from <em>their</em> news, in your voice:</p>
        <div class="lp-draft">
          <p class="lp-draft-subj">Subject: Scaling headcount with AI efficiency</p>
          <p>Patronus AI's nearly insatiable demand is a strong indicator the company is about to scale headcount, which can strain HR teams. AI-screened candidate shortlists can cut time-to-hire by 40%, letting HR focus on more strategic tasks. As you invest in new tooling, how are you planning to optimize hiring to keep up with demand?</p>
          <footer class="ts">real draft · unedited · written for a sample client selling recruiting software</footer>
        </div>
      </div>
    </div>
  </div>
</section>

<!-- Act 4 · Sunrise reveal (dawn gradient) -->
<section class="lp-sunrise">
  <div class="wrap" data-reveal>
    <p class="lp-time ts">07:30</p>
    <h2>It's in your inbox.</h2>
    <p class="lp-sunrise-sub">All of this already runs, every morning. It's called Coldstart.</p>
  </div>
</section>

<!-- Act 5 · How it works & the AI (daylight) -->
<section class="lp-act">
  <div class="wrap wrap-narrow" data-reveal>
    <h2>Yes, it's AI — doing the part AI is actually good at.</h2>
    <p>Overnight, the engine reads thousands of public pages — job posts, funding news, company announcements. Nothing private, nothing scraped from behind a login. A strict filter throws out everything that doesn't genuinely fit what you sell. Only then does AI write: a short email built on that company's real, hours-old news. It never invents facts and it never guesses email addresses — each lead comes with links to find the real decision-maker.</p>
    <p><strong>And the part AI must never do stays yours:</strong> nothing is ever sent without you reading and approving it. Every email that goes out is one you chose to send.</p>
    <p class="lp-punch">That's the whole trick — and it's why this costs €750 a month instead of a €4,000-a-month salary.</p>
  </div>
</section>

<!-- Act 6 · Transparent pricing (daylight) -->
<section class="lp-act lp-pricing-act">
  <div class="wrap wrap-narrow" data-reveal>
    <h2>What it costs. All of it.</h2>
    <div class="lp-pricing">
      <div class="lp-price"><span class="lp-amt">€750</span><span class="lp-per">per month · ex VAT</span></div>
      <p class="lp-setup">+ €350 one-time setup · ex VAT — we configure the engine to your exact niche and offer, by hand.</p>
      <ul class="lp-included">
        <li>Fresh leads found for you every morning</li>
        <li>Each one checked against what you sell — most get rejected before you see them</li>
        <li>A ready-to-send opening email drafted for every lead</li>
        <li>You approve and send — nothing goes out without you</li>
        <li>You own every lead and every draft — export anytime</li>
      </ul>
      <p class="lp-terms-line">Billed monthly. Cancel anytime — no notice period, no lock-in. We take on only a handful of new niches each month.</p>
    </div>
  </div>
</section>

<!-- Act 7 · Closing CTA — sample leads form -->
<section class="lp-act lp-form-act" id="get-leads">
  <div class="wrap wrap-narrow" data-reveal>
    <h2>See your own morning first — free.</h2>
    <p>Tell us what you sell. We'll run the engine for your niche and send you 3 real leads — with drafts — before you pay anything.</p>
    <form class="lp-form" id="sample-form" method="post" asp-page="/Contact">
      <input type="text" name="Website" class="hp" tabindex="-1" autocomplete="off" aria-hidden="true">
      <div class="field"><label for="f-name">Your name</label><input id="f-name" name="Name" type="text" required></div>
      <div class="field"><label for="f-email">Your email</label><input id="f-email" name="Email" type="email" required></div>
      <div class="field"><label for="f-niche">Who your customers are</label><input id="f-niche" name="Niche" type="text" placeholder="e.g. dental clinics in the Benelux"></div>
      <div class="field"><label for="f-sell">What you sell them</label><input id="f-sell" name="WhatYouSell" type="text" placeholder="e.g. scheduling software"></div>
      <p class="lp-form-status" id="form-status" role="status" aria-live="polite"></p>
      <button class="btn btn-primary" type="submit">Send me 3 sample leads →</button>
    </form>
  </div>
</section>

</main>
```

- [ ] **Step 4: Append the landing CSS layer and delete legacy landing blocks**

Append to `site.css` (after the token layer is a fine location):

```css
/* ===== Landing page (Morning Engine) ===== */
.lp h1, .lp h2 { font-family: var(--display); letter-spacing: -0.025em; color: var(--ink); line-height: 1.12; }
.lp h1 { font-size: clamp(2.1rem, 5.4vw, 3.4rem); font-weight: 800; max-width: 20ch; margin: 0 0 22px; }
.lp h2 { font-size: clamp(1.5rem, 3.4vw, 2.2rem); font-weight: 800; margin: 0 0 20px; }
.lp p { color: var(--body-muted); }
.wrap { max-width: 1080px; margin: 0 auto; padding: 0 clamp(20px, 5vw, 56px); }
.wrap-narrow { max-width: 720px; }
.lp-hero { padding: 28px 0 88px; background: var(--daylight); }
.lp-nav { display: flex; justify-content: space-between; align-items: center; margin-bottom: 72px; }
.lp-login { font-size: 0.9rem; color: var(--body-muted); text-decoration: none; }
.lp-login:hover { color: var(--ink); }
.lp-lede { font-size: 1.12rem; line-height: 1.6; max-width: 52ch; margin: 0 0 30px; }
.lp-cta-row { display: flex; gap: 20px; align-items: center; flex-wrap: wrap; }
.lp-quiet { color: var(--body-subtle); text-decoration: none; font-size: 0.95rem; }
.lp-quiet:hover { color: var(--ink); }
.lp-act { padding: 88px 0; }
.lp-spam { margin: 28px 0; padding: 22px 26px; border-left: 3px solid var(--line-soft); background: #fff; border-radius: 0 10px 10px 0; }
.lp-spam p { font-style: italic; margin: 0 0 10px; }
.lp-spam footer { font-size: 0.9rem; color: var(--body-subtle); }
.lp-beliefs { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 20px; }
.lp-beliefs li { padding-left: 22px; border-left: 3px solid var(--sunrise); line-height: 1.6; color: var(--body-muted); }
.lp-beliefs strong { color: var(--ink); display: block; }
.lp-night { background: var(--night); padding: 96px 0; }
.lp-night-label { font-size: 0.72rem; letter-spacing: 0.18em; color: var(--night-faint); margin: 0 0 40px; }
.lp-timeline { border-left: 2px solid var(--night-rule); padding-left: 26px; display: flex; flex-direction: column; gap: 34px; }
.lp-moment p { color: var(--night-text); margin: 0; line-height: 1.6; }
.lp-time { color: var(--sunrise-soft); font-size: 0.85rem; margin: 0 0 6px; }
.lp-draft { margin-top: 16px; background: var(--night-raised); border: 1px solid var(--night-border); border-radius: 10px; padding: 18px 20px; }
.lp-draft-subj { color: var(--night-muted); font-size: 0.9rem; margin: 0 0 8px; }
.lp-draft p { color: #CBD0E8; font-size: 0.95rem; }
.lp-draft footer { margin-top: 12px; font-size: 0.72rem; color: var(--night-faint); }
.lp-sunrise { background: linear-gradient(to bottom, #1C1F33 0%, #54425E 30%, #C96F4A 62%, #F5C97B 82%, #FDFBF7 100%); padding: 130px 0 60px; text-align: center; }
.lp-sunrise h2 { color: #FFFDF9; text-shadow: 0 1px 24px rgba(60,30,50,.35); font-size: clamp(1.9rem, 4.5vw, 2.8rem); }
.lp-sunrise .lp-time { color: #FFE9D2; }
.lp-sunrise-sub { color: #4A4237; font-weight: 500; margin-top: 14px; }
.lp-punch { font-weight: 600; color: var(--ink); }
.lp-pricing { background: #fff; border: 1px solid var(--line-soft); border-radius: 14px; padding: 30px 34px; }
.lp-price { display: flex; align-items: baseline; gap: 12px; }
.lp-amt { font-family: var(--display); font-weight: 800; font-size: 2.6rem; color: var(--ink); }
.lp-per { color: var(--body-subtle); }
.lp-setup { margin: 6px 0 18px; font-size: 0.95rem; }
.lp-included { list-style: none; padding: 0; margin: 0 0 18px; display: flex; flex-direction: column; gap: 9px; }
.lp-included li { padding-left: 26px; position: relative; color: var(--body-muted); }
.lp-included li::before { content: "*"; position: absolute; left: 4px; top: 1px; color: var(--sunrise); font-weight: 800; font-family: var(--display); }
.lp-terms-line { font-size: 0.88rem; color: var(--body-subtle); margin: 0; }
.lp-form { display: flex; flex-direction: column; gap: 16px; max-width: 440px; margin-top: 24px; }
.lp-form-status { min-height: 1.2em; margin: 0; font-size: 0.92rem; }
.lp-form-status.ok { color: #2E7D4F; }
.lp-form-status.err { color: #B3402A; }
.hp { position: absolute !important; left: -9999px !important; }
[data-reveal] { opacity: 0; transform: translateY(14px); transition: opacity .6s ease, transform .6s ease; }
[data-reveal].is-visible { opacity: 1; transform: none; }
@media (prefers-reduced-motion: reduce) {
  [data-reveal] { opacity: 1; transform: none; transition: none; }
}
@media (max-width: 640px) {
  .lp-act, .lp-night { padding: 64px 0; }
  .lp-pricing { padding: 22px; }
}
/* ===== end landing ===== */
```

Delete from `site.css` the now-unused legacy landing blocks: `.rainbow`, `.statusbar` (and `.sb-*`, `@keyframes pulse`), `.caret`/`@keyframes blink`, `.hero`, `.hero-grid`, `.device*`, `.msg*`, `.stats`/`.stat`, `.steps`/`.step`, `.band`, `.leads`/`.lead*`, `.win*`, `.why*`, `.offer*`, `.scarcity`, `.price-*`, `.overlay`/`.modal*`, `.preview`, `.urgency`, `.eyebrow`. KEEP: `.field`, `.btn*`, `.auth*`, `.app-*`, `.fit*`, `.chip`, `.score`, `.lead-modal*`, `.mono` if referenced by non-landing pages (grep each class name across `src/Coldstart.Web` before deleting: `grep -rn "classname" src/Coldstart.Web --include="*.cshtml" --include="*.js"`).

- [ ] **Step 5: Rewrite `wwwroot/js/contact.js`**

Full replacement — scroll reveals + async form submit (the old file's modal/statusbar/demo-screen code goes away):

```js
// Landing page behavior: scroll reveals + sample-leads form submit.
(function () {
  "use strict";

  // Reveal-on-scroll (no-op when reduced motion is preferred — CSS shows everything)
  var reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var revealed = document.querySelectorAll("[data-reveal]");
  if (reduced || !("IntersectionObserver" in window)) {
    revealed.forEach(function (el) { el.classList.add("is-visible"); });
  } else {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (e.isIntersecting) { e.target.classList.add("is-visible"); io.unobserve(e.target); }
      });
    }, { threshold: 0.15 });
    revealed.forEach(function (el) { io.observe(el); });
  }

  // Sample-leads form: post without navigation, report inline.
  var form = document.getElementById("sample-form");
  var status = document.getElementById("form-status");
  if (!form || !status) return;
  form.addEventListener("submit", function (ev) {
    ev.preventDefault();
    var btn = form.querySelector("button[type=submit]");
    btn.disabled = true;
    status.className = "lp-form-status";
    status.textContent = "Sending…";
    fetch(form.action, { method: "POST", body: new FormData(form) })
      .then(function (res) {
        if (!res.ok) throw new Error("bad status " + res.status);
        status.className = "lp-form-status ok";
        status.textContent = "Done — your 3 sample leads are being prepared. Check your inbox in a day or two.";
        form.reset();
      })
      .catch(function () {
        status.className = "lp-form-status err";
        status.textContent = "That didn't go through. Check your name and email, or write to us directly.";
        btn.disabled = false;
      });
  });
})();
```

Note: `FormData` of the Razor form includes the antiforgery hidden input automatically, so `POST /Contact` keeps passing antiforgery validation — same mechanism `ContactTests` relies on.

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~PublicPages|FullyQualifiedName~Contact"`
Expected: PASS. If the `"Yes, it&#x2019;s AI"` assertion fails on encoding, curl the real output (`dotnet run` + `curl -s localhost:PORT/ | grep -o "Yes, it.....AI"`) and pin the assertion to the actual encoded form.
Then: `dotnet test` → all green.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(web): rewrite landing page as Morning Engine keynote narrative"
```

---

### Task 3: Footer and legal pages

**Files:**
- Create: `src/Coldstart.Web/Pages/Shared/_Footer.cshtml`
- Modify: `src/Coldstart.Web/Pages/Shared/_Layout.cshtml` (render footer)
- Create: `src/Coldstart.Web/Pages/Terms.cshtml`
- Rewrite: `src/Coldstart.Web/Pages/Privacy.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (footer + legal layer)
- Test: `tests/Coldstart.Web.Tests/PublicPagesTests.cs`

**Interfaces:**
- Consumes: Task 1 tokens and `.wordmark`.
- Produces: footer partial on every page; routes `/terms` and `/privacy`.

- [ ] **Step 1: Write the failing tests**

Add to `PublicPagesTests.cs`:

```csharp
[Fact]
public async Task GetTerms_ReturnsOk_WithDraftBanner()
{
    // Arrange
    var client = factory.CreateClient();

    // Act
    var html = await client.GetStringAsync("/terms");

    // Assert
    html.Should().Contain("Terms");
    html.Should().Contain("pending review");
    html.Should().Contain("Netherlands");
}

[Fact]
public async Task GetPrivacy_CoversGdprEssentials()
{
    // Arrange
    var client = factory.CreateClient();

    // Act
    var html = await client.GetStringAsync("/privacy");

    // Assert
    html.Should().Contain("Autoriteit Persoonsgegevens");
    html.Should().Contain("Resend");
    html.Should().Contain("Hetzner");
    html.Should().Contain("Schiedam");
}

[Fact]
public async Task Footer_AppearsOnPublicPages_WithRegistryPlaceholders()
{
    // Arrange
    var client = factory.CreateClient();

    // Act
    var landing = await client.GetStringAsync("/");
    var privacy = await client.GetStringAsync("/privacy");

    // Assert
    landing.Should().Contain("KvK");
    privacy.Should().Contain("KvK");
    landing.Should().Contain("/terms");
    landing.Should().Contain("/privacy");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~PublicPages"`
Expected: 3 new tests FAIL (`/terms` 404, no footer, privacy missing GDPR markers).

- [ ] **Step 3: Create the footer partial and render it**

`src/Coldstart.Web/Pages/Shared/_Footer.cshtml`:

```html
<footer class="site-footer">
  <div class="wrap">
    <div class="site-footer-grid">
      <div>
        <a class="wordmark" href="/">coldstart<i>*</i></a>
        <p class="site-footer-co">Coldstart · Schiedam, the Netherlands<br>KvK: — · VAT: NL005516039B31</p>
      </div>
      <nav class="site-footer-nav" aria-label="Legal">
        <a href="/terms">Terms &amp; conditions</a>
        <a href="/privacy">Privacy</a>
        <a href="mailto:adzius.lech@gmail.com">Contact</a>
      </nav>
    </div>
    <p class="site-footer-note">© @DateTime.UtcNow.Year Coldstart. All leads and drafts shown on this site come from real, unedited runs.</p>
  </div>
</footer>
```

(The `KvK: — · VAT: —` placeholders are replaced with real numbers once Andrzej provides them — memory note exists for this.)

In `_Layout.cshtml`, after `@RenderBody()`:

```html
<partial name="_Footer" />
```

Footer CSS (append to `site.css`):

```css
/* ===== Footer + legal ===== */
.site-footer { border-top: 1px solid var(--line-soft); background: var(--daylight); padding: 44px 0 30px; margin-top: 0; }
.site-footer-grid { display: flex; justify-content: space-between; gap: 28px; flex-wrap: wrap; }
.site-footer-co { font-size: 0.88rem; color: var(--body-subtle); line-height: 1.6; margin: 10px 0 0; }
.site-footer-nav { display: flex; gap: 22px; align-items: flex-start; flex-wrap: wrap; }
.site-footer-nav a { color: var(--body-muted); font-size: 0.9rem; text-decoration: none; }
.site-footer-nav a:hover { color: var(--ink); }
.site-footer-note { margin: 26px 0 0; font-size: 0.78rem; color: var(--body-subtle); }
.legal { padding: 56px 0 72px; }
.legal h1 { font-family: var(--display); font-weight: 800; letter-spacing: -0.02em; font-size: clamp(1.8rem, 4vw, 2.5rem); }
.legal h2 { font-family: var(--display); font-weight: 700; font-size: 1.25rem; margin: 34px 0 10px; }
.legal p, .legal li { color: var(--body-muted); line-height: 1.65; }
.legal-banner { display: inline-block; background: #FFF3EC; border: 1px solid #F5CDB8; color: #A04E1E; border-radius: 8px; padding: 8px 14px; font-size: 0.88rem; margin: 14px 0 6px; }
/* ===== end footer + legal ===== */
```

- [ ] **Step 4: Create `/terms` and rewrite `/privacy`**

`src/Coldstart.Web/Pages/Terms.cshtml` (no PageModel needed):

```html
@page "/terms"
@{
    ViewData["Title"] = "Terms & conditions — Coldstart";
    Layout = "/Pages/Shared/_Layout.cshtml";
}
<main class="legal">
  <div class="wrap wrap-narrow">
    <h1>Terms &amp; conditions</h1>
    <p class="legal-banner">Draft — pending review, not legal advice. These terms are a starting point while Coldstart finalizes its legal details.</p>

    <h2>1. Who we are</h2>
    <p>Coldstart ("we") is a business-to-business service based in Schiedam, the Netherlands. KvK: —. VAT: —. Contact: adzius.lech@gmail.com.</p>

    <h2>2. The service</h2>
    <p>Coldstart is a done-for-you lead service: we monitor publicly available sources, select companies that plausibly need what you sell, and draft opening emails for you. You review every draft; nothing is sent without your explicit approval. We provide leads and drafts — we do not guarantee replies, meetings, or revenue.</p>

    <h2>3. Fees and billing</h2>
    <p>The service costs €750 per month plus a one-time setup fee of €350, both excluding VAT. Billing is monthly in advance. You can cancel at any time; cancellation takes effect at the end of the paid month. There is no minimum term and no notice period.</p>

    <h2>4. Your data and your leads</h2>
    <p>Every lead, draft, and result produced for your account belongs to you. You can export them at any time, including after cancellation (on request, up to 30 days after your account ends).</p>

    <h2>5. Acceptable use</h2>
    <p>You agree to use the drafts and leads for lawful business-to-business outreach only, and to comply with applicable email and privacy law in your market (including the GDPR and, in the Netherlands, the Telecommunicatiewet). You send emails from your own address and are the sender in the legal sense.</p>

    <h2>6. Liability</h2>
    <p>We work with public data and automated judgment; occasional errors (a mismatched lead, an imperfect draft) are part of the service's nature. Our total liability under these terms is limited to the fees you paid us in the three months before the event giving rise to the claim. We are not liable for indirect damage, including lost profit.</p>

    <h2>7. Privacy and processing</h2>
    <p>How we handle personal data is described in our <a href="/privacy">privacy policy</a>. Where we process personal data on your behalf, we do so under these terms as your processor.</p>

    <h2>8. Changes</h2>
    <p>We may update these terms; we will announce material changes by email at least 30 days in advance. If you don't agree with a change, you can cancel before it takes effect.</p>

    <h2>9. Governing law</h2>
    <p>These terms are governed by Dutch law. Disputes go to the competent court in Rotterdam, the Netherlands.</p>
  </div>
</main>
```

`src/Coldstart.Web/Pages/Privacy.cshtml` (full replacement, keeps `@model PrivacyModel`):

```html
@page
@model PrivacyModel
@{
    ViewData["Title"] = "Privacy — Coldstart";
    Layout = "_Layout";
}
<main class="legal">
  <div class="wrap wrap-narrow">
    <h1>Privacy policy</h1>
    <p class="legal-banner">Draft — pending review, not legal advice. This is a starter policy while Coldstart finalizes its legal details.</p>

    <h2>Who we are</h2>
    <p>Coldstart is based in Schiedam, the Netherlands, and is the controller for the data described here. KvK: —. VAT: —. Contact: adzius.lech@gmail.com.</p>

    <h2>What we collect, and why</h2>
    <ul>
      <li><strong>Contact-form inquiries</strong> — name, email, your niche, what you sell, and an optional message. Legal basis: our legitimate interest in answering you (art. 6(1)(f) GDPR).</li>
      <li><strong>Account data</strong> — your email address, company profile details you add (address, VAT, KvK, niche, website), and authentication cookies that keep you signed in. Legal basis: performing our contract with you (art. 6(1)(b)).</li>
      <li><strong>Lead data</strong> — company names and publicly available facts about companies, collected from public sources (job boards, funding announcements, public feeds). Where a lead involves a person's business contact details, we process them under our and our customers' legitimate interest in business-to-business contact (art. 6(1)(f)).</li>
    </ul>

    <h2>Who processes it for us</h2>
    <ul>
      <li><strong>Resend</strong> — sends our transactional email.</li>
      <li><strong>Hetzner</strong> — hosts our servers in the EU.</li>
    </ul>
    <p>We don't sell data, and we don't use it for advertising.</p>

    <h2>How long we keep it</h2>
    <p>Contact inquiries: up to 12 months after our last exchange. Account and lead data: for as long as your account exists, then deleted within 30 days of account closure. You can request deletion earlier at any time.</p>

    <h2>Cookies</h2>
    <p>We use only functional cookies: the session cookie that keeps you signed in and the security (anti-forgery) cookie. No tracking, no analytics cookies, no third-party advertising.</p>

    <h2>Your rights</h2>
    <p>You can ask us for access to, correction of, deletion of, or a copy (portability) of your data, and you can object to processing based on legitimate interest. Email adzius.lech@gmail.com and we'll respond within one month. You also have the right to complain to the Dutch supervisory authority, the <strong>Autoriteit Persoonsgegevens</strong> (autoriteitpersoonsgegevens.nl).</p>
  </div>
</main>
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~PublicPages"` → PASS (including the pre-existing `GetPrivacy_ReturnsOk_WithPolicyMarkers`, which asserts `"Privacy"` and `"Schiedam"` — both still present).
Then `dotnet test` → all green (footer adds `KvK`/links to every page including /app — no existing assertion conflicts).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(web): site footer with registry placeholders, /terms page, GDPR privacy policy"
```

---

### Task 4: Customer zone — briefing header, renamed tabs, draft-first cards

**Files:**
- Modify: `src/Coldstart.Application/Leads/LeadSummary.cs` (add `CreatedAt`)
- Modify: `src/Coldstart.Application/Leads/ListLeads.cs` (project `l.CreatedAt`)
- Rewrite: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml` (labels + wordmark only)
- Modify: `src/Coldstart.Web/wwwroot/js/app-leads.js` (card Approve/Skip buttons; explainer dismiss)
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (app layer restyle)
- Tests: `tests/Coldstart.Web.Tests/LeadReviewTests.cs`, `tests/Coldstart.Web.Tests/CustomerZoneTests.cs`, plus any `Coldstart.Application.Tests` touching `LeadSummary`

**Interfaces:**
- Consumes: Task 1 tokens; existing `POST /app?handler=Move` endpoint (form fields `id`, `tab`); existing `LeadSummary` fields `Id, Company, Fit, Status, RecipientEmail, EmailConfidence, Subject, Draft, Source, Score`.
- Produces: `LeadSummary` gains `DateTimeOffset CreatedAt` as the LAST constructor parameter (positional record — appending last avoids breaking positional construction order elsewhere; update all construction sites). Tab VISIBLE labels become "To review" / "Approved" / "Skipped"; slugs, hrefs, `data-tab`, `data-bucket` unchanged.

- [ ] **Step 1: Update the failing tests first**

In `LeadReviewTests.cs`, update every tab-markup literal — the pattern is mechanical, label text changes inside otherwise identical strings:

- `>New <span class="app-tab-count">` → `>To review <span class="app-tab-count">`
- `>Accepted <span class="app-tab-count">` → `>Approved <span class="app-tab-count">`
- `>Discarded <span class="app-tab-count">` → `>Skipped <span class="app-tab-count">`
- `"Auto-skipped"` → `"Skipped automatically"`

In `CustomerZoneTests.cs`:
- `"No leads yet"` → `"Nothing to review yet"`
- `"Add your company details"` stays (profile prompt copy is kept).

Add one new test to `CustomerZoneTests.cs`:

```csharp
[Fact]
public async Task App_ShowsBriefingHeader_WithOvernightCount()
{
    // Arrange — reuse this class's existing helper that seeds a customer with leads
    // (mirror the arrange of App_ShowsRealLeadsForTheLoggedInUsersCustomer)

    // Act
    var html = await client.GetStringAsync("/app");

    // Assert
    html.Should().Contain("Good morning");
    html.Should().Contain("found overnight");
}
```

(Adapt the Arrange to the file's existing seeding helpers — copy the exact setup from `App_ShowsRealLeadsForTheLoggedInUsersCustomer`.)

- [ ] **Step 2: Run to verify failures**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~LeadReview|FullyQualifiedName~CustomerZone"`
Expected: FAIL on all updated literals + the new briefing test.

- [ ] **Step 3: Add `CreatedAt` to the read model**

`LeadSummary.cs` — append the parameter:

```csharp
public sealed record LeadSummary(
    Guid Id,
    string Company,
    string Fit,
    string Status,
    string? RecipientEmail,
    string EmailConfidence,
    string Subject,
    string Draft,
    string Source,
    double Score,
    DateTimeOffset CreatedAt);
```

`ListLeads.cs` — add `l.CreatedAt` as the last argument of the projection. Then `grep -rn "new LeadSummary(" src tests` and add a `CreatedAt` argument at every construction site (tests may build summaries directly).

- [ ] **Step 4: Rewrite `Areas/App/Pages/Index.cshtml`**

Full replacement:

```html
@page "/app"
@model Coldstart.Web.Areas.App.Pages.IndexModel
@{
    ViewData["Title"] = "Your morning briefing";
    Layout = "/Pages/Shared/_Layout.cshtml";
    var overnight = Model.New.Count;
    var latest = Model.New.Count > 0 ? Model.New.Max(l => l.CreatedAt) : (DateTimeOffset?)null;
}

<header class="app-bar">
  <a class="wordmark" href="/">coldstart<i>*</i></a>
  <nav class="app-nav">
    <a href="/app/profile">Company profile</a>
    <form method="post" asp-page="/Account/Logout" asp-area="Identity"><button class="btn btn-ghost" type="submit">Log out</button></form>
  </nav>
</header>

<main class="wrap app-zone">
  @Html.AntiForgeryToken()

  <section class="app-briefing">
    @if (overnight > 0)
    {
      <h1>Good morning — @overnight new @(overnight == 1 ? "lead" : "leads") found overnight</h1>
      @if (latest is not null) { <p class="app-briefing-ts ts">latest found @latest.Value.ToString("HH:mm") UTC</p> }
    }
    else
    {
      <h1>Good morning — no new leads this morning</h1>
      <p class="app-briefing-ts">The engine runs again tonight.</p>
    }
  </section>

  <aside class="app-explainer" id="app-explainer" hidden>
    <p><strong>How this works:</strong> we find leads overnight → you review and approve → the email goes out. Nothing is ever sent without you.</p>
    <button type="button" class="app-explainer-x" id="app-explainer-dismiss" aria-label="Dismiss">Got it</button>
  </aside>

  <section class="app-profile">
    @if (Model.Profile.IsEmpty)
    {
      <div class="app-profile-empty">
        <div><strong>Add your company details</strong><p>Address, VAT, KvK, niche — shown on your account.</p></div>
        <a class="btn btn-primary" href="/app/profile">Add details →</a>
      </div>
    }
    else
    {
      <div class="app-profile-card">
        <h2>@(string.IsNullOrWhiteSpace(Model.Profile.CompanyName) ? Model.CustomerName : Model.Profile.CompanyName)</h2>
        <dl>
          @if (!string.IsNullOrWhiteSpace(Model.Profile.Address)) { <div><dt>Address</dt><dd>@Model.Profile.Address</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.VatNumber)) { <div><dt>VAT</dt><dd>@Model.Profile.VatNumber</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.KvkNumber)) { <div><dt>KvK</dt><dd>@Model.Profile.KvkNumber</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.Niche)) { <div><dt>Niche</dt><dd>@Model.Profile.Niche</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.Website)) { <div><dt>Website</dt><dd>@Model.Profile.Website</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.ContactEmail)) { <div><dt>Contact</dt><dd>@Model.Profile.ContactEmail</dd></div> }
        </dl>
        <a class="app-edit-link" href="/app/profile">Edit</a>
      </div>
    }
  </section>

  <p class="app-sub">@Model.CustomerName</p>

  <nav class="app-tabs" aria-label="Lead tabs">
    <a class="app-tab @(Model.Tab == "new" ? "is-active" : "")" data-tab="new" href="/app?tab=new">To review <span class="app-tab-count">@Model.NewCount</span></a>
    <a class="app-tab @(Model.Tab == "accepted" ? "is-active" : "")" data-tab="accepted" href="/app?tab=accepted">Approved <span class="app-tab-count">@Model.AcceptedCount</span></a>
    <a class="app-tab @(Model.Tab == "discarded" ? "is-active" : "")" data-tab="discarded" href="/app?tab=discarded">Skipped <span class="app-tab-count">@Model.DiscardedCount</span></a>
  </nav>

  @{
    void Card(Coldstart.Application.Leads.LeadSummary lead, bool draggable, bool actionable)
    {
      <article class="app-lead" data-lead-id="@lead.Id" draggable="@(draggable ? "true" : "false")">
        <div class="app-lead-top">
          <h3><a href="/app/leads/@lead.Id">@lead.Company</a><span class="go" aria-hidden="true">→</span></h3>
          <span class="fit fit-@lead.Fit.ToLower()">@lead.Fit</span>
        </div>
        <p class="app-lead-subj">@lead.Subject</p>
        @if (!string.IsNullOrWhiteSpace(lead.Draft) && lead.Draft != "-")
        {
          <p class="app-lead-draft">@(lead.Draft.Length > 220 ? lead.Draft[..220] + "…" : lead.Draft)</p>
        }
        <div class="app-lead-meta"><span class="chip">@lead.Source</span><span class="ts app-lead-ts">@lead.CreatedAt.ToString("dd MMM · HH:mm")</span></div>
        @if (actionable)
        {
          <div class="app-lead-actions">
            <button type="button" class="btn btn-primary app-act-approve" data-move="accepted">Approve</button>
            <button type="button" class="btn btn-ghost app-act-skip" data-move="discarded">Skip</button>
          </div>
        }
      </article>
    }
    void Bucket(string bucket, IReadOnlyList<Coldstart.Application.Leads.LeadSummary> leads, bool draggable, bool actionable, string emptyText)
    {
      <div class="app-leads" data-bucket="@bucket" data-count="@leads.Count">
        @foreach (var l in leads) { Card(l, draggable, actionable); }
        <p class="app-group-empty">@emptyText</p>
      </div>
    }
  }

  <section class="app-panel @(Model.Tab == "new" ? "is-active" : "")" data-panel="new">
    @{ Bucket("new", Model.New, true, true, "Nothing to review yet — the engine hunts again tonight."); }
  </section>

  <section class="app-panel @(Model.Tab == "accepted" ? "is-active" : "")" data-panel="accepted">
    <h2 class="app-group-head">Ready to send</h2>
    @{ Bucket("ready", Model.AcceptedReady, true, false, "Nothing approved yet — approve a lead and it appears here."); }
    <h2 class="app-group-head">Sent</h2>
    @{ Bucket("sent", Model.Sent, false, false, "Nothing sent yet."); }
  </section>

  <section class="app-panel @(Model.Tab == "discarded" ? "is-active" : "")" data-panel="discarded">
    <h2 class="app-group-head">You skipped</h2>
    @{ Bucket("shelved", Model.Shelved, true, false, "You haven't skipped anything."); }
    <h2 class="app-group-head">Skipped automatically — not a fit</h2>
    @{ Bucket("autoskip", Model.AutoSkipped, true, false, "Nothing was skipped automatically."); }
  </section>

  <dialog id="move-error" class="lead-modal" aria-labelledby="move-error-title">
    <form method="dialog" class="lead-modal-card">
      <p class="lead-modal-eyebrow">Move blocked</p>
      <h2 class="lead-modal-title" id="move-error-title">Couldn't move that lead</h2>
      <p class="lead-modal-detail" id="move-error-detail"></p>
      <div class="lead-modal-actions"><button class="btn btn-primary" value="ok" autofocus>Got it</button></div>
    </form>
  </dialog>
</main>
<script src="~/js/app-leads.js" defer></script>
```

- [ ] **Step 5: Extend `app-leads.js` and restyle the app CSS layer**

In `app-leads.js`, the `POST /app?handler=Move` fetch is currently INLINE in the `.app-tab` drop handler (near the bottom of the file's IIFE). First extract it into a helper inside the same IIFE, so drag-and-drop and the new card buttons share one code path:

```js
// Extracted from the drop handler — one shared move path.
function requestMove(id, target) {
  var card = document.querySelector('.app-lead[data-lead-id="' + id + '"]');
  var body = new URLSearchParams();
  body.set('id', id);
  body.set('tab', target);
  fetch('/app?handler=Move', {
    method: 'POST',
    headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/x-www-form-urlencoded' },
    body: body.toString()
  }).then(function (r) { return r.json(); }).then(function (res) {
    if (res.ok) { moveCardTo(card, target); }
    else { showMoveError(res.message); }
  }).catch(function () { showMoveError(); });
}
```

The drop handler body then becomes: `if (!dragId) return; var id = dragId; dragId = null; requestMove(id, tab.getAttribute('data-tab'));`. NOTE: `requestMove` must be declared AFTER the `var token = …` line (it closes over `token`, `moveCardTo`, `showMoveError`).

Then add, still INSIDE the IIFE (the helpers are not visible outside it):

```js
// --- Card Approve/Skip buttons — same endpoint as drag-and-drop. ---
document.querySelectorAll('.app-lead-actions button[data-move]').forEach(function (btn) {
  btn.addEventListener('click', function (ev) {
    ev.preventDefault();
    ev.stopPropagation();
    var card = btn.closest('.app-lead');
    if (card) requestMove(card.getAttribute('data-lead-id'), btn.getAttribute('data-move'));
  });
});

// --- First-visit explainer. ---
var explainer = document.getElementById('app-explainer');
var explainerDismiss = document.getElementById('app-explainer-dismiss');
if (explainer && explainerDismiss) {
  if (!localStorage.getItem('cs-explainer-dismissed')) explainer.hidden = false;
  explainerDismiss.addEventListener('click', function () {
    localStorage.setItem('cs-explainer-dismissed', '1');
    explainer.hidden = true;
  });
}
```

In `site.css`, restyle the `.app-*` layer in place (keep every class name; change only the visual values to tokens — daylight background, `#fff` cards with `1px solid var(--line-soft)` borders, `border-radius: 12px`, `.app-briefing h1` in `var(--display)` weight 800, `.app-briefing-ts`/`.app-lead-ts` in `var(--mono)` at `0.78rem` color `var(--body-subtle)`), and add:

```css
.app-briefing { margin: 34px 0 6px; }
.app-briefing h1 { font-family: var(--display); font-weight: 800; letter-spacing: -0.02em; font-size: clamp(1.5rem, 3.5vw, 2.1rem); margin: 0; }
.app-briefing-ts { margin: 6px 0 0; font-size: 0.8rem; color: var(--body-subtle); }
.app-explainer { display: flex; justify-content: space-between; align-items: center; gap: 16px; background: #FFF3EC; border: 1px solid #F5CDB8; border-radius: 10px; padding: 12px 16px; margin: 14px 0; }
.app-explainer p { margin: 0; font-size: 0.92rem; color: var(--body-muted); }
.app-explainer-x { background: none; border: none; color: var(--sunrise-deep); font-weight: 600; cursor: pointer; }
.app-lead-draft { font-size: 0.9rem; color: var(--body-muted); line-height: 1.5; margin: 8px 0 10px; }
.app-lead-actions { display: flex; gap: 10px; margin-top: 10px; }
.app-lead-actions .btn { padding: 8px 16px; font-size: 0.85rem; }
```

- [ ] **Step 6: Update `Detail.cshtml` labels**

In `Areas/App/Pages/Leads/Detail.cshtml`, keep every `asp-page-handler` value; change only visible texts: `Accept` → `Approve`, `Discard` → `Skip`, `Move to New` → `Move back to review`, `← Back to your leads` → `← Back to your briefing`. Handler names (`Accept`, `Discard`, `Reopen`) are unchanged.

- [ ] **Step 7: Run tests**

Run: `dotnet test`
Expected: all green — `LeadReviewTests` + `CustomerZoneTests` pass with new literals; `LeadDetailTests` untouched (asserts handlers/data, not labels); Application tests compile with the new `LeadSummary` parameter.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(app): morning briefing header, plain-language tabs, draft-first lead cards"
```

---

### Task 5: Email shell restyle — invitation + password reset

**Files:**
- Modify: `src/Coldstart.Web/Email/EmailTemplates.cs` (new `Shell`, updated `Invitation`, `PasswordReset`)
- Modify: `tests/Coldstart.Web.Tests/EmailTemplatesTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Shell(string heading, string introHtml, string ctaText, string ctaUrl, string footnote)` private helper reused by Tasks 6–7. `EmailContent` record unchanged.

- [ ] **Step 1: Update the failing tests first**

In `EmailTemplatesTests.cs`:
- Replace the wordmark assertion (`"Cold"` and `"start"`) with: `html.Should().Contain("coldstart");`
- Add to the invitation test: `html.Should().Contain("#FF6A3D");` (sunrise CTA present) and `content.HtmlBody.Should().NotContain("<img");` (image-free rule pinned).
- Add a new test:

```csharp
[Fact]
public async Task Invitation_ExplainsWhatColdstartIs()
{
    // Arrange + Act
    var content = EmailTemplates.Invitation("Acme Co", "https://example.com/set");

    // Assert — first-touchpoint explanation appears in both bodies
    content.TextBody.Should().Contain("finds companies that need what you sell");
    content.HtmlBody.Should().Contain("finds companies that need what you sell");
}
```

(Drop `async` if the file's tests are synchronous — match its existing style.)

- [ ] **Step 2: Run to verify failures**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~EmailTemplates"` → FAIL.

- [ ] **Step 3: Rewrite the shell and both templates**

In `EmailTemplates.cs`, replace `Shell` with the Morning Engine version and update the two templates' copy:

```csharp
internal static EmailContent Invitation(string company, string setPasswordUrl) => new(
    Subject: "You're invited to Coldstart",
    TextBody:
        $"You've been invited to Coldstart as the admin for {company}.\n\n" +
        "Coldstart finds companies that need what you sell — every morning — and writes your first email to them. You approve and send.\n\n" +
        $"Set your password to get started:\n\n{setPasswordUrl}\n\n" +
        "This link expires in 7 days. If you weren't expecting this, ignore this email.",
    HtmlBody: Shell(
        heading: "You're invited to Coldstart",
        introHtml: $"You've been invited to Coldstart as the admin for <strong>{Enc(company)}</strong>. " +
                   "Coldstart finds companies that need what you sell — every morning — and writes your first email to them. You approve and send. " +
                   "Set your password to get started.",
        ctaText: "Set your password",
        ctaUrl: setPasswordUrl,
        footnote: "This link expires in 7 days. If you weren't expecting this, you can ignore this email."));

internal static EmailContent PasswordReset(string setPasswordUrl) => new(
    Subject: "Reset your Coldstart password",
    TextBody:
        $"Set a new password for your Coldstart account:\n\n{setPasswordUrl}\n\n" +
        "This link expires in 7 days. If you didn't ask for this, ignore this email.",
    HtmlBody: Shell(
        heading: "Reset your password",
        introHtml: "Use the button below to set a new password for your Coldstart account.",
        ctaText: "Set a new password",
        ctaUrl: setPasswordUrl,
        footnote: "This link expires in 7 days. If you didn't ask for this, you can ignore this email."));

private static string Enc(string s) => WebUtility.HtmlEncode(s);

private static string Shell(string heading, string introHtml, string ctaText, string ctaUrl, string footnote) => $$"""
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
<body style="margin:0;padding:0;background:#F1EEE7;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F1EEE7;">
    <tr><td align="center" style="padding:32px 16px;">
      <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#FDFBF7;border:1px solid #E7E2D7;border-radius:12px;overflow:hidden;">
        <tr><td>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
            <td height="4" style="height:4px;background:#1C1F33;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#54425E;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#C96F4A;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#F5C97B;font-size:0;line-height:0;">&nbsp;</td>
            <td height="4" style="height:4px;background:#FF6A3D;font-size:0;line-height:0;">&nbsp;</td>
          </tr></table>
        </td></tr>
        <tr><td style="padding:28px 32px 4px;">
          <span style="font-family:Helvetica,Arial,sans-serif;font-weight:800;font-size:20px;color:#16192B;letter-spacing:-0.01em;">coldstart<span style="color:#FF6A3D;">*</span></span>
        </td></tr>
        <tr><td style="padding:8px 32px 0;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <h1 style="margin:12px 0 10px;font-size:22px;color:#16192B;">{{heading}}</h1>
          <p style="margin:0 0 24px;font-size:16px;line-height:1.6;color:#5D5A50;">{{introHtml}}</p>
        </td></tr>
        <tr><td style="padding:0 32px 8px;">
          <a href="{{ctaUrl}}" style="display:inline-block;background:#FF6A3D;color:#ffffff;text-decoration:none;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-weight:700;font-size:16px;padding:14px 26px;border-radius:9px;">{{ctaText}} &rarr;</a>
        </td></tr>
        <tr><td style="padding:16px 32px 0;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:13px;line-height:1.5;color:#5D5A50;">Or paste this link into your browser:<br><a href="{{ctaUrl}}" style="color:#E85A2E;word-break:break-all;">{{ctaUrl}}</a></p>
        </td></tr>
        <tr><td style="padding:20px 32px 28px;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:13px;color:#8B8578;">{{footnote}}</p>
        </td></tr>
        <tr><td style="padding:18px 32px;background:#F1EEE7;border-top:1px solid #E7E2D7;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:12px;color:#5D5A50;">Coldstart &middot; Schiedam, the Netherlands &middot; <a href="{{Site}}" style="color:#E85A2E;text-decoration:none;">getcoldstart.nl</a></p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
""";
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~EmailTemplates"` → PASS. Then full `dotnet test` → green (`AdminInviteTests`/`PasswordResetTests` send through these templates; they assert URLs and recipients, not styling).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(email): Morning Engine shell for invitation and password reset"
```

---

### Task 6: Welcome email on invite completion

**Files:**
- Modify: `src/Coldstart.Web/Email/EmailTemplates.cs` (add `Welcome`)
- Modify: `src/Coldstart.Web/Areas/Identity/Pages/Account/ResetPassword.cshtml.cs`
- Tests: `tests/Coldstart.Web.Tests/EmailTemplatesTests.cs`, `tests/Coldstart.Web.Tests/PasswordResetTests.cs`

**Interfaces:**
- Consumes: `IEmailSender.SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct)` (from `Coldstart.Application.Ports`); Task 5 `Shell`.
- Produces: `EmailTemplates.Welcome(string appUrl)` returning `EmailContent`; sent exactly when a password reset flips `EmailConfirmed` from false to true (that flip happens only on invite completion).

- [ ] **Step 1: Write the failing tests**

In `EmailTemplatesTests.cs`:

```csharp
[Fact]
public void Welcome_ExplainsNextSteps_AndLinksToTheApp()
{
    // Arrange + Act
    var content = EmailTemplates.Welcome("https://getcoldstart.nl/app");

    // Assert
    content.Subject.Should().Contain("Welcome");
    content.TextBody.Should().Contain("https://getcoldstart.nl/app");
    content.HtmlBody.Should().Contain("https://getcoldstart.nl/app");
    content.TextBody.Should().Contain("morning");
}
```

In `PasswordResetTests.cs`, add (mirror the file's existing invite-completion arrange — it already exercises `POST /Identity/Account/ResetPassword` with a code):

```csharp
[Fact]
public async Task CompleteInvite_SendsWelcomeEmail()
{
    // Arrange — invited user (EmailConfirmed=false) with a valid reset code,
    // copied from this file's existing successful-reset test setup.

    // Act — POST the ResetPassword form with a valid code + new password.

    // Assert
    factory.Email.Sent.Should().Contain(m => m.Subject.Contains("Welcome"));
}

[Fact]
public async Task NormalPasswordReset_DoesNotSendWelcomeEmail()
{
    // Arrange — user with EmailConfirmed=true resets their password.

    // Act — same POST as above.

    // Assert
    factory.Email.Sent.Should().NotContain(m => m.Subject.Contains("Welcome"));
}
```

(Fill the Arrange/Act bodies by copying the exact working setup lines from the file's existing tests — same client, same antiforgery scraping, same code generation. Check `FakeEmailSender.Sent`'s item type for the exact property names.)

- [ ] **Step 2: Run to verify failures**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~EmailTemplates|FullyQualifiedName~PasswordReset"`
Expected: `Welcome_*` fails to compile→then fails; `CompleteInvite_SendsWelcomeEmail` FAILS (no email sent).

- [ ] **Step 3: Implement**

Add to `EmailTemplates.cs`:

```csharp
internal static EmailContent Welcome(string appUrl) => new(
    Subject: "Welcome to Coldstart — your mornings just changed",
    TextBody:
        "Your Coldstart account is ready.\n\n" +
        "Here's what happens next: we configure the engine to your niche, and it starts hunting overnight. " +
        "Each morning, any leads it finds — already checked against what you sell, with a drafted opening email — will be waiting in your briefing.\n\n" +
        $"Your briefing lives here:\n\n{appUrl}\n\n" +
        "You review, approve or skip, and get on with your day. Nothing is ever sent without you.",
    HtmlBody: Shell(
        heading: "Welcome to Coldstart",
        introHtml: "Your account is ready. Here's what happens next: we configure the engine to your niche, and it starts hunting overnight. " +
                   "Each morning, any leads it finds — already checked against what you sell, with a drafted opening email — will be waiting in your briefing. " +
                   "You review, approve or skip, and get on with your day. Nothing is ever sent without you.",
        ctaText: "Open your briefing",
        ctaUrl: appUrl,
        footnote: "Questions? Just reply to this email."));
```

In `ResetPassword.cshtml.cs`: add `Coldstart.Application.Ports.IEmailSender email` to the primary constructor, and change the confirm block in `OnPostAsync`:

```csharp
if (!user.EmailConfirmed)
{
    user.EmailConfirmed = true;
    await users.UpdateAsync(user);
    // Invite completion — first successful password set. Welcome them; best-effort,
    // a mail failure must not block the sign-in below.
    try
    {
        var appUrl = $"{Request.Scheme}://{Request.Host}/app";
        var welcome = EmailTemplates.Welcome(appUrl);
        await email.SendAsync(user.Email!, welcome.Subject, welcome.TextBody, welcome.HtmlBody, ct);
    }
    catch { /* welcome email is best-effort */ }
}
```

Add `using Coldstart.Web.Email;` and `using Coldstart.Application.Ports;` as needed.

- [ ] **Step 4: Run tests**

Run: `dotnet test` → all green (both new tests pass; existing reset tests unaffected because confirmed users skip the branch).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(email): welcome email on invite completion"
```

---

### Task 7: Morning briefing email template

**Files:**
- Modify: `src/Coldstart.Web/Email/EmailTemplates.cs` (add `MorningBriefing` + `BriefingLead` record)
- Test: `tests/Coldstart.Web.Tests/EmailTemplatesTests.cs`

**Interfaces:**
- Consumes: Task 5 `Shell` pattern (this template composes its own lead-rows table between intro and CTA — see code).
- Produces: `EmailTemplates.MorningBriefing(int count, IReadOnlyList<BriefingLead> leads, string appUrl)` and `internal sealed record BriefingLead(string Company, string Subject)`. NOTE: template only — the scheduled job that sends it is a separate follow-up feature, out of this plan's scope.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void MorningBriefing_ListsLeadsAndLinksToTheApp()
{
    // Arrange
    var leads = new[] { new BriefingLead("Acme Co", "A thought on your new funding"),
                        new BriefingLead("Beta BV", "Your first sales hire") };

    // Act
    var content = EmailTemplates.MorningBriefing(2, leads, "https://getcoldstart.nl/app");

    // Assert
    content.Subject.Should().Contain("2 new leads");
    content.TextBody.Should().Contain("Acme Co").And.Contain("Beta BV");
    content.HtmlBody.Should().Contain("Acme Co").And.Contain("A thought on your new funding");
    content.HtmlBody.Should().Contain("https://getcoldstart.nl/app");
    content.HtmlBody.Should().NotContain("<img");
}

[Fact]
public void MorningBriefing_HtmlEncodesLeadFields()
{
    // Arrange
    var leads = new[] { new BriefingLead("A & B <Ltd>", "Re: <script>") };

    // Act
    var content = EmailTemplates.MorningBriefing(1, leads, "https://getcoldstart.nl/app");

    // Assert
    content.HtmlBody.Should().Contain("A &amp; B &lt;Ltd&gt;");
    content.HtmlBody.Should().NotContain("<script>");
}
```

- [ ] **Step 2: Run to verify failures** — compile error (`BriefingLead` missing), then red.

- [ ] **Step 3: Implement**

```csharp
internal sealed record BriefingLead(string Company, string Subject);

internal static EmailContent MorningBriefing(int count, IReadOnlyList<BriefingLead> leads, string appUrl)
{
    var word = count == 1 ? "lead" : "leads";
    var textRows = string.Join("\n", leads.Select(l => $"  • {l.Company} — {l.Subject}"));
    var htmlRows = string.Join("", leads.Select(l =>
        $"<tr><td style=\"padding:10px 0;border-bottom:1px solid #E7E2D7;\">" +
        $"<span style=\"font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-weight:700;font-size:15px;color:#16192B;\">{Enc(l.Company)}</span><br>" +
        $"<span style=\"font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:13px;color:#5D5A50;\">{Enc(l.Subject)}</span>" +
        "</td></tr>"));
    return new(
        Subject: $"Your morning briefing — {count} new {word} waiting",
        TextBody:
            $"Good morning. The engine found {count} new {word} for you overnight:\n\n{textRows}\n\n" +
            $"Each comes with a drafted opening email. Review and approve them here:\n\n{appUrl}\n\n" +
            "Nothing is sent without your approval.",
        HtmlBody: Shell(
            heading: $"Good morning — {count} new {word}",
            introHtml: "The engine found these for you overnight, each with a drafted opening email:" +
                       "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:14px 0 4px;\">" + htmlRows + "</table>",
            ctaText: "Open your briefing",
            ctaUrl: appUrl,
            footnote: "You're receiving this because leads were found for your Coldstart account overnight. Nothing is sent without your approval."));
}
```

- [ ] **Step 4: Run tests** — `dotnet test` → all green.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(email): morning briefing digest template"
```

---

### Task 8: Legacy cleanup and full verification

**Files:**
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (delete legacy tokens/blocks)
- Delete: `src/Coldstart.Web/wwwroot/logo.svg` (replaced by text wordmark)
- Modify: any `.cshtml` still referencing `logo.svg` or legacy classes

**Interfaces:** none new — this task removes what earlier tasks orphaned.

- [ ] **Step 1: Find and remove orphans**

```bash
grep -rn "logo.svg" src/Coldstart.Web --include="*.cshtml"        # replace remaining uses with the wordmark markup
grep -n "\-\-cream\|--orange\|--rose\|--gold\|--teal\|--blue\|--screen" src/Coldstart.Web/wwwroot/css/site.css
```

Replace remaining `logo.svg` `<img>`s (check `Profile.cshtml`, Identity pages) with `<a class="wordmark" href="/">coldstart<i>*</i></a>`. For each legacy CSS variable still referenced, restyle that rule with Morning Engine tokens; when a legacy variable has zero references, delete it from `:root`. Then delete `logo.svg`. Also delete the old `--measure`/`--maxw` duplicates if both token layers define them (keep one).

- [ ] **Step 2: Verify no dead references**

```bash
grep -rn "logo.svg" src/ && echo "FAIL: still referenced" || echo "clean"
grep -n "cream\|#E07A3E\|#A04E1E" src/Coldstart.Web/wwwroot/css/site.css || echo "legacy palette gone"
dotnet build
```

- [ ] **Step 3: Full test suite**

Run: `dotnet test`
Expected: everything green (should be ~145+ tests after the additions).

- [ ] **Step 4: Visual verification (manual, but required)**

```bash
cd src/Coldstart.Web && dotnet run
```

Walk: `/` (scroll the full arc; check the dawn gradient, reveals, form submit), `/terms`, `/privacy`, log in → `/app` (briefing header, explainer dismiss, tabs, card Approve/Skip), a lead detail page, and the Identity login page (inherited restyle sanity). Then narrow the window to 375px and re-walk. Toggle OS reduced-motion and confirm no hidden content. Fix what looks broken before committing.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "chore(web): remove legacy retro-terminal styles and logo"
```

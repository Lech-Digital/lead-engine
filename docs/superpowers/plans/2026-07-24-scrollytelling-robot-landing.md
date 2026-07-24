# Scrollytelling Robot Landing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: this plan is written for hands-on inline execution (superpowers:executing-plans) with a screenshot-critique checkpoint per task, because the work is visual and each task is tuned against the ones before it. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Turn the landing page into a two-scene, scroll-driven narrative — the robot pinned on the right acting out Coldstart's work (boot → watch → sniff → reject → draft → dawn) while the copy scrolls on the left — buttery on desktop, gracefully simplified on mobile and under reduced motion.

**Architecture:** `position: sticky` pins each scene's robot stage; one vanilla `lp-scroll.js` sets `data-active` (current beat, via `IntersectionObserver`) and `--progress` (0..1 scroll scrub, via rAF) on each `.scene`; CSS interprets those to morph the robot. Reveals reuse the existing `contact.js` `data-reveal → is-visible` mechanism. No libraries, no build step.

**Tech Stack:** ASP.NET Core Razor Pages (`Index.cshtml`), plain CSS (`site.css`), one vanilla-JS file (`lp-scroll.js`), inline SVG. Headless Chrome for screenshot critique.

## Global Constraints

- No external JS/CSS libraries, no build step, no image assets — inline SVG + CSS + one vanilla-JS file. (Consistent with the current stack.)
- Only `transform`/`opacity` animate on scroll; the email "typing" width animation is a one-shot on beat activation, not per-frame.
- Reuse the established brand tokens: `--night #1C1F33`, `--sunrise #FF6A3D`, `--daylight #FDFBF7`, cream `#EBE6DA`, and the `*` mark. Reuse the existing detective-robot SVG as the base.
- Reuse the existing reveal convention: `[data-reveal]` → `.is-visible` (owned by `contact.js`; do not duplicate that observer). CSS may add slide/fade variants.
- **JS→CSS contract:** JS sets `--progress` (0..1) and `data-active` (int) on each `.scene`, and nothing else visual. CSS owns all interpretation.
- Fully degrade: `prefers-reduced-motion: reduce` → no pin/scrub (static, stacked, readable); no-JS → plain sections; viewport `< 820px` → unpinned stacked fallback. No content is revealed *only* by scroll.
- No scroll-hijacking: native scrolling only; no `preventDefault`, no smooth-scroll override.
- Never add a `Co-Authored-By` trailer to commits.

---

## File Structure

- **Modify** `src/Coldstart.Web/Pages/Index.cshtml` — restructure the hero into `section.scene.scene--hero`; merge Acts 3–4 into `section.scene.scene--night` with five `.beat`s; grow the robot SVG into stateful layers; add the `lp-scroll.js` include.
- **Modify** `src/Coldstart.Web/wwwroot/css/site.css` — scene/track/stage/beat layout, robot state selectors (`.scene[data-active="N"] …`), `--progress`-driven sky/sweep, reduced-motion + mobile fallbacks, enriched reveals.
- **Create** `src/Coldstart.Web/wwwroot/js/lp-scroll.js` — beat observer + rAF scrub + resize guard.
- **Modify** `tests/Coldstart.Web.Tests/PublicPagesTests.cs` — assert the scene scaffolding + `lp-scroll.js` reference are served, and existing act copy still present.

**Restart to view:** Razor views compile at build, so restart the app (`ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5178 dotnet run --project src/Coldstart.Web`) after `.cshtml` changes. CSS/JS static files are picked up on reload (no restart needed), but a restart is harmless. Screenshot with headless Chrome at scroll offsets (see Task 2 for the offset-capture trick).

---

## Task 1: Scene scaffolding + layout CSS + fallbacks

Restructure the markup into two scenes and lay out the sticky pin + beats, with mobile/reduced-motion fallbacks. The robot is the current static SVG for now (state comes later). Page must read perfectly with **no JS**.

**Files:** Modify `Index.cshtml` (hero → `scene--hero`; Acts 3–4 → `scene--night`), `site.css`.

- [ ] **Step 1: Wrap the hero as `scene--hero`.** Keep the existing `.lp-nav` at the top of the hero. Replace the hero's `.lp-hero-grid` wrapper with the scene scaffold:

```html
<header class="lp-hero">
  <div class="wrap">
    <nav class="lp-nav"> … unchanged … </nav>
    <div class="scene scene--hero" data-scene="hero" data-active="0">
      <div class="scene-track">
        <div class="scene-copy">
          <div class="beat" data-beat="0">
            <div class="scene-copy-inner">
              <h1>Wake up to your next client, already found — with the first email already written.</h1>
              <p class="lp-lede">For companies that sell to other businesses. We watch the market overnight, pick the few companies that need you right now, and draft your opening email. You read it over coffee and hit approve.</p>
              <div class="lp-cta-row">
                <a class="btn btn-primary" href="#get-leads">Get 3 sample leads — free</a>
                <a class="lp-quiet" href="#enemy">See one morning ↓</a>
              </div>
            </div>
          </div>
          <div class="beat" data-beat="1">
            <div class="scene-copy-inner">
              <p class="lp-scene-cue">Your night-shift engine is booting up. It works while you sleep — scroll on.</p>
            </div>
          </div>
        </div>
        <div class="scene-stage" aria-hidden="true">
          <figure class="lp-hero-art">
            <!-- existing robot SVG, unchanged for now (Task 3 adds boot/ready states) -->
          </figure>
        </div>
      </div>
    </div>
  </div>
</header>
```

- [ ] **Step 2: Merge Act 3 + Act 4 into `scene--night`.** Replace the current `<section class="lp-night">…</section>` and `<section class="lp-sunrise">…</section>` with one scene. Keep every line of the existing copy (timeline moments + the real draft + the sunrise reveal), redistributed into five beats:

```html
<section class="scene scene--night" id="morning" data-scene="night" data-active="0">
  <div class="scene-track">
    <div class="scene-copy">
      <div class="beat" data-beat="0"><div class="scene-copy-inner">
        <p class="lp-night-label ts">WHILE YOU SLEEP</p>
        <p class="lp-time ts">06:58</p>
        <p>Patronus AI announces a $50M round amid "nearly insatiable demand." Their news is minutes old.</p>
      </div></div>
      <div class="beat" data-beat="1"><div class="scene-copy-inner">
        <p class="lp-time ts">06:59</p>
        <p>We check them against what <em>you</em> sell.</p>
      </div></div>
      <div class="beat" data-beat="2"><div class="scene-copy-inner">
        <p class="lp-time ts">06:59</p>
        <p>Most companies fail this check — a competitor, a job seeker, plain noise — and are rejected. Out of 77 companies one recent morning, we rejected 74. This one fits.</p>
      </div></div>
      <div class="beat" data-beat="3"><div class="scene-copy-inner">
        <p class="lp-time ts">07:04</p>
        <p>Your opening email is drafted — four sentences, starting from <em>their</em> news, in your voice:</p>
        <div class="lp-draft">
          <p class="lp-draft-subj">Subject: Scaling headcount with AI efficiency</p>
          <p>Patronus AI's nearly insatiable demand is a strong indicator the company is about to scale headcount, which can strain HR teams. AI-screened candidate shortlists can cut time-to-hire by 40%, letting HR focus on more strategic tasks. As you invest in new tooling, how are you planning to optimize hiring to keep up with demand?</p>
          <footer class="ts">real draft · unedited · written for a sample client selling recruiting software</footer>
        </div>
      </div></div>
      <div class="beat" data-beat="4"><div class="scene-copy-inner">
        <p class="lp-time ts">07:30</p>
        <h2>It's in your inbox.</h2>
        <p class="lp-sunrise-sub">All of this already runs, every morning. It's called Coldstart.</p>
      </div></div>
    </div>
    <div class="scene-stage" aria-hidden="true">
      <figure class="lp-hero-art lp-hero-art--night">
        <!-- Task 4–6 build the stateful night robot SVG here; for now, reuse the current robot SVG -->
      </figure>
    </div>
  </div>
</section>
```

- [ ] **Step 3: Add the scene layout CSS** to `site.css` (after the existing `.lp-hero-art` rules). Desktop pins the stage; mobile + reduced-motion unpin and stack.

```css
/* --- Scrollytelling scenes --- */
.scene-track { position: relative; }
.scene-copy-inner { max-width: 30rem; }
.lp-scene-cue { color: var(--body-subtle); font-size: 1rem; }
.scene--night { background: var(--night); color: var(--night-text); }
.scene--night .lp-time { color: var(--sunrise-soft); }
.scene--night h2 { color: var(--night-text); }

/* Desktop: two columns, sticky stage on the right, beats scroll on the left. */
@media (min-width: 820px) {
  .scene-track { display: grid; grid-template-columns: minmax(0,1fr) minmax(0,1fr); gap: 48px; align-items: start; }
  .scene--hero .scene-track { grid-template-columns: 1.05fr 0.95fr; }
  .scene-copy { grid-column: 1; }
  .scene-stage { grid-column: 2; position: sticky; top: 0; height: 100vh; display: grid; place-items: center; will-change: transform; }
  .scene-stage .lp-hero-art svg { max-width: 480px; width: 100%; height: auto; }
  .beat { min-height: 80vh; display: flex; align-items: center; }
  .scene--hero .beat { min-height: 75vh; }
  /* Track height = sum of beats + one pin's worth; the beats' min-heights drive it,
     so no explicit track height is needed — the sticky stage pins across the beats. */
}

/* Mobile + no-desktop: unpin, stack copy above a single robot per scene. */
@media (max-width: 819.98px) {
  .scene-stage { position: static; height: auto; margin-top: 24px; }
  .scene-stage .lp-hero-art svg { max-width: 360px; margin-inline: auto; }
  .beat { min-height: 0; margin-bottom: 28px; }
  .scene--night .beat:not([data-beat="0"]) .lp-time { margin-top: 8px; }
}

/* Reduced motion: never pin (avoids a tall empty pinned column with no scrub). */
@media (prefers-reduced-motion: reduce) {
  .scene-stage { position: static; height: auto; }
  .beat { min-height: 0; margin-bottom: 28px; }
}
```

- [ ] **Step 4: Restart + screenshot (no JS state yet).** Restart the app. Capture the hero and the night scene at a few scroll offsets and at mobile width using the offset trick from Task 2, Step 3. **Verify:** copy is fully present and readable; on desktop the robot stays pinned while beats scroll; on mobile everything stacks with the robot at a sensible size; nothing overflows horizontally.

- [ ] **Step 5: Commit.**
```bash
git add src/Coldstart.Web/Pages/Index.cshtml src/Coldstart.Web/wwwroot/css/site.css
git commit -m "feat(landing): scene scaffolding for scroll sequence (sticky pin + beats + fallbacks)"
```

---

## Task 2: The scroll engine (`lp-scroll.js`)

Set `data-active` (current beat) and `--progress` (0..1) on each scene; guard for reduced-motion/mobile/no-API; re-init on resize.

**Files:** Create `wwwroot/js/lp-scroll.js`; add `<script>` include to `Index.cshtml`.

- [ ] **Step 1: Write `lp-scroll.js`.**
```javascript
// Landing scroll-scene engine: pins each .scene's stage (CSS), and drives the pinned
// robot by writing data-active (current beat) + --progress (0..1 scrub) per scene.
// No scroll-hijacking; native scrolling only. No-ops on mobile / reduced motion.
(function () {
  "use strict";
  var mqReduce = window.matchMedia("(prefers-reduced-motion: reduce)");
  var mqNarrow = window.matchMedia("(max-width: 819.98px)");
  var supported = "IntersectionObserver" in window && "requestAnimationFrame" in window;

  var scenes = [].slice.call(document.querySelectorAll(".scene"));
  if (!scenes.length || !supported) return;

  var beatObservers = [];
  var scrollBound = false;
  var raf = 0;

  function active() { return !mqReduce.matches && !mqNarrow.matches; }

  function setup() {
    teardown();
    if (!active()) { scenes.forEach(function (s) { s.removeAttribute("style"); s.setAttribute("data-active", "0"); }); return; }
    scenes.forEach(function (scene) {
      var beats = [].slice.call(scene.querySelectorAll(".beat"));
      // Activate the beat nearest the viewport centre.
      var io = new IntersectionObserver(function (entries) {
        entries.forEach(function (e) {
          if (e.isIntersecting) scene.setAttribute("data-active", e.target.getAttribute("data-beat"));
        });
      }, { rootMargin: "-45% 0px -45% 0px", threshold: 0 });
      beats.forEach(function (b) { io.observe(b); });
      beatObservers.push(io);
    });
    if (!scrollBound) { window.addEventListener("scroll", onScroll, { passive: true }); scrollBound = true; }
    onScroll();
  }

  function teardown() {
    beatObservers.forEach(function (io) { io.disconnect(); });
    beatObservers = [];
  }

  function onScroll() {
    if (raf) return;
    raf = window.requestAnimationFrame(function () {
      raf = 0;
      if (!active()) return;
      var vh = window.innerHeight;
      scenes.forEach(function (scene) {
        var track = scene.querySelector(".scene-track") || scene;
        var r = track.getBoundingClientRect();
        // progress: 0 when the track's top hits the viewport top, 1 when its bottom does.
        var total = r.height - vh;
        var p = total > 0 ? Math.min(1, Math.max(0, -r.top / total)) : 0;
        scene.style.setProperty("--progress", p.toFixed(4));
      });
    });
  }

  setup();
  var resizeT;
  window.addEventListener("resize", function () { clearTimeout(resizeT); resizeT = setTimeout(setup, 200); });
  mqReduce.addEventListener && mqReduce.addEventListener("change", setup);
})();
```

- [ ] **Step 2: Include it** at the end of `Index.cshtml` (page-specific; `contact.js` stays in `_Layout`):
```html
</main>
<script src="~/js/lp-scroll.js" defer></script>
```

- [ ] **Step 3: Verify data-active/progress with a scripted scroll.** Screenshots can't show mid-scroll state directly, so drive a real headless session and read the attributes. Use this capture script (also the reusable offset-screenshot trick for later tasks):
```bash
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
# capture the night scene at 3 scroll depths + read data-active
for f in 0.15 0.45 0.85; do
  cat > /tmp/cap.js <<EOF
  // no-op placeholder — use --virtual-time-budget + a URL hash scroll instead
EOF
done
# Simplest reliable approach: append a scroll offset via a tiny query the page reads, OR
# use Chrome's --window-size + a screenshot after scrolling via devtools protocol.
```
Practical method actually used during the build: load the live page in a headless session that scrolls to a pixel offset and screenshots (via a small Playwright/puppeteer-free approach: a data: wrapper is not possible cross-origin, so use the DevTools protocol or the `--screenshot` after a `window.scrollTo`). If a scriptable headless driver is unavailable, verify `data-active`/`--progress` by temporarily rendering their values into a fixed-position debug badge (`.scene::after { content: attr(data-active); }`) and screenshotting at offsets, then remove the badge before commit. **Verify:** `data-active` advances 0→…→4 through the night scene; `--progress` moves 0→1; both freeze correctly at the ends; no console errors; page still scrolls natively.

- [ ] **Step 4: Confirm the fallback.** Emulate reduced motion (`--force-prefers-reduced-motion` or a temporary `mqReduce` force) and narrow width: the engine must no-op (no inline `--progress`, `data-active="0"`), and the page reads as stacked sections.

- [ ] **Step 5: Commit.**
```bash
git add src/Coldstart.Web/wwwroot/js/lp-scroll.js src/Coldstart.Web/Pages/Index.cshtml
git commit -m "feat(landing): scroll-scene engine (data-active beats + --progress scrub)"
```

---

## Task 3: Scene 1 — robot boot → ready

Wire the hero robot to `data-active`: beat 0 = booting (eyes/antenna/scanner off, then on), beat 1 = ready (scanner pulsing, gentle idle bob).

**Files:** `Index.cshtml` (hero SVG: add classes/ids to eyes, antenna `*`, scanner), `site.css` (state selectors + transitions).

- [ ] **Step 1: Tag the hero robot parts.** In the hero stage SVG, ensure the eyes carry `class="lp-eye"`, the antenna `*` `class="r-antenna"`, the chest scanner polyline `class="r-scan"`, and the whole robot group `class="lp-bot"`.

- [ ] **Step 2: Add boot/ready CSS** to `site.css`:
```css
/* Scene 1: boot → ready, driven by data-active */
.scene--hero .lp-eye, .scene--hero .r-antenna, .scene--hero .r-scan { transition: opacity .5s ease; }
.scene--hero[data-active="0"] .lp-eye { opacity: 0.25; }         /* dim, booting */
.scene--hero[data-active="0"] .r-antenna { opacity: 0.2; }
.scene--hero[data-active="0"] .r-scan { opacity: 0; }
.scene--hero[data-active="1"] .lp-bot { animation: lp-bob 3.4s ease-in-out infinite; }
@media (prefers-reduced-motion: no-preference) {
  .scene--hero[data-active="1"] .r-scan { animation: lp-scan-dash 2s linear infinite; }
}
@keyframes lp-bob { 0%,100% { transform: translateY(0); } 50% { transform: translateY(-6px); } }
@keyframes lp-scan-dash { to { opacity: 1; } }
```
(Default/fallback state = fully lit, so no-JS shows a normal awake robot.)

- [ ] **Step 3: Restart, screenshot beat 0 vs beat 1.** **Verify:** at the top the robot reads slightly "asleep" (dim eyes), and after one scroll it's fully awake with the scanner going and a soft bob. Reduced motion: no bob, robot lit. Commit.
```bash
git add -A && git commit -m "feat(landing): scene 1 robot boot→ready states"
```

---

## Task 4: Scene 2 robot — night→dawn sky, watch & sniff

Build the stateful night-scene robot: a `--progress`-driven sky (night→dawn), blinking signal dots (watch), and the magnifier raising + candidate cards fading in (sniff). Beats 0–1.

**Files:** `Index.cshtml` (night stage SVG — the stateful version), `site.css` (state + `--progress` sky/sweep).

- [ ] **Step 1: Build the night robot SVG with layered groups** in the `scene--night` stage. Reuse the base robot; add layer groups, each toggled by `data-active`:
  - `.r-sky-night` (full night fill) + `.r-sky-dawn` (dawn gradient rect, `opacity: var(--progress)`) + `.r-stars` (`opacity: calc(1 - var(--progress))`).
  - `.r-signals` — ~5 small dots in the sky; visible at beat 0, each with a staggered blink.
  - `.r-robot` base (body/head/eyes/antenna/scanner).
  - `.r-glass` — magnifier; from beat 1 on, raised; its sweep uses `transform: rotate(calc(-6deg + 11deg*var(--progress)))` around the shoulder (`transform-box: view-box; transform-origin: 256px 236px`).
  - `.r-cards` — 6 candidate cards spread on the ground, hidden at beat 0, fading/scaling in at beat 1. Give the survivor `class="r-card r-card--keep"`, the rest `r-card--drop`.
  - `.r-email`, `.r-inbox`, `.r-check` — present in DOM but hidden until later beats (Task 6).

- [ ] **Step 2: State CSS for beats 0–1.**
```css
.scene--night .r-sky-dawn { opacity: var(--progress, 0); }
.scene--night .r-stars { opacity: calc(1 - var(--progress, 0)); }
.scene--night .r-glass { transform-box: view-box; transform-origin: 256px 236px; transition: transform .6s ease, opacity .5s ease; }
.scene--night .r-cards, .scene--night .r-email, .scene--night .r-inbox, .scene--night .r-check { opacity: 0; transition: opacity .5s ease, transform .5s ease; }
.scene--night[data-active="0"] .r-glass { opacity: .35; transform: rotate(-14deg); }        /* lowered, watching */
.scene--night[data-active="1"] .r-cards { opacity: 1; }
.scene--night[data-active="1"] .r-glass { transform: rotate(calc(-6deg + 11deg * var(--progress, 0))); }
@media (prefers-reduced-motion: no-preference) {
  .scene--night[data-active="0"] .r-signal { animation: lp-twinkle 2.2s ease-in-out infinite; }
}
@keyframes lp-twinkle { 0%,100% { opacity: .25; } 50% { opacity: .9; } }
```

- [ ] **Step 3: Restart, screenshot beats 0 and 1** (and a couple of `--progress` values so the sky shows some dawn). **Verify:** beat 0 = deep night, signals twinkling, glass lowered; beat 1 = candidate cards appear, glass raised and sweeping; sky lightens as progress grows. Commit.
```bash
git add -A && git commit -m "feat(landing): scene 2 watch + sniff (night→dawn sky, signals, cards, sweep)"
```

---

## Task 5: Scene 2 — the reject beat

The bold moment: at beat 2, most candidate cards get an ✕ and fall away; the survivor stays and glows.

**Files:** `Index.cshtml` (add ✕ marks + glow to `.r-cards`), `site.css`.

- [ ] **Step 1: Markup.** Each `.r-card--drop` contains a small ✕ mark (`class="r-x"`, initially `opacity:0`). The survivor `.r-card--keep` has a `.r-card-glow` ellipse behind it (initially `opacity:0`).

- [ ] **Step 2: Reject CSS.**
```css
.scene--night .r-card--drop { transition: transform .55s cubic-bezier(.4,0,.7,1), opacity .55s ease; }
.scene--night .r-x { opacity: 0; transition: opacity .3s ease; }
.scene--night .r-card-glow { opacity: 0; transition: opacity .6s ease; }
.scene--night[data-active="2"] .r-x,
.scene--night[data-active="3"] .r-x,
.scene--night[data-active="4"] .r-x { opacity: 1; }
.scene--night[data-active="2"] .r-card--drop,
.scene--night[data-active="3"] .r-card--drop,
.scene--night[data-active="4"] .r-card--drop { transform: translateY(70px); opacity: 0; }
.scene--night[data-active="2"] .r-card-glow,
.scene--night[data-active="3"] .r-card-glow,
.scene--night[data-active="4"] .r-card-glow { opacity: .4; }
```
(Give the drop cards small per-card `transition-delay`s inline — e.g. `style="transition-delay:.05s"`… — so they don't all fall in unison; a quick cascade reads better.)

- [ ] **Step 3: Restart, screenshot the transition beat 1→2** (capture at a `data-active="2"` scroll depth). **Verify:** ✕'s flash on the rejects, they drop and fade in a quick cascade, one card remains and glows ("this one fits"). The cull is satisfying, not chaotic. Commit.
```bash
git add -A && git commit -m "feat(landing): scene 2 reject beat (cards ✕ and fall away, survivor glows)"
```

---

## Task 6: Scene 2 — draft (typing) + dawn (deliver)

Beat 3: the survivor unfolds into an email whose lines type in. Beat 4: sky blooms to dawn, the email flies into an inbox, a ✓ lands.

**Files:** `Index.cshtml` (`.r-email`, `.r-inbox`, `.r-check` layers), `site.css`.

- [ ] **Step 1: Email + typing.** `.r-email` sits where the survivor card was, larger, with an orange accent bar, a subject line, and 3 body lines (`.r-email-line`) that animate width `0 → full` when beat 3 activates.
```css
.scene--night[data-active="3"] .r-email,
.scene--night[data-active="4"] .r-email { opacity: 1; }
.scene--night .r-email-line { transform: scaleX(0); transform-origin: left; }
@media (prefers-reduced-motion: no-preference) {
  .scene--night[data-active="3"] .r-email-line,
  .scene--night[data-active="4"] .r-email-line { animation: lp-type .5s steps(12) forwards; }
  .scene--night[data-active="3"] .r-email-line:nth-child(2) { animation-delay: .35s; }
  .scene--night[data-active="3"] .r-email-line:nth-child(3) { animation-delay: .7s; }
}
@keyframes lp-type { to { transform: scaleX(1); } }
@media (prefers-reduced-motion: reduce) { .scene--night .r-email-line { transform: scaleX(1); } }
```

- [ ] **Step 2: Dawn + deliver (beat 4).** The dawn sky is already `--progress`-driven and will be near-full by beat 4; add the hand-off: the email translates toward an `.r-inbox` glyph and `.r-check` scales in.
```css
.scene--night[data-active="4"] .r-email { transform: translate(46px, -30px) scale(.82); }
.scene--night[data-active="4"] .r-inbox { opacity: 1; }
.scene--night[data-active="4"] .r-check { opacity: 1; transform: scale(1); }
.scene--night .r-check { transform: scale(.4); transition: opacity .4s ease, transform .4s cubic-bezier(.2,1.4,.4,1); }
```

- [ ] **Step 3: Restart, screenshot beats 3 and 4.** **Verify:** beat 3 = email lines type in left-to-right; beat 4 = sky is dawn, email hands off to the inbox, ✓ pops. Reduced motion: lines are full, no typing, states still resolve. Commit.
```bash
git add -A && git commit -m "feat(landing): scene 2 draft (typing) + dawn deliver (inbox + check)"
```

---

## Task 7: Enriched reveals, full-page polish, a11y/mobile, structural test

Upgrade the between/after sections' reveals, verify the whole page end-to-end across modes, add a structural test, and do the final tuning pass.

**Files:** `site.css` (reveal variants + tuning), `Index.cshtml` (add `data-reveal` variants where useful), `tests/Coldstart.Web.Tests/PublicPagesTests.cs`.

- [ ] **Step 1: Enrich reveals.** Extend the existing `[data-reveal]` treatment with directional variants (reused `.is-visible`), e.g. `[data-reveal="left"]`, `[data-reveal="right"]` sliding in from the sides for the enemy/belief/how-it-works/pricing sections, so the non-pinned parts also feel alive. Keep the reduced-motion baseline (everything visible, no transform).
```css
[data-reveal="left"] { transform: translateX(-28px); }
[data-reveal="right"] { transform: translateX(28px); }
[data-reveal="left"].is-visible, [data-reveal="right"].is-visible { transform: none; }
@media (prefers-reduced-motion: reduce) { [data-reveal="left"], [data-reveal="right"] { transform: none; } }
```
(`contact.js` already toggles `.is-visible` on any `[data-reveal]`, so no JS change needed.)

- [ ] **Step 2: Full-page screenshot sweep.** Desktop: top→bottom at ~8 offsets covering both scenes and all between sections. Mobile (390px): full-page. Reduced-motion: full-page. **Verify:** smooth pinned scrubbing on desktop; both scenes tell the story; no horizontal overflow; no overlap/clipping; mobile is clean and stacked; reduced-motion is static and readable; no console errors.

- [ ] **Step 3: Add a structural test** to `PublicPagesTests.cs` (matches the existing WebApplicationFactory style):
```csharp
    [Fact]
    public async Task Landing_RendersScrollSceneScaffolding_AndKeepsActCopy()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert — scenes, all five night beats, the engine script, and key copy are served
        html.Should().Contain("scene--hero").And.Contain("scene--night");
        foreach (var b in new[] { "data-beat=\"0\"", "data-beat=\"1\"", "data-beat=\"2\"", "data-beat=\"3\"", "data-beat=\"4\"" })
            html.Should().Contain(b);
        html.Should().Contain("lp-scroll.js");
        html.Should().Contain("It's in your inbox.").And.Contain("What it costs. All of it.");
    }
```

- [ ] **Step 4: Run the web tests** (existing PublicPages assertions must still pass — the act copy moved but is all still present):
```bash
dotnet test tests/Coldstart.Web.Tests --filter "PublicPagesTests"
```
Expected: all green.

- [ ] **Step 5: Final tuning + commit.** Adjust dwell (`.beat` min-heights), easing, and observer `rootMargin` to taste per the sweep in Step 2 (scene should feel "a bit faster, not too much" per the spec). Then:
```bash
git add -A && git commit -m "feat(landing): enriched reveals + a11y/mobile polish + scene structural test"
```

---

## Self-Review

- **Spec coverage:** two pinned scenes (Tasks 1,3,4,5,6) ✓; sticky+IO+rAF mechanism (Task 2) ✓; JS→CSS contract (`--progress`/`data-active`) ✓; reject beat kept (Task 5) ✓; short Scene 1 (Task 1/3) ✓; reduced-motion + no-JS + mobile fallbacks (every task + Task 7) ✓; reuse of existing robot/palette/reveals ✓; structural test (Task 7) ✓.
- **No hidden-only-by-scroll content:** reveals start visible-by-default and are enhanced; copy is real in-order DOM.
- **Type/name consistency:** class contract (`.scene`, `.scene-track`, `.scene-stage`, `.scene-copy`, `.beat`, `data-active`, `--progress`, `.r-*` layers, `.is-visible`) is used identically across JS and CSS tasks.
- **Known iteration risk:** exact SVG coordinates and dwell vh values are tuned via screenshots during the build (flagged in the spec and tasks) — appropriate for a visual feature.

## Execution Handoff

This plan is intended for **inline execution by the controller** (superpowers:executing-plans), task-by-task, with a screenshot-critique checkpoint each task — because the choreography must be seen and tuned, which subagents can't do well. Restart the app after `.cshtml` edits; screenshot with headless Chrome at scroll offsets; keep each task's commit self-contained.

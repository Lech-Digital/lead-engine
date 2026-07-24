# Scrollytelling Robot Landing — Design

**Date:** 2026-07-24
**Status:** Accepted
**Branch:** dotnet-port

## Context

The landing page (`src/Coldstart.Web/Pages/Index.cshtml`) already tells Coldstart's story in seven acts (hero → the enemy → the belief → while-you-sleep timeline → sunrise reveal → how-it-works → pricing → CTA form) with a static, brand-true detective-robot hero illustration and simple `data-reveal` fade-ins.

We want the page to feel "supermodern and very much alive" — a cinematic, scroll-driven experience in the spirit of Apple/OnePlus product pages ("scrollytelling"): a visual is pinned on screen while the copy scrolls past, and scroll position scrubs an animation so scrolling feels like scrubbing a film. Here, the robot acts out what Coldstart does as you scroll.

The robot, palette (`--night #1C1F33`, `--sunrise #FF6A3D`, cream), and the `*` mark are already established and must be reused.

## Goal

Turn the landing page into a two-scene scroll-driven narrative: the robot is pinned on the right while the story text scrolls on the left, and the robot morphs through the stages of Coldstart's work (boot up; then watch → sniff → reject → draft → deliver at dawn) — buttery on desktop, gracefully simplified on mobile and under reduced-motion.

## Key Decisions (resolved during brainstorming)

1. **Two pinned scenes:** Scene 1 = hero (robot comes to life); Scene 2 = the "night shift" (maps to Acts 3–4). Normal scrolling sections between and after.
2. **Desktop-first, clean mobile fallback:** full pinned scrubbing on desktop/tablet; on phones it unpins — sections stack, robot appears once per scene, gentle fade/slide reveals, zero scroll jank.
3. **Keep the bold "reject" beat** (candidate cards get ✕'d and fall away).
4. **Scene 1 is short** — long enough to read the hero copy, not a chore.
5. **Tech:** `position: sticky` pinning + `IntersectionObserver` beat activation + a tiny `requestAnimationFrame` scroll-progress scrub. One small self-contained `lp-scroll.js`; no external libraries (no GSAP). Only `transform`/`opacity` animate.

## Non-Goals / Explicit Limitations

- No animation library, no build step, no external assets — hand-built inline SVG + CSS + one vanilla-JS file, consistent with the current stack.
- Native CSS scroll-driven animations (`animation-timeline: scroll()/view()`) are **not** relied upon (uneven browser support); the JS scrub is the universal path. (May be added later as a progressive enhancement.)
- No changes to server-side content, forms, or the existing act copy beyond restructuring markup for the scenes. The real draft copy in Act 3 is reused verbatim.
- Not a full rewrite of every act into scenes — only the hero and the while-you-sleep/sunrise acts become pinned scenes.

## Architecture Overview

```
<main class="lp">
  <section class="scene scene--hero">        ← Scene 1 (pinned)
    <div class="scene-track">                  tall; defines scroll length
      <div class="scene-stage"> robot SVG </div>   position: sticky; top:0; 100vh
      <div class="scene-copy">                     beats flow normally (left)
        <div class="beat" data-beat="0"> hero h1 + lede + CTA </div>
        <div class="beat" data-beat="1"> "ready" cue </div>
      </div>
    </div>
  </section>

  <section class="lp-act" id="enemy"> … </section>     ← normal, enhanced reveals
  <section class="lp-act …belief"> … </section>

  <section class="scene scene--night" id="morning">   ← Scene 2 (pinned)
    <div class="scene-track">
      <div class="scene-stage"> robot SVG (night→dawn) </div>
      <div class="scene-copy">
        <div class="beat" data-beat="0"> 06:58 watch </div>
        <div class="beat" data-beat="1"> 06:59 sniff </div>
        <div class="beat" data-beat="2"> reject </div>
        <div class="beat" data-beat="3"> 07:04 draft </div>
        <div class="beat" data-beat="4"> 07:30 dawn/deliver </div>
      </div>
    </div>
  </section>

  <section class="lp-act"> how it works </section>      ← normal, enhanced reveals
  <section class="lp-act …pricing"> … </section>
  <section class="lp-act …form" id="get-leads"> … </section>
</main>
<script src="~/js/lp-scroll.js" defer></script>
```

**Pinning:** `.scene-stage` is `position: sticky; top: 0; height: 100vh` inside a taller `.scene-track`, so the robot stays fixed while `.scene-copy` beats scroll past. Two-column grid (copy left, stage right) on desktop.

**Beat activation:** each `.beat` is observed with `IntersectionObserver`; when a beat is centered it becomes the scene's active beat. The active index is written as `data-active` on the `.scene`; CSS selectors (`.scene[data-active="2"] .r-cards { … }`) drive the robot's state, with CSS `transition`s producing smooth crossfades/morphs between states.

**Scrub:** a single rAF-throttled `scroll` handler computes each in-view scene's progress `0..1` (how far the track has scrolled through its pinned range) and sets `--progress` on the `.scene`. Continuous elements read it: the night→dawn sky (`--progress` drives a gradient stop / layered opacity) and the magnifier sweep offset. This gives the scrubbed, film-like feel that stepped beats alone cannot.

## Components

### 1. `lp-scroll.js` (new, `wwwroot/js`)

Vanilla, dependency-free, `defer`-loaded. Responsibilities:

- **Guard:** if `matchMedia('(prefers-reduced-motion: reduce)')` matches OR viewport width < 820px OR `IntersectionObserver`/`requestAnimationFrame` unavailable → do nothing (CSS handles the static/stacked fallback). Re-evaluate on resize (debounced) to switch modes if the user crosses the breakpoint.
- **Beat activation:** one `IntersectionObserver` per scene over its `.beat`s (`rootMargin` biased to the vertical center, threshold list). On change, set `scene.dataset.active` to the most-centered beat's index.
- **Scrub:** on `scroll` (rAF-throttled) compute, for each scene whose track overlaps the viewport, `progress = clamp((scrollTop - trackTop) / (trackHeight - viewportHeight), 0, 1)` and set `scene.style.setProperty('--progress', progress)`.
- **Reveals:** a shared `IntersectionObserver` adds `.is-in` to `[data-reveal]` elements (upgrades the current reveal to a transform+opacity slide/fade). One-shot (unobserve after reveal).
- No scroll-hijacking, no `preventDefault`, no smooth-scroll override — native scrolling only.

**CSS-var contract (JS → CSS):** `--progress` (0..1) on each `.scene`; `data-active` (int) on each `.scene`; `.is-in` class on reveal targets. CSS owns all visual interpretation.

### 2. Scene markup + CSS (`Index.cshtml`, `site.css`)

- `.scene-track` height sets the scroll length (tuned a touch faster than a full screen per beat, so it scrubs briskly but still reads): Scene 1 ≈ `150vh` (short — read hero, then the "ready" beat), Scene 2 ≈ `430vh` (five beats at ~80vh dwell each + the pin).
- `.scene-stage`: `position: sticky; top: 0; min-height: 100vh; display: grid; place-items: center;`.
- Desktop grid: `.scene-track { display: grid; grid-template-columns: minmax(0,1fr) minmax(0,1fr); }` with copy on the left, stage on the right (`scene--hero` may bias columns for the hero).
- `.beat`: `min-height: ~80vh; display: flex; align-items: center;` — each beat gets a shorter-than-a-screen dwell so scrolling advances the story quickly without feeling rushed. (Final vh values are tuning knobs, adjusted by screenshot/real-browser feel during the build.)
- Robot state driven by `.scene[data-active="N"]` selectors toggling opacity/transform of SVG layer groups.

### 3. Robot SVG — stateful (`Index.cshtml`)

Extend the existing robot into one SVG carrying all Scene-2 layers, shown/hidden per beat:

- `.r-robot` — body/head/eyes/antenna/scanner (always present).
- `.r-sky` — night→dawn: a night fill plus a dawn gradient layer whose opacity is driven by `--progress` (0 night → 1 dawn), plus stars that fade as dawn rises.
- `.r-signals` — faint blinking "news" dots in the sky (Watch beat).
- `.r-glass` — magnifier; its sweep offset reads `--progress`; raised/active from Sniff on.
- `.r-cards` — a spread of ~6 candidate cards (Sniff). On Reject (`data-active>=2`) all but one get an ✕ overlay and translate/opacity out ("fall away"); the survivor stays and gains the orange glow.
- `.r-email` — the survivor unfolds into an email; body lines animate width 0→full ("typing") when `data-active>=3`.
- `.r-inbox` / `.r-check` — Dawn: envelope flies to an inbox glyph, a ✓ lands (`data-active>=4`).

Scene 1 uses the existing robot with a `boot`/`ready` toggle: eyes/antenna/scanner off→on and an idle bob, driven by `data-active`.

All state changes are CSS `transition`ed (opacity/transform, ~300–500ms, eased) so beat changes crossfade rather than snap.

## Error Handling / Resilience

- **No JS / JS error:** scenes render as ordinary stacked sections (sticky still pins harmlessly, but with no `data-active` the robot shows its default/first state; all copy is present and readable). The page is fully functional without the script.
- **Reduced motion:** JS no-ops; CSS `@media (prefers-reduced-motion: reduce)` removes the pin (`position: static`), stacks copy above/around a single static robot per scene, and disables the sweep/typing/fall animations. Content unchanged.
- **Mobile (<820px):** JS no-ops; CSS unpins and stacks; robot shown once per scene at a sensible size; `data-reveal` still gives gentle fade/slide.
- **Resize across the breakpoint:** debounced re-init enables/disables the pinned behavior without reload.

## Performance

- Only `transform`/`opacity` transitions; no width/top/layout animation on scroll (the "typing" width animation is a one-shot on beat activation, not per-scroll-frame).
- Single rAF-throttled scroll listener for all scenes; observers do the rest (no continuous polling of layout).
- `will-change: transform` only on the sticky stage; removed when idle if needed.
- SVG stays inline and small; no image requests.

## Accessibility

- Copy remains real, in-order DOM text (screen-reader complete regardless of scroll state). The robot SVG is decorative (`aria-hidden`).
- Keyboard: no focus traps; native scrolling; CTA links/buttons keep visible focus.
- `prefers-reduced-motion: reduce` fully honored (no pin, no scrub, no auto-motion).
- No content is revealed *only* via scroll animation — reveal animations start from a visible-by-default baseline enhanced by JS, so nothing is hidden if animations don't run.

## Testing / Verification Strategy

Frontend, so verification is primarily visual + structural:

- **Screenshot critique** (headless Chrome, `--virtual-time-budget`) at several scroll offsets for both scenes on desktop width, and at mobile width — during the build, iterating.
- **Structural test (Web.Tests, WebApplicationFactory):** GET `/` returns 200 and contains the scene scaffolding (`class="scene--hero"`, `class="scene--night"`, the five `data-beat` values, `lp-scroll.js` reference) and still contains the key act copy (existing PublicPages assertions keep passing).
- **Reduced-motion / no-JS sanity:** the page's existing content assertions must not depend on JS; confirm all act copy is present in the served HTML.
- Manual: scroll both scenes in a real browser (Chrome + Safari) to confirm smoothness and the mobile fallback.

## Consequences

**Positive**
- A distinctive, memorable landing experience that dramatizes the product's value instead of describing it.
- Built on the existing brand robot and palette; no new dependencies, no build pipeline, no external assets.
- Fully degrades: reduced-motion, no-JS, and mobile all get a clean, readable page.

**Negative**
- The landing markup + CSS grow substantially; the robot SVG becomes considerably more complex (multiple stateful layers).
- Scroll choreography needs visual tuning (dwell lengths, easing, observer margins) — iterative screenshot/real-browser work, not unit-testable.
- Long scenes increase page height; must keep dwell lengths tight so the page doesn't feel padded.
- Cross-browser scroll/sticky quirks (esp. Safari) require manual verification.

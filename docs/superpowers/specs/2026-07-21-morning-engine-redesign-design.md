# Morning Engine — full redesign of landing page, customer zone, and emails

**Date:** 2026-07-21
**Status:** Approved by Andrzej (brainstorming session, visual companion mockup `morning-engine.html`)

## Problem

Two independent readers could not tell from the current site what Coldstart sells. Diagnosis (confirmed with Andrzej): they thought it was self-serve software, didn't understand "leads"/"buying signals" vocabulary, couldn't tell who it's for, and couldn't picture what actually arrives. The current retro-terminal cream/orange identity also reads as a recognizable AI-default look ("outdated and amateur" in Andrzej's words) and its gimmicks (fake live counters, blinking carets, arcade shadows) undermine trust for a €750/month service.

## Audience & message

- **Buyer:** founders/owners of small B2B companies (agencies, consultancies, SaaS, services) with no sales team and no sales vocabulary. Jargon-free copy throughout: no "pipeline", "outbound", "SDR", "buying signals" without immediate plain-language grounding.
- **The one takeaway a visitor must leave with:** *"They find companies that need what I sell, the morning it becomes true, and write the first email for me. I only approve. It's a done-for-you service, €750/month."*

## Concept: "The Morning Engine"

The landing page is structured like a Jobs keynote — the story builds until the product reveal feels inevitable — and the visual identity is derived from the product's own world: **one morning**. The page opens in daylight, drops into pre-dawn night while the machine works, and the reveal lands at sunrise. Machine moments are marked with monospace clock timestamps; human moments are set in the body face.

### Design tokens

Palette:

| Token | Hex | Role |
|---|---|---|
| `daylight` | `#FDFBF7` | light section background (warm white, not cream-yellow) |
| `ink` | `#16192B` | headings/text on light, dark CTA button |
| `night` | `#1C1F33` | pre-dawn section background |
| `night-raised` | `#242841` | cards on night (border `#33385A`, rule `#2C3049`) |
| `night-text` | `#E9E7F2` | body text on night (muted `#9BA1C4`, faint `#7C82A6`) |
| `sunrise` | `#FF6A3D` | accent + primary CTA (soft variant `#FF8A5C` for timestamps on night) |
| `body-muted` | `#5D5A50` | secondary text on light (subtle `#8B8578`) |
| dawn gradient | `#1C1F33 → #54425E → #C96F4A → #F5C97B → #FDFBF7` | the sunrise reveal band |

Typography:

- **Display:** Cabinet Grotesk (800/700/500) — Fontshare, self-hosted (no CDN at runtime).
- **Body:** Switzer (400/500/600) — Fontshare, self-hosted.
- **Mono:** system mono stack (`ui-monospace, Menlo, Consolas`), tabular numerals — used *only* for clock timestamps and machine metadata. This is a rule, not a preference: mono = the machine speaking.
- Wordmark: lowercase text `coldstart` + sunrise asterisk (`coldstart*`) — replaces the current logo.svg "C" block. Same orange family, new meaning.

Deleted brand elements: rainbow stripe, fake-live status bar with signal counter, blinking carets, `▚` glyphs, arcade `box-shadow: 0 Npx 0` buttons, the purple device-screen palette, the stats row, the timing-window chart.

Quality floor: responsive to mobile, visible keyboard focus, `prefers-reduced-motion` respected (gradient/scroll reveals become static), semantic headings.

## Landing page (`Pages/Index.cshtml`)

Keynote arc, top to bottom:

1. **Hero (daylight).** Plain-language promise: *"Wake up to your next client, already found — with the first email already written."* Sub: who it's for + the coffee/approve ritual. Primary CTA "Get 3 sample leads — free" (scrolls to form); secondary "See one morning ↓". Log in link top-right. No device mock, no stats.
2. **The enemy (daylight).** The spam the visitor themselves deletes: "I hope this email finds you well", sent to 5,000, answered by none. Why "doing outreach" feels dirty — and why they postpone it while their next client is out there now.
3. **The belief (daylight).** Conviction statements: write to few, not thousands; write the morning something happens to them; write about *their* situation; say no to almost everyone ("Out of 77 companies this morning, we rejected 74.").
4. **While you sleep (night).** The timestamped machine story, real data style: `06:58` funding news 11 minutes old → `06:59` checked against what *you* sell, 74/77 rejected → `07:04` four-sentence draft written from their news, shown verbatim as an email artifact card.
5. **Sunrise reveal (dawn gradient).** `07:30` — *"It's in your inbox."* The product name lands here: all of this already runs; it's called Coldstart.
6. **How it works & the AI's role (daylight).** Compact factual section, AI marketed openly: *"Yes, it's AI — doing the part AI is actually good at"* — reading thousands of public pages overnight, judging fit strictly, drafting from real facts, never inventing contacts. Paired trust line: a human (you) approves every single email; nothing sends itself. Punchline: this is why it's €750/month, not a €4,000 salary. Trust points folded in: public sources only, no invented contacts, you own every lead and draft.
7. **Transparent pricing (daylight).** €750/month ex VAT + €350 one-time setup ex VAT, explicit included list (niche configured for you, fresh leads every morning, each qualified + drafted, you approve and send, export anytime), cancel anytime, no lock-in, limited onboarding note.
8. **Closing CTA — sample-leads form.** One polished form (name, email, niche, what you sell): *"Tell us what you sell — we'll send 3 real leads for your niche, free, before you pay anything."* Posts to the existing `/Contact` handler. No mailto links anywhere on the page; the current modal composer + honeypot mechanics are kept but simplified into this inline form.

The timestamped story in act 4 is built from one of the real harvested examples already on the site (e.g. Patronus AI's funding round), so the current page's "real, unedited runs" honesty claim survives the redesign; the other real examples (GitLab, Stripe) appear as supporting artifacts in acts 4–6, not as a card grid.

## Customer zone (`Areas/App/Pages/Index.cshtml` + `Detail.cshtml`, `Profile.cshtml` reskin)

Identity applied (daylight surfaces, ink text, sunrise accent, mono timestamps), plus:

1. **Briefing header:** *"Good morning — 3 new leads found overnight"* with the latest harvest time in mono (falls back gracefully when there's nothing new: "No new leads this morning — next run tonight"). Data comes from the newest lead `CreatedAt` in the current lists; no new backend query beyond what the page model already loads.
2. **First-visit explainer strip** (dismissible, localStorage): we find leads overnight → you review and approve → the email goes out.
3. **Tabs renamed** (labels only, routes/data model unchanged): New → **To review**, Accepted → **Approved** (keeps Ready/Sent groups), Discarded → **Skipped**.
4. **Card layout reworked:** the drafted email (subject + first lines) is the card's hero; company, source, mono timestamp, and "why now" (MatchedSignals) are supporting context; Approve/Skip actions on the card. Drag-and-drop and existing move endpoints unchanged.
5. Empty states rewritten to say what happens next, not just "no leads".

## Emails (`Coldstart.Web/Email/EmailTemplates.cs`)

One shared shell — table-based, inline CSS, **no hosted images** (deliverability rule kept): `daylight` body, text wordmark `coldstart*`, ink headings, one sunrise CTA button, thin dawn-gradient strip (table-cell technique, replacing the 5-color rainbow bar), plain-text alternative for every template.

| Template | Status | Content notes |
|---|---|---|
| Invitation | restyle | + one plain sentence on what Coldstart does (recipient's first touchpoint) |
| Password reset | restyle | unchanged copy otherwise |
| Welcome (post-registration) | new | what happens next, when the first briefing arrives, link to /app |
| Morning briefing (daily digest) | new | "N new leads are waiting", timestamped like the night act, company + subject preview per lead, one CTA to /app |

**Scope note:** the morning-briefing *template* is in scope; the scheduled job that sends it is a separate follow-up implementation task (needs a sender loop + per-customer opt-in) and is explicitly **not** part of this redesign.

## Out of scope

- Admin area (`Areas/Admin`) — untouched except whatever it inherits from shared layout/CSS variables.
- Identity pages (login/register/forgot) — inherit the new tokens via shared CSS but get no structural redesign.
- The harvester/pipeline, pricing values, and the invite-only registration flow — unchanged.
- Sending infrastructure for the daily digest (follow-up task).

## Testing

- Existing 136 tests must stay green (tab renames touch labels only; `EmailTemplates` tests updated for new copy/subjects if they assert on content).
- New/updated unit tests for `EmailTemplates`: each template's subject, CTA URL presence in both text and HTML bodies, HTML-encoding of user-supplied values (company name), per project standards (xUnit + FluentAssertions, AAA).
- Visual verification pass on mobile widths + reduced motion before finishing.

## Consequences

**Positive:** the offer is understandable by a non-sales reader in one screen; identity is distinctive and owned (derived from the product, not a template); AI becomes a selling point with a built-in trust counterweight; email set covers the full customer lifecycle; retro gimmicks that read "amateur" are gone.

**Negative / accepted risks:** full visual break discards existing brand recognition (accepted — near-zero equity today); logo change touches favicon/OG assets; longer narrative page depends on copy quality; two Fontshare font families add ~100KB self-hosted assets; the daily digest promise on the landing page slightly precedes the sending job (mitigated: landing copy says "in your inbox" generically — /app inbox qualifies until the job ships).

# robots.txt + sitemap.xml (Search Console follow-up)

**Date:** 2026-08-03 · **Status:** Accepted

## Context

Google Search Console ownership of `getcoldstart.nl` was verified on 2026-08-03 (apex
TXT record via the Vimexx DNS template). The site has neither `robots.txt` nor
`sitemap.xml`; `wwwroot/site.webmanifest` already exists and is linked in the layout, so
no manifest work is needed. Google removed the sitemap "ping" endpoint in 2023 —
submission is a one-time manual paste in Search Console; the `Sitemap:` line in
robots.txt covers every other crawler.

## Decision

1. **`wwwroot/robots.txt`** (static): allow everything, `Disallow` the non-public
   surfaces `/app`, `/admin`, `/Identity/`, `/followups/`, `/webhooks/`, `/Error`, and
   end with `Sitemap: https://getcoldstart.nl/sitemap.xml`.
2. **`/sitemap.xml` is generated, not hand-written:** an internal static `Sitemap` class
   in Coldstart.Web holds the single hardcoded list of public routes — `/`, `/about`,
   `/contact`, `/examples`, `/faq`, `/technology`, `/terms`, `/privacy`,
   `/verwerkersovereenkomst`, `/inquiry` (the sample-request conversion page is included
   deliberately) — and renders sitemap-protocol XML with absolute URLs only (no
   `lastmod`/`changefreq`/`priority`; Google ignores them). `Program.cs` maps
   `GET /sitemap.xml` → `Results.Content(Sitemap.Xml(baseUrl), "application/xml")`.
3. **Base URL** comes from a new `PublicBaseUrl` config key, defaulting to
   `https://getcoldstart.nl` (the app sits behind Caddy without forwarded-headers
   middleware, so deriving scheme/host from the request is unreliable).
4. **Tests:** unit — `Sitemap.Xml` contains every route as an absolute URL, is
   well-formed XML, and trims a trailing slash on the base URL; integration (existing
   `ColdstartWebFactory`) — `GET /sitemap.xml` → 200 `application/xml` containing the
   apex URL, and `GET /robots.txt` → 200 containing the `Sitemap:` line.

## Consequences

**Positive:** crawlers get an explicit map and explicit keep-out list; the route list
lives in one tested place, so a missing new page is a one-line, reviewable change.
**Negative:** new public pages still require touching the `Sitemap` route list —
accepted over route-discovery magic for a ten-page site; `PublicBaseUrl` is one more
config knob (defaulted correctly for prod).

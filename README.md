# Coldstart

Coldstart finds B2B leads overnight and drafts the first email, so customers wake up to a
reviewed-and-ready outreach queue every morning. Multi-customer SaaS: **.NET 10, Razor Pages,
EF Core + PostgreSQL, ASP.NET Identity**, hexagonal architecture (`Domain` / `Application` /
`Infrastructure` / `Web`).

- **Harvest** — nightly per-customer hunt (HackerNews + funding RSS sources), deterministic
  signal scoring, fingerprint dedup, Groq enrichment (qualify + draft).
- **Review** — customers approve/edit/skip drafts in the app (`/app`); sending is a handoff
  to their own inbox (Gmail/mailto/copy).
- **Billing** — self-serve Stripe subscription (Checkout + Customer Portal + signature-verified
  webhook), Stripe Tax for EU VAT.
- **Admin** — invite-only registration, super-admin console, IMAP webmail for info@.

## Develop

```bash
docker compose up -d db        # PostgreSQL 17 on :5432
dotnet run --project src/Coldstart.Web   # http://localhost:5242 (migrates + seeds on start)
dotnet test                    # full suite (Web/Infra tests need Docker for Testcontainers)
```

Secrets live in `src/Coldstart.Web` user-secrets: `Llm:ApiKey` (Groq), `Stripe:*`
(SecretKey/WebhookSecret/PriceMonthlyId/PriceSetupId), `Mailbox:*` (admin webmail IMAP),
`OutreachMailbox:*` (branded send), `Smtp:*` (Resend transactional mail),
`Authentication:Google:*` (optional OAuth). For local Stripe webhooks:
`stripe listen --forward-to localhost:5242/webhooks/stripe`.

## Deploy

Docker + Caddy on a single host: `docker-compose.prod.yml` + `Caddyfile`, env template in
`deploy/.env.prod.example`.

## Docs

Design specs and implementation plans live in `docs/superpowers/{specs,plans}`; brand assets
in `docs/brand`; ADRs in `docs/adrs`.

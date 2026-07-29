# 0001 — Static API key for the admin API

**Date:** 2026-07-29
**Status:** Accepted

## Context

Coldstart needs terminal administration (operator + Claude via Bash) of the deployed
app: `/api/admin/*` endpoints consumed by a thin CLI. There is exactly one operator
(SuperAdmin). Existing auth is ASP.NET Identity cookies; there is no bearer/JWT setup.

## Decision

A single static API key sent as `X-Admin-Key`, verified by an endpoint filter on the
`/api/admin` route group with a constant-time comparison against `AdminApi:Key`
(user-secrets in dev, `deploy/.env.prod` in production). Fail closed: when no key is
configured, every route in the group returns 404. Transport security comes from
Caddy-terminated HTTPS. Cookie login for a CLI (antiforgery/session handling) and JWT
(issuer/signing/expiry machinery) were rejected as needless for a single operator.

## Consequences

**Positive:** trivially scriptable (`curl -H "X-Admin-Key: ..."`); no token expiry to
babysit; one config value; the filter is swappable for a real authentication scheme
without touching endpoint code if staff are ever added.

**Negative:** one shared secret with full admin power — rotation means changing one env
var and restarting; no per-action audit identity (there is only one operator); the key
must never be logged or committed.

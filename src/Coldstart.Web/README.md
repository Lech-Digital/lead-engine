# Coldstart.Web — local secrets

`appsettings.json` ships with empty placeholders for Google OAuth and SMTP
credentials. Do not put real secrets in that file — use the .NET Secret
Manager for local development instead.

## One-time setup

From `src/Coldstart.Web`:

```bash
dotnet user-secrets init
```

This adds a `UserSecretsId` to `Coldstart.Web.csproj` and stores secrets
outside the repo (`~/.microsoft/usersecrets/<id>/secrets.json` on macOS/Linux).

## Google external login

Google sign-in is **optional and additive**: `Program.cs` only registers the
Google authentication handler when both values below are present, so the
"Continue with Google" button on Login/Register only appears once configured.
With no creds set, the app runs email/password-only.

1. In the [Google Cloud Console](https://console.cloud.google.com/apis/credentials),
   create an OAuth 2.0 Client ID (Web application).
2. Add this **Authorized redirect URI** (adjust host/port for your local run):
   ```
   https://localhost:<port>/signin-google
   ```
3. Set the client ID/secret as user secrets:
   ```bash
   dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>"
   dotnet user-secrets set "Authentication:Google:ClientSecret" "<client-secret>"
   ```

First-time Google sign-in either links to an existing local account with a
matching email (merge-by-email) or creates and provisions a new one.

## SMTP (email sending)

Cold outreach and inquiry notifications send through Gmail SMTP by default
(`Smtp:Host`/`Port` already default to `smtp.gmail.com:465` in
`SmtpOptions`). Only the credentials need to be supplied:

```bash
dotnet user-secrets set "Smtp:Username" "<gmail-address>"
dotnet user-secrets set "Smtp:Password" "<gmail-app-password>"
```

Use a [Gmail App Password](https://myaccount.google.com/apppasswords)
(requires 2-Step Verification), not the account password.

## Verifying

```bash
dotnet user-secrets list --project src/Coldstart.Web
```

Tests never read these secrets — `ColdstartWebFactory` (in
`tests/Coldstart.Web.Tests`) swaps in a fake `IEmailSender` and runs with no
Google configuration, so the test suite is fully self-contained.

## Harvesting leads

`src/Coldstart.Harvester` is a standalone console runner that executes one
harvest pass (`IHarvestLeads`) against the real Coldstart niche: it pulls
Hacker News comments and startup-funding RSS feeds, scores/dedupes them
deterministically, has an LLM (Groq) qualify and draft outreach for the
survivors, and saves the fits under the customer named by
`Niche:TargetCustomerName` (get-or-created by name) — the same rows the
`/app` inbox reads.

**`Niche:TargetCustomerName` must match the email you log into `/app`
with.** The Customer Zone resolves its data by the logged-in account's
customer, so harvested leads only show up in the inbox if the harvester was
configured to target that same customer. `appsettings.json` ships it set to
the operator's login email (`andrzej.lech.andrzej@gmail.com`); update it if
the login account changes. The default in `NicheOptions` is `"Coldstart"`
(used by tests and if the setting is omitted).

The niche config and connection string live in
`src/Coldstart.Harvester/appsettings.json` (checked in — no secrets). Only
the Groq API key is a secret and must be set via the .NET Secret Manager:

```bash
dotnet user-secrets init --project src/Coldstart.Harvester   # one-time
dotnet user-secrets set "Llm:ApiKey" "<groq-api-key>" --project src/Coldstart.Harvester
```

Get a key from the [Groq console](https://console.groq.com/keys). Without
`Llm:ApiKey` set, `GroqLeadEnricher` skips enrichment (returns `null` per
item) rather than failing the run — harvesting/scoring/dedup still work, but
nothing gets saved.

Then, with local Postgres up and migrations applied:

```bash
docker compose up -d db
dotnet run --project src/Coldstart.Harvester
```

This prints a summary line:

```
harvested=95 scored_in=44 deduped=1 enriched=16 saved=1
```

`harvested` → raw items pulled from HN + RSS; `scored_in` → passed the
deterministic keyword/signal score gate (`Niche:MinScore`); `deduped` →
already seen (by content fingerprint); `enriched` → the LLM returned a
parseable result (regardless of fit); `saved` → `FIT: YES`/`MAYBE` leads
persisted. A low `saved` count relative to `enriched` is often correct — the
niche's disqualifiers (competitors, job seekers, anonymous forum chatter,
large enterprises) are intentionally strict. A `429` from Groq on a given
item just means that item gets skipped and picked up again on the next run;
it does not fail the batch. Verify saved leads directly:

```bash
docker compose exec -T db psql -U coldstart -d coldstart \
  -c "select \"Company\", \"Fit\", \"Score\", \"Source\" from leads where \"Source\" in ('hackernews','funding') order by \"Score\" desc limit 10;"
```

Run it as often as you like — it is idempotent per item (fingerprint dedup)
and safe to re-run against the same database.

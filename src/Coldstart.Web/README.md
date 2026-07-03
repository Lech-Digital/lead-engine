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

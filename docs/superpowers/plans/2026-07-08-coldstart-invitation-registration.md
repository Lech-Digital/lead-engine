# Invitation Registration (Spec A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Coldstart invite-only — seed a SuperAdmin, add a password-set/reset flow, kill public registration, and let the SuperAdmin invite company admins who set their own password via an emailed link.

**Architecture:** Every account is created with a random password; the person sets their real password via the ASP.NET Identity password-reset token (the same mechanism powers the SuperAdmin bootstrap, invites, and normal resets). Roles (`SuperAdmin`/`CompanyAdmin`/`User`) are Identity roles. Invite state lives on `ApplicationUser` (`EmailConfirmed` = accepted, new `InvitedAt` = when invited) — no separate invitation entity. Invite orchestration lives in the `/admin` page handler, matching the existing `Register` page pattern.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, ASP.NET Core Identity + EF Core (Postgres), MailKit SMTP (`IEmailSender`, already wired), xUnit + FluentAssertions, Testcontainers + a `FakeEmailSender`.

## Global Constraints

- Domain project has zero dependencies on other projects; ports live in Application; DI in composition roots only (CS-HEX). Identity user management + invite orchestration are composition/Web concerns (the existing `Register` page already calls `UserManager` + `IProvisionCustomer` directly — follow that).
- Async all the way; pass `CancellationToken` to IO (CS-DATA-03).
- **No `Co-Authored-By` trailers on commits.**
- Build must stay at **0 warnings**; full suite green after every task.
- Roles are exactly `SuperAdmin`, `CompanyAdmin`, `User`. SuperAdmin email is the hardcoded constant `adzius.lech@gmail.com`.
- Reset-token lifespan is **7 days**.
- Public `/Identity/Account/Register` is disabled (redirects to Login); the only way to create an account is accepting an invite / setting a password via emailed link.
- Web tests use `ColdstartWebFactory` (Testcontainers Postgres) with the singleton `FakeEmailSender` exposed as `factory.Email` (capture list `factory.Email.Sent` of `(To, Subject, Body)`).

---

### Task 1: Roles constants + seed roles and the SuperAdmin account on startup

**Files:**
- Create: `src/Coldstart.Web/Identity/ColdstartRoles.cs`
- Create: `src/Coldstart.Web/Identity/IdentitySeeder.cs`
- Modify: `src/Coldstart.Web/Program.cs` (call the seeder in the startup scope)
- Test: `tests/Coldstart.Web.Tests/IdentitySeedTests.cs` (create)

**Interfaces:**
- Produces:
  - `public static class ColdstartRoles { public const string SuperAdmin = "SuperAdmin"; public const string CompanyAdmin = "CompanyAdmin"; public const string User = "User"; public const string SuperAdminEmail = "adzius.lech@gmail.com"; public static readonly string[] All = [SuperAdmin, CompanyAdmin, User]; }`
  - `public static class IdentitySeeder { public static Task SeedAsync(IServiceProvider services, CancellationToken ct); }`

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/IdentitySeedTests.cs`:

```csharp
using Coldstart.Web;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class IdentitySeedTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Startup_SeedsThreeRoles()
    {
        // Arrange
        using var _ = factory.CreateClient();   // forces the host (and startup seeding) to build
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Act / Assert
        foreach (var role in ColdstartRoles.All)
            (await roles.RoleExistsAsync(role)).Should().BeTrue($"role {role} should be seeded");
    }

    [Fact]
    public async Task Startup_SeedsSuperAdminUserInSuperAdminRole()
    {
        // Arrange
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Act
        var admin = await users.FindByEmailAsync(ColdstartRoles.SuperAdminEmail);

        // Assert
        admin.Should().NotBeNull();
        admin!.EmailConfirmed.Should().BeTrue();
        admin.CustomerId.Should().BeNull();
        (await users.IsInRoleAsync(admin, ColdstartRoles.SuperAdmin)).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter IdentitySeedTests`
Expected: FAIL — `ColdstartRoles` / `IdentitySeeder` not found (compile error), and no roles/user seeded.

- [ ] **Step 3: Create `ColdstartRoles.cs`**

```csharp
namespace Coldstart.Web;

public static class ColdstartRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string CompanyAdmin = "CompanyAdmin";
    public const string User = "User";

    // Hardcoded operator identity — seeded on startup, sets its own password via Forgot password.
    public const string SuperAdminEmail = "adzius.lech@gmail.com";

    public static readonly string[] All = [SuperAdmin, CompanyAdmin, User];
}
```

- [ ] **Step 4: Create `IdentitySeeder.cs`**

```csharp
using System.Security.Cryptography;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Web;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in ColdstartRoles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(ColdstartRoles.SuperAdminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = ColdstartRoles.SuperAdminEmail,
                Email = ColdstartRoles.SuperAdminEmail,
                EmailConfirmed = true,   // trusted, known identity; sets password via Forgot password
            };
            // Random, unusable password — the operator sets a real one via the reset flow.
            await users.CreateAsync(admin, "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
            await users.AddToRoleAsync(admin, ColdstartRoles.SuperAdmin);
        }
    }
}
```

- [ ] **Step 5: Call the seeder from `Program.cs`**

In `src/Coldstart.Web/Program.cs`, inside the existing startup scope block, add after `DbSeeder.SeedAsync(...)`:

```csharp
    await DbSeeder.SeedAsync(db, CancellationToken.None);
    await Coldstart.Web.IdentitySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter IdentitySeedTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Identity/ColdstartRoles.cs src/Coldstart.Web/Identity/IdentitySeeder.cs src/Coldstart.Web/Program.cs tests/Coldstart.Web.Tests/IdentitySeedTests.cs
git commit -m "feat(auth): seed SuperAdmin/CompanyAdmin/User roles + hardcoded SuperAdmin account"
```

---

### Task 2: Password-set / reset flow (ForgotPassword + ResetPassword) + 7-day token + role redirect

**Files:**
- Create: `src/Coldstart.Web/Identity/RoleRedirect.cs`
- Create: `src/Coldstart.Web/Areas/Identity/Pages/Account/ForgotPassword.cshtml`
- Create: `src/Coldstart.Web/Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs`
- Create: `src/Coldstart.Web/Areas/Identity/Pages/Account/ResetPassword.cshtml`
- Create: `src/Coldstart.Web/Areas/Identity/Pages/Account/ResetPassword.cshtml.cs`
- Modify: `src/Coldstart.Web/Program.cs` (configure 7-day reset-token lifespan)
- Test: `tests/Coldstart.Web.Tests/PasswordResetTests.cs` (create)

**Interfaces:**
- Consumes: `ColdstartRoles`, `IEmailSender` (`Coldstart.Application.Ports`), `UserManager<ApplicationUser>`, `SignInManager<ApplicationUser>`.
- Produces:
  - `internal static class RoleRedirect { internal static async Task<string> PathForAsync(UserManager<ApplicationUser> users, ApplicationUser user, CancellationToken ct); }` → returns `"/admin"` for SuperAdmin else `"/app"`.
  - Pages at `/Identity/Account/ForgotPassword` and `/Identity/Account/ResetPassword` (the reset link uses query params `code` (base64url-encoded token) and `email`).

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/PasswordResetTests.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;
using Coldstart.Web;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class PasswordResetTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task ForgotPassword_ForKnownUser_EmailsAResetLink()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"reset-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!");
        factory.Email.Sent.Clear();

        // Act
        await TestAuth.PostFormAsync(client, "/Identity/Account/ForgotPassword",
            new() { ["Input.Email"] = email });

        // Assert
        factory.Email.Sent.Should().ContainSingle(m => m.To == email);
        factory.Email.Sent.Single().Body.Should().Contain("/Identity/Account/ResetPassword");
    }

    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_ShowsNeutralMessageAndSendsNothing()
    {
        // Arrange
        var client = factory.CreateClient();
        factory.Email.Sent.Clear();

        // Act
        var html = await TestAuth.PostFormReadAsync(client, "/Identity/Account/ForgotPassword",
            new() { ["Input.Email"] = $"nobody-{Guid.NewGuid():N}@test.com" });

        // Assert — no enumeration: neutral confirmation, no email sent
        factory.Email.Sent.Should().BeEmpty();
        html.Should().Contain("we've sent");
    }

    [Fact]
    public async Task ResetPassword_ViaEmailedLink_SetsPasswordAndSignsIn()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"reset2-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "OldPassw0rd!");
        factory.Email.Sent.Clear();
        await TestAuth.PostFormAsync(client, "/Identity/Account/ForgotPassword", new() { ["Input.Email"] = email });
        var link = Regex.Match(factory.Email.Sent.Single().Body, @"/Identity/Account/ResetPassword\?\S+").Value;
        var code = Regex.Match(link, @"code=([^&\s]+)").Groups[1].Value;

        // Act — post a new password using the code from the link
        await TestAuth.PostFormAsync(client, "/Identity/Account/ResetPassword", new()
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.Password"] = "BrandNewP4ss!",
            ["Input.ConfirmPassword"] = "BrandNewP4ss!",
        });

        // Assert — the new password now authenticates
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        (await users.CheckPasswordAsync(user!, "BrandNewP4ss!")).Should().BeTrue();
        user!.EmailConfirmed.Should().BeTrue();
    }
}
```

This test references `TestAuth` helpers created in Task 3. **To keep tasks independently runnable, create the minimal `TestAuth` now** (Task 3 expands it). Create `tests/Coldstart.Web.Tests/TestAuth.cs`:

```csharp
using System.Text.RegularExpressions;
using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Web.Tests;

// Test-only helpers for creating accounts and driving the auth forms, now that public
// registration is gone. Users are created directly via UserManager with a known password.
internal static class TestAuth
{
    internal static async Task CreateUserAsync(ColdstartWebFactory factory, string email, string password,
        string? role = null, bool provisionTenant = true)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        await users.CreateAsync(user, password);
        if (provisionTenant)
        {
            var provision = scope.ServiceProvider.GetRequiredService<IProvisionCustomer>();
            user.CustomerId = await provision.ProvisionAsync(email, CancellationToken.None);
            await users.UpdateAsync(user);
        }
        if (role is not null) await users.AddToRoleAsync(user, role);
    }

    // Reads a page, pulls its antiforgery token (Razor Pages emits one for every method=post form).
    internal static async Task<string?> AntiforgeryAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    internal static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path,
        Dictionary<string, string> fields)
    {
        var token = await AntiforgeryAsync(client, path);
        if (token is not null) fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    internal static async Task<string> PostFormReadAsync(HttpClient client, string path,
        Dictionary<string, string> fields)
    {
        var res = await PostFormAsync(client, path, fields);
        return await res.Content.ReadAsStringAsync();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter PasswordResetTests`
Expected: FAIL — ForgotPassword/ResetPassword pages return 404, so no email is captured.

- [ ] **Step 3: Configure the 7-day token lifespan in `Program.cs`**

In `src/Coldstart.Web/Program.cs`, add after the `AddIdentity(...)` block (before `var app = builder.Build();`):

```csharp
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(7));
```

(`DataProtectionTokenProviderOptions` is in `Microsoft.AspNetCore.Identity`; the file already has `using Microsoft.AspNetCore.Identity;`.)

- [ ] **Step 4: Create `RoleRedirect.cs`**

```csharp
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Coldstart.Web;

internal static class RoleRedirect
{
    // SuperAdmins land on /admin; everyone else on /app.
    internal static async Task<string> PathForAsync(UserManager<ApplicationUser> users, ApplicationUser user, CancellationToken ct) =>
        await users.IsInRoleAsync(user, ColdstartRoles.SuperAdmin) ? "/admin" : "/app";
}
```

- [ ] **Step 5: Create `ForgotPassword.cshtml.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

public sealed class ForgotPasswordModel(UserManager<ApplicationUser> users, IEmailSender email) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Sent { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Input.Email);
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = Url.Page("/Account/ResetPassword", pageHandler: null,
                values: new { area = "Identity", code, email = Input.Email }, protocol: Request.Scheme)!;
            await email.SendAsync(Input.Email, "Reset your Coldstart password",
                $"Set a new password for your Coldstart account:\n\n{link}\n\nThis link expires in 7 days. If you didn't ask for this, ignore this email.",
                ct);
        }

        Sent = true;   // neutral result regardless of whether the account exists
        return Page();
    }

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
    }
}
```

- [ ] **Step 6: Create `ForgotPassword.cshtml`**

```html
@page "/Identity/Account/ForgotPassword"
@model Coldstart.Web.Areas.Identity.Pages.Account.ForgotPasswordModel
@{ ViewData["Title"] = "Forgot password"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<main class="wrap auth-shell">
  <div class="auth-card">
    <h1>Reset your password</h1>
    @if (Model.Sent)
    {
      <p class="form-status">If that account exists, we've sent a link to set a new password.</p>
    }
    else
    {
      <form method="post">
        <div class="field">
          <label asp-for="Input.Email">Email</label>
          <input asp-for="Input.Email" type="email" autocomplete="email" placeholder="you@company.com" required>
          <span asp-validation-for="Input.Email"></span>
        </div>
        <button class="btn btn-primary" type="submit">Send reset link →</button>
      </form>
    }
    <p class="auth-alt"><a asp-page="Login">Back to sign in</a></p>
  </div>
</main>
```

- [ ] **Step 7: Create `ResetPassword.cshtml.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

public sealed class ResetPasswordModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IActionResult OnGet(string? code = null, string? email = null)
    {
        if (string.IsNullOrEmpty(code))
            return BadRequest("A password-set code is required.");
        Input = new InputModel { Code = code, Email = email ?? "" };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            Errors = ["This reset link is no longer valid. Request a new one."];
            return Page();
        }

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code));
        var result = await users.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded)
        {
            Errors = result.Errors.Select(e => e.Description).ToList();
            return Page();
        }

        if (!user.EmailConfirmed) { user.EmailConfirmed = true; await users.UpdateAsync(user); }
        await signIn.SignInAsync(user, isPersistent: false);
        return LocalRedirect(await RoleRedirect.PathForAsync(users, user, ct));
    }

    public sealed class InputModel
    {
        [Required] public string Email { get; set; } = "";
        [Required] public string Code { get; set; } = "";
        [Required, DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = "";
        [Required, DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
```

- [ ] **Step 8: Create `ResetPassword.cshtml`**

```html
@page "/Identity/Account/ResetPassword"
@model Coldstart.Web.Areas.Identity.Pages.Account.ResetPasswordModel
@{ ViewData["Title"] = "Set your password"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<main class="wrap auth-shell">
  <div class="auth-card">
    <h1>Set your password</h1>
    @if (Model.Errors.Count > 0)
    {
      <div class="field-error">@string.Join(" ", Model.Errors)</div>
    }
    <form method="post">
      <input asp-for="Input.Email" type="hidden">
      <input asp-for="Input.Code" type="hidden">
      <div class="field">
        <label asp-for="Input.Password">New password</label>
        <input asp-for="Input.Password" type="password" autocomplete="new-password" required>
        <span asp-validation-for="Input.Password"></span>
      </div>
      <div class="field">
        <label asp-for="Input.ConfirmPassword">Confirm password</label>
        <input asp-for="Input.ConfirmPassword" type="password" autocomplete="new-password" required>
        <span asp-validation-for="Input.ConfirmPassword"></span>
      </div>
      <button class="btn btn-primary" type="submit">Set password & sign in →</button>
    </form>
  </div>
</main>
```

- [ ] **Step 9: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter PasswordResetTests`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/Coldstart.Web/Identity/RoleRedirect.cs src/Coldstart.Web/Areas/Identity/Pages/Account/ForgotPassword.cshtml src/Coldstart.Web/Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs src/Coldstart.Web/Areas/Identity/Pages/Account/ResetPassword.cshtml src/Coldstart.Web/Areas/Identity/Pages/Account/ResetPassword.cshtml.cs src/Coldstart.Web/Program.cs tests/Coldstart.Web.Tests/TestAuth.cs tests/Coldstart.Web.Tests/PasswordResetTests.cs
git commit -m "feat(auth): forgot/reset password flow with 7-day token + role-based redirect"
```

---

### Task 3: Migrate existing tests off the public Register page

**Files:**
- Modify: `tests/Coldstart.Web.Tests/TestAuth.cs` (add `SignInAsync`)
- Modify: `tests/Coldstart.Web.Tests/AuthFlowTests.cs`
- Modify: `tests/Coldstart.Web.Tests/CustomerZoneTests.cs`
- Modify: `tests/Coldstart.Web.Tests/LeadDetailTests.cs`
- Modify: `tests/Coldstart.Web.Tests/LeadReviewTests.cs`

**Interfaces:**
- Consumes: `TestAuth.CreateUserAsync` (Task 2).
- Produces: `internal static Task SignInAsync(ColdstartWebFactory factory, HttpClient client, string email, string password, string? role = null)` — creates the account (if needed) and signs the given client in via the Login form. Replaces `AuthFlowTests.RegisterAsync`.

This is a pure test refactor; the public Register page still works here, so the suite stays green throughout. It must happen **before** Task 4 disables Register.

- [ ] **Step 1: Add `SignInAsync` to `TestAuth`**

Append to `tests/Coldstart.Web.Tests/TestAuth.cs` (inside the class):

```csharp
    // Creates the account (known password, confirmed, provisioned tenant unless role is SuperAdmin)
    // then signs `client` in through the Login form — the replacement for the removed Register flow.
    internal static async Task SignInAsync(ColdstartWebFactory factory, HttpClient client,
        string email, string password, string? role = null)
    {
        await CreateUserAsync(factory, email, password, role, provisionTenant: role != ColdstartRoles.SuperAdmin);
        await PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
        });
    }
```

- [ ] **Step 2: Replace `RegisterAsync` call sites**

In each of `AuthFlowTests.cs`, `CustomerZoneTests.cs`, `LeadDetailTests.cs`, `LeadReviewTests.cs`, replace every call of the form:

```csharp
await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
```

with (note the added `factory` argument; `factory` is the fixture field/ctor param already present in each class):

```csharp
await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
```

Do the same for the differently-named clients (e.g. `clientA`/`emailA`, `owner`/`ownerEmail`, `attacker`). There are 13 call sites total (5 in `CustomerZoneTests`, 4 in `LeadDetailTests`, 1 in `AuthFlowTests`, 5 in `LeadReviewTests` — including the `AddLeadForUserAsync` helper's first line). Replace each in place.

- [ ] **Step 3: Remove the old `RegisterAsync` helper and its now-redundant test**

In `tests/Coldstart.Web.Tests/AuthFlowTests.cs`:
- Delete the `internal static async Task RegisterAsync(HttpClient client, ...)` method (its whole body).
- Delete the `Register_ProvisionsCustomerWithNoLeads` test (a "register is disabled" test replaces it in Task 4).
- Keep `GetApp_WhenNotAuthenticated_RedirectsToLogin` and `Login_WithoutGoogleConfigured_ShowsEmailPasswordOnly`.

- [ ] **Step 4: Run the full web suite to verify green**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj`
Expected: PASS (all previously-green tests still pass via `SignInAsync`).

- [ ] **Step 5: Commit**

```bash
git add tests/Coldstart.Web.Tests/
git commit -m "test(auth): create+login users via TestAuth instead of the public Register page"
```

---

### Task 4: Disable public registration

**Files:**
- Modify: `src/Coldstart.Web/Areas/Identity/Pages/Account/Register.cshtml.cs` (redirect both handlers)
- Modify: `src/Coldstart.Web/Areas/Identity/Pages/Account/Login.cshtml` (drop Register link, add Forgot-password link)
- Modify: `src/Coldstart.Web/Areas/Identity/Pages/Account/Login.cshtml.cs` (redirect by role after sign-in)
- Test: `tests/Coldstart.Web.Tests/AuthFlowTests.cs` (add register-disabled test)

**Interfaces:**
- Consumes: `RoleRedirect.PathForAsync` (Task 2), `ColdstartRoles`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Coldstart.Web.Tests/AuthFlowTests.cs`:

```csharp
    [Fact]
    public async Task Register_IsDisabled_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/Identity/Account/Register");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Login_AsSuperAdmin_RedirectsToAdmin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"sa-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!", ColdstartRoles.SuperAdmin, provisionTenant: false);

        // Act
        var response = await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/admin");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AuthFlowTests`
Expected: FAIL — `/Register` still serves the form (200, not redirect); SuperAdmin login redirects to `/app` not `/admin`.

- [ ] **Step 3: Disable the Register handlers**

Replace the body of `src/Coldstart.Web/Areas/Identity/Pages/Account/Register.cshtml.cs` with a minimal page model that redirects (keeps the route registered but unusable):

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

// Public self-registration is disabled — Coldstart is invite-only. Both verbs bounce to Login.
public sealed class RegisterModel : PageModel
{
    public IActionResult OnGet() => Redirect("/Identity/Account/Login");
    public IActionResult OnPost() => Redirect("/Identity/Account/Login");
}
```

- [ ] **Step 4: Fix `Register.cshtml` so it references no removed members**

Replace `src/Coldstart.Web/Areas/Identity/Pages/Account/Register.cshtml` with a minimal stub (the handler redirects on GET, so this is never rendered, but it must still compile against the new model):

```html
@page "/Identity/Account/Register"
@model Coldstart.Web.Areas.Identity.Pages.Account.RegisterModel
@{ Layout = null; }
```

- [ ] **Step 5: Update the Login page links**

In `src/Coldstart.Web/Areas/Identity/Pages/Account/Login.cshtml`, replace line 53:

```html
    <p class="auth-alt"><a asp-page="Register">Need an account? Register</a></p>
```

with:

```html
    <p class="auth-alt"><a asp-page="ForgotPassword">Forgot your password?</a></p>
```

- [ ] **Step 6: Redirect by role after login**

In `src/Coldstart.Web/Areas/Identity/Pages/Account/Login.cshtml.cs`, add `UserManager<ApplicationUser>` to the constructor and use the role redirect when no explicit returnUrl was given. The success branch currently reads `return LocalRedirect(returnUrl ?? "/app");`. Change the constructor to include `UserManager<ApplicationUser> users` and replace that line with:

```csharp
        if (returnUrl is not null) return LocalRedirect(returnUrl);
        var user = await users.FindByEmailAsync(Input.Email);
        return LocalRedirect(await RoleRedirect.PathForAsync(users, user!, CancellationToken.None));
```

(Add `using Coldstart.Infrastructure.Identity;` and `using Microsoft.AspNetCore.Identity;` and `using Coldstart.Web;` if not present.)

- [ ] **Step 7: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AuthFlowTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Coldstart.Web/Areas/Identity/Pages/Account/Register.cshtml src/Coldstart.Web/Areas/Identity/Pages/Account/Register.cshtml.cs src/Coldstart.Web/Areas/Identity/Pages/Account/Login.cshtml src/Coldstart.Web/Areas/Identity/Pages/Account/Login.cshtml.cs tests/Coldstart.Web.Tests/AuthFlowTests.cs
git commit -m "feat(auth): disable public registration; login redirects by role"
```

---

### Task 5: `ApplicationUser.InvitedAt` + migration

**Files:**
- Modify: `src/Coldstart.Infrastructure/Identity/ApplicationUser.cs`
- Create: EF migration under `src/Coldstart.Infrastructure/Migrations/`
- Test: `tests/Coldstart.Web.Tests/InvitedAtTests.cs` (create)

**Interfaces:**
- Produces: `ApplicationUser.InvitedAt` → `public DateTimeOffset? InvitedAt { get; set; }`.

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/InvitedAtTests.cs`:

```csharp
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class InvitedAtTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task InvitedAt_PersistsOnApplicationUser()
    {
        // Arrange
        using var _ = factory.CreateClient();
        var email = $"inv-{System.Guid.NewGuid():N}@test.com";
        var when = System.DateTimeOffset.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, InvitedAt = when };
            await users.CreateAsync(user, "Passw0rd!");
        }

        // Assert
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            user!.InvitedAt.Should().BeCloseTo(when, System.TimeSpan.FromSeconds(5));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter InvitedAtTests`
Expected: FAIL — `ApplicationUser` has no `InvitedAt` (compile error).

- [ ] **Step 3: Add the property**

In `src/Coldstart.Infrastructure/Identity/ApplicationUser.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace Coldstart.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid? CustomerId { get; set; }
    public DateTimeOffset? InvitedAt { get; set; }
}
```

- [ ] **Step 4: Add the migration**

Run (from repo root):

```bash
dotnet ef migrations add AddUserInvitedAt \
  --project src/Coldstart.Infrastructure \
  --startup-project src/Coldstart.Web \
  --output-dir Migrations
```

Verify the generated migration adds an `InvitedAt` column (nullable `timestamptz`) to `AspNetUsers`.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter InvitedAtTests`
Expected: PASS (startup `MigrateAsync` applies the new column).

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Infrastructure/Identity/ApplicationUser.cs src/Coldstart.Infrastructure/Migrations/ tests/Coldstart.Web.Tests/InvitedAtTests.cs
git commit -m "feat(auth): track ApplicationUser.InvitedAt for invite expiry display"
```

---

### Task 6: `/admin` invite page — SuperAdmin invites a company admin

**Files:**
- Create: `src/Coldstart.Web/Areas/Admin/Pages/_ViewImports.cshtml`
- Create: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml`
- Create: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs`
- Test: `tests/Coldstart.Web.Tests/AdminInviteTests.cs` (create)

**Interfaces:**
- Consumes: `UserManager<ApplicationUser>`, `IProvisionCustomer` (`Coldstart.Application.Customers`), `IEmailSender` (`Coldstart.Application.Ports`), `ColdstartRoles`.
- Produces: page at `/admin` gated by `[Authorize(Roles = ColdstartRoles.SuperAdmin)]`; POST creates a `CompanyAdmin` account (random password, new tenant named from company, `EmailConfirmed=false`, `InvitedAt=now`) and emails a set-password link.

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/AdminInviteTests.cs`:

```csharp
using System.Net;
using Coldstart.Web;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class AdminInviteTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Admin_ForNonSuperAdmin_IsDenied()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"plain-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");   // no role

        // Act
        var response = await client.GetAsync("/admin");

        // Assert — not a SuperAdmin => bounced to login/access-denied
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_CreatesCompanyAdminWithTenant_AndEmailsLink()
    {
        // Arrange
        var client = factory.CreateClient();
        var admin = $"boss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"founder-{Guid.NewGuid():N}@test.com";
        factory.Email.Sent.Clear();

        // Act
        await TestAuth.PostFormAsync(client, "/admin", new()
        {
            ["Input.Company"] = "Acme Co", ["Input.Email"] = invitee,
        });

        // Assert
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(invitee);
        user.Should().NotBeNull();
        user!.EmailConfirmed.Should().BeFalse();
        user.InvitedAt.Should().NotBeNull();
        user.CustomerId.Should().NotBeNull();
        (await users.IsInRoleAsync(user, ColdstartRoles.CompanyAdmin)).Should().BeTrue();
        (await db.Customers.AnyAsync(c => c.Id == new CustomerId(user.CustomerId!.Value))).Should().BeTrue();
        factory.Email.Sent.Should().ContainSingle(m => m.To == invitee);
        factory.Email.Sent.Single().Body.Should().Contain("/Identity/Account/ResetPassword");
    }

    [Fact]
    public async Task Invite_ForExistingEmail_DoesNotCreateSecondAccount()
    {
        // Arrange
        var client = factory.CreateClient();
        var admin = $"boss2-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!", ColdstartRoles.SuperAdmin);
        var existing = $"dup-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, existing, "Passw0rd!");   // already registered
        factory.Email.Sent.Clear();

        // Act
        var html = await TestAuth.PostFormReadAsync(client, "/admin", new()
        {
            ["Input.Company"] = "Dup Co", ["Input.Email"] = existing,
        });

        // Assert — surfaced as an error; no invite email
        html.Should().Contain("already has an account");
        factory.Email.Sent.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AdminInviteTests`
Expected: FAIL — `/admin` returns 404.

- [ ] **Step 3: Create `Areas/Admin/Pages/_ViewImports.cshtml`**

```html
@using Coldstart.Web.Areas.Admin.Pages
@namespace Coldstart.Web.Areas.Admin.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

- [ ] **Step 4: Create `Areas/Admin/Pages/Index.cshtml.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Coldstart.Web.Areas.Admin.Pages;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class IndexModel(
    UserManager<ApplicationUser> users,
    IProvisionCustomer provision,
    IEmailSender email) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }
    public IReadOnlyList<Row> Pending { get; private set; } = [];
    public IReadOnlyList<Row> Accepted { get; private set; } = [];

    public sealed record Row(string Email, int? DaysLeft);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        if (await users.FindByEmailAsync(Input.Email) is not null)
        {
            ModelState.AddModelError("Input.Email", "That person already has an account.");
            await LoadAsync();
            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            EmailConfirmed = false,
            InvitedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, RandomPassword());
        if (!created.Succeeded)
        {
            foreach (var e in created.Errors) ModelState.AddModelError("", e.Description);
            await LoadAsync();
            return Page();
        }

        await users.AddToRoleAsync(user, ColdstartRoles.CompanyAdmin);
        user.CustomerId = await provision.ProvisionAsync(Input.Company, ct);
        await users.UpdateAsync(user);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = Url.Page("/Account/ResetPassword", pageHandler: null,
            values: new { area = "Identity", code, email = Input.Email }, protocol: Request.Scheme)!;
        await email.SendAsync(Input.Email, "You've been invited to Coldstart",
            $"You've been invited to Coldstart as the admin for {Input.Company}.\n\n" +
            $"Set your password to get started:\n\n{link}\n\nThis link expires in 7 days.", ct);

        StatusMessage = $"Invitation sent to {Input.Email}.";
        Input = new InputModel();
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var admins = await users.GetUsersInRoleAsync(ColdstartRoles.CompanyAdmin);
        Pending = admins.Where(u => !u.EmailConfirmed)
            .Select(u => new Row(u.Email!, DaysLeft(u.InvitedAt))).ToList();
        Accepted = admins.Where(u => u.EmailConfirmed)
            .Select(u => new Row(u.Email!, null)).ToList();
    }

    private static int? DaysLeft(DateTimeOffset? invitedAt) =>
        invitedAt is { } t ? Math.Max(0, 7 - (int)(DateTimeOffset.UtcNow - t).TotalDays) : null;

    private static string RandomPassword() =>
        "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public sealed class InputModel
    {
        [Required] public string Company { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
    }
}
```

- [ ] **Step 5: Create `Areas/Admin/Pages/Index.cshtml`**

```html
@page "/admin"
@model Coldstart.Web.Areas.Admin.Pages.IndexModel
@{ ViewData["Title"] = "Admin"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<header class="app-bar">
  <a class="brand" href="/"><img src="/logo.svg" alt="Coldstart" style="height:30px;width:auto;display:block;"></a>
  <nav class="app-nav">
    <form method="post" asp-page="/Account/Logout" asp-area="Identity"><button class="btn btn-ghost" type="submit">Log out</button></form>
  </nav>
</header>

<main class="wrap app-zone">
  <h1>Company admins</h1>
  @if (Model.StatusMessage is not null) { <p class="form-status">@Model.StatusMessage</p> }

  <form method="post" class="admin-invite">
    <div asp-validation-summary="All" class="field-error"></div>
    <div class="field"><label asp-for="Input.Company">Company name</label><input asp-for="Input.Company" required></div>
    <div class="field"><label asp-for="Input.Email">Admin email</label><input asp-for="Input.Email" type="email" required></div>
    <button class="btn btn-primary" type="submit">Send invite →</button>
  </form>

  <h2 class="app-group-head">Pending</h2>
  @if (Model.Pending.Count == 0) { <p class="app-group-empty">No pending invites.</p> }
  else
  {
    <ul class="app-links">
      @foreach (var r in Model.Pending) { <li><span class="app-link-out">@r.Email · expires in @r.DaysLeft d</span></li> }
    </ul>
  }

  <h2 class="app-group-head">Accepted</h2>
  @if (Model.Accepted.Count == 0) { <p class="app-group-empty">No accepted admins yet.</p> }
  else
  {
    <ul class="app-links">
      @foreach (var r in Model.Accepted) { <li><span class="app-link-out">@r.Email</span></li> }
    </ul>
  }
</main>
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AdminInviteTests`
Expected: PASS.

- [ ] **Step 7: Full build + suite**

Run: `dotnet build src/Coldstart.Web -clp:ErrorsOnly` then `dotnet test Coldstart.sln`
Expected: build 0 warnings; all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/Coldstart.Web/Areas/Admin/ tests/Coldstart.Web.Tests/AdminInviteTests.cs
git commit -m "feat(admin): SuperAdmin /admin page to invite company admins via emailed set-password link"
```

---

## Self-Review

**Spec coverage:**
- Roles seeded + SuperAdmin policy → Task 1 (roles), Task 6 (`[Authorize(Roles=SuperAdmin)]`). `/app` stays `[Authorize]` (unchanged). ✔
- SuperAdmin bootstrap (hardcoded email, random pw, EmailConfirmed, sets pw via Forgot password) → Task 1 + Task 2. ✔
- Password-set/reset flow (ForgotPassword neutral message, ResetPassword sets pw + EmailConfirmed + sign-in, 7-day token) → Task 2. ✔
- Disable public registration + remove links → Task 4. ✔
- Invite a company admin (create account now: random pw, CompanyAdmin role, provisioned tenant from company name, EmailConfirmed=false, InvitedAt, emailed link; already-exists handled) → Task 5 (`InvitedAt`) + Task 6. ✔
- `/admin` surface with invite form + pending/accepted list (days-left from InvitedAt) → Task 6. ✔
- Accept = set password (redirect by role) → Task 2 (ResetPassword) + Task 2 `RoleRedirect`. ✔
- Testing with fake IEmailSender capturing links → Tasks 2, 6; existing tests migrated off Register → Task 3. ✔

**Placeholder scan:** none — every step carries real code/commands. `TestAuth` is created in Task 2 (where first needed) and extended in Task 3, noted explicitly.

**Type consistency:** `ColdstartRoles.{SuperAdmin,CompanyAdmin,User,SuperAdminEmail,All}` (Task 1) used verbatim in 2/3/4/6. `RoleRedirect.PathForAsync(UserManager<ApplicationUser>, ApplicationUser, CancellationToken)` (Task 2) used in Task 4 Login. `ApplicationUser.InvitedAt` (Task 5) used in Task 6. `TestAuth.{CreateUserAsync,SignInAsync,PostFormAsync,PostFormReadAsync,AntiforgeryAsync}` consistent across test tasks. `IProvisionCustomer.ProvisionAsync(string, CancellationToken)` matches the existing interface. Reset link param names `code` + `email` consistent between ForgotPassword/invite (producers) and ResetPassword (consumer).

**Ordering guard:** Task 3 (migrate tests off Register) precedes Task 4 (disable Register) so the suite never goes red. Task 5 (`InvitedAt`) precedes Task 6 (which reads it). Task 2 (`RoleRedirect`, reset flow) precedes Task 4 (Login uses `RoleRedirect`) and Task 6 (invite builds a ResetPassword link).

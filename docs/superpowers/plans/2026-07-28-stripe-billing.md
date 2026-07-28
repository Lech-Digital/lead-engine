# Self-Serve Stripe Billing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a customer upgrade to Full by subscribing through Stripe; flip `Customer.Plan` automatically via a signature-verified webhook; let them manage/cancel via the Stripe Customer Portal.

**Architecture:** Hexagonal. Outbound port `IBillingGateway` (Application) + `StripeBillingGateway` (Infrastructure, Stripe.net). Inbound use case `ApplySubscriptionChange` (Application) driven by a `/webhooks/stripe` endpoint (Web). Stripe-hosted Checkout + Customer Portal; no card data touches us. Plan flips happen ONLY via the verified webhook.

**Tech Stack:** .NET 10, EF Core/Npgsql, Stripe.net, ASP.NET Identity, Razor Pages + one minimal-API webhook endpoint. Tests: xUnit + FluentAssertions; `FakeBillingGateway` in `ColdstartWebFactory` (real Stripe path validated manually via test mode + `stripe listen`).

**Design source of truth:** `docs/superpowers/specs/2026-07-28-stripe-billing-design.md`

## Global Constraints

- **Money model:** €750/month recurring + €350 one-time setup fee, both on the first Checkout (subscription mode + one-time line item). **Stripe Tax enabled** (VAT-id collection + EU reverse-charge).
- **Plan flips ONLY via the verified webhook** — never from the browser `success_url` redirect. Webhook signature verification is mandatory (invalid/missing → HTTP 400, no state change).
- **Hexagonal:** the Stripe.net SDK appears **only** in `Coldstart.Infrastructure`. Domain and Application never reference Stripe types. `IBillingGateway` + DTOs live in Application.
- **DDD:** `Customer` state via behaviour (`LinkStripeCustomer`/`ActivateFull`/`DowngradeToFree`), not public setters (CS-DDD-02). `SetPlan` stays (admin comp).
- **Auth:** `/app/billing/*` handlers are `[Authorize]` and operate ONLY on the signed-in user's own customer (never a customer id from the request). The webhook is `[AllowAnonymous]`, authenticated by signature, and maps events to tenants by stored `StripeCustomerId` / `client_reference_id`.
- **Tenant reads in the webhook path bypass the fail-closed filter** (`IgnoreQueryFilters()`), like the other system-level repo reads — there is no HTTP tenant context in a webhook.
- **Standards:** ports in Application; impls `internal`/`sealed`; `CancellationToken` on IO; no `IQueryable` crossing to Application; xUnit + FluentAssertions AAA `Method_State_Expected`; only outbound ports faked in tests. Never `git push`; never add a `Co-Authored-By` trailer. Commit after each task.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `Coldstart.Domain/Customer.cs` | `StripeCustomerId`/`StripeSubscriptionId`/`PlanRenewsAt` + `LinkStripeCustomer`/`ActivateFull`/`DowngradeToFree` | 1 |
| `Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs` + new Migration | map the 3 columns | 1 |
| `tests/Coldstart.Domain.Tests/CustomerBillingTests.cs` (new) | aggregate transition tests | 1 |
| `Coldstart.Application/Billing/IBillingGateway.cs` + DTOs (new) | port + `CheckoutRequest`/`SubscriptionChange`/`SubscriptionState` | 2 |
| `Coldstart.Application/Billing/ApplySubscriptionChange.cs` (new) | map a `SubscriptionChange` → Customer plan | 2 |
| `Coldstart.Application/Ports/ICustomerRepository.cs` + impl | `GetByStripeCustomerIdAsync` | 2 |
| `tests/Coldstart.Application.Tests/ApplySubscriptionChangeTests.cs` (new) | active→Full, cancel→Free, first-activation link, idempotent | 2 |
| `Coldstart.Infrastructure/Billing/StripeOptions.cs`, `StripeBillingGateway.cs`, `StripeEventParser.cs` (new) + `.csproj` (Stripe.net) + `DependencyInjection.cs` | Stripe SDK impls + DI | 3 |
| `Coldstart.Application/Billing/IStripeEventParser.cs` (port, new) | verify+parse a webhook payload → `SubscriptionChange` (test seam) | 3 |
| `Coldstart.Web/Areas/App/Pages/Billing.cshtml(.cs)` (new) + `Index.cshtml` | checkout/portal handlers; foil CTA → checkout; Manage-billing link | 4 |
| `tests/Coldstart.Web.Tests/FakeBillingGateway.cs` (new) + `ColdstartWebFactory.cs` | fake gateway + registration | 4 |
| `tests/Coldstart.Web.Tests/BillingFlowTests.cs` (new) | checkout redirect / portal / auth / foil CTA | 4 |
| `Coldstart.Web/Program.cs` + `appsettings.json` | `MapPost("/webhooks/stripe")`, bind `StripeOptions` | 5 |
| `tests/Coldstart.Web.Tests/StripeWebhookTests.cs` (new) + `FakeStripeEventParser` | bad signature→400; valid event→plan flip | 5 |

---

## Task 1: Customer aggregate — Stripe fields + behaviour + migration

**Files:**
- Modify: `src/Coldstart.Domain/Customer.cs`
- Modify: `src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs`
- Create: an EF migration
- Create: `tests/Coldstart.Domain.Tests/CustomerBillingTests.cs`

**Interfaces — Produces:** `Customer.StripeCustomerId` (`string?`), `Customer.StripeSubscriptionId` (`string?`), `Customer.PlanRenewsAt` (`DateTimeOffset?`); `Customer.LinkStripeCustomer(string)`, `Customer.ActivateFull(string subscriptionId, DateTimeOffset? renewsAt)`, `Customer.DowngradeToFree()`.

- [ ] **Step 1: Write failing Domain tests** — `tests/Coldstart.Domain.Tests/CustomerBillingTests.cs`:
```csharp
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class CustomerBillingTests
{
    [Fact]
    public void ActivateFull_SetsPlanSubscriptionAndRenewal()
    {
        // Arrange
        var c = Customer.Create("Acme");
        var renews = DateTimeOffset.UtcNow.AddDays(30);

        // Act
        c.LinkStripeCustomer("cus_1");
        c.ActivateFull("sub_1", renews);

        // Assert
        c.Plan.Should().Be(CustomerPlan.Full);
        c.StripeCustomerId.Should().Be("cus_1");
        c.StripeSubscriptionId.Should().Be("sub_1");
        c.PlanRenewsAt.Should().Be(renews);
    }

    [Fact]
    public void DowngradeToFree_ClearsSubscription_ButKeepsStripeCustomer()
    {
        // Arrange
        var c = Customer.Create("Acme");
        c.LinkStripeCustomer("cus_1");
        c.ActivateFull("sub_1", DateTimeOffset.UtcNow.AddDays(30));

        // Act
        c.DowngradeToFree();

        // Assert
        c.Plan.Should().Be(CustomerPlan.Free);
        c.StripeSubscriptionId.Should().BeNull();
        c.PlanRenewsAt.Should().BeNull();
        c.StripeCustomerId.Should().Be("cus_1");   // kept so they can re-subscribe / reopen the Portal
    }

    [Fact]
    public void LinkStripeCustomer_IsIdempotent_AndDoesNotChangePlan()
    {
        // Arrange
        var c = Customer.Create("Acme");

        // Act
        c.LinkStripeCustomer("cus_1");
        c.LinkStripeCustomer("cus_1");

        // Assert
        c.StripeCustomerId.Should().Be("cus_1");
        c.Plan.Should().Be(CustomerPlan.Free);
    }
}
```
(If `Coldstart.Domain.Tests` doesn't exist as a project, it does — 38 tests already run there.)

- [ ] **Step 2: Run to verify fail** — `dotnet test tests/Coldstart.Domain.Tests/Coldstart.Domain.Tests.csproj --filter FullyQualifiedName~CustomerBillingTests` → FAIL to compile.

- [ ] **Step 3: Add fields + behaviour to `Customer`:**
```csharp
public string? StripeCustomerId { get; private set; }
public string? StripeSubscriptionId { get; private set; }
public DateTimeOffset? PlanRenewsAt { get; private set; }

public void LinkStripeCustomer(string stripeCustomerId) => StripeCustomerId = stripeCustomerId;

public void ActivateFull(string subscriptionId, DateTimeOffset? renewsAt)
{
    Plan = CustomerPlan.Full;
    StripeSubscriptionId = subscriptionId;
    PlanRenewsAt = renewsAt;
}

public void DowngradeToFree()
{
    Plan = CustomerPlan.Free;
    StripeSubscriptionId = null;
    PlanRenewsAt = null;
    // StripeCustomerId intentionally retained.
}
```

- [ ] **Step 4: Map the columns** — in `CustomerConfiguration.Configure`, after `LastHarvestAt`:
```csharp
builder.Property(c => c.StripeCustomerId).HasMaxLength(64);
builder.Property(c => c.StripeSubscriptionId).HasMaxLength(64);
builder.Property(c => c.PlanRenewsAt);
```

- [ ] **Step 5: Create the migration** — `dotnet ef migrations add AddStripeBillingToCustomer --project src/Coldstart.Infrastructure --startup-project src/Coldstart.Web`. Confirm the generated migration adds the 3 nullable columns to `customers` and nothing else.

- [ ] **Step 6: Run to verify pass** — the CustomerBillingTests pass; then the full `Coldstart.Domain.Tests` project is green. (Migration application is exercised by the Web/Infra test factories on their next run.)

- [ ] **Step 7: Commit**
```bash
git add src/Coldstart.Domain/Customer.cs src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs src/Coldstart.Infrastructure/Migrations tests/Coldstart.Domain.Tests/CustomerBillingTests.cs
git commit -m "feat(billing): Customer Stripe fields + activate/downgrade behaviour + migration"
```

---

## Task 2: Billing port + `ApplySubscriptionChange` use case + repo lookup

**Files:**
- Create: `src/Coldstart.Application/Billing/IBillingGateway.cs` (port + DTOs)
- Create: `src/Coldstart.Application/Billing/ApplySubscriptionChange.cs` (+ `IApplySubscriptionChange`)
- Modify: `src/Coldstart.Application/Ports/ICustomerRepository.cs` + `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs` (register the use case)
- Create: `tests/Coldstart.Application.Tests/ApplySubscriptionChangeTests.cs`

**Interfaces:**
- Produces: `IBillingGateway` (implemented in Task 3); records `CheckoutRequest(Guid CustomerId, string? StripeCustomerId, string CustomerEmail, string SuccessUrl, string CancelUrl)`, `SubscriptionChange(string StripeCustomerId, string? SubscriptionId, SubscriptionState State, DateTimeOffset? RenewsAt, Guid? CustomerRef)`, `enum SubscriptionState { Active, Canceled }`.
- Produces: `IApplySubscriptionChange.HandleAsync(SubscriptionChange change, CancellationToken ct)`.
- Produces: `ICustomerRepository.GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct)`.
- Consumes: `Customer.ActivateFull/DowngradeToFree/LinkStripeCustomer` (Task 1), `ICustomerRepository.GetAsync/UpdateAsync`.

- [ ] **Step 1: Write failing use-case tests** — `ApplySubscriptionChangeTests.cs`. Use a hand-rolled in-memory `ICustomerRepository` fake (or NSubstitute) and a real `Customer`:
```csharp
using Coldstart.Application.Billing;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class ApplySubscriptionChangeTests
{
    [Fact]
    public async Task Active_FirstActivation_LinksStripeCustomerByRef_AndGoesFull()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var repo = new FakeRepo(customer);
        var sut = new ApplySubscriptionChange(repo);
        var renews = DateTimeOffset.UtcNow.AddDays(30);

        // Act — first event has no stored StripeCustomerId link yet; mapped via CustomerRef.
        await sut.HandleAsync(new SubscriptionChange("cus_1", "sub_1", SubscriptionState.Active, renews, customer.Id.Value), default);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Full);
        customer.StripeCustomerId.Should().Be("cus_1");
        customer.StripeSubscriptionId.Should().Be("sub_1");
    }

    [Fact]
    public async Task Canceled_DowngradesToFree()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        customer.LinkStripeCustomer("cus_1");
        customer.ActivateFull("sub_1", DateTimeOffset.UtcNow.AddDays(30));
        var repo = new FakeRepo(customer);
        var sut = new ApplySubscriptionChange(repo);

        // Act — mapped by stored StripeCustomerId, no CustomerRef needed.
        await sut.HandleAsync(new SubscriptionChange("cus_1", "sub_1", SubscriptionState.Canceled, null, null), default);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Free);
    }

    [Fact]
    public async Task Active_ReplayedTwice_IsIdempotent()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var repo = new FakeRepo(customer);
        var sut = new ApplySubscriptionChange(repo);
        var change = new SubscriptionChange("cus_1", "sub_1", SubscriptionState.Active, null, customer.Id.Value);

        // Act
        await sut.HandleAsync(change, default);
        await sut.HandleAsync(change, default);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Full);
        customer.StripeCustomerId.Should().Be("cus_1");
    }

    [Fact]
    public async Task UnknownCustomer_IsIgnored_NoThrow()
    {
        // Arrange
        var repo = new FakeRepo(null);
        var sut = new ApplySubscriptionChange(repo);

        // Act
        Func<Task> act = () => sut.HandleAsync(new SubscriptionChange("cus_x", "sub_x", SubscriptionState.Active, null, Guid.NewGuid()), default);

        // Assert — a stray event for an unknown customer must not throw (Stripe would retry forever).
        await act.Should().NotThrowAsync();
    }

    // Minimal fake: resolves by StripeCustomerId first, else by id (CustomerRef).
    private sealed class FakeRepo(Customer? seed) : ICustomerRepository
    {
        private readonly Customer? _c = seed;
        public Task<Customer?> GetByStripeCustomerIdAsync(string sid, CancellationToken ct) =>
            Task.FromResult(_c?.StripeCustomerId == sid ? _c : null);
        public Task<Customer?> GetAsync(CustomerId id, CancellationToken ct) =>
            Task.FromResult(_c is not null && _c.Id == id ? _c : null);
        public Task UpdateAsync(Customer c, CancellationToken ct) => Task.CompletedTask;
        // The rest are unused here — throw NotImplementedException.
        public Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(Customer c, CancellationToken ct) => throw new NotImplementedException();
        public Task<Customer?> GetByNameAsync(string n, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteAsync(CustomerId id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> IdsDueForHarvestAsync(int t, CancellationToken ct) => throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Run to verify fail** — `--filter FullyQualifiedName~ApplySubscriptionChangeTests` → FAIL to compile.

- [ ] **Step 3: Create the port + DTOs** — `Coldstart.Application/Billing/IBillingGateway.cs`:
```csharp
namespace Coldstart.Application.Billing;

public enum SubscriptionState { Active, Canceled }

public sealed record CheckoutRequest(Guid CustomerId, string? StripeCustomerId, string CustomerEmail, string SuccessUrl, string CancelUrl);
public sealed record SubscriptionChange(string StripeCustomerId, string? SubscriptionId, SubscriptionState State, DateTimeOffset? RenewsAt, Guid? CustomerRef);

public interface IBillingGateway
{
    Task<string> CreateCheckoutSessionUrlAsync(CheckoutRequest req, CancellationToken ct);
    Task<string> CreatePortalSessionUrlAsync(string stripeCustomerId, string returnUrl, CancellationToken ct);
}
```

- [ ] **Step 4: Create the use case** — `Coldstart.Application/Billing/ApplySubscriptionChange.cs`:
```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Billing;

public interface IApplySubscriptionChange
{
    Task HandleAsync(SubscriptionChange change, CancellationToken ct);
}

internal sealed class ApplySubscriptionChange(ICustomerRepository customers) : IApplySubscriptionChange
{
    public async Task HandleAsync(SubscriptionChange change, CancellationToken ct)
    {
        // Resolve by the stored Stripe customer id; on first activation fall back to CustomerRef
        // (Stripe's client_reference_id = our CustomerId).
        var customer = await customers.GetByStripeCustomerIdAsync(change.StripeCustomerId, ct);
        if (customer is null && change.CustomerRef is { } refId)
            customer = await customers.GetAsync(new CustomerId(refId), ct);
        if (customer is null) return;   // stray/unknown → ignore (don't make Stripe retry forever)

        if (string.IsNullOrEmpty(customer.StripeCustomerId))
            customer.LinkStripeCustomer(change.StripeCustomerId);

        if (change.State == SubscriptionState.Active)
            customer.ActivateFull(change.SubscriptionId ?? customer.StripeSubscriptionId ?? "", change.RenewsAt);
        else
            customer.DowngradeToFree();

        await customers.UpdateAsync(customer, ct);
    }
}
```

- [ ] **Step 5: Repo lookup** — add to `ICustomerRepository`:
```csharp
// System-level (webhook, no tenant context): find by the stored Stripe customer id.
Task<Customer?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct);
```
Implement in `CustomerRepository` with `IgnoreQueryFilters()`:
```csharp
public async Task<Customer?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct) =>
    await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.StripeCustomerId == stripeCustomerId, ct);
```

- [ ] **Step 6: Register the use case** — in `Coldstart.Application/DependencyInjection.cs`: `services.AddScoped<Billing.IApplySubscriptionChange, Billing.ApplySubscriptionChange>();`

- [ ] **Step 7: Run to verify pass** — ApplySubscriptionChangeTests green; full `Coldstart.Application.Tests` green.

- [ ] **Step 8: Commit**
```bash
git add src/Coldstart.Application/Billing src/Coldstart.Application/Ports/ICustomerRepository.cs src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs src/Coldstart.Application/DependencyInjection.cs tests/Coldstart.Application.Tests/ApplySubscriptionChangeTests.cs
git commit -m "feat(billing): IBillingGateway port + ApplySubscriptionChange use case + repo lookup"
```

---

## Task 3: Stripe implementation + options + event parser + DI

**Files:**
- Modify: `src/Coldstart.Infrastructure/Coldstart.Infrastructure.csproj` (add `Stripe.net`)
- Create: `src/Coldstart.Infrastructure/Billing/StripeOptions.cs`
- Create: `src/Coldstart.Infrastructure/Billing/StripeBillingGateway.cs`
- Create: `src/Coldstart.Application/Billing/IStripeEventParser.cs` (port) + `src/Coldstart.Infrastructure/Billing/StripeEventParser.cs` (impl)
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Produces: `StripeOptions { SecretKey, WebhookSecret, PriceMonthlyId, PriceSetupId }`.
- Produces: `IStripeEventParser.Parse(string json, string signatureHeader, string webhookSecret) → SubscriptionChange?` (returns null for events we don't act on; **throws** on invalid signature).
- Produces: `StripeBillingGateway : IBillingGateway`.

> This task is thin SDK glue and is **not unit-tested** (verified manually via Stripe test mode + `stripe listen`). Keep the testable seam (`IStripeEventParser`) so Task 5's webhook is testable with a fake.

- [ ] **Step 1: Add the package** — `dotnet add src/Coldstart.Infrastructure package Stripe.net`. Build to confirm it restores.

- [ ] **Step 2: `StripeOptions`:**
```csharp
namespace Coldstart.Infrastructure.Billing;
public sealed class StripeOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string PriceMonthlyId { get; set; } = "";
    public string PriceSetupId { get; set; } = "";
    public bool IsConfigured => SecretKey.Length > 0 && PriceMonthlyId.Length > 0;
}
```

- [ ] **Step 3: `StripeBillingGateway`** — `internal sealed class StripeBillingGateway(IOptions<StripeOptions> options) : IBillingGateway`. Use Stripe.net's `SessionService`/`Stripe.BillingPortal.SessionService` with `StripeConfiguration.ApiKey = opts.SecretKey` (or pass `RequestOptions`). Checkout session: `Mode = "subscription"`, `LineItems` = [ `{ Price = PriceMonthlyId, Quantity = 1 }`, `{ Price = PriceSetupId, Quantity = 1 }` ], `AutomaticTax = new() { Enabled = true }`, `TaxIdCollection = new() { Enabled = true }`, `ClientReferenceId = req.CustomerId.ToString()`, `Customer = req.StripeCustomerId` (when present) else `CustomerEmail = req.CustomerEmail`, `CustomerUpdate` as needed for tax, `SuccessUrl`/`CancelUrl` from the request; return `session.Url`. Portal: `Stripe.BillingPortal.SessionCreateOptions { Customer = stripeCustomerId, ReturnUrl = returnUrl }` → return `session.Url`. Confirm exact Stripe.net type/property names against the installed version.

- [ ] **Step 4: Event parser** — port in Application:
```csharp
namespace Coldstart.Application.Billing;
public interface IStripeEventParser
{
    // Verifies the signature (throws on failure) and maps a handled event to a SubscriptionChange (null if not handled).
    SubscriptionChange? Parse(string json, string signatureHeader, string webhookSecret);
}
```
Impl in Infrastructure uses `EventUtility.ConstructEvent(json, signatureHeader, webhookSecret)` (throws `StripeException` on a bad signature — let it propagate), then switches on `event.Type`: `checkout.session.completed` → read the `Session` (`ClientReferenceId` → `CustomerRef`, `CustomerId` → `StripeCustomerId`, `SubscriptionId`) → `Active`; `customer.subscription.updated` → map status (`active`/`trialing`/`past_due` → `Active`, else `Canceled`), read `current_period_end` → `RenewsAt`; `customer.subscription.deleted` → `Canceled`; anything else → `null`. Confirm the Stripe.net object shapes.

- [ ] **Step 5: DI + options** — in `Infrastructure/DependencyInjection.AddInfrastructure`: `services.AddScoped<Application.Billing.IBillingGateway, Billing.StripeBillingGateway>();` and `services.AddSingleton<Application.Billing.IStripeEventParser, Billing.StripeEventParser>();`. (Options are bound in the Web composition root in Task 5.)

- [ ] **Step 6: Build** — `dotnet build` succeeds; full solution test suite still green (no behavior change yet, just new unreferenced services).

- [ ] **Step 7: Commit**
```bash
git add src/Coldstart.Infrastructure/Billing src/Coldstart.Infrastructure/Coldstart.Infrastructure.csproj src/Coldstart.Infrastructure/DependencyInjection.cs src/Coldstart.Application/Billing/IStripeEventParser.cs
git commit -m "feat(billing): Stripe.net gateway + event parser + options (SDK glue)"
```

---

## Task 4: Web billing endpoints + fake + foil→checkout + Manage-billing

**Files:**
- Create: `src/Coldstart.Web/Areas/App/Pages/Billing.cshtml(.cs)`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml` (foil CTA → checkout; Manage-billing link for Full)
- Create: `tests/Coldstart.Web.Tests/FakeBillingGateway.cs`
- Modify: `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs`
- Create: `tests/Coldstart.Web.Tests/BillingFlowTests.cs`

**Interfaces:** Consumes `IBillingGateway`, `UserManager`→`CustomerId`, `IGetCompanyProfile`/`IGetLeadInbox` for the email + plan.

- [ ] **Step 1: `FakeBillingGateway` + register it** — mirror `FakeOutreach`/`FakeHarvestQueue`:
```csharp
using Coldstart.Application.Billing;
namespace Coldstart.Web.Tests;
public sealed class FakeBillingGateway : IBillingGateway
{
    public CheckoutRequest? LastCheckout { get; private set; }
    public string CheckoutUrl { get; set; } = "https://stripe.test/checkout";
    public string PortalUrl { get; set; } = "https://stripe.test/portal";
    public Task<string> CreateCheckoutSessionUrlAsync(CheckoutRequest req, CancellationToken ct) { LastCheckout = req; return Task.FromResult(CheckoutUrl); }
    public Task<string> CreatePortalSessionUrlAsync(string sid, string returnUrl, CancellationToken ct) => Task.FromResult(PortalUrl);
}
```
In `ColdstartWebFactory.ConfigureWebHost`: `services.RemoveAll<IBillingGateway>(); services.AddSingleton<IBillingGateway>(Billing);` with a `public FakeBillingGateway Billing { get; } = new();` property (mirrors `HarvestQueue`).

- [ ] **Step 2: Write failing web tests** — `BillingFlowTests.cs` (use `AllowAutoRedirect=false`, `TestAuth.SignInAsync`, `AntiforgeryForm` pattern from other tests):
```csharp
// Checkout_ForSignedInFreeCustomer_RedirectsToStripe
//   POST /app/billing?handler=Checkout (with token) → 3xx, Location == factory.Billing.CheckoutUrl,
//   and factory.Billing.LastCheckout.CustomerId == the signed-in customer.
// Portal_WithoutStripeCustomer_DoesNotRedirectToStripe
//   a fresh Free customer (no StripeCustomerId) POST Portal → redirected back to /app (or shown a notice),
//   NOT to PortalUrl.
// Billing_Endpoints_RequireAuth
//   anonymous POST /app/billing?handler=Checkout → redirect to /Identity/Account/Login.
// Foil_UpgradeCta_PostsToCheckout
//   a Free customer with locked leads: GET /app → the unlock CTA is a form posting to the billing checkout
//   handler (assert the form action / handler is present), not a mailto.
```
Write these as full `[Fact]`s following the existing test conventions (see `CustomerZoneTests`/`LeadFoilTests` for auth + antiforgery + `factory.Services.CreateScope()` seeding; use `LeadFoilTests`'/`FoilWebFactory`'s approach if a locked lead is needed — or set the customer to Free and seed >free-quota leads).

- [ ] **Step 2b: Run → fail.**

- [ ] **Step 3: `Billing.cshtml.cs`** — `@page "/app/billing"`, `[Authorize]`, Layout the app shell. Handlers:
  - `OnPostCheckoutAsync` — resolve the signed-in user's customer; build `CheckoutRequest(customerId, customer.StripeCustomerId, userEmail, SuccessUrl=$"{origin}/app?upgraded=1", CancelUrl=$"{origin}/app")`; `var url = await gateway.CreateCheckoutSessionUrlAsync(req, ct); return Redirect(url);`. If billing isn't configured / gateway throws → redirect back to `/app` with a TempData notice.
  - `OnPostPortalAsync` — if the customer has a `StripeCustomerId`, `Redirect(await gateway.CreatePortalSessionUrlAsync(id, $"{origin}/app", ct))`; else redirect to `/app` with a "no active subscription" notice.
  - Build absolute URLs from `Request.Scheme`/`Request.Host`.
  - The page's GET can render a tiny billing status (plan + `PlanRenewsAt`) — optional; keep minimal.

- [ ] **Step 4: Wire the UI in `Index.cshtml`** — replace the `#upgrade` modal's mailto with a form posting to the checkout handler:
  - The "Unlock all N leads" button and the modal's primary action become `<form method="post" asp-page="/Billing" asp-area="App" asp-page-handler="Checkout"><button class="btn btn-primary" type="submit">Subscribe — unlock every lead</button></form>` (antiforgery auto-injected). Keep the explanatory copy (now: "€750/mo + €350 one-time setup, cancel anytime"). Remove the "email andrzej / coming soon" text.
  - Add a **Manage billing** link in the app-bar shown only when `Model.Plan == Full`: `<a class="app-navlink" ...>` that's a form posting to `?handler=Portal` (a link that POSTs, or a small form). (Requires `IndexModel` to expose `Plan` — it already loads the inbox which has `Plan`; surface it.)

- [ ] **Step 5: Run → pass** (BillingFlowTests) then full `Coldstart.Web.Tests` green.

- [ ] **Step 6: Commit**
```bash
git add -A && git commit -m "feat(billing): checkout/portal endpoints + foil upgrade CTA + manage-billing link"
```

---

## Task 5: Stripe webhook endpoint + wiring

**Files:**
- Modify: `src/Coldstart.Web/Program.cs` (`MapPost("/webhooks/stripe")`, bind `StripeOptions`)
- Modify: `src/Coldstart.Web/appsettings.json` (`Stripe` section)
- Create: `tests/Coldstart.Web.Tests/StripeWebhookTests.cs` + a `FakeStripeEventParser`
- Modify: `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs` (register the fake parser)

**Interfaces:** Consumes `IStripeEventParser`, `IApplySubscriptionChange`, `StripeOptions`.

- [ ] **Step 1: Fake parser + register** — a `FakeStripeEventParser : IStripeEventParser` whose `Parse` returns a preset `SubscriptionChange?` or throws a preset exception (to simulate a bad signature). Register it in `ColdstartWebFactory` via `RemoveAll<IStripeEventParser>()` + a settable instance the test controls.

- [ ] **Step 2: Write failing webhook tests** — `StripeWebhookTests.cs`:
```csharp
// Webhook_WithBadSignature_Returns400_AndNoPlanChange
//   parser set to throw → POST /webhooks/stripe → 400; a seeded customer stays Free.
// Webhook_WithActiveSubscription_FlipsCustomerToFull
//   seed a Free customer; parser returns SubscriptionChange(Active, CustomerRef=that id);
//   POST /webhooks/stripe → 200; re-load the customer → Plan == Full.
// Webhook_WithCanceled_FlipsToFree
//   seed a Full customer (LinkStripeCustomer+ActivateFull via a scope); parser returns Canceled by StripeCustomerId;
//   POST → 200; customer → Free.
```
POST with any body + a `Stripe-Signature` header (the fake parser ignores the real value). Seed/read customers via `factory.Services.CreateScope()`.

- [ ] **Step 2b: Run → fail** (404 — endpoint doesn't exist).

- [ ] **Step 3: Map the endpoint** — in `Program.cs` after `app.MapRazorPages()`:
```csharp
app.MapPost("/webhooks/stripe", async (HttpRequest req, IStripeEventParser parser, IApplySubscriptionChange apply,
    IOptions<Coldstart.Infrastructure.Billing.StripeOptions> opts, CancellationToken ct) =>
{
    using var reader = new StreamReader(req.Body);
    var json = await reader.ReadToEndAsync(ct);
    var sig = req.Headers["Stripe-Signature"].ToString();
    SubscriptionChange? change;
    try { change = parser.Parse(json, sig, opts.Value.WebhookSecret); }
    catch { return Results.BadRequest(); }          // bad signature / unparseable
    if (change is not null) await apply.HandleAsync(change, ct);
    return Results.Ok();
});
```
(Uses `Coldstart.Application.Billing` + `Microsoft.Extensions.Options`. The endpoint is anonymous — authenticated by the signature inside `Parse`.)

- [ ] **Step 4: Bind options + appsettings** — in `Program.cs`: `builder.Services.Configure<Coldstart.Infrastructure.Billing.StripeOptions>(builder.Configuration.GetSection("Stripe"));`. In `appsettings.json` add `"Stripe": { "SecretKey": "", "WebhookSecret": "", "PriceMonthlyId": "", "PriceSetupId": "" }` (real values via env/user-secrets).

- [ ] **Step 5: Run → pass** (StripeWebhookTests) then full solution green.

- [ ] **Step 6: Commit**
```bash
git add -A && git commit -m "feat(billing): signature-verified /webhooks/stripe → ApplySubscriptionChange"
```

---

## Operator prerequisites (Stripe dashboard — NOT code; document in the PR/handoff)

Before this works in test or prod: create the Product + two Prices (€750/mo recurring, €350 one-time) and put their ids in `Stripe:PriceMonthlyId`/`PriceSetupId`; enable **Stripe Tax** (register NL + origin address); configure the **Customer Portal** (allow cancel-at-period-end, show invoices); register the **webhook endpoint** (`{host}/webhooks/stripe`, events: `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`) and copy its signing secret to `Stripe:WebhookSecret`; set `Stripe:SecretKey`. Validate end-to-end with **test-mode keys + `stripe listen --forward-to localhost:5178/webhooks/stripe`**.

## Self-review notes

- **Spec coverage:** Customer fields+behaviour+migration (T1); port + plan-mapping use case + idempotency + unknown-customer safety (T2); Stripe.net gateway + Stripe Tax config + event parser seam (T3); checkout/portal endpoints + foil→checkout + manage link + fake (T4); signature-verified webhook + plan flip + config (T5). All mapped.
- **Security:** plan flips only via the webhook (T5) → `ApplySubscriptionChange` (T2); checkout/portal act on the signed-in customer only (T4); webhook anonymous but signature-gated (T3 parser throws → T5 returns 400); webhook repo read uses `IgnoreQueryFilters` (T2).
- **Type consistency:** `SubscriptionChange`/`CheckoutRequest`/`SubscriptionState` defined in T2, produced by the parser in T3, consumed by the endpoint in T5 and the gateway in T4. `IStripeEventParser` port in T3 (Application) with the Infra impl.
- **Known risks flagged:** exact Stripe.net type/property names must be confirmed against the installed package version (T3); the real Stripe path is manual-tested only (documented); operator dashboard setup is a prerequisite.

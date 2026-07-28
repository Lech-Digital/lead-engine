# Self-Serve Stripe Billing — Design

**Date:** 2026-07-28
**Status:** Accepted
**Area:** `Coldstart.Application` (port + use case), `Coldstart.Infrastructure` (Stripe impl), `Coldstart.Web` (webhook + billing endpoints + UI), `Coldstart.Domain` (Customer aggregate)

## Problem

The free/foil tier and `Customer.Plan` (Free/Full) exist, and the nightly harvest already keys off `Full` — but there is **no self-serve path to become Full**. Today `Full` is set only in code (`IdentitySeeder`) or by a developer calling `SetPlan`; the customer-facing "Unlock all leads" foil just says *"email andrzej… self-serve subscriptions coming soon."* This blocks revenue and forces manual work per customer.

## Goal

Let a customer upgrade to Full themselves by subscribing through Stripe, have their plan flip automatically on payment (and flip back when they cancel or stop paying), and let them manage/cancel their subscription — all without card data touching our servers.

## Non-goals

- No embedded card UI (Stripe-hosted Checkout + Portal only; zero PCI scope).
- No usage-based/metered billing, no coupons/trials in v1 (Stripe Portal can add coupons later).
- No in-app invoice rendering (Stripe Portal shows invoices).
- No removal of the admin/dev comp path (`SetPlan` stays).
- No dunning UI — Stripe handles retries; we only react to the terminal outcome.

## Locked decisions (from brainstorming)

1. **Money model:** a €750/month subscription **plus** a one-time €350 setup fee, both on the first Stripe Checkout (subscription mode + a one-time line item). Plan flips to Full on payment.
2. **VAT:** **Stripe Tax** enabled — automatic EU VAT, VAT-number collection at Checkout, B2B reverse-charge for VAT-registered EU businesses.
3. **Integration:** Stripe-hosted **Checkout** (subscribe) + **Customer Portal** (manage/cancel) + **webhooks** (plan flips).
4. **Cancel = at period end:** the customer keeps Full until the paid period ends; the `customer.subscription.deleted` webhook (fired at period end) downgrades to Free.
5. **Admin comp kept:** `Customer.SetPlan(Full)` remains for comps/dogfood/testing; such customers have no Stripe subscription and the billing UI degrades gracefully.

## Architecture (hexagonal)

- **Outbound port — `Coldstart.Application/Billing/IBillingGateway`:**
  - `Task<string> CreateCheckoutSessionUrlAsync(CheckoutRequest req, CancellationToken ct)` — returns the Stripe-hosted Checkout URL to redirect to.
  - `Task<string> CreatePortalSessionUrlAsync(string stripeCustomerId, string returnUrl, CancellationToken ct)` — returns the Customer Portal URL.
  - DTOs (records): `CheckoutRequest(Guid CustomerId, string? StripeCustomerId, string CustomerEmail, string SuccessUrl, string CancelUrl)`.
- **Impl — `Coldstart.Infrastructure/Billing/StripeBillingGateway`** using the **Stripe.net** SDK. Reads `StripeOptions`. Creates the Checkout session (subscription mode, the €750 recurring price + €350 one-time price, `AutomaticTax = enabled`, `TaxIdCollection = enabled`, `customer`/`customer_email` set, `client_reference_id = CustomerId` so the webhook can map back), and Portal sessions.
- **Inbound use case — `Coldstart.Application/Billing/ApplySubscriptionChange`:** given a normalized `SubscriptionChange(stripeCustomerId, stripeSubscriptionId, status, renewsAt, customerRef?)`, load the `Customer` (by stored `StripeCustomerId`, or by `customerRef` on first activation), and drive the aggregate: **active/trialing/past_due-but-current → `Customer.ActivateFull(...)`**; **canceled/unpaid → `Customer.DowngradeToFree()`**. Idempotent (re-applying the same state is a no-op).
- **Web:**
  - `POST /app/billing/checkout` (`[Authorize]`) — resolves the signed-in customer, calls the gateway, redirects to the Checkout URL.
  - `POST /app/billing/portal` (`[Authorize]`) — for a customer with a `StripeCustomerId`, redirects to the Portal URL.
  - `POST /webhooks/stripe` (`[AllowAnonymous]`) — **verifies the Stripe signature** against `WebhookSecret`, parses the event, normalizes it to a `SubscriptionChange`, calls `ApplySubscriptionChange`. Returns 200 quickly; returns 400 on a bad signature.

## Data model (Customer aggregate + migration)

Add to `Customer` (all `private set`, mutated via behaviour — CS-DDD-02):
- `string? StripeCustomerId`
- `string? StripeSubscriptionId`
- `DateTimeOffset? PlanRenewsAt` (period end, for display: "renews / active until …")

Behaviour:
- `LinkStripeCustomer(string stripeCustomerId)` — set once when Checkout creates/returns a Stripe customer.
- `ActivateFull(string subscriptionId, DateTimeOffset? renewsAt)` — sets `Plan = Full`, stores the subscription id + renewal.
- `DowngradeToFree()` — sets `Plan = Free`, clears the subscription id + `PlanRenewsAt` (keeps `StripeCustomerId` so they can re-subscribe / reopen the Portal).

New EF migration adds the three nullable columns. `Free`/`Full` and `SetPlan` are unchanged.

## Flows

**Upgrade.** Foil "Unlock all leads" CTA (and the locked-row 🔒) → `POST /app/billing/checkout` → gateway builds the session (subscription €750/mo + one-time €350, Stripe Tax on, VAT-id collection, `client_reference_id = CustomerId`, `success_url = /app?upgraded=1`, `cancel_url = /app`) → 303 redirect to Stripe. On success Stripe redirects back to `/app`; the **webhook** (not the redirect) is the source of truth that flips the plan, so the foil disappears once the event is processed (a brief "activating…" note on `?upgraded=1` covers the gap).

**Webhook.** `/webhooks/stripe` handles: `checkout.session.completed` (first activation — link the Stripe customer + activate), `customer.subscription.updated` (renewals, status changes → Full/Free per status), `customer.subscription.deleted` (period-end cancel → Free). Each is verified, idempotent, and mapped to our customer via `StripeCustomerId` (or `client_reference_id` on the first event).

**Manage / cancel.** A "Manage billing" affordance shown to Full customers (app-bar link) → `POST /app/billing/portal` → Stripe Customer Portal (update card, invoices, cancel-at-period-end). No cancel logic in our code — we only react to the resulting `subscription.deleted` webhook.

## Config / secrets

`StripeOptions` (bound from the `Stripe` config section, like `OutreachMailbox`): `SecretKey`, `WebhookSecret`, `PriceMonthlyId`, `PriceSetupId`. Test-mode keys via user-secrets in dev; live keys via env in prod. The gateway is only meaningfully exercised when configured; with blank keys the endpoints surface a friendly "billing isn't configured yet" rather than throwing.

## Operational prerequisites (Stripe dashboard, done by the operator — not code)

Listed so the plan can note them: create the Product + two Prices (€750/mo recurring, €350 one-time), enable **Stripe Tax** (register NL + origin), configure the **Customer Portal** (allow cancel), and register the **webhook endpoint** (`/webhooks/stripe`) to obtain its signing secret. These populate `StripeOptions`.

## Security

- Webhook **signature verification** is mandatory (reject unsigned/invalid with 400); never trust the payload otherwise.
- `checkout`/`portal` endpoints are `[Authorize]` and operate **only** on the signed-in user's own customer (never a customer id from the request body).
- The webhook is `[AllowAnonymous]` (Stripe calls it) but authenticated by the signature; it maps events to tenants by the stored `StripeCustomerId` / `client_reference_id`, never by trusting arbitrary input.
- Plan changes happen **only** via the verified webhook, never from the browser redirect (a user can't self-activate by hitting `success_url`).

## Testing

- **`ApplySubscriptionChange` (Application, unit):** active → Full; canceled/unpaid → Free; first-activation links the Stripe customer by `client_reference_id`; idempotent replay is a no-op. Against a real `Customer` aggregate + a fake repo.
- **Web flows:** a `FakeBillingGateway` (mirrors `FakeOutreach`/`FakeMailbox`, registered in `ColdstartWebFactory`) returns a canned URL; assert `/app/billing/checkout` redirects a signed-in Free customer to it, `/app/billing/portal` requires a `StripeCustomerId`, both require auth, and the foil CTA points at the checkout endpoint.
- **Webhook endpoint:** a bad/missing signature → 400 and no plan change; a valid event (constructed via the test seam) → the right plan flip. Signature verification of the real Stripe payload format is validated manually against **Stripe test mode + `stripe listen`** (documented; not unit-tested).
- **Customer aggregate:** `ActivateFull`/`DowngradeToFree`/`LinkStripeCustomer` transitions (Domain unit tests).
- Standards: xUnit + FluentAssertions, AAA, `Method_State_Expected`; only outbound ports are faked (never the Domain).

## Consequences

**Positive:** revenue becomes self-serve; the foil converts directly; plan state is driven by Stripe truth (no drift); zero PCI scope; VAT handled correctly for EU B2B; the nightly-harvest `Full` gate now has a real acquisition path.

**Negative:** a new external dependency (Stripe.net + Stripe availability) and a public webhook endpoint to secure; the real Stripe path can't be fully unit-tested (needs test-mode + CLI for end-to-end confidence); operator must complete the dashboard setup before it works in prod; a plan flip lags Checkout by the webhook round-trip (seconds) — handled with an "activating…" state on return.

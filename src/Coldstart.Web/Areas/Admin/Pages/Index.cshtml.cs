using System.ComponentModel.DataAnnotations;
using Coldstart.Application.Contact;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Web.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
internal sealed class IndexModel(
    AdminUserService admin,
    ICustomerRepository customers,
    IInquiryRepository inquiries,
    InquiryConversionLookup conversionLookup,
    IDismissInquiry dismissInquiry,
    IRestoreInquiry restoreInquiry) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Which slice of the inquiry lifecycle to show. "open" (neither converted nor dismissed) is
    // the operator's actual work queue and stays the default so nothing already resolved crowds it.
    // Nullable on purpose: a non-nullable string [BindProperty] is implicitly "required" under
    // nullable-reference-types, which would fail model validation on any POST (Invite, Dismiss,
    // Restore) that doesn't happen to submit a Filter field. FilterOrDefault below is what callers
    // and the view should read.
    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public string FilterOrDefault => Filter ?? "open";

    public string? StatusMessage { get; private set; }
    public IReadOnlyList<CustomerRow> Rows { get; private set; } = [];
    public IReadOnlyList<InquiryRow> Inquiries { get; private set; } = [];

    public int OpenCount { get; private set; }
    public int ConvertedCount { get; private set; }
    public int DismissedCount { get; private set; }
    public int AllCount { get; private set; }

    // Set by the customer detail page when a Revoke/Remove there deleted the customer outright,
    // leaving no detail page to return to.
    [TempData] public string? Flash { get; set; }

    // Converted/ConvertedCustomerId are derived at read time (see InquiryConversionLookup), never
    // stored — an inquiry is "converted" purely because an account already exists for its email.
    // DismissedAt is real, stored state (an operator judgement nothing else can reconstruct).
    public sealed record InquiryRow(
        Guid Id, string Name, string Email, string Niche, string WhatYouSell, string? Message,
        DateTimeOffset CreatedAt, string? Company, DateTimeOffset? DismissedAt,
        bool Converted, Guid? ConvertedCustomerId, string? ConvertedCustomerName)
    {
        public bool IsDismissed => !Converted && DismissedAt is not null;
        public bool IsOpen => !Converted && DismissedAt is null;
    }

    // One row per customer (not per user) — the detail page is where per-user and per-plan
    // controls live now, so this list only needs to say enough for the operator to pick a company.
    public sealed record CustomerRow(
        Guid CustomerId, string Company, int UserCount,
        CustomerPlan Plan, DateTimeOffset? ComplimentaryUntil,
        HarvestStatus HarvestStatus, DateTimeOffset? LastHarvestAt,
        int LeadsTotal, int LeadsPendingReview);

    public async Task OnGetAsync(Guid? useInquiry, CancellationToken ct)
    {
        StatusMessage = Flash;
        if (useInquiry is { } inqId)
        {
            var inq = await inquiries.GetAsync(new InquiryId(inqId), ct);
            if (inq is not null)
                Input = new InputModel
                {
                    Email = inq.Email.Value,
                    Company = inq.Company ?? "",
                    Niche = inq.Niche,
                    Info = string.IsNullOrWhiteSpace(inq.Message) ? inq.WhatYouSell : $"{inq.WhatYouSell}\n\n{inq.Message}",
                };
        }
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        var result = await admin.InviteAsync(new InviteRequest(Input.Email, Input.Company, Input.Niche, Input.Info), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("Input.Email", result.Message);
            await LoadAsync(ct);
            return Page();
        }
        StatusMessage = result.Message;
        Input = new InputModel();
        await LoadAsync(ct);
        return Page();
    }

    // Dismiss/Restore never delete the inquiry — see Inquiry.Dismiss/Restore — they only move it
    // between the Open and Dismissed slices of this same page. Dismiss is reversible (Restore is
    // right there), so it deliberately carries no confirmation dialog, unlike genuinely
    // destructive admin actions elsewhere on this page.
    public async Task<IActionResult> OnPostDismissAsync(Guid id, CancellationToken ct)
    {
        StatusMessage = (await dismissInquiry.ExecuteAsync(id, DateTimeOffset.UtcNow, ct)).Message;
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync(Guid id, CancellationToken ct)
    {
        StatusMessage = (await restoreInquiry.ExecuteAsync(id, ct)).Message;
        await LoadAsync(ct);
        return Page();
    }

    // Groups the same per-user rows AdminUserService already computes down to one row per
    // customer, then folds in the lead counts the admin API already exposes — no new queries.
    private async Task LoadAsync(CancellationToken ct)
    {
        var userRows = await admin.ListAsync(User.Identity?.Name, ct);
        var leadCounts = await customers.LeadCountsAsync(ct);

        Rows = userRows
            .Where(r => r.CustomerId is not null)
            .GroupBy(r => r.CustomerId!.Value)
            .Select(g =>
            {
                var first = g.First();
                leadCounts.TryGetValue(g.Key, out var lc);
                return new CustomerRow(
                    g.Key, first.Company, g.Count(),
                    first.Plan ?? CustomerPlan.Free, first.ComplimentaryUntil,
                    first.HarvestStatus ?? HarvestStatus.None, first.LastHarvestAt,
                    lc?.Total ?? 0, lc?.PendingReview ?? 0);
            })
            .OrderBy(r => r.Company)
            .ToList();

        var companyNames = Rows.ToDictionary(r => r.CustomerId, r => r.Company);

        // Single query for every account's normalized email (InquiryConversionLookup), matched in
        // memory against every inquiry — not one query per inquiry.
        var converted = await conversionLookup.BuildAsync(ct);
        var all = await inquiries.AllAsync(ct);

        var allRows = all.Select(i =>
        {
            var isConverted = converted.TryGetValue(conversionLookup.Normalize(i.Email.Value), out var customerId);
            var customerName = isConverted && customerId is { } cid && companyNames.TryGetValue(cid, out var name)
                ? name
                : null;
            return new InquiryRow(
                i.Id.Value, i.Name, i.Email.Value, i.Niche, i.WhatYouSell, i.Message, i.CreatedAt, i.Company,
                i.DismissedAt, isConverted, isConverted ? customerId : null, customerName);
        }).ToList();

        OpenCount = allRows.Count(r => r.IsOpen);
        ConvertedCount = allRows.Count(r => r.Converted);
        DismissedCount = allRows.Count(r => r.IsDismissed);
        AllCount = allRows.Count;

        Inquiries = FilterOrDefault switch
        {
            "converted" => allRows.Where(r => r.Converted).ToList(),
            "dismissed" => allRows.Where(r => r.IsDismissed).ToList(),
            "all" => allRows,
            _ => allRows.Where(r => r.IsOpen).ToList(),
        };
    }

    public sealed class InputModel
    {
        [Required] public string Company { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Niche { get; set; } = "";
        public string? Info { get; set; }
    }
}

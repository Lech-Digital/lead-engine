using System.ComponentModel.DataAnnotations;
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
    IInquiryRepository inquiries) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }
    public IReadOnlyList<CustomerRow> Rows { get; private set; } = [];
    public IReadOnlyList<InquiryRow> Inquiries { get; private set; } = [];

    // Set by the customer detail page when a Revoke/Remove there deleted the customer outright,
    // leaving no detail page to return to.
    [TempData] public string? Flash { get; set; }

    public sealed record InquiryRow(Guid Id, string Name, string Email, string Niche, string WhatYouSell, string? Message, DateTimeOffset CreatedAt, string? Company);

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

        Inquiries = (await inquiries.RecentAsync(20, ct))
            .Select(i => new InquiryRow(i.Id.Value, i.Name, i.Email.Value, i.Niche, i.WhatYouSell, i.Message, i.CreatedAt, i.Company))
            .ToList();
    }

    public sealed class InputModel
    {
        [Required] public string Company { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Niche { get; set; } = "";
        public string? Info { get; set; }
    }
}

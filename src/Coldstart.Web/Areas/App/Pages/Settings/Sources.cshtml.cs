using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Settings;

[Authorize]
public sealed class SourcesSettingsModel(
    UserManager<ApplicationUser> users,
    ICustomerRepository customers,
    IUpdateSourceSelection updateSources,
    IEmailSender email,
    Microsoft.Extensions.Options.IOptions<Coldstart.Infrastructure.OpsOptions> ops) : PageModel
{
    // The customer-facing source catalogue. Names must match ILeadSource.Name values.
    internal static readonly (string Name, string Title, string Description)[] Catalogue =
    [
        ("hackernews", "Hacker News", "Global tech community — founders posting about hiring, tools and growing pains."),
        ("funding", "Global funding news", "TechCrunch, tech.eu, EU-Startups — companies that just raised."),
        ("funding-nl", "Dutch business news", "Silicon Canals, Emerce, MT/Sprout — Dutch raises and launches."),
        ("adzuna", "Dutch job postings", "Companies in the Netherlands hiring right now for roles that match your niche."),
    ];

    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public List<SourceRow> Sources { get; set; } = [];

    public sealed class SourceRow
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var customer = await customers.GetAsync(new CustomerId(user.CustomerId.Value), ct);
        Sources = Catalogue.Select(c => new SourceRow
        {
            Name = c.Name, Title = c.Title, Description = c.Description,
            Enabled = customer?.AllowsSource(c.Name) ?? true,
        }).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var known = Catalogue.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = Sources.Where(s => s.Enabled && known.Contains(s.Name)).Select(s => s.Name).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Keep at least one source on — the engine needs somewhere to hunt.";
            return RedirectToPage();
        }
        // All sources on = no restriction stored (future sources join automatically).
        await updateSources.UpdateAsync(user.CustomerId.Value,
            selected.Count == Catalogue.Length ? null : selected, ct);
        StatusMessage = "Saved.";
        return RedirectToPage();
    }

    // The wish box: free text straight to the founder's inbox — customer wishes ARE the roadmap.
    public async Task<IActionResult> OnPostWishAsync(string? wish, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });

        var text = (wish ?? "").Trim();
        if (text.Length is < 3 or > 2000)
        {
            StatusMessage = "Tell us a bit more — a few words is all it takes.";
            return RedirectToPage();
        }

        var customer = await customers.GetAsync(new CustomerId(user.CustomerId.Value), ct);
        await email.SendAsync(ops.Value.AlertEmail,
            $"💡 Wish from {customer?.Name ?? "a customer"}",
            $"From: {customer?.Name ?? "?"} ({user.Email})\nPage: /app/settings/sources\n\n{text}",
            null, ct);
        StatusMessage = "Sent — it lands directly with the founder. Thank you!";
        return RedirectToPage();
    }
}

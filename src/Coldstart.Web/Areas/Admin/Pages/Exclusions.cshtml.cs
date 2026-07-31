using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages;

// The GDPR objection list (privacy policy §5): companies excluded from all future harvesting.
[Authorize(Roles = ColdstartRoles.SuperAdmin)]
internal sealed class ExclusionsModel(ISuppressionRepository suppressions) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    public IReadOnlyList<Suppression> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Rows = await suppressions.AllAsync(ct);

    public async Task<IActionResult> OnPostAddAsync(string? value, string? note, CancellationToken ct)
    {
        try
        {
            await suppressions.AddAsync(Suppression.Create(value ?? "", note, DateTimeOffset.UtcNow), ct);
            StatusMessage = $"\"{value?.Trim()}\" is excluded from all future harvesting.";
        }
        catch (ArgumentException ex)
        {
            StatusMessage = ex.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id, CancellationToken ct)
    {
        await suppressions.RemoveAsync(id, ct);
        StatusMessage = "Exclusion removed.";
        return RedirectToPage();
    }
}

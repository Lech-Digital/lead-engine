using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

public class IndexModel : PageModel
{
    // Outreach emails link here with ?company=<their name>#get-leads so the
    // sample-leads form arrives pre-filled — the prospect only fills the gaps.
    [BindProperty(SupportsGet = true, Name = "company")]
    public string? PrefillCompany { get; set; }
}

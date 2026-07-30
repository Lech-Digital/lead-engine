using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

public class IndexModel : PageModel
{
    // Outreach emails link here with ?company=<their name>#get-leads so the
    // sample-leads form arrives pre-filled — the prospect only fills the gaps.
    [BindProperty(SupportsGet = true, Name = "company")]
    public string? PrefillCompany { get; set; }
    [BindProperty(SupportsGet = true, Name = "name")]
    public string? PrefillName { get; set; }
    [BindProperty(SupportsGet = true, Name = "email")]
    public string? PrefillEmail { get; set; }
    [BindProperty(SupportsGet = true, Name = "niche")]
    public string? PrefillNiche { get; set; }
    [BindProperty(SupportsGet = true, Name = "sell")]
    public string? PrefillSell { get; set; }
}

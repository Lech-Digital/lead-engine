using Coldstart.Application.Contact;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

public sealed class ContactModel(ISubmitInquiry submit) : PageModel
{
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Niche { get; set; } = "";
    [BindProperty] public string WhatYouSell { get; set; } = "";
    [BindProperty] public string? Company { get; set; }
    [BindProperty] public string? Message { get; set; }
    [BindProperty] public string? Website { get; set; } // honeypot — must stay empty

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // Honeypot: bots fill hidden fields. Silently succeed without storing/sending.
        if (!string.IsNullOrWhiteSpace(Website))
            return new OkResult();

        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
            return BadRequest("Name and a valid email are required.");

        await submit.SubmitAsync(new SubmitInquiryRequest(Name, Email, Niche, WhatYouSell, Message, Company), ct);
        return new OkResult();
    }
}

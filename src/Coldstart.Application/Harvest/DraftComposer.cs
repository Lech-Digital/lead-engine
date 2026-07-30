namespace Coldstart.Application.Harvest;

// Assembles the final draft from the AI-written hook and the sender's hardcoded
// pitch/CTA paragraphs. Deterministic selling copy: the model personalizes, the
// profile sells. The fixed blocks may reference the prospect via tokens:
//   {company}      — the lead's company name as display text
//   {company-url}  — the same name URL-encoded, for query-string prefills
public static class DraftComposer
{
    public static string Compose(string aiDraft, SenderContext sender, string company = "")
    {
        if (!sender.HasFixedPitch) return aiDraft;
        var blocks = new[] { aiDraft.Trim(), Fill(sender.PitchLine, company), Fill(sender.CtaLine, company) };
        return string.Join("\n\n", blocks.Where(b => !string.IsNullOrWhiteSpace(b)));
    }

    private static string? Fill(string? block, string company) =>
        block?.Trim()
            .Replace("{company}", company)
            .Replace("{company-url}", Uri.EscapeDataString(company));
}

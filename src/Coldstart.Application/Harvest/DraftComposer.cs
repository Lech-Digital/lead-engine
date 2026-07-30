namespace Coldstart.Application.Harvest;

// Assembles the final draft from the AI-written hook and the sender's hardcoded
// pitch/CTA paragraphs. Deterministic selling copy: the model personalizes, the
// profile sells. The fixed blocks may reference the prospect via tokens:
//   {company}      — the lead's company name as display text
//   {company-url}  — the same name URL-encoded, for query-string prefills
public static class DraftComposer
{
    public static string Compose(string aiDraft, SenderContext sender, string company = "", string prospectNiche = "", string prospectSells = "")
    {
        if (!sender.HasFixedPitch) return aiDraft;
        var hook = InsertIntro(aiDraft.Trim(), Fill(sender.IntroLine, company, prospectNiche, prospectSells));
        var blocks = new[] { hook, Fill(sender.PitchLine, company, prospectNiche, prospectSells), Fill(sender.CtaLine, company, prospectNiche, prospectSells), Fill(sender.CtaButtonUrl, company, prospectNiche, prospectSells) };
        return string.Join("\n\n", blocks.Where(b => !string.IsNullOrWhiteSpace(b)));
    }

    // The AI subject is used unless the sender configured a fixed template.
    public static string ComposeSubject(string aiSubject, SenderContext sender, string company = "", string prospectNiche = "", string prospectSells = "") =>
        string.IsNullOrWhiteSpace(sender.SubjectTemplate) ? aiSubject : Fill(sender.SubjectTemplate, company, prospectNiche, prospectSells)!;

    // The intro ("this email is a live demo…") reads best right before the AI's quoted
    // analysis; with no quoted block it lands after the hook.
    private static string InsertIntro(string aiDraft, string? intro)
    {
        if (string.IsNullOrWhiteSpace(intro)) return aiDraft;
        var paras = aiDraft.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        var quoteIndex = paras.FindIndex(p => p.TrimStart().StartsWith('>'));
        if (quoteIndex < 0) paras.Add(intro); else paras.Insert(quoteIndex, intro);
        return string.Join("\n\n", paras);
    }

    private static string? Fill(string? block, string company, string prospectNiche, string prospectSells) =>
        block?.Trim()
            .Replace("{company}", company)
            .Replace("{company-url}", Uri.EscapeDataString(company))
            .Replace("{prospect-niche-url}", Uri.EscapeDataString(prospectNiche))
            .Replace("{prospect-sells-url}", Uri.EscapeDataString(prospectSells));
}

namespace Coldstart.Application.Harvest;

// Assembles the final draft from the AI-written hook and the sender's hardcoded
// pitch/CTA paragraphs. Deterministic selling copy: the model personalizes, the
// profile sells.
public static class DraftComposer
{
    public static string Compose(string aiDraft, SenderContext sender)
    {
        if (!sender.HasFixedPitch) return aiDraft;
        var blocks = new[] { aiDraft.Trim(), sender.PitchLine?.Trim(), sender.CtaLine?.Trim() };
        return string.Join("\n\n", blocks.Where(b => !string.IsNullOrWhiteSpace(b)));
    }
}

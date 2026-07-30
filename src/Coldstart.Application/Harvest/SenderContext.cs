namespace Coldstart.Application.Harvest;

// Who is sending the outreach. Brief/MentionSourceTool feed the AI prompt; PitchLine and
// CtaLine are hardcoded paragraphs composed into the draft AFTER generation — when either
// is set, the model is asked for the personalized hook only.
public sealed record SenderContext(string? Brief, bool MentionSourceTool, string? PitchLine = null, string? CtaLine = null)
{
    public static SenderContext None { get; } = new(null, false);

    public bool HasFixedPitch =>
        !string.IsNullOrWhiteSpace(PitchLine) || !string.IsNullOrWhiteSpace(CtaLine);
}

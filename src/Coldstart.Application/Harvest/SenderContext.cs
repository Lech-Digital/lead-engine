namespace Coldstart.Application.Harvest;

// Who is sending the outreach. Brief/MentionSourceTool feed the AI prompt; PitchLine and
// CtaLine are hardcoded paragraphs composed into the draft AFTER generation — when either
// is set, the model is asked for the personalized hook only.
// SkipFeedback: recent leads this sender skipped, with reasons — the flywheel that makes
// the engine's fit judgment converge on THIS customer's taste.
public sealed record SenderContext(string? Brief, bool MentionSourceTool, string? PitchLine = null, string? CtaLine = null, string? CtaButtonUrl = null, string? IntroLine = null, string? SubjectTemplate = null, string? SkipFeedback = null)
{
    public static SenderContext None { get; } = new(null, false);

    public bool HasFixedPitch =>
        !string.IsNullOrWhiteSpace(PitchLine) || !string.IsNullOrWhiteSpace(CtaLine)
        || !string.IsNullOrWhiteSpace(CtaButtonUrl);
}

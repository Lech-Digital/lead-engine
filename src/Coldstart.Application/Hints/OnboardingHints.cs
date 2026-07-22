namespace Coldstart.Application.Hints;

public sealed record Hint(string Key, string Body);

// Ordered onboarding sequence. Keys are stable identifiers persisted per user — never rename.
public static class OnboardingHints
{
    public static IReadOnlyList<Hint> All { get; } =
    [
        new("how-it-works", "We find leads overnight → you review and approve → the email goes out. Nothing is ever sent without you."),
        new("review-tabs", "To review, Approved, Skipped — drag a lead between tabs, or open one to edit the draft before approving."),
        new("draft-first", "Every lead comes with a ready first email. Read it, tweak it, approve — or skip and it's gone."),
        new("why-now", "The \"Why now\" line is the buying signal we spotted. Lead with it — that's what makes the timing work."),
    ];
}

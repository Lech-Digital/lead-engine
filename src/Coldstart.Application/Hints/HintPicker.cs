namespace Coldstart.Application.Hints;

public sealed record HintState(IReadOnlyCollection<string> SeenKeys, DateOnly? ShownOn, DateOnly? DismissedOn);
public sealed record HintDecision(Hint? ToShow, HintState NextState);

// Pure selection logic — no clock, no I/O. `today` is supplied by the caller.
// Three cadence rules, all satisfied only with the DismissedOn field:
//   (a) at most one hint newly promoted per calendar day,
//   (b) a promoted hint stays visible on same-day reloads until dismissed,
//   (c) dismissing today's hint does NOT surface the next until tomorrow.
public static class HintPicker
{
    public static HintDecision Pick(HintState state, DateOnly today)
    {
        var next = OnboardingHints.All.FirstOrDefault(h => !state.SeenKeys.Contains(h.Key));
        if (next is null)
            return new HintDecision(null, state);                        // onboarding complete

        var shownToday = state.ShownOn == today;
        var dismissedToday = state.DismissedOn == today;

        // (b) today's promoted hint is still pending — keep showing it, don't advance.
        if (shownToday && !dismissedToday)
            return new HintDecision(next, state);

        // (a) new day and nothing dismissed today yet — promote the next hint and stamp today.
        if (!shownToday && !dismissedToday)
            return new HintDecision(next, state with { ShownOn = today });

        // (c) already dismissed today (or promoted-then-dismissed today) — nothing more today.
        return new HintDecision(null, state);
    }

    public static HintState Dismiss(HintState state, string key, DateOnly today)
    {
        var seen = state.SeenKeys.Contains(key) ? state.SeenKeys : [.. state.SeenKeys, key];
        return state with { SeenKeys = seen, DismissedOn = today };
    }

    public static HintState Reset() => new([], null, null);
}

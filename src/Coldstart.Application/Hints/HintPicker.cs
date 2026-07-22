namespace Coldstart.Application.Hints;

public sealed record HintState(IReadOnlyCollection<string> SeenKeys, DateOnly? ShownOn);
public sealed record HintDecision(Hint? ToShow, HintState NextState);

// Pure selection logic — no clock, no I/O. `today` is supplied by the caller.
public static class HintPicker
{
    public static HintDecision Pick(HintState state, DateOnly today)
    {
        var seen = state.SeenKeys;
        var next = OnboardingHints.All.FirstOrDefault(h => !seen.Contains(h.Key));
        if (next is null)
            return new HintDecision(null, state);                       // onboarding complete

        // Already showed a hint today. Keep showing if undismissed; stop if dismissed.
        if (state.ShownOn == today)
        {
            // Only keep showing if we haven't dismissed anything yet (still the same hint).
            if (seen.Count == 0)
                return new HintDecision(next, state);
            else
                return new HintDecision(null, state);
        }

        // A new day (or never shown): promote the next hint and stamp today. One per day.
        return new HintDecision(next, state with { ShownOn = today });
    }

    public static HintState Dismiss(HintState state, string key)
    {
        if (state.SeenKeys.Contains(key)) return state;
        return state with { SeenKeys = [.. state.SeenKeys, key] };
    }

    public static HintState Reset() => new([], null);
}

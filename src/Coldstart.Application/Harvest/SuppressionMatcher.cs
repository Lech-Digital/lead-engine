namespace Coldstart.Application.Harvest;

// Case-insensitive substring match of suppressed values against harvested text.
// Values are stored normalized (lowercase, ≥3 chars — see Suppression.Create).
public static class SuppressionMatcher
{
    public static bool IsMatch(IReadOnlyCollection<string> suppressedValues, params string?[] texts)
    {
        if (suppressedValues.Count == 0) return false;
        return texts.Any(t => !string.IsNullOrEmpty(t)
            && suppressedValues.Any(v => t.Contains(v, StringComparison.OrdinalIgnoreCase)));
    }
}

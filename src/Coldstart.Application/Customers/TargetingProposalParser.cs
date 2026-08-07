using System.Text.RegularExpressions;

namespace Coldstart.Application.Customers;

// Parses the LLM's labelled output into a TargetingInput. At least one recognisable
// label is required; anything else returns null so an unparseable answer is treated as a
// failure rather than as an empty profile.
public static class TargetingProposalParser
{
    private static readonly string[] Labels =
        ["INDUSTRIES", "GEOGRAPHIES", "ROLES", "SIZE_MIN", "SIZE_MAX", "TRIGGERS", "EXCLUSIONS"];

    public static TargetingInput? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var positions = new List<(int LabelStart, int ContentStart, string Label)>();
        foreach (var label in Labels)
        {
            var m = new Regex($@"^\s*{label}\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Multiline).Match(content);
            if (m.Success) positions.Add((m.Index, m.Index + m.Length, label));
        }
        if (positions.Count == 0) return null;
        positions.Sort((a, b) => a.LabelStart.CompareTo(b.LabelStart));

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < positions.Count; i++)
        {
            var (_, contentStart, label) = positions[i];
            var end = i + 1 < positions.Count ? positions[i + 1].LabelStart : content.Length;
            fields[label] = content[contentStart..end].Trim();
        }

        string? List(string label) =>
            fields.TryGetValue(label, out var v) && !string.IsNullOrWhiteSpace(v)
                ? string.Join('\n', v.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                : null;

        int? Number(string label) =>
            fields.TryGetValue(label, out var v) && int.TryParse(v.Trim(), out var n) ? n : null;

        return new TargetingInput(List("INDUSTRIES"), List("GEOGRAPHIES"), List("ROLES"),
            Number("SIZE_MIN"), Number("SIZE_MAX"), List("TRIGGERS"), List("EXCLUSIONS"));
    }
}

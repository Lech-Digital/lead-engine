using System.Text.RegularExpressions;

namespace Coldstart.Application.Harvest;

// Parses the triage LLM's four-label answer into a TriageVerdict. VERDICT is required —
// if it is absent or unrecognised, this returns null so an unparseable answer is treated
// as a triage FAILURE (retried later), never as a silent rejection of a good prospect.
public static class TriageVerdictParser
{
    private static readonly string[] Labels = ["VERDICT", "COMPANY", "MET", "FAILED"];

    public static TriageVerdict? Parse(string content)
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

        var decision = ParseDecision(fields.GetValueOrDefault("VERDICT"));
        if (decision is null) return null;   // no recognisable VERDICT → treat as failure, never as NotFit

        var company = fields.TryGetValue("COMPANY", out var c) && !string.IsNullOrWhiteSpace(c) && c != "-" ? c : null;

        return new TriageVerdict(decision.Value, company, List(fields, "MET"), List(fields, "FAILED"));
    }

    private static TriageDecision? ParseDecision(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalised = raw.Trim().ToUpperInvariant().Replace(" ", "");
        return normalised switch
        {
            "FIT" => TriageDecision.Fit,
            "NOTFIT" or "NO" => TriageDecision.NotFit,
            "UNSURE" or "MAYBE" => TriageDecision.Unsure,
            _ => null,
        };
    }

    // "none" (case-insensitive) means an explicit empty list, not a single item literally
    // named "none" — the prompt asks for exactly that word when nothing matched/failed.
    private static IReadOnlyList<string> List(Dictionary<string, string> fields, string label)
    {
        if (!fields.TryGetValue(label, out var v) || string.IsNullOrWhiteSpace(v)) return [];
        if (v.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return [];
        return v.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

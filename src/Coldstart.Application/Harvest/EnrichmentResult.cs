using System.Text.RegularExpressions;
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

public sealed record EnrichmentResult(Fit Fit, string Summary, string Subject, string Draft, string Company);

public static class EnrichmentParser
{
    private static readonly string[] Labels = ["FIT", "SUMMARY", "SUBJECT", "DRAFT", "COMPANY"];

    public static EnrichmentResult? Parse(string content)
    {
        // Find each label's start position, then slice from just after its colon up to the
        // start of the NEXT present label (or end of content), so multi-line values
        // (e.g. a two-line DRAFT) are captured in full instead of truncated to one line.
        var positions = new List<(int LabelStart, int ContentStart, string Label)>();
        foreach (var label in Labels)
        {
            var m = LabelRegex(label).Match(content);
            if (m.Success)
                positions.Add((m.Index, m.Index + m.Length, label));
        }
        positions.Sort((a, b) => a.LabelStart.CompareTo(b.LabelStart));

        var fields = new Dictionary<string, string>();
        for (var i = 0; i < positions.Count; i++)
        {
            var (_, contentStart, label) = positions[i];
            var end = i + 1 < positions.Count ? positions[i + 1].LabelStart : content.Length;
            fields[label] = content[contentStart..end].Trim();
        }

        string Field(string label) => fields.TryGetValue(label, out var v) ? v : "";

        var fitRaw = Field("FIT").ToUpperInvariant();
        Fit? fit = fitRaw.StartsWith("YES") ? Fit.Yes : fitRaw.StartsWith("MAYBE") ? Fit.Maybe : fitRaw.StartsWith("NO") ? Fit.No : null;
        if (fit is null) return null;   // no parseable FIT line → treat as failure

        return new EnrichmentResult(fit.Value, Field("SUMMARY"), Field("SUBJECT"), Field("DRAFT"), Field("COMPANY"));
    }

    private static Regex LabelRegex(string label) => new($@"^\s*{label}\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
}

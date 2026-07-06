using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

public sealed record EnrichmentResult(Fit Fit, string Summary, string Subject, string Draft, string Company);

public static class EnrichmentParser
{
    public static EnrichmentResult? Parse(string content)
    {
        string Field(string label)
        {
            foreach (var line in content.Split('\n'))
            {
                var t = line.TrimStart();
                if (t.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                    return t[(label.Length + 1)..].Trim();
            }
            return "";
        }

        var fitRaw = Field("FIT").ToUpperInvariant();
        Fit? fit = fitRaw.StartsWith("YES") ? Fit.Yes : fitRaw.StartsWith("MAYBE") ? Fit.Maybe : fitRaw.StartsWith("NO") ? Fit.No : null;
        if (fit is null) return null;   // no parseable FIT line → treat as failure

        return new EnrichmentResult(fit.Value, Field("SUMMARY"), Field("SUBJECT"), Field("DRAFT"), Field("COMPANY"));
    }
}

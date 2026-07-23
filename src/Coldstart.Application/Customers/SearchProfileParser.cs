using System.Text.RegularExpressions;

namespace Coldstart.Application.Customers;

// Parses the LLM's labelled output into a SearchProfile. Requires OFFER + KEYWORDS;
// SIGNALS/DISQUALIFIERS are optional. Comma-or-newline lists are normalised to newline-joined.
public static class SearchProfileParser
{
    private static readonly string[] Labels = ["OFFER", "KEYWORDS", "SIGNALS", "DISQUALIFIERS"];

    public static SearchProfile? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var positions = new List<(int LabelStart, int ContentStart, string Label)>();
        foreach (var label in Labels)
        {
            var m = new Regex($@"^\s*{label}\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Multiline).Match(content);
            if (m.Success) positions.Add((m.Index, m.Index + m.Length, label));
        }
        positions.Sort((a, b) => a.LabelStart.CompareTo(b.LabelStart));

        var fields = new Dictionary<string, string>();
        for (var i = 0; i < positions.Count; i++)
        {
            var (_, start, label) = positions[i];
            var end = i + 1 < positions.Count ? positions[i + 1].LabelStart : content.Length;
            fields[label] = content[start..end].Trim();
        }

        string Field(string l) => fields.TryGetValue(l, out var v) ? v : "";
        static string JoinList(string raw) => string.Join('\n',
            raw.Split(['\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var offer = Field("OFFER");
        var keywords = JoinList(Field("KEYWORDS"));
        if (offer.Length == 0 || keywords.Length == 0) return null;   // required

        return new SearchProfile(offer, keywords, JoinList(Field("SIGNALS")), JoinList(Field("DISQUALIFIERS")));
    }
}

using System.Text;

namespace Coldstart.Cli;

internal static class Table
{
    public static string Render(string[] headers, IReadOnlyList<string[]> rows)
    {
        var widths = headers
            .Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length)))
            .ToArray();
        string Line(string[] cells) =>
            string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd();

        var sb = new StringBuilder();
        sb.AppendLine(Line(headers));
        foreach (var row in rows) sb.AppendLine(Line(row));
        return sb.ToString().TrimEnd();
    }
}

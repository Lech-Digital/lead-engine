using System.Text;
using Coldstart.Application.Leads;

namespace Coldstart.Web.Areas.App.Pages;

// CSV edition of a customer's leads (the Terms art. 8 export promise). RFC-4180 quoting,
// plus a guard against spreadsheet formula injection in text that came from the internet.
internal static class LeadCsv
{
    internal static string Build(IEnumerable<LeadSummary> leads)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Company,Status,Fit,Score,Source,Recipient email,Email confidence,Subject,Draft,Matched signals,Created (UTC)");
        foreach (var l in leads)
        {
            sb.AppendJoin(',',
                Field(l.Company), Field(StatusLabel(l.Status)), Field(l.Fit),
                l.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                Field(l.Source), Field(l.RecipientEmail ?? ""), Field(l.EmailConfidence),
                Field(l.Subject), Field(l.Draft), Field(l.MatchedSignals),
                Field(l.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm")));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // Portal language, not enum names: customers know "Skipped", not "Shelved".
    private static string StatusLabel(string status) => status switch
    {
        "PendingReview" => "Pending review",
        "Shelved" or "Rejected" => "Skipped",
        _ => status,
    };

    private static string Field(string value)
    {
        // A leading =, +, -, or @ would execute as a formula when opened in Excel/Sheets.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}

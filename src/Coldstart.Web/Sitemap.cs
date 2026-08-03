using System.Text;

namespace Coldstart.Web;

// The single source of truth for what Google may index. A new public page is a
// one-line addition here; everything else (app, admin, identity, webhooks) is
// deliberately absent and additionally disallowed in wwwroot/robots.txt.
internal static class Sitemap
{
    private static readonly string[] Routes =
        ["", "about", "contact", "examples", "faq", "technology", "terms", "privacy", "verwerkersovereenkomst", "inquiry"];

    public static string Xml(string baseUrl)
    {
        var root = baseUrl.TrimEnd('/');
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");
        foreach (var route in Routes)
            sb.AppendLine($"  <url><loc>{root}/{route}</loc></url>");
        sb.Append("</urlset>");
        return sb.ToString();
    }
}

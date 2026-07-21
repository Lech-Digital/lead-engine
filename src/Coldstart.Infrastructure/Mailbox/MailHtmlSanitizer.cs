using Ganss.Xss;

namespace Coldstart.Infrastructure.Mailbox;

internal sealed record SanitizedHtml(string Html, bool RemoteImagesRemoved);

// Boundary defense: mail HTML is hostile input. Everything the panel renders
// passes through here first; the Application layer only ever sees the output.
internal static class MailHtmlSanitizer
{
    public static SanitizedHtml Sanitize(string html)
    {
        var removedRemoteImage = false;
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Add("cid");          // inline images
        sanitizer.AllowedSchemes.Remove("data");

        // Defense-in-depth: don't rely solely on library defaults for these.
        // SVG <image href="..."> is a remote-image vector the IMG-only check
        // below can't see; srcset/sizes let a browser fetch a remote image
        // even when src is a safe cid: reference.
        sanitizer.AllowedTags.Remove("svg");
        sanitizer.AllowedTags.Remove("image");
        sanitizer.AllowedAttributes.Remove("srcset");
        sanitizer.AllowedAttributes.Remove("sizes");

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not AngleSharp.Dom.IElement el) return;
            if (el.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase))
            {
                var src = el.GetAttribute("src") ?? "";
                if (!src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                {
                    removedRemoteImage = true;
                    el.Remove();
                }
            }
            else if (el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                el.SetAttribute("target", "_blank");
                el.SetAttribute("rel", "noopener noreferrer");
            }
        };

        var clean = sanitizer.Sanitize(html);
        return new SanitizedHtml(clean, removedRemoteImage);
    }
}

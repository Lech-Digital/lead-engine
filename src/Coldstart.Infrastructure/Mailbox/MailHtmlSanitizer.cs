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
        // even when src is a safe cid: reference. Inline "style" is a remote-image
        // vector too (background/background-image/content: url(...)), and
        // <input type="image">/<form>/<button> can carry their own remote src
        // or submit targets — the sanitizer has no legitimate need to keep them
        // in read-only mail HTML, so they're dropped outright rather than sanitized.
        sanitizer.AllowedTags.Remove("svg");
        sanitizer.AllowedTags.Remove("image");
        sanitizer.AllowedTags.Remove("input");
        sanitizer.AllowedTags.Remove("form");
        sanitizer.AllowedTags.Remove("button");
        sanitizer.AllowedAttributes.Remove("srcset");
        sanitizer.AllowedAttributes.Remove("sizes");
        sanitizer.AllowedAttributes.Remove("style");

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not AngleSharp.Dom.IElement el) return;
            if (el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                el.SetAttribute("target", "_blank");
                el.SetAttribute("rel", "noopener noreferrer");
                return;
            }

            var src = el.GetAttribute("src");
            if (src is not null && !src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
            {
                removedRemoteImage = true;
                el.Remove();
            }
        };

        var clean = sanitizer.Sanitize(html);
        return new SanitizedHtml(clean, removedRemoteImage);
    }
}

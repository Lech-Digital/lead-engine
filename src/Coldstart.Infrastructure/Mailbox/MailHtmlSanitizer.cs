using Ganss.Xss;

namespace Coldstart.Infrastructure.Mailbox;

internal sealed record SanitizedHtml(string Html, bool RemoteImagesRemoved);

// Boundary defense: mail HTML is hostile input. Everything the panel renders
// passes through here first; the Application layer only ever sees the output.
internal static class MailHtmlSanitizer
{
    private static readonly string[] RemoteResourceAttributes = ["src", "href", "xlink:href", "poster", "background"];

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

        // Not in the default allow-list, but legacy remote-image vectors on
        // otherwise-harmless tags (<td background="...">, <video poster="...">).
        // Let them through the attribute filter so PostProcessNode below can see
        // and strip the remote ones by value, same as it does for src.
        sanitizer.AllowedAttributes.Add("background");
        sanitizer.AllowedAttributes.Add("poster");
        sanitizer.AllowedAttributes.Add("xlink:href");

        // The "style" attribute and the input/svg/image/form/button tags above
        // are dropped outright (never reach PostProcessNode below), but when
        // what they were hiding was itself a remote-image vector, the reader
        // still deserves the "remote images were removed" warning.
        sanitizer.RemovingAttribute += (_, e) =>
        {
            if (e.Attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase) &&
                CssHasRemoteUrl(e.Attribute.Value))
            {
                removedRemoteImage = true;
            }
        };
        sanitizer.RemovingTag += (_, e) =>
        {
            var tag = e.Tag;
            var isRemoteImageTag =
                (tag.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(tag.GetAttribute("type"), "image", StringComparison.OrdinalIgnoreCase)) ||
                tag.TagName.Equals("IMAGE", StringComparison.OrdinalIgnoreCase);
            if (isRemoteImageTag && IsRemoteResource(tag.GetAttribute("src") ?? tag.GetAttribute("href")))
            {
                removedRemoteImage = true;
            }
        };

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not AngleSharp.Dom.IElement el) return;
            if (el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                el.SetAttribute("target", "_blank");
                el.SetAttribute("rel", "noopener noreferrer");
                return;
            }

            // Any element that can point at a remote resource — not just <img> —
            // is a potential tracking-pixel vector (e.g. legacy background="..."
            // on <td>/<table>, <video poster="...">, or leftover xlink:href).
            // Strip the offending attribute rather than the whole element so
            // container content (e.g. table cell text) survives.
            foreach (var attr in RemoteResourceAttributes)
            {
                var value = el.GetAttribute(attr);
                if (IsRemoteResource(value))
                {
                    removedRemoteImage = true;
                    el.RemoveAttribute(attr);
                }
            }
        };

        var clean = sanitizer.Sanitize(html);
        return new SanitizedHtml(clean, removedRemoteImage);
    }

    private static bool IsRemoteResource(string? value) =>
        value is not null && !value.StartsWith("cid:", StringComparison.OrdinalIgnoreCase);

    // Best-effort sniff for a remote url(...) inside a CSS value (e.g. from a
    // "style" attribute that's being dropped wholesale) — used only to decide
    // whether the reader should see the "remote images were removed" notice.
    private static bool CssHasRemoteUrl(string cssValue)
    {
        var idx = cssValue.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            var start = idx + 4;
            var end = cssValue.IndexOf(')', start);
            var raw = end > start ? cssValue[start..end] : cssValue[start..];
            var inner = raw.Trim().Trim('\'', '"');
            if (inner.Length > 0 && !inner.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) return true;
            idx = cssValue.IndexOf("url(", start, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

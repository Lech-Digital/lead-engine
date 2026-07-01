"""Turns a stored plain-text draft (body only, no signature) into the two forms an
email carries: a plain-text part and a lightweight branded HTML part. The HTML logo
is rendered with inline CSS — no image — so it shows even when a recipient has images
turned off, and adds no attachment/tracking-pixel spam signal."""
from __future__ import annotations

import html as _html

SITE_URL = "https://getcoldstart.netlify.app"
SITE_LABEL = "getcoldstart.netlify.app"
SENDER_NAME = "Andrzej Lech"
SENDER_TITLE = "Founder, Coldstart"

TEXT_SIGNATURE = f"{SENDER_NAME}\n{SENDER_TITLE}\n{SITE_URL}"


def render_text(body: str) -> str:
    """Plain-text part: the draft plus a plain signature block."""
    return f"{body.strip()}\n\n{TEXT_SIGNATURE}"


def render_html(body: str) -> str:
    """Branded HTML part: escaped paragraphs plus the CSS-rendered logo signature."""
    paragraphs = "".join(
        f'<p style="margin:0 0 14px;">{_html.escape(block)}</p>'
        for block in body.strip().split("\n\n")
    )
    return (
        '<div style="font-family:-apple-system,BlinkMacSystemFont,\'Segoe UI\',Roboto,'
        "Helvetica,Arial,sans-serif;font-size:15px;line-height:1.6;color:#2b2b2b;"
        'max-width:520px;">'
        f"{paragraphs}{_signature_html()}"
        "</div>"
    )


def _signature_html() -> str:
    return (
        '<div style="margin-top:20px;padding-top:14px;border-top:1px solid #e6ddc9;">'
        "<div style=\"font-family:'SF Mono',ui-monospace,Menlo,Consolas,monospace;"
        'font-weight:800;font-size:17px;letter-spacing:-0.3px;color:#382C23;">'
        '<span style="display:inline-block;background:#E07A3E;color:#FBF4E6;'
        'border-radius:6px;padding:1px 8px;margin-right:7px;">C</span>'
        'Cold<span style="color:#A04E1E;">start</span></div>'
        f'<div style="font-size:14px;color:#382C23;margin-top:7px;"><b>{SENDER_NAME}</b> — Founder<br>'
        f'<a href="{SITE_URL}" style="color:#A04E1E;text-decoration:none;">{SITE_LABEL}</a></div>'
        "</div>"
    )

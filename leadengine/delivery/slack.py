from __future__ import annotations

import requests

from ..models import Lead


class SlackDelivery:
    """Posts a formatted lead to a Slack Incoming Webhook.

    This is the 'appears magically every morning' experience the client pays for.
    Create a webhook at https://api.slack.com/messaging/webhooks and put the URL in
    SLACK_WEBHOOK_URL. One webhook = one channel = one client.
    """

    def __init__(self, webhook_url: str):
        self.webhook_url = webhook_url

    @property
    def enabled(self) -> bool:
        return bool(self.webhook_url)

    def send(self, lead: Lead) -> bool:
        if not self.enabled:
            return False
        try:
            resp = requests.post(self.webhook_url, json=self._blocks(lead), timeout=20)
            resp.raise_for_status()
            return True
        except requests.RequestException:
            return False

    def _blocks(self, lead: Lead) -> dict:
        fit_emoji = {"YES": "🟢", "MAYBE": "🟡", "NO": "🔴"}.get(lead.fit.upper(), "⚪")
        header = lead.title or lead.author or "New lead"
        fields = (
            f"*Source:* {lead.source}  ·  *Score:* {lead.score:.2f}  ·  "
            f"*Fit:* {fit_emoji} {lead.fit or 'n/a'}\n"
            f"*Author:* {lead.author or 'unknown'}\n"
            f"*Signals:* {', '.join(lead.matched_signals) or 'n/a'}"
        )
        text_lines = [f"*{header}*", fields]
        if lead.summary:
            text_lines.append(f"*Why:* {lead.summary}")
        if lead.subject:
            text_lines.append(f"*Subject:* {lead.subject}")
        if lead.draft:
            text_lines.append(f"*Draft:*\n{lead.draft}")
        if lead.contact:
            text_lines.append(f"*Find the buyer:*\n{lead.contact}")
        if lead.url:
            text_lines.append(f"<{lead.url}|View source>")

        return {
            "blocks": [
                {"type": "section", "text": {"type": "mrkdwn", "text": "\n\n".join(text_lines)}},
                {"type": "divider"},
            ],
            # Fallback for notifications / clients that don't render blocks.
            "text": f"New lead ({lead.score:.2f}): {header}",
        }

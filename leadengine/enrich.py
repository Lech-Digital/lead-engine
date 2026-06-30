from __future__ import annotations

import re

from .config import Config
from .contact import build_contact
from .llm.base import LLMClient
from .models import Lead, RawItem
from .signals import SignalResult

SYSTEM = (
    "You are a top-performing B2B SDR known for replies, not blasts. You read one piece of "
    "public web content, judge honestly whether the author/company is a real fit for a "
    "specific offer, and if so write outreach so specific it could not have been sent to "
    "anyone else. You never invent facts. You would rather say NO than send a generic pitch."
)

# Structured, parseable output. We qualify FIRST (cheap to discard a bad fit) and only
# then write the message — this is what separates a $1,500 lead from spam.
PROMPT_TEMPLATE = """\
OFFER WE SELL:
{offer}

WHO IS A GOOD FIT:
{niche}

WHO IS NOT A FIT — mark FIT: NO if ANY of these apply, even when keywords match:
{disqualifiers}

HARVESTED CONTENT (source: {source} · author: {author}):
\"\"\"
{text}
\"\"\"

Pre-detected signals (keyword-level, may be noisy): {signals}

Return EXACTLY these five sections, each on its own line with the label:

FIT: YES | MAYBE | NO  — would this specific person plausibly buy the offer? Be strict; \
default to NO if the content is off-topic, a job seeker, a competitor, or just noise.
SUMMARY: one sentence — who they are and the concrete reason they do or don't fit.
SUBJECT: a 4-7 word subject line referencing their specific situation (no clickbait, no emojis).
DRAFT: 3-4 sentences from {sender_name} ({sender_title}).
COMPANY: the name of the company/organisation this is about, or "-" if it cannot be \
determined from the content. Use the real name only — never guess.

DRAFT rules:
- Open by referencing a SPECIFIC detail from the content above (quote or paraphrase it).
- Connect that detail to one concrete way the offer helps. No feature lists.
- End with a single low-friction question.
- BANNED phrases: "I hope this email finds you well", "I came across", "reaching out", \
"game-changer", "revolutionize", "synergy", "circle back", "touch base", "in today's fast-paced".
- No greeting line, no signature block — just the message body.

If FIT is NO, still fill SUBJECT and DRAFT with "-".
"""


def enrich(item: RawItem, signal: SignalResult, config: Config, llm: LLMClient) -> Lead:
    disqualifiers = config.disqualifiers or ["(none specified)"]
    prompt = PROMPT_TEMPLATE.format(
        offer=config.client.get("offer", ""),
        niche=config.niche.get("description", ""),
        disqualifiers="\n".join(f"- {d}" for d in disqualifiers),
        source=item.source,
        author=item.author or "unknown",
        text=item.text[:4000],
        signals=", ".join(signal.matched) or "none",
        sender_name=config.client.get("sender_name", "Me"),
        sender_title=config.client.get("sender_title", ""),
    )
    raw = llm.complete(SYSTEM, prompt, max_tokens=700)
    parsed = _parse(raw)

    company = parsed["company"]
    # Only research a contact for leads worth pursuing — and only when we have a real
    # company name. Deterministic + free, so it never costs an LLM call.
    contact = ""
    status = ""
    if parsed["fit"].upper() in ("YES", "MAYBE"):
        contact = build_contact(company, config.buyer_titles, item.url)
        # Worth pursuing -> enters the approval queue. It never auto-sends.
        status = "pending_review"

    return Lead(
        fingerprint=item.fingerprint,
        source=item.source,
        author=item.author,
        title=item.title,
        url=item.url,
        created_at=item.created_at,
        score=signal.score,
        matched_signals=signal.matched,
        fit=parsed["fit"],
        summary=parsed["summary"],
        subject=parsed["subject"],
        draft=parsed["draft"],
        company=company if company not in {"-", "unknown", "n/a"} else "",
        contact=contact,
        status=status,
        raw_text=item.text[:2000],
    )


_LABELS = ("FIT", "SUMMARY", "SUBJECT", "DRAFT", "COMPANY")


def _parse(raw: str) -> dict:
    """Pull the four labelled sections out of the model output, tolerant of formatting drift."""
    out = {"fit": "", "summary": "", "subject": "", "draft": raw.strip(), "company": ""}
    if not raw:
        return out

    # Find each label's position, then slice between consecutive labels.
    positions = []
    for label in _LABELS:
        m = re.search(rf"(?im)^\s*{label}\s*:\s*", raw)
        if m:
            positions.append((m.start(), m.end(), label))
    positions.sort()

    for i, (_, content_start, label) in enumerate(positions):
        end = positions[i + 1][0] if i + 1 < len(positions) else len(raw)
        value = raw[content_start:end].strip()
        out[label.lower()] = value

    # Normalise the FIT verdict to one token.
    fit_match = re.search(r"(YES|MAYBE|NO)", out["fit"].upper())
    out["fit"] = fit_match.group(1) if fit_match else out["fit"]
    return out

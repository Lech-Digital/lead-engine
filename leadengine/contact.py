from __future__ import annotations

from urllib.parse import quote_plus

# The genuine missing 20%: turning a company-level signal into "who do I contact and
# how do I find them." We deliberately produce RESEARCH LINKS, never fabricated email
# addresses — a guessed address tanks deliverability and breaks the "never invent
# facts" rule. These links drop you onto the exact people-search for the likely buyer.


def build_contact(company: str, buyer_titles: list[str], source_url: str = "") -> str:
    """Build honest, actionable links to find the decision-maker at `company`.

    Returns an empty string when there is no usable company to research.
    """
    company = (company or "").strip()
    if not company or company in {"-", "unknown", "n/a"}:
        return ""

    primary_title = buyer_titles[0] if buyer_titles else "Head of People"
    lines = [f"Company: {company}"]

    linkedin = (
        "https://www.linkedin.com/search/results/people/?keywords="
        + quote_plus(f"{company} {primary_title}")
    )
    lines.append(f"Find buyer ({primary_title}): {linkedin}")

    web = "https://www.google.com/search?q=" + quote_plus(
        f"{company} {primary_title} LinkedIn"
    )
    lines.append(f"Web search: {web}")

    if source_url:
        lines.append(f"Signal source: {source_url}")

    return "\n".join(lines)

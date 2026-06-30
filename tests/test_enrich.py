from leadengine.config import Config
from leadengine.enrich import enrich
from leadengine.models import RawItem
from leadengine.signals import SignalResult

DISQUALIFIER = "sells recruiting or ATS software themselves (a competitor)"

CONFIG = Config(raw={
    "client": {"offer": "An ATS", "sender_name": "Andrzej", "sender_title": "Founder"},
    "niche": {
        "description": "B2B companies hiring HR leaders",
        "disqualifiers": [DISQUALIFIER, "an anonymous forum commenter just venting"],
        "buyer_titles": ["Head of People", "Head of Talent"],
    },
})


class FakeLLM:
    """Captures the prompt it is given and returns a canned, parseable response."""

    def __init__(self, response: str):
        self.response = response
        self.seen_prompt = ""

    def complete(self, system: str, prompt: str, max_tokens: int = 600) -> str:
        self.seen_prompt = prompt
        return self.response


def _item():
    return RawItem(source="funding", external_id="1", text="Acme raised a Series A.",
                   title="Acme raises $10M", url="https://example.com/acme")


def _signal():
    return SignalResult(score=0.5, matched=["raised"])


def test_enrich_prompt_lists_the_configured_disqualifiers():
    llm = FakeLLM("FIT: NO\nSUMMARY: -\nSUBJECT: -\nDRAFT: -\nCOMPANY: -")
    enrich(_item(), _signal(), CONFIG, llm)
    assert DISQUALIFIER in llm.seen_prompt
    assert "anonymous forum commenter" in llm.seen_prompt


def test_enrich_extracts_company_and_builds_contact_research_for_a_fit():
    llm = FakeLLM(
        "FIT: YES\nSUMMARY: A funded startup scaling its team.\n"
        "SUBJECT: Scaling your team\nDRAFT: Saw you raised. Worth a chat?\n"
        "COMPANY: Acme Inc"
    )
    lead = enrich(_item(), _signal(), CONFIG, llm)
    assert lead.company == "Acme Inc"
    # Honest, actionable research links targeting a configured buyer title — never a
    # fabricated email address.
    assert "linkedin.com" in lead.contact.lower()
    assert "Acme" in lead.contact
    assert "Head of People" in lead.contact
    assert "@" not in lead.contact  # no invented email addresses


def test_enrich_no_fit_gets_no_contact_research():
    llm = FakeLLM("FIT: NO\nSUMMARY: Not a fit.\nSUBJECT: -\nDRAFT: -\nCOMPANY: Acme Inc")
    lead = enrich(_item(), _signal(), CONFIG, llm)
    assert lead.fit == "NO"
    assert lead.contact == ""


def test_enrich_fit_lead_enters_review_queue():
    llm = FakeLLM(
        "FIT: YES\nSUMMARY: A funded startup.\nSUBJECT: Hi\nDRAFT: Worth a chat?\nCOMPANY: Acme Inc"
    )
    lead = enrich(_item(), _signal(), CONFIG, llm)
    # A YES never auto-sends — it waits in the approval queue.
    assert lead.status == "pending_review"
    assert lead.recipient_email == ""


def test_enrich_no_fit_does_not_enter_queue():
    llm = FakeLLM("FIT: NO\nSUMMARY: Not a fit.\nSUBJECT: -\nDRAFT: -\nCOMPANY: -")
    lead = enrich(_item(), _signal(), CONFIG, llm)
    assert lead.status == ""

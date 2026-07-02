from leadengine.config import Config
from leadengine.email.base import EmailSender
from leadengine.email.providers import DryRunSender
from leadengine.models import Lead
from leadengine.pipeline import send_approved
from leadengine.storage import Storage

CONFIG = Config(raw={})


def _approved_lead(fp: str, email: str = "vp@acme.com", company: str = "Acme") -> Lead:
    return Lead(
        fingerprint=fp, source="test", author="a", title="t", url="u", created_at="",
        score=0.9, subject="Scaling your team", draft="Worth a chat?",
        status="approved", recipient_email=email, company=company,
    )


def test_send_approved_sends_and_marks_sent(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    sender = DryRunSender()

    sent, failed, skipped = send_approved(CONFIG, sender=sender, storage=db, now="2026-06-29T00:00:00Z")

    assert (sent, failed, skipped) == (1, 0, 0)
    # send_approved renders the stored draft into a branded text + HTML pair.
    (to, subject, text, html), = sender.sent
    assert (to, subject) == ("vp@acme.com", "Scaling your team")
    assert text.startswith("Worth a chat?")
    assert "Andrzej Lech" in text  # signature appended
    assert html is not None and "start" in html  # branded HTML alternative
    assert db.approved_unsent() == []  # left the queue, won't re-send


def test_send_approved_is_idempotent_across_runs(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    send_approved(CONFIG, sender=DryRunSender(), storage=db, now="t")
    # A second pass has nothing left to send.
    again = DryRunSender()
    assert send_approved(CONFIG, sender=again, storage=db, now="t") == (0, 0, 0)
    assert again.sent == []


def test_send_approved_keeps_lead_queued_on_failure(tmp_path):
    class FailSender(EmailSender):
        enabled = True

        def send(self, to, subject, body, html=None):
            return False

    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    sent, failed, skipped = send_approved(CONFIG, sender=FailSender(), storage=db, now="t")

    assert (sent, failed, skipped) == (0, 1, 0)
    assert [l.fingerprint for l in db.approved_unsent()] == ["lead1"]  # retried next time


def test_send_approved_no_op_when_sender_disabled(tmp_path):
    from leadengine.email.base import NullSender

    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    assert send_approved(CONFIG, sender=NullSender(), storage=db, now="t") == (0, 0, 0)
    assert [l.fingerprint for l in db.approved_unsent()] == ["lead1"]


def test_send_approved_preview_hands_off_but_does_not_mark_sent(tmp_path):
    # A dry-run preview must never burn the queue: the message is handed to the sender
    # for inspection, but the lead stays approved so the real send still happens.
    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    sender = DryRunSender()

    sent, failed, skipped = send_approved(CONFIG, sender=sender, storage=db, now="t", preview=True)

    assert (sent, failed, skipped) == (1, 0, 0)
    assert len(sender.sent) == 1  # was rendered + handed off
    assert [l.fingerprint for l in db.approved_unsent()] == ["lead1"]  # still queued


def test_send_approved_skips_company_already_contacted(tmp_path):
    # "Acme AI" was emailed before; a re-harvested "Acme" (different id + address, same
    # place) must not be emailed a second time.
    db = Storage(str(tmp_path / "x.db"))
    already = _approved_lead("old", email="ceo@acme.com", company="Acme AI")
    already.status, already.sent_at = "sent", "2026-06-30T00:00:00Z"
    db.save(already)
    db.save(_approved_lead("new", email="founder@acme-different.com", company="Acme"))
    sender = DryRunSender()

    sent, failed, skipped = send_approved(CONFIG, sender=sender, storage=db, now="t")

    assert (sent, failed, skipped) == (0, 0, 1)
    assert sender.sent == []
    assert [l.fingerprint for l in db.approved_unsent()] == ["new"]  # left untouched, not sent


def test_send_approved_skips_recipient_domain_already_contacted(tmp_path):
    # Same company domain = same place: hello@x.com then founder@x.com is one outreach.
    db = Storage(str(tmp_path / "x.db"))
    already = _approved_lead("old", email="hello@x.com", company="Xco")
    already.status, already.sent_at = "sent", "t0"
    db.save(already)
    db.save(_approved_lead("new", email="founder@x.com", company="Totally Other Name"))

    sent, failed, skipped = send_approved(CONFIG, sender=DryRunSender(), storage=db, now="t")

    assert (sent, failed, skipped) == (0, 0, 1)


def test_send_approved_dedups_within_a_single_batch(tmp_path):
    # Two approved rows for the same place in one run: send one, skip the other.
    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("a", email="a@dup.com", company="Dup"))
    db.save(_approved_lead("b", email="b@dup.com", company="Dup Labs"))

    sent, failed, skipped = send_approved(CONFIG, sender=DryRunSender(), storage=db, now="t")

    assert (sent, failed, skipped) == (1, 0, 1)

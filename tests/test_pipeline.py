from leadengine.config import Config
from leadengine.email.base import EmailSender
from leadengine.email.providers import DryRunSender
from leadengine.models import Lead
from leadengine.pipeline import send_approved
from leadengine.storage import Storage

CONFIG = Config(raw={})


def _approved_lead(fp: str, email: str = "vp@acme.com") -> Lead:
    return Lead(
        fingerprint=fp, source="test", author="a", title="t", url="u", created_at="",
        score=0.9, subject="Scaling your team", draft="Worth a chat?",
        status="approved", recipient_email=email,
    )


def test_send_approved_sends_and_marks_sent(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    sender = DryRunSender()

    sent, failed = send_approved(CONFIG, sender=sender, storage=db, now="2026-06-29T00:00:00Z")

    assert (sent, failed) == (1, 0)
    assert sender.sent == [("vp@acme.com", "Scaling your team", "Worth a chat?")]
    assert db.approved_unsent() == []  # left the queue, won't re-send


def test_send_approved_is_idempotent_across_runs(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    send_approved(CONFIG, sender=DryRunSender(), storage=db, now="t")
    # A second pass has nothing left to send.
    again = DryRunSender()
    assert send_approved(CONFIG, sender=again, storage=db, now="t") == (0, 0)
    assert again.sent == []


def test_send_approved_keeps_lead_queued_on_failure(tmp_path):
    class FailSender(EmailSender):
        enabled = True

        def send(self, to, subject, body):
            return False

    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    sent, failed = send_approved(CONFIG, sender=FailSender(), storage=db, now="t")

    assert (sent, failed) == (0, 1)
    assert [l.fingerprint for l in db.approved_unsent()] == ["lead1"]  # retried next time


def test_send_approved_no_op_when_sender_disabled(tmp_path):
    from leadengine.email.base import NullSender

    db = Storage(str(tmp_path / "x.db"))
    db.save(_approved_lead("lead1"))
    assert send_approved(CONFIG, sender=NullSender(), storage=db, now="t") == (0, 0)
    assert [l.fingerprint for l in db.approved_unsent()] == ["lead1"]

from leadengine.models import Lead
from leadengine.storage import Storage


def _lead(fp: str, score: float = 0.9, status: str = "") -> Lead:
    return Lead(
        fingerprint=fp, source="test", author="a", title="t", url="u",
        created_at="", score=score, matched_signals=["hiring"], summary="s", draft="d",
        status=status,
    )


def test_save_dedupes_on_fingerprint(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    assert db.save(_lead("abc")) is True       # first insert
    assert db.save(_lead("abc")) is False      # duplicate ignored
    assert db.already_seen("abc") is True
    assert db.already_seen("nope") is False


def test_export_csv_filters_by_score(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    db.save(_lead("high", 0.9))
    db.save(_lead("low", 0.1))
    csv_path = str(tmp_path / "out.csv")
    n = db.export_csv(csv_path, min_score=0.5)
    assert n == 1
    assert "high" in (tmp_path / "out.csv").read_text()
    assert "low" not in (tmp_path / "out.csv").read_text()


def test_save_persists_company_and_contact(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    lead = _lead("withcontact")
    lead.company = "Acme Inc"
    lead.contact = "Company: Acme Inc\nFind buyer (Head of People): https://linkedin.com/..."
    db.save(lead)
    csv_path = str(tmp_path / "out.csv")
    db.export_csv(csv_path, min_score=0.0)
    text = (tmp_path / "out.csv").read_text()
    assert "Acme Inc" in text
    assert "Head of People" in text


def test_existing_db_without_new_columns_is_migrated(tmp_path):
    import sqlite3
    # Simulate a pre-existing v0.x database: all base columns present, but no
    # company/contact (the columns this version adds).
    path = str(tmp_path / "old.db")
    legacy = sqlite3.connect(path)
    legacy.execute(
        """CREATE TABLE leads (
            fingerprint TEXT PRIMARY KEY, source TEXT, author TEXT, title TEXT,
            url TEXT, created_at TEXT, score REAL, matched_signals TEXT, fit TEXT,
            summary TEXT, subject TEXT, draft TEXT, raw_text TEXT, harvested_at TEXT,
            delivered INTEGER DEFAULT 0)"""
    )
    legacy.commit()
    legacy.close()

    db = Storage(path)  # __init__ must add the missing columns, not crash
    lead = _lead("migrated")
    lead.company = "Migrated Co"
    assert db.save(lead) is True
    assert db.undelivered()[0].company == "Migrated Co"


def test_approval_lifecycle_pending_to_approved_to_sent(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    db.save(_lead("lead1", status="pending_review"))

    # Lands in the review queue.
    pending = db.pending_review()
    assert [l.fingerprint for l in pending] == ["lead1"]

    # Operator supplies the real recipient (never guessed) and approves.
    db.set_status("lead1", "approved", recipient_email="vp@acme.com")
    assert db.pending_review() == []                       # left the queue
    approved = db.approved_unsent()
    assert [l.fingerprint for l in approved] == ["lead1"]
    assert approved[0].recipient_email == "vp@acme.com"

    # After sending it leaves the send queue and won't be re-sent.
    db.mark_sent("lead1", "2026-06-29T10:00:00Z")
    assert db.approved_unsent() == []


def test_reject_removes_from_review_queue(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    db.save(_lead("bad", status="pending_review"))
    db.set_status("bad", "rejected")
    assert db.pending_review() == []
    assert db.approved_unsent() == []


def test_resolve_fingerprint_matches_unique_prefix(tmp_path):
    db = Storage(str(tmp_path / "x.db"))
    db.save(_lead("abc123", status="pending_review"))
    db.save(_lead("def456", status="pending_review"))
    assert db.resolve_fingerprint("abc") == "abc123"
    assert db.resolve_fingerprint("zzz") is None      # no match
    db.save(_lead("abc999", status="pending_review"))
    assert db.resolve_fingerprint("abc") is None      # ambiguous -> refuse to guess

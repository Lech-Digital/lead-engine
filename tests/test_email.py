from leadengine.config import Config
from leadengine.email import DryRunSender, NullSender, ResendSender, build_email_sender


def test_null_sender_is_disabled_and_never_sends():
    sender = NullSender()
    assert sender.enabled is False
    assert sender.send("a@b.com", "subj", "body") is False


def test_dryrun_sender_records_without_sending():
    sender = DryRunSender()
    assert sender.enabled is True
    assert sender.send("a@b.com", "subj", "body") is True
    assert sender.sent == [("a@b.com", "subj", "body")]


def test_resend_sender_posts_to_api_and_returns_true_on_2xx(monkeypatch):
    captured = {}

    class FakeResp:
        status_code = 200

        def raise_for_status(self):
            pass

    def fake_post(url, **kw):
        captured["url"] = url
        captured["headers"] = kw.get("headers", {})
        captured["json"] = kw.get("json", {})
        return FakeResp()

    monkeypatch.setattr("leadengine.email.providers.requests.post", fake_post)

    sender = ResendSender(api_key="re_test", from_addr="me@acme.com", from_name="Andrzej")
    ok = sender.send("vp@globex.com", "Scaling your team", "Hi there.")

    assert ok is True
    assert "resend.com" in captured["url"]
    assert captured["headers"]["Authorization"] == "Bearer re_test"
    assert captured["json"]["to"] == ["vp@globex.com"]
    assert captured["json"]["from"] == "Andrzej <me@acme.com>"
    assert captured["json"]["subject"] == "Scaling your team"


def test_resend_sender_returns_false_on_error_never_raises(monkeypatch):
    def boom(url, **kw):
        raise __import__("requests").RequestException("network down")

    monkeypatch.setattr("leadengine.email.providers.requests.post", boom)
    sender = ResendSender(api_key="re_test", from_addr="me@acme.com", from_name="Me")
    assert sender.send("x@y.com", "s", "b") is False  # swallowed, not raised


def test_build_email_sender_selects_provider(monkeypatch):
    # Disabled / no provider -> NullSender.
    assert isinstance(build_email_sender(Config(raw={})), NullSender)

    dry = Config(raw={"delivery": {"email": {"enabled": True, "provider": "dryrun"}}})
    assert isinstance(build_email_sender(dry), DryRunSender)

    monkeypatch.setenv("RESEND_API_KEY", "re_live")
    live = Config(raw={"delivery": {"email": {
        "enabled": True, "provider": "resend",
        "from_addr": "me@acme.com", "from_name": "Me", "api_key_env": "RESEND_API_KEY",
    }}})
    sender = build_email_sender(live)
    assert isinstance(sender, ResendSender)
    assert sender.api_key == "re_live"

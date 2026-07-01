from leadengine.config import Config
from leadengine.email import (
    DryRunSender,
    GmailSmtpSender,
    NullSender,
    ResendSender,
    build_email_sender,
)


def test_null_sender_is_disabled_and_never_sends():
    sender = NullSender()
    assert sender.enabled is False
    assert sender.send("a@b.com", "subj", "body") is False


def test_dryrun_sender_records_without_sending():
    sender = DryRunSender()
    assert sender.enabled is True
    assert sender.send("a@b.com", "subj", "body") is True
    assert sender.sent == [("a@b.com", "subj", "body", None)]


def test_dryrun_sender_records_html_when_given():
    sender = DryRunSender()
    sender.send("a@b.com", "subj", "text body", "<p>html body</p>")
    assert sender.sent == [("a@b.com", "subj", "text body", "<p>html body</p>")]


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


class _FakeSMTP:
    """Stand-in for smtplib.SMTP_SSL usable as a context manager."""

    def __init__(self, host, port, **kw):
        self.host = host
        self.port = port
        self.logged_in = None
        self.messages = []

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False

    def login(self, user, password):
        self.logged_in = (user, password)

    def send_message(self, msg):
        self.messages.append(msg)


def test_gmail_sender_logs_in_and_sends_via_smtp_ssl(monkeypatch):
    captured = {}

    def fake_ctor(host, port, **kw):
        smtp = _FakeSMTP(host, port, **kw)
        captured["smtp"] = smtp
        return smtp

    monkeypatch.setattr("leadengine.email.providers.smtplib.SMTP_SSL", fake_ctor)

    sender = GmailSmtpSender(
        username="adzius.lech@gmail.com",
        password="app-pw-1234",
        from_name="Andrzej Lech",
    )
    ok = sender.send("andrzej.lech.andrzej@gmail.com", "Scaling your team", "Hi there.")

    assert ok is True
    smtp = captured["smtp"]
    assert smtp.host == "smtp.gmail.com"
    assert smtp.port == 465
    assert smtp.logged_in == ("adzius.lech@gmail.com", "app-pw-1234")
    msg = smtp.messages[0]
    assert msg["From"] == "Andrzej Lech <adzius.lech@gmail.com>"
    assert msg["To"] == "andrzej.lech.andrzej@gmail.com"
    assert msg["Subject"] == "Scaling your team"
    assert msg.get_content().strip() == "Hi there."


def test_gmail_sender_sends_multipart_alternative_when_html_given(monkeypatch):
    captured = {}

    def fake_ctor(host, port, **kw):
        smtp = _FakeSMTP(host, port, **kw)
        captured["smtp"] = smtp
        return smtp

    monkeypatch.setattr("leadengine.email.providers.smtplib.SMTP_SSL", fake_ctor)

    sender = GmailSmtpSender(username="me@gmail.com", password="pw")
    ok = sender.send("x@y.com", "subj", "plain body", "<p>rich body</p>")

    assert ok is True
    msg = captured["smtp"].messages[0]
    assert msg.is_multipart()
    assert msg.get_body(preferencelist=("plain",)).get_content().strip() == "plain body"
    assert msg.get_body(preferencelist=("html",)).get_content().strip() == "<p>rich body</p>"


def test_gmail_sender_returns_false_without_credentials():
    assert GmailSmtpSender(username="", password="").send("a@b.com", "s", "b") is False
    assert GmailSmtpSender(username="me@gmail.com", password="").send("a@b.com", "s", "b") is False


def test_gmail_sender_returns_false_on_error_never_raises(monkeypatch):
    def boom(host, port, **kw):
        raise OSError("smtp unreachable")

    monkeypatch.setattr("leadengine.email.providers.smtplib.SMTP_SSL", boom)
    sender = GmailSmtpSender(username="me@gmail.com", password="pw")
    assert sender.send("x@y.com", "s", "b") is False  # swallowed, not raised


def test_build_email_sender_selects_gmail_provider(monkeypatch):
    monkeypatch.setenv("GMAIL_APP_PASSWORD", "app-pw-live")
    cfg = Config(raw={"delivery": {"email": {
        "enabled": True, "provider": "gmail",
        "username": "adzius.lech@gmail.com", "from_name": "Andrzej Lech",
        "password_env": "GMAIL_APP_PASSWORD",
    }}})
    sender = build_email_sender(cfg)
    assert isinstance(sender, GmailSmtpSender)
    assert sender.username == "adzius.lech@gmail.com"
    assert sender.password == "app-pw-live"


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

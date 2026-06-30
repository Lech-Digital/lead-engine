from leadengine.config import Config


def _cfg(delivery):
    return Config(raw={"delivery": delivery})


def test_slack_webhook_reads_direct_url():
    cfg = _cfg({"slack": {"webhook_url": "https://hooks.slack.com/AAA"}})
    assert cfg.slack_webhook == "https://hooks.slack.com/AAA"


def test_slack_webhook_reads_from_named_env_var(monkeypatch):
    # Per-client channels: the config names an env var; the secret stays out of YAML.
    monkeypatch.setenv("SLACK_WEBHOOK_ACME", "https://hooks.slack.com/ACME")
    cfg = _cfg({"slack": {"webhook_env": "SLACK_WEBHOOK_ACME"}})
    assert cfg.slack_webhook == "https://hooks.slack.com/ACME"


def test_slack_webhook_falls_back_to_legacy_env(monkeypatch):
    monkeypatch.setenv("SLACK_WEBHOOK_URL", "https://hooks.slack.com/LEGACY")
    assert Config(raw={}).slack_webhook == "https://hooks.slack.com/LEGACY"


def test_slack_webhook_disabled_returns_empty(monkeypatch):
    monkeypatch.setenv("SLACK_WEBHOOK_URL", "https://hooks.slack.com/LEGACY")
    cfg = _cfg({"slack": {"enabled": False, "webhook_env": "SLACK_WEBHOOK_URL"}})
    assert cfg.slack_webhook == ""


def test_slack_webhook_empty_when_nothing_configured(monkeypatch):
    monkeypatch.delenv("SLACK_WEBHOOK_URL", raising=False)
    assert Config(raw={}).slack_webhook == ""

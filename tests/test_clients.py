import pytest
import yaml

from leadengine.clients import discover_configs, scaffold_client


def test_discover_configs_finds_yaml_files_sorted(tmp_path):
    (tmp_path / "beta.yaml").write_text("client: {}")
    (tmp_path / "acme.yml").write_text("client: {}")
    (tmp_path / "notes.txt").write_text("ignore me")
    found = discover_configs(str(tmp_path))
    assert [p.name for p in found] == ["acme.yml", "beta.yaml"]


def test_discover_configs_missing_dir_is_empty(tmp_path):
    assert discover_configs(str(tmp_path / "nope")) == []


def test_scaffold_client_creates_isolated_config(tmp_path):
    template = tmp_path / "config.yaml"
    template.write_text(yaml.safe_dump({
        "client": {"name": "Acme Recruiting"},
        "niche": {"description": "..."},
        "output": {"sqlite_path": "leads.db", "csv_path": "leads.csv"},
    }))
    clients_dir = tmp_path / "clients"

    path = scaffold_client("Globex Corp", str(clients_dir), template_path=str(template))

    assert path.exists()
    raw = yaml.safe_load(path.read_text())
    assert raw["client"]["name"] == "Globex Corp"
    # Data must be isolated per client so runs never collide.
    assert raw["output"]["sqlite_path"] == "data/globex_corp.db"
    assert raw["output"]["csv_path"] == "data/globex_corp.csv"
    # A per-client delivery channel is pre-wired via a named env var.
    assert raw["delivery"]["slack"]["webhook_env"] == "SLACK_WEBHOOK_GLOBEX_CORP"


def test_scaffold_client_refuses_to_overwrite(tmp_path):
    template = tmp_path / "config.yaml"
    template.write_text(yaml.safe_dump({"client": {"name": "X"}, "output": {}}))
    clients_dir = tmp_path / "clients"
    scaffold_client("Dup", str(clients_dir), template_path=str(template))
    with pytest.raises(FileExistsError):
        scaffold_client("Dup", str(clients_dir), template_path=str(template))

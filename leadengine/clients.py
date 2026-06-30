from __future__ import annotations

import re
from pathlib import Path

import yaml

# Multi-client productization: one config file per client lives in a clients/ folder.
# Each gets its own SQLite DB, CSV, and Slack channel so an agency can run many niches
# side by side without their data or deliveries ever colliding.


def discover_configs(clients_dir: str) -> list[Path]:
    """Return every client config (*.yaml / *.yml) in the folder, sorted by name."""
    d = Path(clients_dir)
    if not d.is_dir():
        return []
    return sorted(
        (p for p in d.iterdir() if p.suffix in (".yaml", ".yml")),
        key=lambda p: p.name,
    )


def slugify(name: str) -> str:
    """'Globex Corp' -> 'globex_corp' for filenames, data paths, and env-var suffixes."""
    return re.sub(r"_+", "_", re.sub(r"[^a-z0-9]+", "_", name.lower())).strip("_")


def scaffold_client(
    name: str, clients_dir: str, template_path: str = "config.yaml"
) -> Path:
    """Create clients/<slug>.yaml from the template, with isolated data + a delivery
    channel pre-wired to a per-client env var. Refuses to overwrite an existing file."""
    slug = slugify(name)
    out_dir = Path(clients_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    dest = out_dir / f"{slug}.yaml"
    if dest.exists():
        raise FileExistsError(f"{dest} already exists — pick another name or edit it directly")

    raw = yaml.safe_load(Path(template_path).read_text()) or {}
    raw.setdefault("client", {})["name"] = name
    raw["output"] = {
        "sqlite_path": f"data/{slug}.db",
        "csv_path": f"data/{slug}.csv",
    }
    raw.setdefault("delivery", {})["slack"] = {
        "enabled": True,
        "webhook_env": f"SLACK_WEBHOOK_{slug.upper()}",
    }

    dest.write_text(yaml.safe_dump(raw, sort_keys=False))
    return dest

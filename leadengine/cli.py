from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

from .clients import discover_configs, scaffold_client
from .config import Config, LLMSettings
from .email import DryRunSender, build_email_sender
from .pipeline import deliver, run, send_approved
from .storage import Storage


def _load_dotenv(path: str = ".env") -> None:
    """Tiny .env loader so we don't add a dependency just for this."""
    p = Path(path)
    if not p.exists():
        return
    for line in p.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, val = line.partition("=")
        os.environ.setdefault(key.strip(), val.strip())


def _run_all(clients_dir: str) -> int:
    """Run every client config in `clients_dir`, each isolated to its own DB/CSV/channel."""
    configs = discover_configs(clients_dir)
    if not configs:
        print(f"No client configs found in {clients_dir}/ — create one with: leadengine new \"<name>\"")
        return 1
    settings = LLMSettings.from_env()
    print(f"LLM provider: {settings.provider} · running {len(configs)} client(s)")
    for path in configs:
        config = Config.load(path)
        name = config.client.get("name", path.stem)
        try:
            stats = run(config, settings)
            print(f"[{name}] {stats}")
        except Exception as exc:  # one client's failure must not abort the rest
            print(f"[{name}] ERROR: {exc}")
        Storage(config.sqlite_path).export_csv(config.csv_path, config.min_score)
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="leadengine", description="Automated AI Lead Engine")
    parser.add_argument("--config", default="config.yaml", help="Path to client config YAML")
    sub = parser.add_subparsers(dest="cmd", required=True)
    sub.add_parser("run", help="Harvest -> score -> enrich -> store one batch of leads")
    p_export = sub.add_parser("export", help="Export stored leads to CSV")
    p_export.add_argument("--min-score", type=float, default=None)
    sub.add_parser("deliver", help="Push undelivered leads to Slack (idempotent)")
    p_runall = sub.add_parser("run-all", help="Run every client config in a folder")
    p_runall.add_argument("--clients-dir", default="clients")
    p_new = sub.add_parser("new", help="Scaffold a new client config")
    p_new.add_argument("name", help="Client name, e.g. \"Globex Corp\"")
    p_new.add_argument("--clients-dir", default="clients")
    sub.add_parser("queue", help="List leads awaiting your approve/reject decision")
    p_approve = sub.add_parser("approve", help="Approve a queued lead and set its recipient")
    p_approve.add_argument("fingerprint", help="Lead id (a short prefix is fine)")
    p_approve.add_argument("--email", required=True, help="Real recipient address (never guessed)")
    p_reject = sub.add_parser("reject", help="Reject a queued lead")
    p_reject.add_argument("fingerprint", help="Lead id (a short prefix is fine)")
    p_send = sub.add_parser("send", help="Send all approved leads via the email provider")
    p_send.add_argument("--dry-run", action="store_true", help="Print sends without calling the API")
    p_contacted = sub.add_parser("contacted", help="List every company already emailed (never re-contacted)")
    p_contacted.add_argument("--out", default=None, help="Also write the list to this markdown file")

    args = parser.parse_args(argv)
    _load_dotenv()

    # These commands operate across clients, so they don't load a single --config.
    if args.cmd == "run-all":
        return _run_all(args.clients_dir)
    if args.cmd == "new":
        dest = scaffold_client(args.name, args.clients_dir, args.config)
        print(f"Created {dest} — edit its niche, then: leadengine --config {dest} run")
        return 0

    config = Config.load(args.config)

    if args.cmd == "run":
        settings = LLMSettings.from_env()
        print(f"LLM provider: {settings.provider}"
              f"{' (no key -> heuristic fallback)' if not settings.api_key else ''}")
        stats = run(config, settings)
        print(stats)
        n = Storage(config.sqlite_path).export_csv(config.csv_path, config.min_score)
        print(f"Exported {n} leads -> {config.csv_path}")
        return 0

    if args.cmd == "deliver":
        if not config.slack_webhook:
            print("No Slack webhook configured for this client — nothing delivered")
            return 0
        n = deliver(config)
        print(f"Delivered {n} leads to Slack")
        return 0

    if args.cmd == "export":
        min_score = args.min_score if args.min_score is not None else config.min_score
        n = Storage(config.sqlite_path).export_csv(config.csv_path, min_score)
        print(f"Exported {n} leads (score >= {min_score}) -> {config.csv_path}")
        return 0

    if args.cmd == "queue":
        return _queue(config)
    if args.cmd in ("approve", "reject"):
        return _decide(config, args)
    if args.cmd == "send":
        return _send(config, args.dry_run)
    if args.cmd == "contacted":
        return _contacted(config, args.out)

    return 1


def _queue(config: Config) -> int:
    storage = Storage(config.sqlite_path)
    pending = storage.pending_review(config.min_score)
    if not pending:
        print("Review queue is empty.")
        return 0
    print(f"{len(pending)} lead(s) awaiting review (approve <id> --email <addr> | reject <id>):\n")
    for lead in pending:
        print(f"  id {lead.fingerprint[:8]}  ·  {lead.fit}  ·  score {lead.score:.2f}  ·  {lead.company or lead.author}")
        print(f"     subject: {lead.subject}")
        if lead.contact:
            # The research links to find the real recipient address.
            for line in lead.contact.splitlines():
                print(f"     {line}")
        print()
    return 0


def _decide(config: Config, args) -> int:
    storage = Storage(config.sqlite_path)
    fp = storage.resolve_fingerprint(args.fingerprint)
    if fp is None:
        print(f"No single lead matches id '{args.fingerprint}' (missing or ambiguous).")
        return 1
    if args.cmd == "reject":
        storage.set_status(fp, "rejected")
        print(f"Rejected {fp[:8]}.")
        return 0
    if "@" not in args.email:
        print(f"'{args.email}' is not a valid email address.")
        return 1
    storage.set_status(fp, "approved", recipient_email=args.email)
    print(f"Approved {fp[:8]} -> {args.email}. Run `leadengine send` to deliver.")
    return 0


def _send(config: Config, dry_run: bool) -> int:
    sender = DryRunSender() if dry_run else build_email_sender(config)
    if not sender.enabled:
        print("No email provider configured (set delivery.email in config). Nothing sent.")
        return 0
    # preview=dry_run: a dry run hands messages to the sender but never stamps them sent.
    sent, failed, skipped = send_approved(config, sender=sender, preview=dry_run)
    label = "DRY-RUN: would send" if dry_run else "Sent"
    note = (f", {failed} failed (still queued)" if failed else "") + (
        f", {skipped} skipped (already contacted)" if skipped else "")
    print(f"{label} {sent} email(s){note}")
    if dry_run:
        for to, subject, *_ in getattr(sender, "sent", []):
            print(f"  -> {to}  ·  {subject}")
    return 0


def _contacted(config: Config, out: str | None) -> int:
    storage = Storage(config.sqlite_path)
    leads = storage.sent()
    name = config.client.get("name", "Coldstart")
    if not leads:
        print("No companies contacted yet.")
        return 0
    print(f"{len(leads)} compan{'y' if len(leads) == 1 else 'ies'} contacted:\n")
    for lead in leads:
        when = (lead.sent_at or "")[:10]
        print(f"  {when}  {lead.company or lead.author:<24} {lead.recipient_email}")
    if out:
        lines = [f"# {name} — companies already contacted", "",
                 "Auto-generated from the sent history (`leadengine contacted`). "
                 "The engine will not email any company on this list again.", "",
                 "| Date | Company | Sent to | Subject |", "| --- | --- | --- | --- |"]
        for lead in leads:
            when = (lead.sent_at or "")[:10]
            subj = (lead.subject or "").replace("|", "\\|")
            lines.append(f"| {when} | {lead.company or lead.author} | {lead.recipient_email} | {subj} |")
        Path(out).parent.mkdir(parents=True, exist_ok=True)
        Path(out).write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"\nWrote {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

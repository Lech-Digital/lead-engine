#!/usr/bin/env bash
#
# Daily Lead Engine run, scheduled via a user LaunchAgent at 11:00 (no admin rights
# needed — see scripts/install_schedule.sh).
# Bounded: runs through Friday 2026-07-03 (inclusive), then no-ops and unloads/removes
# its own LaunchAgent so it cleans up after itself.

PROJECT="/Users/andrzej.lech/Code/private/lead-engine"
END="20260703"   # last day to run, inclusive (YYYYMMDD)
PLIST="$HOME/Library/LaunchAgents/com.leadengine.daily.plist"

cd "$PROJECT" || exit 1
mkdir -p logs
TODAY="$(date +%Y%m%d)"

if [ "$TODAY" -gt "$END" ]; then
  echo "$(date '+%F %T') past end date ($END) — stopping and removing the LaunchAgent." >> logs/daily.log
  launchctl unload "$PLIST" 2>/dev/null || true
  rm -f "$PLIST"
  exit 0
fi

{
  echo ""
  echo "===== $(date '+%F %T') · daily run ====="
  # shellcheck disable=SC1091
  source .venv/bin/activate
  python -m leadengine.cli run
  echo "----- run complete -----"
} >> logs/daily.log 2>&1

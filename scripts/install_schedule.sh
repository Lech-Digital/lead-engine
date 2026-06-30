#!/usr/bin/env bash
#
# Install the daily 11:00 Lead Engine schedule as a USER LaunchAgent.
# Works without admin rights (unlike cron, which needs Full Disk Access granted to the
# system cron daemon). Idempotent: re-running reloads the agent.
#
set -euo pipefail

PROJECT="/Users/andrzej.lech/Code/private/lead-engine"
LABEL="com.leadengine.daily"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

mkdir -p "$HOME/Library/LaunchAgents" "$PROJECT/logs"

cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>$LABEL</string>
  <key>ProgramArguments</key>
  <array>
    <string>$PROJECT/scripts/run_daily.sh</string>
  </array>
  <key>StartCalendarInterval</key>
  <dict>
    <key>Hour</key><integer>11</integer>
    <key>Minute</key><integer>0</integer>
  </dict>
  <key>StandardOutPath</key>
  <string>$PROJECT/logs/launchd.out.log</string>
  <key>StandardErrorPath</key>
  <string>$PROJECT/logs/launchd.err.log</string>
</dict>
</plist>
EOF

launchctl unload "$PLIST" 2>/dev/null || true
launchctl load -w "$PLIST"

echo "Installed LaunchAgent: $PLIST"
echo "Scheduled: every day at 11:00 until 2026-07-03 (inclusive), then self-removes."
launchctl list | grep -i leadengine && echo "(agent is loaded)" || echo "WARNING: agent not found in launchctl list"
echo
echo "Remove early:  scripts/uninstall_schedule.sh"

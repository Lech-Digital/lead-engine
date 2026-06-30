#!/usr/bin/env bash
#
# Remove the daily Lead Engine LaunchAgent.
#
PLIST="$HOME/Library/LaunchAgents/com.leadengine.daily.plist"
launchctl unload "$PLIST" 2>/dev/null || true
rm -f "$PLIST"
echo "Removed the daily schedule."

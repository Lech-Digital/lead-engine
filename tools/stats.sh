#!/usr/bin/env bash
# Coldstart analytics report — pulls visitor + funnel numbers from the
# self-hosted Umami instance and prints a conversion summary.
#
# Usage:   ./tools/stats.sh [days]     (default: 7)
# Needs:   ~/.coldstart-umami with "user: <name>" and "password: <pw>" lines
set -euo pipefail

DAYS="${1:-7}"
BASE="${UMAMI_URL:-https://stats.getcoldstart.nl}"
CREDS="$HOME/.coldstart-umami"

[[ -f "$CREDS" ]] || { echo "Missing $CREDS" >&2; exit 1; }
USER=$(awk -F': ' '/^user:/ {print $2}' "$CREDS")
PASS=$(awk -F': ' '/^password:/ {print $2}' "$CREDS")

NOW_MS=$(( $(date +%s) * 1000 ))
START_MS=$(( NOW_MS - DAYS * 86400000 ))

TOKEN=$(curl -sf -X POST "$BASE/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"username\":\"$USER\",\"password\":\"$PASS\"}" | jq -r .token)
[[ -n "$TOKEN" && "$TOKEN" != "null" ]] || { echo "Umami login failed" >&2; exit 1; }

api() { curl -sf -H "Authorization: Bearer $TOKEN" "$BASE/api$1"; }

SITE_ID=$(api "/websites" | jq -r '.data[] | select(.domain == "getcoldstart.nl") | .id' | head -1)
[[ -n "$SITE_ID" ]] || { echo "Website getcoldstart.nl not found in Umami" >&2; exit 1; }

RANGE="startAt=$START_MS&endAt=$NOW_MS"
STATS=$(api "/websites/$SITE_ID/stats?$RANGE")
EVENTS=$(api "/websites/$SITE_ID/metrics?$RANGE&type=event")
PAGES=$(api "/websites/$SITE_ID/metrics?$RANGE&type=path")
REFERRERS=$(api "/websites/$SITE_ID/metrics?$RANGE&type=referrer")

num()   { jq -r "$1 // 0" <<<"$2"; }
event() { jq -r --arg n "$1" '[.[] | select(.x == $n) | .y] | add // 0' <<<"$EVENTS"; }
pct()   { if (( $2 > 0 )); then awk "BEGIN {printf \"%.1f%%\", 100 * $1 / $2}"; else echo "—"; fi; }

VISITORS=$(num '.visitors' "$STATS")
VISITS=$(num '.visits' "$STATS")
PAGEVIEWS=$(num '.pageviews' "$STATS")
BOUNCES=$(num '.bounces' "$STATS")
TOTALTIME=$(num '.totaltime' "$STATS")

CTA=$(( $(event cta-hero) + $(event cta-nav) ))
SUBMITS=$(event sample-request-submit)
SUCCESS=$(event sample-request-success)

AVG_TIME="—"
(( VISITS > 0 )) && AVG_TIME=$(awk "BEGIN {printf \"%dm %02ds\", $TOTALTIME / $VISITS / 60, ($TOTALTIME / $VISITS) % 60}")

echo "☀️  Coldstart — last $DAYS day(s)  ($(date -r $((START_MS / 1000)) '+%d %b') → $(date '+%d %b'))"
echo
echo "  Traffic"
printf "    %-28s %s\n" "Unique visitors" "$VISITORS"
printf "    %-28s %s\n" "Visits" "$VISITS"
printf "    %-28s %s\n" "Page views" "$PAGEVIEWS"
printf "    %-28s %s\n" "Bounce rate" "$(pct "$BOUNCES" "$VISITS")"
printf "    %-28s %s\n" "Avg. visit time" "$AVG_TIME"
echo
echo "  Funnel                       count   of visitors"
printf "    %-26s %5s   %s\n" "Get-leads CTA clicked" "$CTA" "$(pct "$CTA" "$VISITORS")"
printf "    %-26s %5s   %s\n" "Sample form submitted" "$SUBMITS" "$(pct "$SUBMITS" "$VISITORS")"
printf "    %-26s %5s   %s\n" "Request accepted ✓" "$SUCCESS" "$(pct "$SUCCESS" "$VISITORS")"
echo
printf "  ➤ Conversion (visitor → sample request): %s\n" "$(pct "$SUCCESS" "$VISITORS")"
echo
echo "  Top pages"
jq -r '.[:5][] | "    \(.y)\t\(.x)"' <<<"$PAGES"
echo
echo "  Top referrers"
REF=$(jq -r '[.[] | select(.x != "")] | .[:5][] | "    \(.y)\t\(.x)"' <<<"$REFERRERS")
echo "${REF:-    (only direct traffic so far)}"

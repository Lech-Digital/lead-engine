#!/usr/bin/env bash
# Keeps Docker's build cache from filling this box's disk.
#
# Coldstart and PurePrep both build their images here, and BuildKit's cache grew
# to 13 GB unattended (50% of a 38 GB disk) before anyone noticed. A full disk
# takes down Postgres and both Kestrel apps at once, so this runs weekly:
#   - normally: drop build cache not used in the last week
#   - under disk pressure: drop all of it (costs a slow next build, nothing else)
# Never touches tagged images, volumes, or running containers.
set -euo pipefail

THRESHOLD=70   # percent of / in use that escalates to an aggressive prune

used=$(df --output=pcent / | tail -1 | tr -dc '0-9')
before=$(df -h --output=avail / | tail -1 | tr -d ' ')

if [ "$used" -ge "$THRESHOLD" ]; then
  echo "disk at ${used}% (>= ${THRESHOLD}%) — pruning ALL build cache"
  docker builder prune -af
else
  echo "disk at ${used}% — pruning build cache unused for 168h"
  docker builder prune -f --filter until=168h
fi

# Dangling (untagged) layers left behind by rebuilds. Never removes an image
# that a container is using or that still carries a tag.
docker image prune -f

after=$(df -h --output=avail / | tail -1 | tr -d ' ')
echo "avail before=${before} after=${after}"

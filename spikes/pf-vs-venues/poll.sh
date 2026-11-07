#!/usr/bin/env bash
# Spike d41391d0: poll xivpf.com every 10 minutes until END (epoch seconds), and snapshot
# FFXIV Venues once at the start and once at the end. Venue schedules and overrides cover
# the whole window, so the end snapshot only catches edits made during the night.
# Usage: poll.sh <out-dir> <end-epoch>
set -uo pipefail
out=${1:?out dir}; end=${2:?end epoch}
ua="jaunt-research/0.1 (one-off listing overlap study, 10-minute polls)"
mkdir -p "$out/pf" "$out/venues"
fetch() { # url dest
  local tmp="$2.part"
  if curl -sS --fail --compressed --max-time 90 -A "$ua" "$1" | gzip -9 > "$tmp"; then
    mv "$tmp" "$2"; echo "$(date -u +%FT%TZ) ok $2 $(stat -c %s "$2")"
  else
    rm -f "$tmp"; echo "$(date -u +%FT%TZ) FAIL $1"
  fi
}
venues() { fetch https://api.ffxivvenues.com/v1.0/venue "$out/venues/$(date -u +%Y%m%dT%H%M%SZ).json.gz"; }
venues
while [ "$(date +%s)" -lt "$end" ]; do
  fetch https://xivpf.com/api/listings "$out/pf/$(date -u +%Y%m%dT%H%M%SZ).json.gz"
  sleep 600
done
venues
echo "$(date -u +%FT%TZ) finished"

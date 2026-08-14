#!/bin/bash
#
# self-write-suppression-check.sh — exercises the "your own append is not traffic" rule end to end.
#
# The watcher lives as a bash script inside five markdown role commands, where no C# test can reach
# it. This runs the REAL channel-append.sh against a REAL temp channel and drives a copy of the
# watcher's decision, so the rule that decides whether a session is woken is checked by something
# other than reading it.
#
# The decision function below is a TRANSCRIPTION of `self_write_suppresses` in the role commands.
# Change one and change the other; a copy that drifts is worse than no check, because it certifies
# a rule nobody is running.
#
# IT REFUSES TO RUN IF IT CANNOT FIND THE HELPER. A harness that cannot find what it tests reports
# passes about code it never executed — this repo has already had one do exactly that, 16 confident
# failures about hooks it never invoked.
set -u

HELPER="$(dirname "$0")/channel-append.sh"

if [ ! -f "$HELPER" ]; then
  echo "self-write-suppression-check.sh: cannot find channel-append.sh next to this script ($HELPER)." >&2
  echo "REFUSING TO RUN — a pass from here would be about nothing." >&2
  exit 2
fi

WORK="$(mktemp -d)" || { echo "cannot create a temp directory" >&2; exit 2; }
trap 'rm -rf "$WORK"' EXIT

ch="$WORK/channel.md"
printf '# header\n' > "$ch"
printf 'body\n' > "$WORK/body.txt"

# ---- transcription of the watcher, from the role commands --------------------------------------
read_fp() {
  FP=""; FP_ERR=""
  local size hash
  if ! size="$(wc -c < "$ch" 2>/dev/null)" || [ -z "$size" ]; then FP_ERR="wc -c"; return 1; fi
  if ! hash="$(md5sum "$ch" 2>/dev/null)"  || [ -z "$hash" ]; then FP_ERR="md5sum"; return 1; fi
  size="${size// /}"
  FP="$size ${hash%% *}"
}

self_write_suppresses() {
  local record start after
  record="$ch.self-write.solo"
  [ -f "$record" ] || return 1
  start="$(grep -m1 '^start=' "$record" 2>/dev/null | cut -d= -f2- | tr -d ' ')"
  after="$(grep -m1 '^after=' "$record" 2>/dev/null | cut -d= -f2-)"
  [ -n "$start" ] && [ -n "$after" ] || return 1
  [ "$after" = "$FP" ] || return 1
  [ "$start" -le "${prev%% *}" ] 2>/dev/null
}
# ------------------------------------------------------------------------------------------------

RESULT=""
FAILURES=0

# Runs in the CURRENT shell, never a command substitution: `prev` must advance exactly as it does in
# the monitor loop, and a subshell would silently freeze it at its first value — which makes every
# case pass or fail for the wrong reason.
poll() {
  if read_fp; then
    if [ -n "$prev" ] && [ "$FP" != "$prev" ] && ! self_write_suppresses; then RESULT="FIRE"; else RESULT="quiet"; fi
    prev="$FP"
  else
    RESULT="READ-FAILED"
  fi
}

mine()    { bash "$HELPER" --channel "$ch" --author solo --subject "$1" --body-file "$WORK/body.txt" > /dev/null; }
foreign() { printf '\n## [99] FROM owner — 2026-01-01 00:00 — %s\n\nhi\n' "$1" >> "$ch"; }

check() {
  if [ "$RESULT" = "$2" ]; then
    echo "PASS  $1"
  else
    echo "FAIL  $1 — got $RESULT, wanted $2"
    FAILURES=$((FAILURES + 1))
  fi
}

read_fp || { echo "cannot fingerprint the temp channel — REFUSING TO REPORT" >&2; exit 2; }
prev="$FP"

mine one;                poll; check "our own append does not wake us" quiet
foreign a;               poll; check "somebody else's append wakes us" FIRE
mine two;                poll; check "our append after a foreign one we already saw" quiet
mine three;              poll; check "our append again" quiet
mine four; mine five;    poll; check "two of our appends between polls" quiet
foreign b; mine six;     poll; check "THEY wrote, then we appended — the wake must survive" FIRE
mine seven;              poll; check "our append right after that" quiet
foreign c;               poll; check "foreign append, nothing of ours" FIRE

rm -f "$ch.self-write.solo"
foreign d;               poll; check "no record at all — never suppress" FIRE
printf 'start=0\nafter=1 deadbeef\n' > "$ch.self-write.solo"
foreign e;               poll; check "a record that does not match the file — never suppress" FIRE

if [ "$FAILURES" -gt 0 ]; then
  echo "$FAILURES case(s) FAILED"
  exit 1
fi

echo "all cases passed"

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

# ---- the SUPERVISOR's variant: many channels, one watcher ---------------------------------------
#
# Transcription of `read_fp` and `foreign_change` from supervisor.md. It is the case that matters
# most — the supervisor writes to every channel it watches, so most changes are its own — and the
# case where a mistake is worst: excusing one spoke's traffic because another spoke's change was
# ours would lose an implementer's report.
echo
echo "-- supervisor (many channels) --"

sup="$WORK/orch"
mkdir -p "$sup/imp-1" "$sup/imp-2"
printf '# owner\n' > "$sup/owner-channel.md"
printf '# imp-1\n' > "$sup/imp-1/channel.md"
printf '# imp-2\n' > "$sup/imp-2/channel.md"

sup_read_fp() {
  FP=""; FP_ERR=""
  local files file size hash out=""
  files=( "$sup"/imp-*/channel.md "$sup/owner-channel.md" )
  for file in "${files[@]}"; do
    if ! size="$(wc -c < "$file" 2>/dev/null)" || [ -z "$size" ]; then FP_ERR="wc -c on $file"; return 1; fi
    if ! hash="$(md5sum "$file" 2>/dev/null)"  || [ -z "$hash" ]; then FP_ERR="md5sum on $file"; return 1; fi
    size="${size// /}"
    out="$out$file|$size ${hash%% *}"$'\n'
  done
  FP="$out"
}

foreign_change() {
  local line file now before record start after
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    file="${line%%|*}"; now="${line#*|}"

    before="$(printf '%s' "$prev" | grep -F -m1 "$file|")" || before=""
    before="${before#*|}"

    [ "$now" = "$before" ] && continue
    [ -n "$before" ] || return 0

    record="$file.self-write.supervisor"
    [ -f "$record" ] || return 0
    start="$(grep -m1 '^start=' "$record" 2>/dev/null | cut -d= -f2- | tr -d ' ')"
    after="$(grep -m1 '^after=' "$record" 2>/dev/null | cut -d= -f2-)"
    [ -n "$start" ] && [ -n "$after" ] || return 0
    [ "$after" = "$now" ] || return 0
    [ "$start" -le "${before%% *}" ] 2>/dev/null || return 0
  done <<< "$FP"

  return 1
}

sup_poll() {
  if sup_read_fp; then
    if [ -n "$prev" ] && [ "$FP" != "$prev" ] && foreign_change; then RESULT="FIRE"; else RESULT="quiet"; fi
    prev="$FP"
  else
    RESULT="READ-FAILED"
  fi
}

sup_mine()    { bash "$HELPER" --channel "$1" --author supervisor --subject "$2" --body-file "$WORK/body.txt" > /dev/null; }
sup_foreign() { printf '\n## [99] FROM implementer — 2026-01-01 00:00 — %s\n\nhi\n' "$2" >> "$1"; }

sup_read_fp || { echo "cannot fingerprint the temp channels — REFUSING TO REPORT" >&2; exit 2; }
prev="$FP"

sup_mine "$sup/imp-1/channel.md" brief;      sup_poll; check "our brief to imp-1 does not wake us" quiet
sup_foreign "$sup/imp-1/channel.md" report;  sup_poll; check "imp-1's report wakes us" FIRE
sup_mine "$sup/imp-2/channel.md" brief;      sup_poll; check "our brief to imp-2 does not wake us" quiet

# THE CASE THAT MUST NOT BE EXCUSED BY A SIBLING: imp-1 reports while we write to imp-2, in the same
# gap. A watcher that judged the tick as a whole would call it ours and lose the report.
sup_foreign "$sup/imp-1/channel.md" report2
sup_mine "$sup/imp-2/channel.md" brief2;     sup_poll; check "imp-1 reports while we write to imp-2" FIRE

sup_foreign "$sup/owner-channel.md" owner
sup_mine "$sup/owner-channel.md" reply;      sup_poll; check "the owner writes, then we reply" FIRE
sup_mine "$sup/owner-channel.md" more;       sup_poll; check "our own second entry to the owner" quiet

mkdir -p "$sup/imp-3"; printf '# imp-3\n' > "$sup/imp-3/channel.md"
sup_poll; check "a channel we have never seen before" FIRE

if [ "$FAILURES" -gt 0 ]; then
  echo "$FAILURES case(s) FAILED"
  exit 1
fi

echo "all cases passed"

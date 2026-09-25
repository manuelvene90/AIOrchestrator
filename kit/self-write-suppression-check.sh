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

HELPER="$(dirname "$0")/bin/channel-append.sh"

if [ ! -f "$HELPER" ]; then
  echo "self-write-suppression-check.sh: cannot find bin/channel-append.sh beside this script ($HELPER)." >&2
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

# ---- the SOLO's SIBLING half: another solo's outbox (spec 2026-09-23 §5.3) -----------------------
#
# Transcription of `sib_line_of`, `read_sib_fp` and `sib_changed` from solo/reference/watcher.md —
# and, unlike the transcriptions above, CHECKED against that file: the block below must appear in it
# verbatim, so this copy cannot drift from the one sessions arm.
#
# THE CASE IT EXISTS FOR. Every sibling signs `FROM solo`, so solo A's append to ITS OUTBOX leaves
# `sibling-outbox.md.self-write.solo` beside it — the very record name solo B's OWNER-channel rule
# reads. If anyone ever "helpfully" gave the sibling half the owner half's suppression, B would sleep
# through every sibling. The sibling half therefore has none, and needs none: a solo's own outbox is
# never listed in its own .siblings. And the owner half must still suppress A's own append to its
# owner channel, exactly as before.
echo
echo "-- solo siblings (outboxes) --"

WATCHER_MD="$(dirname "$0")/skills/solo/reference/watcher.md"

if [ ! -f "$WATCHER_MD" ]; then
  echo "self-write-suppression-check.sh: cannot find skills/solo/reference/watcher.md beside this script ($WATCHER_MD)." >&2
  echo "REFUSING TO RUN — the sibling transcription would be checked against nothing." >&2
  exit 2
fi

SIB_TRANSCRIPT="$(cat <<'TRANSCRIPT'
# Prints the state recorded for outbox $2 in fingerprint set $1 ("<size> <hash>", or "absent"), or
# returns non-zero when the set has no line for it — a path never seen.
sib_line_of() {
  local line
  while IFS= read -r line; do
    if [ -n "$line" ] && [ "${line%%|*}" = "$2" ]; then printf '%s' "${line#*|}"; return 0; fi
  done <<< "$1"
  return 1
}

# Sets SIB_LIST (the lines of .siblings), SIB_FP (one "<path>|<size> <hash>" line per outbox) and
# SIB_ERR (what failed, or empty). An outbox that cannot be read keeps its PREVIOUS line: unknown,
# never a change. One that does not exist yet, or is still EMPTY (the taught `touch` before a first
# append), is "absent" - so its first entry IS a change, and the touch alone is not.
read_sib_fp() {
  SIB_LIST=""; SIB_FP=""; SIB_ERR=""
  [ -f "$sibs" ] || return 0
  if ! SIB_LIST="$(cat "$sibs" 2>/dev/null)"; then
    SIB_ERR="reading .siblings"; SIB_LIST=""; SIB_FP="$sib_prev"; return 0
  fi
  local id path rest size hash kept
  while IFS=$'\t' read -r id path rest; do
    [ -n "$id" ] && [ -n "$path" ] || continue
    if [ ! -s "$path" ]; then SIB_FP="$SIB_FP$path|absent"$'\n'; continue; fi
    # Hashed from stdin, not from a file argument: md5sum escapes its whole output line when the
    # path holds a backslash, which every Windows path in .siblings does.
    if ! size="$(wc -c < "$path" 2>/dev/null)" || [ -z "$size" ] \
       || ! hash="$(md5sum < "$path" 2>/dev/null || md5 -q < "$path" 2>/dev/null)" || [ -z "$hash" ]; then
      SIB_ERR="reading the outbox of $id"
      if kept="$(sib_line_of "$sib_prev" "$path")"; then SIB_FP="$SIB_FP$path|$kept"$'\n'; fi
      continue
    fi
    size="${size// /}"
    SIB_FP="$SIB_FP$path|$size ${hash%% *}"$'\n'
  done <<< "$SIB_LIST"
}

# Did outbox $1 change since the last read? Never for a path seen for the FIRST time (a sibling born
# mid-loop is baseline), never when this read could not see it. NO self-write test, on purpose.
sib_changed() {
  local before now
  before="$(sib_line_of "$sib_prev" "$1")" || return 1
  now="$(sib_line_of "$SIB_FP" "$1")" || return 1
  [ "$now" != "$before" ]
}
TRANSCRIPT
)"
SIB_TRANSCRIPT="${SIB_TRANSCRIPT//$'\r'/}"

SHIPPED_WATCHER="$(cat "$WATCHER_MD")"
SHIPPED_WATCHER="${SHIPPED_WATCHER//$'\r'/}"

case "$SHIPPED_WATCHER" in
  *"$SIB_TRANSCRIPT"*) echo "PASS  the sibling transcription is the text solo/reference/watcher.md ships" ;;
  *) echo "FAIL  the sibling transcription is not in solo/reference/watcher.md — the copy drifted, or the half is not shipped"
     FAILURES=$((FAILURES + 1)) ;;
esac

eval "$SIB_TRANSCRIPT"

sib_a="$WORK/solo-a"; sib_b="$WORK/solo-b"
mkdir -p "$sib_a" "$sib_b"
printf '# owner of A\n' > "$sib_a/owner-channel.md"
printf '# outbox of A\n' > "$sib_a/sibling-outbox.md"
printf 'A\t%s\tlive\tAI-Orch · settings work\n' "$sib_a/sibling-outbox.md" > "$sib_b/.siblings"

sibs="$sib_b/.siblings"   # B's watcher
sib_prev=""
read_sib_fp; sib_prev="$SIB_FP"
[ -n "$sib_prev" ] && [ -z "$SIB_ERR" ] || { echo "cannot fingerprint A's outbox from B's side — REFUSING TO REPORT" >&2; exit 2; }

sib_poll() {
  read_sib_fp
  if sib_changed "$sib_a/sibling-outbox.md"; then RESULT="FIRE"; else RESULT="quiet"; fi
  sib_prev="$SIB_FP"
}

AIORCH_ROLE=solo bash "$HELPER" --channel "$sib_a/sibling-outbox.md" --author solo --subject "ASK which DTO" --body-file "$WORK/body.txt" > /dev/null \
  || { echo "the helper refused A's outbox append — REFUSING TO REPORT" >&2; exit 2; }

if [ -f "$sib_a/sibling-outbox.md.self-write.solo" ]; then
  echo "PASS  A's outbox append left a self-write record signed solo (the trap this case is about)"
else
  echo "FAIL  A's outbox append left no self-write record, so the case below proves nothing"
  FAILURES=$((FAILURES + 1))
fi

sib_poll; check "A appends to its outbox — B's sibling half fires despite the solo self-write record" FIRE
sib_poll; check "nothing more from A — B stays quiet" quiet

# A's OWNER half, on A's own owner channel: its own append must still be suppressed.
ch="$sib_a/owner-channel.md"
read_fp || { echo "cannot fingerprint A's owner channel — REFUSING TO REPORT" >&2; exit 2; }
prev="$FP"
AIORCH_ROLE=solo bash "$HELPER" --channel "$ch" --author solo --subject "status" --body-file "$WORK/body.txt" > /dev/null
poll; check "A's own append to its owner channel is still suppressed by its owner half" quiet

# ---- THE TAUGHT CREATE STEP, RUN FOR REAL (review of f2a6b02, C1) --------------------------------
#
# Nothing in the app creates `sibling-outbox.md`, and the helper refuses a channel that does not exist
# (exit 2) — so the recipe's HANDOVER, and a newborn child's first ASK, died on first use. The skill
# now teaches `touch "$ORCH/sibling-outbox.md"` before the first append. This takes that line OUT OF
# THE SHIPPED PROSE (both places it is taught), runs it, and then runs the real helper: a copy typed
# here would certify a command no session is told to run.
echo
echo "-- solo siblings (the outbox does not exist yet) --"

SOLO_SKILL="$(dirname "$0")/skills/solo/SKILL.md"
SIBLINGS_MD="$(dirname "$0")/skills/solo/reference/siblings.md"
TAUGHT_TOUCH='touch "$ORCH/sibling-outbox.md"'

for taught_in in "$SOLO_SKILL" "$SIBLINGS_MD"; do
  if [ ! -f "$taught_in" ]; then
    echo "self-write-suppression-check.sh: cannot find $taught_in — REFUSING TO RUN" >&2
    exit 2
  fi
  if grep -qF -- "$TAUGHT_TOUCH" "$taught_in"; then
    echo "PASS  $(basename "$taught_in") teaches the create step: $TAUGHT_TOUCH"
  else
    echo "FAIL  $(basename "$taught_in") does not teach $TAUGHT_TOUCH — a first append to a new outbox is refused"
    FAILURES=$((FAILURES + 1))
  fi
done

ORCH="$WORK/newborn"
mkdir -p "$ORCH"

# The control: WHY the step exists. Without it the real helper refuses, nothing is written.
if AIORCH_ROLE=solo bash "$HELPER" --channel "$ORCH/sibling-outbox.md" --author solo --subject "HANDOVER — the limits job" --body-file "$WORK/body.txt" > /dev/null 2>&1; then
  RESULT="accepted"
else
  RESULT="refused"
fi
check "control: the helper refuses an outbox that does not exist yet" refused

eval "$TAUGHT_TOUCH"
printf 'keep me\n' > "$WORK/sentinel.md"; ORCH_SAVE="$ORCH"; ORCH="$WORK"
cp "$WORK/sentinel.md" "$WORK/sibling-outbox.md"; eval "$TAUGHT_TOUCH"; ORCH="$ORCH_SAVE"
if cmp -s "$WORK/sentinel.md" "$WORK/sibling-outbox.md"; then RESULT="kept"; else RESULT="truncated"; fi
check "the taught step never truncates an outbox that already has entries" kept

if printed="$(AIORCH_ROLE=solo bash "$HELPER" --channel "$ORCH/sibling-outbox.md" --author solo --subject "HANDOVER — the limits job" --body-file "$WORK/body.txt" 2>/dev/null)"; then
  RESULT="accepted $printed"
else
  RESULT="refused"
fi
check "after the taught step the real helper appends the HANDOVER and prints its bare index" "accepted 1"

if [ "$FAILURES" -gt 0 ]; then
  echo "$FAILURES case(s) FAILED"
  exit 1
fi

echo "all cases passed"

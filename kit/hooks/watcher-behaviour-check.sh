#!/usr/bin/env bash
# AI Orchestrator — behaviour check for the WATCHER LOOP shipped inside the role commands.
#
#   bash kit/hooks/watcher-behaviour-check.sh
#
# WHY THIS EXISTS. On 2026-08-14 the watcher was found to answer "the file changed" when what had
# actually happened was "I could not read the file". Both commands' exit statuses were discarded, so
# a fork that failed produced an empty or partial fingerprint, which compared unequal to the real one
# and fired — one failed read, two phantom wakes, and nothing anywhere recording that a read failed.
# It taxed every session in the orchestration for a day, and it did so hardest on a machine that was
# out of memory, which is when real traffic matters most.
#
# THIS RUNS THE SHIPPED TEXT. It extracts the bash block out of each role command and executes it —
# there is no second copy of the loop here to drift from the one agents are told to arm. Only two
# lines are rewritten, both loop plumbing, and the script REFUSES TO RUN if either rewrite fails to
# apply:
#
#     while true; do   ->   while read -r step; do      (drive it, instead of forever)
#     sleep 5          ->   apply_step "$step"          (a step instead of a wall-clock wait)
#
# so the body under test — read_fp, the failure branch, prev handling, the marker, the blind alarm —
# is byte-for-byte the shipped one. Steps are fed on stdin, so there is no timing in this file and
# nothing here is flaky.
#
# REFUSING TO RUN IS THE POINT. A harness that cannot find what it tests must fail loudly rather than
# report that the thing it never executed is fine: hook-behaviour-check.sh once returned 16 confident
# failures about code it had not run, because nothing-is-ALLOW. Every discovery step below is fatal.

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILLS_DIR="$(cd "$SCRIPT_DIR/../skills" 2>/dev/null && pwd || true)"

FAILURES=0
CHECKS=0

die() { printf 'REFUSING TO RUN: %s\n' "$1" >&2; exit 2; }

check() {
  local what="$1" expected="$2" actual="$3"
  CHECKS=$((CHECKS + 1))
  if [ "$expected" = "$actual" ]; then
    printf '  ok   %s\n' "$what"
  else
    printf '  FAIL %s — expected [%s], got [%s]\n' "$what" "$expected" "$actual"
    FAILURES=$((FAILURES + 1))
  fi
}

[ -n "$SKILLS_DIR" ] && [ -d "$SKILLS_DIR" ] \
  || die "no kit/skills beside this script (looked next to $SCRIPT_DIR) — the role protocols are the subject, and without them every check below would pass by finding nothing"

# ---------------------------------------------------------------------------------------------
# Per-role facts. Only paths differ: which channel file the loop watches, which folder receives the
# marker, what the wake line says, and the reason the marker gives for a failed read.
#
#   role | channel path under the fake HOME | orchestration folder | wake phrase | marker reason
#
# THE REASON IS PER ROLE, EXACT, and never a loose match (decision 20: a check with two routes to green
# pins neither). One-file watchers say `md5sum failed`. The SUPERVISOR watches a SET of channels and has
# said WHICH one is blind since 9ba1c372 (2026-08-14 23:47, one fingerprint line per channel) — this
# column used to hold the single-file wording for it too and was red from that commit until 2026-09-24.
# `<channel>` is replaced with the absolute path of the role's watched channel.
# ---------------------------------------------------------------------------------------------
ROLES="
implementer|.claude/supervision/<orch-id>/<member-id>/channel.md|.claude/supervision/<orch-id>|YOUR CHANNEL CHANGED|md5sum failed
reviewer|.claude/supervision/<orch-id>/<member-id>/channel.md|.claude/supervision/<orch-id>|YOUR CHANNEL CHANGED|md5sum failed
solo|.claude/supervision/orch-under-test/owner-channel.md|.claude/supervision/orch-under-test|OWNER WROTE|md5sum failed
supervisor|.claude/supervision/orch-under-test/imp-1/channel.md|.claude/supervision/orch-under-test|CHANNELS CHANGED|md5sum on <channel> failed
general-supervisor|.claude/supervision/general/channel.md|.claude/supervision/general|GENERAL CHANNEL CHANGED|md5sum failed
"

# Pulls the fenced bash block that defines read_fp out of a role command.
#
# The CR strip is EXPLICIT and load-bearing. The role commands are stored CRLF on this machine
# (core.autocrlf=true, no .gitattributes), and the rewrites below anchor on end-of-line — against a
# CRLF line, `do$` does not match `do\r` and every rewrite would silently fail to apply. It currently
# works only because Git-for-Windows awk opens in text mode and drops the CR for us, which is an
# msys quirk this harness must not depend on: on a checkout that kept CRLF into a POSIX awk, the
# extraction would still succeed and the driver would not, and the refusal below is the only reason
# that would be noticed rather than reported as a pass.
extract_block() {
  awk '
    { sub(/\r$/, "") }
    /^```bash$/            { inblock = 1; buf = ""; next }
    inblock && /^```$/     { if (buf ~ /read_fp\(\)/) { printf "%s", buf; exit } ; inblock = 0; next }
    inblock                { buf = buf $0 "\n" }
  ' "$1"
}

run_role() {
  local role="$1" channel_rel="$2" orch_rel="$3" phrase="$4" reason_template="$5"
  # The watcher loop lives in the role's reference file since the protocols were split; the
  # SKILL.md that owns it only carries the imperative pointer to read it.
  local src="$SKILLS_DIR/$role/reference/watcher.md"

  [ -f "$src" ] || die "$role/reference/watcher.md is not in $SKILLS_DIR — cannot test the loop it ships"

  local block
  block="$(extract_block "$src")"
  [ -n "$block" ] || die "no fenced bash block defining read_fp in $role.md — the watcher is the subject of this harness"

  # ---- the two loop-plumbing rewrites, each verified to have applied ----
  local driven
  driven="$(printf '%s' "$block" | sed -e 's/^while true; do$/while read -r step; do/' -e 's/^  sleep 5$/  apply_step "$step"/')"

  case "$driven" in
    *'while read -r step; do'*) : ;;
    *) die "$role.md: the 'while true; do' rewrite did not apply — the loop shape changed and this harness would have tested nothing" ;;
  esac
  case "$driven" in
    *'apply_step "$step"'*) : ;;
    *) die "$role.md: the 'sleep 5' rewrite did not apply — the loop shape changed and this harness would have tested nothing" ;;
  esac

  # ---- a throwaway HOME laid out exactly like the real supervision tree ----
  local home; home="$(mktemp -d)"
  local channel="$home/$channel_rel"
  local orch="$home/$orch_rel"
  mkdir -p "$(dirname "$channel")" "$orch"
  printf '## [1] FROM supervisor — subject\n' > "$channel"

  # The supervisor watches a set, so give it the rest of the set to glob.
  if [ "$role" = "supervisor" ]; then
    mkdir -p "$orch/rev-1"
    printf 'rev\n' > "$orch/rev-1/channel.md"
    printf 'owner\n' > "$orch/owner-channel.md"
  fi

  local shim="$home/shim"
  mkdir -p "$shim"
  printf '#!/usr/bin/env bash\nexit 1\n' > "$shim/md5sum"
  chmod +x "$shim/md5sum"

  local out="$home/out.txt"

  # apply_step replaces the 5-second wait: it decides what the NEXT read will meet.
  #   ok         reads succeed, file unchanged
  #   fail       reads fail (md5sum cannot run — a fork failure, as on the real machine)
  #   append     an entry lands, reads succeed
  #   appendfail an entry lands AND reads fail — the case that must not lose the entry
  {
    printf 'REAL_PATH="%s"\n' "$PATH"
    printf 'CHANNEL_UNDER_TEST="%s"\n' "$channel"
    printf 'SHIM_DIR="%s"\n' "$shim"
    cat <<'PREAMBLE'
apply_step() {
  case "$1" in
    append|appendfail) printf '## [n] FROM supervisor — another\n' >> "$CHANNEL_UNDER_TEST" ;;
  esac
  case "$1" in
    fail|appendfail) PATH="$SHIM_DIR:$REAL_PATH" ;;
    *)               PATH="$REAL_PATH" ;;
  esac
}
PREAMBLE
    printf '%s\n' "$driven"
  } > "$home/driven.sh"

  # One deterministic stream covering every branch.
  {
    echo ok; echo ok            # quiet baseline
    echo append                 # FIRE 1 — a real entry
    echo ok
    echo fail                   # must be quiet, and must leave the marker
    echo ok                     # recovery must NOT fire — this was phantom #2
    echo appendfail             # an entry lands while the file cannot be read
    echo ok                     # FIRE 2 — preserved prev catches it on the next good read
    for _ in $(seq 1 12); do echo fail; done   # twelve consecutive failures
    echo ok
  # AIORCH_ID and ARGUMENTS are DERIVED from the folder rather than typed, so the environment and the
  # tree can never disagree. Typing them cost this harness two false failures: the marker landed in a
  # folder that did not exist, and the loop correctly declined to create it.
  #
  # AIORCH_SUPERVISION_ROOT IS PINNED TO THE TEMP TREE, not left to HOME: every watcher reads it FIRST.
  # Run from inside a live session (where it is set) this loop used to watch the REAL tree and — worse —
  # WRITE into it: the general supervisor's mark_unreadable dropped a false `.guard-not-in-force` into
  # the real `general/` folder (review of f2a6b02, 2026-09-24). A harness must never reach the live root.
  } | HOME="$home" AIORCH_SUPERVISION_ROOT="$home/.claude/supervision" ARGUMENTS="$(basename "$orch_rel")" AIORCH_ID="$(basename "$orch_rel")" AIORCH_MEMBER="imp-under-test" \
      bash "$home/driven.sh" > "$out" 2>"$home/err.txt"

  local fires blind marker_reason
  # `|| true`, never `|| printf 0`: grep -c already prints 0 when it matches nothing, it just exits
  # non-zero, so the fallback ran IN ADDITION and produced a two-line "0\n0". Harmless against an
  # expectation of 2, but it would have failed a check that expected none, and it made the mutant
  # run's output read like a broken harness rather than a caught defect.
  fires="$(grep -c "$phrase" "$out" 2>/dev/null || true)"
  blind="$(grep -c "WATCHER BLIND" "$out" 2>/dev/null || true)"

  printf '%s\n' "$role"
  check "$role: a real entry wakes it, a failed read never does (2 wakes, not 4)" "2" "$fires"
  check "$role: says it is blind once after twelve failed reads" "1" "$blind"

  if [ -f "$orch/.guard-not-in-force" ]; then
    marker_reason="$(sed -n '3p' "$orch/.guard-not-in-force")"
    check "$role: the marker names the command that failed" "${reason_template//<channel>/$channel}" "$marker_reason"
    check "$role: the marker names the watcher" "watcher" "$(sed -n '1p' "$orch/.guard-not-in-force")"
    # Line 6 is the consequence. Without it the app renders every marker as "ALLOWED the call", which
    # is the hooks' contract and false of a watcher — there is no call.
    check "$role: the marker says what it did instead of allowing a call" \
      "took the fingerprint as unknown rather than as a change" "$(sed -n '6p' "$orch/.guard-not-in-force")"
  else
    check "$role: drops the guard marker when it cannot read" "marker present" "marker absent"
  fi

  rm -rf "$home"
}

# ---------------------------------------------------------------------------------------------
# THE SOLO'S SIBLING HALF (spec 2026-09-23 §5.3). A linked solo also watches every outbox listed in
# "$orch/.siblings", in the SAME loop and the SAME fenced block as its owner half, so it is driven by
# the same two rewrites and extracted by the same extract_block — there is still no second copy of
# the loop here. The owner half runs alongside it, untouched, and must stay silent in this stream.
#
# Steps, on top of run_role's ok/fail:
#   sibappend  sibling B appends to its outbox THROUGH THE REAL bin/channel-append.sh, as a solo
#              (AIORCH_ROLE=solo) — so `sibling-outbox.md.self-write.solo` exists beside it, the exact
#              record a "helpful" self-write suppression in the sibling loop would read and obey. A bare
#              printf never wrote that record, so such a mutant stayed green (review of f2a6b02, I2).
#   sibadd     sibling C joins .siblings, its outbox already holding one entry (first sight)
#   sibaddd    sibling D joins .siblings with NO outbox yet — a newborn child (review I3)
#   sibdfirst  D creates its outbox as taught (touch) and appends its first entry in the same gap:
#              the `absent` state is the only thing that makes that first entry a wake
#   sibadde    sibling E joins, no outbox yet
#   sibetouch  E runs the taught `touch` and writes nothing: an empty outbox is still `absent`
#   pause      "$orch/.paused" appears — the owner put this solo to sleep
#   unpause    it goes again
#   sibfail    reads fail (md5sum cannot run), as run_role's `fail`
# ---------------------------------------------------------------------------------------------
run_sibling_case() {
  local helper="$SCRIPT_DIR/../bin/channel-append.sh"
  [ -f "$helper" ] || die "bin/channel-append.sh is not beside the hooks ($helper) — the sibling appends must go through the real helper"

  local src="$SKILLS_DIR/solo/reference/watcher.md"
  [ -f "$src" ] || die "solo/reference/watcher.md is not in $SKILLS_DIR — cannot test the sibling half it ships"

  local block
  block="$(extract_block "$src")"
  [ -n "$block" ] || die "no fenced bash block defining read_fp in solo/reference/watcher.md"

  local driven
  driven="$(printf '%s' "$block" | sed -e 's/^while true; do$/while read -r step; do/' -e 's/^  sleep 5$/  apply_step "$step"/')"

  case "$driven" in
    *'while read -r step; do'*) : ;;
    *) die "solo sibling case: the 'while true; do' rewrite did not apply — the loop shape changed and this harness would have tested nothing" ;;
  esac
  case "$driven" in
    *'apply_step "$step"'*) : ;;
    *) die "solo sibling case: the 'sleep 5' rewrite did not apply — the loop shape changed and this harness would have tested nothing" ;;
  esac

  local home; home="$(mktemp -d)"
  local root="$home/.claude/supervision"
  local orch="$root/orch-under-test"
  mkdir -p "$orch" "$home/B" "$home/C" "$home/D" "$home/E"
  printf '## [1] FROM owner — subject\n' > "$orch/owner-channel.md"
  printf '## [1] FROM solo — HANDOVER — the limits job\n' > "$home/B/sibling-outbox.md"
  printf '## [1] FROM solo — HANDOVER — the settings job\n' > "$home/C/sibling-outbox.md"
  printf 'B\t%s\tlive\tAI-Orch · limits rework\n' "$home/B/sibling-outbox.md" > "$orch/.siblings"

  local shim="$home/shim"
  mkdir -p "$shim"
  # md5 as well as md5sum: the sibling half falls back to BSD `md5 -q` as the kit rule requires, and on
  # macOS a shim of md5sum alone would leave the read succeeding and the failure branch untested.
  printf '#!/usr/bin/env bash\nexit 1\n' > "$shim/md5sum"
  printf '#!/usr/bin/env bash\nexit 1\n' > "$shim/md5"
  chmod +x "$shim/md5sum" "$shim/md5"

  {
    printf 'REAL_PATH="%s"\n' "$PATH"
    printf 'SHIM_DIR="%s"\n' "$shim"
    printf 'ORCH_UNDER_TEST="%s"\n' "$orch"
    printf 'B_OUTBOX="%s"\n' "$home/B/sibling-outbox.md"
    printf 'C_OUTBOX="%s"\n' "$home/C/sibling-outbox.md"
    printf 'D_OUTBOX="%s"\n' "$home/D/sibling-outbox.md"
    printf 'E_OUTBOX="%s"\n' "$home/E/sibling-outbox.md"
    printf 'HELPER="%s"\n' "$helper"
    cat <<'PREAMBLE'
# As a sibling solo writes: the real helper, signed by the session identity. The body comes on the
# helper's stdin from printf, never from the loop's stdin, which carries the steps. A refusal is marked
# in the timeline so the harness can tell "no wake" from "no write".
sibling_writes() {
  printf 'which DTO do you want?\n' \
    | PATH="$REAL_PATH" AIORCH_ROLE=solo bash "$HELPER" --channel "$1" --author solo --subject "ASK — which DTO" --body-file - > /dev/null 2>&1 \
    || echo "@helper-refused $1"
}

apply_step() {
  echo "@step $1"   # a timeline mark, so WHEN a wake happened can be checked, not only how many
  case "$1" in
    sibappend) sibling_writes "$B_OUTBOX" ;;
    sibadd)    printf 'C\t%s\tlive\tAI-Orch · settings rows\n' "$C_OUTBOX" >> "$ORCH_UNDER_TEST/.siblings" ;;
    sibaddd)   printf 'D\t%s\tlive\tAI-Orch · newborn child job\n' "$D_OUTBOX" >> "$ORCH_UNDER_TEST/.siblings" ;;
    sibdfirst) touch "$D_OUTBOX"; sibling_writes "$D_OUTBOX" ;;
    sibadde)   printf 'E\t%s\tlive\tAI-Orch · quiet child job\n' "$E_OUTBOX" >> "$ORCH_UNDER_TEST/.siblings" ;;
    sibetouch) touch "$E_OUTBOX" ;;
    pause)     : > "$ORCH_UNDER_TEST/.paused" ;;
    unpause)   rm -f "$ORCH_UNDER_TEST/.paused" ;;
  esac
  case "$1" in
    sibfail) PATH="$SHIM_DIR:$REAL_PATH" ;;
    *)       PATH="$REAL_PATH" ;;
  esac
}
PREAMBLE
    printf '%s\n' "$driven"
  } > "$home/driven.sh"

  local out="$home/out.txt"

  {
    echo ok; echo ok            # quiet baseline
    echo sibappend; echo ok     # FIRE 1 — B wrote
    echo sibadd; echo ok        # C seen for the first time: baseline, never a fire
    echo pause; echo sibappend; echo ok   # paused: quiet, and the baseline does not move
    echo unpause; echo ok       # FIRE 2 — the traffic that waited for the owner
    echo sibaddd; echo ok       # D listed, no outbox: absent, not a failure, not a wake
    echo sibdfirst; echo ok     # D FIRE 1 — a newborn's first entry is a change from absent
    echo sibadde; echo ok       # E listed, no outbox
    echo sibetouch; echo ok     # E touched and empty: still absent, no wake
    echo sibfail; echo ok       # a failed read is not a change
    for _ in $(seq 1 12); do echo sibfail; done   # twelve consecutive failed sibling reads
    echo ok
  } | HOME="$home" AIORCH_SUPERVISION_ROOT="$root" ARGUMENTS="orch-under-test" AIORCH_ID="orch-under-test" \
      bash "$home/driven.sh" > "$out" 2>"$home/err.txt"

  local b_fires c_fires d_fires e_fires refused owner_fires sib_blind
  b_fires="$(grep -c "SIBLING B WROTE" "$out" 2>/dev/null || true)"
  c_fires="$(grep -c "SIBLING C WROTE" "$out" 2>/dev/null || true)"
  d_fires="$(grep -c "SIBLING D WROTE" "$out" 2>/dev/null || true)"
  e_fires="$(grep -c "SIBLING E WROTE" "$out" 2>/dev/null || true)"
  refused="$(grep -c "^@helper-refused" "$out" 2>/dev/null || true)"
  owner_fires="$(grep -c "OWNER WROTE" "$out" 2>/dev/null || true)"
  sib_blind="$(grep -c "WATCHER BLIND — a sibling outbox" "$out" 2>/dev/null || true)"

  # The COUNT alone cannot tell "silent while paused, fires on unpause" from "fires during the pause,
  # silent after": both make two. So the wakes are read against the step marks as well.
  local during_pause after_unpause
  during_pause="$(awk '/^@step pause$/ { on = 1; next } /^@step unpause$/ { on = 0 } on && /SIBLING B WROTE/ { n++ } END { print n + 0 }' "$out")"
  after_unpause="$(awk '/^@step unpause$/ { on = 1; next } /^@step / { on = 0 } on && /SIBLING B WROTE/ { n++ } END { print n + 0 }' "$out")"

  printf 'solo (siblings)\n'
  check "solo siblings: B's appends wake it, once live and once after the pause (2)" "2" "$b_fires"
  check "solo siblings: silent while paused" "0" "$during_pause"
  check "solo siblings: the traffic that waited fires on the first read after unpause" "1" "$after_unpause"
  check "solo siblings: the real helper accepted every sibling append" "0" "$refused"
  check "solo siblings: B's appends left the solo self-write record the loop must NOT obey" \
    "present" "$([ -f "$home/B/sibling-outbox.md.self-write.solo" ] && echo present || echo absent)"
  check "solo siblings: a sibling seen for the first time is baseline, never a wake" "0" "$c_fires"
  check "solo siblings: a newborn's first entry into a just-created outbox wakes it once" "1" "$d_fires"
  check "solo siblings: a touched, empty outbox is still absent — no wake" "0" "$e_fires"
  check "solo siblings: the owner half is not disturbed by sibling traffic" "0" "$owner_fires"
  check "solo siblings: says it is blind to the outboxes once after twelve failed reads" "1" "$sib_blind"

  rm -rf "$home"
}

printf 'watcher behaviour — running the loop shipped in %s\n\n' "$SKILLS_DIR"

# A herestring, never a pipe: a piped `while` runs in a subshell and its FAILURES count would be
# discarded, which is this harness certifying itself green by losing the evidence.
while IFS='|' read -r role channel orch phrase reason; do
  [ -n "$role" ] || continue
  [ -n "$reason" ] || die "ROLES row for '$role' has no expected marker reason — every role states its own"
  run_role "$role" "$channel" "$orch" "$phrase" "$reason"
done <<< "$(printf '%s\n' "$ROLES")"

run_sibling_case

printf '\n%s checks, %s failures\n' "$CHECKS" "$FAILURES"
[ "$FAILURES" -eq 0 ] || exit 1

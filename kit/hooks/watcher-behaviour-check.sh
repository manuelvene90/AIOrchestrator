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
# marker, what the wake line says, and how precisely the failure marker names the failed read.
#
# THE FIFTH FIELD EXISTS BECAUSE ONE ROLE WATCHES SEVERAL FILES. Four of the five loops watch a
# single channel, so `FP_ERR="md5sum"` names the failed read completely — there is only one file it
# could be about. The supervisor watches every spoke plus the owner channel, so it writes
# `FP_ERR="md5sum on $file"`, and that extra half is the ONLY actionable part of the marker for the
# role that has more than one candidate: "md5sum failed" on a nine-member orchestration tells the
# owner nothing they can act on, which is the failure mode CLAUDE.md decision 21 is about.
#
# This harness used to assert the bare literal for all five and had therefore reported a standing
# failure against the one role that carries the useful message. Asserting a per-role expectation
# keeps the comparison exact — a prefix or substring match would have made the check pass for a
# marker that named no file at all, which is the behaviour being ruled out.
#
#   role | channel path under the fake HOME | orchestration folder | wake phrase | fingerprint scope
#
# fingerprint scope: `command` = the marker names the command only (one watched file)
#                    `per-file` = it names the command AND the file (several watched files)
# ---------------------------------------------------------------------------------------------
ROLES="
implementer|.claude/supervision/<orch-id>/<member-id>/channel.md|.claude/supervision/<orch-id>|YOUR CHANNEL CHANGED|command
reviewer|.claude/supervision/<orch-id>/<member-id>/channel.md|.claude/supervision/<orch-id>|YOUR CHANNEL CHANGED|command
solo|.claude/supervision/orch-under-test/owner-channel.md|.claude/supervision/orch-under-test|OWNER WROTE|command
supervisor|.claude/supervision/orch-under-test/imp-1/channel.md|.claude/supervision/orch-under-test|CHANNELS CHANGED|per-file
general-supervisor|.claude/supervision/general/channel.md|.claude/supervision/general|GENERAL CHANNEL CHANGED|command
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
  local role="$1" channel_rel="$2" orch_rel="$3" phrase="$4" fp_scope="$5"
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

  # What line 3 of the marker must say, for THIS role. `$channel` is the file the shimmed md5sum is
  # made to fail on, and for a multi-file watcher it is also the first entry of the globbed set, so
  # the expectation is exact rather than a pattern. An unrecognised scope DIES rather than defaulting:
  # a default here would silently assert the wrong contract for a role added later, which is this
  # harness's own founding failure — nothing-is-ALLOW.
  local expected_reason
  case "$fp_scope" in
    command)  expected_reason="md5sum failed" ;;
    per-file) expected_reason="md5sum on $channel failed" ;;
    *) die "$role: unknown fingerprint scope '$fp_scope' in the ROLES table — the marker contract for this role is undeclared, and guessing it would certify whatever the loop happens to write" ;;
  esac

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
  } | HOME="$home" ARGUMENTS="$(basename "$orch_rel")" AIORCH_ID="$(basename "$orch_rel")" AIORCH_MEMBER="imp-under-test" \
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
    check "$role: the marker names the command that failed" "$expected_reason" "$marker_reason"
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

printf 'watcher behaviour — running the loop shipped in %s\n\n' "$SKILLS_DIR"

# A herestring, never a pipe: a piped `while` runs in a subshell and its FAILURES count would be
# discarded, which is this harness certifying itself green by losing the evidence.
while IFS='|' read -r role channel orch phrase fp_scope; do
  [ -n "$role" ] || continue
  run_role "$role" "$channel" "$orch" "$phrase" "$fp_scope"
done <<< "$(printf '%s\n' "$ROLES")"

# =================================================================================================
# TICKET MODE — the shrunken watcher added in the one-wake-model series (2026-09-15, Task 9).
#
# Same extraction discipline as above: pull the fenced bash block out of the role's OWN watcher.md
# (the "## Ticket mode — when the app decides" section) and run it — no second copy of the loop to
# drift from the shipped one. The block is found by the "# TICKET MODE." marker rather than
# `read_fp()`, since this loop has no fingerprinting left to find.
#
# THE MONITOR NEVER PARSES THE TICKET'S JSON — it compares the file's raw bytes against what it saw
# last time. So the driver below never needs to write real JSON either; a bare letter is a fine
# "ticket" for this harness, and using one is deliberate: a harness that fed real JSON here could
# hide a script that had started parsing it, which is exactly the coupling the ticket format forbids.
# =================================================================================================

extract_ticket_block() {
  awk '
    { sub(/\r$/, "") }
    /^```bash$/            { inblock = 1; buf = ""; next }
    inblock && /^```$/     { if (buf ~ /# TICKET MODE\./) { printf "%s", buf; exit } ; inblock = 0; next }
    inblock                { buf = buf $0 "\n" }
  ' "$1"
}

#   role | ticket path under the fake HOME | meeting path under the fake HOME, or empty | AIORCH_MEMBER, or empty
TICKET_ROLES="
supervisor|.claude/supervision/orch-under-test/.wake-supervisor|.claude/supervision/orch-under-test/.meeting|
implementer|.claude/supervision/orch-under-test/imp-1/.wake||imp-1
reviewer|.claude/supervision/orch-under-test/rev-1/.wake||rev-1
solo|.claude/supervision/orch-under-test/solo-1/.wake||solo-1
general-supervisor|.claude/supervision/general/.wake||
communicator|.claude/supervision/orch-under-test/.wake-communicator||
"

run_ticket_role() {
  local role="$1" ticket_rel="$2" meeting_rel="$3" member="$4"
  local src="$SKILLS_DIR/$role/reference/watcher.md"

  [ -f "$src" ] || die "$role/reference/watcher.md is not in $SKILLS_DIR — cannot test the ticket loop it ships"

  local block
  block="$(extract_ticket_block "$src")"
  [ -n "$block" ] || die "no fenced bash block carrying '# TICKET MODE.' in $role.md — Task 9's ticket-mode section is the subject of this harness and appears to be missing"

  local driven
  driven="$(printf '%s' "$block" | sed -e 's/^while true; do$/while read -r step; do/' -e 's/^  sleep 2$/  apply_step "$step"/')"

  case "$driven" in
    *'while read -r step; do'*) : ;;
    *) die "$role.md ticket mode: the 'while true; do' rewrite did not apply — the loop shape changed and this harness would have tested nothing" ;;
  esac
  case "$driven" in
    *'apply_step "$step"'*) : ;;
    *) die "$role.md ticket mode: the 'sleep 2' rewrite did not apply — the loop shape changed and this harness would have tested nothing" ;;
  esac

  local has_meeting=0
  case "$block" in
    *'.meeting'*) has_meeting=1 ;;
  esac
  # meeting_rel is only meaningful when the shipped block actually checks it. A role whose script
  # never mentions .meeting must not be scored on meeting behaviour it does not implement — that
  # would be an assertion that "passes" whether the code is right or simply absent (CLAUDE.md
  # decision 20), so scenario 3 below runs ONLY when has_meeting=1.
  if [ -n "$meeting_rel" ] && [ "$has_meeting" -eq 0 ]; then
    die "$role.md ticket mode: expected a .meeting check (meeting_rel is set in TICKET_ROLES) but the shipped block has none — table and script disagree"
  fi

  printf '%s (ticket mode)\n' "$role"

  local home ticket meeting
  home="$(mktemp -d)"
  ticket="$home/$ticket_rel"
  mkdir -p "$(dirname "$ticket")"
  if [ -n "$meeting_rel" ]; then
    meeting="$home/$meeting_rel"
  else
    meeting="$home/.claude/supervision/unused-no-meeting-support/.meeting"
  fi
  mkdir -p "$(dirname "$meeting")"

  {
    printf 'TICKET_UNDER_TEST="%s"\n' "$ticket"
    printf 'MEETING_UNDER_TEST="%s"\n' "$meeting"
    cat <<'PREAMBLE'
apply_step() {
  case "$1" in
    set:*)      printf '%s' "${1#set:}" > "$TICKET_UNDER_TEST" ;;
    meeting_on) : > "$MEETING_UNDER_TEST" ;;
    meeting_off) rm -f "$MEETING_UNDER_TEST" ;;
  esac
}
PREAMBLE
    printf '%s\n' "$driven"
  } > "$home/driven.sh"

  # AIORCH_ID/AIORCH_MEMBER, exactly as SpawnCommand_Builder sets them for the real process —
  # verified 2026-09-15, not guessed (see the ticket-mode sections' own "Adapted from $ARGUMENTS"
  # notes). general-supervisor takes no AIORCH_MEMBER: its script hardcodes the "general" folder.
  local aiorch_id="orch-under-test" aiorch_member="$member"
  [ "$role" = "general-supervisor" ] && aiorch_id="general"

  # ---- Scenario 1: no ticket has ever been written — total silence, not just "no WAKE". ----
  local out1="$home/out1.txt"
  { echo ok; echo ok; echo ok; } \
    | HOME="$home" AIORCH_ID="$aiorch_id" AIORCH_MEMBER="$aiorch_member" bash "$home/driven.sh" > "$out1" 2>"$home/err1.txt"
  check "$role: a missing ticket produces no output at all" "" "$(cat "$out1")"

  # ---- Scenario 2: fires once per DISTINCT ticket value, never once per poll. ----
  local out2="$home/out2.txt"
  rm -f "$ticket"
  { echo ok; echo 'set:A'; echo ok; echo ok; echo 'set:B'; echo ok; echo ok; } \
    | HOME="$home" AIORCH_ID="$aiorch_id" AIORCH_MEMBER="$aiorch_member" bash "$home/driven.sh" > "$out2" 2>"$home/err2.txt"
  check "$role: two distinct tickets wake it exactly twice, not once per poll" "2" "$(grep -c "WAKE" "$out2" 2>/dev/null || true)"

  # ---- Scenario 2b: EVERY wake line sends the session to the pack the ticket names (Task 11). ----
  # The pack is the whole payoff of the one-wake-model series: the app assembles the session's brief,
  # its last report, the entries that woke it and the repo state, and the ticket carries the path. A
  # wake line that does not point at it leaves a terminal session doing what it always did — going to
  # look for its own state — with the pack sitting unread beside it. Counted against the SAME two
  # wakes above, so a script that fired twice and named the pack once is a failure, not a pass.
  check "$role: every wake line sends the session to its state pack" "2" "$(grep -c "state pack" "$out2" 2>/dev/null || true)"

  # ---- Scenario 3 (meeting-capable roles only): silence during the meeting, exactly one delivery after. ----
  if [ "$has_meeting" -eq 1 ]; then
    local out3="$home/out3.txt"
    rm -f "$ticket" "$meeting"
    {
      echo 'set:A'        # FIRE 1 — baseline ticket before any meeting
      echo meeting_on
      echo 'set:B'        # a ticket lands WHILE the meeting is on — must be held, not lost
      echo ok
      echo ok
      echo meeting_off
      echo ok             # FIRE 2 — the held ticket, delivered exactly once
      echo ok             # must NOT fire again for the same value
    } | HOME="$home" AIORCH_ID="$aiorch_id" AIORCH_MEMBER="$aiorch_member" bash "$home/driven.sh" > "$out3" 2>"$home/err3.txt"
    check "$role: silent through the meeting, then delivers the held ticket exactly once" \
      "2" "$(grep -c "WAKE" "$out3" 2>/dev/null || true)"
  fi

  rm -rf "$home"
}

printf '\nwake tickets (Task 9) — running the loop shipped in %s\n\n' "$SKILLS_DIR"

while IFS='|' read -r role ticket_rel meeting_rel member; do
  [ -n "$role" ] || continue
  run_ticket_role "$role" "$ticket_rel" "$meeting_rel" "$member"
done <<< "$(printf '%s\n' "$TICKET_ROLES")"

printf '\n%s checks, %s failures\n' "$CHECKS" "$FAILURES"
[ "$FAILURES" -eq 0 ] || exit 1

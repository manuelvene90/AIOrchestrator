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
COMMANDS_DIR="$(cd "$SCRIPT_DIR/../commands" 2>/dev/null && pwd || true)"

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

[ -n "$COMMANDS_DIR" ] && [ -d "$COMMANDS_DIR" ] \
  || die "no kit/commands beside this script (looked next to $SCRIPT_DIR) — the role commands are the subject, and without them every check below would pass by finding nothing"

# ---------------------------------------------------------------------------------------------
# Per-role facts. Only paths differ: which channel file the loop watches, which folder receives the
# marker, and what the wake line says.
#
#   role | channel path under the fake HOME | orchestration folder | wake phrase
# ---------------------------------------------------------------------------------------------
ROLES="
implementer|.claude/supervision/<orch-id>/<member-id>/channel.md|.claude/supervision/<orch-id>|YOUR CHANNEL CHANGED
reviewer|.claude/supervision/<orch-id>/<member-id>/channel.md|.claude/supervision/<orch-id>|YOUR CHANNEL CHANGED
solo|.claude/supervision/orch-under-test/owner-channel.md|.claude/supervision/orch-under-test|OWNER WROTE
supervisor|.claude/supervision/orch-under-test/imp-1/channel.md|.claude/supervision/orch-under-test|CHANNELS CHANGED
general-supervisor|.claude/supervision/general/channel.md|.claude/supervision/general|GENERAL CHANNEL CHANGED
"

# Pulls the fenced bash block that defines read_fp out of a role command.
extract_block() {
  awk '
    /^```bash$/            { inblock = 1; buf = ""; next }
    inblock && /^```$/     { if (buf ~ /read_fp\(\)/) { printf "%s", buf; exit } ; inblock = 0; next }
    inblock                { buf = buf $0 "\n" }
  ' "$1"
}

run_role() {
  local role="$1" channel_rel="$2" orch_rel="$3" phrase="$4"
  local src="$COMMANDS_DIR/$role.md"

  [ -f "$src" ] || die "$role.md is not in $COMMANDS_DIR — cannot test the loop it ships"

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
  } | HOME="$home" ARGUMENTS="$(basename "$orch_rel")" AIORCH_ID="$(basename "$orch_rel")" AIORCH_MEMBER="imp-under-test" \
      bash "$home/driven.sh" > "$out" 2>"$home/err.txt"

  local fires blind marker_reason
  fires="$(grep -c "$phrase" "$out" 2>/dev/null || printf 0)"
  blind="$(grep -c "WATCHER BLIND" "$out" 2>/dev/null || printf 0)"

  printf '%s\n' "$role"
  check "$role: a real entry wakes it, a failed read never does (2 wakes, not 4)" "2" "$fires"
  check "$role: says it is blind once after twelve failed reads" "1" "$blind"

  if [ -f "$orch/.guard-not-in-force" ]; then
    marker_reason="$(sed -n '3p' "$orch/.guard-not-in-force")"
    check "$role: the marker names the command that failed" "md5sum failed — fingerprint taken as unknown, not as a change" "$marker_reason"
    check "$role: the marker names the watcher" "watcher" "$(sed -n '1p' "$orch/.guard-not-in-force")"
  else
    check "$role: drops the guard marker when it cannot read" "marker present" "marker absent"
  fi

  rm -rf "$home"
}

printf 'watcher behaviour — running the loop shipped in %s\n\n' "$COMMANDS_DIR"

# A herestring, never a pipe: a piped `while` runs in a subshell and its FAILURES count would be
# discarded, which is this harness certifying itself green by losing the evidence.
while IFS='|' read -r role channel orch phrase; do
  [ -n "$role" ] || continue
  run_role "$role" "$channel" "$orch" "$phrase"
done <<< "$(printf '%s\n' "$ROLES")"

printf '\n%s checks, %s failures\n' "$CHECKS" "$FAILURES"
[ "$FAILURES" -eq 0 ] || exit 1

#!/usr/bin/env bash
# MAY THIS ORCHESTRATION'S SUPERVISOR GO FRESH? — the three preconditions of plan 06, asked of the
# machine rather than of a document.
#
#   0  yes
#   1  no, and it names which precondition failed
#   2  it CANNOT TELL — a path it needs is not there. Never confused with 0: a check that did not run
#      is not a check that passed (CLAUDE.md decision 20, hook-behaviour-check.sh, 2026-08-11, which
#      reported 16 confident failures about code it had never executed).
#
# Read-only. It opens no channel and prints no entry text.
set -u

SUP="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"
PACK_MIN_AGE_DAYS="${AIORCH_PACK_MIN_AGE_DAYS:-3}"

# ----- the self-test, defined BEFORE the dispatch line below calls it -----
# bash resolves a function at call time, so a definition placed after the --self-test branch gives
# "command not found". And it re-invokes the script as `bash "$0"`, never `"$0"`: the file is not
# executable until somebody chmod +x's it, and `"$0"` alone exits 126.
self_test() {
  local failures=0 root out rc

  # CANNOT CHECK => 2. Never 0, never 1: "I did not look" is not "it is fine".
  root="$(mktemp -d)"
  out="$(AIORCH_SUPERVISION_ROOT="$root/nope" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 2 ] || { echo "FAIL: a missing supervision root must exit 2, got $rc"; failures=1; }
  case "$out" in *"cannot check"*) ;; *) echo "FAIL: it must say it cannot check"; failures=1;; esac

  # EACH CASE ISOLATES ONE PRECONDITION. Sabotage check, 2026-09-15: an earlier draft asserted only
  # `rc = 1` for the empty-conclusions case while the pack was also too young — so the case passed via
  # P2 and pinned NOTHING about P3, and deleting the P3 check outright left the self-test green. An
  # assertion with two routes to its state pins neither (CLAUDE.md decision 20). So: the two
  # preconditions a case is not about are SATISFIED first, and the case asserts the NAME, not just
  # the code.
  mkdir -p "$root/repo-1"
  : > "$root/repo-1/.supervisor.pack.md"
  touch -t 202601010000 "$root/repo-1/.supervisor.pack.md"   # P1 and P2 satisfied

  # P3 alone: absent conclusions.
  out="$(AIORCH_SUPERVISION_ROOT="$root" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 1 ] || { echo "FAIL: no conclusions file must exit 1, got $rc"; failures=1; }
  case "$out" in *"P3 FAIL"*) ;; *) echo "FAIL: an absent conclusions file must name P3 — got: $out"; failures=1;; esac
  case "$out" in *"P1 FAIL"*|*"P2 FAIL"*) echo "FAIL: only P3 should have failed here — got: $out"; failures=1;; esac

  # P3 alone: an EMPTY conclusions file, which is the exact state in which the flip loses everything
  # and reports success. It must fail like an absent one.
  : > "$root/repo-1/.supervisor.state.md"
  out="$(AIORCH_SUPERVISION_ROOT="$root" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 1 ] || { echo "FAIL: an empty conclusions file must exit 1, got $rc"; failures=1; }
  case "$out" in *"P3 FAIL"*) ;; *) echo "FAIL: an empty conclusions file must name P3 — got: $out"; failures=1;; esac

  printf 'dead ends:\n- the fence route (2026-09-13): the tailer re-anchors first\n' \
    > "$root/repo-1/.supervisor.state.md"

  # P1 alone: conclusions written, no pack has ever existed.
  rm -f "$root/repo-1/.supervisor.pack.md"
  out="$(AIORCH_SUPERVISION_ROOT="$root" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 1 ] || { echo "FAIL: no pack at all must exit 1, got $rc"; failures=1; }
  case "$out" in *"P1 FAIL"*) ;; *) echo "FAIL: a missing pack must name P1 — got: $out"; failures=1;; esac
  case "$out" in *"P3 FAIL"*) echo "FAIL: only P1 should have failed here — got: $out"; failures=1;; esac

  # P2 alone: a pack exists but is younger than the window. Without this case the P2 branch is never
  # executed by anything, and a branch that only ever returns "fine" is the shape of a deleted guard.
  : > "$root/repo-1/.supervisor.pack.md"
  out="$(AIORCH_SUPERVISION_ROOT="$root" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 1 ] || { echo "FAIL: a pack younger than the window must exit 1, got $rc"; failures=1; }
  case "$out" in *"P2 FAIL"*) ;; *) echo "FAIL: a young pack must name P2 — got: $out"; failures=1;; esac
  case "$out" in *"P1 FAIL"*|*"P3 FAIL"*) echo "FAIL: only P2 should have failed here — got: $out"; failures=1;; esac

  # All three => 0.
  touch -t 202601010000 "$root/repo-1/.supervisor.pack.md"
  out="$(AIORCH_SUPERVISION_ROOT="$root" bash "$0" repo-1 2>&1)"; rc=$?
  [ "$rc" = 0 ] || { echo "FAIL: all preconditions met must exit 0, got $rc — $out"; failures=1; }

  rm -rf "$root"
  [ "$failures" = 0 ] && echo "preflight self-test: PASS"
  return "$failures"
}

[ "${1:-}" = "--self-test" ] && { self_test; exit $?; }

ORCH="${1:-}"
[ -n "$ORCH" ] || { echo "usage: preflight.sh <orch-id> | --self-test" >&2; exit 2; }

FOLDER="$SUP/$ORCH"
[ -d "$FOLDER" ] || { echo "cannot check: $FOLDER does not exist" >&2; exit 2; }

STATE="$FOLDER/.supervisor.state.md"
fail=0

# P1 — has a state pack ever been written here at all? A pack is what replaces a transcript; without
# one, going fresh hands the session nothing.
oldest="$(find "$FOLDER" -maxdepth 2 -name '*pack.md' -type f 2>/dev/null | head -1)"

if [ -z "$oldest" ]; then
  echo "P1 FAIL: no state pack has ever been written under $FOLDER — plan 01 has not shipped here"
  fail=1
# P2 — and has one been around longer than the window? A pack is rewritten every fresh turn, so its
# mtime is always recent; what "three days" is about is whether ANY pack-shaped file here predates
# the window.
elif [ -z "$(find "$FOLDER" -maxdepth 2 -name '*pack.md' -type f -mtime +"$PACK_MIN_AGE_DAYS" 2>/dev/null | head -1)" ]; then
  echo "P2 FAIL: no pack under $FOLDER is older than $PACK_MIN_AGE_DAYS days — the pack has not been proven long enough"
  fail=1
fi

# P3 — the conclusions have a home AND something in it. An EMPTY file is a FAIL, not a pass: it is
# precisely the state in which the flip loses everything and reports success.
if [ ! -s "$STATE" ]; then
  echo "P3 FAIL: $STATE is missing or empty — the supervisor's conclusions have nowhere to live, and going fresh would drop them"
  fail=1
fi

# Said even on success, because the next reader needs to know WHICH copies were consulted (decision 18).
echo "preflight read: $FOLDER (oldest pack: ${oldest:-none}, conclusions: $STATE)"
exit "$fail"

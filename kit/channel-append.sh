#!/bin/bash
#
# channel-append.sh — the ONE way a session appends to a supervision channel.
#
# It exists because "re-read the last header and add one" cannot be made safe by trying harder: the
# window it leaves open IS the write. Two writers who both read [71] both write [72], and a writer
# that emits its entry in more than one write() call gets another author's header dropped into the
# middle of it. Both happened on 2026-08-13, five minutes apart, and the second put a reviewer's
# nine findings under the supervisor's header — an audit trail that misattributes under load is
# worse than one that drops entries, because it is confidently wrong.
#
# WHAT THIS GUARANTEES: writers that use this helper are serialised against every other writer that
# uses it, INCLUDING the app, which takes the same lock from .NET. It cannot bind a writer that does
# not ask. A session can append with a bare redirect and nothing here will stop it — every session
# runs as the same OS user and no location is out of its reach. So the true sentence is "writers
# using the protocol cannot collide with each other", never "channel appends are atomic".
#
# The lock is a DIRECTORY, because mkdir is a single atomic syscall that fails when the target
# exists — the one exclusive-create bash and .NET can both perform without either emulating the
# other. flock was rejected: msys flock and Windows LockFileEx are different mechanisms, and
# assuming msys/Windows equivalence is how this repo has already produced silent failures.
#
# Exit codes are meant to be distinguishable, because "could not acquire" and "wrote it" must never
# look alike to the caller:
#   0  the entry was appended; its index is printed on stdout
#   2  usage error
#   3  COULD NOT ACQUIRE THE LOCK within the budget — nothing was written, retry is the caller's
#   4  an I/O error; nothing was appended
#
set -u

STALE_SECONDS=60          # Must match ChannelFile_Lock.STALE_SECONDS. See its comment: a GUESS,
                          # deliberately conservative, invalidated only by a writer that does
                          # something slow while holding the lock — which nothing may do.
RETRY_INITIAL_MS=50
RETRY_MAX_MS=400
DEFAULT_BUDGET_SECONDS=10

usage() {
  echo "usage: channel-append.sh --channel <file> --author <word> --subject <text> --body-file <file> [--budget-seconds N]" >&2
  echo "       (--body - reads the body from stdin)" >&2
  exit 2
}

CHANNEL=""; AUTHOR=""; SUBJECT=""; BODY_FILE=""; BUDGET_SECONDS="$DEFAULT_BUDGET_SECONDS"

while [ $# -gt 0 ]; do
  case "$1" in
    --channel)        CHANNEL="${2:-}"; shift 2 ;;
    --author)         AUTHOR="${2:-}"; shift 2 ;;
    --subject)        SUBJECT="${2:-}"; shift 2 ;;
    --body-file)      BODY_FILE="${2:-}"; shift 2 ;;
    --budget-seconds) BUDGET_SECONDS="${2:-}"; shift 2 ;;
    -h|--help)        usage ;;
    *) echo "channel-append.sh: unknown argument '$1'" >&2; usage ;;
  esac
done

[ -n "$CHANNEL" ] && [ -n "$AUTHOR" ] && [ -n "$SUBJECT" ] && [ -n "$BODY_FILE" ] || usage

if [ "$BODY_FILE" = "-" ]; then
  BODY_FILE="$(mktemp)" || { echo "channel-append.sh: cannot create a temp file" >&2; exit 4; }
  cat > "$BODY_FILE"
  trap 'rm -f "$BODY_FILE"' EXIT
fi

[ -f "$BODY_FILE" ] || { echo "channel-append.sh: body file '$BODY_FILE' does not exist" >&2; exit 2; }
[ -f "$CHANNEL" ] || { echo "channel-append.sh: channel '$CHANNEL' does not exist" >&2; exit 2; }

LOCK_DIR="${CHANNEL}.lock"
OWNER_FILE="$LOCK_DIR/owner"
HELD=0

# Identifies THIS acquisition, not this process. $$ alone is not enough: pids are reused, and the
# question at release time is "is this still the lock I took", which a pid cannot answer.
OWNERSHIP_TOKEN="$$-$(date -u +%s)-${RANDOM}${RANDOM}"

release_lock() {
  # Two guards, and the second one is the important one.
  #
  # HELD stops a run that never acquired from deleting somebody else's lock on its way out.
  #
  # The TOKEN stops something subtler and worse: our lock being broken as stale mid-write and
  # re-acquired by another writer, after which deleting "$LOCK_DIR" by path would destroy THEIR
  # lock while they are writing, and a third writer could then acquire alongside them. The stale
  # break exists to recover from a dead holder, and without this check it arms that. The token is
  # minted per acquire — a pid is reused and a path is reused, an acquisition is not.
  if [ "$HELD" = "1" ]; then
    local held_token
    held_token="$(grep -m1 '^token=' "$OWNER_FILE" 2>/dev/null | cut -d= -f2-)"

    if [ "$held_token" = "$OWNERSHIP_TOKEN" ]; then
      rm -rf "$LOCK_DIR" 2>/dev/null || true
    else
      echo "channel-append.sh: NOT releasing the lock on $(basename "$CHANNEL") — it was broken as stale and another writer holds it now; this write overran ${STALE_SECONDS}s." >&2
    fi

    HELD=0
  fi
}
trap 'release_lock' EXIT INT TERM

# Returns 0 when the holder looks dead. Anything it cannot READ or PARSE counts as ALIVE: a false
# "alive" costs a wait, a false "dead" breaks a live lock and corrupts the file the lock protects.
lock_is_stale() {
  [ -d "$LOCK_DIR" ] || return 1

  local stamp held_epoch now_epoch

  # No owner file is USUALLY a writer part-way through acquiring, and breaking one microseconds old
  # would be worse than waiting. But treating it as alive UNCONDITIONALLY made the state permanently
  # unbreakable by both sides — and this script is the only thing that can create it, because it
  # mkdirs the lock and then writes the metadata. A hard kill in between (the app tree-kills every
  # session on exit, which does not run the EXIT trap below) would wedge the channel forever.
  # So fall back to the DIRECTORY's own age.
  if [ ! -f "$OWNER_FILE" ]; then
    held_epoch="$(date -u -r "$LOCK_DIR" +%s 2>/dev/null)" || return 1
    [ -n "$held_epoch" ] || return 1
    now_epoch="$(date -u +%s)"
    [ $((now_epoch - held_epoch)) -gt "$STALE_SECONDS" ]
    return $?
  fi

  stamp="$(grep -m1 '^utc=' "$OWNER_FILE" 2>/dev/null | cut -d= -f2-)"
  [ -n "$stamp" ] || return 1

  held_epoch="$(date -u -d "$stamp" +%s 2>/dev/null)" || return 1
  [ -n "$held_epoch" ] || return 1
  now_epoch="$(date -u +%s)"

  [ $((now_epoch - held_epoch)) -gt "$STALE_SECONDS" ]
}

# Breaking is a RENAME, never a delete. Two writers can both judge the same lock stale; if both
# deleted it both would then acquire, producing exactly the collision this exists to prevent. Only
# one rename can win. The broken lock is kept as evidence of a writer that died holding it.
break_if_stale() {
  if lock_is_stale; then
    # Read the stamp BEFORE the move: afterwards the path is gone, and a diagnostic that cannot say
    # WHEN the dead holder took the lock is not evidence of anything.
    local held_since
    held_since="$(grep -m1 '^utc=' "$OWNER_FILE" 2>/dev/null | cut -d= -f2-)"

    if mv "$LOCK_DIR" "${LOCK_DIR}.broken.$$.$(date -u +%s)" 2>/dev/null; then
      echo "channel-append.sh: broke a stale lock on $(basename "$CHANNEL") — its holder took it at ${held_since:-an unrecorded time} and never released it" >&2
    fi
  fi
}

acquire_lock() {
  local waited_ms=0 budget_ms delay_ms="$RETRY_INITIAL_MS"
  budget_ms=$(awk "BEGIN{printf \"%d\", $BUDGET_SECONDS * 1000}")

  while true; do
    # mkdir, NOT the stage-and-rename shape C# uses, and this asymmetry is deliberate and measured.
    #
    # C# can fill a staging directory and rename it into place because Directory.Move fails on ANY
    # existing target, so the lock never exists without its metadata. bash has no primitive with
    # that behaviour. Measured on this machine:
    #   plain `mv src dst` on an existing dst  -> moves src INSIDE dst and reports SUCCESS
    #   `mv -T src dst` on a non-empty dst     -> fails (correct)
    #   `mv -T src dst` on an EMPTY dst        -> SUCCEEDS, replacing it
    # That last one is disqualifying: a writer that had just mkdir'd its lock and not yet written
    # the metadata would have it stolen, and TWO writers would believe they held it — the exact
    # collision this protocol exists to prevent, and worse than the wedge it was meant to fix.
    #
    # mkdir fails on any existing directory, empty or not, which is the exclusivity required. The
    # window it leaves (lock created, metadata not yet written) is closed downstream instead:
    # lock_is_stale falls back to the DIRECTORY's age, so an abandoned empty lock is breakable
    # rather than permanent. The state is recovered from rather than removed, because removing it
    # is not available here at an acceptable price.
    if mkdir "$LOCK_DIR" 2>/dev/null; then
      HELD=1
      printf 'pid=%s\nutc=%s\nrole=%s\ntoken=%s\n' "$$" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "session" "$OWNERSHIP_TOKEN" > "$OWNER_FILE"
      return 0
    fi

    break_if_stale

    [ "$waited_ms" -ge "$budget_ms" ] && return 1

    sleep "$(awk "BEGIN{printf \"%.3f\", $delay_ms/1000}")"
    waited_ms=$((waited_ms + delay_ms))
    delay_ms=$((delay_ms * 2))
    [ "$delay_ms" -gt "$RETRY_MAX_MS" ] && delay_ms="$RETRY_MAX_MS"
  done
}

if ! acquire_lock; then
  echo "channel-append.sh: COULD NOT ACQUIRE the lock on '$CHANNEL' within ${BUDGET_SECONDS}s — NOTHING WAS WRITTEN." >&2
  echo "channel-append.sh: retry; do not append without the lock, that is the collision this prevents." >&2
  exit 3
fi

# ---- critical section -------------------------------------------------------------------------
# The index is read HERE, inside the lock, which is what stops two writers choosing the same one.
# Read it outside and the lock protects the write while leaving the decision it depends on racing.
#
# THE C# PARSER IS AUTHORITATIVE, and this pattern is a transcription of its regex
# (ChannelEntry_Parser.Header_Regex): ^##\s*\[(\d+)\]\s*FROM\s+(\S+). Any whitespace after the
# hashes, any whitespace before FROM, and FROM is REQUIRED.
#
# It used to require exactly one space and no FROM, which made the two scanners disagree — a header
# written "##  [82] FROM x" was counted by the app and invisible here. The dangerous direction is
# this side UNDER-counting: the app appends [84], a session then acquires the lock cleanly, sees a
# maximum of 82, and mints a duplicate. Allocating the index inside the lock is what made the
# duplicate-index defect one problem instead of two, and two scanners that disagree hand it back.
LAST_INDEX="$(grep -oE '^##[[:space:]]*\[[0-9]+\][[:space:]]*FROM[[:space:]]' "$CHANNEL" 2>/dev/null | grep -oE '[0-9]+' | sort -n | tail -1)"
[ -n "$LAST_INDEX" ] || LAST_INDEX=0
NEXT_INDEX=$((LAST_INDEX + 1))

# The HIGHEST index, not the last line's: once a collision has happened the file is no longer
# sorted, and numbering from the tail hands out an index that already exists further up.

# The helper stamps the time itself. An agent writing its own stamp is guessing — a future stamp
# blanks the app's time-on-task display, and one was observed 10 hours ahead of the entry it sat on.
STAMP="$(date +'%Y-%m-%d %H:%M')"

STAGED_ENTRY="$(mktemp)" || { echo "channel-append.sh: cannot create a temp file" >&2; exit 4; }

# The leading newline is the whole requirement, and it is not cosmetic: the parser matches its header
# regex per line with no lookback, so an entry is read iff its header BEGINS A LINE. Starting with a
# newline guarantees that whether or not the channel ended in one. There is no blank-line rule — that
# was believed briefly on 2026-08-13 and disproved by reading the parser.
{
  printf '\n## [%s] FROM %s — %s — %s\n\n' "$NEXT_INDEX" "$AUTHOR" "$STAMP" "$SUBJECT"
  cat "$BODY_FILE"
  printf '\n'
} > "$STAGED_ENTRY" || { rm -f "$STAGED_ENTRY"; echo "channel-append.sh: could not stage the entry" >&2; exit 4; }

# ONE append of a fully-formed entry. The entry is built in a temp file first so that exactly one
# write() reaches the channel: a writer that emits header and body separately is the shape that let
# another author's header land in the middle of an entry.
if ! cat "$STAGED_ENTRY" >> "$CHANNEL"; then
  rm -f "$STAGED_ENTRY"
  echo "channel-append.sh: the append FAILED — nothing was written" >&2
  exit 4
fi

rm -f "$STAGED_ENTRY"
# ---- end critical section ---------------------------------------------------------------------

release_lock
echo "$NEXT_INDEX"

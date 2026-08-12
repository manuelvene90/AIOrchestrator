#!/usr/bin/env bash
# AI Orchestrator — the voice a hook uses when it CANNOT DECIDE.
#
# WHY THIS EXISTS. Every enforcement hook here shells out to extract the payload, and on 2026-08-11
# this machine spent hours at its commit limit: forks failed, python3 could not start, and bash
# itself dumped core twice. A hook that cannot run its extractor cannot evaluate its predicate — and
# each one silently ALLOWED, which is indistinguishable from deciding the call was fine. Fifteen
# guards looked armed and were not, and nothing anywhere recorded that they had stopped working.
#
# The rule this file implements: A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS.
#
# Both halves are deliberate.
#
#   ALLOWS, because hooks ADVISE and the app ENFORCES at the point of effect. Every session runs as
#   the same OS user as the app, so a session that wanted to could delete the flag, edit the hook, or
#   unwire it from settings.json. A guard here restrains an honest session and stops nobody else, so
#   inventing a denial it cannot justify would wedge the honest one — which is exactly how a
#   respawned supervisor was once denied its own watcher and could never be woken again.
#
#   SAYS SO, because silent consent is the failure that cost the evening.
#
# WHERE THE LINE ACTUALLY GOES, AND WHO ACTUALLY READS IT — CORRECTED. An earlier version of this
# comment said the line goes to a log "the app already tails and the UI already shows". THAT IS
# FALSE. It was checked afterwards: the only code touching orchestrator.log.jsonl is the WRITER, the
# UI panel is fed by an in-process event rather than by reading the file, there is no watcher on it,
# and the only other mention is Telegram alert text telling the owner to open it by hand.
#
# So this is a FORENSIC RECORD, not a notification. It is worth writing — it would have shortened a
# night that was spent proving the guards had stopped working — but nobody is told. Delivery is a
# separate deliverable on the ledger and needs a decision about what the owner should see. Until then,
# do not build anything on the assumption that writing here reaches a human.
#
# NOT Telegram: the owner cannot act on "a hook could not parse its input", and alerts they cannot act
# on are the thing this system exists to prevent. NOT stderr, which nothing reads.
#
# ONE COPY, sourced by every hook, because three hooks agreeing by hand is three chances to drift —
# and this file exists because two of them had already drifted into opposite behaviour for the same
# inability.
#
# It must never be the reason a call fails: every path here returns 0.

# Suppression window. The supervisor hook is bounded by the ten-minute question window, but the
# reviewer hook has NO flag gate — it would log for the life of a session, 269 bytes per undecidable
# call. A guard's voice must not fill the disk the guard is protecting.
AIORCH_LOG_REPEAT_SECONDS=${AIORCH_LOG_REPEAT_SECONDS:-300}

# The app rotates at 8 MB and drops writes below 512 MB free (both exist because this machine hit 0%
# free). This append bypasses both, so it enforces the size ceiling itself and simply stops.
AIORCH_LOG_MAX_BYTES=${AIORCH_LOG_MAX_BYTES:-8388608}

# Names WHICH predicate could not be evaluated and WHY. "could not extract a tool name from the
# payload" is actionable; "hook error" is the silence again.
aiorch_log_undecidable() {
  local predicate="$1" reason="$2" hook_name log_file stamp orch_folder suppress_file safe_orch log_size

  # No orchestration id means no log to write to — a hook running outside a session, e.g. in a test.
  if [ -z "${AIORCH_ID:-}" ]; then
    return 0
  fi

  orch_folder="$HOME/.claude/supervision/$AIORCH_ID"

  # Never CREATE the orchestration folder from here. If it is not there this is not a live
  # orchestration, and a hook inventing state the app owns is worse than a missing log line.
  if [ ! -d "$orch_folder" ]; then
    return 0
  fi

  log_file="$orch_folder/orchestrator.log.jsonl"

  # Size ceiling, checked before writing. The app owns rotation; this only declines to grow a file
  # past the point where the app would have rotated it.
  log_size=$(wc -c < "$log_file" 2>/dev/null || printf 0)

  if [ "${log_size:-0}" -ge "$AIORCH_LOG_MAX_BYTES" ] 2>/dev/null; then
    return 0
  fi

  hook_name=$(basename "${BASH_SOURCE[1]:-hook}" 2>/dev/null || printf 'hook')

  # ONE LINE PER PREDICATE PER WINDOW. The same inability repeats on every tool call for as long as
  # the cause lasts, and a hundred identical lines say nothing the first one did not.
  suppress_file="$orch_folder/.hook-log-$hook_name-$(printf '%s' "$predicate" | tr -c 'a-zA-Z0-9' '-')"

  if [ -f "$suppress_file" ]; then
    local now_seconds last_seconds age_seconds

    now_seconds=$(printf '%(%s)T' -1)
    last_seconds=$(stat -c %Y "$suppress_file" 2>/dev/null || printf 0)
    age_seconds=$(( now_seconds - ${last_seconds:-0} ))

    # A failed `stat` leaves age enormous and the line is written. Deliberate: `stat` needs a fork,
    # and a machine that cannot fork is precisely when this record is worth having.
    if [ "$age_seconds" -lt "$AIORCH_LOG_REPEAT_SECONDS" ] 2>/dev/null; then
      return 0
    fi
  fi

  # printf's %(...)T is a BASH BUILTIN — no fork. That matters here more than anywhere: the condition
  # this file reports on is a machine that cannot fork, and `date` failing wrote an empty timestamp
  # into a field the app parses as a date.
  #
  # TZ=UTC IS NOT OPTIONAL. The builtin formats in LOCAL time while the format string below appends
  # `.0000000Z`, which asserts UTC — so the first version of this fix traded a fork for a stamp two
  # hours ahead, wearing a Z. Every hook-written line would have been offset from every app-written
  # line in the same file and labelled as though it were not.
  #
  # That is the worst field to be wrong in here. This log is read only by a human reconstructing an
  # incident afterwards, so the timestamp is the one thing the file exists to provide — and this repo
  # has already been bitten by a stamp two hours out, which is why the app blanks a future stamp
  # rather than trusting it.
  stamp=$(TZ=UTC printf '%(%Y-%m-%dT%H:%M:%S)T' -1 2>/dev/null)

  if [ -z "$stamp" ]; then
    stamp="1970-01-01T00:00:00"
  fi

  # $AIORCH_ID DOES reach the JSON line. The app validates it on the way in, but that guarantee lives
  # three files away and for unrelated reasons — so it is made locally here instead of asserted. A
  # comment that overstates a defence is how the next reader stops checking.
  safe_orch=$(printf '%s' "$AIORCH_ID" | tr -c 'a-zA-Z0-9._-' '-')

  printf '{"ts":"%s.0000000Z","orch":"%s","level":"Warning","message":"%s could not evaluate %s (%s) — ALLOWED the call; this guard is not in force"}\n' \
    "$stamp" "$safe_orch" "$hook_name" "$predicate" "$reason" >> "$log_file" 2>/dev/null

  : > "$suppress_file" 2>/dev/null

  return 0
}

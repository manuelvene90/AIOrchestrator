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
#   SAYS SO, because silent consent is the failure that cost the evening. The line goes to the
#   orchestration log, which the app already tails and the UI already shows. NOT to Telegram: the
#   owner cannot act on "a hook could not parse its input", and alerts they cannot act on are the
#   thing this system exists to prevent. NOT to stderr, which nothing reads.
#
# ONE COPY, sourced by every hook, because three hooks agreeing by hand is three chances to drift —
# and this file exists because two of them had already drifted into opposite behaviour for the same
# inability.
#
# It must never be the reason a call fails: every path here returns 0.

# Names WHICH predicate could not be evaluated and WHY. "could not extract a tool name from the
# payload" is actionable; "hook error" is the silence again.
aiorch_log_undecidable() {
  local predicate="$1" reason="$2" hook_name log_file stamp

  # No orchestration id means no log to write to — a hook running outside a session, e.g. in a test.
  if [ -z "${AIORCH_ID:-}" ]; then
    return 0
  fi

  log_file="$HOME/.claude/supervision/$AIORCH_ID/orchestrator.log.jsonl"

  # Never CREATE the orchestration folder from here. If it is not there this is not a live
  # orchestration, and a hook inventing state the app owns is worse than a missing log line.
  if [ ! -d "$HOME/.claude/supervision/$AIORCH_ID" ]; then
    return 0
  fi

  hook_name=$(basename "${BASH_SOURCE[1]:-hook}")

  # Round-trip format the app's reader already parses. If `date` itself cannot fork — the very
  # condition this file reports on — the stamp is empty rather than the line being lost.
  stamp=$(date -u +'%Y-%m-%dT%H:%M:%S.0000000Z' 2>/dev/null)

  # No user-controlled text reaches this line: the predicate and reason are fixed strings chosen by
  # the caller, so there is nothing to escape and no way to inject a broken record into a file the
  # app is tailing.
  printf '{"ts":"%s","orch":"%s","level":"Warning","message":"%s could not evaluate %s (%s) — ALLOWED the call; this guard is not in force"}\n' \
    "$stamp" "$AIORCH_ID" "$hook_name" "$predicate" "$reason" >> "$log_file" 2>/dev/null

  return 0
}

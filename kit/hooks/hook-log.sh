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
#   ALLOWS, because hooks ADVISE and the app ENFORCES at the point of effect. Every session runs as
#   the same OS user as the app, so a guard here restrains an honest session and stops nobody else;
#   inventing a denial it cannot justify would wedge the honest one.
#
#   SAYS SO, because silent consent is the failure that cost the evening.
#
# THE HOOK DROPS A MARKER. THE APP WRITES THE RECORD. That split is the whole design, and the reason
# is structural rather than stylistic:
#
#   - The app's log panel is fed by an IN-PROCESS event. A hook is a separate process and can never
#     raise it, so anything a hook writes to the log file itself is invisible until a human goes
#     looking — and a record nobody sees until they suspect a problem preserves exactly the property
#     we are trying to remove, since a guard that has stopped working looks like one that is working.
#   - The app's writer already owns rotation at 8 MB and a low-disk drop below 512 MB free, both added
#     after this machine hit 0%. An earlier version of this file carried its own copy of the 8 MB
#     number — two copies of one threshold, in two languages — and could not honour the disk rule at
#     all, because that needs a DriveInfo the shell does not have.
#
# So this writes a FACT and nothing else: the marker exists or it does not. No timestamp, no JSON, no
# size ceiling, no suppression window. Everything this file got wrong was complexity it should never
# have been carrying; the app picks the marker up on the tick it already runs, writes the entry
# through its own guarded writer, surfaces it, and deletes it.
#
# It must never be the reason a call fails: every path here returns 0.

# Names WHICH predicate could not be evaluated and WHY. "could not extract a tool name from the
# payload" is actionable; "hook error" is the silence again.
aiorch_log_undecidable() {
  local predicate="$1" reason="$2" hook_name hook_path orch_folder member fingerprint

  # No orchestration id means no orchestration to tell — a hook running outside a session, e.g. in a
  # test.
  if [ -z "${AIORCH_ID:-}" ]; then
    return 0
  fi

  orch_folder="$HOME/.claude/supervision/$AIORCH_ID"

  # Never CREATE the orchestration folder from here. If it is not there this is not a live
  # orchestration, and a hook inventing state the app owns is worse than a missing record.
  if [ ! -d "$orch_folder" ]; then
    return 0
  fi

  hook_path="${BASH_SOURCE[1]:-}"
  hook_name=$(basename "${hook_path:-hook}" 2>/dev/null || printf 'hook')

  # WHO tripped it and WHICH COPY of the script did. Without these two, a session deliberately
  # exercising a guard on its own branch is indistinguishable from the shipped guard failing in
  # production — and on 2026-08-13 that cost a supervisor a reviewer's round: three alerts named
  # `reviewer-readonly-check.sh`, but the reason text existed only in an implementer's UNMERGED
  # worktree copy, and nothing in the alert could say so.
  #
  # The md5 is the field that settles it: compared against the installed copy it separates "a branch
  # is being tested" from "the guard you are relying on is not running".
  #
  # Both degrade to EMPTY rather than to a sentinel or an error. A machine that cannot fork may well
  # have no md5sum either, and this function must never be the reason a call fails — every path here
  # still returns 0, and the reader treats an empty line as an absent field.
  member="${AIORCH_MEMBER:-}"
  fingerprint=$(md5sum "$hook_path" 2>/dev/null | cut -d' ' -f1)

  # One marker, overwritten rather than appended: the app deletes it once recorded, so what matters
  # is that ONE exists to be found. Repeating the same inability a hundred times says nothing the
  # first one did not, and the app is where any judgement about repetition belongs — it now has one.
  printf '%s\n%s\n%s\n%s\n%s\n' "$hook_name" "$predicate" "$reason" "$member" "$fingerprint" > "$orch_folder/.guard-not-in-force" 2>/dev/null

  return 0
}

#!/usr/bin/env bash
# AI Orchestrator — a question STOPS the supervisor, exactly as it does in a terminal.
#
# WHY THIS EXISTS: the supervisor asked the owner a question and then carried on working — briefing
# implementers, running commands, changing state. By the time the owner answered, the answer landed
# against a world that had already moved, and the conversation became incoherent: five questions in
# flight, replies two topics behind, no way to tell which were still live.
#
# Queueing its messages was tried first and was the WRONG fix: it hid the symptom while the
# supervisor kept working. The owner was blunt about it — "this means that the sup is moving in the
# background. This should not happen. The sup should wait for my answer."
#
# So the app raises <orch>/.awaiting-answer the moment a question reaches the owner, and this hook
# refuses every tool call while it is there. The session has nothing left to do but end its turn and
# wait — which is precisely the terminal's behaviour. The app deletes the flag as soon as the owner
# says anything, and expires it after 10 minutes so a silent owner cannot brick the orchestration.
#
# Only supervisor sessions are affected (AIORCH_ROLE is set by the spawner). Any unexpected
# condition ALLOWS the call — an enforcement bug must never wedge a session.

set -u

if [ "${AIORCH_ROLE:-}" != "supervisor" ]; then
  exit 0
fi

if [ -z "${AIORCH_ID:-}" ]; then
  exit 0
fi

FLAG_FILE="$HOME/.claude/supervision/$AIORCH_ID/.awaiting-answer"

if [ ! -f "$FLAG_FILE" ]; then
  exit 0
fi

# WHAT THIS BLOCKS, AND WHY IT IS NOT EVERYTHING ANY MORE.
#
# The rule is that the WORLD must not change under an owner who is deciding — not that the session
# must be paralysed. Denying literally every call was measured as harmful on 2026-08-11: it blocked
# reads, it blocked the PLAN.md write that the Stop hook simultaneously demanded (a true deadlock,
# ~20 minutes of a live supervisor producing nothing), and it held a written answer away from an
# implementer that had been idle for eight minutes waiting on it.
#
# So: anything that cannot change the world is allowed, and everything that can is still denied.

INPUT=$(cat 2>/dev/null) || exit 0

# Same extraction as the reviewer hook — python3 when present, and a failed read ALLOWS the call,
# because an enforcement bug must never wedge a session.
TOOL=$(printf '%s' "$INPUT" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("tool_name",""))' 2>/dev/null)

if [ -z "$TOOL" ]; then
  exit 0
fi

# READS change nothing. This was pure collateral damage.
case "$TOOL" in
  Read|Grep|Glob|NotebookRead|WebFetch|WebSearch|TodoWrite|Monitor|BashOutput|TaskOutput|TaskList|TaskGet)
    exit 0 ;;
esac

TARGET=$(printf '%s' "$INPUT" | python3 -c 'import json,sys; d=json.load(sys.stdin).get("tool_input",{}); print(" ".join(str(d.get(k,"")) for k in ("file_path","path","command","notebook_path")))' 2>/dev/null)

deny() {
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"YOU ASKED THE OWNER A QUESTION — STOP AND WAIT. %s Whatever you change now makes their answer arrive against a different world, which is exactly what made previous conversations incoherent. END YOUR TURN: your monitor wakes you the moment they reply and this clears automatically. Reading, updating PLAN.md, and answering a member that is already waiting on you all remain allowed — everything else waits."}}\n' "$1"
  exit 0
}

SUPERVISION="$HOME/.claude/supervision/$AIORCH_ID"

# THE LEDGER is a record of work that already happened, not a change to what the owner is deciding —
# and the Stop hook can demand it, so it must never be unreachable.
case "$TARGET" in
  *"$AIORCH_ID/PLAN.md"*|*"$AIORCH_ID"/PLAN.md*) exit 0 ;;
esac
case "$TARGET" in
  *PLAN.md*)
    if printf '%s' "$TARGET" | grep -q "supervision/$AIORCH_ID"; then
      exit 0
    fi ;;
esac

# A MEMBER CHANNEL is allowed only when that member is ALREADY WAITING on a verdict.
#
# UNBLOCKING IS NOT MOVING IN THE BACKGROUND; BRIEFING IS. Answering someone who has filed something
# and is sitting idle releases work that was already in flight before the question. Writing to a
# member that is NOT waiting starts new work while the owner decides, which is the precise behaviour
# the owner objected to: "this means that the sup is moving in the background. This should not
# happen. The sup should wait for my answer."
#
# The app decides who is waiting and publishes it in .awaiting-verdict — this hook only looks the
# answer up. An earlier version re-derived it here by reading the last "FROM" line of the channel,
# and that copy drifted from the app's within the hour: an app NUDGE lands between a member's report
# and the supervisor's reply, so the last line read "app" and the hook denied exactly the reply it
# exists to allow. One rule, one place, and this is not the place.
MEMBER_ID=$(printf '%s' "$TARGET" | grep -oE "supervision/$AIORCH_ID/(imp|rev)-[0-9]+/channel\.md" | head -1 | grep -oE '(imp|rev)-[0-9]+')

if [ -n "$MEMBER_ID" ]; then
  AWAITING_FILE="$SUPERVISION/.awaiting-verdict"

  if [ -f "$AWAITING_FILE" ] && grep -qx "$MEMBER_ID" "$AWAITING_FILE" 2>/dev/null; then
    exit 0
  fi

  deny "$MEMBER_ID is not waiting on a verdict — writing to it now is briefing new work, not unblocking someone."
fi

deny "Do not run anything, do not brief anyone, do not keep working."

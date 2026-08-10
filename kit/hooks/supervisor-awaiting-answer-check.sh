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

cat <<'JSON'
{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"YOU ASKED THE OWNER A QUESTION — STOP AND WAIT. Do not run anything, do not brief anyone, do not keep working: whatever you change now makes their answer arrive against a different world, which is exactly what made previous conversations incoherent. END YOUR TURN. Your monitor wakes you the moment they reply, and this block clears automatically then. If you truly cannot wait, the thing to do is not to work around this — it is to not have asked yet."}}
JSON

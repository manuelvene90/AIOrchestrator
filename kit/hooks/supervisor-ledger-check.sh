#!/usr/bin/env bash
# AI Orchestrator — turn-end enforcement for the task ledger.
#
# WHY THIS EXISTS: PLAN.md was the only artifact in the supervision protocol whose omission produced
# NO signal. The same supervisor session that skipped it at four consecutive boundaries never once
# skipped /style-check — because that one is blocked by a Stop hook. The difference is enforcement,
# not diligence, so the ledger gets the same lever.
#
# The app raises <supervision>/<orch-id>/.ledger-behind when a supervisor posts a verdict into an
# implementer channel without touching PLAN.md. This hook simply refuses to let that turn end.
#
# Only supervisor sessions are affected (AIORCH_ROLE is set by the spawner); everything else exits
# silently, and any unexpected condition ALLOWS the turn — an enforcement bug must never wedge a
# session.

set -u

if [ "${AIORCH_ROLE:-}" != "supervisor" ]; then
  exit 0
fi

if [ -z "${AIORCH_ID:-}" ]; then
  exit 0
fi

FLAG_FILE="$HOME/.claude/supervision/$AIORCH_ID/.ledger-behind"

if [ ! -f "$FLAG_FILE" ]; then
  exit 0
fi

# DEADLOCK GUARD. A stop hook must never demand an action that another hook currently forbids.
#
# The awaiting-answer PreToolUse hook denies tool calls while a question is with the owner, and this
# hook refuses to end the turn until PLAN.md is written — so with both flags up, the write this hook
# demands is the write that one forbids, and the session spins until the flag expires. Measured on a
# live supervisor on 2026-08-11: ~20 minutes and six attempts across two tools, producing nothing
# but identical refusals.
#
# Deferring costs nothing: the flag stays raised, and the ledger is enforced at the next turn end,
# once compliance is possible again. The enforcement is delayed, never skipped.
if [ -f "$HOME/.claude/supervision/$AIORCH_ID/.awaiting-answer" ]; then
  exit 0
fi

PLAN_FILE="$HOME/.claude/supervision/$AIORCH_ID/PLAN.md"

cat <<JSON
{"decision":"block","reason":"TASK LEDGER BEHIND. You posted a verdict to an implementer channel without updating $PLAN_FILE, so the owner's progress bar is now wrong. Update the ledger to match what is actually done (one task per line: - [ ] open, - [>] in progress, - [x] done, - [!] blocked), then end your turn. This block clears automatically once the file is written."}
JSON

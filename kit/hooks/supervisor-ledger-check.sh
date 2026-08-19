#!/usr/bin/env bash
# AI Orchestrator — turn-end enforcement for the task ledger.
#
# WHY THIS EXISTS: PLAN.md was the only artifact in the supervision protocol whose omission produced
# NO signal. The same supervisor session that skipped it at four consecutive boundaries never once
# skipped /style-check — because that one is blocked by a Stop hook. The difference is enforcement,
# not diligence, so the ledger gets the same lever.
#
# The app raises <supervision>/<orch-id>/.ledger-behind when the ledger falls behind what has
# happened — a supervisor posting a verdict into an implementer channel, or the OWNER asking for
# something — without PLAN.md being touched. This hook simply refuses to let that turn end.
#
# IT COVERS THE SOLO TOO, and for two hours it did not. The check was `role != supervisor -> exit 0`,
# so the single session of a BASIC orchestration — which owns PLAN.md by its own role command,
# because there is no supervisor to own it — was structurally exempt from the one lever that makes
# this artifact get maintained. It then did what an unenforced protocol step gets done: the owner
# asked for six things over two hours and the bar read 3/3 the whole time. Their reading of it, and
# the reason this is a hook change and not an apology (2026-08-14):
#
#   "you are just a session like any other. If you failed to upgrade the plan file any other future
#    session also might fail. Fix this permanently for any future orchestration or solo session."
#
# Members are still exempt, and that is not an oversight: an implementer or a reviewer does not own
# the ledger and must not edit it, so blocking their turn would demand a write the protocol forbids.
#
# Any unexpected condition ALLOWS the turn — an enforcement bug must never wedge a session.
#
# THE MARKER LIST BELOW IS A COPY, and it is the one copy that cannot import the C# original: this is
# bash, and PlanLedger_Markers is the single definition everything else builds from. It lost `- [-]`
# once already, because that marker was added to the parser additively and nothing updated the lists
# that had copied it. `LedgerLegendTests` reads THIS FILE and fails if the five stop matching, which
# is the only join available between the two languages.

set -u

case "${AIORCH_ROLE:-}" in
  supervisor|solo) ;;
  *) exit 0 ;;
esac

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

# THE REASON NAMES BOTH DEBTS, because the hook cannot tell them apart and guessing would send a
# solo — which posts no verdicts — hunting for an implementer channel it does not have.
cat <<JSON
{"decision":"block","reason":"TASK LEDGER BEHIND. Work happened or the owner asked for something, and $PLAN_FILE was not touched — so the owner's progress bar is now wrong. Update it (one task per line: - [ ] open, - [>] in progress, - [x] done, - [!] blocked, - [?] blocked on the owner, - [-] not doing), and if this was an owner request, add its row to the OWNER REQUESTS table as well. An owner message that needs no new task still needs that row — writing it is what clears this. Then end your turn; the block clears automatically once the file is written."}
JSON

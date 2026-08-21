#!/usr/bin/env bash
# AI Orchestrator — turn-end enforcement for "run to the end".
#
# WHY THIS EXISTS: the owner told sessions not to stop mid-endeavour, repeatedly, and they kept
# stopping. Their words, 2026-08-20: "no matter how many times i tell it not to get stuck and keep
# going, it will keep getting stuck, so I fear the solution we implemented might not be enough."
#
# They were right, and the ledger hook next door already explains why:
#
#   "The same supervisor session that skipped it at four consecutive boundaries never once skipped
#    /style-check — because that one is blocked by a Stop hook. The difference is enforcement, not
#    diligence."
#
# A role command is prose. Prose is what had already failed — twice in one evening for the fan-out
# rule, and again here. So "keep going" gets the same lever the ledger got: a session with open work,
# nothing blocked on the owner and no question outstanding simply cannot end its turn.
#
# THE ESCAPES ARE THE SESSION'S OWN, and every one of them is an honest statement rather than a way
# out: finish the work, mark a line `- [?]` because it truly waits on the owner, or ask them a
# QUESTION. Nothing here can be satisfied by pretending.
#
# Any unexpected condition ALLOWS the turn. An enforcement bug must never wedge a session — the same
# rule the ledger hook states, and the reason every branch below fails open.

set -u

# Members are exempt: an implementer or reviewer reports to the supervisor and its turn ending IS
# the report. Only the roles that own an endeavour end-to-end are held to it.
case "${AIORCH_ROLE:-}" in
  supervisor|solo) ;;
  *) exit 0 ;;
esac

if [ -z "${AIORCH_ID:-}" ]; then
  exit 0
fi

ORCH_FOLDER="$HOME/.claude/supervision/$AIORCH_ID"
PLAN_FILE="$ORCH_FOLDER/PLAN.md"
CHANNEL_FILE="$ORCH_FOLDER/owner-channel.md"

if [ ! -f "$PLAN_FILE" ]; then
  exit 0
fi

# ONE DEMAND AT A TIME. The ledger hook asks for a PLAN.md write; asking for that AND for more work
# in the same breath gives a session two instructions and no order to do them in. The ledger speaks
# first, this one at the next turn end. Enforcement delayed, never skipped — the same reasoning the
# ledger hook uses to defer to the awaiting-answer hook.
if [ -f "$ORCH_FOLDER/.ledger-behind" ]; then
  exit 0
fi

# A question is already with the owner. Waiting is not stopping.
if [ -f "$ORCH_FOLDER/.awaiting-answer" ]; then
  exit 0
fi

# THE OWNER IS SITTING AT THIS TERMINAL, so there is no silence for them to notice.
#
# Terminal mode ("/pc") deliberately turns the awaiting-answer block OFF and tells the session to ask
# in its own prompt, in prose, rather than with QUESTION:/OPTION: lines — nothing is being texted and
# there are no buttons to tap. That makes BOTH escapes above unavailable BY DESIGN. So a session that
# asked them something face to face and is waiting for the answer got blocked for doing exactly what
# it was told, and its only way out was to backdate a `- [?]` onto a line that is not really blocked —
# which then exempts the WHOLE file from this hook, because that check is file-wide.
#
# The owner, 2026-08-21, having watched it happen repeatedly: *"I also keep getting this in sessions
# ... Not sure if that is right but happens quite often."* It was not right, and this was the case.
#
# THIS IS NOT A NEW POLICY, it is the one the app already made. The same flag already silences this
# orchestration's watcher and already lifts the awaiting-answer block; this hook simply did not know
# about it, because it and terminal mode were built days apart. And the premise of the rule does not
# hold here: a stop costs the owner their ATTENTION, because they have to notice the silence and prod
# a session that went quiet. When they are in the chair they watch the turn end, and answering costs
# them a keystroke.
#
# DERIVED, NEVER AUTHORED (MeetingFlag_Marker): the app re-syncs this file on every presence change,
# on close, and for every session at startup, so a flag left behind by a crash is cleared the moment
# the app returns. It cannot quietly grant one session a permanent exemption.
if [ -f "$ORCH_FOLDER/.meeting" ]; then
  exit 0
fi

# NOTHING LEFT TO DO — the endeavour really is finished, so the turn may end. This is the exit that
# makes the rule livable: it is not "never stop", it is "never stop with work still open".
# `grep -c` PRINTS 0 AND EXITS 1 when it matches nothing, so an `|| echo 0` here appends a SECOND
# zero and the comparison below silently fails on the one input that matters: a finished ledger.
# Caught by running the harness rather than by reading it.
OPEN_LINES="$(grep -cE '^[[:space:]]*- \[( |>)\]' "$PLAN_FILE" 2>/dev/null || true)"

if [ "${OPEN_LINES:-0}" -le 0 ] 2>/dev/null; then
  exit 0
fi

# SOMETHING GENUINELY WAITS ON THE OWNER. `- [?]` is the marker that says so, and a session that has
# marked one is not stalling — it is blocked, which is the first of the three legitimate reasons.
if grep -qE '^[[:space:]]*- \[\?\]' "$PLAN_FILE" 2>/dev/null; then
  exit 0
fi

# THE SESSION HAS JUST ASKED. Only the LAST entry counts: an old question further up the channel was
# answered long ago, and treating it as current would let one ancient QUESTION exempt every turn
# from here to the end of the orchestration.
if [ -f "$CHANNEL_FILE" ]; then
  LAST_ENTRY="$(awk '/^## \[/{buf=""} {buf = buf $0 "\n"} END{printf "%s", buf}' "$CHANNEL_FILE" 2>/dev/null || true)"

  if printf '%s' "$LAST_ENTRY" | grep -qE '^QUESTION:' 2>/dev/null; then
    exit 0
  fi
fi

cat <<JSON
{"decision":"block","reason":"DO NOT STOP — $OPEN_LINES ledger line(s) are still open and nothing is waiting on the owner. The default is to run the endeavour to the end (their directive, 2026-08-20): finishing a phase and reporting is NOT a turn boundary, it only feels like one. Carry straight on with the next open line in $PLAN_FILE. If you truly cannot proceed, say so honestly instead: mark the line '- [?] <task> - blocked on: <what you need from them>' if it really waits on the OWNER, or end your channel entry with a 'QUESTION:' line and 2-4 'OPTION:' lines. Marking every line done to escape this is a lie the owner will read on their phone. If they told you to stop, or asked for step-by-step, mark the remaining lines '- [-] not doing' with the reason and this clears."}
JSON

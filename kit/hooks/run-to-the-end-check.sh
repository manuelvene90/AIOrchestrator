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

  # WAITING ON SOMETHING ALREADY RUNNING, which is not the same as giving up and is the case that
  # made this hook actively harmful (owner, 2026-08-21: it "keeps intervening constantly, essentially
  # preventing solo from responding to me").
  #
  # What happened, from that session's own transcript: it had one open line, was waiting on a full
  # suite it had already started in the background, and correctly refused to mark `- [?]` because
  # nothing owner-side blocked it. Its three sanctioned escapes were finish / blocked-on-owner /
  # ask a question, and NONE of them describes waiting. So, obeying, it converted a free wait into a
  # NINE-MINUTE foreground poll -- and a session inside one blocking tool call cannot pick up the
  # owner's messages, which is the mechanism behind their complaint. Their question sat unanswered
  # for over ten minutes and they had to interrupt the wait by hand.
  #
  # ENDING THE TURN IS HOW THE OWNER REACHES YOU. A session waiting on a background job is woken by
  # the job and by its own monitor; holding the turn open buys nothing and costs the owner their
  # reply. So waiting is a legitimate reason to stop, and it is declared where they can see it.
  #
  # DECLARED, NOT INFERRED, and deliberately so: bash cannot verify that a build is really running,
  # so this escape rests on the session saying it in the CHANNEL -- in front of the owner, next to
  # the app's own view of when it last wrote. That is the same bargain the QUESTION: escape above
  # makes, and the same one the whole kit makes (decision 21: hooks advise, and honesty is visible).
  # It is self-clearing for free: the moment the session writes anything else, that entry is no
  # longer the last one and the escape is gone.
  #
  # Read the way every marker in this kit is read: in the SUBJECT anywhere, or at the START of a
  # body line. Mid-sentence prose about waiting is discussion, not a declaration.
  #
  # THE TRAILING BOUNDARY IS LOAD-BEARING: "WAITING ONLY" CONTAINS "WAITING ON". Without it, a line
  # opening "WAITING ONLY for the reviewer" would silently take this exit -- the identical shape to
  # "MUTATION WINDOW CLOSED" containing "WINDOW CLOSED", which this kit already paid for once and
  # which its own role commands warn about. Anything that is not a letter counts as the boundary, so
  # a colon or a dash after the marker still reads as a declaration.
  FIRST_LINE="$(printf '%s' "$LAST_ENTRY" | head -n1)"

  case "$FIRST_LINE" in
    '## ['*) LAST_SUBJECT="$FIRST_LINE" ;;
    *)       LAST_SUBJECT="" ;;
  esac

  if printf '%s' "$LAST_SUBJECT" | grep -qE 'WAITING ON([^A-Za-z]|$)' 2>/dev/null; then
    exit 0
  fi

  if printf '%s' "$LAST_ENTRY" | grep -qE '^WAITING ON([^A-Za-z]|$)' 2>/dev/null; then
    exit 0
  fi
fi

cat <<JSON
{"decision":"block","reason":"DO NOT STOP — $OPEN_LINES ledger line(s) are still open, and none of them is marked as blocked on the owner. The default is to run the endeavour to the end (their directive, 2026-08-20): finishing a phase and reporting is NOT a turn boundary, it only feels like one. Carry straight on with the next open line in $PLAN_FILE.\nIf you truly cannot proceed, say so honestly instead. Every one of these is a STATEMENT, not a way out, and each clears this block:\n  • WAITING on something you already started — a build, a suite, a sub-agent: put 'WAITING ON <what>' in your channel entry's SUBJECT, or at the START of a body line. Then END THE TURN. Do NOT poll it in the foreground: a session sitting inside one long tool call cannot read the owner's messages, and ending the turn is how they reach you — the job and your monitor both wake you.\n  • Blocked on a MACHINE rather than on them: mark the line '- [!] <task>'.\n  • Blocked on the OWNER: mark it '- [?] <task> - blocked on: <what you need from them>'. This is the only one that puts it on their plate, so do not use it for a build.\n  • You need them to CHOOSE: end your channel entry with a 'QUESTION:' line and 2-4 'OPTION:' lines.\n  • They told you to stop, or asked for step-by-step: mark the rest '- [-] not doing' with the reason.\nMarking every line done to escape this is a lie the owner will read on their phone."}
JSON

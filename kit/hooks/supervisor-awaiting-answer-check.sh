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

# A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS — see hook-log.sh for both halves.
# Sourced defensively: a missing helper must not be the thing that breaks a session, so the calls
# below degrade to no-ops rather than to failures.
# DEFINED FIRST, UNCONDITIONALLY, then overridden by the real one. The stub used to live in an
# `else`, so it covered a MISSING helper only — a helper that EXISTS but is truncated or empty left
# the function undefined and the call failed to stderr, the stream this feature's own header says
# nobody reads. That window is real rather than theoretical: KitAssets_Installer overwrites
# ~/.claude/hooks at every app start, so a hook firing during that copy sees a partial file.
aiorch_log_undecidable() { return 0; }

if [ -f "$(dirname "$0")/hook-log.sh" ]; then
  . "$(dirname "$0")/hook-log.sh" 2>/dev/null || true
fi

if ! INPUT=$(cat 2>/dev/null); then
  aiorch_log_undecidable "any rule" "the payload could not be read from stdin"
  exit 0
fi

# python3 when present. A failed extraction ALLOWS the call — an enforcement bug must never wedge a
# session — but it no longer does so silently: on 2026-08-11 this exact branch was taken for hours
# while the machine was at its commit limit and python3 could not fork, and nothing recorded that
# every guard had stopped working.
TOOL=$(printf '%s' "$INPUT" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("tool_name",""))' 2>/dev/null)

if [ -z "$TOOL" ]; then
  aiorch_log_undecidable "which tool is being called" "no tool name could be extracted from the payload"
  exit 0
fi

# The split is by DECIDABILITY, not by trust, and not by tool name.
#
# A tool that carries an exact file_path can be judged: one target, one decision. A COMMAND LINE
# cannot — it is compound, and every exemption written against it is a substring test that a longer
# command satisfies incidentally. That is not a matching bug to be tightened; it is the category.
# One boundary command that appends a brief to a member and then edits the ledger satisfied the
# ledger exemption and was allowed in full, and our own supervisor.md prescribes writing exactly
# that.
#
# So: readers pass, exactly-scoped writers are judged on their path, and everything that can EXECUTE
# is denied — Bash and Monitor alike. Monitor is denied for what it can do rather than for its name,
# which is also what catches the next execution-capable tool nobody has told this hook about. It ran
# arbitrary shell for the life of a session and sat in the read-only list.

deny() {
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"YOU ASKED THE OWNER A QUESTION — STOP AND WAIT. %s Whatever you change now makes their answer arrive against a different world, which is exactly what made previous conversations incoherent. END YOUR TURN: your monitor wakes you the moment they reply and this clears automatically. Reading is allowed; so is updating PLAN.md, or answering a member already waiting on you — with Edit, never Write."}}\n' "$1"
  exit 0
}

# Pure readers: nothing they do can change the world the owner is deciding about.
case "$TOOL" in
  Read|Grep|Glob|NotebookRead|WebFetch|WebSearch|TodoWrite|BashOutput|TaskOutput|TaskList|TaskGet)
    exit 0 ;;
esac

# MONITOR IS ALLOWED, and not because it is safe — it runs arbitrary shell for the life of the
# session, which is why the capability rule below denies everything else that executes.
#
# It is allowed because denying it cannot buy anything and can cost everything. A session that wants
# to work while the owner decides does not need Monitor: it can delete .awaiting-answer, edit this
# script, or remove this hook from settings.json, all in its own tree and as the same OS user. So the
# denial stops nobody who does not want to comply — while for an HONEST session it was total:
# supervisor.md makes arming the persistent Monitor part of booting, and it is the ONLY thing that
# ever wakes a supervisor. A session respawned with this flag still up was denied its watcher, ended
# its turn as instructed, and could never be woken again by anything — a live process, a green pid, a
# healthy card, and an orchestration that would never take another turn.
#
# An advisory guard that can permanently brick the thing it advises has its priorities inverted.
case "$TOOL" in
  Monitor) exit 0 ;;
esac

# Anything not a pure reader and not an exactly-scoped write is refused, whatever it is called.
case "$TOOL" in
  Write|Edit|NotebookEdit) ;;
  Bash) deny "A shell command cannot be scoped to one target, so none is allowed here — use Edit for the ledger or for a member that is waiting." ;;
  *) deny "Only reading, and writing to the ledger or to a member already waiting on you, are allowed while the owner is deciding." ;;
esac

# ONE target, and it is normalised before anything is compared to it. Backslashes are the NATIVE
# spelling on this platform, and matching only forward slashes meant every exemption below failed on
# the path a Windows session actually passes: the ledger write was denied, which re-created the very
# deadlock this branch exists to remove — masked only by the Stop hook's defer added beside it.
TARGET=$(printf '%s' "$INPUT" | python3 -c 'import json,sys; d=json.load(sys.stdin).get("tool_input",{}); print(str(d.get("file_path") or d.get("notebook_path") or d.get("path") or "").replace(chr(92), "/"))' 2>/dev/null)

# THIS ONE USED TO DENY, and it was the odd one out. Three places in this file face the same
# inability — stdin unreadable at the top, no tool name above, no target here — and this one invented
# a refusal while the other two granted silent consent. Same interpreter, same failure, opposite
# conduct, and I wrote all three without noticing they disagreed.
#
# It comes into line rather than the others coming into line with it, because a hook that cannot read
# its input cannot know that denying is safe either. Refusing a call it never understood is a denial
# it cannot justify, and this guard is advisory: the enforcement that must actually hold lives in the
# app, at the point of effect.
if [ -z "$TARGET" ]; then
  aiorch_log_undecidable "which file is being written" "the payload named no file path this hook could read"
  exit 0
fi

SUPERVISION="$HOME/.claude/supervision/$AIORCH_ID"

# THE LEDGER is a record of work that already happened, not a change to what the owner is deciding —
# and the Stop hook can demand it, so it must never be unreachable. Matched as a whole path ending,
# not as a substring anywhere in the value.
case "$TARGET" in
  */supervision/"$AIORCH_ID"/PLAN.md) exit 0 ;;
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
#
# ADVISORY, NOT A BOUNDARY. This file lives where the restrained session can write it, and so does
# this script, and so does the settings.json that decides whether this script runs at all — every
# session runs as the same OS user as the app, so no path is out of reach. A supervisor that wanted
# to could enter itself here. That is accepted deliberately: this guard exists to keep an honest
# session on protocol, and the enforcement that must actually HOLD lives in the app, at the point of
# effect, where a session can only ask. Do not build anything on the assumption that this file is
# trustworthy.
MEMBER_ID=$(printf '%s' "$TARGET" | grep -oE "/supervision/$AIORCH_ID/(imp|rev)-[0-9]+/channel\.md$" | grep -oE '(imp|rev)-[0-9]+')

if [ -n "$MEMBER_ID" ]; then
  # EDIT ONLY. A channel is append-only and Write REPLACES it: a supervisor's Write on
  # imp-3/channel.md once destroyed that member's own boot entry, and it then waited 35 minutes for a
  # brief that was already in its file. supervisor.md forbids it in as many words — so this guard
  # must not be the thing that permits it, and an earlier version of this message actively
  # RECOMMENDED it.
  case "$TOOL" in
    Edit) ;;
    *) deny "A member channel is append-only: use Edit, never $TOOL, which replaces the file and destroys what the member wrote." ;;
  esac

  AWAITING_FILE="$SUPERVISION/.awaiting-verdict"

  if [ -f "$AWAITING_FILE" ] && grep -qx "$MEMBER_ID" "$AWAITING_FILE" 2>/dev/null; then
    exit 0
  fi

  deny "$MEMBER_ID is not waiting on a verdict — writing to it now is briefing new work, not unblocking someone."
fi

deny "That file is neither the ledger nor a member waiting on you."

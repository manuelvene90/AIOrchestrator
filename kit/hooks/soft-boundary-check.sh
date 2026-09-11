#!/usr/bin/env bash
# AI Orchestrator — the SOFT BOUNDARY inside one long turn.
#
# WHY THIS EXISTS, measured on the production VPS on 2026-09-09. The median member turn is 10 model
# calls; the tail runs 60 to 92. **41 % of that night's member tokens sat in turns that ran to the
# print runner's deadline**, and the context inside such a turn climbs to about 200 k — so every
# later call in it is dearer than the one before.
#
# The closing turn is the HARD net and it already exists (Running/ClosingTurn/*). What was missing is
# a chance to stop BEFORE the axe, at a point the session picks rather than one the clock picks. That
# is all this is: past a threshold, ONE advisory into the model's context saying "reach a stable
# point, save what your role saves, write your report".
#
# IT ADVISES, IT NEVER BLOCKS (decision 21: hooks advise, the app enforces at the point of effect).
# There is no permission verdict anywhere in the output, and the last line of every path is `exit 0`
# — including the path that emits the advisory, where the heredoc's own write can fail (stdout closed
# by the caller gives `cat` an EBADF and rc 1). That used to be the script's exit code; an earlier
# version of this header claimed "no exit code but 0 on any path" while that path returned 1, and an
# untrue absolute in a comment is a defect here.
#
# HOW THE ADVISORY REACHES THE MODEL — VERIFIED, not assumed. `hookSpecificOutput.additionalContext`
# from a PreToolUse hook was probed against the installed CLI 2.1.266 on 2026-09-09: the transcript
# records a `hook_additional_context` attachment rendered as
# `<system-reminder>PreToolUse:Bash hook additional context: …</system-reminder>`, and the model
# quoted the probe's magic string back verbatim while the tool call still ran. RE-PROVED 2026-09-10
# on the installed 2.1.267, because the version had moved: a hook emitting a magic string, a
# `claude -p` turn making one Bash call — the model answered with `XYLOPHONE-4G-7731` and the call
# still printed its output.
#
# ═══ THE COUNT IS KEYED ON prompt_id, WHICH IS THE TURN ═══
#
# `session_id` is NOT the turn. `RoleRunnerConfig_Factory.Create_Default` gives `Fresh` to the
# general supervisor alone; implementer, reviewer and solo default to Terminal + Transcript, where
# ONE session spans every turn the member ever takes. Keyed on the session, the latch below latched
# for the life of the session: re-measured 2026-09-10 on the pre-fix script, one session id with
# four 40-call turns produced advisories 0, 1, 0, 0 — once in four turns, and not even in the turn
# that earned it. (The `1` lands on the FIRST call of turn 2 because tool_use_id numbering restarts
# there and, with the field split broken, that id was what the sub-agent gate was comparing.)
#
# `prompt_id` is per TURN in both modes, because the bridge sends one prompt per turn. CAPTURED
# 2026-09-10 against the installed CLI 2.1.267 — a probe PreToolUse hook wrote all five payloads of
# two `claude -p` turns to disk — and these are the three questions it had to answer:
#   • PRESENT on all five, beside session_id and tool_use_id;
#   • STABLE across the calls of one turn — the three Bash calls of turn 1 all carried
#     prompt_id 514d2a46-54bc-4dd5-967c-1d8d51d1d914;
#   • DIFFERENT between two turns of one RESUMED session — session
#     b705a0bf-f889-4a28-bd7e-7b352afd8f63 kept its id across the `--resume` and turn 2's two calls
#     carried bad80642-f45d-448a-bc4d-b854b7d294ca.
# It falls back to `session_id` when absent (an older CLI), which restores the old per-session
# behaviour rather than going silent: advising once per session is worse than advising once per turn
# and better than nothing.
#
# ═══ THE COUNT INCLUDES SUB-AGENTS, THE ADVISORY DOES NOT GO TO THEM ═══
#
# Captured 2026-09-10 in the same probe, a third turn that fanned out: a sub-agent's tool call
# carries the PARENT's `session_id`, the parent's `transcript_path` AND the parent's `prompt_id`
# (session 1802ef2f…, prompt 41dbb6ba… on all three payloads — the main agent's Bash, the main
# agent's Agent call, and the Explore sub-agent's Bash), with only `agent_type` (plus an `agent_id`)
# naming the sub-agent. So keying on prompt_id keeps both properties: their calls cost real tokens
# and count, but telling a read-only Explore agent to commit and write a report is nonsense and
# would burn the once-per-turn latch on the wrong recipient — silencing the advisory in exactly the
# fan-out-heavy turns it exists for. The discriminator is derived rather than guessed: the FIRST
# call of a turn is always the main agent's (a sub-agent can only exist because the main agent called
# the Agent tool, which is itself a counted call in the same turn and arrives here first — captured
# in that order in the probe above), so its `agent_type` is recorded once and every later call in
# that turn is compared against it.
#
# `agent_type` IS OFTEN ABSENT, and that absence is what broke the first version of this script. The
# app passes no `--agent` and the kit ships no agents, so on the sessions it spawns the field is not
# in the payload at all; it appears for a sub-agent, for an `--agent` session, and when a plugin
# supplies a default agent. Both shapes were captured on 2026-09-10, one probe each: with the
# machine's plugins left on, every main-agent payload carried `agent_type` =
# `vibe-framework:vibe-agent`, and with them switched off in the probe's own settings the KEY WAS
# NOT IN THE PAYLOAD AT ALL for the main agent's Bash and Agent calls, while the Explore
# sub-agent's Bash still carried `agent_type` = `Explore`. That second shape is the app's.
# The fields are therefore separated by a UNIT SEPARATOR (0x1f) and not by a tab: tab is IFS
# WHITESPACE, so consecutive tabs collapse and an empty field VANISHES — `read` then shifted
# tool_use_id into the agent slot, the sub-agent gate compared two different ids on every call, and
# the hook fired 0 times in 45 calls with a threshold of 35 (measured on the shipped script, real
# payload shape, and the first-agent marker held `toolu_0001`). 0x1f is not whitespace in any locale.
#
# ═══ WHAT THIS SCRIPT DOES NOT KNOW ═══
#
# IT HAS NOT READ A CLOCK. It counts tool calls. 35 fast reads can land two minutes into a turn, and
# the 30-minute deadline is the print runner's alone (`printRunner.turnTimeoutMinutes`, enforced by
# PrintTurnDispatcher) while a terminal spawn sets AIORCH_ROLE and is never killed at all. So the
# advisory states the count as a fact and names the deadline only as THE REASON THE BOUNDARY EXISTS,
# leaving the model to resolve whether it applies from the AIORCH_RUNNER its own boot printed. An
# earlier version asserted "the 30-minute deadline is close" on every fire, which was a clock it had
# never read and, on a terminal session, a deadline that does not exist.
#
# ONE DIRECTORY PER TURN, so the 24 h sweep takes all of a turn's state or none of it. The counter,
# the first-agent marker and the latch used to be three siblings named after the session; the sweep
# removed the marker and the latch by mtime while every call refreshed the counter's, so a session
# older than a day was UN-LATCHED and spoke a second time — measured 2026-09-10 on the pre-fix
# script (backdate `.agent` and `.fired`, keep `.calls` live, let a new session trigger the sweep).
# With the marker gone the gate also cannot tell a sub-agent from the main agent any more, which is
# how that second advisory can reach a read-only Explore agent; that consequence is reasoned from
# the code, not separately reproduced. The fixed shape was measured both ways: a live turn (one more
# call after being backdated three days) keeps its directory AND its latch, and a dead turn goes
# whole.
#
# CASE FOLDING IS STATED, NOT CLAIMED AWAY. Both ids become path components, and on APFS or NTFS two
# that differ only in case share one entry where on ext4 they do not — so the hook is not identical
# across the three OS in that one respect. What rules it out here is EVIDENCE, not a safe direction:
# every turn key the CLI emitted in the 2026-09-10 capture is a LOWERCASE hex UUID (five distinct
# ids, prompt_id and session_id alike), so two keys differing only in case cannot arise. The direction
# would NOT be safe if that ever changed — one directory per turn holds the latch as well as the
# counter, so two merged turns would share both: the first would be advised early and the second
# would hear nothing at all, which is the defect above wearing another hat. `tool_use_id` IS mixed
# case, and there the cost is bounded and one-sided: two call ids differing only in case share one
# slot and undercount by one, which advises one call later and never never.
#
# NO md5, NO `date`, NO `stat`, and the latch is a `mkdir` — portability by absence rather than by
# fallback (`.claude/rules/kit-and-scripts.md`).

set -u

# THE ROLES THAT DO THE WORK, AND ONLY THOSE. The supervisor's turn is the owner's phone line and the
# general supervisor's is their concierge: advising either of them to stop and report mid-sentence
# would interrupt the one conversation this whole system exists to protect. AIORCH_ROLE is set by the
# spawner, so an unset value is a session outside an orchestration and gets nothing.
case "${AIORCH_ROLE:-}" in
  implementer|reviewer|solo) ;;
  *) exit 0 ;;
esac

# THE THRESHOLD, IN ONE PLACE.
#
# 35 calls. The distribution it is set against, both measured: the pre-fresh baseline was p50 2 /
# p80 9 / p90 18 / max 59 calls per turn, and on 2026-09-09 with fresh turns the median was 10 and
# the maximum 92. So 35 leaves roughly the top tenth of turns to hear anything at all and says
# nothing to the ordinary ten-call turn — which is the point: the risk being managed here is SHORT
# THINKING (an agent that knows it may be stopped taking the smaller decision), and a generous
# ceiling is the mitigation the design named for it.
#
# Overridable by the environment so the bridge can tune it per role or per stage later without a kit
# change. A value that is not a plain number falls back to the default rather than disabling the
# advisory silently — a typo must not turn a guard off. `0` is the explicit OFF.
SOFT_BOUNDARY_CALLS="${AIORCH_SOFT_BOUNDARY_CALLS:-35}"

case "$SOFT_BOUNDARY_CALLS" in
  '' | *[!0-9]* ) SOFT_BOUNDARY_CALLS=35 ;;
esac

# THE RANGE, NOT ONLY THE ALPHABET. `99999999999999999999` is every character a digit and still not
# an integer bash can compare: `[ 99999999999999999999 -le 0 ]` exits 2 with "integer expression
# expected", the `if` read that failure as false, the script carried on, and `[ COUNT -lt THRESHOLD ]`
# failed the same way — so a threshold typed one digit too long FIRED ON CALL 1 (measured). The cap
# is a length rather than a comparison because the comparison is the thing that cannot be trusted at
# the boundary; 9 digits leaves any threshold up to 999999999, which is nine hundred times the
# longest turn ever recorded here.
if [ "${#SOFT_BOUNDARY_CALLS}" -gt 9 ]; then
  SOFT_BOUNDARY_CALLS=35
fi

if [ "$SOFT_BOUNDARY_CALLS" -le 0 ]; then
  exit 0
fi

# A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS — see hook-log.sh for both halves.
# DEFINED FIRST, UNCONDITIONALLY, then overridden by the real one: a helper that EXISTS but is
# truncated (KitAssets_Installer overwrites the installed copy at every app start, so a hook firing
# during that copy sees a partial file) would otherwise leave the function undefined and the call
# would fail to stderr, the stream nobody reads.
aiorch_log_undecidable() { return 0; }

if [ -f "$(dirname "$0")/hook-log.sh" ]; then
  . "$(dirname "$0")/hook-log.sh" 2>/dev/null || true
fi

if ! INPUT=$(cat 2>/dev/null); then
  aiorch_log_undecidable "how many calls this turn has made" "the payload could not be read from stdin"
  exit 0
fi

# ONE EXTRACTION, FOUR FIELDS, WRITTEN AS BYTES AND SEPARATED BY 0x1f (see the header: a tab loses an
# absent field, and `agent_type` is absent on the sessions the app spawns).
#
# python3 is what every other hook here uses for its payload, and the bytes matter: on a mixed
# machine python3 is native Windows python, whose text-mode stdout turns every newline it writes into
# CRLF — a multi-line `print` would hand back values with a `\r` glued to the end of all but the
# last, and those values are compared for equality. `sys.stdout.buffer.write` with no trailing
# newline cannot be translated at all.
RAW=$(printf '%s' "$INPUT" | python3 -c 'import json,sys
d = json.load(sys.stdin)
sys.stdout.buffer.write("\x1f".join([
    str(d.get("prompt_id") or ""),
    str(d.get("session_id") or ""),
    str(d.get("agent_type") or ""),
    str(d.get("agent_id") or ""),
    str(d.get("tool_use_id") or ""),
]).encode("utf-8"))' 2>/dev/null)

if [ -z "$RAW" ]; then
  aiorch_log_undecidable "which turn this call belongs to" "the payload could not be parsed, so neither prompt_id nor session_id was extracted"
  exit 0
fi

IFS=$'\x1f' read -r PROMPT SESSION AGENT AGENT_ID CALL_ID <<< "$RAW"

# prompt_id IS THE TURN; session_id is the fallback for a CLI that does not send one. See the header
# for the capture that settles all three questions about the field.
TURN="${PROMPT:-}"

if [ -z "$TURN" ]; then
  TURN="${SESSION:-}"
fi

# THE TURN KEY BECOMES A PATH, so it is validated rather than trusted. Both candidates are UUIDs;
# anything outside that alphabet cannot be counted safely and is not guessed at.
case "$TURN" in
  '' | *[!0-9A-Za-z-]* )
    aiorch_log_undecidable "which turn this call belongs to" "the payload carried no usable prompt_id or session_id"
    exit 0
    ;;
esac

# ONE FILE PER CALL, NAMED BY tool_use_id — so the count needs no lock at all: the id is unique per
# call, a retry of the same call rewrites the same name, and parallel calls in one batch cannot
# collide. Only the LATCH has to be atomic, and that is a mkdir.
case "${CALL_ID:-}" in
  '' | *[!0-9A-Za-z_-]* ) CALL_ID="anon-$$-${RANDOM:-0}" ;;
esac

# ONE DIRECTORY PER TURN, holding all three pieces of state, so the sweep below removes a turn whole.
STATE_ROOT="${TMPDIR:-/tmp}/aiorch-soft-boundary"
TURN_DIR="$STATE_ROOT/$TURN"
AGENT_FILE="$TURN_DIR/agent"
FIRED_DIR="$TURN_DIR/fired"

mkdir -p "$STATE_ROOT" 2>/dev/null || true

# `mkdir` without -p IS the "am I the first call of this turn" test: it succeeds in exactly one
# process, so two parallel first calls cannot both take this branch.
if mkdir "$TURN_DIR" 2>/dev/null; then
  # THE FIRST CALL OF A TURN IS THE MAIN AGENT'S, always — a sub-agent exists only because the main
  # agent called the Agent tool, which is itself a counted call that arrives here first. So this is
  # recorded once and never rewritten; it is the derived discriminator the header describes. It is
  # written even when empty, which is the ordinary case: `agent_type` is absent for the main agent on
  # the sessions the app spawns, and an EMPTY marker still distinguishes it from a named sub-agent.
  printf '%s' "$AGENT" > "$AGENT_FILE" 2>/dev/null || true

  # Swept on a new turn rather than on every call: one traversal per turn, of a folder holding a
  # handful of empty files per turn. `find -mtime` is POSIX; `-mindepth 1` keeps the root itself out
  # of the reckoning, which is what would otherwise delete the folder being created. Every call adds
  # a NEW file to its turn directory, which refreshes that directory's mtime, so a live turn cannot
  # be swept out from under itself — and a turn is one unit here, so what the sweep takes it takes
  # whole. The previous shape kept the counter, the marker and the latch as three siblings and the
  # sweep removed two of the three, un-latching a live session.
  find "$STATE_ROOT" -mindepth 1 -maxdepth 1 -mtime +1 -exec rm -rf {} + 2>/dev/null || true
fi

if [ ! -d "$TURN_DIR" ]; then
  aiorch_log_undecidable "how many calls this turn has made" "the call counter under $STATE_ROOT could not be created"
  exit 0
fi

: > "$TURN_DIR/c.$CALL_ID" 2>/dev/null || true

# COUNTED BY GLOB, WITH NO FORK AND NO `ls` TO MISREAD. The `c.` prefix is what separates the call
# files from the marker and the latch now that all three share one directory; with nullglob off an
# unmatched pattern stays literal, which `-e` then reports as absent.
set -- "$TURN_DIR"/c.*

if [ -e "$1" ]; then
  COUNT=$#
else
  COUNT=0
fi

if [ "$COUNT" -lt "$SOFT_BOUNDARY_CALLS" ]; then
  exit 0
fi

# A SUB-AGENT'S CALL COUNTS BUT IS NOT ADVISED — see the header. A missing marker file means the
# question cannot be answered, and there the advisory is DELIVERED and the inability recorded:
# advising the main agent one turn too eagerly costs nothing, and silently never advising anyone is
# the failure this whole hook exists to remove.
# A SUB-AGENT IS IDENTIFIED BY ITS IDENTITY FIRST, ITS NAME SECOND. `agent_id` is present on every
# sub-agent payload captured (2026-09-10, CLI 2.1.267) and absent on a main-agent one, and the CLI's
# own schema says why that is the reliable half: agent_type "is present when the hook fires from
# within a subagent (alongside agent_id), or on the main thread of a session started with --agent
# (WITHOUT agent_id)". So the name alone is ambiguous — the re-review of this fix proved it, with a
# main agent whose agent_type was a plugin's default agent and a sub-agent of that same spawnable
# type: the advisory went to the sub-agent and burnt the turn's latch, which is the one failure this
# gate exists to prevent. The name check stays underneath as belt and braces for a CLI that ever
# omits the id.
if [ -n "${AGENT_ID:-}" ]; then
  exit 0
fi

if [ -f "$AGENT_FILE" ]; then
  FIRST_AGENT=$(cat "$AGENT_FILE" 2>/dev/null || printf '')

  if [ "$AGENT" != "$FIRST_AGENT" ]; then
    exit 0
  fi
else
  aiorch_log_undecidable "whether this call is the main agent's or a sub-agent's" "the first-agent marker beside the call counter is missing"
fi

# ONCE PER TURN, NOT ONCE PER CALL PAST THE THRESHOLD. A line repeated on all 57 remaining calls of a
# 92-call turn is a waterfall, which is the thing this system exists to prevent (decision 14) — and
# it would drown the advice it is trying to give. mkdir fails on an existing directory in a single
# atomic syscall, so two parallel calls crossing the threshold together cannot both fire.
if ! mkdir "$FIRED_DIR" 2>/dev/null; then
  exit 0
fi

# WORDING IS HALF THE MECHANISM, and it says only what this script knows.
#
# The named risk is SHORT THINKING: an agent that knows it may be stopped takes the smaller decision.
# So the advisory spends more words forbidding that than asking for the stop. Three things it must
# never do again, each a finding against the version before it: claim a clock it has not read; invite
# the model to nominate the NEAREST thing that looks complete ("at or near a stable point" —
# "near" is gone); or promise that stopping "loses nothing", which is false in transcript mode, where
# there is no pack, and false whenever the work being dropped is what would have closed the line.
cat <<JSON
{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"SOFT BOUNDARY — this turn has made $COUNT tool calls (the advisory point is $SOFT_BOUNDARY_CALLS). That total is the TURN's, not only yours: a sub-agent you fanned out to spends its calls on your turn, so the number can be far above what your own history shows. This is ADVICE: nothing is blocked, it is said once per turn, and it is never repeated.\\nWHAT THIS LINE KNOWS: the call count, and nothing else. IT HAS READ NO CLOCK — it is not telling you that time is nearly up, and it may have arrived two minutes into your turn or twenty-five. Why the count matters anyway: a turn's context grows with every call (measured 2026-09-09: around 200 k inside the long turns), so each further call in it costs more than the last. And if the bridge is running you one turn per message, a turn has a hard deadline after which it is killed and asked for a closing report — 41 % of that night's member tokens sat in turns that ran to it. Whether that deadline is yours is something YOU know and this line does not: it is the runner your boot command printed. A turn that ends where you chose is cheaper than one that is cut.\\nWHAT TO DO — both halves, and the second matters more:\\n  • If you are AT a stable point — a change that is complete and verifiable NOW — save your work the way your role does (an implementer commits, a reviewer only reports) and write your report in your channel. AT, not near: do not go looking for the nearest thing that could be called finished, and do not shrink the change to make it fit. Weigh what ending here costs, because it is not always nothing: if the bridge starts your next turn FRESH it writes you a pack of what you left and little is lost, but if it resumes you in the same session there is no pack, and if the work you would drop is what closes your ledger line, ending here buys a whole extra round trip.\\n  • If you are MID-CHANGE: FINISH THAT CHANGE FIRST. Do not abandon work, do not leave the tree half-edited, and never stop before something is verifiable. Carrying on is always allowed and nobody penalises a turn that ran long — the cost of continuing is the growing context this line just described, never a judgement on you.\\nDO NOT TAKE A SMALLER DECISION BECAUSE OF THIS LINE. Do not narrow what you were asked to do, do not skip a check, do not drop a verification, and never report work you have not run. Stopping short of the task is a worse outcome than a turn that ran long — all that is being asked is that IF this turn is going to end, you pick where — and ending a turn is not stopping: the next turn carries on from what you left."}}
JSON

# EVERY PATH ENDS AT 0, THIS ONE INCLUDED. `cat` above returns 1 when stdout is closed by the caller,
# and that used to be the script's exit code — which made the header's "no exit code but 0 on any
# path" untrue on the only path that produces output.
exit 0

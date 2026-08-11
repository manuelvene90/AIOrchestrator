#!/usr/bin/env bash
# AI Orchestrator — behaviour check for the supervisor enforcement hooks.
#
# HOW TO RUN:  bash kit/hooks/hook-behaviour-check.sh
# It needs nothing installed, touches nothing outside a temporary HOME it creates and deletes, and
# exits non-zero on the first case that does not match intent.
#
# WHY THIS EXISTS: these two hooks deadlocked a live supervisor seven times in one evening — the
# PreToolUse hook denied every call while a question was with the owner, and the Stop hook refused to
# end the turn until PLAN.md was written, so the demanded write was the forbidden write. They had no
# coverage at all.
#
# WHY THE FIXTURES ARE BUILT BY PYTHON AND VALIDATED. An earlier version pasted Windows paths
# straight into a JSON string by hand. The backslashes made the payload invalid JSON, python3 threw,
# the hook saw an empty tool name and FAILED OPEN — and "fail open" reads as ALLOW, so the two cases
# asserting ALLOW passed while proving nothing. A reviewer deleted the path normalisation entirely
# and this file stayed green.
#
# That is worse than a thin test: malformed input always yields ALLOW, so ANY future ALLOW case
# written with a Windows path would have been a silent no-op. Two defences now:
#   1. fixtures are serialised by json.dumps, so no escaping is ever hand-rolled;
#   2. every fixture is parsed BEFORE use, and an unparseable one fails the run loudly instead of
#      passing quietly.
#
# The row that matters most is "ledger behind + question open -> ALLOW". If that ever goes back to
# DENY, a supervisor is stuck until a flag expires.

set -u

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
LEDGER_HOOK="$SCRIPT_DIR/supervisor-ledger-check.sh"
AWAIT_HOOK="$SCRIPT_DIR/supervisor-awaiting-answer-check.sh"

FAILURES=0
ORCH="check-orch"

REAL_HOME="$HOME"
TEMP_HOME=$(mktemp -d)
export HOME="$TEMP_HOME"

SUPERVISION="$TEMP_HOME/.claude/supervision/$ORCH"
mkdir -p "$SUPERVISION/imp-1" "$SUPERVISION/imp-2"

# The same path in the spelling a Windows session actually passes.
WIN_SUPERVISION=$(printf '%s' "$SUPERVISION" | tr '/' '\\')

cleanup() {
  export HOME="$REAL_HOME"
  rm -rf "$TEMP_HOME"
}
trap cleanup EXIT

# Serialised, never concatenated: json.dumps owns the escaping so a backslash cannot break a fixture.
fixture() {
  python3 -c 'import json,sys; print(json.dumps({"tool_name": sys.argv[1], "tool_input": ({sys.argv[2]: sys.argv[3]} if len(sys.argv) > 3 else {})}))' "$@"
}

# A hook "denies" when it emits a deny decision (PreToolUse) or a block decision (Stop).
verdict() {
  if printf '%s' "$1" | grep -q '"deny"\|"block"'; then
    printf 'DENY'
  else
    printf 'ALLOW'
  fi
}

check() {
  local description="$1" expected="$2" actual="$3"

  if [ "$expected" = "$actual" ]; then
    printf '  ok    %-44s %s\n' "$description" "$actual"
    return 0
  fi

  printf '  FAIL  %-44s got %s, wanted %s\n' "$description" "$actual" "$expected"
  FAILURES=$((FAILURES + 1))
}

# Runs a hook, but ONLY after proving the fixture is real JSON. A malformed fixture makes the hook
# fail open, which is indistinguishable from a genuine ALLOW — that is exactly how two cases here
# certified nothing for a whole commit.
run_hook() {
  local hook="$1" payload="$2" role="${3:-supervisor}"

  if ! printf '%s' "$payload" | python3 -c 'import json,sys; json.load(sys.stdin)' >/dev/null 2>&1; then
    printf 'UNPARSEABLE_FIXTURE'
    return
  fi

  printf '%s' "$payload" | AIORCH_ROLE="$role" AIORCH_ID="$ORCH" bash "$hook" 2>/dev/null
}

printf '\nStop hook — the task ledger\n'
touch "$SUPERVISION/.ledger-behind"
check "ledger behind, no question" DENY "$(verdict "$(run_hook "$LEDGER_HOOK" '{}')")"

touch "$SUPERVISION/.awaiting-answer"
check "ledger behind + question open" ALLOW "$(verdict "$(run_hook "$LEDGER_HOOK" '{}')")"

rm -f "$SUPERVISION/.ledger-behind"
check "no ledger debt" ALLOW "$(verdict "$(run_hook "$LEDGER_HOOK" '{}')")"

printf '\nPreToolUse hook — a question is with the owner\n'
check "Read" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Read file_path 'C:/repo/Foo.cs')")")"
check "Grep" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Grep pattern 'x')")")"
check "Write into the repo" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/Foo.cs')")")"
check "Edit into the repo" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path 'C:/repo/Foo.cs')")")"
check "Bash: dotnet build" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Bash command 'dotnet build')")")"
check "Write PLAN.md" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path "$SUPERVISION/PLAN.md")")")"
check "Write PLAN.md, backslash path" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path "$WIN_SUPERVISION\\PLAN.md")")")"
check "Edit owner-channel.md" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/owner-channel.md")")")"

# A command line cannot be scoped, so it gets nothing — not even for the ledger. This is the
# compound command supervisor.md prescribes at a boundary, which used to be allowed in full.
check "Bash mentioning PLAN.md is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Bash command "cat >> $SUPERVISION/imp-1/channel.md && echo x >> $SUPERVISION/PLAN.md")")")"

# MUTATION-VISIBLE for the Bash branch specifically. The two cases above are DENIED even with that
# branch deleted — a real Bash payload carries no path, so it reaches the empty-target deny and gets
# the same verdict for an unrelated reason. Found by mutating this file, not by reading it.
#
# This one carries a `path`, which the target extractor does accept: delete the Bash branch and the
# exemption below welcomes it. It is also not hypothetical — the extractor reads file_path,
# notebook_path AND path, so anything execution-capable arriving with one is exactly this shape.
check "Bash carrying a path to PLAN.md is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Bash path "$SUPERVISION/PLAN.md")")")"

# Monitor is ALLOWED, and not because it is safe. supervisor.md makes arming the persistent Monitor
# part of booting and it is the only thing that ever wakes a supervisor, so denying it left a
# respawned session unable to arm a watcher and unwakeable forever — while stopping nobody who did
# not want to comply, since the flag and this script are both writable by the session itself.
check "Monitor is allowed (the only waker)" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Monitor command 'bash watcher.sh')")")"

# MUTATION-VISIBLE by construction: an execution-capable tool carrying a file_path the exemptions
# would otherwise welcome. Delete the catch-all capability deny and this one turns ALLOW, which the
# older SomeFutureTool case could not do — it carried only a command, so it fell through to an empty
# target and was denied by the next check for an unrelated reason.
check "an unknown tool aimed at PLAN.md is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture SomeFutureTool file_path "$SUPERVISION/PLAN.md")")")"

# Unblocking is not moving in the background; briefing is. The app publishes who is waiting.
#
# READ THIS BEFORE TRUSTING THE CASES BELOW. The list is HAND-WRITTEN here, so they prove only that
# the hook reads it correctly — they can say nothing about whether the app publishes the right
# members into it, and a bug in the PUBLISHER would leave this check green. That half has already
# been wrong twice. It is covered in C# by AwaitingVerdictPredicateTests; nothing joins the two
# halves, and that needs a harness able to run the app's tick. Do not read a green run as end-to-end.
printf 'imp-2\n' > "$SUPERVISION/.awaiting-verdict"
check "Edit a member that IS waiting" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/imp-2/channel.md")")")"
check "same, backslash path" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$WIN_SUPERVISION\\imp-2\\channel.md")")")"
check "Edit a member that is NOT waiting" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/imp-1/channel.md")")")"

# A channel is append-only and Write REPLACES it. A supervisor's Write on imp-3/channel.md once
# destroyed that member's own boot entry, and it waited 35 minutes for a brief already in its file.
check "Write on a WAITING member is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path "$SUPERVISION/imp-2/channel.md")")")"
check "NotebookEdit on a member is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture NotebookEdit notebook_path "$SUPERVISION/imp-2/channel.md")")")"

rm -f "$SUPERVISION/.awaiting-verdict"
check "no awaiting-verdict file (fail closed)" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/imp-2/channel.md")")")"

check "a non-supervisor role is untouched" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/Foo.cs')" implementer)")"

rm -f "$SUPERVISION/.awaiting-answer"
check "no question open" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/Foo.cs')")")"

# The guard on the guard: if this ever reports ALLOW, every ALLOW case above is meaningless.
printf '\nThe fixture guard itself\n'
check "an unparseable fixture is NOT a pass" UNPARSEABLE_FIXTURE "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Write","tool_input":{"file_path":"C:\bad\escape"}}')"

printf '\n'

if [ "$FAILURES" -ne 0 ]; then
  printf '%s case(s) did not match intent.\n\n' "$FAILURES"
  exit 1
fi

printf 'All cases match intent.\n\n'

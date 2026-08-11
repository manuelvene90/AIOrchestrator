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
# coverage at all. A shell script is awkward to unit-test from xunit, which is exactly the argument
# that left them unverified; this is the cheaper answer.
#
# The row that matters most is "ledger behind + question open -> ALLOW". If that one ever goes back
# to DENY, a supervisor is stuck until a flag expires.

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

cleanup() {
  export HOME="$REAL_HOME"
  rm -rf "$TEMP_HOME"
}
trap cleanup EXIT

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
    printf '  ok    %-42s %s\n' "$description" "$actual"
    return 0
  fi

  printf '  FAIL  %-42s got %s, wanted %s\n' "$description" "$actual" "$expected"
  FAILURES=$((FAILURES + 1))
}

run_hook() {
  printf '%s' "$2" | AIORCH_ROLE="${3:-supervisor}" AIORCH_ID="$ORCH" bash "$1" 2>/dev/null
}

printf '\nStop hook — the task ledger\n'
touch "$SUPERVISION/.ledger-behind"
check "ledger behind, no question" DENY "$(verdict "$(run_hook "$LEDGER_HOOK" '')")"

touch "$SUPERVISION/.awaiting-answer"
check "ledger behind + question open" ALLOW "$(verdict "$(run_hook "$LEDGER_HOOK" '')")"

rm -f "$SUPERVISION/.ledger-behind"
check "no ledger debt" ALLOW "$(verdict "$(run_hook "$LEDGER_HOOK" '')")"

printf '\nPreToolUse hook — a question is with the owner\n'
check "Read" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Read","tool_input":{"file_path":"C:/repo/Foo.cs"}}')")"
check "Grep" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Grep","tool_input":{"pattern":"x"}}')")"
check "Write into the repo" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Write","tool_input":{"file_path":"C:/repo/Foo.cs"}}')")"
check "Bash: dotnet build" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Bash","tool_input":{"command":"dotnet build"}}')")"
check "Write PLAN.md" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Write\",\"tool_input\":{\"file_path\":\"$SUPERVISION/PLAN.md\"}}")")"
check "Edit owner-channel.md" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"$SUPERVISION/owner-channel.md\"}}")")"

# The NATIVE spelling on this platform. Matching only forward slashes denied the ledger write and
# re-created the deadlock this branch exists to remove.
WIN_SUPERVISION=$(printf '%s' "$SUPERVISION" | tr '/' '\\')
check "Write PLAN.md, backslash path" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Write\",\"tool_input\":{\"file_path\":\"$WIN_SUPERVISION\\\\PLAN.md\"}}")")"

# A command line cannot be scoped, so it gets nothing — not even for the ledger. This is the
# compound command our own supervisor.md prescribes at a boundary, which used to be allowed in full.
check "Bash mentioning PLAN.md is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"cat >> $SUPERVISION/imp-1/channel.md && echo x >> $SUPERVISION/PLAN.md\"}}")")"
check "Monitor executes, so it is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Monitor\",\"tool_input\":{\"command\":\"rm -f $SUPERVISION/.awaiting-answer\"}}")")"
check "an unknown tool is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"SomeFutureTool","tool_input":{"command":"anything"}}')")"
check "Edit into the repo" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Edit","tool_input":{"file_path":"C:/repo/Foo.cs"}}')")"

# Unblocking is not moving in the background; briefing is. The app publishes who is waiting.
#
# READ THIS BEFORE TRUSTING THE TWO CASES BELOW. The list is HAND-WRITTEN here, so they prove only
# that the hook reads it correctly — they can say nothing about whether the app publishes the right
# members into it, and a bug in the PUBLISHER would leave this check green. That half has already
# been wrong once: the publisher answered "spoke last" instead of "has filed work and is waiting on a
# verdict", so a member whose only entry was "imp-1 online" was published as waiting and the hook
# then permitted BRIEFING it — the exact thing this exemption exists to forbid.
#
# The publisher is covered in C#, by MemberStateAppEntryTests. Nothing joins the two halves; that
# needs a harness that can run the app's tick, which does not exist yet. Do not read a green run here
# as end-to-end.
printf 'imp-2\n' > "$SUPERVISION/.awaiting-verdict"
check "Edit a member that IS waiting" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"$SUPERVISION/imp-2/channel.md\"}}")")"
check "same, backslash path" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"$WIN_SUPERVISION\\\\imp-2\\\\channel.md\"}}")")"
check "Edit a member that is NOT waiting" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"$SUPERVISION/imp-1/channel.md\"}}")")"

rm -f "$SUPERVISION/.awaiting-verdict"
check "no awaiting-verdict file (fail closed)" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "{\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"$SUPERVISION/imp-2/channel.md\"}}")")"

check "a non-supervisor role is untouched" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Write","tool_input":{"file_path":"C:/repo/Foo.cs"}}' implementer)")"

rm -f "$SUPERVISION/.awaiting-answer"
check "no question open" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Write","tool_input":{"file_path":"C:/repo/Foo.cs"}}')")"

printf '\n'

if [ "$FAILURES" -ne 0 ]; then
  printf '%s case(s) did not match intent.\n\n' "$FAILURES"
  exit 1
fi

printf 'All cases match intent.\n\n'

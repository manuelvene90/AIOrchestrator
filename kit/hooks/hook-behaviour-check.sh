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
# written with a Windows path would have been a silent no-op. Three defences now:
#   1. fixtures are serialised by json.dumps, which is what actually CLOSES the class — no escaping
#      is ever hand-rolled, so the bad payload cannot be written in the first place;
#   2. every fixture is parsed before use, and an unparseable one yields a sentinel that no case
#      expects, so it fails the run instead of scoring ALLOW;
#   3. the same for a hook that is missing, crashes, or cannot start.
#
# Defence 2 is stated carefully because it was overstated before. The sentinel used to be swallowed
# by verdict() — which returned ALLOW for anything that was not a denial — so only the one case that
# compares it WITHOUT verdict() could ever see it: the header claimed a guard that covered 1 case of
# 29. verdict() now passes every non-verdict outcome through untouched. A comment that overstates a
# defence is how the next reader stops checking, which is the same defect in prose.
#
# The row that matters most is "ledger behind + question open -> ALLOW". If that ever goes back to
# DENY, a supervisor is stuck until a flag expires.

set -u

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
LEDGER_HOOK="$SCRIPT_DIR/supervisor-ledger-check.sh"
AWAIT_HOOK="$SCRIPT_DIR/supervisor-awaiting-answer-check.sh"

# REFUSE TO RUN RATHER THAN CERTIFY. The hooks are resolved relative to THIS file, so a copy taken
# somewhere else — `git show <sha>:… > /tmp/x.sh && bash /tmp/x.sh` is how it actually happened —
# finds neither of them. Every invocation then returns nothing, nothing scored as ALLOW, and the run
# reported 16 confident failures about code it never executed, with its ten ALLOW cases "passing".
# That result was quoted as evidence in a live investigation and sent it down a false trail.
#
# One refusal beats thirty results that describe nothing. Exit 2 means THIS RUN CERTIFIES NOTHING —
# the same code the environment probe uses, and deliberately distinct from exit 1, which means the
# suite ran and the hooks are wrong.
if [ ! -f "$LEDGER_HOOK" ] || [ ! -f "$AWAIT_HOOK" ]; then
  printf '\n  REFUSED  this harness cannot find the hooks it tests.\n'
  printf '           expected beside %s:\n' "$SCRIPT_DIR"
  [ -f "$LEDGER_HOOK" ] || printf '             MISSING  %s\n' "$LEDGER_HOOK"
  [ -f "$AWAIT_HOOK" ] || printf '             MISSING  %s\n' "$AWAIT_HOOK"
  printf '           Run it from kit/hooks/ in a checkout. Nothing was tested.\n\n'
  exit 2
fi

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
#
# EVERY NON-VERDICT OUTCOME PASSES STRAIGHT THROUGH, and that is the point. This function used to
# collapse them all into ALLOW, so "the hook allowed it" and "the hook crashed, was killed, never ran,
# or was handed a fixture it could not parse" were the same result — and ALLOW is what most of these
# cases assert, so a broken run scored as a passing one. The sentinel below was the only outcome that
# could be detected, and only because that one case compares it without calling this function.
#
# Note what is NOT treated as a failure: empty output with exit 0. That is how a hook legitimately
# says ALLOW, so emptiness alone cannot be the signal — the EXIT STATUS is, and run_hook turns a
# non-zero one into an outcome that no case expects.
verdict() {
  case "$1" in
    UNPARSEABLE_FIXTURE|HOOK_MISSING|HOOK_EXIT_*)
      printf '%s' "$1"
      return ;;
  esac

  if printf '%s' "$1" | grep -q '"deny"\|"block"'; then
    printf 'DENY'
  else
    printf 'ALLOW'
  fi
}

# A known-DENY probe, run before AND after the PreToolUse block. A TOTAL environment failure is loud
# — every DENY case reddens at once — but a PARTIAL one is not: this machine hit its commit limit
# three times tonight, and a fork that fails for only some invocations leaves the affected ALLOW
# cases passing silently in a sea of green. The probe converts that into a VOID run instead, because
# a suite that cannot evaluate its subject must not report on it.
assert_environment_can_evaluate() {
  local when="$1" result

  result=$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/Foo.cs')")")

  if [ "$result" = "DENY" ]; then
    return 0
  fi

  printf '\n  VOID  the environment cannot evaluate these hooks (%s): probe returned %s, wanted DENY\n' "$when" "$result"
  printf '        Every ALLOW case in this run is unverifiable. Nothing here is a pass.\n\n'
  exit 2
}

# Lets a case assert the SETUP it depends on, not only the hook's answer.
flag_state() {
  if [ -f "$1" ]; then
    printf 'PRESENT'
  else
    printf 'ABSENT'
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
  local hook="$1" payload="$2" role="${3:-supervisor}" output status

  # A missing hook produces no output, and no output used to read as ALLOW — so a copy of this file
  # run from anywhere else found neither hook and reported 16 confident failures about code it never
  # executed, while its ten ALLOW cases "passed". A harness must not certify the absence of the thing
  # it is testing.
  if [ ! -f "$hook" ]; then
    printf 'HOOK_MISSING'
    return
  fi

  if ! printf '%s' "$payload" | python3 -c 'import json,sys; json.load(sys.stdin)' >/dev/null 2>&1; then
    printf 'UNPARSEABLE_FIXTURE'
    return
  fi

  output=$(printf '%s' "$payload" | AIORCH_ROLE="$role" AIORCH_ID="$ORCH" bash "$hook" 2>/dev/null)
  status=$?

  # Crashed, killed, or unable to start. Distinct from "ran and said nothing", which is a real ALLOW.
  if [ "$status" -ne 0 ]; then
    printf 'HOOK_EXIT_%s' "$status"
    return
  fi

  printf '%s' "$output"
}

printf '\nStop hook — the task ledger\n'
touch "$SUPERVISION/.ledger-behind"
check "ledger behind, no question" DENY "$(verdict "$(run_hook "$LEDGER_HOOK" '{}')")"

# ONE route to ALLOW per case, which is why "no ledger debt" runs BEFORE the question is raised.
# With the flag already up there are two independent reasons to allow, so deleting the hook's ledger
# check entirely leaves this case green — it certifies nothing.
#
# CHECKED, not merely commented. The ordering above is the whole substance of the case, and a comment
# cannot stop a future reader restoring the state it warns about — that state is exactly what shipped
# at 133911e, which ran fully green while proving nothing. So the precondition is asserted as a case
# in its own right: if the question is open here, this run says so instead of quietly certifying.
rm -f "$SUPERVISION/.ledger-behind"
check "no-ledger-debt runs with NO question open" ABSENT "$(flag_state "$SUPERVISION/.awaiting-answer")"
check "no ledger debt" ALLOW "$(verdict "$(run_hook "$LEDGER_HOOK" '{}')")"

# Raised here and deliberately LEFT UP: every PreToolUse case below is about a question being open.
touch "$SUPERVISION/.ledger-behind" "$SUPERVISION/.awaiting-answer"
check "ledger behind + question open" ALLOW "$(verdict "$(run_hook "$LEDGER_HOOK" '{}')")"
rm -f "$SUPERVISION/.ledger-behind"

printf '\nPreToolUse hook — a question is with the owner\n'
assert_environment_can_evaluate "before"
check "Read" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Read file_path 'C:/repo/Foo.cs')")")"
check "Grep" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Grep pattern 'x')")")"
check "Write into the repo" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/Foo.cs')")")"
check "Edit into the repo" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path 'C:/repo/Foo.cs')")")"
check "Bash: dotnet build" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Bash command 'dotnet build')")")"
check "Write PLAN.md" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path "$SUPERVISION/PLAN.md")")")"
check "Write PLAN.md, backslash path" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path "$WIN_SUPERVISION\\PLAN.md")")")"
check "Edit owner-channel.md" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/owner-channel.md")")")"

# The ANCHORING of the ledger exemption, which is the fix for a CRITICAL: any file merely NAMED
# PLAN.md used to satisfy it. Only the one inside this orchestration's own folder may.
check "a PLAN.md outside supervision is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/PLAN.md')")")"
check "another orchestration's PLAN.md is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path "$TEMP_HOME/.claude/supervision/other-orch/PLAN.md")")")"

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

# TWO-DIGIT IDS. `imp-1` must not satisfy `imp-10`, and `imp-10` must work at all. The whole-line
# match is already correct in both directions — which is exactly why it needs a case: a correct
# behaviour with nothing pinning it is one refactor away from being an incorrect one.
#
# imp-1 IS THE WAITING MEMBER HERE. It used to run with imp-2 in the file, so it asserted only that
# imp-10 is not imp-2 — true, and already covered three lines above.
#
# BUT DO NOT READ THIS CASE AS LOAD-BEARING: it is documentation, not a pin. Measured, not assumed —
# mutating the hook's whole-line match to a bare substring leaves it GREEN even in this corrected
# form, because the prefix hazard is asymmetric. Waiting `imp-1` against target `imp-10` searches for
# the LONGER id inside the shorter file and misses either way; only the reverse direction bites, and
# that is the sibling case below, which does redden. Kept because the invariant is worth stating in
# both directions, and labelled because a case whose weight is overstated is how the next reader
# stops checking.
printf 'imp-1\n' > "$SUPERVISION/.awaiting-verdict"
check "imp-1 waiting does NOT unlock imp-10" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/imp-10/channel.md")")")"
printf 'imp-10\n' > "$SUPERVISION/.awaiting-verdict"
check "a two-digit member id works" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/imp-10/channel.md")")")"
check "imp-10 waiting does NOT unlock imp-1" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/imp-1/channel.md")")")"
printf 'imp-2\n' > "$SUPERVISION/.awaiting-verdict"

# A channel is append-only and Write REPLACES it. A supervisor's Write on imp-3/channel.md once
# destroyed that member's own boot entry, and it waited 35 minutes for a brief already in its file.
check "Write on a WAITING member is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path "$SUPERVISION/imp-2/channel.md")")")"
check "NotebookEdit on a member is denied" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture NotebookEdit notebook_path "$SUPERVISION/imp-2/channel.md")")")"

rm -f "$SUPERVISION/.awaiting-verdict"
check "no awaiting-verdict file (fail closed)" DENY "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Edit file_path "$SUPERVISION/imp-2/channel.md")")")"

check "a non-supervisor role is untouched" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/Foo.cs')" implementer)")"

# ── A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS ───────────────────────────────
#
# Both halves need a case, and the SECOND one is the whole point. Allowing was never the defect —
# allowing SILENTLY was. Fifteen guards looked armed and were not while this machine was at its
# commit limit, and nothing anywhere recorded that they had stopped working.
#
# A payload with no file_path is the undecidable case that used to DENY, which made this file the odd
# one out: three places face the same inability and one of them invented a refusal.
MARKER_FILE="$SUPERVISION/.guard-not-in-force"
rm -f "$MARKER_FILE"

check "an undecidable write is ALLOWED" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write nothing_useful 'x')")")"
check "...and it leaves a MARKER, not silence" PRESENT "$(flag_state "$MARKER_FILE")"

# THE HOOK DROPS A FACT; THE APP WRITES THE RECORD. Three lines, no timestamp, no JSON, no size
# ceiling. The app's log panel is fed by an in-process event a separate process can never raise, so a
# hook-written line stayed invisible until somebody went looking — which preserves the very property
# this exists to remove. Writing it app-side also means ONE rotation threshold instead of a copy of
# the 8 MB number living here in shell, unable to honour the low-disk half at all.
check "the marker names the hook" FOUND "$(grep -q 'supervisor-awaiting-answer-check' "$MARKER_FILE" 2>/dev/null && printf FOUND || printf MISSING)"
check "the marker names the predicate" FOUND "$(grep -q 'which file is being written' "$MARKER_FILE" 2>/dev/null && printf FOUND || printf MISSING)"
check "the marker is three lines" 3 "$(wc -l < "$MARKER_FILE" 2>/dev/null | tr -d ' ')"

# NO TIMESTAMP IN THE SHELL, and this case is why. The previous version stamped it here and got the
# zone wrong — local time wearing a Z — in the one field a post-mortem record exists to provide.
check "the marker carries no timestamp" ABSENT "$(grep -qE '[0-9]{4}-[0-9]{2}-[0-9]{2}' "$MARKER_FILE" 2>/dev/null && printf PRESENT || printf ABSENT)"

# A DECIDABLE call marks nothing. The fixture is a WRITE on purpose: a Read exits about forty lines
# above the marker site, so a Read-based case cannot see a mutant that marks on a decidable write.
rm -f "$MARKER_FILE"
check "a decidable DENY marks NOTHING" ABSENT "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/Foo.cs')")" >/dev/null; flag_state "$MARKER_FILE")"

rm -f "$MARKER_FILE"
check "a decidable ALLOW marks NOTHING" ABSENT "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path "$SUPERVISION/PLAN.md")")" >/dev/null; flag_state "$MARKER_FILE")"

rm -f "$MARKER_FILE"

# The same probe again, and it has to be AFTER: a fork limit reached mid-run leaves everything above
# it green and everything below it unverifiable, and only a second reading can tell those apart.
assert_environment_can_evaluate "after"

rm -f "$SUPERVISION/.awaiting-answer"
check "no question open" ALLOW "$(verdict "$(run_hook "$AWAIT_HOOK" "$(fixture Write file_path 'C:/repo/Foo.cs')")")"

# THE GUARDS ON THE GUARDS. If any of these reports ALLOW, every ALLOW case above is meaningless —
# they are assertions about the CLASS of silent failure, not about the individual instances that
# happened to be found. Each one is the exact shape that certified nothing for a whole commit.
printf '\nThe guards on this harness itself\n'
check "an unparseable fixture is NOT a pass" UNPARSEABLE_FIXTURE "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Write","tool_input":{"file_path":"C:\bad\escape"}}')"

# Through verdict() deliberately: the sentinel used to be swallowed there, so this is the case that
# would have caught the overstated header claim.
check "…and it survives verdict()" UNPARSEABLE_FIXTURE "$(verdict "$(run_hook "$AWAIT_HOOK" '{"tool_name":"Write","tool_input":{"file_path":"C:\bad\escape"}}')")"

# A hook that is not there produces nothing, and nothing used to read as consent.
check "a MISSING hook is not a pass" HOOK_MISSING "$(verdict "$(run_hook "$SCRIPT_DIR/no-such-hook.sh" '{}')")"

# A hook that runs and fails is not a pass either. `exit 3` stands in for crashed, killed, or unable
# to start — the states this machine produced three times tonight at its commit limit.
printf '#!/usr/bin/env bash\nexit 3\n' > "$TEMP_HOME/failing-hook.sh"
check "a FAILING hook is not a pass" HOOK_EXIT_3 "$(verdict "$(run_hook "$TEMP_HOME/failing-hook.sh" '{}')")"

# And the one that must NOT be a failure: silence with exit 0 is how a hook says ALLOW. Without this
# case the obvious fix for the three above is "empty means broken", which would redden every genuine
# ALLOW in the file.
printf '#!/usr/bin/env bash\nexit 0\n' > "$TEMP_HOME/silent-hook.sh"
check "silence with exit 0 IS an allow" ALLOW "$(verdict "$(run_hook "$TEMP_HOME/silent-hook.sh" '{}')")"

# A DETACHED COPY REFUSES, and this case exists because of what it asserts: REFUSED, not merely a
# non-zero exit. Delete the preflight and a detached copy still fails — the environment probe catches
# it — but it fails saying "the environment cannot evaluate these hooks", which is the WRONG CAUSE.
# That misdiagnosis is not hypothetical: a detached run reported 16 failures tonight, was read as
# evidence of a sick machine, and sent an investigation down an hour-long false trail. Getting the
# right answer for the wrong stated reason is how this file lies to the next reader.
cp "$0" "$TEMP_HOME/detached-copy.sh"
check "a detached copy REFUSES to run" REFUSED "$(bash "$TEMP_HOME/detached-copy.sh" 2>&1 | grep -oE 'REFUSED|VOID|did not match' | head -1)"

printf '\n'

if [ "$FAILURES" -ne 0 ]; then
  printf '%s case(s) did not match intent.\n\n' "$FAILURES"
  exit 1
fi

printf 'All cases match intent.\n\n'

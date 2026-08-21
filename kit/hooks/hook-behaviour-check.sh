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
REVIEWER_HOOK="$SCRIPT_DIR/reviewer-readonly-check.sh"
RUNEND_HOOK="$SCRIPT_DIR/run-to-the-end-check.sh"

# REFUSE TO RUN RATHER THAN CERTIFY. The hooks are resolved relative to THIS file, so a copy taken
# somewhere else — `git show <sha>:… > /tmp/x.sh && bash /tmp/x.sh` is how it actually happened —
# finds neither of them. Every invocation then returns nothing, nothing scored as ALLOW, and the run
# reported 16 confident failures about code it never executed, with its ten ALLOW cases "passing".
# That result was quoted as evidence in a live investigation and sent it down a false trail.
#
# One refusal beats thirty results that describe nothing. Exit 2 means THIS RUN CERTIFIES NOTHING —
# the same code the environment probe uses, and deliberately distinct from exit 1, which means the
# suite ran and the hooks are wrong.
if [ ! -f "$LEDGER_HOOK" ] || [ ! -f "$AWAIT_HOOK" ] || [ ! -f "$REVIEWER_HOOK" ]; then
  printf '\n  REFUSED  this harness cannot find the hooks it tests.\n'
  printf '           expected beside %s:\n' "$SCRIPT_DIR"
  [ -f "$LEDGER_HOOK" ] || printf '             MISSING  %s\n' "$LEDGER_HOOK"
  [ -f "$AWAIT_HOOK" ] || printf '             MISSING  %s\n' "$AWAIT_HOOK"
  [ -f "$REVIEWER_HOOK" ] || printf '             MISSING  %s\n' "$REVIEWER_HOOK"
  printf '           Run it from kit/hooks/ in a checkout. Nothing was tested.\n\n'
  exit 2
fi

FAILURES=0
ORCH="check-orch"

# The reviewer hook scopes its one permitted write to THIS member's folder, so the runner has to pass
# an id the way the spawner does. Irrelevant to the two supervisor hooks, which never read it.
MEMBER="rev-1"

REAL_HOME="$HOME"
TEMP_HOME=$(mktemp -d)
export HOME="$TEMP_HOME"

SUPERVISION="$TEMP_HOME/.claude/supervision/$ORCH"
mkdir -p "$SUPERVISION/imp-1" "$SUPERVISION/imp-2" "$SUPERVISION/$MEMBER"

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

# WHICH RULE DENIED, not merely that something did. A payload can reach DENY by more than one route —
# a destructive verb carrying a redirect is the standing example — and a case that accepts either
# route pins neither, which is how a guard stayed green here with its check deleted. Cases that care
# about the route assert through this; cases that only care that the door is shut still use verdict().
#
# Every non-verdict outcome passes through untouched for the same reason it does in verdict(): a
# crashed, missing or unparseable run must not be able to score as a rule firing. It cannot match any
# expected reason, so it fails the case rather than passing it.
deny_reason() {
  case "$1" in
    UNPARSEABLE_FIXTURE|HOOK_MISSING|HOOK_EXIT_*)
      printf '%s' "$1"
      return ;;
  esac

  case "$1" in
    *"deletes or rewrites files"*)     printf 'files' ;;
    *"edits files in place"*)          printf 'editor' ;;
    *"changes repository state"*)      printf 'git' ;;
    *"installs or scaffolds"*)         printf 'pkg' ;;
    *"writes output into a file"*)     printf 'tee' ;;
    *"redirects output into a file"*)  printf 'redirect' ;;
    *)                                 printf 'NO_DENIAL' ;;
  esac
}

# A known-DENY probe, run before AND after the PreToolUse block. A TOTAL environment failure is loud
# — every DENY case reddens at once — but a PARTIAL one is not: this machine hit its commit limit
# three times tonight, and a fork that fails for only some invocations leaves the affected ALLOW
# cases passing silently in a sea of green. The probe converts that into a VOID run instead, because
# a suite that cannot evaluate its subject must not report on it.
#
# IT HAS NO PRECONDITION, AND IT MUTATES NOTHING. Both properties were learned the hard way.
#
# The probe used to expect a DENY that the await hook only produces while `.awaiting-answer` is up —
# an UNSTATED precondition, which this file takes down for the cases that follow it. Called after that
# point the probe returned ALLOW and announced "the environment cannot evaluate these hooks": a
# confident wrong diagnosis of a flag it had itself failed to check, in the function whose whole job
# is to stop this suite from lying. Two routes to one state, reported as one cause.
#
# The first fix raised the flag around the probe and restored it. That works and it is the wrong shape:
# an instrument that mutates the state it measures around. Not hypothetical — a mutation run of that
# version removed the raise and kept the restore, so the probe DELETED a flag the suite had raised and
# the entire PreToolUse block went red.
#
# So the probe is now a denial with no precondition anywhere: the reviewer hook refuses a destructive
# verb unconditionally, with no flag, no orchestration folder and no state of any kind. It can be
# called from any point in the file, which is the property that lets it sit after the LAST case.
#
# It is also the better canary. This payload drives the full python reducer — the heaviest path in
# these hooks and the one that actually dies when the machine cannot fork — where the old Write
# payload exercised only the lighter extraction. The probe that missed a dying machine tonight was
# measuring the cheaper thing.
assert_environment_can_evaluate() {
  local when="$1" result

  result=$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'rm -rf build')" reviewer)")

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

  output=$(printf '%s' "$payload" | AIORCH_ROLE="$role" AIORCH_ID="$ORCH" AIORCH_MEMBER="$MEMBER" bash "$hook" 2>/dev/null)
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

# ── PreToolUse hook — the reviewer is read-only ──────────────────────────────────────────────────
#
# BOTH DIRECTIONS, and the second one is the defect. The destructive cases matched UNANCHORED
# SUBSTRINGS, so `rm ` was found inside "confi**rm **the finding" and `dd ` inside "a**dd ** tests":
# ordinary English in a quoted report body read as a destructive command. That denied the ONE write
# the role exists to make — its own findings — while a reviewer that wanted to mutate the tree could
# still do it by any spelling the list did not literally contain.
#
# So the DENY cases here are not decoration. A matcher that stopped denying would satisfy every ALLOW
# case in this section, and the fix for a false positive is exactly the change that would cause it.
printf '\nPreToolUse hook — the reviewer is read-only\n'

REV_CHANNEL="$SUPERVISION/$MEMBER/channel.md"

check "rm is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'rm -rf build')" reviewer)")"
check "git commit is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git commit -m fix')" reviewer)")"
check "sed -i is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'sed -i s/a/b/ Foo.cs')" reviewer)")"

# ONE CASE PER DENIED GIT SUBCOMMAND, AND THE READ-ONLY HALF TOO.
#
# There was exactly ONE git case before this, which is how a rewrite dropped the file-removal and
# file-move subcommands and stayed green. Unlike the wrapper forms, quoting cannot hide a subcommand
# from a substring matcher, so the old matcher caught those two ROBUSTLY and the loss was real: both
# stage a change AND touch the working tree. "Let me just take this stale file out of the index" is a
# sentence a reviewer actually thinks.
#
# Driven off a list so that a token in the hook with no case here reads as an omission rather than
# hiding behind a green run. The ALLOW half is not decoration: a matcher that denied everything would
# satisfy every line of the loop above it.
for git_subcommand in commit add rm mv push merge rebase reset checkout switch stash cherry-pick revert worktree tag clean restore apply am; do
  check "git $git_subcommand is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "git $git_subcommand Foo.cs")" reviewer)")"
done

for git_readonly in "log --oneline" "diff HEAD" "status --short" "show HEAD" "blame Foo.cs"; do
  check "git $git_readonly is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "git $git_readonly")" reviewer)")"
done
check "npm install is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'npm install')" reviewer)")"
check "redirection into a file is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x > Foo.cs')" reviewer)")"

# ANCHORING IS NOT JUST "STARTS WITH": a destructive command sitting after a separator is still a
# command. Mutating the fix to test only the front of the string leaves every case above green.
check "rm after && is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'cat Foo.cs && rm Foo.cs')" reviewer)")"
check "rm inside \$() is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo $(rm Foo.cs)')" reviewer)")"

# The reviewer's actual tools have to survive.
check "git log is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git log --oneline -5')" reviewer)")"
check "git diff is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git diff master..fix/r1')" reviewer)")"

# F10 ITSELF — prose is not a command. Each of these was DENIED before the fix, and each is a report
# a reviewer would really write.
check "a report saying 'confirm' is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> $REV_CHANNEL <<'EOF'
I confirm the finding is real.
EOF")" reviewer)")"
check "a report saying 'add tests' is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> $REV_CHANNEL <<'EOF'
They should add tests for this.
EOF")" reviewer)")"

# Searching FOR a destructive command is read-only work, and the reviewer's job is full of it.
check "grep for a destructive string is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn "rm -rf" scripts/')" reviewer)")"

# A report that QUOTES a command in a fenced block. Heredoc bodies are DATA, never commands — without
# that, a reviewer reporting on `git commit` is refused for naming its subject.
check "a report quoting a command is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> $REV_CHANNEL <<'EOF'
The implementer ran:
git commit -F /tmp/msg.txt
rm -rf obj/
That is what I am reporting on.
EOF")" reviewer)")"

# ── THE PAYLOADS THAT ACTUALLY BLOCKED TWO REVIEWERS, BYTE-EXACT ─────────────────────────────────
#
# Every case below is a real command that a reviewer really ran and really had refused by the copy
# installed on this machine — recovered from rev-8's and rev-9's own reports as base64, not
# reconstructed from a description. That matters: five constructions written from a PARAGRAPH about
# rev-9's finding all passed, because the paragraph described a defect it never had. Exact bytes
# ended a whole round of measuring the wrong thing.
#
# They are here because they are better than anything the author of the fix would invent. Each one
# names its real-world origin so a later reader cannot mistake them for padding.
#
# rev-8: an ordinary English word ending in the two letters of the removal command. Every one of
# these was refused, and it had to hyphenate its report to file at all.
check "the word alarm in a report" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "printf '%s' 'the alarm fired at noon' >> $REV_CHANNEL")" reviewer)")"
check "the word confirm in a report" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "printf '%s' 'I confirm the finding' >> $REV_CHANNEL")" reviewer)")"
check "the word perform in a report" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "printf '%s' 'perform the check' >> $REV_CHANNEL")" reviewer)")"

# rev-9 ROW A, and its own control: the SAME sentence with two words changed. The pair is the point —
# one word of ordinary English decided whether a reviewer could file, and nothing in the shape of the
# command differs at all.
check "rev-9 ROW A: the word add in a body" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> $REV_CHANNEL <<'EOF'
          Not a reason to hold the commit — a reason to add one sentence to the docstring.
EOF")" reviewer)")"
check "rev-9 ROW A control: the same, reworded" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> $REV_CHANNEL <<'EOF'
          Not a reason to hold the commit — a reason to put one sentence in the docstring.
EOF")" reviewer)")"

# rev-9 ROWS C and D: read-only git, as commands and as prose. One finds a common ancestor, the other
# lists linked checkouts; neither can mutate anything.
check "rev-9 ROW C: git merge-base" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git merge-base fix/reviewer-hook-substring-match master')" reviewer)")"
check "rev-9 ROW C: the same command in PROSE" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> $REV_CHANNEL <<'EOF'
I ran git merge-base fix/reviewer-hook-substring-match master to find the ancestor.
EOF")" reviewer)")"

# rev-9 ROW E, the sharpest of the set: a pure READ, denied for what its PATTERN was searching for. On
# a branch whose subject is substring matching, the guard forbade searching for the tokens under
# review. Bracketing each token made the identical command pass — which is the control.
check "rev-9 ROW E: grep FOR the mutating names" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "printf '%s' abc | grep -oE 'rm|mv|git commit' | sort")" reviewer)")"
check "rev-9 ROW E control: the bracketed spelling" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "printf '%s' abc | grep -oE '[r]m|[m]v' | sort")" reviewer)")"

# rev-9 ROW B, BOTH HALVES, and they go opposite ways on purpose.
#
# The `>` sits inside a single-quoted argument in a C# loop condition. Where nothing mutates, that is
# text and must be allowed. Where the command is `perl -i`, the file IS rewritten in place and the
# denial is CORRECT — a reviewer may not run a mutation whatever text it carries.
#
# The installed copy denied both with "redirects output into a file", reaching the right verdict on
# the perl run for the wrong reason. Asserting the REASON here is what keeps that distinction: if this
# ever denies as `redirect` again, the redirect rule has started reading quoted text.
check "rev-9 ROW B: the C# line as a grep pattern" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn "for (var index = entries.Count - 1; index >= 0; index--)" src/')" reviewer)")"
check "rev-9 ROW B: perl -i is denied as an EDIT" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "perl -0777 -i -pe 's/old/for (var index = entries.Count - 1; index >= 0; index--)/' Foo.cs")" reviewer)")"

# rev-6 characterised the installed guard as TWO predicates rather than a list of unlucky words, and
# filed the rule so a fix could be tested against it instead of against anecdotes. These cases test
# the RULE. Neither needed a code change here — both were already closed by lexer commits earlier on
# this branch — so no mutation was manufactured for them; their evidence is the differential, measured
# 2026-08-14: every ALLOW case below is refused by the copy installed on this machine and passes here.
#
# PREDICATE 1: a bare two-letter delete command followed by a SPACE, with the character in FRONT of it
# never examined. The trailing space is the trigger, which is why `confirmed` passes and `confirm `
# does not. The three word cases above are the same class; these are rev-6's own words, and `odd` is
# the copy-command variant nothing else in this file covers.
check "rev-6 P1: platform, form, harm in prose" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "printf '%s' 'the platform is odd and the form is harmless' >> $REV_CHANNEL")" reviewer)")"
check "rev-6 P1: term and confirm in prose" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "printf '%s' 'I confirm the term is correct' >> $REV_CHANNEL")" reviewer)")"

# PREDICATE 2, AND IT IS THE ONE NOBODY HAD: a matcher reading the greater-than character as a write
# ANYWHERE in the command, with its own message. rev-6 hit it on an awk range filter whose only
# offence was a numeric at-least comparison — it could not type the operator in its own report either.
#
# WHY THIS ONE IS WORSE THAN ITS SEVERITY: a numeric comparison is how you check a measured total
# against an expected one, which is the discipline the fleet was asked to adopt the same morning. The
# guard bit hardest on the habit being installed, in the role most likely to need it. ROW B covers the
# operator inside a quoted grep PATTERN; here it is a bare operator in a program argument, which is a
# different position and was untested.
check "rev-6 P2: awk numeric at-least filter" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "awk '\$3 >= 100 { print \$1 }' results.txt")" reviewer)")"
check "rev-6 P2: awk numeric range filter" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "awk 'NR >= 5 && NR <= 10' results.txt")" reviewer)")"

# THE CONTROL, and it is the same command so the pair pins the position rather than the word `awk`:
# an operator inside the program is text, an operator outside it is a redirect and must stay denied.
check "rev-6 P2 control: awk with a REAL redirect" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "awk '{ print }' results.txt > out.txt")" reviewer)")"

# rev-9 F1: THE IN-PLACE FLAG IS A LETTER IN A CLUSTER, AND THE RULE KNEW ONE SPELLING.
#
# The predicate asked whether an argument STARTED WITH `-i`. That is true of `-i` and `-i.bak` and of
# nothing else anyone types. rev-9 watched the first three of these rewrite the file they name and be
# allowed; the next three are the same defect in `sed`, which the finding never mentioned.
#
# The DENY cases assert the REASON, not just the verdict. `editor` is the answer that means the guard
# understood WHY — a denial arriving as `redirect` or `files` would be the right verdict reached by
# accident, and this file has already caught that once on rev-9's ROW B.
check "rev-9 F1: perl -pi bundled" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "perl -pi -e 's/a/b/' Foo.cs")" reviewer)")"
check "rev-9 F1: perl -0777 -pi" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "perl -0777 -pi -e 's/a/b/' Foo.cs")" reviewer)")"
check "rev-9 F1: perl -ni bundled" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "perl -ni -e 's/a/b/' Foo.cs")" reviewer)")"
check "F1: sed bundles it too" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "sed -ni 's/a/b/p' Foo.cs")" reviewer)")"
check "F1: sed --in-place, the long form" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "sed --in-place 's/a/b/' Foo.cs")" reviewer)")"
check "F1: sed --in-place with a suffix" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "sed --in-place=.bak 's/a/b/' Foo.cs")" reviewer)")"

# `-l` IS AN OPTIONAL OCTAL, SO IT ONLY SWALLOWS DIGITS. The first draft of the fix stopped the scan
# at every value-taking letter and let this through — an ordinary perl one-liner that rewrites the
# file. It is here because the fix got it wrong first, which is the only reason a case is worth its
# line: nothing digit-like follows `l`, so `p` and `i` are still flags.
check "F1: perl -lpi, optional-digit flag" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "perl -lpi -e 's/a/b/' Foo.cs")" reviewer)")"

# THE CONTROLS. Every one of these contains the letter `i` in a flag cluster or an argument and none
# of them edits anything. Without them the fix could be "deny sed and perl outright", which would pass
# all seven cases above and take the reviewer's own reading tools away — the exact trade this branch
# exists to refuse. `-I` is an include directory and `-M` a module name; case matters, and so does
# knowing where a flag's argument begins.
check "F1 control: perl -pe writes nothing" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "perl -pe 's/a/b/' Foo.cs")" reviewer)")"
check "F1 control: sed -n prints only" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "sed -n 's/a/b/p' Foo.cs")" reviewer)")"
check "F1 control: perl -Ilib is a directory" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "perl -Ilib -e 'print 1'")" reviewer)")"
check "F1 control: perl -Mi::Foo is a module" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "perl -Mi::Foo -e 'print 1'")" reviewer)")"

# rev-10 F2: THE ONE WRITE THIS ROLE PERMITS, IN THE SPELLING A WINDOWS SESSION ACTUALLY TYPES.
#
# Inside double quotes the tokeniser ate every backslash, so `"C:\Users\…"` reduced to `C:Users…` —
# a path with no separators left, matching no own-folder segment. The reviewer was DENIED appending
# to its own channel in exactly the spelling `kit/commands/reviewer.md:24` documents, while the
# POSIX spelling two lines away was fine.
#
# WHY IT SURVIVED 203 CASES: every reviewer-channel case in this file used $REV_CHANNEL, which is
# POSIX-only. The Windows fixture that did exist was asserted against the awaiting-answer hook,
# whose payloads never reach this lexer. A whole spelling was untested, not tested and passing.
#
# THE TWO DENY ROWS ARE NOT DECORATION. Unquoted, the shell really does eat those backslashes, so
# that denial is correct and must survive the fix; and a quoted Windows path OUTSIDE the member
# folder must stay denied or the fix is just "allow anything with a backslash in it". A matcher
# that blanket-allowed Windows paths would satisfy both ALLOW rows and neither of these.
WIN_REV_CHANNEL=$(printf '%s' "$REV_CHANNEL" | tr '/' '\\')
WIN_ELSEWHERE=$(printf '%s' "$TEMP_HOME/src/Foo.cs" | tr '/' '\\')

check "own channel, DOUBLE-quoted Windows path" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> \"$WIN_REV_CHANNEL\"")" reviewer)")"
check "own channel, SINGLE-quoted Windows path" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> '$WIN_REV_CHANNEL'")" reviewer)")"
check "own channel, UNQUOTED Windows path" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> $WIN_REV_CHANNEL")" reviewer)")"
check "a quoted Windows path ELSEWHERE" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> \"$WIN_ELSEWHERE\"")" reviewer)")"

# ── INDIRECT EXECUTION ───────────────────────────────────────────────────────────────────────────
#
# All four of these were DENIED by the crude substring matcher BY ACCIDENT — the token appeared
# literally somewhere in the string — and a first-word reduction classifies `eval`, `bash`, `xargs`
# and `find` instead, confidently and wrongly. Demonstrated against master, not argued.
#
# These are the cases that say the reduction must FOLLOW the command through indirection. Fixing them
# by re-adding a substring scan would satisfy every one of them and re-break the whole section above,
# so they are only meaningful next to the ALLOW cases.
check "eval carrying rm is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'eval rm -rf build')" reviewer)")"
check "eval carrying a quoted rm is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'eval "rm -rf build"')" reviewer)")"
check "bash -c carrying rm is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'bash -c "rm -rf build"')" reviewer)")"
check "sh -c carrying rm is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "sh -c 'rm -rf build'")" reviewer)")"
check "xargs carrying rm is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo build | xargs rm -rf')" reviewer)")"
check "xargs with flags is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo build | xargs -n1 -I{} rm -rf {}')" reviewer)")"
check "find -exec carrying rm is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'find . -name "*.tmp" -exec rm {} ;')" reviewer)")"
check "sudo rm is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'sudo rm -rf build')" reviewer)")"

# ── A PREFIX'S FLAGS BELONG TO THE PREFIX ────────────────────────────────────────────────────────
#
# The prefix loop stopped at the first word starting with `-`, so the FLAG became the command name:
# `env -u FOO rm -rf build` allowed, `env rm -rf build` denied. Nothing about the first is an evasion
# attempt — both are how people invoke things — and an advisory guard exists for exactly that
# population.
check "env with a value-taking flag is denied" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'env -u FOO rm -rf build')" reviewer)")"
check "command with a flag is denied" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'command -p rm -rf build')" reviewer)")"
check "env with a valueless flag is denied" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'env -i rm -rf build')" reviewer)")"
check "sudo with a value-taking flag is denied" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'sudo -u root rm -rf build')" reviewer)")"
check "prefixes stacked, flags and all" git "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'sudo env -u X git commit -m y')" reviewer)")"

# CONTROL, and it cannot redden: the flagless spelling was denied before this fix and after it. Its
# job is to say the flag handling did not break the path that already worked.
check "control: the flagless spelling of the same" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'env rm -rf build')" reviewer)")"

# THE OTHER DIRECTION, which is the half a "skip the flags" fix breaks if written carelessly.
# `command -v rm` PRINTS where rm is and runs nothing; refusing it is a false denial of ordinary
# read-only work, and a reviewer checking what is on PATH is doing its job.
# Both name a DENIED verb on purpose. `command -V git` would pass with the inspection handling
# deleted — `git` with no subcommand is allowed anyway — so it would have been a case that cannot
# fail, dressed as one that can. Measured under the mutation, then the payload was changed.
check "command -v reports and does not run" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'command -v rm')" reviewer)")"
check "command -V, same" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'command -V rm')" reviewer)")"

# CONTROLS, and neither can redden: with the flag handling removed the prefix stops at the flag and
# both are allowed for that reason instead. They are here to catch a fix that turns ordinary
# prefixed read-only work into a refusal, which is the direction this branch exists to protect.
check "control: a prefix with flags carrying a read-only command" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'env -u FOO git log --oneline')" reviewer)")"
check "control: a prefix with no command at all" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'env')" reviewer)")"

# ── `<<` IS ONLY A HEREDOC WHEN IT IS AN OPERATOR ────────────────────────────────────────────────
#
# The stripper matched `<<` ANYWHERE on a line, including inside quotes: it set the marker to `b`,
# then dropped every following line waiting for a terminator that never came. Those lines were not
# classified as prose — they were not classified at all. The first case is the control that proves the
# second one is about QUOTING and not about newlines.
check "newline then rm is denied (control)" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo hi
rm -rf build')" reviewer)")"
check "a quoted << does not open a heredoc" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo "a << b"
rm -rf build')" reviewer)")"

# ── A HEREDOC BODY STARTS AT THE NEXT LINE, NOT AT THE MARKER ────────────────────────────────────
#
# The stripper jumped from the marker straight to the newline, so everything in between was thrown
# away — and a command that is never seen is never classified. `cat <<EOF; rm -rf build` allowed the
# `rm` without ever looking at it, which master denied.
#
# The same block also stopped reading `$((1<<3))` as a heredoc opener. That set the marker to `3`,
# waited for a terminator that never arrived, and swallowed the rest of the command line.
check "a command after a heredoc opener on the same line" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'cat <<EOF; rm -rf build
text
EOF')" reviewer)")"
check "a redirect on the same line as the opener" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'cat <<EOF > Foo.cs
text
EOF')" reviewer)")"
check "two heredocs on one line, then a verb" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'cat <<A <<B; rm -rf build
one
A
two
B')" reviewer)")"
# THE PAYLOAD IS MULTI-LINE ON PURPOSE, and I only know that because the mutation reddened nothing.
# On ONE line the fix above already saves it: the marker is set to `3`, no newline ever arrives, so no
# body is consumed and the `rm` survives to be classified. It takes a following LINE for the swallow
# to happen, so the single-line spelling is a case that cannot fail — kept below, labelled, because it
# is the shape a person actually types.
check "an arithmetic shift is not a heredoc" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo $((1<<3))
rm -rf build')" reviewer)")"
check "control: the same on one line" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo $((1<<3)); rm -rf build')" reviewer)")"

# AND THE DIRECTION THE WHOLE BRANCH EXISTS FOR: the body is still prose. A reviewer's report quoting
# a destructive command inside its own append must be allowed, or the guard silences the role it
# governs. If the fix above ever starts classifying body lines, this is what catches it.
check "a report body naming a verb is still allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "cat >> $SUPERVISION/$MEMBER/channel.md <<'EOF'
rm -rf build
git commit -m x
EOF")" reviewer)")"

# ── THE OVER-BLOCKING HALF, found by rev-4 doing real read-only work ─────────────────────────────
#
# Both of these are DENIED BY MASTER TOO — pre-existing false positives, not regressions. A guard that
# blocks a reviewer's own tools gets worked around, and then it guards nothing.
check "git worktree list is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git worktree list')" reviewer)")"
check "git worktree add is still denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git worktree add ../wt -b x')" reviewer)")"
check "git branch --all is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git branch --all')" reviewer)")"
check "git branch -d is still denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git branch -d topic')" reviewer)")"
check "a quoted > is not a redirect" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn "a -> b" src/')" reviewer)")"
check "a C# generic signature is not a redirect" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn "Dictionary<string, List<int>>" src/')" reviewer)")"
check "fd duplication is not a file write" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git log --oneline 2>&1')" reviewer)")"

# ── TWO MORE SPELLINGS THE LEXER READ AS SOMETHING ELSE ──────────────────────────────────────────
#
# `>|` was not in the operator table, so it tokenised as `>` followed by a pipe: the `>` found no word
# to take and a plain truncating write reported itself as an unreadable target. `>&` was exempted
# outright as descriptor duplication, which is what `2>&1` is — but `>& out.log` is the csh spelling
# of "both streams into this FILE", so the operator alone never decided it; the TARGET does.
check "the noclobber-override spelling of >" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x >| Foo.cs')" reviewer)")"
check "the csh spelling, into a file" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x >& out.log')" reviewer)")"

# The same operator with a DESCRIPTOR target writes nothing and must stay allowed — this is the pair
# that stops the fix above from becoming "deny every >&", which would refuse ordinary `2>&1` work.
check "…but the same operator onto a descriptor" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x >&2')" reviewer)")"
check "…and closing a descriptor" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x 2>&-')" reviewer)")"

# `$'…'` is ANSI-C quoting: the `$` is syntax and the word is what the quotes hold. Keeping it made
# the command reduce to `$rm`, which is in no denied set — an answer about a word the shell never sees.
check "an ANSI-C quoted verb is the verb" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "\$'rm' -rf build")" reviewer)")"
check "…and an ANSI-C quoted read-only command is not" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "\$'echo' hello")" reviewer)")"

# Quoted, so not an operator at all — the over-blocking direction for the new spelling.
check "a quoted >| is not a redirect" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn "a >| b" src/')" reviewer)")"

# A reviewer that appends with printf instead of a heredoc. The previous version refused this the
# moment its prose contained a parenthesis, so the earlier fix worked only for the one shape its
# author happened to use.
check "a printf append with parens is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "printf '%s' 'I confirm (yes) the finding' >> $SUPERVISION/$MEMBER/channel.md")" reviewer)")"

# ── THE OWN-CHANNEL EXEMPTION MUST NOT DEGENERATE WHEN THE IDS ARE MISSING ───────────────────────
#
# The exemption is built from the orchestration and member ids. With an EMPTY-STRING default the
# pattern collapses to three slashes — which a crafted path can simply contain, and then walk out of
# with a parent reference. Sentinels that cannot occur in a real path mean an unset id matches
# nothing, which is what the original did.
#
# Run with both ids EMPTY. Note the harness passes them explicitly: `${x:-default}` substitutes for an
# empty value too, so a helper written that way silently tests the DEFAULTS instead — that mistake
# hid this regression from my first comparison.
check "a crafted path cannot satisfy an empty id" DENY "$(verdict "$(MEMBER=""; run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> /tmp/supervision/$ORCH//../../etc/foo")" reviewer)")"
check "a real own-channel append still works" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $SUPERVISION/$MEMBER/channel.md")" reviewer)")"

# THE SHAPE THE ROLE COMMAND ACTUALLY SHOWS, and the reason every own-channel case above is not
# enough: they all spell the path out. A reviewer following its own instructions names the channel in
# a VARIABLE first, and at reduction time the redirect target is that variable, which contains no
# path. Refusing it does not degrade the role, it silences it — the first reviewer to file anything
# is told to do the very thing it just tried. Both directions, because an exemption that matched any
# variable at all would satisfy the first of these and wave through every write in the tree.
check "the role command's variable append is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "ch=\"$SUPERVISION/$MEMBER/channel.md\"
cat >> \"\$ch\" <<'EOF'
## [3] FROM reviewer — findings
EOF")" reviewer)")"
check "a variable naming NO own channel is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "ch=/tmp/elsewhere.txt
echo x >> \"\$ch\"")" reviewer)")"
check "a variable naming ANOTHER member is denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "ch=\"$SUPERVISION/imp-2/channel.md\"
echo x >> \"\$ch\"")" reviewer)")"
check "another member's channel is still denied" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $SUPERVISION/imp-2/channel.md")" reviewer)")"

# ── THE EXEMPTION IS A PROPERTY OF THE TARGET, NOT TEXT ANYWHERE IN THE COMMAND ──────────────────
#
# The fix for the CRITICAL above fell back to the original whole-command match whenever the target
# could not be read as a literal, and inherited the original's breadth with it: the exempting text
# only had to appear SOMEWHERE. A trailing comment was enough, and the second clause did not even
# require an append, so it permitted a TRUNCATING write to an arbitrary target.
#
# The variable is now resolved from the command line's own assignments instead, so these deny while
# every legitimate shape above still passes. Asserted through deny_reason: "it denies" would also be
# green if `echo` were somehow classified as a verb, and it is the REDIRECT rule that must catch these.
check "the exempt path in a comment does not exempt" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo pwned >> \$T   # $SUPERVISION/$MEMBER/channel.md")" reviewer)")"
check "…nor does the baseline word in a comment" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo pwned > $T   # watch-base')" reviewer)")"

# An assignment is only an assignment in LEADING position. After a command word it is an argument,
# and if this case ever allows, `echo ch=<exempt path> >> $ch` is a general write primitive.
check "an assignment-shaped argument cannot exempt" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo ch=$SUPERVISION/$MEMBER/channel.md >> \$ch")" reviewer)")"

# The baseline clause, both directions. Append is the whole of what any exemption here grants.
check "an append to the watcher baseline is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo 1 >> $TEMP_HOME/watch-base")" reviewer)")"
check "a TRUNCATING write to it is not" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo 1 > $TEMP_HOME/watch-base")" reviewer)")"

# The brace spelling of the same variable — one substitution form working is not the other working.
check "the braced variable append is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "ch=\"$SUPERVISION/$MEMBER/channel.md\"
cat >> \"\${ch}\"")" reviewer)")"

# ── THE EXEMPTION IS CHECKED ON A NORMALISED PATH ────────────────────────────────────────────────
#
# The test was a substring match on the raw target, so a path could satisfy it on the way through and
# then walk back out with `..`. This is the INTEGRITY hole, not a file-safety one: it lets one member
# append to another member's channel, and an entry's author is only the text inside it — a forged
# report, verdict or STANDING BY marker would be indistinguishable from a real one to the supervisor
# and to the app.
#
# THE THREE TRAVERSAL CASES ARE DENIED TWICE OVER, and that is written here because it was MEASURED
# rather than assumed. Two mechanisms landed together — the path is normalised, and the remainder
# after the folder must be a bare filename — and EITHER alone refuses these payloads, so neither
# mutation reddens them. Removing BOTH does, which is the run that says they are not passing for some
# third reason nobody has looked at.
#
# The mechanisms are pinned INDIVIDUALLY by two other cases below: the single-dot ALLOW (normalisation
# and only it) and the descend-below DENY (containment and only it). So all five earn their place —
# delete the single-dot case and normalisation is unpinned, delete the descend-below case and
# containment is unpinned, delete these three and rev-6's actual finding is asserted nowhere.
# THE LEFT BOUNDARY. The containment test searched for `supervision/<orch>/<member>/` and checked only
# what came AFTER it, so any directory whose name merely ENDED with the word satisfied it. The fix for
# this exact class was already four lines below, on the baseline clause, with a comment explaining it —
# written in the same change that left this one substring-based.
#
# The test compares SEGMENTS now, so both boundaries close by construction rather than by a guard per
# side. These four cases are one class in four spellings: a longer name before, and a longer name in
# each of the three segments that must match exactly.
check "a directory whose name ENDS with supervision" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $TEMP_HOME/evil-supervision/$ORCH/$MEMBER/notes.md")" reviewer)")"

# THE NEXT THREE ARE CONTROLS and only the case above pins the fix — measured, not guessed. The old
# substring test denied these three anyway: a longer name in any of the three matched segments means
# the searched-for string is not present at all, so they were never the hole. They pin the segment
# comparison against a LOOSER replacement — a `startswith` per segment, say — and the mutation that
# would redden them is not the one that restores the substring.
check "control: a longer supervision segment" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $TEMP_HOME/supervisionX/$ORCH/$MEMBER/notes.md")" reviewer)")"
check "control: a longer member segment" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $TEMP_HOME/supervision/$ORCH/${MEMBER}X/notes.md")" reviewer)")"
check "control: a longer orchestration segment" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $TEMP_HOME/supervision/${ORCH}X/$MEMBER/notes.md")" reviewer)")"

# The pair that stops the fix from becoming "deny everything": the same three segments, exact, from a
# root that is not this run's HOME at all — the folder is identified by its segments, not by where it
# happens to live.
check "the same segments under a different root" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $TEMP_HOME/supervision/$ORCH/$MEMBER/channel.md")" reviewer)")"

check "a traversal into another member's channel" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $SUPERVISION/$MEMBER/../imp-1/channel.md")" reviewer)")"
check "a traversal out of the orchestration" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $SUPERVISION/$MEMBER/../../other-orch/$MEMBER/channel.md")" reviewer)")"
check "the same traversal through tee" tee "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x | tee -a $SUPERVISION/$MEMBER/../imp-1/channel.md")" reviewer)")"

# INSIDE the folder, not merely PAST it. A path that matches and then descends is not contained by
# what it matched, and this direction is the one a substring test cannot see at all.
check "descending below the own folder is not inside it" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $SUPERVISION/$MEMBER/sub/evil.md")" reviewer)")"

# And normalisation must not refuse the ordinary spellings of the permitted write.
check "a single-dot segment is still the same file" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $SUPERVISION/$MEMBER/./channel.md")" reviewer)")"

# The baseline clause is now the file's NAME, not a word anywhere in the path.
check "a directory merely named watch-base" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x >> $TEMP_HOME/watch-base/evil.txt")" reviewer)")"

# ── A REDIRECT TARGET THE REDUCER CANNOT RESOLVE IS UNANALYSABLE, NOT ABSENT ─────────────────────
#
# A target that begins with a command or process substitution is emitted as an OPERATOR, so the
# target read as None and the redirect rule concluded there was nothing to look at — a confident
# answer about something it had not seen. Master refused these, and it is an everyday logging idiom.
#
# The fix is NOT to guess. It is to say so: the reducer now reports undecidable, which allows AND
# leaves the marker, instead of silently concluding the redirect was harmless.
check "a redirect into a substitution is not silently allowed" PRESENT "$(rm -f "$MARKER_FILE"; run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x > $(date +%F).log')" reviewer >/dev/null; flag_state "$MARKER_FILE")"
check "a redirect into a backtick target, same" PRESENT "$(rm -f "$MARKER_FILE"; run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x > `date +%F`.log')" reviewer >/dev/null; flag_state "$MARKER_FILE")"

# AN UNRESOLVABLE TARGET MUST NOT SWITCH OFF THE VERB RULES. Reporting undecidable from inside the
# reduction happened BEFORE the commands were classified, so a redirect nobody could resolve disabled
# the file-verb, git, editor and package rules too — every rule in the file — for a command whose
# verb had already been tokenised. That traded a narrow silent allow for a WIDE logged one, and
# `> $(date).log` is an everyday idiom rather than anything contrived.
#
# ASSERTED BY WHICH RULE FIRED, NOT MERELY THAT SOMETHING DID. "It denies" has two routes here: the
# verb rule catching the verb, which is the fix — or an unreadable target simply becoming a plain
# redirect DENY, which would score green on every line below while silently undoing the
# undecidable-and-allow behaviour the two cases above pin. A case with two routes to green pins
# neither, so each of these names the rule it expects to hear from.
#
# The first three cases are imp-1's, adopted; the rest close the families they left uncovered.
check "a destructive verb is denied despite an unresolvable target" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'rm -rf build > $(date +%F).log')" reviewer)")"
check "a git state change, same" git "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git commit -m x > $(date +%F).log')" reviewer)")"
check "control: literal target, same verb" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'rm -rf build > /tmp/a.log')" reviewer)")"
check "an in-place edit, same" editor "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'sed -i s/a/b/ Foo.cs > $(date +%F).log')" reviewer)")"
check "a package install, same" pkg "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'npm install > `date +%F`.log')" reviewer)")"

# The raise aborted the SPLIT, not just the classification, so everything after the redirect was
# never separated into commands at all. A denied verb sitting after it is the case that shows the
# difference between "classified and allowed" and "never looked at".
check "a denied verb AFTER the unresolvable redirect" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x > $(date +%F).log; rm -rf build')" reviewer)")"

# And the other direction, which is what stops the fix above from being "deny everything unreadable":
# with no denied verb present, the same target is still ALLOWED, and still marked. The two PRESENT
# cases above assert the marker; this asserts the verdict they do not.
check "an innocuous command with the same target is still allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x > $(date +%F).log')" reviewer)")"

# The ordinary resolvable cases must NOT become undecidable — that would trade a silent allow for a
# noisy one and pin nothing.
check "an ordinary redirect is still a plain deny" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x > Foo.cs')" reviewer)")"
check "...and marks nothing" ABSENT "$(rm -f "$MARKER_FILE"; run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x > Foo.cs')" reviewer >/dev/null; flag_state "$MARKER_FILE")"

rm -f "$MARKER_FILE"

# ── THE VERB SET IS THE CEILING ON EVERYTHING ELSE ───────────────────────────────────────────────
#
# A scanner that reduces flawlessly and then consults a set with no `cp` in it still allows a reviewer
# to overwrite any file in the repo. Master allows both of these too — a WIDENING beyond master, which
# every other change on this branch avoided, and it is here because it was ordered after a reviewer
# demonstrated the gap.
#
# The prose control matters more than usual for these two: `cp` and `mkdir` are short, ordinary words
# that appear in sentences, and refusing a report that mentions one is the exact false denial this
# branch exists to end.
check "cp is denied" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'cp a b')" reviewer)")"
check "cp with flags is denied" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'cp -r src dst')" reviewer)")"
check "mkdir is denied" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'mkdir newdir')" reviewer)")"
check "mkdir -p is denied" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'mkdir -p a/b')" reviewer)")"
check "a new verb reached through find -exec" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'find . -name "*.cs" -exec cp {} /tmp ;')" reviewer)")"
check "prose naming cp is still allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn "cp a b" src/')" reviewer)")"
check "a command merely starting with mk is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'mktemp -d')" reviewer)")"

# `tee` GOES THROUGH THE EXEMPTION, NOT INTO THE VERB SET. `… | tee -a "$ch"` is the permitted append
# in a different spelling, and refusing it with a message telling the reviewer to append to its own
# channel would rebuild the CRITICAL — the guard silencing the role it governs — on the branch that
# exists to remove it. So the deny cases below are only the writes a reviewer genuinely may not make,
# and the ALLOW cases are the same write it may already make with `>>`.
check "tee into an arbitrary file is denied" tee "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x | tee /tmp/out.log')" reviewer)")"
check "tee -a into an arbitrary file, same" tee "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x | tee -a /tmp/out.log')" reviewer)")"
check "tee into ANOTHER member's channel is denied" tee "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x | tee -a $SUPERVISION/imp-2/channel.md")" reviewer)")"

# Append is the whole of the permission, here as everywhere: without -a, tee TRUNCATES the channel it
# is pointed at, which is the opposite of the write being allowed.
check "tee WITHOUT -a into its own channel is denied" tee "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x | tee $SUPERVISION/$MEMBER/channel.md")" reviewer)")"
check "tee -a into its own channel is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x | tee -a $SUPERVISION/$MEMBER/channel.md")" reviewer)")"
check "…and through a variable, as the role command writes it" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "ch=\"$SUPERVISION/$MEMBER/channel.md\"
echo x | tee -a \"\$ch\"")" reviewer)")"

# ONE PERMITTED TARGET DOES NOT EXCUSE THE OTHERS. tee takes a list, and a rule that allowed the
# command because its first file was exempt would write everything after it too.
check "a permitted target plus a second file is denied" tee "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "echo x | tee -a $SUPERVISION/$MEMBER/channel.md /tmp/other.log")" reviewer)")"

check "tee with no file writes nothing and is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo x | tee')" reviewer)")"
check "prose naming tee is still allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn "tee -a x" src/')" reviewer)")"

# ── A LINE CONTINUATION IS DELETED, NOT ESCAPED ──────────────────────────────────────────────────
#
# `\` + newline is removed by a shell and the two halves join. The scanner escaped the newline like
# any other character instead, so it stayed inside the word and `\nrm` matched no verb.
#
# READ THE HISTORY HERE BEFORE DELETING ANYTHING: this was DENIED before the CRLF fix, and denied for
# a reason that had nothing to do with continuations. The extractor's stray `\r` sat between the
# backslash and the newline, so the backslash escaped the `\r` and the surviving newline acted as a
# separator — the verb was classified because a bug was standing in the right place. Removing the
# stray byte exposed the real defect, which means the CRLF fix opened this row and this case closes
# it. A mask is not a fix, and the case that only passes while a second bug is present is the one
# that quietly stops meaning anything.
check "a continuation before the verb" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command '\
rm -rf build')" reviewer)")"
check "a continuation before a git subcommand" git "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git \
commit -m x')" reviewer)")"
# A CONTROL, AND LABELLED AS ONE BECAUSE IT CANNOT REDDEN. Removing the continuation handling leaves
# this green: `rm` is the first word either way, so the verb is classified whether the join happens or
# not. It earns its place by catching a join that mangles the ARGUMENTS or raises — not by pinning the
# fix, which the two cases above do. Measured under the mutation rather than assumed; a case that
# cannot fail is worth keeping only when the file says out loud that it cannot.
check "control: a continuation between verb and args" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'rm \
-rf build')" reviewer)")"

# The other direction: joining lines must not manufacture a denial, and an ordinary escaped backslash
# must still be an escaped backslash rather than a continuation.
check "a continuation in a read-only command" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn \
"pattern" src/')" reviewer)")"
check "a continuation inside double quotes" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo "a \
b"')" reviewer)")"
check "an escaped backslash is not a continuation" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'echo "a \\ b"')" reviewer)")"

# ── THE COMMAND MUST REACH THE REDUCER AS THE BYTES THAT WERE SENT ───────────────────────────────
#
# python3 here is native Windows python, and its text-mode stdout turned every newline it wrote into
# CRLF — including the ones INSIDE the command. The reducer received a `\r` glued to the last word of
# every line but the last, and words are compared for equality, so `commit\r` was not a denied git
# subcommand and `install\r` was not a denied package one. Both DENY on master, which never cared
# where a carriage return sat; the regression arrived with the lexer that reads position.
#
# The verb-first cases are the control: `rm` at the start of its line was never affected, so a fix
# that "works" without moving them is not evidence. Only the argument position was corrupted, which
# is why this survived every multi-line case already in this file.
check "a denied git subcommand at end of line" git "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git commit
echo done')" reviewer)")"
check "a denied package subcommand, same" pkg "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'npm install
echo done')" reviewer)")"
check "control: verb at start of line was never hit" files "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'rm -rf build
echo done')" reviewer)")"
check "control: a read-only git command over two lines" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git log --oneline
echo done')" reviewer)")"

# The shape that exposed it: a path built across two assignments. The carriage return landed INSIDE
# the resolved path rather than at its end, so the exemption stopped matching and a reviewer's own
# append was refused. Both directions, because an exemption that ignored the middle of the path would
# satisfy the first and wave through the second.
check "a path built from two assignments is allowed" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "base=\"$SUPERVISION\"
ch=\"\$base/$MEMBER/channel.md\"
printf x >> \"\$ch\"")" reviewer)")"
check "…and the same built for another member is not" redirect "$(deny_reason "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "base=\"$SUPERVISION\"
ch=\"\$base/imp-2/channel.md\"
printf x >> \"\$ch\"")" reviewer)")"

# ── A TRAILING COMMENT IS NOT A REASON TO STOP GUARDING ──────────────────────────────────────────
#
# An apostrophe in a `#` comment — "don't", "it's" — made the reducer raise on an unbalanced quote,
# and unbalanced means undecidable, which ALLOWS. So the guard switched off for the WHOLE command
# because of a contraction in a comment nobody meant as syntax. That is ordinary writing, not an
# exploit, which is exactly why it matters.
#
# Note what the fix is NOT: the undecidable→allow posture is correct and stays. An apostrophe in a
# comment simply is not undecidable — the reducer could not see comments.
check "a comment with a contraction still denies" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "rm -rf build # don't do this")" reviewer)")"
check "same for a git subcommand" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "git commit -m x # it's staged")" reviewer)")"
check "a # inside quotes is not a comment" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'grep -rn "#define FOO" src/')" reviewer)")"
check "a # mid-word is not a comment" DENY "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'rm -rf build#1')" reviewer)")"

# ── A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS ────────────────────────────────
#
# The reducer returning nothing used to be a SILENT allow, which is decision 21's exact failure:
# allowing was never the defect, allowing with nothing anywhere recording it was. ALLOW is correct —
# this guard advises an honest session and every session can edit it anyway — but it must say so.
rm -f "$MARKER_FILE"
check "an unparseable command is ALLOWED" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command "grep -rn 'unbalanced src/")" reviewer)")"
check "...and it leaves a MARKER, not silence" PRESENT "$(flag_state "$MARKER_FILE")"
check "the marker names the reviewer hook" FOUND "$(grep -q 'reviewer-readonly-check' "$MARKER_FILE" 2>/dev/null && printf FOUND || printf MISSING)"

rm -f "$MARKER_FILE"
check "a decidable reviewer call marks NOTHING" ABSENT "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'git log --oneline')" reviewer)" >/dev/null; flag_state "$MARKER_FILE")"
rm -f "$MARKER_FILE"

check "a non-reviewer role is untouched" ALLOW "$(verdict "$(run_hook "$REVIEWER_HOOK" "$(fixture Bash command 'rm -rf build')" implementer)")"

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

# THE LAST PROBE, AND IT HAS TO BE LAST. The other two sit at the top and just before the PreToolUse
# block, so everything after them — which is now most of this file — ran unprobed. A fork that starts
# failing mid-run leaves every ALLOW case after that point passing for the wrong reason, and this
# machine does fail to fork: a run tonight died with `dofork: child died unexpectedly` and was caught
# only because the second probe happened to be downstream of the damage.
#
# THE EVIDENCE SPLITS, which is why the run is voided rather than failed: a DENIAL cannot be produced
# by a reducer that never started, so denials stand on their own. Allowances cannot, and there is no
# way from here to say which of them were evaluated — so the whole run is declared worthless rather
# than half-reported.

# -- Stop hook -- run to the end -----------------------------------------------------------------
#
# THIS HOOK SHIPPED WITH NO COVERAGE HERE AT ALL (8fab25f, 2026-08-20), and the gap is what let a
# false block survive a week of green runs: it and terminal mode were built days apart, and nothing
# in this file asked whether they agreed.
#
# EVERY ESCAPE IS A SEPARATE ROUTE TO ALLOW, so each case clears the other routes and ASSERTS it has
# cleared them. An ALLOW that could be produced by two reasons pins neither -- the rule this file
# already learned the hard way -- and this hook has five of them, which makes it the worst offender
# in the kit for that mistake.
printf '
Stop hook -- run to the end
'

RUNEND_PLAN="$SUPERVISION/PLAN.md"
RUNEND_CHANNEL="$SUPERVISION/owner-channel.md"

# The PreToolUse block above deliberately LEAVES .awaiting-answer up. Inheriting it here would give
# every case below a second route to ALLOW, so it is cleared and the clearing is checked.
rm -f "$SUPERVISION/.ledger-behind" "$SUPERVISION/.awaiting-answer" "$SUPERVISION/.meeting"
check "starts with no ledger debt" ABSENT "$(flag_state "$SUPERVISION/.ledger-behind")"
check "starts with no open question" ABSENT "$(flag_state "$SUPERVISION/.awaiting-answer")"
check "starts with no meeting" ABSENT "$(flag_state "$SUPERVISION/.meeting")"

printf '%s
' '- [ ] a line nobody is blocked on' > "$RUNEND_PLAN"
printf '%s
' '## [1] FROM solo - d - s' 'a plain report, no question in it' > "$RUNEND_CHANNEL"

# THE RULE ITSELF. If this stops denying, every ALLOW case below is satisfied by a hook that does
# nothing, and the section certifies nothing.
check "open work, nothing pending" DENY "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

# A member's turn ending IS its report to the supervisor.
check "a member is exempt" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}' implementer)")"

# THE FALSE BLOCK THE OWNER REPORTED, 2026-08-21: *"I also keep getting this in sessions ... Not sure
# if that is right but happens quite often."*
#
# In terminal mode the app turns the awaiting-answer block OFF and the role commands tell the session
# to ask in prose rather than with QUESTION:/OPTION: lines -- so both of the escapes this hook knew
# about are unavailable BY DESIGN, and a session waiting on an answer it asked for face to face was
# blocked for obeying. The control case above it is the point: the SAME ledger, the SAME channel,
# one flag apart.
touch "$SUPERVISION/.meeting"
check "the owner is at this terminal" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"
rm -f "$SUPERVISION/.meeting"
check "and the block returns when they leave" DENY "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

# A line that genuinely waits on the owner.
printf '%s
' '- [ ] a line nobody is blocked on' '- [?] this one waits on them' > "$RUNEND_PLAN"
check "a line is blocked on the owner" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

# The session has just asked, and only the LAST entry counts.
printf '%s
' '- [ ] a line nobody is blocked on' > "$RUNEND_PLAN"
printf '%s
' '## [1] FROM solo - d - s' 'QUESTION: merge or hold?' > "$RUNEND_CHANNEL"
check "the last entry is a question" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

printf '%s
' '## [1] FROM solo - d - s' 'QUESTION: merge or hold?' '' '## [2] FROM solo - d - s' 'carried on regardless' > "$RUNEND_CHANNEL"
check "an OLDER question does not exempt" DENY "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

# WAITING ON SOMETHING ALREADY RUNNING. The owner, 2026-08-21: the hook "keeps intervening
# constantly, essentially preventing solo from responding to me". The session it happened to had one
# open line, was waiting on a full suite it had already started, and correctly refused to mark `[?]`
# because nothing owner-side blocked it -- so, obeying, it turned a free wait into a NINE-MINUTE
# foreground poll and could not read the owner's question for over ten minutes.
#
# The marker is read the way every marker in this kit is: in the SUBJECT anywhere, or at the START of
# a body line. The negative cases are the substance -- an escape that any sentence about waiting can
# trigger is not an escape, it is an off switch, and the control immediately above each one is the
# SAME channel with the SAME ledger.
printf '%s
' '- [>] land on master: full suite then push' > "$RUNEND_PLAN"

printf '%s
' '## [1] FROM solo - d - suite running, WAITING ON the full suite' 'body text' > "$RUNEND_CHANNEL"
check "the marker in the subject" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

printf '%s
' '## [1] FROM solo - d - suite running' 'WAITING ON the full suite (52 projects)' > "$RUNEND_CHANNEL"
check "the marker starting a body line" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

printf '%s
' '## [1] FROM solo - d - suite running' 'I am waiting on the full suite' > "$RUNEND_CHANNEL"
check "lowercase prose is not a declaration" DENY "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

printf '%s
' '## [1] FROM solo - d - suite running' 'Right now I am WAITING ON the suite' > "$RUNEND_CHANNEL"
check "mid-line is discussion, not a marker" DENY "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

# SELF-CLEARING, and this is what makes the escape safe to hand out: it lasts exactly as long as it
# is the newest entry. The moment the session says anything else, the wait is over by its own account.
printf '%s
' '## [1] FROM solo - d - WAITING ON the suite' '' '## [2] FROM solo - d - suite done' 'all green' > "$RUNEND_CHANNEL"
check "a superseded wait does not exempt" DENY "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

printf '%s
' '## [1] FROM solo - d - suite done' 'all green' '' '## [2] FROM solo - d - WAITING ON the deploy' '' > "$RUNEND_CHANNEL"
check "the newest entry declares the wait" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

# "WAITING ONLY" CONTAINS "WAITING ON", and an escape that a near-miss can take is not an escape.
# Exactly the shape of "MUTATION WINDOW CLOSED" containing "WINDOW CLOSED" -- the collision this kit
# has already paid for once. The two ALLOW cases beside it are the control: the boundary must admit
# a colon and a dash, or the marker becomes fussy in a way nobody will remember.
printf '%s
' '## [1] FROM solo - d - s' 'WAITING ONLY for the reviewer to come back' > "$RUNEND_CHANNEL"
check "WAITING ONLY is a near miss, not the marker" DENY "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

printf '%s
' '## [1] FROM solo - d - s' 'WAITING ON: the full suite' > "$RUNEND_CHANNEL"
check "a colon after the marker still counts" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

printf '%s
' '## [1] FROM solo - d - WAITING ON - the full suite' 'body' > "$RUNEND_CHANNEL"
check "a dash after the marker still counts" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

printf '%s
' '## [1] FROM solo - d - s' 'a plain report' > "$RUNEND_CHANNEL"

# BLOCKED ON A MACHINE. `- [!]` already cleared this hook and the block message never said so, which
# is why sessions invented foreground polls instead: the escape existed and nobody was told. Pinned
# now so a future tightening of the open-line regex cannot silently take it away again.
printf '%s
' '## [1] FROM solo - d - s' 'a plain report' > "$RUNEND_CHANNEL"
printf '%s
' '- [!] land on master - blocked on the suite' > "$RUNEND_PLAN"
check "a line blocked on a machine" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

# THE BLOCK MESSAGE HAS TO TEACH THEM, or the escapes above are dead on arrival -- which is exactly
# how `- [!]` sat unused. Asserted on the emitted text, not on the file, so a reworded message that
# drops one is caught.
printf '%s
' '- [>] land on master: full suite then push' > "$RUNEND_PLAN"
BLOCK_TEXT="$(run_hook "$RUNEND_HOOK" '{}')"
for taught in 'WAITING ON' '\- \[!\]' '\- \[\?\]' 'QUESTION:' '\- \[-\]'; do
  check "the block message offers $taught" TAUGHT "$(printf '%s' "$BLOCK_TEXT" | grep -qE "$taught" && printf 'TAUGHT' || printf 'MISSING')"
done

# NOTHING LEFT TO DO. This is the exit that makes the rule livable, and it is the one a `grep -c`
# quirk silently broke once already -- `grep -c` prints 0 AND exits 1 when it matches nothing, so an
# `|| echo 0` appended a SECOND zero and the comparison failed on the one input that matters.
printf '%s
' '- [x] all done' > "$RUNEND_PLAN"
printf '%s
' '## [1] FROM solo - d - s' 'a plain report' > "$RUNEND_CHANNEL"
check "a finished ledger may stop" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"

# The ledger hook speaks first: one demand at a time.
printf '%s
' '- [ ] a line nobody is blocked on' > "$RUNEND_PLAN"
touch "$SUPERVISION/.ledger-behind"
check "the ledger hook has the floor" ALLOW "$(verdict "$(run_hook "$RUNEND_HOOK" '{}')")"
rm -f "$SUPERVISION/.ledger-behind" "$RUNEND_PLAN" "$RUNEND_CHANNEL"

assert_environment_can_evaluate "after every case"

printf '\n'

if [ "$FAILURES" -ne 0 ]; then
  printf '%s case(s) did not match intent.\n\n' "$FAILURES"
  exit 1
fi

printf 'All cases match intent.\n\n'

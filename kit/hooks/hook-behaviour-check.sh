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
    *"redirects output into a file"*)  printf 'redirect' ;;
    *)                                 printf 'NO_DENIAL' ;;
  esac
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

printf '\n'

if [ "$FAILURES" -ne 0 ]; then
  printf '%s case(s) did not match intent.\n\n' "$FAILURES"
  exit 1
fi

printf 'All cases match intent.\n\n'

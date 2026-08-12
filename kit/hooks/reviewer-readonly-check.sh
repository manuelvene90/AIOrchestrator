#!/usr/bin/env bash
# AI Orchestrator — read-only enforcement for REVIEWER sessions.
#
# WHY THIS EXISTS: a reviewer is launched without Write/Edit/NotebookEdit, which stops the obvious
# route but leaves Bash wide open — `echo ... > file`, `git commit`, `sed -i`, `rm` all mutate the
# repo just as effectively. "Investigate only, do not change anything" in a role command is prose,
# and prose is what the ledger investigation showed gets skipped. This is the lever.
#
# It runs as a PreToolUse hook on Bash and blocks mutating commands. The ONE write a reviewer needs
# is appending its report to its own channel, so appends under its own member folder are allowed.
#
# Only reviewer sessions are affected (AIORCH_ROLE is set by the spawner). Any unexpected condition
# ALLOWS the command — an enforcement bug must never wedge a session.

set -u

if [ "${AIORCH_ROLE:-}" != "reviewer" ]; then
  exit 0
fi

# A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS — see hook-log.sh for both halves.
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

# THE COMMENT HERE USED TO PROMISE A GREP FALLBACK, and there was none: python3 or nothing, and
# nothing meant a silent allow. So on a machine without python3 this guard was not degraded, it was
# absent — and the comment was the reason nobody checked.
#
# The fallback is NOT being built. A grep-based reader of JSON is the bare-substring class this
# branch has spent the night removing, and it would be a second extraction implementation competing
# with this one. The honest shape is one extractor that either works or says it did not.
COMMAND=$(printf '%s' "$INPUT" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("tool_input",{}).get("command",""))' 2>/dev/null)

if [ -z "$COMMAND" ]; then
  aiorch_log_undecidable "what command is being run" "no command could be extracted from the payload"
  exit 0
fi

deny() {
  # A denied command must teach, not just refuse — say what to do instead.
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"REVIEWER IS READ-ONLY: %s You do not fix what you find — report it as a finding in your channel (append with >>) and the supervisor assigns it to an implementer. If a review genuinely cannot proceed without a mutation, say so in your channel and stop."}}\n' "$1"
  exit 0
}

# git commands that change repository state. Read-only git (log/diff/show/status/blame) is the
# reviewer's main tool and must stay available.
case "$COMMAND" in
  *"git commit"*|*"git add"*|*"git push"*|*"git merge"*|*"git rebase"*|*"git reset"*|*"git checkout"*|*"git switch"*|*"git stash"*|*"git cherry-pick"*|*"git revert"*|*"git worktree"*|*"git branch -"*|*"git tag"*|*"git clean"*|*"git restore"*|*"git apply"*|*"git am"*)
    deny "that git command changes repository state." ;;
esac

# In-place editors and file removal.
case "$COMMAND" in
  *"sed -i"*|*"perl -i"*|*"rm "*|*"rmdir "*|*"mv "*|*"truncate "*|*"dd "*|*"Remove-Item"*|*"Set-Content"*|*"Add-Content"*|*"Out-File"*|*"New-Item"*)
    deny "that command deletes or rewrites files." ;;
esac

# Package/build side effects that write into the tree.
case "$COMMAND" in
  *"npm install"*|*"npm ci"*|*"yarn add"*|*"pip install"*|*"dotnet add"*|*"dotnet new"*|*"nuget "*)
    deny "that command installs or scaffolds into the working tree." ;;
esac

# Output redirection: allowed ONLY when it appends (>>) into this reviewer's own supervision folder
# — that is how it files its report. Everything else redirecting into a file is a write.
if printf '%s' "$COMMAND" | grep -Eq '(^|[^>&0-9])>{1,2}[^&]'; then
  IS_OWN_CHANNEL_APPEND=0

  if printf '%s' "$COMMAND" | grep -q '>>' \
     && printf '%s' "$COMMAND" | grep -q "supervision/${AIORCH_ID:-__none__}/${AIORCH_MEMBER:-__none__}/"; then
    IS_OWN_CHANNEL_APPEND=1
  fi

  # The watcher writes its own baseline file next to the channel — same folder, same allowance.
  if printf '%s' "$COMMAND" | grep -q 'watch-base'; then
    IS_OWN_CHANNEL_APPEND=1
  fi

  if [ "$IS_OWN_CHANNEL_APPEND" -eq 0 ]; then
    deny "that command redirects output into a file. The only write you may make is appending (>>) to your own channel under your member folder."
  fi
fi

exit 0

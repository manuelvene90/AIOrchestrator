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

# WHAT IS A COMMAND, AND WHAT IS PROSE.
#
# These tests used to be UNANCHORED SUBSTRING matches, and the cost landed on the one write this role
# exists to make. `rm ` is inside "confi[rm ]the finding" and `dd ` is inside "a[dd ]tests", so an
# ordinary English report body was read as a destructive command and refused — while anything the
# list did not literally spell was still allowed. The matcher was simultaneously too strict and too
# weak, and the strictness fell entirely on the reviewer's findings.
#
# The command is therefore reduced to the words that actually occupy a COMMAND POSITION: the first
# word of the string, and the first word after each shell separator (`;` `|` `&` `&&` `||`, a
# newline, and the inside of `$( )`, `( )` or backticks). Prose in a quoted argument never occupies
# one. Heredoc BODIES are dropped first, because a heredoc body is data being written, never a
# command — without that, a reviewer reporting on `git commit` is refused for naming its subject.
#
# The DENIED SET IS UNCHANGED — same tokens as before, tested properly. Anchoring is the fix;
# extending the list is not, because a denial list that grows by exception never closes the class.
#
# The residual bias is deliberate and is the safe one: anything this reduction cannot classify stays
# a command and is denied. A false denial is visible and the reviewer can report it; a false allow
# silently lets a read-only session rewrite the tree.

# `<<EOF`, `<<-EOF`, `<<'EOF'` and `<<"EOF"` open a body; a line equal to the marker closes it.
strip_heredoc_bodies() {
  awk '
    {
      if (marker != "") {
        line = $0
        sub(/^[ \t]+/, "", line)
        sub(/[ \t]+$/, "", line)
        if (line == marker)
          marker = ""
        next
      }

      if (match($0, /<<-?[ \t]*("[^"]+"|'\''[^'\'']+'\''|[A-Za-z_][A-Za-z0-9_]*)/)) {
        marker = substr($0, RSTART, RLENGTH)
        sub(/^<<-?[ \t]*/, "", marker)
        gsub(/["'\'']/, "", marker)
      }

      print
    }
  '
}

COMMAND_POSITIONS=$(
  printf '%s\n' "$COMMAND" \
    | strip_heredoc_bodies \
    | sed -E 's/&&|\|\|/\n/g; s/[;|&]/\n/g; s/\$\(/\n/g; s/[()`]/\n/g'
)

while IFS= read -r SEGMENT; do
  # Drop what stands in FRONT of the command without being it: leading blanks, `VAR=value` prefixes,
  # and wrappers like `sudo`. Without this, `sudo rm -rf x` reads as the command `sudo`.
  while : ; do
    SEGMENT=${SEGMENT#"${SEGMENT%%[![:space:]]*}"}
    WORD=${SEGMENT%%[[:space:]]*}

    case "$WORD" in
      *=*|sudo|command|env|nohup|time|builtin|exec)
        REST=${SEGMENT#"$WORD"}
        if [ "$REST" = "$SEGMENT" ]; then
          break
        fi
        SEGMENT=$REST ;;
      *)
        break ;;
    esac
  done

  CMD=${SEGMENT%%[[:space:]]*}
  REST=${SEGMENT#"$CMD"}
  REST=${REST#"${REST%%[![:space:]]*}"}
  ARG=${REST%%[[:space:]]*}

  # PowerShell cmdlets are case-insensitive to the shell, so the test has to be too.
  CMD_LOWER=$(printf '%s' "$CMD" | tr '[:upper:]' '[:lower:]')

  case "$CMD_LOWER" in
    rm|rmdir|mv|truncate|dd|remove-item|set-content|add-content|out-file|new-item)
      deny "that command deletes or rewrites files." ;;
    sed|perl)
      case " $SEGMENT " in
        *" -i"*) deny "that command edits files in place." ;;
      esac ;;
    git)
      case "$ARG" in
        commit|add|push|merge|rebase|reset|checkout|switch|stash|cherry-pick|revert|worktree|tag|clean|restore|apply|am)
          deny "that git command changes repository state." ;;
        branch)
          # `git branch` alone lists; anything with a flag can create, move or delete one.
          case " $SEGMENT " in
            *" -"*) deny "that git command changes repository state." ;;
          esac ;;
      esac ;;
    npm)
      case "$ARG" in
        install|ci) deny "that command installs or scaffolds into the working tree." ;;
      esac ;;
    yarn)
      case "$ARG" in
        add) deny "that command installs or scaffolds into the working tree." ;;
      esac ;;
    pip|pip3)
      case "$ARG" in
        install) deny "that command installs or scaffolds into the working tree." ;;
      esac ;;
    dotnet)
      case "$ARG" in
        add|new) deny "that command installs or scaffolds into the working tree." ;;
      esac ;;
    nuget)
      deny "that command installs or scaffolds into the working tree." ;;
  esac
done <<EOF
$COMMAND_POSITIONS
EOF

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

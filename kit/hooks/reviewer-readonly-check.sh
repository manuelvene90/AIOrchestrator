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
#
# WRITTEN AS BYTES, NEVER `print`ed. python3 here is native Windows python, so its text-mode stdout
# translates EVERY newline it writes into CRLF — including the ones inside the command. The reducer
# then saw a `\r` glued to the last word of every line but the last, and a word is compared for
# equality: `commit\r` is not in the denied git subcommands, `install\r` is not in the package ones.
#
#     git commit <newline> echo done      master DENY, this branch ALLOW
#     npm install <newline> echo done     master DENY, this branch ALLOW
#
# A substring matcher never cared where the carriage return sat, which is why this arrived with the
# rewrite that made the lexer position-sensitive, and why the bytes had to be looked at rather than
# the string. Same family as everything else python3 does on this machine: silent, and it looks fine.
#
# The other hooks extract single-line values, where the trailing CRLF is removed by the command
# substitution — checked, not assumed. This is the only extractor whose value can span lines.
COMMAND=$(printf '%s' "$INPUT" | python3 -c 'import json,sys; sys.stdout.buffer.write(json.load(sys.stdin).get("tool_input",{}).get("command","").encode("utf-8"))' 2>/dev/null)

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
# Three rewrites of this matcher have failed on the SAME missing capability: it could not tell shell
# syntax from text inside quotes.
#
#   - unanchored substrings found `rm ` inside "confi[rm ]the finding" and refused a reviewer's own
#     report — the one write this role exists to make;
#   - anchoring to command position fixed that and lost `git rm`/`git mv`, which the substring rule
#     had been catching robustly;
#   - the heredoc stripper matched `<<` INSIDE A QUOTED STRING, set the marker to a word that never
#     arrived as a terminator, and silently dropped every command after it;
#   - and the redirect rule still reads the `>` in `grep -rn "a -> b" src/` as a redirection, so
#     ordinary read-only work is refused.
#
# All four are one defect. So the reduction is done ONCE, by a quote-aware scanner, in python3 —
# which this hook already requires absolutely (the payload above is extracted with it, and the header
# says python3 or nothing). sed and awk cannot track quote state, which is precisely how the last two
# versions got it wrong.
#
# WHAT DID NOT CHANGE: the denied set, the messages, the own-channel exemption and the advisory
# posture. This is the same policy with a parser that can actually see the command.
#
# A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS (decision 21). An unparseable command
# line — an unbalanced quote, anything the scanner cannot reduce — is logged as undecidable and
# ALLOWED. It is deliberately NOT denied: this guard advises an honest session, every session can
# reach and edit it anyway, and a guard that invents refusals it cannot justify is the one that gets
# worked around. The earlier claim that "anything the reduction cannot classify stays a command and
# is denied" was both untrue of the code and the wrong rule to want.
VERDICT=$(AIORCH_COMMAND="$COMMAND" python3 - <<'PYEOF'
import os, re, sys

# SENTINELS, NOT EMPTY STRINGS. The own-channel exemption is built from these two ids, and with an
# empty default the pattern collapses to three slashes — which a crafted path can contain and then
# walk out of with a parent reference. A value that cannot occur in a real path means a missing id
# matches nothing, which is the property the original had and this reduction had lost. Nothing
# validates these two ids the way AIORCH_ROLE is validated above, so the default has to hold the line.
ORCH = os.environ.get("AIORCH_ID", "") or "__none__"
MEMBER = os.environ.get("AIORCH_MEMBER", "") or "__none__"

FILE_VERBS = {"rm", "rmdir", "mv", "truncate", "dd",
              "remove-item", "set-content", "add-content", "out-file", "new-item"}

# Same eighteen as before plus the two a previous rewrite dropped.
GIT_DENIED = {"commit", "add", "rm", "mv", "push", "merge", "rebase", "reset", "checkout", "switch",
              "stash", "cherry-pick", "revert", "tag", "clean", "restore", "apply", "am"}

# `git worktree` and `git branch` are NOT wholly state-changing, and denying them outright left a
# reviewer with no way to list either — a guard that blocks the reviewer own tools gets worked around.
GIT_WORKTREE_READONLY = {"list"}
GIT_BRANCH_READONLY_FLAGS = {"-a", "--all", "-l", "--list", "-r", "--remotes", "-v", "-vv",
                             "--verbose", "--show-current", "--contains", "--no-contains",
                             "--merged", "--no-merged", "--sort", "--format", "--color", "--no-color"}

PKG = {"npm": {"install", "ci"}, "yarn": {"add"}, "pip": {"install"}, "dotnet": {"add", "new"}}

PREFIXES = {"sudo", "command", "env", "nohup", "time", "builtin", "exec"}
SHELLS = {"bash", "sh", "zsh", "dash"}
XARGS_FLAGS_WITH_VALUE = {"-I", "-n", "-P", "-L", "-d", "-E", "-a", "-s",
                          "--max-args", "--max-procs", "--replace", "--delimiter"}

CMD_SEPARATORS = {";", "&&", "||", "|", "&", "(", ")", "`", "$(", "\n"}
REDIRECTS = {">", ">>", ">&", "&>", "<", "<<", "<<<"}


class Undecidable(Exception):
    pass


class UnresolvedTarget:
    """A redirect target the scanner cannot READ — deliberately NOT the same value as no target.

    `> $(date).log` emits its target as an operator, so there is no word to take. Saying so is right;
    saying it from inside `split_commands` was not. `analyse` calls that before `classify_words` has
    run, so the exception aborted the reduction before a single command had been classified, and one
    everyday logging idiom switched off the file-verb, git, editor and package rules as well as the
    redirect rule — a narrow silent allow traded for a WIDE logged one.

    The undecidable is real, but it is a fact about ONE redirect. Carrying it as a value instead of
    an exception lets the verb that was already tokenised be classified first, and a DENY from that
    verb outranks it.
    """


UNRESOLVED = UnresolvedTarget()


def strip_comments_and_heredoc_bodies(s):
    """Removes the two spans that are TEXT rather than syntax: `#` comments and heredoc bodies.

    Both in ONE pass, because each contains characters the other must not interpret. A comment can
    hold an apostrophe ("don't"), and a heredoc body can hold anything at all — so whichever is
    scanned second would read the first's contents as quotes. Splitting this into two passes fails
    in one direction whichever order they run in: a trailing comment with a contraction made the
    reducer raise on an unbalanced quote, and undecidable ALLOWS, so a contraction switched the guard
    off for the whole command.

    `<<` only opens a heredoc when it is a real operator: the version that matched it anywhere on the
    line treated the `<<` inside `echo "a << b"` as one, waited for a terminator named `b`, and
    swallowed every command that followed. An UNTERMINATED heredoc really does make the rest a body —
    that is what a shell does with it — so dropping it there is the correct reading, not a guess.
    """
    out = []
    i, n = 0, len(s)
    at_word_start = True

    while i < n:
        c = s[i]

        # A LINE CONTINUATION IS NOT AN ESCAPED CHARACTER, IT IS NOTHING. A shell deletes `\` and the
        # newline together and joins what is either side; escaping the newline instead leaves it in
        # the word, and `\nrm` matches no verb. Both scanners have to agree on this, so the same two
        # lines appear in `tokenize` — and `at_word_start` is deliberately left alone, because the
        # word continues across the join exactly as if the break were not there.
        if c == "\\" and i + 1 < n and s[i + 1] == "\n":
            i += 2; continue

        if c == "\\" and i + 1 < n:
            out.append(s[i:i + 2]); i += 2; at_word_start = False; continue

        # A `#` only starts a comment at the START OF A WORD — `build#1` is a filename, not a comment.
        if c == "#" and at_word_start:
            newline = s.find("\n", i)
            if newline == -1:
                break
            i = newline
            continue

        if c == "'":
            j = s.find("'", i + 1)
            if j == -1:
                raise Undecidable("an unbalanced single quote")
            out.append(s[i:j + 1]); i = j + 1; at_word_start = False; continue

        if c == '"':
            j = i + 1
            while j < n and s[j] != '"':
                j += 2 if s[j] == "\\" else 1
            if j >= n:
                raise Undecidable("an unbalanced double quote")
            out.append(s[i:j + 1]); i = j + 1; at_word_start = False; continue

        # `<<<` is a herestring, not a heredoc: it takes a word, not a body.
        if s[i:i + 3] == "<<<":
            out.append("<<<"); i += 3; at_word_start = True; continue

        if s[i:i + 2] == "<<":
            k = i + 2
            if k < n and s[k] == "-":
                k += 1
            while k < n and s[k] in " \t":
                k += 1

            marker = ""
            if k < n and s[k] in "\"'":
                quote = s[k]; k += 1
                while k < n and s[k] != quote:
                    marker += s[k]; k += 1
                k += 1
            else:
                while k < n and (s[k].isalnum() or s[k] == "_"):
                    marker += s[k]; k += 1

            if not marker:
                out.append("<<"); i += 2; continue

            newline = s.find("\n", k)
            if newline == -1:
                break

            body = s[newline + 1:]
            consumed = 0
            for line in body.split("\n"):
                consumed += len(line) + 1
                if line.strip() == marker:
                    break
            out.append("\n")
            i = newline + 1 + consumed
            continue

        out.append(c)
        at_word_start = c in " \t\n;|&()<>`"
        i += 1

    return "".join(out)


def tokenize(s):
    """Words (quotes removed) and operators, with quoted text never becoming an operator."""
    tokens = []
    buf = []
    has_word = False
    i, n = 0, len(s)

    def flush():
        nonlocal buf, has_word
        if has_word:
            tokens.append(("W", "".join(buf)))
        buf = []
        has_word = False

    while i < n:
        c = s[i]

        # The same rule as the stripper: `\` + newline is deleted, not escaped. Note it does NOT set
        # has_word — a continuation on its own does not begin a word, so `\<newline>rm -rf x` reduces
        # to the command it is, rather than to a word called `\nrm` that matches no verb.
        if c == "\\" and i + 1 < n and s[i + 1] == "\n":
            i += 2; continue

        if c == "\\" and i + 1 < n:
            buf.append(s[i + 1]); has_word = True; i += 2; continue

        if c == "'":
            j = s.find("'", i + 1)
            if j == -1:
                raise Undecidable("an unbalanced single quote")
            buf.append(s[i + 1:j]); has_word = True; i = j + 1; continue

        if c == '"':
            j = i + 1
            while j < n and s[j] != '"':
                if s[j] == "\\" and j + 1 < n:
                    # Inside double quotes a continuation is still deleted, as the shell does.
                    if s[j + 1] != "\n":
                        buf.append(s[j + 1])
                    j += 2; continue
                buf.append(s[j]); j += 1
            if j >= n:
                raise Undecidable("an unbalanced double quote")
            has_word = True; i = j + 1; continue

        if c in " \t":
            flush(); i += 1; continue

        if c == "\n":
            flush(); tokens.append(("O", "\n")); i += 1; continue

        three = s[i:i + 3]
        if three == "<<<":
            flush(); tokens.append(("O", "<<<")); i += 3; continue

        two = s[i:i + 2]
        if two in ("&&", "||", ">>", "<<", ">&", "&>", "$("):
            flush(); tokens.append(("O", two)); i += 2; continue

        if c in ";|&()`<>":
            flush(); tokens.append(("O", c)); i += 1; continue

        buf.append(c); has_word = True; i += 1

    flush()
    return tokens


def split_commands(tokens):
    """Simple commands (word lists) plus every redirect and its target."""
    commands, redirects, current = [], [], []
    i = 0

    while i < len(tokens):
        kind, text = tokens[i]

        if kind == "O":
            if text in REDIRECTS:
                target = None
                if i + 1 < len(tokens) and tokens[i + 1][0] == "W":
                    target = tokens[i + 1][1]
                    i += 1
                elif text in (">", ">>", "&>"):
                    # A target that is a command or process substitution arrives as an OPERATOR, so
                    # there is no word to read. That is UNANALYSABLE, not absent: reporting "no
                    # target" here was a confident answer about something never seen, and it let an
                    # everyday logging idiom straight through.
                    #
                    # RECORDED, NOT RAISED. This function runs before any command is classified, so
                    # raising here answered for the whole command line: `<destructive> > $(date).log`
                    # allowed, `git commit … > $(date).log` allowed — every rule in the file off, for
                    # a verb this scanner had already tokenised. `analyse` now raises after the verb
                    # rules have had their turn.
                    target = UNRESOLVED
                redirects.append((text, target))
            elif text in CMD_SEPARATORS:
                if current:
                    commands.append(current)
                    current = []
            i += 1
            continue

        current.append(text)
        i += 1

    if current:
        commands.append(current)

    return commands, redirects


def classify_words(words, depth):
    if depth > 4:
        raise Undecidable("indirection nested deeper than this scanner follows")

    index = 0
    while index < len(words):
        word = words[index]
        if re.match(r"^[A-Za-z_][A-Za-z0-9_]*=", word) or word.lower() in PREFIXES:
            index += 1
            continue
        break

    if index >= len(words):
        return None

    command = words[index].lower()
    args = words[index + 1:]
    first = args[0] if args else ""

    if command in FILE_VERBS:
        return "files"

    if command in ("sed", "perl") and any(a == "-i" or a.startswith("-i") for a in args):
        return "editor"

    if command == "git":
        if first == "worktree":
            return None if len(args) > 1 and args[1] in GIT_WORKTREE_READONLY else "git"
        if first == "branch":
            flags = [a for a in args[1:] if a.startswith("-")]
            return None if all(f.split("=")[0] in GIT_BRANCH_READONLY_FLAGS for f in flags) else "git"
        return "git" if first in GIT_DENIED else None

    if command in PKG:
        return "pkg" if first in PKG[command] else None

    if command == "nuget":
        return "pkg"

    # INDIRECTION: what these carry IS a command, so it is analysed as one. `xargs` and `find -exec`
    # are ordinary honest idioms and are the reason this exists; `eval` and `bash -c` come free with
    # the same machinery. The split-token evasion is deliberately NOT chased — every version of this
    # matcher loses to it, and chasing it leads straight back to substring scanning.
    if command == "eval":
        return analyse(" ".join(args), depth + 1) if args else None

    if command == "xargs":
        i = 0
        while i < len(args) and args[i].startswith("-"):
            takes_value = args[i].split("=")[0] in XARGS_FLAGS_WITH_VALUE and "=" not in args[i]
            i += 2 if takes_value and len(args[i]) <= 2 else 1
        return analyse(" ".join(args[i:]), depth + 1) if i < len(args) else None

    if command in SHELLS and "-c" in args:
        position = args.index("-c")
        return analyse(args[position + 1], depth + 1) if position + 1 < len(args) else None

    if command == "find":
        for position, arg in enumerate(args):
            if arg not in ("-exec", "-execdir"):
                continue
            carried = []
            for following in args[position + 1:]:
                if following in (";", "+"):
                    break
                carried.append(following)
            found = analyse(" ".join(carried), depth + 1)
            if found:
                return found
        return None

    return None


ASSIGNMENT = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)=(.*)$", re.S)
VARIABLE = re.compile(r"\$\{([A-Za-z_][A-Za-z0-9_]*)\}|\$([A-Za-z_][A-Za-z0-9_]*)")


def leading_assignments(commands):
    """Variables assigned in this command line, read ONLY from the leading run of a simple command.

    That is where a shell assignment actually is, and the restriction is the security property, not
    tidiness: collecting `name=value` from anywhere would let `echo ch=<exempt path> >> $ch` define
    the mapping that exempts its own write. In leading position it is an assignment because the shell
    would treat it as one; after a command word it is an argument and is ignored here too.
    """
    assignments = {}

    for words in commands:
        for word in words:
            match = ASSIGNMENT.match(word)
            if not match:
                break
            assignments[match.group(1)] = match.group(2)

    return assignments


def expand_variables(text, assignments):
    """Substitutes `$name` and `${name}` from this command line's own assignments.

    Bounded passes, and unknown names are left standing rather than guessed at — an unresolved `$`
    in a target means the exemption simply does not match, which denies.
    """
    for _ in range(5):
        expanded = VARIABLE.sub(
            lambda match: assignments.get(match.group(1) or match.group(2), match.group(0)), text)
        if expanded == text:
            break
        text = expanded

    return text


def redirect_reason(operator, target, assignments):
    # `>&` and `2>&1` duplicate a descriptor; nothing is written to a file.
    if operator in ("<", "<<", "<<<", ">&"):
        return None
    if target is None:
        return None

    # Handed straight back to `analyse`, which decides what it means once every command has been
    # classified. This branch is the ONLY thing an unreadable target may affect.
    if target is UNRESOLVED:
        return UNRESOLVED

    # A TARGET NAMED BY A VARIABLE IS RESOLVED, NOT SEARCHED FOR. The reviewer role command shows the
    # channel path put in a variable and the append using the variable, so a token-only test refuses
    # the one write a reviewer is allowed to make — that was the CRITICAL. The first fix for it fell
    # back to the original behaviour, matching the exemption substring ANYWHERE in the command, and
    # inherited the original's breadth along with its coverage: the exempting text could sit in a
    # trailing comment while the write went somewhere else entirely.
    #
    #   echo pwned >> $T   # <exempt path>      allowed a write to $T
    #   echo pwned >  $T   # watch-base         allowed a TRUNCATING one
    #
    # Resolving the variable from this command line's own assignments makes the exemption a property
    # of the TARGET again, which is the only thing it was ever meant to be about. The CRITICAL case
    # still passes because the assignment is right there in the same command line; text that is not
    # the target — a comment, an argument, a heredoc body — can no longer exempt anything.
    #
    # What this does NOT cover: a variable assigned in an earlier tool call. There is nothing in the
    # payload to resolve it from, so it denies — as both master and the pre-fix branch already did.
    normalised = expand_variables(target, assignments).replace("\\", "/")

    own_channel = "supervision/%s/%s/" % (ORCH, MEMBER)

    if operator == ">>" and own_channel in normalised:
        return None

    # The watcher baseline lives beside the channel. APPEND-ONLY, like the clause above: without that
    # requirement this clause permitted a truncating write, which is strictly worse than the write
    # the exemption exists to allow.
    if operator == ">>" and "watch-base" in normalised:
        return None

    return "redirect"


def analyse(source, depth=0):
    commands, redirects = split_commands(tokenize(strip_comments_and_heredoc_bodies(source)))
    assignments = leading_assignments(commands)

    for words in commands:
        reason = classify_words(words, depth)
        if reason:
            return reason

    # THE VERB RULES HAVE ALREADY RUN. An unreadable target is reported only if nothing above it was
    # denied, so a command whose verb is denied is still denied — the redirect it happens to carry
    # cannot excuse it — and the undecidable marker survives for the case where the redirect really
    # is the only thing in question.
    unresolved = False

    for operator, target in redirects:
        reason = redirect_reason(operator, target, assignments)
        if reason is UNRESOLVED:
            unresolved = True
            continue
        if reason:
            return reason

    if unresolved:
        raise Undecidable("a redirect target this scanner cannot resolve")

    return None


try:
    verdict = analyse(os.environ.get("AIORCH_COMMAND", ""))
except Undecidable as undecidable:
    print("UNDECIDABLE %s" % undecidable)
except Exception as failure:
    print("UNDECIDABLE the command could not be reduced (%s)" % type(failure).__name__)
else:
    print("DENY %s" % verdict if verdict else "ALLOW")
PYEOF
)

if [ -z "$VERDICT" ]; then
  aiorch_log_undecidable "whether this command mutates anything" "the command reducer produced no verdict"
  exit 0
fi

case "$VERDICT" in
  "DENY files")
    deny "that command deletes or rewrites files." ;;
  "DENY editor")
    deny "that command edits files in place." ;;
  "DENY git")
    deny "that git command changes repository state." ;;
  "DENY pkg")
    deny "that command installs or scaffolds into the working tree." ;;
  "DENY redirect")
    deny "that command redirects output into a file. The only write you may make is appending (>>) to your own channel under your member folder." ;;
  UNDECIDABLE*)
    aiorch_log_undecidable "whether this command mutates anything" "${VERDICT#UNDECIDABLE }"
    exit 0 ;;
esac

exit 0

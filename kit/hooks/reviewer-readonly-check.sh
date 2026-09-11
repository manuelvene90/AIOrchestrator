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
# nobody reads. The window is narrower than it was — kit/ ships as a Claude Code plugin and the
# hooks are loaded from the checkout, not copied into ~/.claude/hooks (kit/install.sh:10-14) — but a
# truncated or half-written helper still leaves the function undefined, so the stub stays.
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

# `cp` and `mkdir` are a DELIBERATE WIDENING BEYOND MASTER, ordered rather than assumed. Master
# denies neither, so "the denied set matches master" — the invariant this branch carried, after that
# claim was once found false and corrected — no longer holds, and saying so is the point of this
# comment. They are here because a guard that parses perfectly and then consults a set without `cp`
# in it still allows a reviewer to overwrite any file in the repo.
#
# The PowerShell spellings in this set are INCOMPLETE and deliberately left that way: `remove-item`
# and `new-item` are here, `copy-item` and `move-item` are not. That asymmetry is pre-existing and
# widening it further was not ordered — it is filed as a finding rather than fixed in passing.
FILE_VERBS = {"rm", "rmdir", "mv", "cp", "mkdir", "truncate", "dd",
              "remove-item", "set-content", "add-content", "out-file", "new-item"}

# THE SUBSET A CONTAINMENT QUESTION CAN BE ASKED ABOUT. Every verb here names the files it touches
# as ordinary words, so "is this inside the scratch folder" has an answer. `truncate` and `dd` write
# files too and nobody asked for them there — a widening is what was ordered and no more — and the
# PowerShell spellings are left alone for the same reason.
SCRATCH_FILE_VERBS = {"rm", "rmdir", "mkdir", "cp", "mv"}

# A COPY READS ITS SOURCES AND WRITES ITS DESTINATION, and only the write is a permission question:
# copying the repo INTO scratch is the whole point of the folder. Every other verb here destroys or
# removes what it names, so a `mv` out of the repo is a repo mutation and both ends must be contained.
DESTINATION_ONLY_VERBS = {"cp"}

# `-t DEST` NAMES THE DESTINATION SOMEWHERE OTHER THAN THE LAST WORD, so the position rule the
# widening rests on does not hold. Refused rather than parsed: this guard widens only where it is
# sure, and `cp -t <repo> <scratch>/mutant` is a write into the repo whose last word is contained.
DESTINATION_FLAGS = {"-t", "--target-directory"}

# WHERE sed TAKES ITS SCRIPT FROM. Given none of these it reads the script as its first bare word,
# and a script is not a file it rewrites — `sed -i 's/a/b/' <scratch>/Foo.cs` names one file, not two.
# perl always takes its script from a flag, so it needs no such subtraction.
SCRIPT_FLAG_LETTERS = "ef"
SCRIPT_FLAG_LONG = ("--expression", "--file")

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

# THE IN-PLACE FLAG IS A LETTER IN A CLUSTER, NOT A WORD. The rule here used to ask whether any
# argument STARTED WITH `-i`, which is true of `-i` and `-i.bak` and of nothing else anyone types:
# `perl -pi -e`, `perl -ni -e` and `sed -ni` all rewrite the named file and all walked straight past
# it, and so did `sed --in-place`, which was never mentioned in the finding. Bundled is the ordinary
# form; `-i` alone is the spelling the guard could already see.
#
# THE SCAN HAS TO STOP SOMEWHERE, and that is the whole difficulty. In a cluster, the letters after a
# value-taking flag are that flag's ARGUMENT, not more flags — so `perl -Ilib` and `perl -Mi::Foo`
# contain an `i` that is part of a directory and a module name. Reading those as in-place would deny
# ordinary read-only work, which is the failure this branch exists to end.
#
# The sets are PER-COMMAND because the same letter is a different flag in each: `-f` names a script
# file in sed and takes nothing in perl, so `perl -fi -e …` is an edit and a shared set would read its
# `i` as a filename. `-l` sets line length in sed and is an optional octal in perl.
#
# TWO CLASSES, and the second one is not decoration — the first draft of this function had only the
# first and let `perl -lpi -e` through, which is an ordinary one-liner and rewrites the file. A flag
# whose value is OPTIONAL DIGITS only swallows what actually follows it: in `-lpi` nothing digit-like
# follows `l`, so `p` and `i` are still flags, while in `-0777` the digits are the value.
#
# Case matters. `-I` is an include directory in perl and `-i` is the edit; they are different flags.
IN_PLACE_LONG = ("--in-place",)
VALUE_CLUSTER_FLAGS = {"sed": "efl", "perl": "eEFImMDx"}
DIGIT_VALUE_CLUSTER_FLAGS = {"sed": "", "perl": "0lC"}


def is_in_place_edit(command, args):
    """Does any argument carry the in-place flag, in ANY spelling this command accepts?"""
    value_flags = VALUE_CLUSTER_FLAGS.get(command, "")
    digit_flags = DIGIT_VALUE_CLUSTER_FLAGS.get(command, "")

    for a in args:
        if a in IN_PLACE_LONG or any(a.startswith(long + "=") for long in IN_PLACE_LONG):
            return True

        # `--foo` is a long option and never a cluster; a bare `-` and a non-flag word are neither.
        if not a.startswith("-") or a.startswith("--") or a == "-":
            continue

        cluster = a[1:]
        index = 0
        while index < len(cluster):
            ch = cluster[index]

            if ch == "i":
                return True

            # The rest of the cluster belongs to this flag, so nothing after it is a flag at all.
            if ch in value_flags:
                break

            if ch in digit_flags:
                index += 1
                while index < len(cluster) and cluster[index].isdigit():
                    index += 1
                continue

            index += 1

    return False

PREFIXES = {"sudo", "command", "env", "nohup", "time", "builtin", "exec"}

# Flags that swallow the NEXT word, per prefix. The list is BOUNDED and that is a decision, not an
# oversight: an unknown value-taking flag falls through as though it took none, its value is read as
# the command, and the command is then almost certainly not in any denied set — so the guard misses,
# which is the direction an advisory guard is allowed to be wrong in. The covered set is what people
# actually type; chasing every sudo flag would be chasing the evasion case, which this branch has
# been told twice not to do.
PREFIX_VALUE_FLAGS = {
    "env": frozenset({"-u", "--unset", "-C", "--chdir", "-S", "--split-string"}),
    "sudo": frozenset({"-u", "--user", "-g", "--group", "-p", "--prompt", "-U", "--other-user",
                       "-r", "--role", "-t", "--type", "-C", "--close-from", "-h", "--host"}),
    "exec": frozenset({"-a"}),
}

# Flags that make the prefix REPORT ON a command rather than run it. Nothing is executed, so nothing
# can be denied.
PREFIX_INSPECTION_FLAGS = {
    "command": frozenset({"-v", "-V"}),
}
SHELLS = {"bash", "sh", "zsh", "dash"}
XARGS_FLAGS_WITH_VALUE = {"-I", "-n", "-P", "-L", "-d", "-E", "-a", "-s",
                          "--max-args", "--max-procs", "--replace", "--delimiter"}

CMD_SEPARATORS = {";", "&&", "||", "|", "&", "(", ")", "`", "$(", "\n"}
REDIRECTS = {">", ">>", ">&", "&>", ">|", "<", "<<", "<<<"}

# The redirect operators that WRITE A FILE. `>|` is `>` with noclobber overridden — it was not in the
# operator table at all, so it tokenised as `>` followed by a pipe, the `>` found no word to take, and
# a plain truncating write reported itself as an unreadable target. `>&` is here too because its
# target decides what it is: a DESCRIPTOR duplicates (`2>&1`, nothing written), a NAME is a file.
WRITING_REDIRECTS = {">", ">>", "&>", ">|", ">&"}


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
    pending_bodies = []

    while i < n:
        c = s[i]

        # A HEREDOC BODY BEGINS AT THE NEXT LINE, so everything between the marker and that line is
        # ordinary command text. Jumping straight from the marker to the newline threw it away:
        # `cat <<EOF; rm -rf build` lost the `rm` entirely, and a command that is never seen is never
        # classified. The body is consumed HERE, once the line is genuinely over, and one body per
        # marker so `cat <<A <<B` still works.
        if c == "\n" and pending_bodies:
            out.append("\n")
            i += 1
            for marker in pending_bodies:
                while i < n:
                    end = s.find("\n", i)
                    line = s[i:] if end == -1 else s[i:end]
                    i = n if end == -1 else end + 1
                    if line.strip() == marker:
                        break
            pending_bodies = []
            at_word_start = True
            continue

        # ARITHMETIC IS NOT A REDIRECT. `$((1<<3))` carries a left SHIFT, and reading it as a heredoc
        # opener set the marker to `3`, waited for a terminator that never came, and swallowed every
        # command after it — `echo $((1<<3)); rm -rf build` allowed, with the `rm` never looked at.
        # The span is copied through untouched rather than interpreted; nothing inside it is a
        # command, so nothing inside it needs classifying.
        if s[i:i + 3] == "$((":
            depth, j = 0, i
            while j < n:
                if s[j] == "(":
                    depth += 1
                elif s[j] == ")":
                    depth -= 1
                    if depth == 0:
                        j += 1
                        break
                j += 1
            out.append(s[i:j]); i = j; at_word_start = False; continue

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

            # The operator and its marker are kept so the scan stays in step, and the REST OF THE
            # LINE is scanned normally — a redirect, a separator and another command may all follow
            # a heredoc opener on the same line. The body is dropped when the newline arrives.
            out.append(s[i:k])
            pending_bodies.append(marker)
            i = k
            at_word_start = False
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

        # DOLLAR IS SPELLED chr(36) BECAUSE THIS FILE IS A HEREDOC INSIDE A COMMAND SUBSTITUTION.
        # bash 3.2 (the /bin/bash macOS ships) re-scans the body of `$( … )` looking for the closing
        # paren and does not leave a quoted heredoc alone: the two bytes `$` `"` read as a
        # locale-translation opener and the dollar is DELETED. Written literally, the comparison
        # below arrived at python as `buf[-1] == ""` — always false — so ANSI-C quoting stopped
        # being stripped and `$'rm' -rf build` was ALLOWED, on a machine where the harness case for
        # it fails and every other case passes. Silent, and it looks fine: exactly the class this
        # file keeps paying for. The character is written as its code point so no such pair exists.
        # `$'…'` IS ANSI-C QUOTING AND `$"…"` IS LOCALE TRANSLATION: in both the `$` is syntax, and
        # the word is what the quotes contain. Keeping it made `$'rm' -rf build` reduce to a command
        # called `$rm`, which is in no denied set — the guard was answering about a word the shell
        # never sees. The `$` is dropped here rather than in the stripper because this is where words
        # are built; the stripper only needs to know where the quoted span ends.
        if c in "'\"" and buf and buf[-1] == chr(36):
            buf.pop()

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
                    if s[j + 1] == "\n":
                        j += 2; continue

                    # POSIX: inside double quotes the backslash escapes ONLY these five. Before
                    # anything else it is an ORDINARY CHARACTER and the shell keeps it. Eating it
                    # unconditionally — which this branch did — turned `"C:\Users\…"` into
                    # `C:Users…`, and a path whose separators are gone matches no own-folder
                    # segment: the one write this role permits was DENIED in exactly the Windows
                    # spelling `kit/commands/reviewer.md:24` documents. The unquoted spelling is a
                    # different case and is still denied above at the `\` branch, correctly — there
                    # the shell really does eat them.
                    if s[j + 1] in '$`"\\':
                        buf.append(s[j + 1]); j += 2; continue

                    buf.append("\\"); j += 1; continue
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
        if two in ("&&", "||", ">>", "<<", ">&", "&>", ">|", "$("):
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
                elif text in WRITING_REDIRECTS:
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


def classify_words(words, depth, assignments):
    if depth > 4:
        raise Undecidable("indirection nested deeper than this scanner follows")

    # A PREFIX'S FLAGS BELONG TO THE PREFIX, NOT TO THE COMMAND IT CARRIES. This loop used to stop at
    # the first word starting with `-`, so `env -u FOO rm -rf build` classified `-u` as the command
    # and allowed it while the bare `env rm -rf build` was denied. Nobody typing the first one is
    # trying to evade anything — the difference between the two is not something a person thinks
    # about, which is exactly the population an advisory guard is for.
    index = 0
    while index < len(words):
        word = words[index]

        if re.match(r"^[A-Za-z_][A-Za-z0-9_]*=", word):
            index += 1
            continue

        prefix = word.lower()
        if prefix not in PREFIXES:
            break

        index += 1
        value_flags = PREFIX_VALUE_FLAGS.get(prefix, frozenset())
        inspection_flags = PREFIX_INSPECTION_FLAGS.get(prefix, frozenset())

        while index < len(words) and words[index].startswith("-") and len(words[index]) > 1:
            flag = words[index].split("=")[0]

            # `command -v rm` PRINTS where rm is; it does not run it. Denying that would refuse
            # ordinary read-only work, which is the half of this branch that keeps getting lost.
            if flag in inspection_flags:
                return None

            takes_value = flag in value_flags and "=" not in words[index]
            index += 2 if takes_value else 1

    if index >= len(words):
        return None

    command = words[index].lower()
    args = words[index + 1:]
    first = args[0] if args else ""

    # TARGET-AWARE ONLY INSIDE THE SCRATCH FOLDER. Everywhere else this is the flat denial it always
    # was — the containment predicate is the only thing that can turn one of these into an allow.
    if command in FILE_VERBS:
        return None if file_verb_allowed(command, args, assignments) else "files"

    if command in ("sed", "perl") and is_in_place_edit(command, args):
        return None if in_place_edit_allowed(command, args, assignments) else "editor"

    if command == "git":
        if first == "worktree":
            return None if len(args) > 1 and args[1] in GIT_WORKTREE_READONLY else "git"
        if first == "branch":
            flags = [a for a in args[1:] if a.startswith("-")]
            return None if all(f.split("=")[0] in GIT_BRANCH_READONLY_FLAGS for f in flags) else "git"
        return "git" if first in GIT_DENIED else None

    # `tee` IS A REDIRECT WITH A DIFFERENT SPELLING, so it gets the redirect's exemption rather than a
    # flat deny. `… | tee -a "$ch"` is the one write a reviewer is allowed to make, and refusing it
    # with a message telling the reviewer to append to its own channel is the CRITICAL silencing shape
    # rebuilt in new clothes — on the branch that exists to remove it. Same predicate as the redirect
    # rule, called here rather than copied: two implementations of one exemption would drift, and the
    # one that drifted would be the one nobody was reading.
    if command == "tee":
        targets = [a for a in args if not (a.startswith("-") and len(a) > 1)]
        appending = any(a in ("-a", "--append") for a in args)
        operator = ">>" if appending else ">"
        if all(write_target_allowed(operator, t, assignments) for t in targets):
            return None
        return "tee"

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


def normalise_path(text):
    """Resolves `.` and `..` LEXICALLY. Never through the filesystem.

    The exemption was a substring test on the raw target, so `.../<me>/../<someone-else>/channel.md`
    matched — the test passed on the way through and the path then walked straight back out of the
    folder it had just satisfied.

    That is not a file-safety hole, it is an INTEGRITY one: it lets one member append to another
    member's channel, and an entry's author is only the text inside it. Gating decisions are made off
    those entries by the supervisor and by the app, so a forged report, verdict or STANDING BY marker
    would be indistinguishable from a real one.

    Lexical on purpose: the file need not exist, this process must not touch the disk to answer a
    question about a string, and a symlink race is not something a PreToolUse hook can win anyway.
    """
    normalised = text.replace("\\", "/")
    leading = "/" if normalised.startswith("/") else ""
    resolved = []

    for part in normalised.split("/"):
        if part in ("", "."):
            continue
        if part == "..":
            # A `..` with nothing to pop is kept, so it cannot silently vanish and leave a path that
            # looks contained when it is not.
            if resolved and resolved[-1] != "..":
                resolved.pop()
            else:
                resolved.append(part)
            continue
        resolved.append(part)

    return leading + "/".join(resolved)


def is_file_in_own_folder(resolved):
    """Is this path a file DIRECTLY inside this member's own folder — one question, one answer.

    It compares SEGMENTS rather than searching for a substring, which closes both boundaries by
    construction instead of by a guard per side. The previous version searched for
    `supervision/<orch>/<member>/` and checked only what came AFTER it, so the left side was open and
    any directory whose name merely ENDED with the word satisfied it:

        /tmp/evil-supervision/<orch>/<member>/notes.md      was ALLOWED

    A boundary check bolted onto the left would have worked and would have been the third guard on one
    predicate. The identity question is "are these the segments", and asked that way a name that
    merely contains another cannot answer yes.

    The fix for this exact class was already four lines below, on the baseline clause, with a comment
    explaining it — written in the same change that left this one substring-based. Fixing a class and
    not sweeping for it is how the twin survives.
    """
    segments = resolved.split("/")

    for index in range(len(segments) - 3):
        if (segments[index] == "supervision"
                and segments[index + 1] == ORCH
                and segments[index + 2] == MEMBER):
            # DIRECTLY inside: the file must be the last segment, so a path that descends further is
            # not in the folder it matched.
            return index + 3 == len(segments) - 1

    return False


def is_in_own_scratch(resolved):
    """Is this path the reviewer's own scratch folder, or anything under it, at any depth?

    THE TWIN OF is_file_in_own_folder, and deliberately its shape rather than its wording: the same
    segment identity test, the same two ids, one segment deeper. A reviewer needs somewhere to write
    a MUTANT of a source file and run the suite against it — mutation testing is the detector that
    caught four tests passing for the wrong reason on these branches when nobody reading the code
    did, and with every writable path denied it degraded into imagining what a mutation would do.

    TWO DIFFERENCES FROM THE OWN-FOLDER RULE, both intended. It is RECURSIVE, because a mutant comes
    with an obj/ folder and a copy of its neighbours; and it admits the folder ITSELF, because
    nothing else can create it for the reviewer and nothing else will clean it up.

    What it does NOT change: the member folder one segment above stays append-only, so the channel a
    reviewer files into cannot be truncated, moved or deleted through here.
    """
    segments = resolved.split("/")

    for index in range(len(segments) - 3):
        if (segments[index] == "supervision"
                and segments[index + 1] == ORCH
                and segments[index + 2] == MEMBER
                and segments[index + 3] == "scratch"):
            return True

    return False


def resolve_target(text, assignments):
    """The two steps every containment question needs, in one place: expand, then normalise."""
    return normalise_path(expand_variables(text, assignments))


def operands_of(command, args):
    """The words a command ACTS ON: flags dropped, and the values those flags take dropped with them.

    Written off the same per-command flag tables `is_in_place_edit` uses, because the same letter is
    a different flag in each: without them `sed -i -e 's/a/b/' <scratch>/Foo.cs` reads its script as
    a second file and the whole command is refused for naming a path that is not one.

    Unknown shapes fall through as operands, which DENIES — the safe direction for a rule whose
    answer only ever widens a permission.
    """
    value_letters = VALUE_CLUSTER_FLAGS.get(command, "")
    digit_letters = DIGIT_VALUE_CLUSTER_FLAGS.get(command, "")
    operands = []
    index = 0

    while index < len(args):
        word = args[index]

        # `--` ends the options; everything after it is an operand however it is spelled.
        if word == "--":
            operands.extend(args[index + 1:])
            break

        if not word.startswith("-") or word == "-":
            operands.append(word)
            index += 1
            continue

        # A long option carries its value with an `=` or not at all, as far as this reader goes.
        if word.startswith("--"):
            index += 1
            continue

        cluster = word[1:]
        position = 0
        takes_next = False

        while position < len(cluster):
            letter = cluster[position]

            # The rest of the cluster is this flag's value; if there is no rest, the next word is.
            if letter in value_letters:
                takes_next = position == len(cluster) - 1
                break

            if letter in digit_letters:
                position += 1
                while position < len(cluster) and cluster[position].isdigit():
                    position += 1
                continue

            position += 1

        index += 2 if takes_next else 1

    return operands


def file_verb_allowed(command, args, assignments):
    """Is this file verb contained by the reviewer's own scratch folder?"""
    if command not in SCRATCH_FILE_VERBS:
        return False

    for word in args:
        if word.split("=")[0] in DESTINATION_FLAGS:
            return False

    operands = [resolve_target(word, assignments) for word in operands_of(command, args)]

    # NO OPERAND IS NOT CONTAINMENT. `all()` over an empty list is True, which would have turned a
    # bare verb into a permission.
    if not operands:
        return False

    if command in DESTINATION_ONLY_VERBS:
        return len(operands) > 1 and is_in_own_scratch(operands[-1])

    return all(is_in_own_scratch(operand) for operand in operands)


def in_place_edit_allowed(command, args, assignments):
    """Does this in-place edit rewrite ONLY files inside the scratch folder?

    Every file operand, not just the last: sed takes as many as you give it and rewrites each one.
    """
    operands = operands_of(command, args)

    if command == "sed" and not sed_takes_script_from_a_flag(args):
        operands = operands[1:]

    if not operands:
        return False

    return all(is_in_own_scratch(resolve_target(word, assignments)) for word in operands)


def sed_takes_script_from_a_flag(args):
    """Was sed handed its script by -e/-f rather than as a bare first word?

    Deliberately generous: a cluster that merely CONTAINS one of the letters counts. Being wrong here
    keeps the script in the operand list, which denies — and denying an edit is the direction this
    rule is allowed to be wrong in.
    """
    for word in args:
        if word.split("=")[0] in SCRIPT_FLAG_LONG:
            return True
        if word.startswith("--") or not word.startswith("-"):
            continue
        if any(letter in SCRIPT_FLAG_LETTERS for letter in word[1:]):
            return True

    return False


def write_target_allowed(operator, target, assignments):
    """THE one write a reviewer may make, in one place because two rules need to agree on it.

    The redirect rule and `tee` are the same permission wearing different spellings, and an exemption
    implemented twice is an exemption that will drift — with the copy nobody is reading being the one
    that drifts. Whatever this returns True for is exactly what a reviewer can write, whichever
    syntax it reaches for.
    """
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
    resolved = resolve_target(target, assignments)

    # THE SCRATCH FOLDER TAKES ANY WRITING OPERATOR, and the truncating one is the point: a mutant is
    # written whole, not appended to. This sits ABOVE the append gate on purpose — that gate is the
    # own-channel rule, where append really is the whole of the permission, and the two must not be
    # confused for one another.
    if is_in_own_scratch(resolved):
        return True

    if operator != ">>":
        # Append is the whole of the permission. Nothing below needs to say so again.
        return False

    if is_file_in_own_folder(resolved):
        return True

    # The watcher baseline. Its NAME, not a substring of the path: `/tmp/watch-base/anything` used to
    # satisfy this, which is a directory that merely shares the word.
    #
    # FILED, NOT FIXED: nothing in this repo reads or writes a `watch-base` file — the current watcher
    # holds its baseline in memory — so this clause may be dead. Deleting a permission is a policy
    # call, so it is tightened here and left for the supervisor to decide.
    if resolved.rsplit("/", 1)[-1] == "watch-base":
        return True

    return False


def redirect_reason(operator, target, assignments):
    # Reading a file is not writing one.
    if operator in ("<", "<<", "<<<"):
        return None
    if target is None:
        return None

    # `>&` IS DECIDED BY ITS TARGET. `2>&1` and `>&2` duplicate a descriptor and write no file, which
    # is why this operator was exempted outright — but `>& out.log` is the csh spelling of "send both
    # streams to this FILE", and the blanket exemption waved it through. A descriptor is digits, or
    # `-` for closing the stream; anything else is a name.
    if operator == ">&" and target is not UNRESOLVED and (target.isdigit() or target == "-"):
        return None

    # Handed straight back to `analyse`, which decides what it means once every command has been
    # classified. This branch is the ONLY thing an unreadable target may affect.
    if target is UNRESOLVED:
        return UNRESOLVED

    return None if write_target_allowed(operator, target, assignments) else "redirect"


def analyse(source, depth=0):
    commands, redirects = split_commands(tokenize(strip_comments_and_heredoc_bodies(source)))
    assignments = leading_assignments(commands)

    for words in commands:
        reason = classify_words(words, depth, assignments)
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
  # Its own message rather than borrowing the redirect one: a reviewer told its `tee` "redirects
  # output" has to work out what the guard actually objected to, and the whole point of these strings
  # is that a refusal teaches. `tee -a` into its own channel is permitted and does not reach here.
  "DENY tee")
    deny "that command writes output into a file. You may pipe into tee only with -a and only into your own channel under your member folder." ;;
  UNDECIDABLE*)
    aiorch_log_undecidable "whether this command mutates anything" "${VERDICT#UNDECIDABLE }"
    exit 0 ;;
esac

exit 0

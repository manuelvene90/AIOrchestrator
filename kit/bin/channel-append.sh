#!/bin/bash
#
# channel-append.sh — the ONE way a session appends to a supervision channel.
#
# It exists because "re-read the last header and add one" cannot be made safe by trying harder: the
# window it leaves open IS the write. Two writers who both read [71] both write [72], and a writer
# that emits its entry in more than one write() call gets another author's header dropped into the
# middle of it. Both happened on 2026-08-13, five minutes apart, and the second put a reviewer's
# nine findings under the supervisor's header — an audit trail that misattributes under load is
# worse than one that drops entries, because it is confidently wrong.
#
# WHAT THIS GUARANTEES: writers that use this helper are serialised against every other writer that
# uses it, INCLUDING the app, which takes the same lock from .NET. It cannot bind a writer that does
# not ask. A session can append with a bare redirect and nothing here will stop it — every session
# runs as the same OS user and no location is out of its reach. So the true sentence is "writers
# using the protocol cannot collide with each other", never "channel appends are atomic".
#
# The lock is a DIRECTORY, because mkdir is a single atomic syscall that fails when the target
# exists — the one exclusive-create bash and .NET can both perform without either emulating the
# other. flock was rejected: msys flock and Windows LockFileEx are different mechanisms, and
# assuming msys/Windows equivalence is how this repo has already produced silent failures.
#
# Exit codes are meant to be distinguishable, because "could not acquire" and "wrote it" must never
# look alike to the caller:
#   0  the entry was appended; its index is printed on stdout
#   2  usage error
#   3  COULD NOT ACQUIRE THE LOCK within the budget — nothing was written, retry is the caller's
#   4  an I/O error; nothing was appended
#
set -u

STALE_SECONDS=60          # Must match ChannelFile_Lock.STALE_SECONDS. See its comment: a GUESS,
                          # deliberately conservative, invalidated only by a writer that does
                          # something slow while holding the lock — which nothing may do.
RETRY_INITIAL_MS=50
RETRY_MAX_MS=400
DEFAULT_BUDGET_SECONDS=10

usage() {
  echo "usage: channel-append.sh --channel <file> --subject <text> (--body-file <file> | --body -)" >&2
  echo "                         [--author <word>] [--type <kind>] [--budget-seconds N]" >&2
  echo "" >&2
  echo "  typed entry (composed and VALIDATED here, so a malformed one is refused before the write):" >&2
  echo "    --question <text>     one question for the owner; needs 2-4 --option" >&2
  echo "    --option <label>      repeatable; ${MAX_OPTIONS:-4} at most, ${OPTION_WIDTH:-28} characters each" >&2
  echo "    --recommend <text>    which option you would take, and why in one clause" >&2
  echo "    --risk low|medium|high" >&2
  echo "    --row <id>            the ledger row this decides" >&2
  echo "    --deadline <2h|90m|N> bound a question that can otherwise wait for ever" >&2
  echo "    --default <n>         the option number applied when the deadline passes (1-based)" >&2
  echo "    --state <text>        your one-line state for PULSE, at turn end" >&2
  echo "    --report <text>       a plain report body" >&2
  echo "    --attach <path>       repeatable; a file the owner should receive" >&2
  echo "    --to owner|member     which channel kind this is for (checked against --channel)" >&2
  echo "" >&2
  echo "  The index and the timestamp are computed HERE, never by the caller (CLAUDE.md decision 12)." >&2
  echo "  The author is derived from AIORCH_ROLE/AIORCH_MEMBER; a mismatching --author is refused." >&2
  exit 2
}

CHANNEL=""; AUTHOR=""; SUBJECT=""; BODY_FILE=""; BUDGET_SECONDS="$DEFAULT_BUDGET_SECONDS"

# ---- the typed entry (E3) -----------------------------------------------------------------------
ENTRY_TYPE=""; QUESTION=""; RECOMMEND=""; RISK=""; ROW=""; STATE_LINE=""; REPORT=""; TO_KIND=""
DEADLINE=""; DEFAULT_OPTION=""
OPTIONS=(); ATTACHMENTS=()

while [ $# -gt 0 ]; do
  case "$1" in
    --channel)        CHANNEL="${2:-}"; shift 2 ;;
    --author)         AUTHOR="${2:-}"; shift 2 ;;
    --subject)        SUBJECT="${2:-}"; shift 2 ;;
    --body-file)      BODY_FILE="${2:-}"; shift 2 ;;
    --budget-seconds) BUDGET_SECONDS="${2:-}"; shift 2 ;;
    --type)           ENTRY_TYPE="${2:-}"; shift 2 ;;
    --question)       QUESTION="${2:-}"; shift 2 ;;
    --option)         OPTIONS+=("${2:-}"); shift 2 ;;
    --recommend)      RECOMMEND="${2:-}"; shift 2 ;;
    --risk)           RISK="${2:-}"; shift 2 ;;
    --row)            ROW="${2:-}"; shift 2 ;;
    --state)          STATE_LINE="${2:-}"; shift 2 ;;
    --report)         REPORT="${2:-}"; shift 2 ;;
    --attach)         ATTACHMENTS+=("${2:-}"); shift 2 ;;
    --to)             TO_KIND="${2:-}"; shift 2 ;;
    --deadline)       DEADLINE="${2:-}"; shift 2 ;;
    --default)        DEFAULT_OPTION="${2:-}"; shift 2 ;;
    -h|--help)        usage ;;
    *) echo "channel-append.sh: unknown argument '$1'" >&2; usage ;;
  esac
done

# ---- the grammar, read from the ONE file the app also reads (E3 requirement 1) -----------------
#
# It sits beside this script in the kit, so a tool without its grammar is not a shape that can ship:
# AIOrchestrator.csproj copies kit/grammar/ next to kit/bin/. The app reads the SAME file, embedded
# as a resource, and ChannelGrammarTests fails if the two copies differ by a byte.
#
# WHY jq AND NOT grep: the markers carry colons and spaces ("BLOCKED ON OWNER"), and a grep-based
# reader of JSON is a parser nobody wrote on purpose. jq is already required by the installer.
GRAMMAR_FILE="${AIORCH_CHANNEL_GRAMMAR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../grammar" 2>/dev/null && pwd)/channel-grammar.json}"

grammar() {
  # A missing key is fatal, never empty: an empty marker writes an entry the app cannot recognise,
  # which is the silent half of the drift E3 exists to end.
  local value
  value="$(jq -er "$1" "$GRAMMAR_FILE" 2>/dev/null)" || {
    echo "channel-append.sh: the channel grammar has no '$1' (looked in '$GRAMMAR_FILE'). Every marker" >&2
    echo "                   this tool writes comes from there, so there is nothing to fall back to." >&2
    exit 4
  }
  printf '%s' "$value"
}

TYPED_CALL=0
if [ -n "$ENTRY_TYPE" ] || [ -n "$QUESTION" ] || [ -n "$STATE_LINE" ] || [ -n "$REPORT" ] \
   || [ ${#OPTIONS[@]} -gt 0 ] || [ ${#ATTACHMENTS[@]} -gt 0 ] || [ -n "$RECOMMEND" ] \
   || [ -n "$RISK" ] || [ -n "$ROW" ] || [ -n "$DEADLINE" ] || [ -n "$DEFAULT_OPTION" ]; then
  TYPED_CALL=1
fi

if [ "$TYPED_CALL" = "1" ]; then
  # ONE LINE, NAMING THE FIX. A typed entry reads the grammar with jq; without it there is no way to
  # know what a marker is spelled like, and guessing is the drift E3 removed. install.sh has always
  # required jq and install.ps1 did not until 2026-09-10 — so this refusal is the one a Windows
  # session actually hits, in msys bash, and it has to be actionable on its own.
  command -v jq >/dev/null 2>&1 || {
    echo "channel-append.sh: REFUSED — a typed entry needs jq to read the channel grammar and jq is not on PATH. NOTHING WAS WRITTEN. Install it (winget install jqlang.jq / brew install jq / apt install jq) — on Windows it must be on the PATH msys bash sees — or write this entry with --body-file." >&2
    exit 4
  }

  [ -f "$GRAMMAR_FILE" ] || {
    echo "channel-append.sh: cannot find the channel grammar at '$GRAMMAR_FILE'." >&2
    echo "                   It ships beside this script in the kit; set AIORCH_CHANNEL_GRAMMAR to point at it." >&2
    exit 4
  }

  MAX_LINES="$(grammar '.ceilings.max_lines')"
  MAX_CHARACTERS="$(grammar '.ceilings.max_characters')"
  MIN_OPTIONS="$(grammar '.ceilings.min_options')"
  MAX_OPTIONS="$(grammar '.ceilings.max_options')"
  OPTION_WIDTH="$(grammar '.ceilings.option_label_width')"
  DEADLINE_MAX_HOURS="$(grammar '.deadline.maximum_hours')"
fi

# ---- the author is the SESSION's, not the caller's word (E3 requirement 2) ----------------------
#
# `--author <word>` accepted anything, and a member signing as "supervisor" is how a model choice got
# erased from a brief: the entry was believed because of the name on it. The launcher already exports
# the role and the member id to every session, so the truth is in the process and the flag is at best
# a restatement of it.
#
# OUTSIDE A SESSION IT STILL WORKS. The app itself appends through the same lock from .NET, and a
# human debugging by hand has no AIORCH_ROLE — so with nothing exported, `--author` is taken as given.
# What is refused is the case that actually lied: a session that HAS a role claiming another one.
derive_author() {
  if [ -n "${AIORCH_MEMBER:-}" ]; then
    printf '%s' "$AIORCH_MEMBER"
  elif [ -n "${AIORCH_ROLE:-}" ]; then
    printf '%s' "$AIORCH_ROLE"
  else
    printf ''
  fi
}

SESSION_AUTHOR="$(derive_author)"

if [ -n "$SESSION_AUTHOR" ]; then
  if [ -n "$AUTHOR" ] && [ "$AUTHOR" != "$SESSION_AUTHOR" ]; then
    echo "channel-append.sh: REFUSED — --author '$AUTHOR' is not this session's identity ('$SESSION_AUTHOR', from AIORCH_MEMBER/AIORCH_ROLE)." >&2
    echo "                   Nothing was written. An entry signed with another role's name is believed because of the name on it;" >&2
    echo "                   that is how a member's brief was once attributed to the supervisor. Drop --author, or fix it." >&2
    exit 2
  fi

  AUTHOR="$SESSION_AUTHOR"
fi

[ -n "$CHANNEL" ] && [ -n "$AUTHOR" ] && [ -n "$SUBJECT" ] || usage
[ -n "$BODY_FILE" ] || [ "$TYPED_CALL" = "1" ] || usage

# THE BUDGET IS VALIDATED HERE, BEFORE ANY ARITHMETIC SEES IT, and this is a lock-safety rule rather
# than input hygiene. `$(( 2.5 * 1000 ))` is a bash SYNTAX ERROR, and a syntax error inside
# acquire_lock aborts the function AND the `if ! acquire_lock` guard around it: execution resumed in
# the critical section with no lock held, appended, and exited 0 — a torn write reported as a
# serialised one, which is the exact failure this whole file exists to prevent (measured 2026-09-06
# with `--budget-seconds 2.5`, and every role command invites exactly that by telling a session to
# raise the budget when the channel is busy). Decimals are ACCEPTED, as the awk form this replaced
# accepted them; anything else stops the run before it can write.
case "$BUDGET_SECONDS" in
  ''|*[!0-9.]*|*.*.*|.) echo "channel-append.sh: --budget-seconds must be a number of seconds (e.g. 10 or 2.5), got '$BUDGET_SECONDS'" >&2; exit 2 ;;
esac

# Seconds to milliseconds without floating point: whole part x1000 plus the first three decimals,
# right-padded. Pure shell, so no external tool's dialect can decide whether the lock works.
BUDGET_WHOLE="${BUDGET_SECONDS%%.*}"
BUDGET_FRACTION="${BUDGET_SECONDS#*.}"
[ "$BUDGET_FRACTION" = "$BUDGET_SECONDS" ] && BUDGET_FRACTION=""
BUDGET_FRACTION="$(printf '%s000' "$BUDGET_FRACTION" | cut -c1-3)"
BUDGET_MS=$(( ${BUDGET_WHOLE:-0} * 1000 + ${BUDGET_FRACTION:-0} ))

[ "$BUDGET_MS" -gt 0 ] || { echo "channel-append.sh: --budget-seconds must be greater than zero, got '$BUDGET_SECONDS'" >&2; exit 2; }

# ---- a typed entry is VALIDATED AND COMPOSED HERE, before anything is written -------------------
#
# The point of the tool is that a malformed entry never reaches the channel. The app already coaches
# after the fact (OwnerMessage_Contract), and coaching after the fact is a message the owner's phone
# has already carried — so the same rules run one moment earlier, and every fault is NAMED.
#
# FAULTS ARE COLLECTED, NOT THROWN ONE AT A TIME. A caller told "missing --option" fixes that and is
# then told "too long", which is two round trips for one entry. Everything wrong is reported together.
if [ "$TYPED_CALL" = "1" ]; then
  FAULTS=()

  if [ -n "$BODY_FILE" ]; then
    FAULTS+=("--body-file cannot be combined with the typed flags: the body is composed from them, so passing both means two bodies and no way to choose.")
  fi

  # --to is checked against the channel it was given, because the two disagreeing is a real defect
  # and not a formality: an owner question appended to a member spoke is a question nobody answers.
  case "$TO_KIND" in
    ''|owner|member) : ;;
    *) FAULTS+=("--to must be 'owner' or 'member', got '$TO_KIND'.") ;;
  esac

  if [ "$TO_KIND" = "owner" ] && [ -n "$CHANNEL" ]; then
    case "$CHANNEL" in
      *owner-channel.md) : ;;
      *) FAULTS+=("--to owner but --channel is '$(basename "$CHANNEL")', which is not an owner channel — an owner question on a member spoke is a question nobody answers.") ;;
    esac
  fi

  # The declared type: either given, or inferred from the flags that can only mean one thing. It is
  # PERSISTED (E3 requirement 3), so the state pack and the digest read it instead of guessing from
  # the subject's first word.
  if [ -z "$ENTRY_TYPE" ]; then
    if [ -n "$QUESTION" ]; then ENTRY_TYPE="question"
    elif [ -n "$STATE_LINE" ]; then ENTRY_TYPE="state"
    elif [ -n "$REPORT" ]; then ENTRY_TYPE="report"
    elif [ ${#ATTACHMENTS[@]} -gt 0 ]; then ENTRY_TYPE="attachment"
    fi
  fi

  if [ -z "$ENTRY_TYPE" ]; then
    FAULTS+=("--type is missing and cannot be inferred: pass one of $(grammar '.types.values | join(\", \")').")
  elif ! jq -e --arg t "$ENTRY_TYPE" '.types.values | index($t)' "$GRAMMAR_FILE" >/dev/null 2>&1; then
    FAULTS+=("--type '$ENTRY_TYPE' is not a type this app knows: $(grammar '.types.values | join(\", \")').")
  fi

  # A QUESTION owes the owner a choice. Two to four (brief E2) — fewer is not a choice, more is a
  # list. Both bounds are the grammar's, so the tool and the app cannot disagree about them.
  if [ -n "$QUESTION" ] || [ ${#OPTIONS[@]} -gt 0 ]; then
    if [ -z "$QUESTION" ]; then
      FAULTS+=("--option was given without --question: options with nothing to decide are buttons that answer nothing.")
    fi

    if [ ${#OPTIONS[@]} -lt "$MIN_OPTIONS" ]; then
      FAULTS+=("--question needs at least $MIN_OPTIONS --option (got ${#OPTIONS[@]}): a question with one option is not a choice.")
    fi

    if [ ${#OPTIONS[@]} -gt "$MAX_OPTIONS" ]; then
      FAULTS+=("--question takes at most $MAX_OPTIONS --option (got ${#OPTIONS[@]}): past that the owner is reading a list, not making a choice.")
    fi

    for option in ${OPTIONS+"${OPTIONS[@]}"}; do
      if [ -z "${option// }" ]; then
        FAULTS+=("an --option is empty: a blank button is one the owner cannot read.")
      elif [ "${#option}" -gt "$OPTION_WIDTH" ]; then
        FAULTS+=("--option '$option' is ${#option} characters; $OPTION_WIDTH is what fits one line of a phone button, so longer labels get replaced by numbers.")
      fi
    done
  fi

  if [ -n "$RISK" ]; then
    if ! jq -e --arg r "$RISK" '.risk_levels.values | index($r)' "$GRAMMAR_FILE" >/dev/null 2>&1; then
      FAULTS+=("--risk must be one of $(grammar '.risk_levels.values | join(\", \")'), got '$RISK': a risk nobody can compare is not a risk.")
    fi
  fi

  # A DEADLINE IS 2h, 90m, OR A BARE NUMBER OF MINUTES, and past the grammar's ceiling the app reads
  # it as NO deadline at all — so a value beyond it is refused here rather than silently meaning the
  # opposite of what was written.
  if [ -n "$DEADLINE" ]; then
    case "$DEADLINE" in
      ''|*[!0-9hm]*) FAULTS+=("--deadline must be like '2h', '90m' or a bare number of minutes, got '$DEADLINE'.") ;;
      *h)
        deadline_hours="${DEADLINE%h}"
        if [ -z "$deadline_hours" ] || [ "$deadline_hours" -gt "$DEADLINE_MAX_HOURS" ]; then
          FAULTS+=("--deadline '$DEADLINE' is past the ${DEADLINE_MAX_HOURS}h ceiling, which the app reads as NO deadline — the opposite of what you wrote.")
        fi
        ;;
      *) : ;;
    esac

    if [ -z "$QUESTION" ]; then
      FAULTS+=("--deadline was given without --question: there is nothing for it to bound.")
    fi
  fi

  # THE DEFAULT IS THE OPTION NUMBER AS THE OWNER SEES IT — 1-based, matching the buttons. An
  # out-of-range default is applied to nothing, so the question would expire having decided nothing
  # while reporting that a default was set.
  if [ -n "$DEFAULT_OPTION" ]; then
    case "$DEFAULT_OPTION" in
      ''|*[!0-9]*) FAULTS+=("--default must be an option NUMBER as the owner sees it (1-based), got '$DEFAULT_OPTION'.") ;;
      *)
        if [ "$DEFAULT_OPTION" -lt 1 ] || [ "$DEFAULT_OPTION" -gt "${#OPTIONS[@]}" ]; then
          FAULTS+=("--default $DEFAULT_OPTION names no option: there are ${#OPTIONS[@]}, numbered 1 to ${#OPTIONS[@]}.")
        fi
        ;;
    esac

    if [ -z "$DEADLINE" ]; then
      FAULTS+=("--default without --deadline is never applied: a default is what happens when the deadline passes.")
    fi
  fi

  for attachment in ${ATTACHMENTS+"${ATTACHMENTS[@]}"}; do
    [ -f "$attachment" ] || FAULTS+=("--attach '$attachment' does not exist: the entry would name a file the owner never receives.")
  done

  if [ ${#FAULTS[@]} -gt 0 ]; then
    echo "channel-append.sh: REFUSED — NOTHING WAS WRITTEN. $(printf '%s' "${#FAULTS[@]}") thing(s) to fix:" >&2
    for fault in "${FAULTS[@]}"; do
      echo "  - $fault" >&2
    done
    exit 2
  fi

  # ---- composed from the grammar's own marker words ---------------------------------------------
  BODY_FILE="$(mktemp)" || { echo "channel-append.sh: cannot create a temp file" >&2; exit 4; }
  trap 'rm -f "$BODY_FILE"' EXIT

  # EVERY OPTIONAL LINE IS AN `if`, NOT AN `&&`, and that is a bug this cost once: a group whose LAST
  # command is `[ -n "$X" ] && printf …` exits non-zero when X is empty, so composing a question with
  # no --state reported "could not compose the entry" and wrote nothing. An `if` with a false
  # condition and no else exits 0, which is what a skipped optional line means.
  {
    if [ -n "$REPORT" ]; then printf '%s\n' "$REPORT"; fi

    if [ -n "$QUESTION" ]; then
      # The question LAST among the prose, and the option block under it: a question is the last
      # thing in an entry or it is a moving target (OwnerMessage_Contract's ProseAfterTheQuestion).
      if [ -n "$ROW" ]; then printf '%s %s\n' "$(grammar '.markers.row')" "$ROW"; fi
      if [ -n "$RISK" ]; then printf '%s %s\n' "$(grammar '.markers.risk')" "$RISK"; fi
      printf '%s %s\n' "$(grammar '.markers.question')" "$QUESTION"
      for option in ${OPTIONS+"${OPTIONS[@]}"}; do
        printf '%s %s\n' "$(grammar '.markers.option')" "$option"
      done
      if [ -n "$RECOMMEND" ]; then printf '%s %s\n' "$(grammar '.markers.recommend')" "$RECOMMEND"; fi
      if [ -n "$DEADLINE" ]; then printf '%s %s\n' "$(grammar '.markers.deadline')" "$DEADLINE"; fi
      if [ -n "$DEFAULT_OPTION" ]; then printf '%s %s\n' "$(grammar '.markers.default')" "$DEFAULT_OPTION"; fi
    fi

    for attachment in ${ATTACHMENTS+"${ATTACHMENTS[@]}"}; do
      printf '%s %s\n' "$(grammar '.markers.attach')" "$attachment"
    done

    # The declared state goes last so it is the entry's final line, which is where the bridge strips
    # it from the body after reading it into PULSE.
    if [ -n "$STATE_LINE" ]; then printf '%s %s\n' "$(grammar '.markers.state')" "$STATE_LINE"; fi
  } > "$BODY_FILE" || { echo "channel-append.sh: could not compose the entry" >&2; exit 4; }

  # THE CEILINGS, MEASURED ON WHAT WAS ACTUALLY COMPOSED — not on the flags. The owner reads the
  # entry, so the entry is what has to fit (brief E2: 5 lines, 600 characters).
  #
  # THE LINE COUNT IS PROSE ONLY, AND THAT IS A RULING THIS TOOL HAD TO MAKE. A well-formed question
  # is SIX marker lines by construction — ROW:, RISK:, QUESTION:, two OPTION:, RECOMMEND: — so a
  # ceiling that counted them refused every valid question, which is what the first version of this
  # check did. The app's own OwnerMessage_Contract.Is_TooLong counts every non-blank line and so
  # coaches every question as too long: one of the "pairs that cannot both be obeyed" the E3 audit
  # measured, still standing. The reading that makes both rules obeyable is that the ceiling is about
  # PROSE — the sentences the owner reads — while a marker line is structure the app turns into a
  # button or a field. Raised with the owner; the tool cannot wait for the answer, because refusing
  # every question is not a usable default. The CHARACTER ceiling still counts everything: a wall of
  # text is a wall whatever the marker at its left edge.
  MARKER_PREFIXES="$(jq -r '.markers | to_entries[] | select(.key != "_comment") | .value' "$GRAMMAR_FILE")"

  BODY_PROSE_LINES=0
  while IFS= read -r line; do
    [ -z "${line// }" ] && continue

    is_marker=0
    while IFS= read -r marker; do
      [ -z "$marker" ] && continue
      case "$line" in
        "$marker"*) is_marker=1; break ;;
      esac
    done <<< "$MARKER_PREFIXES"

    [ "$is_marker" = "1" ] || BODY_PROSE_LINES=$((BODY_PROSE_LINES + 1))
  done < "$BODY_FILE"

  BODY_CHARACTERS="$(wc -c < "$BODY_FILE" | tr -d ' ')"

  if [ "$BODY_PROSE_LINES" -gt "$MAX_LINES" ] || [ "$BODY_CHARACTERS" -gt "$MAX_CHARACTERS" ]; then
    echo "channel-append.sh: REFUSED — NOTHING WAS WRITTEN. The composed entry is $BODY_PROSE_LINES lines of prose and $BODY_CHARACTERS characters;" >&2
    echo "                   the ceiling is $MAX_LINES lines and $MAX_CHARACTERS characters. Say less, or move the detail to a spoke." >&2
    echo "                   (Marker lines — the question, its options, the recommendation — are not counted as prose.)" >&2
    exit 2
  fi
fi

if [ "$BODY_FILE" = "-" ]; then
  BODY_FILE="$(mktemp)" || { echo "channel-append.sh: cannot create a temp file" >&2; exit 4; }
  cat > "$BODY_FILE"
  trap 'rm -f "$BODY_FILE"' EXIT
fi

[ -f "$BODY_FILE" ] || { echo "channel-append.sh: body file '$BODY_FILE' does not exist" >&2; exit 2; }
[ -f "$CHANNEL" ] || { echo "channel-append.sh: channel '$CHANNEL' does not exist" >&2; exit 2; }

LOCK_DIR="${CHANNEL}.lock"
OWNER_FILE="$LOCK_DIR/owner"
HELD=0

# Identifies THIS acquisition, not this process. $$ alone is not enough: pids are reused, and the
# question at release time is "is this still the lock I took", which a pid cannot answer.
OWNERSHIP_TOKEN="$$-$(date -u +%s)-${RANDOM}${RANDOM}"

release_lock() {
  # Two guards, and the second one is the important one.
  #
  # HELD stops a run that never acquired from deleting somebody else's lock on its way out.
  #
  # The TOKEN stops something subtler and worse: our lock being broken as stale mid-write and
  # re-acquired by another writer, after which deleting "$LOCK_DIR" by path would destroy THEIR
  # lock while they are writing, and a third writer could then acquire alongside them. The stale
  # break exists to recover from a dead holder, and without this check it arms that. The token is
  # minted per acquire — a pid is reused and a path is reused, an acquisition is not.
  if [ "$HELD" = "1" ]; then
    local held_token
    held_token="$(grep -m1 '^token=' "$OWNER_FILE" 2>/dev/null | cut -d= -f2-)"

    if [ "$held_token" = "$OWNERSHIP_TOKEN" ]; then
      rm -rf "$LOCK_DIR" 2>/dev/null || true
    else
      echo "channel-append.sh: NOT releasing the lock on $(basename "$CHANNEL") — it was broken as stale and another writer holds it now; this write overran ${STALE_SECONDS}s." >&2
    fi

    HELD=0
  fi
}
trap 'release_lock' EXIT INT TERM

# Returns 0 when the holder looks dead. Anything it cannot READ or PARSE counts as ALIVE: a false
# "alive" costs a wait, a false "dead" breaks a live lock and corrupts the file the lock protects.
# Echoes the epoch the holder took the lock, or NOTHING when the metadata cannot be TRUSTED —
# deliberately one answer covering every reason: the file is missing, it has no utc line, the stamp
# will not parse, or the stamp is in the FUTURE.
#
# The future case belongs here rather than at the call site because it is not a different kind of
# problem. Staleness is now - held, so a future stamp makes that negative, it never exceeds the
# threshold, the lock is never stale, and a dead holder wedges the channel forever — the same outcome
# as a stamp that will not parse. Clock skew between a session and the app is enough, on a file two
# languages write.
usable_held_epoch() {
  local stamp epoch
  [ -f "$OWNER_FILE" ] || return 1

  stamp="$(grep -m1 '^utc=' "$OWNER_FILE" 2>/dev/null | cut -d= -f2-)"
  [ -n "$stamp" ] || return 1

  # GNU date first, BSD date (macOS) second: `-d` is GNU-only, and on macOS it fails — which fell
  # through to the directory-age fallback and quietly made every lock look metadata-less there.
  epoch="$(date -u -d "$stamp" +%s 2>/dev/null)" \
    || epoch="$(date -u -j -f '%Y-%m-%dT%H:%M:%SZ' "$stamp" +%s 2>/dev/null)" \
    || return 1
  [ -n "$epoch" ] || return 1

  [ "$epoch" -le "$(date -u +%s)" ] || return 1

  printf '%s' "$epoch"
}

lock_is_stale() {
  [ -d "$LOCK_DIR" ] || return 1

  local held_epoch
  held_epoch="$(usable_held_epoch)" || held_epoch=""

  # ONE condition, not a row of special cases. The metadata is either usable or it is not, and every
  # way of not being usable has the same answer: fall back to the age of the directory, the one clock
  # this script can vouch for. This was a chain of guards that happened to share a recovery, which is
  # not one condition — it is several, and the next route gets added beside them. That is not
  # hypothetical: this defect reached production by four separate routes.
  [ -n "$held_epoch" ] || { directory_is_older_than_stale; return $?; }

  [ $(( $(date -u +%s) - held_epoch )) -gt "$STALE_SECONDS" ]
}

# The one recovery path for "the owner file cannot be trusted", whatever the reason — absent,
# unparseable, or stamped in the future. A live acquire is microseconds old.
directory_is_older_than_stale() {
  local dir_epoch now_epoch
  dir_epoch="$(date -u -r "$LOCK_DIR" +%s 2>/dev/null)" || return 1
  [ -n "$dir_epoch" ] || return 1
  now_epoch="$(date -u +%s)"

  [ $((now_epoch - dir_epoch)) -gt "$STALE_SECONDS" ]
}

# Breaking is a RENAME, never a delete. Two writers can both judge the same lock stale; if both
# deleted it both would then acquire, producing exactly the collision this exists to prevent. Only
# one rename can win. The broken lock is kept as evidence of a writer that died holding it.
break_if_stale() {
  if lock_is_stale; then
    # Read the stamp BEFORE the move: afterwards the path is gone, and a diagnostic that cannot say
    # WHEN the dead holder took the lock is not evidence of anything.
    local held_since
    held_since="$(grep -m1 '^utc=' "$OWNER_FILE" 2>/dev/null | cut -d= -f2-)"

    if mv "$LOCK_DIR" "${LOCK_DIR}.broken.$$.$(date -u +%s)" 2>/dev/null; then
      echo "channel-append.sh: broke a stale lock on $(basename "$CHANNEL") — its holder took it at ${held_since:-an unrecorded time} and never released it" >&2
    fi
  fi
}

# A SESSION WOKEN BY ITS OWN APPEND RELOADS ITS WHOLE CONTEXT TO LEARN NOTHING, and that was about
# half of every wake on the owner's machine: the watchers fingerprint the channel file, and a
# fingerprint cannot tell whose write changed it. So the writer says so, from inside the lock, where
# the answer is not a guess.
#
# TWO FACTS, because the fingerprint alone would swallow an owner's message. If they wrote at 23:14
# and we appended our own entry at 23:15, the file's fingerprint is still exactly the one our write
# left behind — and a watcher suppressing on that alone would sleep through them. So the record also
# carries the size the channel had when our unbroken run of writes STARTED. A watcher suppresses only
# when the fingerprint matches AND that start is at or before the last size it saw for itself:
# anything foreign in between moves the start past it, and it fires.
#
# The run is EXTENDED rather than restarted while our writes stay consecutive (the previous record's
# size is still the file's size when we take it again), so a session appending twice in one turn is
# not woken by its own second entry. The moment anything else appends, the next write starts a new
# run after it.
#
# Per AUTHOR, never one file per channel: a supervisor and an implementer both watch the implementer's
# channel, and a shared record would let the supervisor's append suppress the IMPLEMENTER's wake —
# turning a token saving into a missed brief.
record_self_write() {
  local safe_author previous_after previous_after_size start_size after_size after_hash

  safe_author="$(printf '%s' "$AUTHOR" | tr -c 'A-Za-z0-9_-' '_')"
  SELF_WRITE_FILE="${CHANNEL}.self-write.${safe_author}"

  after_size="$(wc -c < "$CHANNEL" 2>/dev/null | tr -d ' ')" || return 0
  after_hash="$(md5sum "$CHANNEL" 2>/dev/null)" || return 0
  [ -n "$after_size" ] && [ -n "$after_hash" ] || return 0

  previous_after="$(grep -m1 '^after=' "$SELF_WRITE_FILE" 2>/dev/null | cut -d= -f2-)"
  previous_after_size="${previous_after%% *}"

  if [ -n "$previous_after_size" ] && [ "$previous_after_size" = "$BEFORE_SIZE" ]; then
    start_size="$(grep -m1 '^start=' "$SELF_WRITE_FILE" 2>/dev/null | cut -d= -f2-)"
  fi

  [ -n "${start_size:-}" ] || start_size="$BEFORE_SIZE"

  # Best effort throughout: a missing record costs one needless wake, which is the behaviour this
  # replaces. Never a reason to fail an append that already landed.
  printf 'start=%s\nafter=%s %s\n' "$start_size" "$after_size" "${after_hash%% *}" \
    > "$SELF_WRITE_FILE" 2>/dev/null || true
}

acquire_lock() {
  local waited_ms=0 budget_ms delay_ms="$RETRY_INITIAL_MS"
  # Computed and validated at parse time (see BUDGET_MS): macOS's awk rejected the printf form this
  # replaced, budget_ms came back EMPTY, and the helper spun on a held lock for ever.
  budget_ms="$BUDGET_MS"

  while true; do
    # mkdir, NOT the stage-and-rename shape C# uses, and this asymmetry is deliberate and measured.
    #
    # C# can fill a staging directory and rename it into place because Directory.Move fails on ANY
    # existing target, so the lock never exists without its metadata. bash has no primitive with
    # that behaviour. Measured on this machine:
    #   plain `mv src dst` on an existing dst  -> moves src INSIDE dst and reports SUCCESS
    #   `mv -T src dst` on a non-empty dst     -> fails (correct)
    #   `mv -T src dst` on an EMPTY dst        -> SUCCEEDS, replacing it
    # That last one is disqualifying: a writer that had just mkdir'd its lock and not yet written
    # the metadata would have it stolen, and TWO writers would believe they held it — the exact
    # collision this protocol exists to prevent, and worse than the wedge it was meant to fix.
    #
    # mkdir fails on any existing directory, empty or not, which is the exclusivity required. The
    # window it leaves (lock created, metadata not yet written) is closed downstream instead:
    # lock_is_stale falls back to the DIRECTORY's age, so an abandoned empty lock is breakable
    # rather than permanent. The state is recovered from rather than removed, because removing it
    # is not available here at an acceptable price.
    if mkdir "$LOCK_DIR" 2>/dev/null; then
      HELD=1
      printf 'pid=%s\nutc=%s\nrole=%s\ntoken=%s\n' "$$" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "session" "$OWNERSHIP_TOKEN" > "$OWNER_FILE"
      return 0
    fi

    break_if_stale

    [ "$waited_ms" -ge "$budget_ms" ] && return 1

    sleep "$((delay_ms / 1000)).$(printf '%03d' $((delay_ms % 1000)))"
    waited_ms=$((waited_ms + delay_ms))
    delay_ms=$((delay_ms * 2))
    [ "$delay_ms" -gt "$RETRY_MAX_MS" ] && delay_ms="$RETRY_MAX_MS"
  done
}

if ! acquire_lock; then
  echo "channel-append.sh: COULD NOT ACQUIRE the lock on '$CHANNEL' within ${BUDGET_SECONDS}s — NOTHING WAS WRITTEN." >&2
  echo "channel-append.sh: retry; do not append without the lock, that is the collision this prevents." >&2
  exit 3
fi

# ---- critical section -------------------------------------------------------------------------
# THE LAST GATE, and it is not redundant with the `if ! acquire_lock` above. That guard was bypassed
# once already — an arithmetic syntax error aborted the function and the condition with it — and the
# cost was an entry written outside the lock and reported as success. `HELD` is set at the one place
# the lock is actually taken, so this asks the only question that matters here: do we hold it?
[ "$HELD" = "1" ] || {
  echo "channel-append.sh: reached the write with no lock held — refusing to append. NOTHING WAS WRITTEN." >&2
  exit 3
}

# The index is read HERE, inside the lock, which is what stops two writers choosing the same one.
# Read it outside and the lock protects the write while leaving the decision it depends on racing.
#
# THE C# PARSER IS AUTHORITATIVE, and this pattern is a transcription of its regex
# (ChannelEntry_Parser.Header_Regex): ^##\s*\[(\d+)\]\s*FROM\s+(\S+). Any whitespace after the
# hashes, any whitespace before FROM, and FROM is REQUIRED.
#
# It used to require exactly one space and no FROM, which made the two scanners disagree — a header
# written "##  [82] FROM x" was counted by the app and invisible here. The dangerous direction is
# this side UNDER-counting: the app appends [84], a session then acquires the lock cleanly, sees a
# maximum of 82, and mints a duplicate. Allocating the index inside the lock is what made the
# duplicate-index defect one problem instead of two, and two scanners that disagree hand it back.
LAST_INDEX="$(grep -oE '^##[[:space:]]*\[[0-9]+\][[:space:]]*FROM[[:space:]]' "$CHANNEL" 2>/dev/null | grep -oE '[0-9]+' | sort -n | tail -1)"
[ -n "$LAST_INDEX" ] || LAST_INDEX=0
NEXT_INDEX=$((LAST_INDEX + 1))

# The HIGHEST index, not the last line's: once a collision has happened the file is no longer
# sorted, and numbering from the tail hands out an index that already exists further up.

# The helper stamps the time itself. An agent writing its own stamp is guessing — a future stamp
# blanks the app's time-on-task display, and one was observed 10 hours ahead of the entry it sat on.
STAMP="$(date +'%Y-%m-%d %H:%M')"

# The size BEFORE our append, read inside the lock. Half of the self-write record below; see it for
# why the fingerprint alone is not enough.
BEFORE_SIZE="$(wc -c < "$CHANNEL" 2>/dev/null | tr -d ' ')"

STAGED_ENTRY="$(mktemp)" || { echo "channel-append.sh: cannot create a temp file" >&2; exit 4; }

# The leading newline is the whole requirement, and it is not cosmetic: the parser matches its header
# regex per line with no lookback, so an entry is read iff its header BEGINS A LINE. Starting with a
# newline guarantees that whether or not the channel ended in one. There is no blank-line rule — that
# was believed briefly on 2026-08-13 and disproved by reading the parser.
{
  printf '\n## [%s] FROM %s — %s — %s\n' "$NEXT_INDEX" "$AUTHOR" "$STAMP" "$SUBJECT"

  # THE DECLARED TYPE, PERSISTED (E3 requirement 3) — directly under the header, so the parser finds
  # it without scanning the body. Entries written before this landed carry no such line, and the
  # parser reads that as "untyped" rather than as an error: the dual-parser transition is the whole
  # reason a session on the old skill is never mute.
  if [ -n "$ENTRY_TYPE" ]; then printf '%s%s\n' "$(grammar '.type_field.line_prefix')" "$ENTRY_TYPE"; fi

  printf '\n'
  cat "$BODY_FILE"
  printf '\n'
} > "$STAGED_ENTRY" || { rm -f "$STAGED_ENTRY"; echo "channel-append.sh: could not stage the entry" >&2; exit 4; }

# ONE append of a fully-formed entry. The entry is built in a temp file first so that exactly one
# write() reaches the channel: a writer that emits header and body separately is the shape that let
# another author's header land in the middle of an entry.
if ! cat "$STAGED_ENTRY" >> "$CHANNEL"; then
  rm -f "$STAGED_ENTRY"
  echo "channel-append.sh: the append FAILED — nothing was written" >&2
  exit 4
fi

rm -f "$STAGED_ENTRY"

record_self_write
# ---- end critical section ---------------------------------------------------------------------

release_lock
echo "$NEXT_INDEX"

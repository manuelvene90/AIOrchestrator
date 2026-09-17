#!/usr/bin/env python3
"""Aggregates only. NEVER prints a whole .jsonl or a whole channel."""
import argparse, collections, json, re, statistics, sys
from pathlib import Path

HEADER = re.compile(r"^##\s*\[(\d+)\]\s*FROM\s+(\w[\w-]*)\s*—\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2})\s*—\s*(.*)$")
MEMBERS = {"implementer", "reviewer", "solo", "communicator"}

# A fenced-code delimiter, the same shape `ChannelFence_Screen.Fence_Regex` matches: up to three
# leading spaces, a run of at least three backticks or tildes, an optional info string.
FENCE = re.compile(r"^ {0,3}(?P<fence>`{3,}|~{3,})(?P<info>.*)$")

ARCHIVE_SUFFIX = ".archive.md"
CHANNEL_GLOBS = ("*/**/channel*.md", "*/owner-channel*.md")

# BYTES PER TOKEN — an [estimate], and the one number here that is not read off the disk.
# 3.87 was MEASURED on this repo's own channel prose with real `-p` turns, and it replaces the
# 2.8–3.2 an earlier document guessed at, which overstated every token figure derived from it by
# 20–38 %. It is still a ratio applied to bytes, not a tokeniser: every figure computed with it
# carries ESTIMATE in its key so no reader can mistake it for a count.
BYTES_PER_TOKEN = 3.87

# THE SUBJECTS PLAN 02 ROUTES OUT OF THE CHANNEL — the 17 sites of `AppNoteKinds`, matched on the
# agent tag plus a family word. It is a HEURISTIC OVER A FILE and says so in its key name: the
# routing decision is made in C# at the call site, and nothing in a channel file records which site
# wrote an entry. Good enough for a BEFORE figure; never good enough to decide anything at run time.
#
# Each marker was read off the constant that produces it (branch source, worktree
# `AIOrchestrator-measure`, 2026-09-17) rather than copied from the plan's prose — and one of the
# plan's guesses was WRONG: it listed "has gone deaf" for the orphan report, which writes
# `OrphanEscalation_Decider.Describe_Report`'s "<member> may be deaf to wakes" and would never have
# matched. A marker that matches nothing is a figure that is quietly too small.
#
# SIXTEEN MARKERS FOR SEVENTEEN SITES, hand-counted against the 17 `AppNoteKinds` call sites (8 in
# PrintTurnDispatcherModel, 4 `routedKind:` on the choke point, 5 `Route_ChannelNote`): the two stall
# sites — "failed outside the process" and the attempt-limit one — both write
# `StallAlert_Decider.Build_SubjectStem`, so one marker covers both. Nothing is missing.
ROUTED_MARKERS = (
    "turn_ended",                                # PrintTurn_Words.TURN_ENDED_SUBJECT
    "turn stalled",                              # PrintTurn_Words.TURN_STALLED_SUBJECT
    "turn waiting on a usage limit",             # PrintTurn_Words.TURN_LIMITED_SUBJECT
    "new traffic is waiting on a usage limit",   # PrintTurn_Words.NEW_TRAFFIC_LIMITED_SUBJECT
    "turns killed at the deadline",              # PrintTurn_Words.DEADLINE_KILLS_SUBJECT
    "an earlier final message was superseded",   # PrintTurn_Words.SUPERSEDED_FINAL_SUBJECT
    "reply not addressable",                     # PrintTurn_Words.MISADDRESSED_SUBJECT
    "plan.md is behind your verdicts",           # BridgeEngineModel.cs:4110  LedgerAdvisory
    "plan.md has lines that cannot show progress",   # BridgeEngineModel.cs:4191
    "plan.md claims work that nobody is doing",      # BridgeEngineModel.cs:4279
    "may be deaf to wakes",                      # OrphanEscalation_Decider.Describe_Report
    "breaks the message contract",               # BridgeEngineModel.cs:13825  ContractCoaching
    "your question was not sent to the owner",   # BridgeEngineModel.cs:13856
    "your earlier open question was superseded", # BridgeEngineModel.cs:14027
    "you already asked this",                    # BridgeEngineModel.cs:14081
    "this question is already open",             # BridgeEngineModel.cs:14104
)

AGENT_TAG = "[agent]"   # AppEntryAudience_Tag.AGENT_TAG — a PREFIX test, as the C# one is.


def map_quoted_lines(lines):
    """One flag per line: true when that line lies inside a CLOSED fenced block, delimiters included.

    The same rule as `ChannelFence_Screen.Map_QuotedLines`, and for the same reason: a header-shaped
    line quoted inside a ``` block is EVIDENCE, not a new entry. A fence-blind reader splits one
    message into two phantom entries and signs the tail with whoever was quoted (CLAUDE.md decision
    12) — and for the byte figures below it does something subtler and worse: it attributes the tail
    of a supervisor's brief to `app`, inflating the very bureaucracy share this file exists to
    measure.

    ONLY A CLOSED FENCE SUPPRESSES ANYTHING. An unpaired ``` suppresses nothing, so the worst case
    of this rule is the fence-blind behaviour it replaces rather than a channel that stops parsing.
    """
    quoted = [False] * len(lines)
    open_line, open_char, open_length = -1, "", 0

    for i, line in enumerate(lines):
        match = FENCE.match(line)

        if not match:
            continue

        run, info = match.group("fence"), match.group("info").strip()

        if open_line < 0:
            open_line, open_char, open_length = i, run[0], len(run)
            continue

        if run[0] != open_char or len(run) < open_length or info:
            continue

        for inside in range(open_line, i + 1):
            quoted[inside] = True

        open_line = -1

    return quoted


def read_entries(data):
    """Every entry in one channel file, with the BYTES it occupies.

    ONE PARSER for both readings — the wake counts and the boot read. Two readers of a channel that
    must agree exactly is the defect CLAUDE.md decision 12 records twice; the tailer and the parser
    were made one for it. An entry's span runs from its own header to the next header (or EOF), so
    the spans partition the file and `live_bytes_outside_any_entry` is what is left over — a figure
    reported rather than absorbed, because it is the arithmetic check on all of this.

    Byte counting is done on BYTES, never on the decoded string: this prose is full of em dashes and
    accented words, and a length measured in characters understates a UTF-8 channel by several
    percent. Decoding happens only to match the header.
    """
    raw_lines = data.split(b"\n")
    texts = [line.decode("utf-8", "replace").rstrip("\r") for line in raw_lines]
    quoted = map_quoted_lines(texts)

    starts, offset = [], 0

    for i, raw in enumerate(raw_lines):
        if not quoted[i]:
            match = HEADER.match(texts[i])

            if match:
                starts.append((offset, match))

        offset += len(raw) + 1   # the newline the split consumed

    for position, (start, match) in enumerate(starts):
        end = starts[position + 1][0] if position + 1 < len(starts) else len(data)

        yield {
            "index": int(match.group(1)),
            "author": match.group(2),
            "stamp": match.group(3),
            "subject": match.group(4),
            "bytes": end - start,
        }


def is_routed_bookkeeping(author, subject):
    """Whether this entry is one of the 17 kinds plan 02 takes out of the channel — MODELLED.

    Three conditions, all necessary. `app` authored it; it is AGENT-tagged, because an owner-facing
    app entry is never routed whatever its kind (`AnOwnerFacingNoteNeverLeavesTheChannel…`); and its
    subject carries a family word. The tag test is a PREFIX one, like `AppEntryAudience_Tag`'s, so a
    session quoting the tag into a brief does not become bookkeeping by mentioning it.
    """
    if author != "app":
        return False

    lowered = subject.strip().lower()

    if not lowered.startswith(AGENT_TAG):
        return False

    return any(marker in lowered for marker in ROUTED_MARKERS)


def find_channels(root):
    """Every channel file under the root, live and archived, each one once."""
    found = {}

    for pattern in CHANNEL_GLOBS:
        for path in root.glob(pattern):
            found[path] = None

    return sorted(found)


def collect(root, channels):
    """Two readings of the SAME entries, because that difference is the whole point.

    Under the bash watcher a channel change is a wake, so an app-authored entry — a turn_ended, a
    STATUS line, a ledger advisory — wakes the supervisor exactly like a member's report does.
    Under the app's own policy it wakes nobody (PrintTurn_Trigger.Is_Inbound excludes
    ChannelAuthors.App). The gap between the two columns is what the one-wake-model series buys.

    NEITHER COLUMN IS SEEDED WITH A CONSTANT. An earlier draft hardcoded the ticket column's `app`
    to zero and asserted it was zero, which pinned nothing and made the headline figure
    unmeasurable (CLAUDE.md decision 20).

    AND A THIRD READING, which is plan 02's rather than plan 01's: the BOOT READ. A session starts
    by reading "`session.json` and every channel file in your home, top to bottom"
    (`kit/skills/supervisor/SKILL.md:131`), so its boot read is the LIVE bytes of that
    orchestration's channels — the archive is not in it, which is what the compactor is for. How
    much of that is the app's own bookkeeping is the figure plan 02 never had.
    """
    by_author = collections.Counter()
    watcher = collections.Counter({"owner": 0, "member": 0, "app": 0, "unrecognised": 0})
    ticket = collections.Counter({"owner": 0, "member": 0, "app": 0, "unrecognised": 0})

    live_files = archive_files = 0
    live_bytes = live_entry_bytes = live_app_bytes = 0
    archived_bytes = 0
    routed_entries = routed_bytes = 0
    live_bytes_by_orch = collections.Counter()

    # LIVE **AND** ARCHIVE for the wake counts. Channel_Compactor moves older entries into a sibling
    # `.archive.md`, so a live-file count is not monotonic and reading only the live files
    # undercounts silently — CLAUDE.md decision 13, the defect that once told an owner their message
    # was still waiting long after it had been answered. This script measures the very thing that
    # rule is about, so it reads both.
    #
    # THE BOOT READ IS THE OTHER WAY ROUND, and deliberately: a booting session reads the live file,
    # not the archive, so counting archived bytes into it would inflate every orchestration that has
    # been running long enough to compact. The two readings disagree on purpose, and each says which
    # it is in its own key.
    for channel in channels:
        data = channel.read_bytes()
        archived = channel.name.endswith(ARCHIVE_SUFFIX)
        orch = channel.relative_to(root).parts[0]

        if archived:
            archive_files += 1
            archived_bytes += len(data)
        else:
            live_files += 1
            live_bytes += len(data)
            live_bytes_by_orch[orch] += len(data)

        for h in read_entries(data):
            author = h["author"]
            by_author[author] += 1

            # AN AUTHOR WORD THIS BUILD DOES NOT KNOW IS COUNTED, NOT DROPPED. Headers are
            # agent-written (decision 12) and a hand-written one bypasses channel-append.sh: measured
            # 2026-09-15 on the VPS, 39 entries carry the author `sup` instead of `supervisor`.
            # Dropping them would hide both the traffic and the fact that the vocabulary is leaking.
            kind = ("owner" if author == "owner"
                    else "member" if author in MEMBERS
                    else "app" if author == "app"
                    else "supervisor" if author == "supervisor"
                    else "unrecognised")

            if kind in ("owner", "member", "app", "unrecognised"):
                # The fingerprint monitor fires on ANY change it cannot prove is its own.
                watcher[kind] += 1

            # The app's policy: the owner and members are inbound; the app's own bookkeeping is not.
            # An unrecognised author is inbound to nobody and is reported so it can be chased.
            if kind in ("owner", "member"):
                ticket[kind] += 1

            if archived:
                continue

            live_entry_bytes += h["bytes"]

            if author == "app":
                live_app_bytes += h["bytes"]

            if is_routed_bookkeeping(author, h["subject"]):
                routed_entries += 1
                routed_bytes += h["bytes"]

    orchestrations = len(live_bytes_by_orch)

    return {
        "entries_by_author": dict(by_author),
        "wake_causes_under_watcher_measured": dict(watcher),
        "wake_causes_under_ticket_MODELLED": dict(ticket),
        "wakes_the_ticket_would_avoid_MODELLED": sum(watcher.values()) - sum(ticket.values()),

        # MEASURED: every figure here is a byte count off the disk.
        "boot_read_bytes_measured": {
            "orchestrations": orchestrations,
            "live_channel_files": live_files,
            "archive_files": archive_files,
            "live_bytes": live_bytes,
            "live_bytes_in_entries": live_entry_bytes,
            "live_bytes_outside_any_entry": live_bytes - live_entry_bytes,
            "live_bytes_authored_by_app": live_app_bytes,
            "median_live_bytes_per_orchestration":
                int(statistics.median(live_bytes_by_orch.values())) if orchestrations else 0,
            "archived_bytes_outside_the_boot_read": archived_bytes,
        },

        # MODELLED: which app entries plan 02 routes is decided in C# at the call site. This is a
        # subject-word heuristic over the file, and the suffix is the only thing stopping a reader
        # from taking it for a measurement (the lesson of `wake_causes_under_ticket_MODELLED`).
        "boot_read_bookkeeping_MODELLED": {
            "entries": routed_entries,
            "bytes": routed_bytes,
            "share_of_live_bytes": round(routed_bytes / live_bytes, 4) if live_bytes else 0.0,
            "share_of_app_authored_bytes": round(routed_bytes / live_app_bytes, 4) if live_app_bytes else 0.0,
        },

        # ESTIMATE: bytes ÷ 3.87. Not a tokeniser — see BYTES_PER_TOKEN.
        "boot_read_tokens_ESTIMATE_at_3_87_bytes_per_token": {
            "live": round(live_bytes / BYTES_PER_TOKEN),
            "app_authored": round(live_app_bytes / BYTES_PER_TOKEN),
            "bookkeeping_MODELLED": round(routed_bytes / BYTES_PER_TOKEN),
            "median_per_orchestration":
                round(statistics.median(live_bytes_by_orch.values()) / BYTES_PER_TOKEN) if orchestrations else 0,
        },

        "bookkeeping_out_of_the_boot_read_MODELLED": routed_entries,
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", required=True)
    p.add_argument("--format", choices=["text", "json"], default="text")
    a = p.parse_args()

    root = Path(a.root)

    # REFUSES rather than reporting zero (CLAUDE.md decision 20): a harness that cannot find what it
    # measures must never certify its absence. `0 entries` for a mistyped path — or for a supervision
    # home that has been moved — reads exactly like a quiet machine, and a quiet machine is the
    # conclusion this script exists to rule out.
    if not root.is_dir():
        print(f"baseline: --root {root} does not exist or is not a directory", file=sys.stderr)
        return 2

    channels = find_channels(root)

    if not channels:
        print(f"baseline: no channel files under {root} — expected {' or '.join(CHANNEL_GLOBS)}", file=sys.stderr)
        return 2

    result = collect(root, channels)

    if a.format == "json":
        json.dump(result, sys.stdout)
    else:
        for section, values in result.items():
            # A section is a breakdown or a single number. The first draft printed only breakdowns and
            # crashed on the scalar — and nothing caught it, because the test exercised --format json
            # only. The text path is the one a person actually reads.
            if isinstance(values, dict):
                print(f"== {section} ==")
                for k, v in sorted(values.items(), key=lambda kv: -kv[1]):
                    print(f"  {k:40s} {v}")
            else:
                print(f"== {section} == {values}")
    return 0

if __name__ == "__main__":
    sys.exit(main())

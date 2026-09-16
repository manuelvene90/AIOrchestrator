#!/usr/bin/env python3
"""Aggregates only. NEVER prints a whole .jsonl or a whole channel."""
import argparse, collections, json, re, sys
from pathlib import Path

HEADER = re.compile(r"^##\s*\[(\d+)\]\s*FROM\s+(\w[\w-]*)\s*—\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2})\s*—\s*(.*)$")
MEMBERS = {"implementer", "reviewer", "solo", "communicator"}

def read_headers(path):
    for line in path.read_text(errors="replace").splitlines():
        m = HEADER.match(line)
        if m:
            yield {"index": int(m.group(1)), "author": m.group(2), "stamp": m.group(3), "subject": m.group(4)}

def collect(root):
    """Two readings of the SAME entries, because that difference is the whole point.

    Under the bash watcher a channel change is a wake, so an app-authored entry — a turn_ended, a
    STATUS line, a ledger advisory — wakes the supervisor exactly like a member's report does.
    Under the app's own policy it wakes nobody (PrintTurn_Trigger.Is_Inbound excludes
    ChannelAuthors.App). The gap between the two columns is what the one-wake-model series buys.

    NEITHER COLUMN IS SEEDED WITH A CONSTANT. An earlier draft hardcoded the ticket column's `app`
    to zero and asserted it was zero, which pinned nothing and made the headline figure
    unmeasurable (CLAUDE.md decision 20).
    """
    by_author = collections.Counter()
    watcher = collections.Counter({"owner": 0, "member": 0, "app": 0, "unrecognised": 0})
    ticket = collections.Counter({"owner": 0, "member": 0, "app": 0, "unrecognised": 0})

    # LIVE **AND** ARCHIVE. Channel_Compactor moves older entries into a sibling `.archive.md`, so a
    # live-file count is not monotonic and reading only the live files undercounts silently — CLAUDE.md
    # decision 13, the defect that once told an owner their message was still waiting long after it had
    # been answered. This script measures the very thing that rule is about, so it reads both.
    for channel in sorted(root.glob("*/**/channel*.md")) + sorted(root.glob("*/owner-channel*.md")):
        for h in read_headers(channel):
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

            continue

    return {
        "entries_by_author": dict(by_author),
        "wake_causes_under_watcher_measured": dict(watcher),
        "wake_causes_under_ticket_MODELLED": dict(ticket),
        "wakes_the_ticket_would_avoid_MODELLED": sum(watcher.values()) - sum(ticket.values()),
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", required=True)
    p.add_argument("--format", choices=["text", "json"], default="text")
    a = p.parse_args()
    result = collect(Path(a.root))
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
                    print(f"  {k:16s} {v}")
            else:
                print(f"== {section} == {values}")
    return 0

if __name__ == "__main__":
    sys.exit(main())

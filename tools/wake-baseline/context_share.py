#!/usr/bin/env python3
"""Inherited-context share per role. Aggregates only — NEVER prints a transcript or any message text.

THE ONE NUMBER PLAN 06 TURNS ON. The 2026-09-15 one-wake-model spec says 91 % of a supervisor call's
context is carried from earlier turns; that figure was measured on the VPS in early September and has
never been re-derived. `inherited_share` is cache_read / (cache_read + input + cache_creation): of
everything the model was handed, the fraction it had already been handed. A FRESH session's share is
near zero by construction — it has no prefix to hit — which is exactly what the flip is bought with.

WHAT IS NOT INHERITED: cache_creation_input_tokens. A call that WRITES the cache paid for those tokens
this turn. Folding them into the inherited half would make a session's very first call look 90 %
inherited, which flatters every fresh arm of the comparison this script exists to judge.
"""
import argparse
import collections
import json
import sys
from pathlib import Path


def usage_rows(path):
    """Yields (message_id, usage) for assistant messages.

    A malformed line is SKIPPED, not fatal: a transcript is appended to while it is read, so its last
    line is regularly half a JSON object. A measurement that dies on that is one nobody can run
    against a live machine, which is the only machine worth measuring.
    """
    with path.open(errors="replace") as handle:
        for line in handle:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue

            if row.get("type") != "assistant":
                continue

            message = row.get("message") or {}
            usage = message.get("usage")

            if isinstance(usage, dict) and message.get("id"):
                yield message["id"], usage


def collect(projects, role_of):
    """One pass over every transcript, deduplicated by message id across ALL files.

    Deduplication is global rather than per file on purpose: a resumed session replays its earlier
    assistant messages, and a sidechain repeats them again in another file. Counting `m1` once per
    file it appears in is the same error as counting it twice in one (spec C7).
    """
    seen = set()
    totals = collections.defaultdict(lambda: {"calls": 0, "input": 0, "inherited": 0})

    for path in sorted(projects.rglob("*.jsonl")):
        role = role_of.get(path.name, "unattributed")

        for message_id, usage in usage_rows(path):
            if message_id in seen:
                continue

            seen.add(message_id)

            fresh = (usage.get("input_tokens") or 0) + (usage.get("cache_creation_input_tokens") or 0)
            inherited = usage.get("cache_read_input_tokens") or 0

            bucket = totals[role]
            bucket["calls"] += 1
            bucket["input"] += fresh + inherited
            bucket["inherited"] += inherited

    result = {}

    for role, bucket in totals.items():
        calls = bucket["calls"]
        result[role] = {
            "calls": calls,
            "mean_input_tokens": round(bucket["input"] / calls) if calls else 0,
            "mean_inherited_tokens": round(bucket["inherited"] / calls) if calls else 0,
            "inherited_share": round(bucket["inherited"] / bucket["input"], 4) if bucket["input"] else 0.0,
        }

    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--projects", required=True, help="the Claude Code projects directory to read")
    parser.add_argument("--role-of", action="append", default=[],
                        help="<transcript file name>=<role>, repeatable; anything unnamed is 'unattributed'")
    parser.add_argument("--format", choices=["text", "json"], default="text")
    args = parser.parse_args()

    projects = Path(args.projects)

    # REFUSES rather than reporting zero (CLAUDE.md decision 20): a harness that cannot find what it
    # measures must never certify its absence. `0 calls` for a mistyped path reads exactly like a
    # quiet machine.
    if not projects.is_dir():
        print(f"context_share: --projects {projects} does not exist or is not a directory", file=sys.stderr)
        return 2

    role_of = dict(pair.split("=", 1) for pair in args.role_of)
    result = collect(projects, role_of)

    if args.format == "json":
        json.dump(result, sys.stdout)
        return 0

    for role, figures in sorted(result.items()):
        print(f"== {role} ==")

        for key, value in figures.items():
            print(f"  {key:24s} {value}")

    return 0


if __name__ == "__main__":
    sys.exit(main())

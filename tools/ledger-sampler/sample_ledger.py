#!/usr/bin/env python3
"""ledger-sampler — records WHAT the orchestrations delivered, so cost can be divided by it.

WHY THIS EXISTS.  The installation can measure what it consumes and not what it produces, so
"the day cost 600 M tokens" cannot be judged: the same number is a bargain against twenty
delivered lines and a disaster against one. The count itself is not missing — `PLAN.md` carries
it — but its HISTORY is. Measured 2026-09-10 on the VPS: no orchestration's `PLAN.md` is under
git (none of the six has a `.git`), the file is rewritten in place, and `orchestrator.log.jsonl`
records "the ledger is behind" and never "line X closed at 14:23". So yesterday's count is gone
and nothing can recover it. A sample taken from now on is the cheapest thing that fixes that.

WHY A SAMPLER AND NOT AN EVENT IN THE APP.  An event would be exact to the minute and would cost
a build and a deploy that restarts six live orchestrations. Cost per delivered item does not need
the minute: "between 14:00 and 15:00, two lines closed" divides just as well. This runs beside the
app, touches nothing it owns, and is removed by deleting a file and a crontab line.

IT COUNTS EXACTLY WHAT THE APP COUNTS.  The rules are `PlanLedger_Parser` and
`PlanLedger_Sections`, mirrored here deliberately rather than approximated — a denominator that
disagrees with the progress bar is worse than no denominator. If those classes change, change
this with them; `test_matches_parser.md` beside this file lists what was mirrored and when.

WHERE IT WRITES.  `~/.aiorch-ledger-samples/` by default, and NEVER under the supervision root:
the bridge discovers orchestrations by watching that folder for new directories (decision 3), so
a folder dropped there could be mistaken for an orchestration and given a Telegram topic.
"""

import argparse, glob, hashlib, json, os, re, sys, time

# Mirrors PlanLedger_Parser.TaskLine_Regex (named groups; the indent group is first there too).
TASK_LINE = re.compile(r"^(?P<indent>[ \t]*)-\s*\[(?P<marker>x|X| |>|!|\?|-)\]\s*(?P<text>.*)$")
HEADING = re.compile(r"^\s{0,3}#{1,6}\s+(?P<title>.*?)\s*#*\s*$")

# Mirrors PlanLedger_Sections.NON_LEDGER_HEADING_PREFIXES — matched on the heading TITLE, as a
# case-insensitive PREFIX, so "## PARKED — found, not asked for" is the parked section.
NON_LEDGER_PREFIXES = ("PARKED", "OWNER REQUESTS")

# Mirrors the switch in PlanLedger_Parser.Parse_OrNull. "-" (not doing) is deliberately NOT in the
# total: it is neither owed nor delivered, and it is the marker that makes 100 % reachable.
MARKER_BUCKET = {"x": "done", "X": "done", ">": "in_progress", "!": "blocked",
                 "?": "blocked", " ": "open", "-": "not_doing"}


def parse_plan(text):
    counts = {k: 0 for k in ("done", "in_progress", "blocked", "open", "not_doing")}
    counts["blocked_on_owner"] = 0
    counts["sub_tasks"] = 0
    in_non_ledger = False
    saw_task_line = False
    for raw in text.split("\n"):
        line = raw.rstrip("\r")
        heading = HEADING.match(line)
        if heading:
            title = heading.group("title")
            in_non_ledger = any(title.upper().startswith(p) for p in NON_LEDGER_PREFIXES)
        m = TASK_LINE.match(line)
        if not m or in_non_ledger:
            continue
        saw_task_line = True
        marker = m.group("marker")
        counts[MARKER_BUCKET[marker]] += 1
        if marker == "?":
            counts["blocked_on_owner"] += 1
        if m.group("indent"):
            counts["sub_tasks"] += 1
    if not saw_task_line:
        return None
    counts["total"] = counts["done"] + counts["in_progress"] + counts["blocked"] + counts["open"]
    return counts


def sample(supervision_root, out_dir, orch_glob):
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, "ledger-samples.jsonl")
    stamp = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    written = 0
    lines = []
    for plan_path in sorted(glob.glob(os.path.join(supervision_root, orch_glob, "PLAN.md"))):
        orch = os.path.basename(os.path.dirname(plan_path))
        try:
            with open(plan_path, errors="replace") as handle:
                text = handle.read()
        except OSError as error:                      # a plan being rewritten right now, or gone
            lines.append(json.dumps({"ts": stamp, "orch": orch, "unreadable": type(error).__name__}))
            written += 1
            continue
        counts = parse_plan(text)
        if counts is None:                            # a plan with no task lines is not an error
            continue
        record = {"ts": stamp, "orch": orch,
                  "plan_sha1": hashlib.sha1(text.encode("utf-8", "replace")).hexdigest()[:12]}
        record.update(counts)
        lines.append(json.dumps(record, sort_keys=True))
        written += 1
    if lines:
        with open(out_path, "a") as handle:           # append only; a sample is never rewritten
            handle.write("\n".join(lines) + "\n")
    return out_path, written


def report(out_dir, since, until):
    """Closed lines per orchestration between two samples — the denominator, from the samples."""
    path = os.path.join(out_dir, "ledger-samples.jsonl")
    if not os.path.exists(path):
        print("no samples yet at " + path)
        return
    first, last = {}, {}
    for line in open(path, errors="replace"):
        try:
            o = json.loads(line)
        except ValueError:
            continue
        if "done" not in o:
            continue
        ts = o["ts"]
        if since and ts < since:
            continue
        if until and ts >= until:
            continue
        orch = o["orch"]
        first.setdefault(orch, o)
        last[orch] = o
    if not last:
        print("no samples in this window")
        return
    print("%-14s %8s %8s %9s %8s %9s  %s" %
          ("orch", "closed", "opened", "total now", "open now", "blocked", "window"))
    grand = 0
    for orch in sorted(last):
        a, b = first[orch], last[orch]
        closed = b["done"] - a["done"]
        opened = b["total"] - a["total"]
        grand += closed
        print("%-14s %8d %8d %9d %8d %9d  %s .. %s" %
              (orch, closed, opened, b["total"], b["open"], b["blocked"], a["ts"][:16], b["ts"][:16]))
    print("%-14s %8d" % ("TOTAL closed", grand))
    print("\nNOTE the first sample in the window is the baseline, so a window with one sample "
          "reports zero closed. Sampling began when this tool was installed; nothing before that "
          "is recoverable.")



# --- self-test ------------------------------------------------------------------------------
# The parked/owner-requests exclusion is the half of this parser that real data does not exercise:
# sampled 2026-09-10, all seven live plans had zero task lines under a non-ledger heading, so a
# mirror that silently stopped excluding would have looked correct. A harness that cannot reach
# what it tests must fail loudly rather than pass (decision 20), so these cases are synthetic and
# the runner asserts on every one.
SELF_TEST_PLAN = """# Endeavour

- [x] delivered one
- [X] delivered two
- [ ] still owed
- [>] moving
- [!] stuck
- [?] stuck on the owner
- [-] decided against
  - [x] a sub-task, indented

## PARKED - found, not asked for

- [ ] a discovery nobody asked for
- [x] a discovery someone fixed anyway

## Owner requests (lowercase heading, still excluded)

- [ ] something the backend wrote

### Back to the ledger

- [x] counted again after the next heading
"""

SELF_TEST_EXPECTED = {
    "done": 4,              # two top-level, the indented sub-task, and the one after the next heading
    "open": 1,
    "in_progress": 1,
    "blocked": 2,           # [!] and [?]
    "blocked_on_owner": 1,  # [?] is in blocked AND here
    "not_doing": 1,         # [-] is never in the total
    "sub_tasks": 1,
    "total": 8,             # done + in_progress + blocked + open, excluding not_doing
}


def self_test():
    failures = []
    got = parse_plan(SELF_TEST_PLAN)
    if got is None:
        failures.append("parse_plan returned None on a plan that has task lines")
    else:
        for key, want in SELF_TEST_EXPECTED.items():
            if got.get(key) != want:
                failures.append("%s: expected %r, got %r" % (key, want, got.get(key)))
    if parse_plan("# A plan with prose and no task lines\n\nnothing here.\n") is not None:
        failures.append("a plan with no task lines should return None, not zeroes")
    if parse_plan("") is not None:
        failures.append("empty text should return None")
    for line in failures:
        print("FAIL " + line)
    if failures:
        print("%d failure(s) - this parser no longer mirrors PlanLedger_Parser" % len(failures))
        return 1
    print("self-test OK: %d cases, parked and owner-requests sections excluded, "
          "ledger resumes at the next heading" % (len(SELF_TEST_EXPECTED) + 2))
    return 0


def main(argv):
    p = argparse.ArgumentParser(prog="sample_ledger.py", description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--supervision-root", default=os.path.expanduser("~/.claude/supervision"))
    p.add_argument("--out-dir", default=os.path.expanduser("~/.aiorch-ledger-samples"),
                   help="NEVER put this under the supervision root — see the module docstring.")
    p.add_argument("--orch", default="*")
    p.add_argument("--report", action="store_true", help="print deltas instead of taking a sample")
    p.add_argument("--since", default="")
    p.add_argument("--until", default="")
    p.add_argument("--self-test", action="store_true",
                   help="check this parser still mirrors PlanLedger_Parser, and exit")
    args = p.parse_args(argv)
    if args.self_test:
        sys.exit(self_test())
    if os.path.abspath(args.out_dir).startswith(os.path.abspath(args.supervision_root) + os.sep):
        sys.exit("refusing to write inside the supervision root: the bridge would read a new "
                 "directory there as a new orchestration")
    if args.report:
        report(args.out_dir, args.since, args.until)
    else:
        path, n = sample(args.supervision_root, args.out_dir, args.orch)
        print("sampled %d orchestration(s) -> %s" % (n, path))


if __name__ == "__main__":
    main(sys.argv[1:])

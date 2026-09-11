#!/usr/bin/env python3
"""token-gate — read-only token measurement for an AIOrchestrator installation.

Replaces gate_delivery.py + gate_turns.py, which hardcoded one cut instant, two supervisor
session ids and two hand-eyeballed divisors, and summed the three token classes into one
number (2026-09-10 review, finding F5).

WHAT IT READS  metadata only: `usage`, timestamps, model, ids, tool NAMES and the file-path
argument of a tool call. Never the text of a message.

WHAT IT WRITES  nothing. Run it over stdin so it leaves no file on the box:

    ssh orch@<vps> 'python3 -' < tools/token-gate/token_gate.py -- tokens --since 2026-09-08

THE THREE CLASSES  a raw sum of the four usage fields is not a quantity: on this installation
97 % of it is `cache_read`, context the model was already given and is passed again. They are
reported apart and never added together:

    new     = input_tokens + cache_creation_input_tokens   text processed from scratch
    reread  = cache_read_input_tokens                      identical to the previous request
    output  = output_tokens                                answer + thinking

`ctx` (new + reread) is the context a call carried, which is the metric the fresh-turn work
moves. A cache miss does not change ctx — it moves tokens from reread to new.
"""

import argparse, collections, glob, json, os, statistics, sys

# --- roles -------------------------------------------------------------------------------
# Explicit, and anything unrecognised keeps its own name and is COUNTED. The predecessor's
# `"imp" if m.startswith("imp") else "rev"` silently declared every other member a reviewer.
ROLE_BY_PREFIX = {
    "imp": "implementer",
    "rev": "reviewer",
    "sup": "supervisor",
    "com": "communicator",
    "solo": "solo",
    "media": "media",
}


def role_of(member_id):
    for prefix, role in ROLE_BY_PREFIX.items():
        if member_id == prefix or member_id.startswith(prefix + "-"):
            return role
    return "other:" + member_id


def norm_ts(value):
    """One timestamp form for every comparison. The two old scripts wrote the same instant as
    '2026-09-08T20:34:00' and '2026-09-08T20:34:00Z' and string-compared both against ISO
    transcript stamps, so one of them was always off by the length of the suffix."""
    if not value:
        return ""
    t = str(value).strip().replace(" ", "T")
    if t.endswith("Z"):
        t = t[:-1]
    if "+" in t[10:]:
        t = t[: 10 + t[10:].index("+")]
    return t


def in_window(ts, since, until):
    t = norm_ts(ts)
    if not t:
        return False
    if since and t < since:
        return False
    if until and t >= until:
        return False
    return True


def usage_classes(u):
    """(new, reread, output, cw5m, cw1h) from one `usage` object."""
    inp = u.get("input_tokens") or 0
    cw = u.get("cache_creation_input_tokens") or 0
    cr = u.get("cache_read_input_tokens") or 0
    out = u.get("output_tokens") or 0
    detail = u.get("cache_creation") or {}
    cw5 = detail.get("ephemeral_5m_input_tokens") or 0
    cw1 = detail.get("ephemeral_1h_input_tokens") or 0
    return inp + cw, cr, out, cw5, cw1


# --- the role map, from the app's own turn logs -------------------------------------------
def build_role_map(supervision_root, orch_glob):
    """session id -> (orch, member, role).

    Members write `<orch>/<member>/turns.jsonl`. The SUPERVISOR writes a DOTTED file at the
    orchestration level, `<orch>/.supervisor.turns.jsonl[.N]`, which a `*/*/turns.jsonl` glob
    cannot see — which is why the old script fell back to a hardcoded list of two session ids
    and silently lost every supervisor that was not one of them."""
    smap = {}
    patterns = [
        (os.path.join(supervision_root, orch_glob, "*", "turns.jsonl*"), "member"),
        (os.path.join(supervision_root, orch_glob, ".supervisor.turns.jsonl*"), "supervisor"),
    ]
    for pattern, kind in patterns:
        for path in glob.glob(pattern):
            parts = path.split(os.sep)
            if kind == "member":
                orch, member = parts[-3], parts[-2]
            else:
                orch, member = parts[-2], "sup"
            try:
                handle = open(path, errors="replace")
            except OSError:
                continue
            with handle:
                for line in handle:
                    try:
                        o = json.loads(line)
                    except ValueError:
                        continue
                    sid = o.get("session_id") or o.get("sessionId")
                    if sid:
                        smap[sid] = (orch, member, role_of(member))
    return smap


def iter_transcript_calls(projects_root, smap, since, until):
    """One record per deduped model call. Dedupe is (sessionId, requestId|message.id) —
    globally, not per file, so a call appearing in two files is counted once."""
    seen = set()
    for path in glob.glob(os.path.join(projects_root, "**", "*.jsonl"), recursive=True):
        is_sub = os.sep + "subagents" + os.sep in path
        project = path.split(os.sep + "projects" + os.sep)[-1].split(os.sep)[0]
        try:
            handle = open(path, errors="replace")
        except OSError:
            continue
        with handle:
            for line in handle:
                if '"usage"' not in line:
                    continue
                try:
                    o = json.loads(line)
                except ValueError:
                    continue
                msg = o.get("message")
                if not isinstance(msg, dict):
                    continue
                u = msg.get("usage")
                if not isinstance(u, dict):
                    continue
                ts = o.get("timestamp") or ""
                if not in_window(ts, since, until):
                    continue
                sid = o.get("sessionId")
                key = (sid, o.get("requestId") or msg.get("id"))
                if key in seen:
                    continue
                seen.add(key)
                if is_sub:
                    orch, member, role = "-", "-", "subagent"
                else:
                    orch, member, role = smap.get(sid, ("-", "-", "unattributed"))
                new, reread, out, cw5, cw1 = usage_classes(u)
                yield dict(ts=norm_ts(ts), sid=sid, project=project, orch=orch, member=member,
                           role=role, model=msg.get("model") or "?", new=new, reread=reread,
                           out=out, cw5=cw5, cw1=cw1)


def key_of(rec, group_by):
    return {"day": rec["ts"][:10], "hour": rec["ts"][:13], "role": rec["role"],
            "member": rec["orch"] + "/" + rec["member"], "orch": rec["orch"],
            "model": rec["model"], "project": rec["project"]}[group_by]


# --- commands ------------------------------------------------------------------------------
def cmd_tokens(args, smap):
    # NOTE there is deliberately no "first call of a turn" column here. Windowed transcript
    # data cannot tell a session's cold start from the first call that happens to fall inside
    # the window, and a first draft of this tool reported the latter as the former (median 0,
    # because a mid-session call is a full cache hit). `turns` reads usage.iterations[0], which
    # IS the turn's first call, and is where that number belongs.
    agg = collections.defaultdict(lambda: collections.Counter())
    for rec in iter_transcript_calls(args.projects_root, smap, args.since, args.until):
        a = agg[key_of(rec, args.group_by)]
        a["calls"] += 1
        for f in ("new", "reread", "out", "cw5", "cw1"):
            a[f] += rec[f]
    if args.json:
        print(json.dumps({k: dict(v) for k, v in agg.items()}, indent=1, sort_keys=True))
        return
    print("%-24s %7s %10s %10s %9s %11s %11s" %
          (args.group_by, "calls", "new(M)", "reread(M)", "out(M)", "ctx/call(k)", "new/call(k)"))
    total = collections.Counter()
    for k in sorted(agg):
        a = agg[k]
        c = max(a["calls"], 1)
        print("%-24s %7d %10.2f %10.1f %9.2f %11.1f %11.2f" %
              (k, a["calls"], a["new"] / 1e6, a["reread"] / 1e6, a["out"] / 1e6,
               (a["new"] + a["reread"]) / c / 1000, a["new"] / c / 1000))
        total.update(a)
    c = max(total["calls"], 1)
    print("%-24s %7d %10.2f %10.1f %9.2f %11.1f %11.2f" %
          ("TOTAL", total["calls"], total["new"] / 1e6, total["reread"] / 1e6, total["out"] / 1e6,
           (total["new"] + total["reread"]) / c / 1000, total["new"] / c / 1000))
    if total["cw5"] or total["cw1"]:
        print("cache writes: %.2fM at the 5-minute TTL, %.2fM at the 1-hour TTL" %
              (total["cw5"] / 1e6, total["cw1"] / 1e6))
    unattributed = agg.get("unattributed")
    if args.group_by == "role" and unattributed:
        print("\nNOTE %d calls could not be mapped to a role. They are reported, never folded into "
              "another bucket." % unattributed["calls"])


def cmd_turns(args, smap):
    """Per-TURN statistics from the app's own turn logs. `usage.iterations[0]` is the turn's
    first model call, i.e. the context a fresh turn boots with."""
    rows = []
    patterns = [(os.path.join(args.supervision_root, args.orch, "*", "turns.jsonl*"), False),
                (os.path.join(args.supervision_root, args.orch, ".supervisor.turns.jsonl*"), True)]
    for pattern, is_sup in patterns:
        for path in glob.glob(pattern):
            parts = path.split(os.sep)
            orch, member = (parts[-2], "sup") if is_sup else (parts[-3], parts[-2])
            try:
                handle = open(path, errors="replace")
            except OSError:
                continue
            with handle:
                for line in handle:
                    try:
                        o = json.loads(line)
                    except ValueError:
                        continue
                    if o.get("aiorch_kind") != "turn":
                        continue
                    at = o.get("aiorch_at") or ""
                    if not in_window(at, args.since, args.until):
                        continue
                    u = o.get("usage") or {}
                    new, reread, out, _, _ = usage_classes(u)
                    its = u.get("iterations") or []
                    fc_ctx = fc_new = None
                    if its:
                        i0 = its[0]
                        f_new, f_reread, _, _, _ = usage_classes(i0)
                        fc_ctx, fc_new = f_new + f_reread, f_new
                    rows.append(dict(orch=orch, member=member, role=role_of(member),
                                     at=norm_ts(at), calls=o.get("num_turns"), new=new,
                                     ctx=new + reread, out=out, fc_ctx=fc_ctx, fc_new=fc_new,
                                     dur=o.get("duration_ms") or 0, err=bool(o.get("is_error"))))
    if not rows:
        print("no turns in this window")
        return
    print("%-14s %5s %9s %11s %11s %10s %9s %7s %5s" %
          ("role", "turns", "calls/turn", "1st-ctx(k)", "1st-new(k)", "ctx/turn(k)", "out/turn", "min", "err"))

    def med(values):
        values = [v for v in values if v is not None]
        return statistics.median(values) if values else 0

    for role in sorted({r["role"] for r in rows}):
        rs = [r for r in rows if r["role"] == role]
        print("%-14s %5d %9.1f %11.0f %11.1f %10.0f %9.0f %7.1f %5d" %
              (role, len(rs), med([r["calls"] for r in rs]), med([r["fc_ctx"] for r in rs]) / 1000,
               med([r["fc_new"] for r in rs]) / 1000, med([r["ctx"] for r in rs]) / 1000,
               med([r["out"] for r in rs]), med([r["dur"] for r in rs]) / 60000,
               sum(1 for r in rs if r["err"])))


def cmd_mix(args, smap):
    """Tool mix: production (Edit/Write/NotebookEdit + git commit) vs orientation (Read/Grep/
    Glob/other Bash). Rates per delivered item are printed ONLY when --items is given: the old
    script carried the divisors 23 and 7 inline, with a comment saying it was unsure of the first."""
    counts = collections.defaultdict(collections.Counter)
    commits = collections.Counter()
    seen = set()
    for path in glob.glob(os.path.join(args.projects_root, "**", "*.jsonl"), recursive=True):
        is_sub = os.sep + "subagents" + os.sep in path
        try:
            handle = open(path, errors="replace")
        except OSError:
            continue
        with handle:
            for line in handle:
                if '"tool_use"' not in line:
                    continue
                try:
                    o = json.loads(line)
                except ValueError:
                    continue
                if o.get("type") != "assistant":
                    continue
                if not in_window(o.get("timestamp"), args.since, args.until):
                    continue
                sid = o.get("sessionId")
                msg = o.get("message")
                if not isinstance(msg, dict) or not isinstance(msg.get("content"), list):
                    continue
                key = (sid, msg.get("id"))
                if key in seen:
                    continue
                seen.add(key)
                role = "subagent" if is_sub else smap.get(sid, ("-", "-", "unattributed"))[2]
                for block in msg["content"]:
                    if not isinstance(block, dict) or block.get("type") != "tool_use":
                        continue
                    name = block.get("name") or "?"
                    counts[role][name] += 1
                    if name == "Bash":
                        # the command string is a tool ARGUMENT, not message text
                        cmd = str((block.get("input") or {}).get("command") or "")
                        if "git commit" in cmd:
                            commits[role] += 1
    print("%-14s %7s %12s %9s %13s %7s" % ("role", "calls", "production", "commits", "orientation", "Agent"))
    for role in sorted(counts):
        c = counts[role]
        prod = c["Edit"] + c["Write"] + c["NotebookEdit"]
        ori = c["Read"] + c["Grep"] + c["Glob"] + c["Bash"] - commits[role]
        print("%-14s %7d %12d %9d %13d %7d" % (role, sum(c.values()), prod, commits[role], ori, c["Agent"]))
        if args.items:
            print("%-14s %7s per delivered item (--items %d): production %.1f, orientation %.1f" %
                  ("", "", args.items, (prod + commits[role]) / args.items, ori / args.items))


def main(argv):
    p = argparse.ArgumentParser(prog="token_gate.py", description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("command", choices=["tokens", "turns", "mix"])
    p.add_argument("--since", default="", help="ISO instant or prefix, inclusive (e.g. 2026-09-08 or 2026-09-08T20:34)")
    p.add_argument("--until", default="", help="ISO instant or prefix, exclusive")
    p.add_argument("--group-by", default="day",
                   choices=["day", "hour", "role", "member", "orch", "model", "project"])
    p.add_argument("--orch", default="*", help="orchestration id or glob (default: all)")
    p.add_argument("--supervision-root", default=os.path.expanduser("~/.claude/supervision"))
    p.add_argument("--projects-root", default=os.path.expanduser("~/.claude/projects"))
    p.add_argument("--items", type=int, default=0,
                   help="delivered-item count for the per-item rates of `mix`. No default: a "
                        "divisor is a measurement and belongs to whoever counted it.")
    p.add_argument("--json", action="store_true")
    args = p.parse_args(argv)
    args.since, args.until = norm_ts(args.since), norm_ts(args.until)
    smap = build_role_map(args.supervision_root, args.orch)
    print("# role map: %d sessions across %d orchestrations | window: %s .. %s" %
          (len(smap), len({v[0] for v in smap.values()}), args.since or "start", args.until or "now"),
          file=sys.stderr)
    {"tokens": cmd_tokens, "turns": cmd_turns, "mix": cmd_mix}[args.command](args, smap)


if __name__ == "__main__":
    main(sys.argv[1:])

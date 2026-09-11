import json, os, glob, collections
# METADATA ONLY: timestamp, usage, model, ids. Never message text.
root = os.path.expanduser("~/.claude/projects")
seen = set()
calls = collections.Counter()
raw   = collections.Counter()   # naive sum of 4 usage fields
cread = collections.Counter()
cwrit = collections.Counter()
fresh = collections.Counter()
outp  = collections.Counter()
bymodel = collections.Counter()
for path in glob.glob(os.path.join(root, "**", "*.jsonl"), recursive=True):
    try:
        fh = open(path, "r", errors="replace")
    except Exception:
        continue
    with fh:
        for line in fh:
            if '"usage"' not in line:
                continue
            try:
                o = json.loads(line)
            except Exception:
                continue
            msg = o.get("message")
            if not isinstance(msg, dict):
                continue
            u = msg.get("usage")
            if not isinstance(u, dict):
                continue
            ts = o.get("timestamp") or ""
            if len(ts) < 13:
                continue
            key = (o.get("sessionId"), o.get("requestId") or msg.get("id"))
            if key in seen:
                continue
            seen.add(key)
            k = (ts[:10], ts[11:13])
            i  = u.get("input_tokens", 0) or 0
            cw = u.get("cache_creation_input_tokens", 0) or 0
            cr = u.get("cache_read_input_tokens", 0) or 0
            ot = u.get("output_tokens", 0) or 0
            calls[k] += 1
            fresh[k] += i; cwrit[k] += cw; cread[k] += cr; outp[k] += ot
            raw[k]   += i + cw + cr + ot
            bymodel[(ts[:10], msg.get("model") or "?")] += i + cw + cr + ot
days = sorted({d for d, _ in calls})
print("=== HOURLY (UTC) ===")
print("day        hh   calls   raw(M)  fresh(M)  cwrite(M)  cread(M)  out(M)")
for d in days:
    tc = tr = tf = tcw = tcr = to = 0
    for h in sorted({hh for dd, hh in calls if dd == d}):
        k = (d, h)
        tc += calls[k]; tr += raw[k]; tf += fresh[k]; tcw += cwrit[k]; tcr += cread[k]; to += outp[k]
        print(f"{d} {h} {calls[k]:6d} {raw[k]/1e6:8.1f} {fresh[k]/1e6:9.2f} {cwrit[k]/1e6:10.2f} {cread[k]/1e6:9.1f} {outp[k]/1e6:7.2f}")
    print(f"{d} ALL {tc:6d} {tr/1e6:8.1f} {tf/1e6:9.2f} {tcw/1e6:10.2f} {tcr/1e6:9.1f} {to/1e6:7.2f}")
    print()
print("=== TOKENS BY MODEL PER DAY (raw M) ===")
for (d, m), v in sorted(bymodel.items()):
    print(f"{d}  {m:40s} {v/1e6:9.1f}")

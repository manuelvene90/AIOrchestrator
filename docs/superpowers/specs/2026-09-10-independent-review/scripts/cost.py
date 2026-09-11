import json, os, glob, collections
# METADATA ONLY: usage, model, timestamps, ids, member dir names. No message text.
HOME = os.path.expanduser("~")
# price per 1M tokens: (input, output, cache_write_5m=1.25x, cache_read=0.1x)
P = {
 "claude-opus-5":   (5.0, 25.0),
 "claude-sonnet-5": (2.0, 10.0),
 "claude-haiku-4-5-20251001": (1.0, 5.0),
}
def price(model):
    return P.get(model, (5.0, 25.0))   # unknown -> opus rate, stated as assumption

# ---- session -> role map, built from supervision turns.jsonl ----
sid2role = {}
for f in glob.glob(HOME + "/.claude/supervision/*/*/turns.jsonl"):
    member = f.split("/")[-2]
    orch   = f.split("/")[-3]
    try:
        for line in open(f, errors="replace"):
            try: o = json.loads(line)
            except Exception: continue
            s = o.get("session_id") or o.get("sessionId")
            if s: sid2role[s] = (orch, member)
    except Exception: pass

def classify(path, sid):
    if "/subagents/" in path: return "subagent"
    hit = sid2role.get(sid)
    if not hit: return "unmapped"
    m = hit[1]
    if m in ("sup", "supervisor"): return "supervisor"
    if m.startswith("imp"): return "member:imp"
    return "member:" + m

seen = set()
agg = collections.defaultdict(lambda: [0,0,0,0,0,0.0])  # calls,in,cw,cr,out,usd
for path in glob.glob(HOME + "/.claude/projects/**/*.jsonl", recursive=True):
    try: fh = open(path, errors="replace")
    except Exception: continue
    with fh:
        for line in fh:
            if '"usage"' not in line: continue
            try: o = json.loads(line)
            except Exception: continue
            msg = o.get("message")
            if not isinstance(msg, dict): continue
            u = msg.get("usage")
            if not isinstance(u, dict): continue
            ts = o.get("timestamp") or ""
            if len(ts) < 13: continue
            sid = o.get("sessionId")
            key = (sid, o.get("requestId") or msg.get("id"))
            if key in seen: continue
            seen.add(key)
            model = msg.get("model") or "?"
            pin, pout = price(model)
            i  = u.get("input_tokens",0) or 0
            cw = u.get("cache_creation_input_tokens",0) or 0
            cr = u.get("cache_read_input_tokens",0) or 0
            ot = u.get("output_tokens",0) or 0
            usd = (i*pin + cw*pin*1.25 + cr*pin*0.10 + ot*pout) / 1e6
            k = (ts[:10], classify(path, sid))
            a = agg[k]
            a[0]+=1; a[1]+=i; a[2]+=cw; a[3]+=cr; a[4]+=ot; a[5]+=usd
print("sessions mapped to a role:", len(sid2role))
print()
print("day        role           calls   in(M)  cwrite(M)  cread(M)  out(M)    USD    USD/call")
days = sorted({d for d,_ in agg})
for d in days:
    tot = [0,0,0,0,0,0.0]
    for r in sorted({rr for dd,rr in agg if dd==d}):
        a = agg[(d,r)]
        for j in range(6): tot[j]+=a[j]
        print(f"{d} {r:14s} {a[0]:6d} {a[1]/1e6:7.2f} {a[2]/1e6:10.2f} {a[3]/1e6:9.1f} {a[4]/1e6:7.2f} {a[5]:8.2f} {a[5]/max(a[0],1):9.4f}")
    print(f"{d} {'TOTAL':14s} {tot[0]:6d} {tot[1]/1e6:7.2f} {tot[2]/1e6:10.2f} {tot[3]/1e6:9.1f} {tot[4]/1e6:7.2f} {tot[5]:8.2f} {tot[5]/max(tot[0],1):9.4f}")
    print()

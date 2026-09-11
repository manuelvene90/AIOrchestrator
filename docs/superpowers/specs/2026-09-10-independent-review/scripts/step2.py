import json,os,glob,collections
HOME=os.path.expanduser("~")
sid2role={}
for f in glob.glob(HOME+"/.claude/supervision/*/*/turns.jsonl"):
    m=f.split("/")[-2]
    try:
        for line in open(f,errors="replace"):
            try:o=json.loads(line)
            except Exception:continue
            s=o.get("session_id") or o.get("sessionId")
            if s: sid2role[s]=m
    except Exception:pass
seen=set()
agg=collections.defaultdict(lambda:[0,0,collections.Counter()])  # calls, ctx_tokens, models
for path in glob.glob(HOME+"/.claude/projects/**/*.jsonl",recursive=True):
    if "/subagents/" in path: continue
    try: fh=open(path,errors="replace")
    except Exception: continue
    with fh:
        for line in fh:
            if '"usage"' not in line: continue
            try:o=json.loads(line)
            except Exception:continue
            msg=o.get("message")
            if not isinstance(msg,dict):continue
            u=msg.get("usage")
            if not isinstance(u,dict):continue
            ts=o.get("timestamp") or ""
            if len(ts)<13 or ts[:10]<"2026-09-07": continue
            sid=o.get("sessionId"); k=(sid,o.get("requestId") or msg.get("id"))
            if k in seen: continue
            seen.add(k)
            m=sid2role.get(sid)
            if not m or not m.startswith("imp"): continue
            ctx=(u.get("input_tokens",0) or 0)+(u.get("cache_creation_input_tokens",0) or 0)+(u.get("cache_read_input_tokens",0) or 0)
            key=ts[:13]
            a=agg[key]; a[0]+=1; a[1]+=ctx; a[2][(msg.get("model") or "?").replace("claude-","")]+=1
print("IMPLEMENTER members only — mean context tokens per model call, by UTC hour")
print("hour           calls   ctx/call(k)  models")
for k in sorted(agg):
    a=agg[k]
    if a[0]<10: continue
    mods=",".join(f"{m}:{c}" for m,c in a[2].most_common(3))
    print(f"{k}Z {a[0]:6d} {a[1]/a[0]/1000:11.1f}  {mods}")

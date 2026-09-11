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
seen=set(); agg=collections.defaultdict(lambda:[0,0,set()])
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
            if not ts.startswith("2026-09-10"): continue
            sid=o.get("sessionId"); k=(sid,o.get("requestId") or msg.get("id"))
            if k in seen: continue
            seen.add(k)
            if sid in sid2role: continue
            ctx=(u.get("input_tokens",0) or 0)+(u.get("cache_creation_input_tokens",0) or 0)+(u.get("cache_read_input_tokens",0) or 0)
            # bucket by which project folder the transcript lives in
            proj=path.split("/projects/")[-1].split("/")[0]
            a=agg[proj]; a[0]+=1; a[1]+=ctx; a[2].add(sid)
print("2026-09-10 UNMAPPED main-session calls, by project folder")
print("calls   ctx/call(k)  sessions  project")
for p,(c,t,s) in sorted(agg.items(), key=lambda x:-x[1][1]):
    print(f"{c:5d} {t/c/1000:11.1f} {len(s):9d}  {p}")

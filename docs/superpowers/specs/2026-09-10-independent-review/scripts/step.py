import json, os, glob, collections
HOME=os.path.expanduser("~")
P={"claude-opus-5":(5.0,25.0),"claude-sonnet-5":(2.0,10.0),"claude-haiku-4-5-20251001":(1.0,5.0)}
sid2role={}
for f in glob.glob(HOME+"/.claude/supervision/*/*/turns.jsonl"):
    m=f.split("/")[-2]
    try:
        for line in open(f,errors="replace"):
            try:o=json.loads(line)
            except Exception:continue
            s=o.get("session_id") or o.get("sessionId")
            if s: sid2role[s]=m
    except Exception: pass
def cls(path,sid):
    if "/subagents/" in path: return "subagent"
    m=sid2role.get(sid)
    if not m: return "unmapped"
    return "member:imp" if m.startswith("imp") else "member:rev"
# windows: label -> (startISO, endISO)
W=[("A 09-07 all      ","2026-09-07T00","2026-09-08T00"),
   ("B 09-08 all      ","2026-09-08T00","2026-09-09T00"),
   ("C 09-09 pre-cut  ","2026-09-09T00","2026-09-09T17"),
   ("D 09-09 post-cut ","2026-09-09T17","2026-09-10T00"),
   ("E 09-10 to 14h   ","2026-09-10T00","2026-09-10T14"),
   ("F 09-08 same-hrs ","2026-09-08T00","2026-09-08T14"),
   ("G 09-07 same-hrs ","2026-09-07T00","2026-09-07T14")]
seen=set(); agg=collections.defaultdict(lambda:[0,0.0,0])
for path in glob.glob(HOME+"/.claude/projects/**/*.jsonl",recursive=True):
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
            if len(ts)<13: continue
            sid=o.get("sessionId"); k=(sid,o.get("requestId") or msg.get("id"))
            if k in seen: continue
            seen.add(k)
            pin,pout=P.get(msg.get("model") or "",(5.0,25.0))
            i=u.get("input_tokens",0) or 0; cw=u.get("cache_creation_input_tokens",0) or 0
            cr=u.get("cache_read_input_tokens",0) or 0; ot=u.get("output_tokens",0) or 0
            usd=(i*pin+cw*pin*1.25+cr*pin*0.10+ot*pout)/1e6
            tok=i+cw+cr+ot
            r=cls(path,sid)
            for lab,s,e in W:
                if s<=ts[:13]<e:
                    a=agg[(lab,r)]; a[0]+=1; a[1]+=usd; a[2]+=tok
                    a2=agg[(lab,"ALL")]; a2[0]+=1; a2[1]+=usd; a2[2]+=tok
print("window              role          calls      USD   USD/call  tok/call(k)")
for lab,_,_ in W:
    for r in ["member:imp","member:rev","subagent","unmapped","ALL"]:
        a=agg.get((lab,r))
        if not a: continue
        print(f"{lab} {r:12s} {a[0]:6d} {a[1]:8.2f} {a[1]/max(a[0],1):9.4f} {a[2]/max(a[0],1)/1000:10.1f}")
    print()

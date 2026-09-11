import json,glob,os,collections,re
HOME=os.path.expanduser("~")
# supervisor turn starts, from the app's own log line:
#   "stream turn <orch>/sup/<n> started — attempt <a>, entries: <src> [i], <src> [j], ..."
START=re.compile(r"turn\s+(\S+)/sup/(\d+)\s+started\s+—\s+attempt\s+(\d+),\s*entries:\s*(.*)$")
SRC=re.compile(r"([A-Za-z][\w-]*)\s*\[")
rows=[]
for f in glob.glob(HOME+"/.claude/supervision/*/orchestrator.log.jsonl"):
    for line in open(f,errors="replace"):
        try: o=json.loads(line)
        except Exception: continue
        m=str(o.get("message") or ""); ts=str(o.get("ts") or "")
        g=START.search(m)
        if not g or len(ts)<13: continue
        srcs=SRC.findall(g.group(4))
        n_entries=g.group(4).count("[")
        kind = "owner" if any(s.lower()=="owner" for s in srcs) else "member"
        rows.append((ts, g.group(1), int(g.group(3)), kind, n_entries))
def bucket(ts):
    d=ts[:10]
    if d<"2026-09-09": return "A pre  ("+d+")"
    if d=="2026-09-09": return "B 09-09 pre-cut" if ts[11:13]<"17" else "C 09-09 post-cut"
    return "D 09-10 (to 14Z)"
agg=collections.defaultdict(lambda:[0,0,0,0,0])  # turns, owner, member, entries, multi-entry
hours=collections.defaultdict(set)
for ts,orch,att,kind,ne in rows:
    if att!=1: continue          # first attempt only: retries are not wake-ups
    b=bucket(ts); a=agg[b]
    a[0]+=1; a[1]+= kind=="owner"; a[2]+= kind=="member"; a[3]+=ne; a[4]+= ne>1
    hours[b].add(ts[:13])
print("SUPERVISOR wake-ups (first attempt only), from orchestrator.log.jsonl")
print("window                turns  owner  member  entries  multi  active_h  turns/active_h  entries/turn")
for b in sorted(agg):
    t,o,mm,e,mu=agg[b]; h=len(hours[b])
    print(f"{b:20s} {t:6d} {o:6d} {mm:7d} {e:8d} {mu:6d} {h:9d} {t/max(h,1):15.1f} {e/max(t,1):13.2f}")
print()
print("total supervisor turn-starts parsed:", len(rows), "| first-attempt:", sum(a[0] for a in agg.values()))

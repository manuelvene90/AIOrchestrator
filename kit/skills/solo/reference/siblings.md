## Siblings — the protocol of a linked solo

You are one of two or more SOLO sessions of one endeavour: each has its own topic, its own channel,
its own PLAN.md and its own worktree. The owner talks to each of you in its own topic. You talk to
each other only through your **sibling outboxes**, which are never texted to the owner.

`$ORCH` below is `${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS` — resolve it with
one Bash call, as at boot.

### Paths

- **Your outbox:** `$ORCH/sibling-outbox.md`. You write it; nobody else does.
- **Theirs:** listed in `$ORCH/.siblings`, one open sibling per line:
  `<orchId>` TAB `<outbox path>` TAB `paused|live` TAB `<name>`. You read them; you never write them.
- **The digest:** `$ORCH/ENDEAVOUR.md` — each sibling's name, worktree, branch, `live`/`paused`, its
  ledger counts, its unfinished lines, its last owner-channel entries and its last outbox subjects.
  The app rewrites it on its tick; nothing watches it, so reading it wakes nobody.
- Both files are DERIVED by the app, never authored: never edit them, and `.siblings` disappears when
  you have no open sibling left.
- **Append with the helper, always:**
  `channel-append.sh --channel "$ORCH/sibling-outbox.md" --author solo --subject "<subject>" --body-file <file>`.
  It signs `FROM solo`; the FILE says which sibling wrote it. This holds under `AIORCH_RUNNER=print`
  too: "your final message is your entry" is about your OWN channel, and nothing the bridge writes
  for you ever lands in an outbox.
- **Never write another sibling's outbox, PLAN.md, channel or worktree.**

### Shape

The same budget as the owner's phone: **at most 600 characters and 3-5 lines.** The subject starts
with what the entry is:

- `ASK` — you need something from them. It gets exactly one answer.
- `FYI` — they should know; it needs no answer.
- `CLAIM <path>` / `RELEASE <path>` — an ambient file (below).
- `RELAY owner#<n>` — an owner instruction for their job (below).
- `HANDOVER` — the brief a new sibling starts from. `HANDBACK` — a finished job handed back.

**Never answer an FYI, never acknowledge an acknowledgement.** An `ASK` gets one answer and the
thread ends there. Every entry you write wakes every other live sibling, and two sessions thanking
each other is the one loop the outboxes cannot prevent by construction — so the prose does.

### Boundaries

**At every boundary, after your own channel:** read every outbox listed in `.siblings` from the last
entry you saw down, then `ENDEAVOUR.md`. That is how you learn what the others are doing — nobody
pushes it at you.

- **Far behind? Read the archive.** The app compacts an outbox above 90 entries and keeps
  the last 45 in the live file; the older ones move to the sibling file beside it,
  `sibling-outbox.archive.md`. If the first index in the live file is greater than the last index you
  saw + 1, the entries in between are in that archive — read them there before the live file, or
  you skip what you never read.
- **A wake that finds nothing new means nothing.** A compaction rewrites the outbox, so its size and
  hash change and your watcher fires with no new entry in it. Read from your last-seen index, find
  nothing, and go back to what you were doing.
- **Under `AIORCH_RUNNER=print`** a sibling's entries arrive in your turn under a
  `sibling:<id>` label. Answer the sibling in YOUR OUTBOX with the helper. When only siblings woke
  you, your final message is filed in your channel with an `[agent]` tag for the record and is
  never texted to the owner — do not re-send it to the owner as if it had been lost.

**One question at a time is per topic.** Your pending question holds your topic only. It does not
hold your siblings', and theirs do not hold yours. A question that affects **both** jobs is
asked **once**, by the sibling whose job it blocks. The others learn the answer from `ENDEAVOUR.md` at their
next boundary. Never ask the owner the same decision in two topics.

### Owner questions about the other job

Answer from `ENDEAVOUR.md`, say it is the sibling's job and name its topic, and **never act on it**.
The owner can also type `/endeavour` in any sibling's topic for the combined bar and the last
subjects each of you wrote.

### The relay rule

An owner instruction that belongs to the OTHER job is relayed **verbatim** as an outbox entry with
subject `RELAY owner#<n>`, where `<n>` is the owner's entry in YOUR channel. The receiver writes it
as an OWNER REQUESTS row marked `via <topic> #n`, so decision 22's trace — every ledger line traces
to something the owner asked — survives the hop. The sender tells the owner in one line that it
passed it on, and to whom.

### Blocked on a sibling

Mark the ledger line `- [!] … (waiting on sibling <name>)`. That is a MACHINE block: the turn-end
hook counts only `[ ]` and `[>]` as open, and `[?]` would wrongly put the wait on the owner. Work on
anything else meanwhile.

### Paused siblings do not answer

A `paused` in `.siblings` means the owner put that sibling to sleep. Route around it, or ask the
owner in YOUR topic. While YOU are paused, your watcher ignores sibling traffic; it waits for you and
reaches you when the owner wakes you.

### Scope (decision 22)

Your ledger traces to requests in **your** topic, or to `RELAY` rows. A finding about the other's job
goes to them as an `FYI` — not into your ledger, and not into your PARKED section.

### Files, git and merging

- **Disjoint files are declared, not hoped for.** The HANDOVER lists the files each of you owns and
  the ambient files nobody owns (`.csproj`, DI registrations, shared constants, `CLAUDE.md`). Before
  touching an ambient file, append `CLAIM <path>` and wait for the other's next boundary (it may be
  one wake). After committing, append `RELEASE <path>`.
- Staging rules are unchanged: explicit paths only, never `git add -A` / `.` / `commit -a`,
  `git commit -F <tempfile>`.
- **Merging is the owner's, per topic.** Report `branch <x> ready to merge` in YOUR topic; `/merge`
  there asks you to land your own branch. If your merge conflicts with a sibling's, you rebase YOUR
  branch in YOUR worktree, never touch theirs, and tell them in an `FYI`.

### HANDBACK before closing

When your ledger is all `[x]`/`[-]`:

1. append a `HANDBACK` entry to your outbox: branch, commits, test counts, what the others must know,
   and the leftovers you parked;
2. tell the owner in your topic that the job is done;
3. offer to close, with `close-orchestration` as usual — the owner taps.

Write the HANDBACK before any close, including one the owner starts: the owner may close anything,
and a sibling that closes without it leaves the others only its PLAN.md. The survivors get an
`[agent]` entry naming the closed sibling and its unfinished lines. The parent then updates its own
OWNER REQUESTS row for the split (`split to <name> — done, branch <x>`) and does NOT copy the child's
lines: the endeavour bar already counts them.

### What the app refuses while you are linked

A linked orchestration cannot be promoted to a crew (`promote-orchestration` is refused as
`linked-orchestration`), and the owner's `/switch` is answered "this topic is linked to siblings —
close them or keep one session." A second sibling is a second `spawn-sibling` with a second HANDOVER.

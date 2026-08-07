# Why supervised sessions go dormant — answering the two-mode debug prompt

Investigating `DEBUG-PROMPT-orphaned-watchers.md` (rewritten 2026-08-08 to separate Mode A from
Mode B). Evidence: the app's source, `~/.claude/supervision/da-vinci-fintech-suite-2/` (channels,
`.watcher-log`, `orchestrator.log.jsonl`) and the five session transcripts of 2026-08-07.

All transcript times below are **UTC**; the `.watcher-log` and the channels are **local (UTC+2)**.

---

## Answers to the numbered questions

### Q1 — What event reaps the watchers? **CONFIRMED: nothing in the app can.**

Every process-termination path in the codebase:

| Path | file:line | What it kills |
|---|---|---|
| Session termination | `Termination/SessionTerminator.cs:39` | `Process.GetProcessById(pid).Kill(entireProcessTree: true)` where `pid` comes from the session's **own pid file** |
| App shutdown | `SessionTerminator.cs:107` → same call | every pid file under the supervision root |
| Close orchestration | `SessionTerminator.cs:117` → same call | every pid file under one orchestration |
| Close implementer | `BridgeEngineModel.cs:1461` → same call | one member's pid file |
| Orphan recovery | `BridgeEngineModel.cs:555` → same call | one member's pid file |
| Git snapshot timeout | `Git/GitSnapshot_Reader.cs:181` | the `git` child process it spawned |
| Translator / transcriber timeouts | `MessageTranslatorModel.cs:129`, `VoiceTranscriberModel.cs:77` | the helper process each spawned |

There is no kill-by-name, no job object, no process-group kill, and `Process.GetProcessById` is the
only lookup (`SessionTerminator.cs:31`, `SessionWatchdogModel.cs:185`). Every session-directed kill
takes the session's shell **and all its descendants together** — the watcher cannot die while its
session lives. Since the reaped sessions demonstrably survived (they resumed with context intact),
**the app did not kill those watchers.** As the prompt says, that pushes the cause outside the app.

Also confirmed: **no session killed them either.** Searching all five transcripts for
`taskkill|pkill|Stop-Process|kill -9|killall|TaskStop` over the whole day returns **0 commands**.

### Q2 — Does owner-message delivery reap tasks? **CONFIRMED NO — refuted with timestamps.**

Correlating the eight clustered kill instants against `orchestrator.log.jsonl` (±20 s):

| Kill instant (UTC) | App events within ±20 s |
|---|---|
| 08:50:29 | 3 (owner message delivered) |
| 09:06:57 | **0** |
| 11:06:41 | **0** |
| 11:21:03 | **0** |
| 11:28:05 | **0** |
| 11:55:50 | 1 (owner message buffered) |
| 12:02:35 | 4 (owner message delivered) |
| 12:08:31 | **0** |

**Five of eight kill instants have no app activity at all.** The owner-message correlation that
motivated this question comes from the two instants that happened to coincide; it does not hold.

### Q3 — Is the reap process-group-wide? **CONFIRMED, and WIDER than the prompt assumed.**

The prompt asks whether all of *one session's* tasks die together. They do — but that understates
it. Kills land in the **same second across independent sessions**, i.e. across separate `claude`
processes in separate terminal windows:

```
08:50:29   com + imp-2 + sup        (three sessions, one second)
09:06:57   com + imp-2 + sup
11:06:41   com + imp-3
11:21:03   com + imp-3
11:28:05   com + imp-1
11:55:50   com + imp-1
12:02:35   com + sup ×2             (the "double kill" of the prompt — and com as well)
12:08:31   sup, imp-1, com          (within 2 s)
```

No mechanism inside one session can terminate another session's child process. Combined with Q1 and
the zero kill-commands result, the reaper is **machine-wide and external to both the app and the
sessions**.

### The mechanism — **CONFIRMED by a controlled comparison: it is Bash `run_in_background`, not Monitor**

The prompt's leading hypothesis was formed from imp-2's own account, which the prompt itself warns
against trusting. It is nonetheless correct, and here it is established from the outside instead:

- **29 kills on 2026-08-07. Every one of them is a Bash `run_in_background` task.**
- **Zero Monitor tasks were killed — ever.**
- imp-2 armed a single persistent Monitor (`b8mcn3rst`) and it delivered at **11:39:04, 11:58:11,
  12:05:04 and 12:20:54** — one task, alive **41+ minutes**, straddling the reap instants of
  **11:43:18, 11:55:50, 12:02:35 and 12:08:31** that killed Bash watchers in *three other sessions
  at the same second*.

That is a control, not a testimonial: same machine, same orchestration, same instants, different
mechanism, opposite outcome. It also disposes of the age hypothesis from the other direction — the
Monitor is by far the longest-lived watcher of the day and was never touched.

**What the external reaper actually is remains UNPROVEN.** It is outside the app's code and outside
anything the sessions ran, so it cannot be established from this repository. It does not need to be:
the fix is to stop using the mechanism it eats.

### Q4 — Is "alive + idle + no watcher" detectable? **Partly, and the prompt is right that Mode A makes it harder.**

The existing detector (`BridgeEngineModel.cs:445`) guards its false positive correctly: a session
whose transcript is still growing is working, not orphaned (`Is_SessionMidTurn`,
`BridgeEngineModel.cs:433` → `SessionActivity_Probe`), so a long build or test suite is never
flagged.

But it only fired when someone *else's* entry was unanswered — `entries[^1].Author == Implementer`
returned early, which is **exactly Mode A**. So Mode A was provably uncovered. See the change below.

### Q5 — Should the wake-up path be the app's job? **Partly — and this is now split cleanly.**

- **Mode B is solved by the mechanism change** (persistent Monitor), not by moving the wake-up path.
- **Mode A cannot be solved by any watcher**, because it is not a watcher failure: the session is
  waiting for a message nobody is going to send. Only an outside party can end that wait, and the
  app is the only outside party that is always awake. That part *is* the app's job, and it is what
  the change below adds.

---

## Changes made

**1. All five role commands now arm ONE persistent `Monitor` at boot instead of a `run_in_background`
Bash watcher per turn end** (`kit/commands/{supervisor,implementer,communicator,reviewer,general-supervisor}.md`).

Two long-standing failure modes disappear as a side effect, which is why this is a smaller change
than it looks:

- **The re-arm obligation is gone.** It was item #2 of `INVENTORY-unenforced-steps.md` — a step
  running on memory alone, where one miss stalls the orchestration silently.
- **The baseline race is gone.** The old watcher needed its baseline captured at turn *start*;
  captured at arm time, everything arriving mid-turn became invisible forever (the imp-3 blind
  watcher, `DEBUG-PROMPT-imp3-status.md`). A persistent monitor holds `prev` continuously — there is
  no window for a change to fall into.

Each role command carries the measurement, not just the instruction, so nobody "improves" it back.

**2. The idle detector now covers Mode A** (`BridgeEngineModel.cs:462`). A member that spoke last and
then went quiet past the threshold, while not mid-turn, is nudged — the nudge appends to its channel,
which is precisely the missing input.

The line is drawn at **who owes whom a reply**, not at how the member phrased its last entry. Two
states are legitimate dormancy and are excluded: `AwaitingSupervisorReview` (a filed report waiting
on the supervisor) and `BlockedOnOwner`. Everything else a member said last — an open writing window,
a "proceeding with X" — means it stopped with nobody about to speak to it.

An earlier draft of this fix keyed on "writing window still open" alone. Checking it against the real
channels killed it: imp-2's actual dormancy at 14:34 followed a `WRITING WINDOW CLOSED` entry
("Threading still not done"), so the narrow version would have missed the very case that prompted the
investigation.

**3. The SUPERVISOR is now covered too** (`Nudge_IdleSupervisor`, `BridgeEngineModel.cs`). Nothing
watched it before, and it is the session whose dormancy costs most — every member's report waits
behind it, and it was reaped five times on 2026-08-07. When a member's channel ends with that
member's own entry, has waited past the threshold, and the supervisor is not mid-turn, an app entry
naming the waiting members lands on `owner-channel.md` (its own channel — not the member's, where it
would read as traffic addressed to that member).

The nudge text distinguishes all three cases, because they call for different actions: "resume the
work you announced" / "read the traffic you have not answered" / "these reports are waiting on you".

---

---

## Mode C — a healthy watcher that never matches (added after the owner reported it)

A third way a session goes silent with nothing dead: the watcher is alive and the traffic arrives,
but the watcher's text pattern stops matching because the writer changed their wording. Reported by
the owner: the supervisor "wrote channel headers three different ways", and an implementer fixed its
own side by matching on **who wrote it** rather than on words in the subject — immune to the
supervisor's sloppiness instead of dependent on its consistency.

Both halves are now fixed, and the reading side is already immune:

**Reader side (done, and stronger than pattern-matching):** the persistent Monitor fingerprints the
WHOLE FILE (`wc -c` + `md5sum`). Any byte that changes is traffic, so no wording can hide from it.
All the role commands now say explicitly not to narrow it to a text pattern, with this incident as
the reason — otherwise the next session "optimises" it into a grep and reintroduces the bug.

**Writer side — the damage is worse than a missed wake, and it was invisible.** A malformed header
is not merely unmatched by a watcher: `ChannelEntry_Parser` folds the line into the previous entry's
body, so the entry **does not exist for the app**. It is never mirrored to Telegram (the owner never
sees that message at all), never counted as traffic by the idle detector, never resolves state, and
`Get_NextIndex` keeps handing out an index that is already taken. Nothing errors.

Five such lines are sitting in the live channels right now, all supervisor-written, in three shapes:

```
imp-1/channel.md:2961  ## [SUPERVISOR — 2026-08-08 04:14] SWITCH YOUR WATCHER: …
imp-2/channel.md:343   ## [2b] FROM supervisor — 2026-08-07 12:56 — Excellent pass. …
imp-2/channel.md:2145  ## [supervisor] FROM supervisor — 2026-08-08 04:48 — CHECK YOUR WATCHER ANCHOR …
imp-3/channel.md:1591  ## [SUPERVISOR — 2026-08-08 04:14] SWITCH YOUR WATCHER: …
imp-3/channel.md:1664  ## [supervisor] FROM supervisor — 2026-08-08 04:48 — CHECK YOUR WATCHER ANCHOR …
```

The last one is the supervisor's own entry *warning the implementers about bad header formats* —
itself malformed, and therefore invisible to everyone it was warning.

`ChannelShape_Validator` now finds these and the engine posts a correction into the offending
channel (once per offence), naming the exact line and the only accepted format, and telling the
writer to **re-append as a NEW entry** rather than edit — the channel is append-only. When the
malformed entry was on the owner channel, the owner also gets a Telegram alert, because the loss is
theirs: content they were supposed to receive never arrived.

The false positive that would make this useless is covered by test: ordinary markdown headings in a
report body (`## BRANCH SUMMARY — …`, `## What I changed`) are NOT entry headers and are never
flagged. A line counts as an attempted header only if it opens a bracket like one does, or names an
author with `FROM`. Verified against the real channels: exactly the five above, and `## BRANCH
SUMMARY` correctly ignored.

Supervisor-side prose was tightened too (`kit/commands/supervisor.md`) — but prose is the weakest
lever here, which is why the detector exists.

---

## Live experiment still running

All sessions were moved to persistent Monitors on 2026-08-07 at ~12:22–12:23 UTC — **except the
communicator**, which was still arming Bash watchers at 12:25:14 and is the single most-reaped
session of the day (20 kills). Its role command is fixed here, but **the running communicator will
keep using Bash until it is respawned.** If any Monitor is ever reported killed, the mechanism
conclusion above is wrong and the reap is broader than measured.

## Separately found while auditing (real, unrelated to the watchers)

`WindowFocus/TerminalWindow_Focuser.cs:127` matches window titles with `Contains`, and returns the
first `EnumWindows` hit. Orchestration ids collide as substrings — `da-vinci-fintech-suite-1` and
`da-vinci-fintech-suite-2` both exist today, and the fragment `SUP · da-vinci-fintech-suite-1` is
not a substring of the other, but `SUP · da-vinci-fintech-suite` (the prefix used when an id is
shortened, and any id that is a prefix of another) is. The consequence is worst in
`Try_Close_ByTitleFragment` (`:88`), which is called after a kill and could post `WM_CLOSE` to
another orchestration's terminal; `Try_Focus_` and `Try_PlaceWindow_` would merely target the wrong
window. Not the cause of anything in this report — logged so it is not lost.

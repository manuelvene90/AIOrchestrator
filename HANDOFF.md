# Handoff — 2026-08-13

Written by the supervisor of `ai-orchestrator-3` at the end of a long session, for whoever picks
this up next. **Everything below was verified against the code at `801eb4d`, not remembered.**

## Where things stand

- **master is `801eb4d`. 668 tests green**, run on the merge commit itself.
- **All worktrees removed.** Their three branches were fully merged and their trees clean; verified
  before removal.
- **A rebuild is pending.** Nothing merged on 2026-08-12 reaches a running session until the app is
  closed, rebuilt from the MAIN checkout, and relaunched. See "The rebuild" below.
- The task ledger with full evidence for everything here lives at
  `~/.claude/supervision/ai-orchestrator-3/PLAN.md`. The channels in that folder are the log — read
  them as history, not as a to-do list.

## Do this first: R1 is a live critical defect on master

**A failed Telegram send can silently drop the answer to the owner's own question.** Traced through
`AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`:

```
:1730   _ownerAwaitingAnswer.Remove(orchId)      the flag is cleared HERE
        ...the Telegram send happens AFTER this
:1795   send fails -> return false -> the append is left unconfirmed -> the tailer RE-EMITS it
:1706   on the re-emission, ownerIsWaiting reads FALSE
        -> OwnerPush_Policy.Should_Push re-evaluates under the NARRATION policy
        -> the answer is suppressed, and nothing anywhere says so
```

The comment at `:1795` records that the owner already reported this class of failure once — a
supervisor's message that never arrived.

**Fix:** clear the flag only after a *confirmed* send. It is small, it is in the engine, and it wants
a test that fails for its own reason plus a review. The engine is reachable from tests — see
`NudgeOncePerThingProbeTests` for the harness shape that works (interfaces via
`BridgeEngine_Factory.Create`, `RecordingSpawner_Fake`, a temp root so no Telegram client and no
network).

## Then: the two unmerged branches

### `fix/quiet-clock-ignores-app` — MERGED as `2110c56`. Kept because its REASONING was wrong twice more.

Landed as `fix/quiet-clock-rebase` (`63465df`, `50df689`, `214265d`, `b4d5d5d`) and merged at
`2110c56`. The code is right; what was recorded ABOUT it was not, so this section is now a correction
notice rather than a merge plan.

- **It was correctly held back at the time**: on its own it made the nudge loop *faster*, 8 minutes
  to 6, because the app's own write to the channel was the only thing DELAYING the re-arm.
- **That hazard is gone, but NOT because of where the marker gate sits.** The ordering claim that used
  to be here was wrong: the gate is textually DOWNSTREAM of the clock check. What closes the loop is
  that `_nudgedAboutEntry` is written when a nudge is sent and **never removed** — no `.Remove` for it
  exists anywhere — so the app permanently remembers which conversation entry it nudged about. Order
  does not enter into it, which is why the original phrasing mattered: "upstream" is the kind of
  sentence someone reorders code on. *Cited by symbol deliberately — the line numbers that used to sit
  here were master's rather than the branch's, and the clock line among them was the very code the
  merge replaced.*
- **AND THAT MEMORY IS ONLY AS GOOD AS THE IDENTITY IT KEYS ON.** Until `fix/marker-gate-spans-archive`
  the identity was read from the LIVE FILE ALONE, so a compacted channel produced null — no memory, no
  gate, and the loop back every 8 minutes on exactly the channels running longest. CLAUDE.md item 13,
  thirty lines from the function that already handled it. Fixed and pinned there.
- **The second route to a null identity is closed too — `5f3dc1f`, same branch.** A channel holding
  only app entries and no conversation anywhere, and it needs no compaction at all: the app writes to
  a member channel before its first brief (a `/resume` broadcast will do it), the member is then
  eligible through `Has_UnansweredInboundTraffic`, has no identity, and is nudged forever. Keyed on a
  sentinel now — **at most once, never none**, because a `/resume` is an app entry a respawned member
  is genuinely supposed to act on and may be the only thing telling it to start. **Still open on
  master until that branch lands.**
  Note it is the same `/resume` path as the bound finding below, one layer down: **the unstick command
  creating the condition that defeats the gate**, where below it is the unstick command postponing the
  alarm. One story, two levels — which is the argument for reading `/resume` as a first-class writer
  rather than an occasional owner action.
- **THE RESIDUAL'S BOUND WAS WRONG, AND IT IS THE FIFTH TIME — one bullet below the one that counted
  four.** What was recorded here, and what `214265d`'s commit message still says on disk, is *"bounded
  at one delay, not starvation — four app write paths into a member channel, none on a timer"*. Both
  halves are false.
  - **Compaction is a fifth path**: `Compact_LongChannels` → `Channel_Compactor.Compact_IfNeeded` →
    `Atomic_FileWriter.Write_AllText`, a rename-over, and the target carries the temp file's timestamp
    — measured on this filesystem, not reasoned from semantics. It re-arms every 46 entries once a
    channel passes `COMPACT_ABOVE_ENTRIES`.
  - **But the count was never the load-bearing error — the TEST was.** *"Does any writer repeat on a
    timer"* is the wrong question; *"can a writer recur during a single stall"* is the right one. By
    that test `/resume` — already counted, then waved through on a property that did not bear on it —
    has no dedupe of any kind: `Resume_AllSessions_Async` appends to every open member of every open
    orchestration, once per command, with no memory field. **So the command the owner sends *because*
    things are stuck was postponing the alarm for the stuck thing.**
  - **Correct bound: 8 minutes × the number of app writes landing after the unanswered entry,
    uncapped.** Not starvation — nothing recurs without an external cause — but not one delay either.
    Which also means the merged fix was worth MORE than the "modest refinement" it was scaled down to,
    not less.
- **Two clocks read a member's channel and only one was converted.** `Measure_QuietFor`'s mtime
  FALLBACK inherits the entire defect while its own docstring promises it fails noisy, and the
  supervisor-nudge path still reads a member's channel by raw `File.GetLastWriteTimeUtc`. Both are
  being fixed on a branch off `2110c56`; until it lands, "the quiet clock is fixed" is half true.
- **Nothing here should be believed without re-reading the code it cites — including this bullet.**
  Reasoning about this file's subject has now outrun it five times, twice inside paragraphs written to
  correct the previous time.

`f2485cf` is worth keeping either way: it catches the engine passing `UtcNow` where `Now` is meant,
which on a machine at UTC+2 makes `quietFor` negative and **silences every nudge in the system with a
green suite**. Before it existed, that mutation reddened nothing.

### `wip/bridge-critical-fixes` — do NOT merge. Mine it for the defect list.

`9523d42`, tip 2026-08-11 21:16, **72 commits behind master**. It rewrites 335 lines of an engine
that has been rewritten repeatedly since, it was never reviewed, and its own commit message says one
test is RED and unresolved (`Owes_Delivery_StaysTrue_WhenAPartialAppendStopsTheReEmission` — whether
the test or the fix is wrong was never settled).

**Its value is the list of defects it names, re-fixed against current master:**

| id | what it says | status |
|---|---|---|
| R1 | awaiting-answer flag cleared before the send | **VERIFIED STILL LIVE** — see above |
| R2 | unfiltered `catch (OperationCanceledException)` escaping the append, skipping the settle and switching compaction off system-wide | not checked |
| R3 | the compaction guard reads the wrong buffer after `Rewind_Unconfirmed` | not checked |
| R4 | silent empty/missing cursor; messages promise duplicates when the real failure is a one-way hole | not checked |
| R5 | `Read_From` short read with the offset advancing anyway | not checked |
| R7/R8 | compact only what was polled; muted time must not count as retry time | not checked |

**Check each against master before fixing anything** — some may have been fixed incidentally in the
72 commits since, and a fix for a defect that no longer exists is worse than none.

## The rebuild

```
cd C:\Users\Gianpiero\source\repos\AIOrchestrator
dotnet build AIOrchestrator.slnx
```

- **The app must be CLOSED.** It holds `AIOrchestratorCoreLib.dll`, so the build fails MSB3021 while
  it runs — and MSB3027 fails *after* a green compile, so a half-failed build reads as fine and
  silently leaves the old assets installed.
- **Build from the MAIN checkout, never a worktree.** A worktree build lands in that worktree's
  output folder, which the running app never reads.
- **`KitAssets_Installer` overwrites `~/.claude/commands` from the build output at every startup**, so
  the build IS the delivery — no manual copying.
- **After relaunch, verify by reading the INSTALLED BYTES** (size and mtime under `~/.claude/commands`)
  rather than assuming the build landed.

## Habits this session paid for the hard way

- **Say which copy you read** — branch source, build output, or installed. They drift, and a finding
  about one is not a finding about another.
- **A mutation that does not model the defect it names is a green that means nothing.** Run a control:
  delete the guard and confirm the test goes red for *its own* rule.
- **Pin the CALL, not just the callee.** Extracting a decision into a testable seam does not pin the
  call site that uses it; three separate defects this session lived in the wiring.
- **Under an unknown rate, choose the rule that fails toward the visible side.** A spurious wake is
  one visible alarm; a missed one loses filed work silently.
- **Never assert on a state with two routes to it** — and after fixing such an assertion, check the
  fix for the same shape. It recurred three times in one evening, in one file.

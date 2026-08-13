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

### `fix/quiet-clock-ignores-app` — rebase it, do not merge it as-is

Two commits: `11a1c1a` (the fix) and `f2485cf` (a probe that pins which clock the engine passes).

- **It conflicts** in `Nudge_Decider.cs`, the file the "one nudge per unanswered thing" work rewrote.
- **It was correctly held back at the time**: on its own it made the nudge loop *faster*, 8 minutes
  to 6, because the app's own write to the channel was the only thing DELAYING the re-arm.
- **That hazard is gone, but NOT because of where the marker gate sits.** The ordering claim that used
  to be here was wrong: the gate is textually DOWNSTREAM of the clock check
  (`BridgeEngineModel.cs:964` vs `:997-1002`). What closes the loop is that `_nudgedAboutEntry` is
  written at `:1008` and never removed, so the app permanently remembers which conversation entry it
  nudged about. Order does not enter into it.
- **AND THAT MEMORY IS ONLY AS GOOD AS THE IDENTITY IT KEYS ON.** Until `fix/marker-gate-spans-archive`
  the identity was read from the LIVE FILE ALONE, so a compacted channel produced null — no memory, no
  gate, and the loop back every 8 minutes on exactly the channels running longest. CLAUDE.md item 13,
  thirty lines from the function that already handled it. Fixed and pinned there; **one route to a null
  identity remains open and is written up in that file's docstring** (a channel holding only app
  entries and no conversation anywhere).
- **A real residual the quiet clock still plugs**: an app write resets the **first** nudge's clock, so
  a legitimate nudge can be delayed by up to 8 minutes. Bounded at one delay, not starvation — four
  app write paths into a member channel, none on a timer.
- **This has now been wrong FOUR times, the fourth in the paragraph correcting the third.** Reasoning
  about this code keeps outrunning it. Nothing here should be believed without re-reading the lines it
  cites — including this bullet.

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

# stage/16 — the owner's phone line never queues behind the work it dispatched

**Date:** 2026-09-10 · **Branch:** `stage/16-the-supervisor-never-queues` (main checkout, from `ours/integration` `a67a83b`) · **Owner decision:** "exempt from the limits, always" (2026-09-10, chosen over a configurable reserved slot).

**OWNER REQUEST (2026-09-10):** *"I write to the supervisor and it is very slow to answer. Something is wrong."*

## What was measured (VPS journal, build `481efc9`)

Five implementers briefed at 21:17:18; `printRunner.maxConcurrentTurnsPerOrchestration` at its default of 3, so imp-4/5/6 ran and imp-7/8 queued. The owner's message reached the channel at 21:18:57 (`entry #559`). The supervisor's turn for it — `stream turn fincanva-5/sup/142` — started at **21:47:53**, the same second imp-5's closing turn ended after its 30-minute deadline kill: 29 minutes in the FIFO behind imp-8 (started 21:34:41 when imp-6 ended) and imp-7 (21:37:49 when imp-4 ended). Then it answered in 46 s.

Meanwhile the phone read `still at it · your message has been waiting 18 min` and the channel got `the owner is waiting on you — answer them at your next boundary` (21:21:28, 21:42:52) — both built from `Is_TurnInFlight`, which is true for a queued turn and a running one alike. The suite already knew: `StopAsync_DrainsAnInFlightTurn_ThenStops` carries the comment *"An entry in the in-flight table alone could still be waiting for its slot"*.

## What changed (branch source)

| file | change |
|---|---|
| `Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` | `Is_ExemptFromSlots(role)`: Supervisor, Solo and General take **no** global or per-orchestration slot — each is one session with one turn at a time, so the exemption adds at most one concurrent turn per orchestration. A `_running` set beside `_inFlight`, written the moment the slots are held (or immediately for an exempt role); `Is_TurnQueued` = in flight and not running. One INFO line when a turn is actually going to wait (`Turn for 'x' is queued — waiting for a free turn slot (N per orchestration)`) — the log never had a trace of the queue before. |
| `Running/PrintTurnDispatcher/IPrintTurnDispatcher.cs` | `Is_TurnQueued(orchId, memberId)`. |
| `Status/MemberWorking_Decider.cs` | `WorkingVerdicts.Queued` — positive knowledge, decided before Working when the dispatcher says the admitted turn has no slot. `Is_Busy(verdict)` (Working or Queued) for the do-not-disturb guards; `QUEUED_WORDS` for every surface; `Describe_OrNull(Queued)`. |
| `Bridge/BridgeEngine/BridgeEngineModel.cs` | `Resolve_MemberWorking` feeds `Is_TurnQueued`; `Is_Working` folds Queued into "occupied" (a queued session cannot read a nudge either); the two implementer guards (nudge, orphan) use `Is_Busy`. The busy narration says **queued — waiting for a free turn slot** instead of "mid-task"/"still at it" when that is the truth, and the agent-facing "the owner is waiting on you" entry is **not** written while queued — the queued turn already carries the owner's message. |

## Probes

- `PrintTurnDispatcherTests.TheSupervisor_NeverWaitsForASlot_BehindAnImplementerTurn` — one slot per orchestration, an implementer holding it for 8 s, the supervisor registered afterwards: its stream process starts inside 5 s. Counted by the fake's `stream-start` line kind (a stream session logs two lines, so a total count was wrong the first time).
- `PrintTurnDispatcherTests.AQueuedTurn_IsReportedAsQueued_AndTheRunningOneIsNot` — two implementers, one slot: both in flight, only the second queued; nothing queued after the drain.
- `MemberWorkingDeciderTests`: `ATurnWaitingForASlot_IsQueued_NotWorking`, `QueuedWithoutATurnInFlight_IsNotQueued`, `WorkingAndQueued_AreBusy_TheOthersAreNot`, `Queued_IsNotDescribedAsWorking`; Queued added to the safe-to-disturb and has-words theories.

**Mutation-checked:** with `Is_ExemptFromSlots` returning `false`, `TheSupervisor_NeverWaitsForASlot_…` fails (the 5 s budget expires); restored, it passes. **A lesson from doing it:** restoring the mutated file with `mv file.bak file` keeps the backup's OLD mtime, so the incremental build did not recompile and the next run tested the mutated binary while reading the restored source — two runs were misread that way. `touch` the file after a restore, and say which binary ran.

## Not in this branch

- The engine's narration wording is pinned only through the decider's constant (`BridgeEngineModel` is `internal sealed` with no `InternalsVisibleTo`; the sentence is built from `MemberWorking_Decider.QUEUED_WORDS`, which the suite does check).
- PULSE member glyphs come from channel entries (`TopicStatusMember`), not from the dispatcher; a queued implementer still renders from its declared state there. Not the owner's request; noted.

## PARKED (found on the way — decision 22)

- `Is_TurnInFlight`'s doc comment on the interface still describes it as "THE ONE TRUE ANSWER TO 'IS IT WORKING'"; it is the answer to "is a turn admitted". Reworded only on the new member; the old comment is left for the reader who lands on it from git blame.

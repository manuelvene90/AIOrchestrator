# One Wake Model — Plan 01 report

**Date:** 2026-09-15 · **Branch:** `feat/one-wake-model` (worktree `../AIOrchestrator-relations`), based on `ours/integration` at `27ceefb` · **Plan:** `2026-09-15-one-wake-model-01-ticket-and-decider.md` · **Spec:** `../specs/2026-09-15-one-wake-model-design.md`

## Copies read

Source: this worktree. Tests: run here, on this machine, by the dispatching session — never taken from a
sub-agent's report. Live data: the VPS at `orch@159.195.254.120`, **read-only**, `~/.claude/supervision`
only; `/home/orch/AIOrchestrator` was deliberately never entered. No running app on this machine.
`dotnet` is at `~/.dotnet/dotnet` (SDK 10.0.401), not on PATH.

## What shipped

| task | commit | what |
|---|---|---|
| 1 | `ebf0ba4` | baseline script (`tools/wake-baseline/`) |
| 2 | `37fd626` | `runners.<role>.wake` = `watcher \| ticket`, default `watcher` |
| 3 | `1b5e2f4` | `IPrintSessionState.DrivesTurns`; absent reads as `true` |
| 3b | `ecdfbea` | all ten `CreateFrom_Existing_*` preserve it |
| — | `afecfd9` | baseline reads archives too, and counts an unrecognised author |
| 4 | `b291540` | the dispatcher screens on `DrivesTurns` |
| 5 | `40d9e0e` | a terminal spawn keeps its state; two production callers move with it |
| 6 | `d85aa37` | `WakeDecision_Resolver` — one home for the wake question |
| 7 | `535a15e` | `WakeTicket_Store` |
| 9 | `5d8464e` | the ten-line monitor, additive beside the fingerprint one |
| 8 | `59ee3d7` | the engine writes tickets with the bridge's own policy |
| 11 | `ab83a1e` | the ticket carries the state pack |
| 10 | `234be55` | a ticket nobody acts on is a stall |

**Suite at the end: 3 955 passed, 10 skipped, 0 failed** (`dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`, ~3 min 20 s).
**Kit harness: 44 checks, 1 failure** — pre-existing, verified by stashing the kit changes and re-running (25 checks, the same failure): supervisor's `FP_ERR` is per-file (`"md5sum on $file"`) while the harness expects `"md5sum failed"`. Watcher-mode logic, untouched by this series.

## The measurement — before

Measured on the VPS, **9 days (2026-09-06 22:06 → 2026-09-15 11:35), 20 orchestrations, 119 channels**, live files **and** archives:

| cause of wake | entries |
|---|---|
| the app's own bookkeeping | 4 955 |
| members | 1 122 |
| owner | 714 |
| unrecognised author (`sup`) | 77 |
| **under the fingerprint watcher** | **6 868** |
| **under the app's policy** | **1 836** |
| **avoided by the ticket** | **5 032 (73 %)** |

Reproduce with `python3 tools/wake-baseline/baseline.py --root <supervision root>` — aggregates only, never a whole channel or `.jsonl`.

**Corroboration, not inheritance:** the 2026-09-08 spec counted ~1 966 app entries over its own shorter window; this is an independent count over a longer one. The first run of this script read only live files and undercounted by 2.6× — `Channel_Compactor` had already archived 61 % of them (CLAUDE.md decision 13, walked into by the tool whose job is to measure it).

## The measurement — after: NOT TAKEN, and why

It cannot be taken from this machine. It requires `wake: ticket` set for one role on a machine that actually runs orchestrations, left for about two days, and the same script re-run.

**The owner's step, one line in `config.json`:**

```json
{ "runners": { "supervisor": { "runner": "terminal", "wake": "ticket" } } }
```

Then, after two days: `python3 tools/wake-baseline/baseline.py --root ~/.claude/supervision` and compare `wake_causes_under_watcher` against the tickets actually written (`orchestrator.log.jsonl`, lines reading `wake ticket <n> —`).

**Reversible in one edit**, and that is load-bearing: `Write` emits the key (it did not, until Task 2 — an explicit `"wake": "ticket"` was silently reverted by the app's own next save), so the setting survives a round-trip.

## The marketplace — the plan's step is deliberately NOT executed

The plan's Task 12 said to re-register `aiorch-local` against this checkout. **It is not done, and should not be.**

When this series began, `known_marketplaces.json` registered `aiorch-local` at
`/Users/nvene/Visual Studio/AIOrchestrator-integration/kit` — **a directory that does not exist**, so no
corrected skill could reach any session. By the end of the series it registers
`/Users/nvene/Visual Studio/AIOrchestrator/kit`, the main checkout, which does exist. **Repaired by
somebody else between the two readings, not by this session.** The finding was true when written and is
now stale; it is recorded here rather than deleted so a reader of the earlier commits can tell.

Pointing it at this worktree would install an **unmerged branch's** skills onto the owner's machine.
The registration is correct as it stands; what is missing is the merge, which is the owner's call.

Consequently the installed cache carries **none** of this series (`grep -c "TICKET MODE"` → 0), and
Tasks 9 and 11 are verified against the branch source and the kit harness, **not** against a restarted
session. Under CLAUDE.md decision 17 that is the weaker of the two verifications, and it is stated
rather than glossed.

## Known gaps, stated

1. **The watchdog half of Task 5's fix has no end-to-end test.** `SessionWatchdogModel.Is_PrintRun` now asks whether a session drives turns rather than whether its file exists; the `BridgeEngineModel` half is proved by `EffortDialOnABridgeDrivenSupervisorTests`, which failed on Task 5 alone and passes now. Three constructions of the watchdog equivalent were defeated by the harness's 90 s spawn grace and were removed rather than committed red or deleted to force green.
2. **`WakeDecision_Resolver.Resolve_OrNull` has no production caller.** The sweep enters at `Read_Pending` + `Decide_OrNull` because `Resolve_OrNull` persists nothing, and ask-and-discard re-baselines a terminal session's cursor — making it permanently deaf. Kept for callers that read without needing to record.
3. **`.usage.json`'s path is rebuilt by hand in ~19 places**; Task 10 added the nineteenth. A `UsageFile_Locator` is the right home. Parked (decision 22).
4. **`_wakeTicketWatchByStateFile` entries are never removed** when an orchestration closes. Bounded by sessions seen per process.
5. **`PrintSessionState_Store.Delete_IfExists` is dead code** since Task 5, and its docstring now describes behaviour that no longer exists.
6. **77 entries on the VPS carry the author `sup`** instead of `supervisor` — hand-written headers bypassing `channel-append.sh` (decision 12's hazard, live). The baseline counts them as `unrecognised` rather than dropping them.
7. **Three tests flake under parallel load**, each green 3/3 in isolation: `ClosingTurnReviewFixTests.AClosingTurnThatSaysNothing…`, `TolerantFileReaderTests.AFileLockedExclusivelyForAMoment…`, `EffortDialOnABridgeDrivenSupervisorTests…`. Not caused by this series; seen once each across ~12 full runs.

## What the series did NOT change

No channel format change; `channel-append.sh`, the lock and the compactor are untouched. No Telegram
change. No change to review independence. The fingerprint watcher still ships. `runners.<role>.wake`
defaults to `watcher`, so **no live session behaves differently until somebody sets it.**

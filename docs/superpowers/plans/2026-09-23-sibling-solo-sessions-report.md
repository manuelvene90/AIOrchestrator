# Sibling solo sessions — GATE REPORT

**Date:** 2026-09-24 · **Branch:** `feat/sibling-solos` · **Base:** `2c2ecb3` (master + plan 03/04 settings
work) · **Merged in along the way:** plan 03 as `2c389e6`, plan 04 as `7ea82c1` and again as `962b13a`
· **HEAD at the gate:** `962b13a` + the Task 18 test fix (`task-18-fix1-commit.txt`) + this report.
**Written by:** the Task 18 implementer (Opus 5.5) for the plan's controller, the solo session of
`ai-orchestrator-29`. The ledger this report is built from is
`.superpowers/sdd/2026-09-23-sibling-solo-sessions-plan/progress.md`. Every ruling, fix round and
deferred minor below is copied from it or from the task-N-report.md / task-N-review.md files beside it,
not reconstructed.
**Spec:** `docs/superpowers/specs/2026-09-23-sibling-solo-sessions-design.md` (`b510849`).
**Plan:** `docs/superpowers/plans/2026-09-23-sibling-solo-sessions-plan.md`.

**Which copy (CLAUDE.md decision 18):** everything here is the **branch source** in the worktree
`C:\Users\Gianpiero\source\repos\AIOrchestrator-siblings`, built and run in that worktree's own `bin\Debug`.
Every task report says the same of itself. No task read, built, installed or started the running app,
the installed `~/.claude` kit (plugin, commands, hooks) or the main checkout, and this report makes no
claim about any of them. A merge is not live until the owner's running binary is rebuilt from it
(decision 23), and a kit edit is not live until the installer has run and a session has been restarted
(decision 17).

**Kit verification status: verified against a restarted session: NOT YET — owner step 4.**

---

## 1. The gate (Task 18)

### 1.1 The focused filters (Step 1)

```
dotnet test AIOrchestratorCoreLib.Tests --filter "…~Siblings|…~Endeavour|…~SpawnSibling|…~SiblingLaunch|…~WorkingPath|…~GitHead|…~ProgressReportBuilder|…~SiblingTurnSources|…~SoloIsToldAboutSiblings|…~SupervisionPathsSiblingFiles"
  → Non superati: 1 · Superati: 265 · Ignorati: 0 · Totale: 266 · 2 m 22 s
```

The one red was `Bridge.Siblings.SiblingConfirmationTests.ABornSibling_AndItsParent_GetTheDerivedFilesOnTheTick`:
`IOException: The process cannot access the file '…\aiorchestrator-2\.siblings' because it is being used
by another process`, thrown from the **test's own** raw `File.ReadAllText` inside its wait predicate while
the tick's `Atomic_FileWriter` replaced the file. The same test was red once in the Task 16/15/17 run
and green 3/3 alone then. Alone now: **green ×2** (`--no-build`, 6 s each).

**Classification: test defect, the file-lock family** (the one Tasks 13c and 15 already cured in
`SiblingEngine_Harness.Read_Session` and `.Channel`). It is fixed in the test by
`task-18-fix1-commit.txt`, as its own commit. While re-running the two sibling classes after that change,
a second red of the same class showed up: `SpawnSiblingArrivalTests.AValidRequest_IsParked_AndTheRequesterIsToldItIsHeld`
("the requester was not told its request is HELD", with only the channel seed in it). The engine parks the
file first and appends the HELD notice afterwards (`Process_SpawnSiblingRequests`). The test read the
channel once, as soon as the parked file appeared, so it raced the append. Alone it was **green ×2**. It is
fixed in the same commit: the test now waits for the entry. After the change, `SiblingConfirmationTests` +
`SpawnSiblingArrivalTests` passed **29/29**. The commit changes no production file and weakens no
assertion.

### 1.2 The three harnesses (Step 1)

**Scope check done before each run.** Each harness pins `AIORCH_SUPERVISION_ROOT` to a temp tree, and I
confirmed that by reading it before running:
- `watcher-behaviour-check.sh` sets `AIORCH_SUPERVISION_ROOT="$home/.claude/supervision"` on a `mktemp -d`
  home, both in `run_role` (line 179) and in the sibling case (line 325).
- `hook-behaviour-check.sh` exports `HOME` and `AIORCH_SUPERVISION_ROOT` to a `mktemp -d` tree (lines 70-76).
- `self-write-suppression-check.sh` works in a `mktemp -d` `WORK` folder, and the only thing it runs is
  `channel-append.sh`, which reads neither variable.

On top of that, each run was started with `AIORCH_SUPERVISION_ROOT` exported to a fresh
`/tmp/sib-t18-root*` tree, and `find` shows that nothing was written into that tree. (A usage-limit
pause, which reset at 19:40, cut the session between the harnesses and the suite. I re-anchored with
`git status` and `git diff --stat`, and read every harness log to its `exit=` line before going on.) Nothing was written under
`C:\Users\Gianpiero\.claude\supervision`.

| harness | result | exit | time (loaded box) |
|---|---|---|---|
| `kit/hooks/watcher-behaviour-check.sh` | **35 checks, 0 failures**. The supervisor case fixed in `9db3e87` (per-role reasons) is green. | **0** | 4 m 07 s |
| `kit/self-write-suppression-check.sh` | 27 PASS, "all cases passed" | **0** | 1 m 59 s |
| `kit/hooks/hook-behaviour-check.sh` | 293 `ok` lines, "All cases match intent." | **0** | 32 m 22 s |

The watcher harness's old red (the stale supervisor expectation, `md5sum failed` against
`md5sum on <file> failed`) is closed. `9db3e87` gave each role its exact reason. No harness needed a fix
at the gate.

### 1.3 The build (Step 2)

```
dotnet build AIOrchestrator.slnx -c Debug   → Avvisi: 20 · Errori: 0   (after the test fix)
```

All six projects were built, the WPF host included (`AIOrchestrator -> …\AIOrchestrator\bin\Debug\net10.0-windows\AIOrchestrator.dll`).
Every one of the 20 warnings is a pre-existing xUnit analyzer warning (xUnit1025, xUnit1031, xUnit2029,
xUnit2031). None is a CS warning.

### 1.4 The suite, once, whole, alone (Step 3)

Run 2026-09-24 19:42–20:02, HEAD `962b13a` + the Task 18 test fix in the working tree, from a clean shell
(`AIORCH_*` unset, WinGet on PATH). Before it started I checked that no `testhost` was running, and no
other suite of mine was running during it. Another repo's `dotnet build` (`00_Shared/DvlDebugServiceModels`)
was running on the box, and the machine was heavily loaded throughout.

```
dotnet test AIOrchestratorCoreLib.Tests                  → Non superati: 7 · Superati: 4839 · Ignorati: 6 · Totale: 4852 · 19 m 16 s
dotnet test tools/claude-contract/ClaudeContract.Tests   → Non superati: 0 · Superati: 31 · Ignorati: 8 · Totale: 39 · 23 s
```

**The set of reds, and each one alone** (`--no-build --filter "FullyQualifiedName~<name>"`, run twice):

| red in the full run | message | alone | classification |
|---|---|---|---|
| `Bridge.TheDoubleTickSurvivesTheEditGapTests.TypedWaitThenGo_ReachesDoubleTick` | `IOException … owner-channel.md … used by another process` | green ×2 | flake, file-lock family (a test-side raw read; plan 03's campaign) |
| `Bridge.StallAlertClockProbeTests.APlainReportGoneQuiet_NeverEarnsAStallAlert` | `IOException … orchestrator-global.log.jsonl … used by another process` | green ×2 | flake, file-lock family |
| `Bridge.AnEntryWrittenAfterAnAppEntryStillReachesThePhoneTests.AnEntryAppendedAfterTheAppsOwnEntry_IsStillMirrored` | "the SECOND question never reached the phone" | green ×2 | flake, wall-clock / tailer quiet window under load (not a sibling file) |
| `Running.WakeUpDigestSecondReviewTests.FiveCycles_WithAnOwnerMessageAndARestart_EachReportWaitsItsOwnWindow` | "cycle 4 was not held before the restart" | green ×2 | flake, known print-runner wall-clock family (red in plan 03's gate and in Task 13 too) |
| `Running.ClosingTurnTests.ASilenceKill_KeepsTodaysRetryPath_AndRunsNoClosingTurn` | "the mute turn was not cut short" | green ×2 | flake, known (red in plan 03's gate) |
| `Running.PrintTurnDispatcherTests.ATurnThatOutlivesTheTimeout_IsKilledAndRetried_AndStallsWithAnAlertOnTheThird` | `Assert.Equal` expected 3 | green ×2 | flake, the same print-runner FakeClaude / wall-clock class |
| `Bridge.Siblings.SiblingOnlyReplyIsNotTextedTests.AnAgentTaggedSoloReply_ReachesNoTopic_AndLeavesTheOwnersAnswerCreditOpen` | "the owner's message was never delivered, so no credit was raised" | green ×2 | flake, **this plan's test**: it fails at its own precondition (the owner's inbound message had not been delivered within the wait window), before it reaches anything the sibling code does. It is a load-timing window, not an O3 failure. It was green in every earlier filtered run (Task 13 fix round, and the focused filter at this gate). |

**No red is real, and the gate is not stopped.** Six of the seven reds are in files this plan never
touched: the `Running/` print-runner family and the `Bridge/` file-lock / wall-clock family. The seventh
is a sibling test that fails before it reaches the behaviour it pins. The two reds the focused filter
found are fixed by `task-18-fix1-commit.txt`, and neither repeated in the full run. None of
`SiblingConfirmationTests`, `SiblingLifecycleTests` or `SpawnSiblingArrivalTests` was red.

---

## 2. Per task

Every task read and edited the **branch source** of this worktree only; each task-N-report.md opens by
saying so. "Controller" means the controller's own `--filter` run after the implementer's, made before
committing. Under the global constraints, no task before Task 18 ran the whole suite.
**Engine lines** are `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`. Each task report
lists its lines one by one, and they are summarised here.

| task | what was built | commits | engine lines changed | verification | reds met, and whether they repeated alone |
|---|---|---|---|---|---|
| 1 — session model | `EndeavourId`, `BornFromOrchId`, `BornFromHandover`, `WorkingPath` on the session (nullable, set once, serialised); `Set_EndeavourId` / `Set_SiblingLink`; `EndeavourMembers_Resolver` (membership derived, never a list) | `b4ee2d2` | none | 38/38; controller 57/57; mutant (`?? existing.WorkingPath` dropped) red | none |
| 2 — paths | `Get_SiblingOutboxFile` (`sibling-outbox.md`), `Get_SiblingsListFile` (`.siblings`), `Get_EndeavourDigestFile` (`ENDEAVOUR.md`), in the orch folder root, so `ChannelDiscovery` never finds the outbox (O3 by construction) | `8e3a577` | none | combined 587/587 (1 pre-existing skip) | none |
| 3 — the cap as data | `endeavour.maxOpenSiblings` (default 3, range 1-10, Machine/Kernel), `IEndeavourSettings`, `EndeavourSettings_Json.Parse` with no `Write` | `8a53580` | none | 8/8 through the real loader | none |
| 4 — `spawn-sibling` reader | `ISpawnSiblingRequest`, `SiblingName_Rules` (ruling F: 2-4 words after the code), `job` ≤ 200 chars and one line, `handover` > 0, `reason`; model/effort ignored | `fa59eb0` | none | 38/38; review Approved | none |
| 5 — launcher | `WorkingPath_Resolver`; `Start_SiblingOrchestration`, which creates, links, names, copies overrides and seeds PLAN.md before `Add_Member`; `Respawn_Implementer` spawns in `WorkingPath` (every respawn route: watchdog, restart, `/model`, `/effort`, resume) | `860fe3e` | none | behavioural RED 6/14 on `RepoPath`, GREEN 14/14; neighbours 56/56 | none |
| 6 — refusal table | `SiblingWorld_Reader` (git only here), `SiblingRequest_Validator`, `SiblingRefusals` (10 + `worktree-not-absolute`), `SiblingNotice_Wording`, `WorkingPath_Comparer`, `GitWorktree_Tool`. Fix round 1: `worktree-shared` over every open session plus git's main checkout, the parent's own name reserved, git calls counted | `720fe50`, `f7ee7d4` | none | 36/36, then 134/134; 3 mutants each killed by one test; fix-round RED 4 | none |
| 7 — birth step | `SiblingBirth_Step` (link the parent **after** a successful launch), birth note / parent / General wording, `SiblingEngine_Harness` (real git worktree, system clock, first-sight wait), `ScriptedInbound_Fake.Use_DistinctTopicIds` / `Sent_InTopic`. Fix round 1: the owner half of the birth note is in the subject, and a spawn failure after creation links the parent and names the child | `8a3e2e0`, `08eaea4` | none | 63/63, then 65/65; mutant (spec-literal order) killed by `ALaunchFailure_…LeavesTheParentUnlinked` | none |
| 8 — arrival | `Process_SpawnSiblingRequests`: refuse to the requester (`unspawnable` to General, as Agent), or park, or wait out a usage-limit pause; `ParkedCloseKinds.Sibling`; the whole prompt half (ruling A); `Build(…, requesterName)` (ruling H) | `402af95` | +67/−3: a `using`; "THE FOUR THAT SPAWN" + one call in `!dispatchPaused`; new `Process_SpawnSiblingRequests`; `Ask_OwnerToConfirmClose_Async` passes the requester's name | RED 11/11, GREEN 50/50, neighbours 202/202; controller 332 passed / 1 skipped; mutants M1-M4 each red | an OOM episode mid-task (foreign 20 GB testhost) truncated the engine; it was restored and every measurement redone |
| 9 — the tap (O1) | the confirmed tap hands the birth to the tick (`!dispatchPaused`, just before the watchdog: a held yes during a pause, and no double-spawn window); prompt-time re-run of the table; `CloseTapResult.ArchiveLabel` so "✅ Started" appears only when the birth ran (ruling E); verbatim failure to the requester (Agent) and General (Owner) | `7bbf71d` | +325/−47: two fields; the drain call in the tick; `Tell_SiblingRefusal`; `Is_BeingResolved`; the moot arm and prompt-time re-run; the tap hand-off; extracted `Record_TapDecision_OnPrompt_Async`; the Sibling arm of `Execute_ConfirmedClose`; five new call-and-append methods | RED 12/16, GREEN, widened 244/244 + 170/170; controller 263/263 | the pause test gave up at 20 s before the 60 s probe throttle (a test fault, the wait raised to 90 s); a shared `scratchpad/mutate.py` collision ran once against the plan-04 worktree (plan-04 verified clean) → ruling S3 |
| 9b — held-for-pause wording + unclassifiable tap | the prompt no longer promises a re-ask past the 12 h lapse; the executor acts on the tap's own read (a transient null read can no longer run a birth inline on the inbound loop) | `4e78b54` (wording); engine half in `7e5d700` | `Execute_ConfirmedClose(confirmation, tapped)` and signature; the tick drain re-reads; catch comment | RED reproduced the bug exactly ("Children: 1, archived: [started-…]") | none |
| 10 — derived files | `.siblings`, `ENDEAVOUR.md` reconciled every tick (`EndeavourArtefacts_Step`, `EndeavourDigest_Builder/Reader`, `EndeavourMarkers_Sync`, `DerivedFile_Writer`), branch from `.git/HEAD` (`GitHead_Reader`, no git on the tick), `PlanLedger_Markers` constants | `7e5d700` | +25/−8 with 9b: `Sync_EndeavourArtefacts();` after `Sync_PausedFlags();` and the method | RED 37/45, GREEN 45/45, final 443/443; controller 198/198; mutants M2-M6 each red | none |
| 10b — review minors | outbox compaction after the owner's delivery; "no git on the tick" pinned by scan; the name in the `##` header sanitised; `SiblingDigestInput` doc and `OwnerChannelHistory` rename | `6f63439` | `Compact_SiblingOutboxes();` before `Compact_LongChannels();` and the method; a doc line | RED 5/30, GREEN 38/38; 3 mutants red | none |
| 13 — print/stream runners | `TurnSourceKinds` (Owner/Spoke/Sibling), a solo woken by its siblings' outboxes and never while paused, the ruling-D cursor prune, `ENDEAVOUR.md` in the state pack; beyond the brief: an outbox is never a reply target, `Advance_Cursors` keeps an unresolved source's cursor, a sibling entry is never digested. Fix round 1 (ruling S4): a reply to siblings alone is filed `[agent]`, never texted, and never counted as an answer | `adc36d6`, `00500c9` | none (runner files) | RED 14/29, GREEN 29/29, widened 387/387; fix round RED 3, GREEN 21/21, widened 617 (+2 red, below) | `WakeUpDigestSecondReviewTests.FiveCycles_…` green ×2 alone (wall-clock family); `SiblingConfirmationTests.AConfirmedTapWhoseRequestCannotBeReadOnce_…` red 2/6 alone → Task 13c |
| 11 — General's bar | `EndeavourProgress_Reader.Sum_OrNull`; `ProgressReport_Builder.Build_OpenOrchestrationsText` / `Build_EndeavourBlock_OrNull` (ruling B) / `Build_CountsLine`; an endeavour renders once as `🔗 <group> — n/m done` | `f28373e` | +4/−25: General's branch of `Build_ProgressReportText` and `Build_OrchestrationCountsLine` delegate to the builder | RED 16/16, GREEN, widened 303/303; controller 282/283 | `SiblingConfirmationTests.AConfirmedTapWhoseRequestCannotBeReadOnce_…` flaky alone 1/3 (not Task 11's) → 13c |
| 13c — the flake | test-side session reads through `Tolerant_FileReader` (`Read_Session(s)`) | `79e11b6` | none | class 5/5 green (18/18 each) | the cause: the test's raw `session.json` read |
| 12 — `/endeavour` (O3) | `EndeavourReport_Builder` (the block + the last 3 outbox subjects per open member, archive included); menu 36 → 37; O3 pinned on the real engine (`SiblingOutboxIsNeverMirroredTests`) | `a91359c` | `endeavour` arm in `Try_RunOwnerCommand_Async`; new `Send_EndeavourReport_Async` | RED 10/15, GREEN 15/15, wide 984/986 (2 skips by design); controller 514/514; 2 mutants red | none |
| 14 — lifecycle | survivors get an `[agent]` close notice with the closed sibling's unfinished lines; promote and `/switch` refused while linked | `14ccf45` | +33: refusal block in `Process_PromoteOrchestrationRequests`; survivor post-step in `Execute_Close` (own try/catch); early reply in `Switch_OrchestrationShape_Async` | RED 5/9, GREEN 9/9 ×6, brief filter 34/34 ×3, wide 373/373; mutant (Owner audience) killed after adding the `[agent]` assertion | `ClosingASiblingWithOpenLines_IsNotRefused` once, right after the build → 14b and Task 15 |
| 14b — ruling S5 | `OrchestrationSessionStoreModel.Get_Session_OrNull` reads through `Tolerant_FileReader` (the only production read of `session.json`) | `708440f` | none | store 28/28; wide 489/490 (1 skip by design); lifecycle alone 10/10 | `SpawnSiblingArrivalTests.EachRefusal_…(worktree-missing)`: the test's own raw channel read, 11/11 alone (fixed at this gate) |
| 16 — kit prose | `solo/SKILL.md` "asking for a SIBLING" (recipe, the refusal shapes), `reference/siblings.md` (§8.2, archive and spurious-wake carries, print runner `[agent]`), a general-supervisor bullet; ruling F's example `AI-Orch · limits rework`. Fix round 1 (ruling S6): `touch "$ORCH/sibling-outbox.md"` before the first append, pinned with the real helper | `a653a7e`, `9db3e87` | none | RED 12/19, GREEN 19/19; Kit 257/257; fix round Kit 260/260 | none |
| 15 — the O4 pin | `QuestionHoldIsPerTopicTests` on two linked solos with distinct topics, using real QUESTION entries; the harness `Channel()` goes through the tolerant reader | `463fc3c` | none | green at once 2/2 (a pin) | the lifecycle flake reproduced once: a test-side raw read, fixed here |
| 17 — the watcher | the solo watcher's sibling half (per-file fingerprints, baseline on first sight, silent while `.paused`, one WATCHER BLIND after 12 failures); harness sibling case; self-write transcription checked verbatim. Fix round 1: exact per-role reasons, `sibappend` through the real helper, an empty outbox is `absent`, `AIORCH_SUPERVISION_ROOT` pinned in `run_role` and `hook-behaviour-check.sh` | `f2a6b02`, `9db3e87` | none | 3 watcher mutants red; fix round 2 mutants red; 3 harnesses exit 0; Kit\|Siblings\|QuestionHold 422/422 | `ABornSibling_AndItsParent_…` once under load, green 3/3 alone (fixed at this gate) |
| 18 — the gate | §1 above; test fix `task-18-fix1-commit.txt` | this commit + the fix | none | §1 | §1.1, §1.4 |

### 2.1 The two mutants the brief names

- **Task 9 (the tap).** The brief's mutant disabled the Sibling arm of `Execute_ConfirmedClose`, so a
  confirmed tap fell into the unknown-kind arm. It was **caught by**
  `SiblingConfirmationTests.AConfirmedSibling_NeverClosesTheRequester`, on its "started" assertion: "the
  confirmed sibling tap started nothing. Archived: [unexecuted-sibling-…]". That test asserts both halves
  on purpose. A test that asserted only `parent.ClosedUtc == null` would have stayed green, because the
  unknown-kind arm closes nothing either. The companion M2 (the Sibling arm routed into `Execute_Close`)
  was caught by the same test's `Assert.Null` on `parent.ClosedUtc`. M3-M6 (pause gate, tailer seed,
  prompt-time re-run, ruling E) each reddened their own test (Task 9 report, mutation table).
- **Task 15 (O4).** The mutant changed the engine's question-hold call (`BridgeEngineModel.cs:3976`) to
  `_store.Load_All().Any(s => s.ClosedUtc == null && Is_AwaitingAnswer(s.OrchId))`, which is a global
  hold. **Both facts went red**: `QuestionHoldIsPerTopicTests.AQuestionPendingInA_DoesNotHoldBsEntries`
  and `.AnAnswerInA_ReleasesOnlyAsHeldQuestion` ("O4 BROKEN: B's question was held …"). The file was
  restored byte-exact.

---

## 3. Owner decisions O1–O4

Accepted as proposed on 2026-09-23 (entry [43], "Yes, all 4"; 'go ahead' at 14:12). **No answer
differed from the proposal, so no task changed because of one.**

| # | question | answer as given | built by |
|---|---|---|---|
| O1 | Does a sibling's birth need your tap? | **Yes**. The tap lapses after 12 h. | Tasks 8 (park + prompt), 9 (tap → birth) |
| O2 | Maximum open siblings per endeavour | **3**, as the catalogue row `endeavour.maxOpenSiblings` | Task 3 (row), Task 6 (`at-cap`) |
| O3 | Should sibling-to-sibling traffic reach your phone? | **Never pushed**; on demand with `/endeavour` | Task 2 (outbox not discoverable), 12 (`/endeavour` + engine pin), 13 fix round (print path, S4) |
| O4 | Does a question pending in one sibling's topic hold the other's? | **No**, holds are per topic | Task 15 (engine pin + prose) |

## 4. Deviations from the spec

1. **The check order (Task 6).** The order is `unspawnable` → `not-a-solo` → **`handover-already-used`** →
   `no-handover-entry` → `at-cap` → `worktree-not-absolute` → `worktree-missing` → `worktree-not-of-repo`
   → `worktree-shared` → `name-taken`. Spec §4.2 lists `at-cap` before already-used. With the spec's
   order, a retry of the request whose own birth filled the cap would be answered `at-cap` instead of
   "already used, here is the child". `ARetryAfterTheBirthThatFilledTheCap_IsAlreadyUsed_NotAtCap` pins
   this, and the validator's XML doc records it. Also new: `worktree-not-absolute`, a label outside the
   spec's list, added under the binding carry that forbids anything keyed on a relative path.
2. **The parent is stamped after the launch (Task 7).** Spec §4.3 stamps the parent's `EndeavourId`
   (step 2) before it launches the child (step 3). `SiblingBirth_Step` launches first, and stamps the
   parent only after the launch returns, using the id the child was given. A launch that fails before
   creation therefore leaves the parent unlinked (`ALaunchFailure_Throws_AndLeavesTheParentUnlinked`,
   which the spec-literal mutant reddens). A failure after creation links the parent to the child that
   exists, and names that child (fix round 1). Consequence (Task 13 M1, noted, not changed): for a first
   sibling, the child's `sibling:<parent>` print source appears one tick late, so the parent's HANDOVER
   is delivered once as traffic rather than absorbed as baseline.
3. **The `unspawnable` audience (Task 8).** The spec's "general-channel failure, as in promote" would be
   the Owner audience. It is `AppEntryAudiences.Agent` in General instead, under decision 15: the owner
   cannot act on a request from an orchestration that does not exist. The method doc and the commit body
   record this.
4. Smaller ones, each recorded in its task report: the requester's own display name is reserved
   (`name-taken`); General's birth-*failure* line is Owner audience (Task 9, following the
   `Execute_Close` precedent, because the Uncertain prompt line sends the owner to General); the digest's
   branch comes from `.git/HEAD` (`GitHead_Reader`), not from `GitSnapshot_Reader` as §5.1 says, so no git
   runs on the tick; an empty outbox counts as `absent` for the watcher (Task 17 fix round), so the taught
   `touch` never wakes anyone; the spec's §4.1 example name `AI-Orch · limits` breaks the spec's own
   2-4-word rule (ruling F), and the skill teaches `AI-Orch · limits rework`.

## 5. Rulings

**Pre-flight rulings A–H (spec binding, all applied).** A: the whole pure prompt half moved into Task 8,
because a throwing arm is reached by the per-tick ask sweep (measured by Task 8's M1). B:
`Build_EndeavourBlock_OrNull`, one spelling of the group, used by General and `/endeavour`. C: the O3 and O4
pins run on `SiblingEngine_Harness` with distinct topic ids. D: `CreateFrom_Delivered` takes the source
kind. E: Sibling decision wording, and a tap-time refusal renders `⚠️ Not started — …`, never `✅`. F: 2-4
words after the code. G: the `PauseGatesEveryWakerScanTests` edits serialised, each a self-contained fact.
H: one prompt path, `Build` fed the requester's name.

**Controller rulings S1–S6 (binding).**
- **S1: execution order.** Tasks 1-6 first, with no engine edits. Task 7 and the engine chain (8, 9, 11,
  12, 14) only after plan 03's Tasks 8 and 9 were merged in. Task 12 only after plan 04's Task 5 (menu
  count 35→36→37). Task 14 only after plan 03's Task 10. Cost if wrong: idle time.
- **S2: session conventions.** As in plans 03/04: no git writes in sub-agents (task-N-commit.txt, the
  controller commits), the env/jq prefix, and the trailer `Co-Authored-By: Claude Opus 5.5`.
- **S3: unique scratch names.** Every dispatch names unique scratch files, prefixed with the task and
  branch, after two agents shared `scratchpad/mutate.py` on 2026-09-23. Cost: none.
- **S4: Task 13 I1 fixed at once, not carried.** A print turn woken only by sibling traffic wrote its
  final message into the solo's mirrored owner channel, which breaks O3. The fix STOPS THE PUSH: the
  entry stays in the channel as the record, `[agent]`-tagged, never mirrored and never credited as an
  answer. It is not routed to the solo's outbox, because two print solos would then acknowledge each
  other forever. Cost if wrong: one more round.
- **S5: the store's raw `session.json` read** (parked 2026-09-24) was admitted as part of the lifecycle
  line (decision 22's first admission), because it BLOCKS a requested line: a survivor's close notice
  could be lost to it. Task 14b reads through `Tolerant_FileReader`. Cost if wrong: a small, general
  robustness change outside the sibling files.
- **S6: C1 (nothing created the outbox) fixed in the PROSE.** The fix is `touch`, which never truncates,
  before the first `channel-append.sh`, in both SKILL.md and siblings.md. A harness step pins it by
  running the REAL helper against an absent outbox. The app does not create the file, because that would
  need an engine change at gate time. Cost if wrong: one extra line the solo must follow.

## 6. Deferred minors (from the ledger and the reviews, not fixed)

- T1: stacked `<summary>` blocks in `OrchestrationSession_Factory.cs:74` (pre-existing); session.json now always carries four `null` keys.
- T2-4: `AnIllegalName` asserts only non-empty (it could pin a keyword per rule); a non-string name/job/worktree falls to a generic "wrong type" (pre-existing pattern); stacked `<summary>` in `OrchestrationRequests_Reader.cs:71-75`; the spec's §4.1 example name is one word (ruling F).
- T6 (PARKED): 8.3 short-path normalisation belongs in `SiblingWorld_Reader` if it is ever wanted (a short-spelled worktree is refused `worktree-not-of-repo`, and the refusal lists git's spellings); `GitSnapshot_Reader` reads stdout before `WaitForExit`, so its 8 s timeout never fires (a hung git would stall the loop at arrival or tap).
- T7: a kit-gate-refused spawn reports "started" (same as start-orchestration). The watchdog double-spawn window was closed by Task 9's tick hand-off.
- T8: the HELD notice says "The owner has been asked" at arrival ("is being asked" would be accurate); a throw after a successful `Park` sends mixed messages (promote precedent); restart and duplicate-drop idempotency is covered by structure, with no end-to-end harness test.
- T9: the held yes is in memory only (a restart re-asks, fail-safe); a pause starting between the tap and the next tick leaves the original prompt text until the lift; the birth note's append result is ignored (edge); the pause test does not check the final "✅ Started" edit.
- T10: a persistent derived-file failure logs one Warning per tick (the `Sync_PausedFlag` precedent); a test clock nit; sub-tasks count against the digest's 15-line cap.
- T13: M1 (first-sibling print source one tick late, see §4.2) noted, not changed; rules keyed on "the last solo entry" (PULSE, the digest's last 6) were not audited for `[agent]`-tagged solo entries; about eight test classes still carry private branch-source walks.
- T11: a one-member group renders as a group; `Split_Code_OrNull` splits on the first separator (creation already enforces one).
- T12: two more tests poll the store raw (fixed for the arrival class's channel reads at this gate); `/endeavour` reads the full outbox history per call (owner-driven, fine).
- T14: a promotion parked BEFORE its solo became linked is not re-checked at the tap (the spec names only arrival); the survivor name is the full display name.
- T16/17: a sibling whose outbox read FAILS on first sight is baselined on the next good read (an entry in that window is not announced; the boundary read still catches it); siblings.md does not state the cap value (configurable; `at-cap` names it).
- Standing: CLAUDE.md's PAUSE bullet should gain "the sibling turn sources (`TurnSources_Resolver`)" (spec §5.4); not edited by any task.

## 7. The owner's phone checklist (Step 4)

**The owner runs this after merging and rebuilding the app. This session did not run it: sessions never
start the WPF host (git-and-boundaries).** Before starting:

- **Which binary is live** (decision 23): `Get-Process AIOrchestrator | Select Path` must be the freshly
  built output, not `bin\Debug - Copia\…`.
- **Which kit is installed** (decision 17): **after the merge, run `kit/install.ps1`**. The skills
  changed in this plan (solo SKILL.md, `reference/siblings.md`, `reference/watcher.md`, the
  general-supervisor bullet) and in plan 04 as well. Then restart the app so `KitAssets_Bootstrapper`
  records a fresh verdict, and start a **new** solo, because a session that was running before the
  restart still holds the old skill text.

| # | do | expect |
|---|---|---|
| 0a | Open the settings page | It opens on `127.0.0.1:7391`, with **editing open by default** (plan 04). |
| 0b | In a topic, let a session ask a question with options (classic preset) | Under the options: **"❔ Explain the options"** and **"💬 Let's talk"** (`202d2f8`: the buttons under a question are a setting). |
| 1 | In a solo topic: *"do X in parallel, I want to follow both"* | The solo creates a worktree, runs `touch` on its outbox, appends `HANDOVER` to it, and drops `spawn-sibling`. One line tells you it asked. |
| 2 | — | **One prompt in that topic**: 🔗 … New topic / Job / Why, with **✅ Start it / ✋ Keep one session** (O1). |
| 3 | Tap ✅ | A new topic appears with the requested name and the repo's colour. Its first message is `🔗 Sibling of …`. The parent topic gets `⚙ App: sibling started — … (its own topic)`. |
| 4 | Write in the new topic | Only the new sibling answers. The parent is silent. |
| 5 | Ask the parent about the other job | It answers from its digest and does not act. An instruction for the other job is relayed, with a one-line "passed it on". |
| 6 | Watch both topics for 10 minutes of work | **No sibling-to-sibling text on the phone** (O3). `/endeavour` in either topic shows the pair and the last outbox subjects. |
| 7 | `/progress` in General | One `🔗 … — n/m done` group with a line per sibling. Other orchestrations render as before. |
| 8 | Let one sibling ask a question and leave it unanswered | The **other** topic keeps delivering (O4). |
| 9 | **Resume from the worktree (§7.1).** Kill the child's terminal. | The watchdog respawns it, and `orchestrator.log.jsonl` says `resuming conversation`, **not** `fresh`. If it says fresh, take §10's fallback: stop and report, do not patch live. |
| 10 | `/pause` in one sibling | The other keeps working. The paused one stays silent to sibling traffic. Writing in its topic wakes it, and it reads its outboxes. |
| 11 | `/switch` in a sibling topic | `this topic is linked to siblings — close them or keep one session.` |
| 12 | Close one sibling (`/close`, tap) | The other gets an `[agent]` entry and **no phone push** about it. General's group drops the closed line but **keeps its counts**. |
| 13 | Drop the same `spawn-sibling` file again by hand | Refused `handover-already-used`, naming the existing child. No second topic. |
| 14 | Try a fourth open sibling | Refused `at-cap` (O2 = 3), naming the open ones. |

Row 9 is the one thing no test here can prove: the tests pin that every respawn runs in the worktree
(`SiblingLaunchTests`), but not that Claude Code's transcript lookup finds the conversation from there.

## 8. Verdict

**The plan is built, reviewed and gated in the branch source.** Every task, 1-17 with 9b, 10b, 13c and
14b, is complete and reviewed clean, several after one fix round. All three harnesses exit 0. The solution
builds with 0 errors, the WPF host included. The one full run has 7 reds, and each one passed twice alone.
The contract suite is green.

What is NOT proven here, and waits on the owner:
- **The kit is not verified against a restarted session** (decision 17). The owner runs `kit/install.ps1`,
  restarts the app and starts a new solo (§7).
- **Resume from the worktree (§7.1, checklist row 9)** is proven only as "every respawn runs in the
  worktree". Claude Code's own transcript lookup is proven only live.
- **The flake family stands.** This branch adds one name to it
  (`SiblingOnlyReplyIsNotTextedTests.AnAgentTaggedSoloReply_…`, a precondition timing window under load).
  Plan 03's deferred Task 11 campaign still owns the family. D12's bar (three local full runs + CI) is not
  met here either, and CI has not run on this branch.

# Sibling solo sessions: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A solo can ask the app for a **sibling**: a second solo in its own orchestration, with its own Telegram topic, linked to the first by an `endeavourId`. The siblings coordinate through one outbox each. Each reads the others' state from a derived digest at every boundary. The owner sees one combined bar in General.

**Architecture:** A sibling is an ordinary basic orchestration, so every per-orchestration mechanism (topic, routing, ledger, hold, pause, resume, watchdog, `/model`) comes with it unchanged. Four nullable session.json fields link siblings. Membership is **derived** from `endeavourId`, never stored as a list. New logic goes in new components under `Bridge/Siblings/`, `Sessions/`, `Running/TurnSource/` and `Configuration/EndeavourSettings/`. `BridgeEngineModel.cs` gets **call sites only**: one request processor, one tap branch, one tick step, one command, two refusals and one post-close step, each a few lines that call out. The kit gets one new reference file, one new SKILL.md section and a second half of the solo watcher.

**Tech Stack:** .NET 10 (`net10.0`; WPF host `net10.0-windows`), xUnit, `System.Text.Json.Nodes`, git (the executor lists worktrees), bash (msys on Windows) for the kit harnesses.

**Spec:** `docs/superpowers/specs/2026-09-23-sibling-solo-sessions-design.md` (this worktree, commit `b510849`). Read the spec in full, together with this plan. The plan argues from the spec's sections (§n), and each task names the ones it implements.

**Worktree and branch:** `C:\Users\Gianpiero\source\repos\AIOrchestrator-siblings`, branch `feat/sibling-solos`, based on `master` at `83da77c`. Every task commits here. Never commit to `master`: the merge is the owner's call (`.claude/rules/git-and-boundaries.md`).

**Which copy every statement here was read from (CLAUDE.md decision 18):**

| document | copy read |
|---|---|
| the spec, `CLAUDE.md`, `.claude/rules/*.md` | branch source, worktree `AIOrchestrator-siblings`, HEAD `b510849` |
| plan 03 (the style this imitates) | `git show plan/03-behavioural-seams:docs/superpowers/plans/2026-09-12-behavioural-seams-03.md` |
| every production and test file named below | branch source, worktree `AIOrchestrator-siblings`, HEAD `b510849`, read 2026-09-23. Line numbers are approximate. Every citation names a symbol, so search for the symbol, not the line. |
| build output, installed `~/.claude`, the running app | **NOT READ.** Nothing here makes a claim about them. Task 18 is the first place any of them matters. |

---

## Owner decisions: the plan assumes the spec's recommendations

The owner is being asked to accept §9's four recommendations. This plan is written **as if they have**. Each decision lives in **one task** (or one named step), so a different answer changes that task and nothing else.

| # | assumed answer | where it lives | what a different answer changes |
|---|---|---|---|
| **O1** | A sibling's birth needs the owner's **tap**. The prompt lapses after `CloseConfirmation_Parking.EXPIRY_HOURS` (12). | **Task 9** only. Task 8 parks the request, and Task 9 asks for the tap and executes on ✅. | For "no tap": delete Task 9, and have Task 8 Step 4 call `Execute_SiblingBirth` (Task 9 Step 5's method, moved into Task 8) instead of `CloseConfirmation_Parking.Park`. Tasks 6 and 7 do not move. |
| **O2** | At most **3 open members** per endeavour, the requester included (so a parent plus two children). The number is data: `endeavour.maxOpenSiblings`. | **Task 3** (the setting) plus one parameter in Task 6 (`maxOpenMembers`). | Another number: change the catalogue default in Task 3. No cap at all: Task 3 sets the default to the maximum, and nothing else changes. |
| **O3** | Sibling traffic is **never pushed** to the phone. `/endeavour` shows it on demand. | **Task 12**: the command, plus a test pinning that an outbox is never mirrored. | For "push": Task 12 becomes "add the outbox to `ChannelDiscovery`" and its pin test flips. No other task reads the answer. |
| **O4** | Question holds stay **per topic**. A question that affects both jobs is asked once, by the sibling whose job it blocks. | **Task 15**: an engine pin test plus one prose paragraph in `reference/siblings.md`. | For "cross-topic holds": Task 15 becomes a change to `QuestionHold_Policy.Should_Hold`'s caller, and its test flips. No other task reads the answer. |

---

## Non-goals: say no to these out loud

- **The WPF app.** It gets whatever the engine gives it for free. A sibling is an orchestration, so it already shows as a card. No XAML is touched.
- **Crews with siblings.** A linked orchestration cannot be promoted or `/switch`ed (§7.6, Task 14). No task teaches the supervisor role anything.
- **Unlinking.** v1 has no way to take an orchestration out of an endeavour (§7.6).
- **The spec's PARKED findings (§10).** The PreToolUse hold hook missing from solo's frontmatter, `Clear_AwaitingAnswer_ForDeadSession` covering the supervisor only, `AIORCH_SUPERVISION_ROOT` not exported by `Build_SessionScript`, and decision 4's merged-spoke tags. **None blocks a task below.** If one turns out to block a task during execution, it becomes part of that task: say which task and why in the report, and do not open a new one (decision 22).
- **Splitting `BridgeEngineModel.cs`.** The move-out rule (`.claude/rules/code-conventions.md`) applies per touched piece. Each engine-touching task names the piece it moves and the lines it changes.

## Global Constraints

- **Every `dotnet` command is prefixed, in the same bash invocation, exactly like this:**
  `export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet …`
  The PATH line puts `jq` on the path for the statusline parity fixtures. The `env -u` strips the orchestration variables that the executing session itself carries. Without them, tests that resolve the supervision root or the role read the **live** tree.
- **`AIOrchestratorCoreLib` is strict.** Use the triple: `IXxx` + `internal sealed class XxxModel : IXxx` + `static Xxx_Factory` with `Create_*`. Only the factory calls `new`. Name files `Xxx_Yyy.cs`, with the underscore only when the second word is a role (`_Resolver`, `_Builder`, `_Reader`, `_Step`, `_Validator`, `_Sync`, `_Wording`, `_Comparer`). Name methods `Verb_Object[_Modifier]`, and end anything that may not resolve in `_OrNull`. Properties are get-only, set from a primary constructor. **No `record` types.** Ad-hoc multi-value returns are value tuples. Plain data types have no underscore (`SiblingWorld`, `SiblingDigestInput`). XML docs argue the **why**, with dated evidence where it exists. A comment that only restates the code is noise.
- **Tests.** xUnit `[Fact]`/`[Theory]`. Test folders mirror production namespaces 1:1. The class is `<Subject>Tests` with the underscore stripped. Methods are `Verb_Scenario_Outcome`. **Stubs, not mocks.**
- **`BridgeEngineModel.cs` (16 337 lines) gets call sites, not logic.** Any logic this plan needs goes in a new file under `Bridge/Siblings/` (or the named folder), and the engine calls it. Where a task edits an existing engine method, that method's touched piece moves out if it is more than a call-and-append. Each engine task's report **names every line changed** in the engine.
- **Engine tests are real engine tests that use the existing fakes, and they never spawn a real `claude`.** The harness is `BridgeEngine_Factory.Create_WithTelegramClient(paths, configProvider, store, launcher, log, telegramClient, timing)`, or `Create_WithDecisionState(…, BridgeTestTiming.Fast(), …)` when the test taps a button. The Telegram fakes are the existing ones: `FailableTelegram_Fake` (inside `Tests/Bridge/OwnerAnswerSurvivesFailedSendTests.cs`) for mirror assertions, and `ScriptedInbound_Fake` (inside `Tests/Bridge/TheInboundLoopSurvivesItsOwnBatchTests.cs`) for prompts and taps, because it **records button rows** (`Find_ButtonFor`, `LastButtonMessageId`, `Queue_Updates`). The spawner is always `RecordingSpawner_Fake`, and the config fixture **never** names a print or stream runner: `BridgeEngine_Factory` wires the real per-OS `claude` into the print dispatcher, so a test that registers a print session can start a live process. Task 7 creates one shared fixture, `TestSupport/SiblingEngine_Harness.cs`, so the five engine test classes below do not each copy the constructor (the rule's "share the helpers instead of adding a fifth copy").
- **Never run the full suite except in Task 18.** Run one suite at a time on this machine. A red seen under load is re-run **alone** with `--filter` before it is believed. Compare the **set of names** of failing tests, never the count. The known flaky families are the file-lock / wall-clock tests under `Bridge/` (plan 03's campaign) and `PrintRunnerTestHarness.Drive_Until`. A red in those is re-run alone twice and reported by name either way.
- **Never spawn git on the 2-second tick.** The executor lists worktrees at arrival and at tap, which are rare events. The digest reads a branch name from files (`GitHead_Reader`, Task 10), never from a `git` process: N siblings × every 2 s × several `git` forks is a load nobody asked for.
- **Decision 12: never a second copy of a formatter or a fact.** The bar is `PlanProgress_Formatter.Describe_Counts`. Ledger parsing is `PlanLedger_Parser.Parse_OrNull`. History spanning live file and archive is `ChannelHistory_Counter.Read_Entries` (decision 13). The working directory is `WorkingPath_Resolver.Resolve` (Task 5): one definition, read by the launcher, the validator and the digest.
- **Decision 15: an alert the owner cannot act on does not go to Telegram.** Refusals, survivor notices and held notices are `AppEntryAudiences.Agent`. Only the birth note, the parent's "sibling started" line and the tap prompt are Owner-audience.
- **Decision 22: only what the spec needs.** A reviewer or implementer who finds something else writes one `NOTICED (not fixed)` line in the report.
- **Kit edits** (`kit/skills/**`, `kit/hooks/**`, `kit/*.sh`) follow `.claude/rules/kit-and-scripts.md`: reorganise, never rewrite an existing rule; `#!/usr/bin/env bash`, `set -euo pipefail` where the file already uses it, BSD portability (`md5sum` → `md5 -q` fallback, no `date -d`, no `stat -c`), a `mkdir` lock and never `flock`. Per CLAUDE.md decision 17, a kit edit is **not verified by editing `kit/` and re-running the installer**. It is verified against a **restarted** session, because `KitAssets_Bootstrapper` records its verdict at startup. Task 18 does that, and says so.
- **Windows.** `python3` is native Windows Python and cannot open msys paths. Bash heredocs over about 6 KB die as a fake quote error, so write scripts with the Write tool and run the file. Quote `git show "ref:path"` whole. `git worktree list --porcelain` prints `C:/Users/...` with forward slashes (Task 6 normalises this).
- **Git.** Stage explicit paths. Never `git add -A` / `.` / `commit -a`, never `--no-verify`. Multi-line messages go through `git commit -F <tempfile>`, with the temp file written by the Write tool into the scratchpad. Use one commit per task (or per concern inside a task). Messages are English, `type(scope): clause`, and the body carries the why. Every commit ends with:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- **Say which copy you read** in every report: branch source, build output, installed (`~/.claude`), or the running app's folder (`Get-Process AIOrchestrator | Select Path`).

## Review Focus

These are the five inputs the spec implies and no happy-path test meets, most likely first. Each has its test in the named task.

1. **A worktree path spelled differently from `git worktree list`.** Git prints forward slashes, and the agent may write backslashes, a trailing separator or another case. The same tree must compare equal. Otherwise a legitimate request is refused `worktree-not-of-repo`, or two siblings share a tree without `worktree-shared` firing. → Task 6, `WorkingPathComparerTests`.
2. **A sibling name carrying a tab, a newline or a stray `·`.** The agent chooses the name. A tab splits the `.siblings` line that bash reads with `IFS=$'\t'`, and a newline adds a phantom sibling. The reader refuses control characters (Task 4). The marker writer still sanitises, in case a hand-edited session.json slips one through (Task 10). → Tasks 4 and 10.
3. **A second tap, or a retry of the request after the birth.** The owner double-taps, or the solo re-drops the same file after a `/model` respawn. Either way the result must be exactly one child, and the retry is answered "already started as `<id>`". → Task 9, `SiblingConfirmationTests.ASecondRequestCitingTheSameHandover_IsAnsweredWithTheExistingChild`.
4. **The endeavour's first orchestration (the one whose id is the `endeavourId`) closes while its children live.** The group must still render with the closed member's counts included (§3.5, the bar never goes backwards), and the header must not vanish or name only the survivors wrongly. → Task 11.
5. **The HANDOVER entry was compacted into `sibling-outbox.archive.md` before the request arrived.** This happens after more than 90 outbox entries, or when the request sat through a usage-limit pause. The lookup must still find it, because history spans the archive (decision 13). → Task 6 (`SiblingWorld_Reader`) and Task 10 (the compaction sweep).

---

## File Structure

**Created**

```
AIOrchestratorCoreLib/Sessions/EndeavourMembers_Resolver.cs    ← derived membership (§3.2), one definition
AIOrchestratorCoreLib/Sessions/WorkingPath_Resolver.cs         ← WorkingPath ?? RepoPath, one definition
AIOrchestratorCoreLib/Sessions/WorkingPath_Comparer.cs         ← "same tree?" across slash/case/trailing-separator spellings

AIOrchestratorCoreLib/Configuration/EndeavourSettings/          ← O2
  IEndeavourSettings.cs
  EndeavourSettingsModel.cs
  EndeavourSettings_Factory.cs
  EndeavourSettings_Json.cs

AIOrchestratorCoreLib/GeneralSupervision/SpawnSiblingRequest/
  ISpawnSiblingRequest.cs
  SpawnSiblingRequestModel.cs
  SpawnSiblingRequest_Factory.cs
AIOrchestratorCoreLib/GeneralSupervision/SiblingName_Rules.cs   ← "<code> · <2-4 words>", no control characters

AIOrchestratorCoreLib/Bridge/Siblings/
  SiblingRefusals.cs                ← the archive labels, named once
  SiblingWorld.cs                   ← the facts a validation reads (plain data)
  SiblingWorld_Reader.cs            ← gathers them: store, outbox history, parked files, git worktree list
  SiblingRequest_Validator.cs       ← pure: request + world → refusal or null
  SiblingNotice_Wording.cs          ← every sentence the app says about siblings
  SiblingBirth_Step.cs              ← link parent, launch child; returns what the engine appends
  EndeavourMarkers_Sync.cs          ← .siblings
  SiblingDigestInput.cs             ← one sibling's slice of the digest (plain data)
  EndeavourDigest_Builder.cs        ← ENDEAVOUR.md text, capped (pure)
  EndeavourDigest_Reader.cs         ← gathers the inputs from disk
  EndeavourArtefacts_Step.cs        ← the tick step: markers + digest + outbox compaction
  EndeavourProgress_Reader.cs       ← the summed bar (§3.5)
  EndeavourReport_Builder.cs        ← /endeavour text
  SiblingClose_Step.cs              ← survivor notices after a close
AIOrchestratorCoreLib/Git/GitHead_Reader.cs                     ← branch name from .git/HEAD files, no process
AIOrchestratorCoreLib/Telegram/ProgressReport_Builder.cs        ← General's /progress body, moved out of the engine
AIOrchestratorCoreLib/Running/TurnSource/TurnSourceKinds.cs     ← Owner | Spoke | Sibling

kit/skills/solo/reference/siblings.md

AIOrchestratorCoreLib.Tests/TestSupport/GitWorktree_Tool.cs         ← a real temp repo + worktree, refuses to run without git
AIOrchestratorCoreLib.Tests/TestSupport/SiblingEngine_Harness.cs    ← the one shared engine fixture for Tasks 8, 9, 12, 14, 15
AIOrchestratorCoreLib.Tests/Sessions/EndeavourMembersResolverTests.cs
AIOrchestratorCoreLib.Tests/Sessions/WorkingPathResolverTests.cs
AIOrchestratorCoreLib.Tests/Sessions/WorkingPathComparerTests.cs
AIOrchestratorCoreLib.Tests/SupervisionPaths/SupervisionPathsSiblingFilesTests.cs
AIOrchestratorCoreLib.Tests/Configuration/EndeavourSettings/EndeavourSettingsJsonTests.cs
AIOrchestratorCoreLib.Tests/GeneralSupervision/SpawnSiblingRequestTests.cs
AIOrchestratorCoreLib.Tests/Launching/SiblingLaunchTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingRequestValidatorTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingWorldReaderTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingBirthStepTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/SpawnSiblingArrivalTests.cs         ← engine
AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingConfirmationTests.cs         ← engine, O1
AIOrchestratorCoreLib.Tests/Bridge/Siblings/EndeavourMarkersSyncTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/EndeavourDigestBuilderTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/EndeavourArtefactsStepTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/EndeavourProgressReaderTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/EndeavourReportBuilderTests.cs
AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingOutboxIsNeverMirroredTests.cs  ← engine, O3
AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingLifecycleTests.cs            ← engine
AIOrchestratorCoreLib.Tests/Bridge/Siblings/QuestionHoldIsPerTopicTests.cs      ← engine, O4
AIOrchestratorCoreLib.Tests/Git/GitHeadReaderTests.cs
AIOrchestratorCoreLib.Tests/Telegram/ProgressReportBuilderTests.cs
AIOrchestratorCoreLib.Tests/Running/SiblingTurnSourcesTests.cs
AIOrchestratorCoreLib.Tests/Kit/SoloIsToldAboutSiblingsTests.cs

docs/superpowers/plans/2026-09-23-sibling-solo-sessions-report.md   ← Task 18
```

**Modified**

| file | change | task |
|---|---|---|
| `Sessions/OrchestrationSession/IOrchestrationSession.cs`, `OrchestrationSessionModel.cs`, `OrchestrationSession_Factory.cs` | four nullable fields; `CreateFrom_Existing_WithEndeavourId`, `CreateFrom_Existing_WithSiblingLink`; `CreateFrom_Existing` carries the four | 1 |
| `Sessions/SessionJson_Serializer.cs` | writes and reads the four keys; absent means null | 1 |
| `Sessions/OrchestrationSessionStore/IOrchestrationSessionStore.cs`, `OrchestrationSessionStoreModel.cs` | `Set_EndeavourId`, `Set_SiblingLink` | 1 |
| `Tests/Sessions/LoadAllCounting_Store_Fake.cs` | forwards the two new store members | 1 |
| `SupervisionPaths/ISupervisionPaths.cs`, `SupervisionPathsModel.cs` | three path methods | 2 |
| `Configuration/SettingsCatalog/SettingsCatalog.cs` | the `endeavour.maxOpenSiblings` row | 3 |
| `Configuration/OrchestratorConfig/IOrchestratorConfig.cs`, `OrchestratorConfigModel.cs`, `OrchestratorConfig_Factory.cs`, `Configuration/OrchestratorConfig_Loader.cs` | `Endeavour` block; every copying factory carries it | 3 |
| `GeneralSupervision/OrchestrationRequests_Reader.cs` | `SPAWN_SIBLING_ACTION`, parse case, `Read_SpawnSiblingRequest_OrNull`, the `known` list | 4 |
| `GeneralSupervision/PendingRequests/IPendingRequests.cs`, `PendingRequestsModel.cs`, `PendingRequests_Factory.cs` | `SpawnSiblingRequests` | 4 |
| `Launching/OrchestrationLauncher/IOrchestrationLauncher.cs`, `OrchestrationLauncherModel.cs` | `Start_SiblingOrchestration`; `Respawn_Implementer` spawns in `WorkingPath_Resolver.Resolve(session)` | 5 |
| `Tests/Bridge/LaunchWitness_Fake.cs`, the launcher fake in `Tests/Running/WatchdogPrintSessionTests.cs` | the new interface member | 5 |
| `Git/GitSnapshot_Reader.cs` | `Find_WorktreePaths` becomes `public` (no behaviour change) | 6 |
| `GeneralSupervision/ParkedCloseRequest/IParkedCloseRequest.cs`, `ParkedCloseRequestModel.cs`, `ParkedCloseRequest_Factory.cs`, `ParkedCloseRequest_Reader.cs` | `ParkedCloseKinds.Sibling`; `Sibling` payload; `Create_ForSibling` | 8 |
| `GeneralSupervision/ParkedConfirmation_Planner.cs` | `Resolve_Kind` knows `spawn-sibling` | 8 |
| `GeneralSupervision/ParkedCloseRequest/CloseConfirmationPrompt_Builder.cs` | `Build_ForSibling`, labels, `Describe_*` arms | 9 |
| `Bridge/BridgeEngine/BridgeEngineModel.cs` | `Process_PendingRequests` spawning group (~5000); new `Process_SpawnSiblingRequests` (8); `Ask_OwnerToConfirmClose_Async` moot arm and prompt text (~5919, ~5992) (9); `Execute_ConfirmedClose` Sibling branch (~6361) + `Execute_SiblingBirth` (9); tick call after `Sync_PausedFlags()` (~1638) (10); `Build_ProgressReportText` General branch (~7613) (11); `/endeavour` in the command switch (~7160) (12); `Execute_Close` post-step (~5725), `Process_PromoteOrchestrationRequests` (~5390), `Switch_OrchestrationShape_Async` (~8928) (14) | 8–14 |
| `Telegram/BotCommandMenu.cs`, `Tests/Telegram/BotCommandMenuTests.cs` | `/endeavour` | 12 |
| `Running/TurnSource/ITurnSource.cs`, `TurnSourceModel.cs`, `TurnSource_Factory.cs`, `TurnSources_Resolver.cs` | `Kind`; `Create_Sibling`; sibling sources for a linked, unpaused solo | 13 |
| `Running/PrintTurn_Trigger.cs`, `Running/TurnCursor/TurnCursor_Factory.cs`, `Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` (~899) | source-aware `Is_Inbound` / `Select_Pending` | 13 |
| `Running/StatePack/StatePackInputs.cs`, `StatePackInputs_Reader.cs`, `StatePack_Builder.cs` | the digest in the fresh-start pack | 13 |
| `Tests/Bridge/PauseGatesEveryWakerScanTests.cs` | the sibling-source pause gate | 13 |
| `kit/skills/solo/SKILL.md` | new section and a boot step | 16 |
| `kit/skills/general-supervisor/SKILL.md` | one paragraph | 16 |
| `kit/skills/solo/reference/watcher.md` | the sibling half | 17 |
| `kit/hooks/watcher-behaviour-check.sh`, `kit/self-write-suppression-check.sh` | sibling cases | 17 |
| `CLAUDE.md` | decision 27 and the PAUSE waker list, **only on the owner's instruction** | 19 |

---

## Task order and dependencies

```
wave 1 (parallel, disjoint files):  T1 sessions · T2 paths · T3 setting (O2) · T4 request reader · T16 kit prose · T17 kit watcher
wave 2:                             T5 launcher (needs T1) · T10a digest builders, the pure half of T10 (needs T1, T2)
wave 3:                             T6 validator + world (needs T1, T2, T3, T4, T5's WorkingPath_Resolver)
wave 4:                             T7 birth step (needs T5, T6)
ENGINE CHAIN, strictly serial, one at a time, in this order:
                                    T8 arrival → T9 tap (O1) → T10 tick step → T11 progress → T12 /endeavour (O3) → T14 lifecycle
beside the chain:                   T13 print runner (needs T1, T2, T10's digest file; touches no engine file)
after T14 and T16:                  T15 per-topic holds (O4): an engine test + a siblings.md paragraph, no engine edit
last:                               T18 gate · T19 CLAUDE.md (owner's word only)
```

**Why the chain is serial.** Tasks 8, 9, 10, 11, 12 and 14 all edit `BridgeEngineModel.cs`. Decision 16 allows parallel writers only on disjoint files, and 16 000 lines edited by two sessions means a hand-merge of the file every task depends on. Task 10 is split: its pure builders (`EndeavourMarkers_Sync`, `EndeavourDigest_Builder`, `GitHead_Reader`, Steps 1–6) can land early as "T10a". Only its Step 7 (the tick call) waits for its turn in the chain.

---

### Task 1: Session model — four fields, and membership derived rather than stored (§3.2)

**Files:**
- Modify: `AIOrchestratorCoreLib/Sessions/OrchestrationSession/IOrchestrationSession.cs`, `OrchestrationSessionModel.cs`, `OrchestrationSession_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Sessions/SessionJson_Serializer.cs`
- Modify: `AIOrchestratorCoreLib/Sessions/OrchestrationSessionStore/IOrchestrationSessionStore.cs`, `OrchestrationSessionStoreModel.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Sessions/LoadAllCounting_Store_Fake.cs`
- Create: `AIOrchestratorCoreLib/Sessions/EndeavourMembers_Resolver.cs`
- Modify tests: `AIOrchestratorCoreLib.Tests/Sessions/SessionJsonSerializerTests.cs`, `OrchestrationSessionStoreTests.cs`
- Create test: `AIOrchestratorCoreLib.Tests/Sessions/EndeavourMembersResolverTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `IOrchestrationSession.EndeavourId : string?`, `.BornFromOrchId : string?`, `.BornFromHandover : string?` (form `"<orch>#<n>"`), `.WorkingPath : string?`
  - `OrchestrationSession_Factory.Create(…, string? endeavourId = null, string? bornFromOrchId = null, string? bornFromHandover = null, string? workingPath = null)`, with the four as **trailing optional** parameters after `paused`
  - `OrchestrationSession_Factory.CreateFrom_Existing_WithEndeavourId(IOrchestrationSession existing, string endeavourId)`
  - `OrchestrationSession_Factory.CreateFrom_Existing_WithSiblingLink(IOrchestrationSession existing, string endeavourId, string bornFromOrchId, string bornFromHandover, string workingPath)`
  - `IOrchestrationSessionStore.Set_EndeavourId(string orchId, string endeavourId)` and `.Set_SiblingLink(string orchId, string endeavourId, string bornFromOrchId, string bornFromHandover, string workingPath)`
  - `EndeavourMembers_Resolver.Resolve_All(IReadOnlyList<IOrchestrationSession> sessions, string endeavourId) : IReadOnlyList<IOrchestrationSession>` (open **and** closed, in `sessions` order)
  - `EndeavourMembers_Resolver.Resolve_OpenSiblings(IReadOnlyList<IOrchestrationSession> sessions, IOrchestrationSession self) : IReadOnlyList<IOrchestrationSession>` (open, same endeavour, never `self`; empty when `self.EndeavourId` is null)
  - `EndeavourMembers_Resolver.Count_Open(IReadOnlyList<IOrchestrationSession> sessions, string endeavourId) : int`

**The trap in this task is `CreateFrom_Existing`.** Every `Set_*` in the store rebuilds the session through it. A new field that `CreateFrom_Existing` does not carry is **silently cleared by every unrelated write**, such as the next `Set_MemberPid`. The `paused` field's own comment records the same hazard ("without it every unrelated copy would quietly wake an orchestration"). The four new fields are set once and never cleared, so they need no `wasSet` flag: `x ?? existing.X` is correct. The test below proves they survive an unrelated write.

- [ ] **Step 1: Write the failing serializer tests.** Add to `SessionJsonSerializerTests.cs`:

```csharp
/// <summary>
/// THE FOUR SIBLING FIELDS ROUND-TRIP. Written by the executor at a birth (spec §3.2) and read by
/// every tick after it, so a field that does not survive Serialize → Deserialize is a sibling that
/// forgets its endeavour on the next load.
/// </summary>
[Fact]
public void TheSiblingFields_RoundTrip()
{
    var session = OrchestrationSession_Factory.Create(
        "ai-orchestrator-8", "AIOrchestrator", @"C:\repo", DateTime.UtcNow, null, null, null, null,
        "AI-Orch · limits", null, null, [], TelegramDeliveryModes.Normal, null,
        endeavourId: "ai-orchestrator-7",
        bornFromOrchId: "ai-orchestrator-7",
        bornFromHandover: "ai-orchestrator-7#14",
        workingPath: @"C:\repo.worktrees\limits");

    var back = SessionJson_Serializer.Deserialize(SessionJson_Serializer.Serialize(session), "test");

    Assert.Equal("ai-orchestrator-7", back.EndeavourId);
    Assert.Equal("ai-orchestrator-7", back.BornFromOrchId);
    Assert.Equal("ai-orchestrator-7#14", back.BornFromHandover);
    Assert.Equal(@"C:\repo.worktrees\limits", back.WorkingPath);
}

/// <summary>
/// ABSENT MEANS NULL, and null means "today's behaviour": not linked, spawned at RepoPath. Every
/// session.json on the owner's machine was written before these keys existed. The fixture is a
/// literal copy of a pre-change file, not one built by the current serializer, which would already
/// know the keys.
/// </summary>
[Fact]
public void APreSiblingSessionJson_LoadsWithAllFourNull()
{
    const string PRE_CHANGE = """
        {"orchId":"crm-2","repoName":"CRM","repoPath":"C:\\crm","createdUtc":"2026-09-01T10:00:00.0000000Z",
         "members":[],"telegramMode":"Normal","ownerPresence":"Remote","awaitingTest":false,"done":false,"paused":false}
        """;

    var session = SessionJson_Serializer.Deserialize(PRE_CHANGE, "fixture");

    Assert.Null(session.EndeavourId);
    Assert.Null(session.BornFromOrchId);
    Assert.Null(session.BornFromHandover);
    Assert.Null(session.WorkingPath);
}
```

- [ ] **Step 2: Write the failing store and membership tests.** Add to `OrchestrationSessionStoreTests.cs` (use its existing temp-root fixture):

```csharp
/// <summary>
/// AN UNRELATED WRITE MUST NOT UNLINK A SIBLING. Every Set_* rebuilds the session through
/// CreateFrom_Existing, and a field it forgets to carry is cleared by the next pid write, which
/// happens on every spawn.
/// </summary>
[Fact]
public void TheSiblingLink_SurvivesAnUnrelatedWrite()
{
    _store.Create_Orchestration("ai-orchestrator-8", "AIOrchestrator", _repo);
    _store.Set_SiblingLink("ai-orchestrator-8", "ai-orchestrator-7", "ai-orchestrator-7", "ai-orchestrator-7#14", _worktree);
    _store.Set_Paused("ai-orchestrator-8", true);
    _store.Set_DisplayName("ai-orchestrator-8", "AI-Orch · limits");

    var session = _store.Get_Session("ai-orchestrator-8");

    Assert.Equal("ai-orchestrator-7", session.EndeavourId);
    Assert.Equal("ai-orchestrator-7#14", session.BornFromHandover);
    Assert.Equal(_worktree, session.WorkingPath);
}

[Fact]
public void Set_EndeavourId_StampsTheParent_AndLeavesTheOtherThreeNull()
```

Create `EndeavourMembersResolverTests.cs`. Build the sessions with `OrchestrationSession_Factory.Create` in memory (no disk):

```csharp
[Fact] public void OpenSiblings_ExcludeSelf()
[Fact] public void OpenSiblings_IncludeOnlyTheSameEndeavour()        // a second endeavour's members are not listed
[Fact] public void OpenSiblings_ExcludeClosedMembers()               // ClosedUtc set → gone from the list
[Fact] public void OpenSiblings_OfAnUnlinkedSession_IsEmpty()        // EndeavourId null → [] even if others are linked
[Fact] public void All_IncludesClosedMembers_InStoreOrder()          // §3.5: a closed sibling stays in the sum
[Fact] public void CountOpen_CountsTheRequesterToo()                 // O2 counts members, requester included
```

- [ ] **Step 3: Run the tests and watch them fail to compile.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SessionJsonSerializerTests|FullyQualifiedName~OrchestrationSessionStoreTests|FullyQualifiedName~EndeavourMembersResolverTests"
```

Expected: a build error naming `EndeavourId` / `Set_SiblingLink` / `EndeavourMembers_Resolver`.

- [ ] **Step 4: Implement.**
  - `IOrchestrationSession`: four get-only properties, each with an XML doc giving its meaning, its writer and "absent means null" (copy the meanings from spec §3.2's table). `EndeavourId`'s doc says **why its value is the first orchestration's id**: stable, readable, and never re-allocated, because `OrchId_Allocator.Allocate_NextOrchId` only counts upwards. It also says **why there is no `siblings[]` list** (spec §3.2's four reasons, in one paragraph).
  - `OrchestrationSessionModel`: four constructor parameters at the end, and four properties.
  - `OrchestrationSession_Factory.Create` (the long overload): four trailing optional parameters, passed to the model. `CreateFrom_Existing`: four optional parameters `string? endeavourId = null, …`, each resolved as `endeavourId ?? existing.EndeavourId`. Add the two `CreateFrom_Existing_With*` wrappers.
  - `SessionJson_Serializer.Serialize`: add `["endeavourId"]`, `["bornFromOrchId"]`, `["bornFromHandover"]`, `["workingPath"]` after `paused`. `Deserialize`: read each with `Get_String_OrNull`, with one comment: absent in every session written before 2026-09-23, and null is the only safe reading (not linked, spawned at `RepoPath`).
  - The store: `Set_EndeavourId` and `Set_SiblingLink`, written exactly like `Set_Paused` (the same lock, the same `Save(…)`). `Set_SiblingLink` is **one save** for all four child fields, so a crash cannot leave a child half-linked.
  - `LoadAllCounting_Store_Fake`: forward both calls to `inner`.
  - `EndeavourMembers_Resolver`: a static class with the three methods above. It takes the **list** rather than the store, so a caller that already has `Load_All()` for this tick (the engine's `Sessions_ThisTick()`) does not trigger a second read. The doc cites §3.2: "Membership is derived, never stored as a list."

- [ ] **Step 5: Run the same filter until it passes.** Expected: all green, and no test outside the three classes changes.

- [ ] **Step 6: Commit.**

```bash
git add AIOrchestratorCoreLib/Sessions/OrchestrationSession/IOrchestrationSession.cs AIOrchestratorCoreLib/Sessions/OrchestrationSession/OrchestrationSessionModel.cs AIOrchestratorCoreLib/Sessions/OrchestrationSession/OrchestrationSession_Factory.cs AIOrchestratorCoreLib/Sessions/SessionJson_Serializer.cs AIOrchestratorCoreLib/Sessions/OrchestrationSessionStore/IOrchestrationSessionStore.cs AIOrchestratorCoreLib/Sessions/OrchestrationSessionStore/OrchestrationSessionStoreModel.cs AIOrchestratorCoreLib/Sessions/EndeavourMembers_Resolver.cs AIOrchestratorCoreLib.Tests/Sessions/LoadAllCounting_Store_Fake.cs AIOrchestratorCoreLib.Tests/Sessions/SessionJsonSerializerTests.cs AIOrchestratorCoreLib.Tests/Sessions/OrchestrationSessionStoreTests.cs AIOrchestratorCoreLib.Tests/Sessions/EndeavourMembersResolverTests.cs
git commit -F "$MSG"   # feat(sessions): link orchestrations into an endeavour — four nullable fields, membership derived
```

---

### Task 2: Paths — the outbox, the sibling list, the digest (§3.3, §3.4)

**Files:**
- Modify: `AIOrchestratorCoreLib/SupervisionPaths/ISupervisionPaths.cs`, `SupervisionPathsModel.cs`
- Create test: `AIOrchestratorCoreLib.Tests/SupervisionPaths/SupervisionPathsSiblingFilesTests.cs`

**Interfaces:**
- Produces: `ISupervisionPaths.Get_SiblingOutboxFile(string orchId)` → `<root>/<orch>/sibling-outbox.md`; `Get_SiblingsListFile(string orchId)` → `<root>/<orch>/.siblings`; `Get_EndeavourDigestFile(string orchId)` → `<root>/<orch>/ENDEAVOUR.md`

- [ ] **Step 1: Write the failing test.**

```csharp
[Fact]
public void TheThreeSiblingFiles_LiveInTheOrchestrationFolderRoot()
{
    var paths = SupervisionPaths_Factory.Create(_root);

    Assert.Equal(Path.Combine(paths.Get_OrchestrationFolder("a-1"), "sibling-outbox.md"), paths.Get_SiblingOutboxFile("a-1"));
    Assert.Equal(Path.Combine(paths.Get_OrchestrationFolder("a-1"), ".siblings"), paths.Get_SiblingsListFile("a-1"));
    Assert.Equal(Path.Combine(paths.Get_OrchestrationFolder("a-1"), "ENDEAVOUR.md"), paths.Get_EndeavourDigestFile("a-1"));
}

/// <summary>
/// THE OUTBOX IS NOT A CHANNEL THE TAILER KNOWS, and that is the O3 answer obtained with no code
/// (spec §3.3). ChannelDiscovery enumerates owner-channel.md and imp-*/rev-* spokes only. If a
/// later change made it glob every *.md in the folder, sibling traffic would start ringing the
/// owner's phone. Task 12 pins the same fact at the engine.
/// </summary>
[Fact]
public void AnOutboxWithEntries_IsNotDiscoveredAsAChannel()
```

(For the second test: create the orchestration folder, write `owner-channel.md` and a `sibling-outbox.md` holding one `## [1] FROM solo — ASK x` entry, then assert that `ChannelDiscovery.Find_ChannelFiles(paths)` contains no path ending `sibling-outbox.md`.)

- [ ] **Step 2: Run the test and see a compile failure.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SupervisionPathsSiblingFilesTests"
```

- [ ] **Step 3: Implement** the three methods beside `Get_PlanFile`, in the same shape: `Path.Combine(Get_OrchestrationFolder(orchId), "…")`. Each gets a one-line doc naming who writes the file. For the outbox, add "the sibling itself, through channel-append.sh, and nobody else". For the other two, add "the app, derived, never authored".

- [ ] **Step 4: Run the same filter until it passes.**

- [ ] **Step 5: Commit** `feat(paths): the sibling outbox, the .siblings list and ENDEAVOUR.md`, staging the three files.

---

### Task 3: The cap as data — `endeavour.maxOpenSiblings` (O2, §9)

**This task is O2.** A different answer changes the default below and nothing else.

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/EndeavourSettings/IEndeavourSettings.cs`, `EndeavourSettingsModel.cs`, `EndeavourSettings_Factory.cs`, `EndeavourSettings_Json.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs` (a row in `Build_Kernel`)
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig/IOrchestratorConfig.cs`, `OrchestratorConfigModel.cs`, `OrchestratorConfig_Factory.cs`, `AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs`
- Create test: `AIOrchestratorCoreLib.Tests/Configuration/EndeavourSettings/EndeavourSettingsJsonTests.cs`

**Interfaces:**
- Consumes: `SettingsCatalog.Find_OrNull(path)`, `SettingDefinition_Factory.Create_Int(…)`, `Settings_Resolver.Resolve_Long(definition, presetTree, configTree, session: null)`. The shape to copy is `Configuration/EffortSettings/EffortSettings_Json.cs`.
- Produces:
  - `SettingsCatalog.ENDEAVOUR_MAX_OPEN_SIBLINGS_PATH = "endeavour.maxOpenSiblings"`, shipped default **3**, minimum 1, maximum 10, `SettingScopes.Machine`, `SettingCategories.Kernel`, `RestartKinds.None`
  - `IEndeavourSettings.MaxOpenSiblings : int`, which counts **open members of one endeavour, the requester included**
  - `EndeavourSettings_Factory.Create(int maxOpenSiblings)`, `.Create_Default()`
  - `EndeavourSettings_Json.Parse(JsonObject? configRoot, JsonObject? presetTree) : IEndeavourSettings`
  - `IOrchestratorConfig.Endeavour : IEndeavourSettings`

- [ ] **Step 1: Write the failing tests.** Use the temp `ISupervisionPaths` fixture that `PerRoleModelDefaultsTests` uses, and write real JSON to `ConfigFile`:

```csharp
[Fact] public void WithNoConfigAtAll_TheCapIsThree()
[Fact] public void AValueInConfigJson_IsRead()                      // {"endeavour":{"maxOpenSiblings":2}} → 2
[Fact] public void AnOutOfRangeValue_FallsToTheDefault_AndDoesNotThrow()   // 0 and 11 → 3; the loader runs every tick with no try/catch above it
[Fact] public void AWrongType_FallsToTheDefault_AndDoesNotThrow()   // "three" → 3

/// <summary>
/// A COPYING FACTORY THAT FORGETS THE BLOCK RESETS IT. OrchestratorConfig_Factory.Create_WithStatusScreenshots
/// rebuilds a whole config from a source. If it does not pass Endeavour through, toggling
/// screenshots silently puts the cap back to the default.
/// </summary>
[Fact] public void CreateWithStatusScreenshots_KeepsTheEndeavourBlock()
```

- [ ] **Step 2: Run the tests and see a compile failure.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~EndeavourSettingsJsonTests"
```

- [ ] **Step 3: Implement.**
  - Add the catalogue row to `Build_Kernel()` with `Create_Int`. Its description carries the cost sentence from §9 O2: "Each extra sibling adds a topic, a session, and another block in every sibling's digest. Counts open members of one endeavour, the requester included."
  - The triple follows the shape above.
  - `_Json.Parse` resolves through the catalogue, like `EffortSettings_Json`. It is **READ AND NEVER WRITTEN**: no `Write` method, for the reason `EffortSettings_Json` gives (a block written back becomes a stated value).
  - First confirm whether `Settings_Resolver` already applies `minimum`/`maximum`: read `Settings_Resolver.Resolve` and `SettingValidators`. If it does not, `Parse` clamps by returning the default for out-of-range values. Either way the test decides.
  - `IOrchestratorConfig.Endeavour`: `OrchestratorConfig_Factory.Create` (both overloads) gains a trailing optional `IEndeavourSettings? endeavour = null` → `endeavour ?? EndeavourSettings_Factory.Create_Default()`, and `Create_WithStatusScreenshots` passes `source.Endeavour`.
  - The loader passes `EndeavourSettings_Json.Parse(configRoot, preset)` on the preset rung, beside `EffortSettings_Json.Parse`.
  - **Neither `kit/presets/classic.json` nor `quiet.json` states the key.** The shipped default is the answer.

- [ ] **Step 4: Run the filter until it passes.** Then run `--filter "FullyQualifiedName~SettingsCatalogTests|FullyQualifiedName~PresetProbeTests|FullyQualifiedName~OrchestratorConfig"`. A catalogue test that enumerates categories or rows may need the new row. If it does, extend the test's expectation rather than exempting the row.

- [ ] **Step 5: Commit** `feat(config): endeavour.maxOpenSiblings — the sibling cap is data, default 3 (O2)`.

---

### Task 4: The request — `spawn-sibling` (§4.1, §4.2 reader half)

**Files:**
- Create: `AIOrchestratorCoreLib/GeneralSupervision/SpawnSiblingRequest/ISpawnSiblingRequest.cs`, `SpawnSiblingRequestModel.cs`, `SpawnSiblingRequest_Factory.cs`
- Create: `AIOrchestratorCoreLib/GeneralSupervision/SiblingName_Rules.cs`
- Modify: `AIOrchestratorCoreLib/GeneralSupervision/OrchestrationRequests_Reader.cs`
- Modify: `AIOrchestratorCoreLib/GeneralSupervision/PendingRequests/IPendingRequests.cs`, `PendingRequestsModel.cs`, `PendingRequests_Factory.cs`
- Create test: `AIOrchestratorCoreLib.Tests/GeneralSupervision/SpawnSiblingRequestTests.cs`
- Modify test: `AIOrchestratorCoreLib.Tests/GeneralSupervision/OrchestrationRequestsReaderTests.cs` (the `known` list)

**Interfaces:**
- Produces:
  - `OrchestrationRequests_Reader.SPAWN_SIBLING_ACTION = "spawn-sibling"`, `.SIBLING_JOB_MAX_CHARS = 200`
  - `ISpawnSiblingRequest { string OrchId; string Name; string Job; int HandoverIndex; string WorktreePath; string Reason; string SourceFilePath; }`
  - `SpawnSiblingRequest_Factory.Create(string orchId, string name, string job, int handoverIndex, string worktreePath, string reason, string sourceFilePath)`, which throws on an empty `orchId` (the `PromoteOrchestrationRequest_Factory` precedent)
  - `IPendingRequests.SpawnSiblingRequests : IReadOnlyList<ISpawnSiblingRequest>`
  - `OrchestrationRequests_Reader.Read_SpawnSiblingRequest_OrNull(string filePath)`, which re-reads one parked file through the same parse
  - `SiblingName_Rules.Describe_Refusal_OrNull(string name) : string?`

**Plumbing to be aware of:** `Try_ParseInto_OrReason` takes **one list per request type**. Adding `List<ISpawnSiblingRequest> spawnSiblingRequests` means editing its signature and all four callers: `Read_Pending`, `Read_CloseOrchestrationRequest_OrNull`, `Read_CloseImplementerRequest_OrNull` and `Read_PromoteOrchestrationRequest_OrNull`. That is mechanical. Do it, and do not refactor the pattern (decision 22).

- [ ] **Step 1: Write the failing tests.** Use the `PromoteOrchestrationRequestTests` style. Write JSON files into a temp `RequestsFolder` and call `Read_Pending`:

```csharp
const string WELL_FORMED = """
    {"action":"spawn-sibling","orchId":"ai-orchestrator-7","name":"AI-Orch · limits",
     "job":"Rework the usage-limit pause so a restored pause can be lifted per window",
     "handover":14,"worktree":"C:/Users/x/repo.worktrees/limits",
     "reason":"two jobs the owner wants to steer separately; disjoint files"}
    """;

[Fact] public void AWellFormedRequest_IsRead()                 // every field lands; HandoverIndex == 14
[Theory]
[InlineData("orchId")] [InlineData("name")] [InlineData("job")]
[InlineData("handover")] [InlineData("worktree")] [InlineData("reason")]
public void EachMissingField_IsRejectedWithItsName(string field)   // MalformedRequests[0].Reason contains the field; 'reason' → MISSING_REASON_MESSAGE
[Fact] public void AHandoverThatIsNotAPositiveInteger_IsRejected()  // "14", 0, -3, 14.5 → rejected, reason names 'handover'
[Fact] public void ANameWithoutTheMiddleDot_IsRejected()           // "limits rework"
[Fact] public void ANameWithATabOrANewline_IsRejected()            // Review Focus 2 — ".siblings" is tab-separated and bash-read
[Fact] public void ANameWithMoreThanFourWordsAfterTheCode_IsRejected()
[Fact] public void AJobOver200Characters_IsRejected()
[Fact] public void AParkedFile_IsReReadByPath()                    // Read_SpawnSiblingRequest_OrNull
[Fact] public void AModelOrEffortField_IsIgnored_NotHonoured()     // §4.1: absent on purpose; the child copies the parent's overrides
```

In `OrchestrationRequestsReaderTests`, extend the unknown-action test so the message lists `spawn-sibling`. That test exists for this exact slip: the `default:` comment records `promote-orchestration` going missing from the list.

- [ ] **Step 2: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SpawnSiblingRequestTests|FullyQualifiedName~OrchestrationRequestsReaderTests"
```

- [ ] **Step 3: Implement.**
  - `SiblingName_Rules.Describe_Refusal_OrNull`: returns null for a legal name. Otherwise it returns one sentence naming the rule broken:
    - any char `< 0x20` is refused;
    - the name must contain exactly one `" · "`;
    - the code part must be 1–12 characters with no spaces;
    - the part after the dot must be 1–4 space-separated words (the spec says 2-4 words, and one is allowed because `solo/SKILL.md`'s own examples include one-word names; state that in the doc);
    - the total must be at most 64 characters.
    Its doc cites `solo/SKILL.md` "EVERY TOPIC NAME STARTS WITH THE PLATFORM CODE", and says **the code is not checked against the parent's** ("A SUB-PRODUCT KEEPS ITS OWN CODE", §4.1).
  - The parse case copies the `PROMOTE_ORCHESTRATION_ACTION` case's comment on **JSON only; facts about the world are the executor's**. Read `handover` with `root["handover"] is JsonValue v && v.TryGetValue<int>(out var n) && n > 0`, because a string `"14"` is refused rather than coerced. Unknown fields such as `model` are ignored, and a comment says why (§4.1).
  - Add `SPAWN_SIBLING_ACTION` to the `known` array in `default:`.

- [ ] **Step 4: Run the filter until it passes.**

- [ ] **Step 5: Commit** `feat(requests): spawn-sibling — the reader checks JSON, the executor checks the world`.

---

### Task 5: Launcher — `Start_SiblingOrchestration`, and every spawn in the sibling's own tree (§4.3 step 3, §6, §7.1)

**Files:**
- Create: `AIOrchestratorCoreLib/Sessions/WorkingPath_Resolver.cs`
- Modify: `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/IOrchestrationLauncher.cs`, `OrchestrationLauncherModel.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/LaunchWitness_Fake.cs`, and the `IOrchestrationLauncher` fake inside `AIOrchestratorCoreLib.Tests/Running/WatchdogPrintSessionTests.cs`
- Create tests: `AIOrchestratorCoreLib.Tests/Sessions/WorkingPathResolverTests.cs`, `AIOrchestratorCoreLib.Tests/Launching/SiblingLaunchTests.cs`

**Interfaces:**
- Consumes: Task 1's `Set_SiblingLink`, `EndeavourId`, `WorkingPath`.
- Produces:
  - `WorkingPath_Resolver.Resolve(IOrchestrationSession session) : string` = `session.WorkingPath ?? session.RepoPath`
  - `IOrchestrationLauncher.Start_SiblingOrchestration(string parentOrchId, string displayName, string workingPath, string bornFromHandover) : IOrchestrationSession`

**Order inside `Start_SiblingOrchestration` is the whole point (§4.3 step 3):**

1. validate `workingPath` exists;
2. `Allocate_NextOrchId(_paths, parent.RepoName)`;
3. `_store.Create_Orchestration(orchId, parent.RepoName, parent.RepoPath)`;
4. `_store.Set_SiblingLink(orchId, parent.EndeavourId ?? parent.OrchId, parent.OrchId, bornFromHandover, workingPath)`;
5. `_store.Set_DisplayName(orchId, displayName)`;
6. copy `parent.ImplementerModelOverride` and `parent.ImplementerEffortOverride` through their setters;
7. `PlanSeed_Writer.Ensure_Exists`;
8. **only then** `Add_Member(orchId, MemberKinds.Solo)`.

The terminal title is built at spawn (`SessionWindowTitle_Builder`) and the cwd is read at spawn, so a field stamped after `Add_Member` is a field the first process never saw.

**Stamping the parent's own `endeavourId` is NOT this method's job.** It belongs to the birth step (Task 7). The launcher starts sessions and does not edit other orchestrations.

- [ ] **Step 1: Write the failing tests.** `WorkingPathResolverTests`: null → `RepoPath`; set → `WorkingPath`. `SiblingLaunchTests` uses the `OrchestrationLauncherTests` fixture with `RecordingSpawner_Fake`. Make the parent with `Start_BasicOrchestration`, and create the worktree folder as a plain directory, because the launcher does not check git (Task 6 does).

```csharp
/// <summary>THE FIRST PROCESS SEES THE LINK. Asserted on what the spawner was HANDED, not on session.json afterwards.</summary>
[Fact] public void TheChildIsSpawnedInItsWorktree_WithItsNameOnTheWindow()
       // spawner.Launches.Last(): WorkingDirectory == worktree; DisplayName == "AI-Orch · limits"
[Fact] public void TheChildCarriesTheLink_AndTheParentsDials()
       // EndeavourId == parent id; BornFromOrchId == parent id; BornFromHandover == "p#14"; ImplementerModelOverride/EffortOverride copied
[Fact] public void AGrandchild_JoinsTheFirstEndeavour_NotItsParentsId()
       // parent.EndeavourId = "root-1" → child.EndeavourId == "root-1"
[Fact] public void AMissingWorktree_Throws_AndCreatesNothing()
       // no new session.json, no spawn
[Fact] public void AWatchdogRespawn_UsesTheWorktree()        // Respawn_Implementer(child, "solo-1") → WorkingDirectory == worktree
[Fact] public void AModelDialRespawn_UsesTheWorktree()       // set override, Respawn_Implementer again → same
[Fact] public void AnUnlinkedSolo_StillSpawnsAtRepoPath()    // today's behaviour, pinned
[Fact] public void TheResumeIdPath_IsUnchanged()             // write solo-1/.usage.json with a transcript that exists → the launch carries the same ResumeSessionId it did before this task
```

- [ ] **Step 2: Run the tests and see a compile failure.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SiblingLaunchTests|FullyQualifiedName~WorkingPathResolverTests"
```

- [ ] **Step 3: Implement.**
  - In `Respawn_Implementer`, change **one expression**: `SessionLaunch_Factory.Create(role, orchId, memberId, session.RepoPath, …)` → `WorkingPath_Resolver.Resolve(session)`. Add a comment: the one path every spawn and respawn takes (watchdog, `/model`, `/effort`, app restart), so a sibling's cwd is stable, and **resume depends on that**. Claude Code keys transcripts by working directory (§7.1).
  - `Respawn_Supervisor` and `Respawn_Communicator` keep `RepoPath`. A linked orchestration never has a supervisor (§7.6), and the comment says so.
  - Add `Start_SiblingOrchestration` as ordered above, logging `Log_Info(orchId, "SIBLING orchestration created … of '<parent>' in '<worktree>'")`.
  - Add the member to both fakes. `LaunchWitness_Fake` records the call like its neighbours. The watchdog fake throws `NotSupportedException`, because that test never starts a sibling.

- [ ] **Step 4: Run the filter until it passes.** Then run the neighbours: `--filter "FullyQualifiedName~OrchestrationLauncherTests|FullyQualifiedName~PromoteToFullCrewTests|FullyQualifiedName~BasicOrchestrationTests|FullyQualifiedName~WatchdogPrintSessionTests"`.

- [ ] **Step 5: Commit** `feat(launch): start a sibling in its own worktree — every spawn and respawn uses WorkingPath`.

---

### Task 6: Validation — the world, and the refusal table (§4.2)

**Files:**
- Create: `AIOrchestratorCoreLib/Sessions/WorkingPath_Comparer.cs`
- Create: `AIOrchestratorCoreLib/Bridge/Siblings/SiblingRefusals.cs`, `SiblingWorld.cs`, `SiblingWorld_Reader.cs`, `SiblingRequest_Validator.cs`, `SiblingNotice_Wording.cs`
- Modify: `AIOrchestratorCoreLib/Git/GitSnapshot_Reader.cs`, making `Find_WorktreePaths` `public` (body unchanged)
- Create: `AIOrchestratorCoreLib.Tests/TestSupport/GitWorktree_Tool.cs`
- Create tests: `AIOrchestratorCoreLib.Tests/Sessions/WorkingPathComparerTests.cs`, `AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingRequestValidatorTests.cs`, `SiblingWorldReaderTests.cs`

**Interfaces:**
- Consumes: Task 1 (`EndeavourMembers_Resolver`, the four fields), Task 2 (`Get_SiblingOutboxFile`), Task 3 (`Endeavour.MaxOpenSiblings`), Task 4 (`ISpawnSiblingRequest`, `Read_SpawnSiblingRequest_OrNull`), Task 5 (`WorkingPath_Resolver`), `HandoverEntry_Detector.HANDOVER_MARKER`, `MemberState_Resolver.Contains_Marker`, `ChannelHistory_Counter.Read_Entries`, `CloseConfirmation_Parking.Find_Parked`, `OrchestrationShape.Is_BasicOrchestration`.
- Produces:
  - `SiblingRefusals`: constants `UNSPAWNABLE = "unspawnable"`, `NOT_A_SOLO = "not-a-solo"`, `HANDOVER_ALREADY_USED = "handover-already-used"`, `NO_HANDOVER_ENTRY = "no-handover-entry"`, `AT_CAP = "at-cap"`, `WORKTREE_MISSING = "worktree-missing"`, `WORKTREE_NOT_OF_REPO = "worktree-not-of-repo"`, `WORKTREE_SHARED = "worktree-shared"`, `NAME_TAKEN = "name-taken"`, `LINKED_ORCHESTRATION = "linked-orchestration"` (the last is used by Task 14)
  - `WorkingPath_Comparer.Are_Same(string a, string b) : bool`
  - `SiblingWorld` (plain data, primary-constructor class): `IOrchestrationSession? Requester`, `IReadOnlyList<IOrchestrationSession> Sessions`, `IReadOnlyList<IChannelEntry> RequesterOutboxHistory`, `IReadOnlyList<ISpawnSiblingRequest> OtherParkedSiblingRequests`, `IReadOnlyList<string> RepoWorktreePaths`, `bool WorktreeExists`, `int MaxOpenMembers`
  - `SiblingWorld_Reader.Read(ISupervisionPaths paths, IOrchestrationSessionStore store, IOrchestratorConfig config, ISpawnSiblingRequest request, string? ownParkedPath) : SiblingWorld`
  - `SiblingRequest_Validator.Decide_Refusal_OrNull(ISpawnSiblingRequest request, SiblingWorld world) : (string Label, string Subject, string Body)?`
  - `SiblingRequest_Validator.Format_HandoverKey(string orchId, int index) : string` → `"<orch>#<n>"` (the one spelling of `bornFromHandover`)
  - `SiblingNotice_Wording.Describe_Held(ISpawnSiblingRequest request) : (string Subject, string Body)` and the refusal bodies, one method per label

**The ORDER of the checks differs from spec §4.2's table in one place, deliberately.** `handover-already-used` is checked **before** `at-cap` and before `no-handover-entry`. After a successful birth, the endeavour has one more open member. A retry of the same file would then be answered `at-cap` ("close a sibling"), which is misleading, when the true answer is the idempotent "already started as `<id>`" (§4.4). The order is:

`unspawnable` → `not-a-solo` → `handover-already-used` → `no-handover-entry` → `at-cap` → `worktree-missing` → `worktree-not-of-repo` → `worktree-shared` → `name-taken`

Record this in the validator's doc and in the self-review.

- [ ] **Step 1: Write the failing comparer tests (Review Focus 1).**

```csharp
[Theory]
[InlineData(@"C:\Users\x\repo.worktrees\limits", "C:/Users/x/repo.worktrees/limits")]   // git's spelling
[InlineData(@"C:\Users\x\repo.worktrees\limits\", @"C:\Users\x\repo.worktrees\limits")] // trailing separator
[InlineData(@"c:\users\x\REPO.worktrees\limits", @"C:\Users\x\repo.worktrees\limits")]  // case — OrdinalIgnoreCase, the GitSnapshot_Reader precedent
public void TheSameTree_SpelledTwoWays_IsTheSame(string a, string b)

[Fact] public void AParentFolder_IsNotTheSameTree()        // repo vs repo.worktrees\limits
[Fact] public void AnUnparseablePath_IsNotTheSame_AndDoesNotThrow()   // "C:\\bad\0path" → false
```

- [ ] **Step 2: Write the failing validator tests.** They are pure: build `SiblingWorld` in memory. Write one test per label, plus the order:

```csharp
[Fact] public void AMissingRequester_IsUnspawnable()
[Fact] public void AClosedRequester_IsUnspawnable()
[Fact] public void ACrew_IsNotASolo()                                   // SupervisorSpawnedUtc set
[Fact] public void AHandoverCitedByALiveSibling_IsAlreadyUsed_AndNamesIt()   // body contains "already started as ai-orchestrator-8"
[Fact] public void AHandoverCitedByAnotherParkedRequest_IsAlreadyUsed_AndSaysHeld()   // body contains "already held — do not re-drop"
[Fact] public void AHandoverCitedByAClosedSibling_IsStillAlreadyUsed()  // one HANDOVER births at most one sibling, ever (§4.4)
[Fact] public void NoEntryAtThatIndex_IsNoHandoverEntry()
[Fact] public void AnEntryAtThatIndexByTheOwner_IsNoHandoverEntry()     // author gate: FROM owner quoting "HANDOVER" does not count
[Fact] public void AnEntryWithoutTheMarker_IsNoHandoverEntry()
[Fact] public void AtTheCap_IsAtCap_AndNamesTheOpenSiblings()           // MaxOpenMembers 3, three open → refused
[Fact] public void OneBelowTheCap_IsAllowed()
[Fact] public void AMissingWorktree_IsWorktreeMissing()
[Fact] public void AWorktreeNotListedByGit_IsNotOfRepo()
[Fact] public void TheRequestersOwnRepoPath_IsShared()
[Fact] public void AnOpenSiblingsWorktree_IsShared()
[Fact] public void AClosedSiblingsWorktree_IsNotShared()                // a finished job's tree may be reused
[Fact] public void AnOpenSiblingsName_IsTaken()
[Fact] public void AValidRequest_HasNoRefusal()

/// <summary>THE IDEMPOTENT ANSWER BEATS THE CAP (see the ordering note): a retry after a birth that filled the endeavour.</summary>
[Fact] public void ARetryAfterTheBirthThatFilledTheCap_IsAlreadyUsed_NotAtCap()
```

- [ ] **Step 3: Write the failing world-reader tests (Review Focus 5).** `SiblingWorldReaderTests`:

```csharp
/// <summary>
/// THE HANDOVER ENTRY MAY HAVE BEEN COMPACTED. The request can wait on disk through a usage-limit
/// pause (§4.2), and a busy outbox compacts above 90 entries, moving [14] into
/// sibling-outbox.archive.md. Reading the live file alone would refuse a perfectly good request as
/// no-handover-entry. History goes through ChannelHistory_Counter (decision 13).
/// </summary>
[Fact] public void TheHandoverEntry_IsFound_AfterCompaction()
       // write 100 FROM solo entries with [14] carrying HANDOVER; Channel_Compactor.Compact_IfNeeded(outbox); Read → history contains index 14

[Fact] public void TheWorktreeList_ComesFromGit()          // GitWorktree_Tool: real temp repo + one worktree → RepoWorktreePaths contains it
[Fact] public void OwnParkedFile_IsNotCountedAsAnotherParkedRequest()   // ownParkedPath excluded, or a parked request refuses itself at tap time
```

`GitWorktree_Tool.Create_RepoWithWorktree(string root, string worktreeName) : (string RepoPath, string WorktreePath)`:
- runs `git init`, `git -c user.email=t@t -c user.name=t commit --allow-empty -m init` and `git worktree add ../<name> -b <name>` via `ProcessStartInfo`;
- **throws `"git not found on PATH — REFUSING to run a worktree test that would assert about nothing"`** if the first command cannot start (decision 20).

- [ ] **Step 4: Run all three classes and watch them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~WorkingPathComparerTests|FullyQualifiedName~SiblingRequestValidatorTests|FullyQualifiedName~SiblingWorldReaderTests"
```

- [ ] **Step 5: Implement.**
  - `WorkingPath_Comparer.Are_Same`: `Path.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar))` → `Path.TrimEndingDirectorySeparator` → `string.Equals(…, OrdinalIgnoreCase)`, inside try/catch → false. The doc says why OrdinalIgnoreCase: `GitSnapshot_Reader.Read_RepoAndWorktrees` already compares worktrees this way, and two rules for one comparison would disagree.
  - `SiblingWorld_Reader.Read`:
    - `store.Load_All()`;
    - `ChannelHistory_Counter.Read_Entries(paths.Get_SiblingOutboxFile(request.OrchId))`, which is empty when the file is missing;
    - `CloseConfirmation_Parking.Find_Parked(paths)` → `Read_SpawnSiblingRequest_OrNull`, skipping `ownParkedPath` compared with `WorkingPath_Comparer.Are_Same`;
    - `GitSnapshot_Reader.Find_WorktreePaths(requester.RepoPath)`, **only when the requester exists**, because that call starts a process;
    - `Directory.Exists(request.WorktreePath)`;
    - `config.Endeavour.MaxOpenSiblings`.
  - `SiblingRequest_Validator.Decide_Refusal_OrNull` follows the order above.
    - The handover lookup: the entry in `RequesterOutboxHistory` whose `Index == request.HandoverIndex`, `Author == ChannelAuthors.Solo`, and `MemberState_Resolver.Contains_Marker(entry, HandoverEntry_Detector.HANDOVER_MARKER)`. Reuse the existing matcher, never a second one (its doc gives the position rules).
    - `handover-already-used`: any session in `Sessions` whose `BornFromHandover == Format_HandoverKey(request.OrchId, request.HandoverIndex)`, open or closed, **or** any `OtherParkedSiblingRequests` with the same `OrchId` and `HandoverIndex`.
    - The cap uses `EndeavourMembers_Resolver.Count_Open(Sessions, requester.EndeavourId ?? requester.OrchId)`. An unlinked requester counts as 1.
  - `SiblingNotice_Wording` holds every sentence. The refusal bodies copy spec §4.2's "What the requester is told" column and end with "The owner has NOT been asked and nothing was changed.", the promote precedent.

- [ ] **Step 6: Run the filter until it passes.** Also run `--filter "FullyQualifiedName~GitSnapshotReaderTests"`: the visibility change must not move it.

- [ ] **Step 7: Commit** `feat(siblings): the refusal table — pure validation over a gathered world`.

---

### Task 7: The birth step — link, launch, and the words the engine appends (§4.3 steps 2–5)

**Files:**
- Create: `AIOrchestratorCoreLib/Bridge/Siblings/SiblingBirth_Step.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/Siblings/SiblingNotice_Wording.cs`
- Create: `AIOrchestratorCoreLib.Tests/TestSupport/SiblingEngine_Harness.cs` (used from Task 8 on; created here so it is reviewed once, before any engine task needs it)
- Create test: `AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingBirthStepTests.cs`

**Interfaces:**
- Consumes: Task 5's `Start_SiblingOrchestration`, Task 1's `Set_EndeavourId`, Task 6's `Format_HandoverKey`.
- Produces:
  - `SiblingBirth_Step.Execute(IOrchestrationSessionStore store, IOrchestrationLauncher launcher, ISupervisionPaths paths, ISpawnSiblingRequest request) : (IOrchestrationSession Child, (string Subject, string Body) BirthNote, (string Subject, string Body) ParentNotice, (string Subject, string Body) GeneralLine)`. It throws on launch failure; the engine catches.
  - `SiblingNotice_Wording.Describe_BirthNote(string parentName, string job, string parentOutboxPath, int handoverIndex)`: subject `🔗 Sibling of <parentName>`, body `job: <job>\nWrite here about this job only.\nYour brief: <outbox> entry [<n>]`
  - `SiblingNotice_Wording.Describe_ParentStarted(string childId, string childName)`: subject `sibling '<childId>' started — <childName> (its own topic)`
  - `SiblingEngine_Harness`, a disposable fixture:
    - temp root, repo, config and secrets files written as `TheInboundLoopSurvivesItsOwnBatchTests` writes them;
    - `Store`, `Launcher` (real, over `RecordingSpawner_Fake`), `Spawner`;
    - `Telegram` (`ScriptedInbound_Fake`, with the opt-in `Use_DistinctTopicIds()` added in this task);
    - `Engine` built with `Create_WithDecisionState(…, BridgeTestTiming.Fast(), …)`;
    - helpers `Start_Solo_Async(name)`, `Append_Solo(orchId, subject, body)`, `Append_Outbox(orchId, subject)`, `Drop_Request(json)`, `Tap_Async(labelFragment)`, `Wait_Until_Async(predicate, ms)`, `Archived_Names()`, `Channel(orchId)`.
    `Tap_Json` is copied from `TheInboundLoopSurvivesItsOwnBatchTests` into the harness with its comment. The two copies are named in the report as a known duplicate: that class is not this plan's to edit.

**`ScriptedInbound_Fake.Create_ForumTopic_Async` returns `1L` for every topic.** A test with two orchestrations would then route both to topic 1. Add `public void Use_DistinctTopicIds()`, after which topic ids count up from 100. It is **opt-in**, so no existing test's expectation moves. Say so in the method's doc.

**The step does not append to channels.** `Append_OrchestrationAppEntry` and `Raise_OwnerWait` are engine state (they raise activity and hold the owner credit). The step returns the words, and the engine appends them in the order §4.3 step 4 fixes. That keeps the step testable without an engine and keeps the ordering rule in one place (Task 9).

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void AnUnlinkedParent_GetsItsOwnIdAsEndeavourId()        // §4.3 step 2
[Fact] public void ALinkedParent_KeepsItsEndeavourId()
[Fact] public void TheChild_IsStartedWithTheRequestedNameAndTree()     // spawner saw worktree + name
[Fact] public void TheBornFromHandoverKey_IsTheOneSpelling()           // "ai-orchestrator-7#14", via Format_HandoverKey
[Fact] public void TheBirthNote_NamesTheParent_TheJob_AndWhereTheBriefIs()
[Fact] public void ALaunchFailure_Throws_AndLeavesTheParentUnlinked()
       // missing worktree → throws; parent.EndeavourId still null. So the step stamps the parent AFTER a successful launch and passes the id to the launcher explicitly; see Step 3
```

- [ ] **Step 2: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SiblingBirthStepTests"
```

- [ ] **Step 3: Implement.** Spec §4.3 lists "stamp the parent" (step 2) **before** the launch (step 3). The last test above says a failed launch must not leave the parent linked to an endeavour of one. Resolve it like this:
  - `Start_SiblingOrchestration` already computes the endeavour id as `parent.EndeavourId ?? parent.OrchId` for the child (Task 5).
  - The step calls `Set_EndeavourId(parent)` only **after** it returns.
  - The resulting state is identical, and there is no orphan link on failure.

  Record this in the step's doc as a deliberate reorder of spec §4.3 steps 2–3.

- [ ] **Step 4: Run the filter until it passes.** Then compile the harness by running `--filter "FullyQualifiedName~TheInboundLoopSurvivesItsOwnBatchTests"`, because the fake changed.

- [ ] **Step 5: Commit** `feat(siblings): the birth step — link after a successful launch, words for the engine`.

---

### Task 8: ENGINE — arrival: refuse or park; never during a usage-limit pause (§4.2 executor, §7.3)

**Serial: the first engine task.** Engine lines changed:
- `Process_PendingRequests`: add one line in the `!dispatchPaused` group;
- new method `Process_SpawnSiblingRequests`, about 30 lines, which is call-and-append only.

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
- Modify: `AIOrchestratorCoreLib/GeneralSupervision/ParkedCloseRequest/IParkedCloseRequest.cs` (`ParkedCloseKinds.Sibling`; `ISpawnSiblingRequest? Sibling { get; }`), `ParkedCloseRequestModel.cs`, `ParkedCloseRequest_Factory.cs` (`Create_ForSibling(ISpawnSiblingRequest request, string requester, string parkedFilePath)`, with `Sibling` null for the other three kinds), `ParkedCloseRequest_Reader.cs` (a fourth arm via `Read_SpawnSiblingRequest_OrNull`)
- Modify: `AIOrchestratorCoreLib/GeneralSupervision/ParkedConfirmation_Planner.cs` (`Resolve_Kind`: `SPAWN_SIBLING_ACTION => Sibling`)
- Create test: `AIOrchestratorCoreLib.Tests/Bridge/Siblings/SpawnSiblingArrivalTests.cs`

**Interfaces:**
- Consumes: Tasks 4, 6, 7 (`SiblingEngine_Harness`), `CloseConfirmation_Parking.Park`, `Archive_ResolvedRequest_BestEffort`, `Append_OrchestrationAppEntry`.
- Produces: a parked `spawn-sibling` file in `awaiting-owner/`, which Task 9 turns into a prompt. `ParkedCloseKinds.Sibling`, and `IParkedCloseRequest.Sibling`.

**O1 lives in Task 9, not here.** This task ends at "parked, and the requester told it is HELD". If the owner answers O1 with "no tap", Step 4's `Park` call becomes a direct `Execute_SiblingBirth(request)` (Task 9 Step 5, moved here), and Task 9 is deleted.

- [ ] **Step 1: Write the failing engine tests.** Use `SiblingEngine_Harness`. The parent is a real basic orchestration started through the harness, with its HANDOVER entry appended to its outbox by `ChannelAppendTool` or a direct write of `## [1] FROM solo — HANDOVER limits`.

```csharp
[Theory]
[InlineData("unspawnable")] [InlineData("not-a-solo")] [InlineData("no-handover-entry")]
[InlineData("at-cap")] [InlineData("worktree-missing")] [InlineData("worktree-not-of-repo")]
[InlineData("worktree-shared")] [InlineData("name-taken")]
public async Task EachRefusal_IsAnAgentEntry_AnArchiveLabel_AndNoPrompt(string label)
    // arrange the world for `label`; drop the request; wait for Archived_Names() to contain "<label>-…"
    // assert: the requester's owner-channel has a FROM app entry whose subject names the refusal
    //         Telegram fake recorded NO button (Find_ButtonFor("Start it") == null)
    //         nothing was spawned (Spawner.Launches count unchanged)

[Fact] public async Task AValidRequest_IsParked_AndTheRequesterIsToldItIsHeld()
    // awaiting-owner/ holds one file; requester's channel: "sibling HELD — the owner confirms this with a tap"

/// <summary>
/// A SPAWN DURING A USAGE-LIMIT PAUSE STAYS ON DISK (§4.2, §7.3). It joins the three that spawn,
/// in the !dispatchPaused group, for the reason Process_PendingRequests records: a pause that
/// spawned anyway is not a pause.
/// </summary>
[Fact] public async Task UnderADispatchPause_TheFileStaysOnDisk_AndRunsAfterTheLift()
    // put the engine in a dispatch pause the way DispatchPauseRestartTests does; drop; tick; file still in RequestsFolder;
    // lift; file leaves RequestsFolder (parked)
```

The `unspawnable` case's text goes to the **general** channel (`Append_GeneralAppEntry(AppEntryAudiences.Agent, …)`), because the requester does not exist. Assert it there. The spec's table says "general-channel failure, as in promote", and promote uses `AppEntryAudiences.Owner` there. **Use Agent** (decision 15: the owner cannot act on a missing orchestration's request), and record the deviation in the report.

- [ ] **Step 2: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SpawnSiblingArrivalTests"
```

- [ ] **Step 3: The parked-kind plumbing.**
  - Add `Sibling` to `ParkedCloseKinds`, **last**, so no persisted enum ordinal moves.
  - Add `IParkedCloseRequest.Sibling`, `Create_ForSibling` and the reader arm. The requester description constant is `SIBLING_REQUESTER_DESCRIPTION = "the solo session of this orchestration"`.
  - `ParkedConfirmation_Planner.Resolve_Kind` gains the arm. Its doc already says why unknown means null.
  - `CloseConfirmationPrompt_Builder.Build`'s `switch` must stay total. Add `ParkedCloseKinds.Sibling => Build_ForSibling(request, request.OrchId)`, with `Build_ForSibling` written in Task 9. **Until Task 9 lands**, that arm throws `NotSupportedException("the sibling prompt is Task 9's")`. No request can reach it before then, because nothing parks before this task's Step 4 and the tests do not tap.

- [ ] **Step 4: `Process_SpawnSiblingRequests`.** Add it to the `if (!dispatchPaused)` block after `Process_PromoteOrchestrationRequests(pending)`, and update that block's comment from "THE THREE THAT SPAWN" to "THE FOUR". Its body, for each request:

```csharp
var world = SiblingWorld_Reader.Read(_paths, _store, _configProvider.Get_Current(), request, ownParkedPath: null);
var refusal = SiblingRequest_Validator.Decide_Refusal_OrNull(request, world);

if (refusal != null)
{
    if (refusal.Value.Label == SiblingRefusals.UNSPAWNABLE)
        Append_GeneralAppEntry(AppEntryAudiences.Agent, refusal.Value.Subject, refusal.Value.Body);
    else
        Append_OrchestrationAppEntry(request.OrchId, AppEntryAudiences.Agent, refusal.Value.Subject, refusal.Value.Body);

    Archive_ResolvedRequest_BestEffort(request.SourceFilePath, refusal.Value.Label);
    continue;
}

var parkedPath = CloseConfirmation_Parking.Park(_paths, request.SourceFilePath);   // O1: the tap. See Task 9.
_log.Log_Info(request.OrchId, $"spawn-sibling held for the owner's confirmation ({parkedPath})");
var held = SiblingNotice_Wording.Describe_Held(request);
Append_OrchestrationAppEntry(request.OrchId, AppEntryAudiences.Agent, held.Subject, held.Body);
```

Wrap it in the same try/catch shape as `Process_PromoteOrchestrationRequests`: fail closed, archive `unheld`, and tell the requester.

- [ ] **Step 5: Run the filter until it passes.** Then run the neighbours: `--filter "FullyQualifiedName~PromoteOrchestrationRequestTests|FullyQualifiedName~CloseTapArchiveProbeTests|FullyQualifiedName~ParkedConfirmation"`. **Isolate any red with `--filter` and re-run it alone before believing it.**

- [ ] **Step 6: Commit** `feat(siblings): spawn-sibling arrives — refused to the solo, or parked for the owner`. The body lists the engine lines changed.

---

### Task 9: ENGINE — the owner's tap (O1), and the birth it authorises (§4.3, §4.4)

**This task is O1.** Serial after Task 8. Engine lines changed:
- `Ask_OwnerToConfirmClose_Async`: the `mootBecause` switch (~5919) gains a Sibling arm that re-runs the validator; the prompt-text line (~5992) picks `Build_ForSibling` for the Sibling kind;
- `Execute_ConfirmedClose`: one `else if` arm (~6361);
- new method `Execute_SiblingBirth`, about 35 lines, call-and-append.

**Files:**
- Modify: `AIOrchestratorCoreLib/GeneralSupervision/ParkedCloseRequest/CloseConfirmationPrompt_Builder.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
- Create test: `AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingConfirmationTests.cs`

**Interfaces:**
- Consumes: Tasks 6, 7, 8.
- Produces:
  - `CloseConfirmationPrompt_Builder.Build_ForSibling(IParkedCloseRequest request, string requesterName) : string`, which renders spec §2.1 step 3 exactly:

    ```
    🔗 <requesterName> wants a sibling session for a parallel job
    New topic: <Sibling.Name>
    Job: <Sibling.Job>
    Why: <Sibling.Reason>
    ```
  - `Build_ButtonLabels(Sibling) => ("✅ Start it", "✋ Keep one session")`
  - `Describe_AskedFor(Sibling) => $"a sibling session ('{name}')"`, `Describe_AskedFor_ToGeneral`, the resolution text (`confirms ? "starting…" : "kept as one session"`) and `Describe_NothingDone(Sibling) => "nothing was started"`

**The mutation this task must survive (spec §11, memory "verify a mutation applied"):** a Sibling-kind file must **never** reach `Execute_Close`. `Execute_ConfirmedClose` names every kind, and its comment explains why a catch-all is a live hazard. After the test is green:
1. delete the Sibling arm by hand;
2. re-run `AConfirmedSibling_NeverClosesTheRequester`;
3. **watch it go red**;
4. restore the arm.

Record in the report that the mutant reddened and which assertion caught it.

- [ ] **Step 1: Write the failing engine tests.**

```csharp
[Fact] public async Task AParkedRequest_PromptsInTheRequestersTopic_WithTheTwoButtons()
    // Telegram.Find_ButtonFor("Start it") and ("Keep one session") both non-null; the prompt text contains name, job, reason

[Fact] public async Task TapYes_StartsOneChild_LinkedAndNamed_BeforeItsFirstSpawn()
    // one new session; EndeavourId/BornFromOrchId/BornFromHandover/WorkingPath/DisplayName/overrides set;
    // Spawner.Launches.Last() carries the worktree and the name, so the fields existed at spawn

/// <summary>
/// AFTER THE LAUNCH, NEVER BEFORE (Process_StartRequests' ordering rule, §4.3 step 4): a
/// bridge-driven session baselines at registration, so an entry written first would be history
/// it never answers. The birth note comes first, then the job as FROM owner.
/// </summary>
[Fact] public async Task TapYes_WritesTheBirthNoteThenTheJob_AfterTheLaunch()
    // child owner-channel: entry 1 FROM app (Owner audience) "🔗 Sibling of …", entry 2 FROM owner == job

[Fact] public async Task TapYes_RaisesTheOwnerWaitOnTheChild()            // the child is in owner debt: its first act is an OWNER REQUESTS row
[Fact] public async Task TapYes_TellsTheParent_AndGeneral()               // parent: Owner-audience "sibling '<id>' started — <name> (its own topic)"
[Fact] public async Task TapYes_ArchivesAsStarted()                       // Archived_Names() has "started-…"
[Fact] public async Task AConfirmedSibling_NeverClosesTheRequester()      // parent.ClosedUtc stays null (the mutation target)
[Fact] public async Task TapNo_TellsTheRequester_AndStartsNothing()       // agent entry with "DECLINED"; no new session; archive "declined-…"
[Fact] public async Task AnUnansweredPrompt_LapsesAfterTwelveHours()      // set the parked file's mtime back 13 h; tick; archive "expired-…" (whatever Expire_CloseConfirmation labels) and the requester is told

/// <summary>RE-VALIDATED AT THE TAP (§4.2): a request whose worktree became shared while it waited is refused, not born.</summary>
[Fact] public async Task AWorktreeThatBecameSharedWhileParked_IsRefusedAtTheTap()

/// <summary>Review Focus 3. One HANDOVER, one child, however many times it is asked.</summary>
[Fact] public async Task ASecondRequestCitingTheSameHandover_IsAnsweredWithTheExistingChild()
    // after TapYes, drop the same JSON again → archive "handover-already-used-…"; body contains the child id; still one child
[Fact] public async Task ASecondRequestWhileTheFirstIsParked_IsAnsweredHeld()
[Fact] public async Task TheSameTapDeliveredTwice_StartsOneChild()        // Tap_Json twice with one callback id
```

- [ ] **Step 2: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SiblingConfirmationTests"
```

- [ ] **Step 3: The prompt.** `Build_ForSibling` and every `Describe_*` arm listed under Interfaces. Replace Task 8's temporary `NotSupportedException` arm in `Build`. In the engine, the prompt text becomes `request.Kind == ParkedCloseKinds.Sibling ? CloseConfirmationPrompt_Builder.Build_ForSibling(request, session.DisplayName ?? session.OrchId) : CloseConfirmationPrompt_Builder.Build(request, unresolved)`.

- [ ] **Step 4: The moot arm.** In `Ask_OwnerToConfirmClose_Async`, the Sibling kind is not moot by a one-line rule. It re-runs `SiblingRequest_Validator` with `SiblingWorld_Reader.Read(…, ownParkedPath: parkedPath)`. On a refusal it appends the refusal to the requester as Agent, archives with the refusal's label, and returns. Keep the arm small by calling a local `Refuse_ParkedSibling_IfNoLongerValid(request, parkedPath) : bool`.

- [ ] **Step 5: `Execute_ConfirmedClose` + `Execute_SiblingBirth`.** Add the arm:

```csharp
else if (request.Kind == ParkedCloseKinds.Sibling)
{
    archiveLabel = Execute_SiblingBirth(confirmation.ParkedPath, request.Sibling!);
}
```

`Execute_SiblingBirth(string parkedPath, ISpawnSiblingRequest sibling) : string` returns the archive label:
1. re-validate with `ownParkedPath: parkedPath`, and on refusal append it to the requester as Agent and return its label;
2. `var birth = SiblingBirth_Step.Execute(_store, _launcher, _paths, sibling)`;
3. `Append_OrchestrationAppEntry(birth.Child.OrchId, AppEntryAudiences.Owner, birth.BirthNote…)`;
4. `ChannelAppender.Append_OwnerEntry(_paths.Get_OwnerChannelFile(birth.Child.OrchId), sibling.Job, DateTime.Now)`, then `Raise_OwnerWait(birth.Child.OrchId)`, the exact `Process_StartRequests` precedent including its "could not be appended" error branch;
5. `Append_OrchestrationAppEntry(sibling.OrchId, AppEntryAudiences.Owner, birth.ParentNotice…)`;
6. `Append_GeneralAppEntry(AppEntryAudiences.Agent, birth.GeneralLine…)`;
7. `return "started"`.

A throw from step 2 propagates to `Execute_ConfirmedClose`'s existing catch, which archives through `CloseTapOutcome_Decider`. Check that the resulting label reads as a failure, not "started". If `Describe_ForArchive` would say "started" for a failure, set `archiveLabel` only after success.

Check the line near `Execute_ConfirmedClose`'s caller (~6263): `result.Request?.Kind == ParkedCloseKinds.Orchestration` gates a close-specific follow-up. Confirm it does nothing for Sibling, and add a test assertion if it could.

- [ ] **Step 6: Run the filter until it passes. Then run the mutation from the task header.**

- [ ] **Step 7: Neighbours.** Run `--filter "FullyQualifiedName~CloseTapArchiveProbeTests|FullyQualifiedName~CloseConfirmationPrompt|FullyQualifiedName~PromoteToFullCrewTests|FullyQualifiedName~SpawnSiblingArrivalTests"`, isolating any red.

- [ ] **Step 8: Commit** `feat(siblings): the owner's tap starts the sibling — birth note, job, one child per handover (O1)`.

---

### Task 10: The derived files — `.siblings`, `ENDEAVOUR.md`, and outbox compaction (§3.3 compaction, §3.4, §5.1, §7.4)

**Steps 1–6 are "T10a"**. They touch no engine file and may land any time after Tasks 1 and 2. **Step 7 is the engine call** and waits for its turn in the chain (after Task 9). Engine line changed: one call, `Sync_EndeavourArtefacts();`, after `Sync_PausedFlags();` in `Execute_MirrorTick_Inside_Snapshot_Async`, plus a 5-line method.

**Files:**
- Create: `AIOrchestratorCoreLib/Git/GitHead_Reader.cs`
- Create: `AIOrchestratorCoreLib/Bridge/Siblings/EndeavourMarkers_Sync.cs`, `SiblingDigestInput.cs`, `EndeavourDigest_Builder.cs`, `EndeavourDigest_Reader.cs`, `EndeavourArtefacts_Step.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (Step 7 only)
- Create tests: `AIOrchestratorCoreLib.Tests/Git/GitHeadReaderTests.cs`, `AIOrchestratorCoreLib.Tests/Bridge/Siblings/EndeavourMarkersSyncTests.cs`, `EndeavourDigestBuilderTests.cs`, `EndeavourArtefactsStepTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 2, 5 (`WorkingPath_Resolver`), `PlanLedger_Parser.Parse_OrNull`, `PlanProgress_Formatter.Describe_Counts`, `IPlanProgress.Lines` (`PlanLedgerLine.Marker`), `ChannelHistory_Counter.Read_Entries`, `Atomic_FileWriter.Write_AllText`, `Channel_Compactor.Compact_IfNeeded(string)`, `PausedFlag_Marker` (as the shape to copy).
- Produces:
  - `GitHead_Reader.Read_Branch_OrNull(string workingTreePath) : string?`. It reads `<tree>/.git`: a directory → `.git/HEAD`; a file `gitdir: <p>` → `<p>/HEAD`. `ref: refs/heads/<b>` → `<b>`, a detached sha → its first 7 characters, anything else → null. **No process.**
  - `EndeavourMarkers_Sync.Build_Text(IReadOnlyList<(string OrchId, string OutboxPath, bool Paused, string Name)> siblings) : string` (pure), and `.Sync(ISupervisionPaths paths, IOrchestrationSession self, IReadOnlyList<IOrchestrationSession> openSiblings) : bool changed`
  - `SiblingDigestInput` (plain data): `Name, OrchId, WorkingPath, Branch?, Paused, IPlanProgress? Progress, IReadOnlyList<IChannelEntry> OwnerChannelTail, IReadOnlyList<string> OutboxSubjects`
  - `EndeavourDigest_Builder` constants `MAX_UNFINISHED_LINES = 15`, `MAX_OWNER_ENTRIES = 6`, `MAX_ENTRY_BODY_CHARS = 400`, `MAX_OUTBOX_SUBJECTS = 3`; `.Build(IReadOnlyList<SiblingDigestInput> siblings) : string`
  - `EndeavourDigest_Reader.Read_Inputs(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> openSiblings) : IReadOnlyList<SiblingDigestInput>`
  - `EndeavourArtefacts_Step.Reconcile(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions) : IReadOnlyList<string>`, which returns the failures to log (never throws)

**Rules the builder and sync carry, each with a test:**
- `.siblings` has one line per **open** sibling, never the orchestration itself: `<orchId>\t<outbox path>\t<paused|live>\t<name>`. The name has `\t`, `\r` and `\n` replaced by a space (Review Focus 2, the belt to Task 4's braces). The file ends with `\n`, is written with `Atomic_FileWriter`, and is written **only when the text changes**.
- It is **deleted** when the orchestration is closed, unlinked, or has no open siblings: "IT MUST NOT OUTLIVE THE MODE" (`MeetingFlag_Marker`). The startup case is the same code: the first tick sees a closed or unlinked session and removes a stale file left by a crash (§7.4).
- `ENDEAVOUR.md` has one block per open sibling:
  - a header line: name, orch id, working path, branch (or `branch ?`), `live|paused`;
  - the counts line via `Describe_Counts`, or `no task ledger yet`;
  - the unfinished lines in file order (markers `>`, ` `, `!`, `?`; use the `PlanLedger_Markers` constants, never literals), capped at 15 with a `+k more` tail;
  - the last 6 owner-channel entries whose `Author` is `Owner` or `Solo` (app entries of every audience are skipped by construction), each as `[n] FROM <word> — <subject>` plus at most 400 body characters, newest last;
  - `outbox:` plus the last 3 outbox subjects.

  **No clock in the text** (the `GeneralDashboard_Composer` rule), so an unchanged world rewrites nothing. It is deleted under the same rule as `.siblings`.
- Compaction: for every **open** linked orchestration whose outbox exists, call `Channel_Compactor.Compact_IfNeeded(outbox)` (the no-guard overload). The doc gives the spec's reason: no tailer cursor exists to re-anchor, and the print runner's cursor is by identity.
- Every file operation is inside try/catch. A failure is returned as a string for `_log.Log_Warning`, and **never** thrown out of the tick.

- [ ] **Step 1: Write the failing `GitHeadReaderTests`** against a fake tree:
  - `.git` as a directory with `HEAD` = `ref: refs/heads/feat/x\n` → `feat/x`;
  - `.git` as a file `gitdir: <abs>` whose `HEAD` holds a sha → 7 characters;
  - no `.git` → null;
  - an unreadable `HEAD` → null.

- [ ] **Step 2: Write the failing sync and builder tests.**

```csharp
// EndeavourMarkersSyncTests
[Fact] public void TheLine_HasFourTabSeparatedFields_AndTheOwnOrchestrationIsNeverListed()
[Fact] public void ATabOrNewlineInAName_BecomesASpace()                  // Review Focus 2
[Fact] public void APausedSibling_IsMarkedPaused()
[Fact] public void AnUnchangedWorld_DoesNotRewriteTheFile()              // mtime unchanged across two Sync calls
[Fact] public void NoOpenSiblings_DeletesTheFile()

// EndeavourDigestBuilderTests
[Fact] public void MoreThan15UnfinishedLines_AreCapped_WithAPlusKMoreTail()
[Fact] public void OnlyTheLast6OwnerOrSoloEntries_AreShown_NewestLast()
[Fact] public void AnAppEntry_IsNeverShown()                             // FROM app, either audience
[Fact] public void ABodyOver400Chars_IsCut()
[Fact] public void OnlyTheLast3OutboxSubjects_AreShown()
[Fact] public void FinishedLines_AreNotShown()                           // [x] and [-]
[Fact] public void TheTextCarriesNoClock()                               // two builds a minute apart are byte-identical

// EndeavourArtefactsStepTests
[Fact] public void TwoLinkedOpenSolos_EachGetBothFiles_NamingTheOther()
[Fact] public void AClosedMember_LosesItsFiles_AndDropsFromTheOthersList()
[Fact] public void AStaleFileFromACrash_IsRemovedOnTheFirstReconcile()   // write .siblings for an unlinked session; Reconcile → gone
[Fact] public void AnOutboxOver90Entries_IsCompacted()                   // 100 entries → live file ≤ KEEP_RECENT_ENTRIES + archive exists
[Fact] public void TheDigest_ReadsHistoryAcrossCompaction()              // owner-channel compacted → the last 6 still come from ChannelHistory_Counter
[Fact] public void ALockedFile_IsReportedAsAFailure_NotThrown()
```

- [ ] **Step 3: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~GitHeadReaderTests|FullyQualifiedName~EndeavourMarkersSyncTests|FullyQualifiedName~EndeavourDigestBuilderTests|FullyQualifiedName~EndeavourArtefactsStepTests"
```

- [ ] **Step 4: Implement** `GitHead_Reader`, `EndeavourMarkers_Sync`, `EndeavourDigest_Builder`, `EndeavourDigest_Reader` and `EndeavourArtefacts_Step` to the rules above. The reader uses `WorkingPath_Resolver.Resolve` for the tree and `GitHead_Reader` for the branch. **Never `GitSnapshot_Reader`, which forks `git` several times** (Global Constraints).

- [ ] **Step 5: Run the filter until it passes.**

- [ ] **Step 6: Commit (T10a)** `feat(siblings): the derived .siblings list and ENDEAVOUR.md digest, capped and clock-free`.

- [ ] **Step 7 (engine, in chain order after Task 9): wire the tick.**

```csharp
Sync_PausedFlags();

Sync_EndeavourArtefacts();   // after .paused, so a sibling's `paused|live` column reads this tick's truth
```

```csharp
void Sync_EndeavourArtefacts()
{
    foreach (var failure in Bridge.Siblings.EndeavourArtefacts_Step.Reconcile(_paths, Sessions_ThisTick()))
        _log.Log_Warning(GLOBAL_ORCH_ID, failure);
}
```

Extend `PauseGatesEveryWakerScanTests.ThePausedMarkerIsReconciledOnEveryTick_…` with one assertion: `Sync_EndeavourArtefacts()` appears **after** `Sync_PausedFlags()` in the tick body. Then run:

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PauseGatesEveryWakerScanTests|FullyQualifiedName~EndeavourArtefactsStepTests|FullyQualifiedName~SiblingConfirmationTests"
```

Commit `feat(siblings): reconcile the endeavour artefacts on every tick`, naming the engine lines.

---

### Task 11: ENGINE — the summed bar, and General groups linked orchestrations (§2.4, §3.5)

**Serial after Task 10 Step 7.** The touched engine piece is the `messageThreadId == null` branch of `Build_ProgressReportText` (~7613–7627). It **moves out** into `Telegram/ProgressReport_Builder`, and the engine keeps a one-line call.

**Files:**
- Create: `AIOrchestratorCoreLib/Bridge/Siblings/EndeavourProgress_Reader.cs`, `AIOrchestratorCoreLib/Telegram/ProgressReport_Builder.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
- Create tests: `AIOrchestratorCoreLib.Tests/Bridge/Siblings/EndeavourProgressReaderTests.cs`, `AIOrchestratorCoreLib.Tests/Telegram/ProgressReportBuilderTests.cs`

**Interfaces:**
- Consumes: `EndeavourMembers_Resolver.Resolve_All`, `PlanLedger_Parser.Parse_OrNull`, `PlanProgress_Factory.Create`, `PlanProgress_Formatter.Describe_Counts(IPlanProgress)`.
- Produces:
  - `EndeavourProgress_Reader.Sum_OrNull(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> members) : IPlanProgress?`. It sums `Done`, `InProgress`, `Blocked`, `BlockedOnOwner`, `NotDoing` and `Total`, and concatenates `OpenTasks`, `InProgressTasks` and `BlockedTasks`. It is null when no member has a parseable PLAN.md.
  - `ProgressReport_Builder.Build_OpenOrchestrationsText(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions) : string`, which is General's body. With no endeavours it is **byte-identical** to today's.

**Rendering (§2.4):**
- Unlinked open orchestrations render exactly as today: `<name>: <counts>` or `<name>: no task ledger yet`.
- An endeavour with **at least one open member** renders once, at the position of its first member in `sessions` order:
  - header `🔗 <group name> — <Describe_Counts(sum)>`;
  - then one line per **open** member: `   · <name>: <counts>`.
  - The sum **includes closed members** (§3.5, the bar never goes backwards).
  - The group name: when every member's display name has the form `<code> · <rest>` with the same code, it is `<code> · <rest1> + <rest2> …`. Otherwise it is the full names joined with ` + `. Names come from all members, closed ones included, in `sessions` order.
- `no open orchestrations` when nothing is open, unchanged.

- [ ] **Step 1: Write the golden test first, against today's engine output.** In `ProgressReportBuilderTests`, build two **unlinked** orchestrations with real PLAN.md files, one with `- [x] a\n- [>] b\n- [ ] c` and one with no PLAN.md. Assert `Build_OpenOrchestrationsText` equals this literal:

```csharp
Assert.Equal(
    "one: 1/3 done (33%) · 1 running\ntwo: no task ledger yet",
    ProgressReport_Builder.Build_OpenOrchestrationsText(_paths, _store.Load_All()));
```

**Before trusting the literal, derive it from the unmoved engine.** `/progress` sent in General through the harness gives the reference text. If the literal and the engine disagree (the `Describe_Counts` tail wording, for example), the engine's output is the truth: fix the literal, not the builder.

- [ ] **Step 2: Write the failing grouping and sum tests.**

```csharp
// ProgressReportBuilderTests
[Fact] public void TwoLinkedOpenSiblings_RenderAsOneGroup_WithTheSummedBar()
       // "🔗 AI-Orch · settings + limits — 9/14 done (64%)…\n   · AI-Orch · settings: 6/8…\n   · AI-Orch · limits: 3/6…"
[Fact] public void AnUnlinkedOrchestrationBesideAGroup_RendersAsToday()
[Fact] public void MixedPlatformCodes_JoinFullNames()
/// <summary>Review Focus 4. The endeavour's first orchestration closed; its two children live.</summary>
[Fact] public void AClosedFirstMember_StaysInTheSumAndTheGroupName_ButGetsNoLine()
[Fact] public void AnEndeavourWithNoOpenMember_DoesNotRender()

// EndeavourProgressReaderTests
[Fact] public void Sum_AddsEveryCount()
[Fact] public void Sum_IncludesAClosedMember()
[Fact] public void Sum_SkipsParkedAndOwnerRequestsSections()   // PARKED / OWNER REQUESTS lines excluded, because the parser skips them
[Fact] public void Sum_OfMembersWithNoLedger_IsNull()
```

- [ ] **Step 3: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ProgressReportBuilderTests|FullyQualifiedName~EndeavourProgressReaderTests"
```

- [ ] **Step 4: Implement and move.**
  - `ProgressReport_Builder` reproduces `Build_OrchestrationCountsLine` for the unlinked case. Read the engine method and copy **its** strings, and **leave the engine's copy in place**: the topic branch and other callers still use it.
  - The engine's `messageThreadId == null` branch becomes `return Telegram.ProgressReport_Builder.Build_OpenOrchestrationsText(_paths, _store.Load_All());`.
  - This leaves two implementations of the plain counts line: the engine's private `Build_OrchestrationCountsLine` and the builder's. **Make the engine's delegate to the builder's**: add `public static string Build_CountsLine(ISupervisionPaths paths, string orchId, string displayName, PlanProgressSnapshot? previous)` on the builder, and have the engine's method call it. That is decision 12 applied to this move.

- [ ] **Step 5: Run the filter until it passes.** Then run `--filter "FullyQualifiedName~GeneralDashboardTests|FullyQualifiedName~Progress"`. `GeneralDashboard_Composer.Compose` wraps this body, so its tests are the second golden.

- [ ] **Step 6: Commit** `feat(siblings): General groups an endeavour under one summed bar`, naming the engine lines moved.

---

### Task 12: ENGINE — `/endeavour` on demand, and sibling traffic never pushed (O3, §2.2, §2.4)

**This task is O3.** Serial after Task 11. Engine lines changed:
- one `else if (command == "endeavour")` arm in the command switch, beside `progress`;
- a new 6-line `Send_EndeavourReport_Async` that calls the builder and chunks like `Send_ProgressReport_Async`.

**Files:**
- Create: `AIOrchestratorCoreLib/Bridge/Siblings/EndeavourReport_Builder.cs`
- Modify: `AIOrchestratorCoreLib/Telegram/BotCommandMenu.cs`, `AIOrchestratorCoreLib.Tests/Telegram/BotCommandMenuTests.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
- Create tests: `AIOrchestratorCoreLib.Tests/Bridge/Siblings/EndeavourReportBuilderTests.cs`, `SiblingOutboxIsNeverMirroredTests.cs`

**Interfaces:**
- Consumes: Task 11's builder (the group block), Task 1, Task 2.
- Produces: `EndeavourReport_Builder.Build(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions, IOrchestrationSession? topicSession) : string`

**What `/endeavour` says:**
- In a linked topic: the group block from Task 11 for **this** endeavour only, then per open member `<name> — outbox:` plus the last 3 outbox subjects (history via `ChannelHistory_Counter`).
- In an unlinked topic: `this topic is not part of an endeavour — /progress shows its ledger`.
- In General: `/endeavour works inside a sibling's topic — /progress here already groups them`.

- [ ] **Step 1: Write the failing tests.**

```csharp
// EndeavourReportBuilderTests
[Fact] public void InALinkedTopic_ShowsTheGroupAndEachOutboxsLastThreeSubjects()
[Fact] public void InAnUnlinkedTopic_SaysSo()
[Fact] public void InGeneral_PointsToTheTopics()
[Fact] public void AnotherEndeavour_IsNotShown()

// SiblingOutboxIsNeverMirroredTests: engine, FailableTelegram_Fake, the O3 pin
/// <summary>
/// O3: SIBLING TRAFFIC IS NEVER PUSHED. The outbox sits in the orchestration folder's root,
/// where ChannelDiscovery does not look (spec §3.3), so no code enforces this. The test is here
/// so that a future "discover every *.md" change turns red instead of ringing the owner's phone
/// with two sessions' coordination.
/// </summary>
[Fact] public async Task AnOutboxAppend_NeverReachesTelegram()
    // two linked solos with topics; append "## [2] FROM solo — ASK which DTO?" to A's outbox; drive 5 ticks;
    // Assert no sent/edited text contains "which DTO"

// BotCommandMenuTests: add "endeavour" to the expected order right after "progress"/"left"/"tasks" (wherever the owner-use order puts ledger commands; read the test's own list)
```

- [ ] **Step 2: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~EndeavourReportBuilderTests|FullyQualifiedName~SiblingOutboxIsNeverMirroredTests|FullyQualifiedName~BotCommandMenuTests"
```

- [ ] **Step 3: Implement.**
  - The builder.
  - The menu entry `("endeavour", "The linked jobs of this topic's endeavour: combined bar and what the siblings last told each other")`. English, and within Telegram's length limit, which the menu test checks.
  - The engine arm and `Send_EndeavourReport_Async`.
  - **`/endeavour` is a typed command, not a PULSE button**, so `TopicCommandButtons.KNOWN_COMMANDS` does not change. Say so in the report so a reviewer does not "fix" it.

- [ ] **Step 4: Run the filter until it passes.** Then run `--filter "FullyQualifiedName~EveryTopicButtonIsWiredTests|FullyQualifiedName~TopicCommandButtons"`.

- [ ] **Step 5: Commit** `feat(siblings): /endeavour shows the siblings on demand; their traffic is never pushed (O3)`.

---

### Task 13: The print and stream runners — sibling sources, source-aware trigger, pause gate, the pack (§5.4)

**Touches no engine file.** It runs beside the chain once Tasks 1, 2 and Task 10's digest file exist.

**Files:**
- Create: `AIOrchestratorCoreLib/Running/TurnSource/TurnSourceKinds.cs`
- Modify: `AIOrchestratorCoreLib/Running/TurnSource/ITurnSource.cs`, `TurnSourceModel.cs`, `TurnSource_Factory.cs`, `TurnSources_Resolver.cs`
- Modify: `AIOrchestratorCoreLib/Running/PrintTurn_Trigger.cs`, `AIOrchestratorCoreLib/Running/TurnCursor/TurnCursor_Factory.cs`, `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` (the `Select_Pending` call, ~899)
- Modify: `AIOrchestratorCoreLib/Running/StatePack/StatePackInputs.cs`, `StatePackInputs_Reader.cs`, `StatePack_Builder.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/PauseGatesEveryWakerScanTests.cs`
- Create test: `AIOrchestratorCoreLib.Tests/Running/SiblingTurnSourcesTests.cs`

**Interfaces:**
- Produces:
  - `enum TurnSourceKinds { Owner, Spoke, Sibling }`
  - `ITurnSource.Kind : TurnSourceKinds`. `IsOwnerChannel` stays, now meaning `Kind == Owner`, so no caller changes.
  - `TurnSource_Factory.SIBLING_KEY_PREFIX = "sibling:"`, `.Create_Sibling(string siblingOrchId, string outboxPath)` with key `sibling:<orchId>`. The existing `Create(key, path, isOwnerChannel)` maps to Owner/Spoke.
  - `PrintTurn_Trigger.Is_Inbound(SessionRoles role, TurnSourceKinds kind, ChannelAuthors author)`, where **on a Sibling source, `Solo` is inbound for a Solo**. The old two-argument overload is kept and delegates with `TurnSourceKinds.Owner`, so `UndeliveredSpokeTraffic_Reporter` and the ten test call sites do not move.
  - `PrintTurn_Trigger.Select_Pending(SessionRoles role, TurnSourceKinds kind, IReadOnlyList<IChannelEntry> entries, ITurnCursor cursor)`, with the old overload kept the same way
  - `StatePackInputs` gains a trailing optional `string? endeavourDigest = null`

**The pause gate lives in the resolver.** A **paused** solo resolves **no** sibling sources. By the dispatcher's own rule, a cursor is never dropped for a source that merely did not resolve this tick, so the sibling cursor survives. When the pause lifts, the source resolves again and the backlog is delivered in one turn. That mirrors the watcher's "sibling traffic waits for them" (§5.3, Task 17), and it is the gate §5.4 asks for.

**A child's first sight of its parent's outbox is a baseline, not a backlog.** The HANDOVER entry is already in the parent's outbox when the child's print session first registers. `firstSightOfThisSession` → `Create_Baseline` absorbs it as history, which is right: the child reads its brief because the birth note names it. It must not be woken by it a second time. A test pins this.

- [ ] **Step 1: Write the failing tests.**

```csharp
// SiblingTurnSourcesTests — store + paths in a temp tree, no dispatcher, no process
[Fact] public void ALinkedSolo_IsWokenByEachOpenSiblingsOutbox()        // Resolve(…Solo…) → own Owner source + one Sibling per open sibling
[Fact] public void AnUnlinkedSolo_HasOnlyItsOwnSource()                 // today, pinned
[Fact] public void APausedLinkedSolo_ResolvesNoSiblingSources()
[Fact] public void AClosedSibling_IsNotASource()
[Fact] public void OnASiblingSource_ASoloEntryIsInbound()               // Is_Inbound(Solo, Sibling, Solo) == true
[Fact] public void OnTheOwnerSource_ASoloEntryIsStillNotInbound()       // Is_Inbound(Solo, Owner, Solo) == false: the one-member loop
[Fact] public void OnASiblingSource_AnAppOrOwnerEntryIsNotInbound()     // nobody but a sibling writes there; anything else is not a wake
[Fact] public void TheBaselineOfASiblingSource_AbsorbsItsExistingSoloEntries()   // Create_Baseline(sibling source, Solo, entries) → delivered contains the HANDOVER digest
[Fact] public void ThePack_CarriesTheDigest_WhenTheFileExists()         // StatePackInputs_Reader.Read → EndeavourDigest == file text; StatePack_Builder output contains it under "## Your endeavour"
[Fact] public void ThePack_OfAnUnlinkedSolo_HasNoEndeavourSection()

// PauseGatesEveryWakerScanTests
[Fact] public void TheSiblingTurnSources_AreGatedOnPause()
       // Read_Source("TurnSources_Resolver.cs"): the sibling branch contains "Paused" before "Create_Sibling"
```

- [ ] **Step 2: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SiblingTurnSourcesTests|FullyQualifiedName~PauseGatesEveryWakerScanTests"
```

- [ ] **Step 3: Implement.**
  - The resolver: for `SessionRoles.Solo`, return `[own]`, plus sibling sources when `store.Get_Session_OrNull(orchId)` is open, linked and **not paused**, from `EndeavourMembers_Resolver.Resolve_OpenSiblings(store.Load_All(), session)`. Dedupe paths case-insensitively with the existing `seenPaths` idiom.
  - `TurnCursor_Factory` lines 42 and 76 pass `source.Kind`.
  - The dispatcher call at ~899 passes `source.Kind`.
  - `StatePackInputs_Reader.Read` reads `paths.Get_EndeavourDigestFile(orchId)` with a tolerant read. A missing file gives null, and an unreadable one adds a line to `unavailable`.
  - `StatePack_Builder` appends `## Your endeavour (ENDEAVOUR.md)\n<text>` when it is non-null.
  - **Check `PrintSessionState_Store` persistence.** If cursors are persisted with an `isOwnerChannel` flag, rather than rebuilt from the resolved source on each read, a persisted sibling cursor must round-trip its kind. Read it and say which it is in the report.

- [ ] **Step 4: Run the filter until it passes.** Then run the runner neighbours, which are the wall-clock family, so isolate any red:

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PrintTurnTrigger|FullyQualifiedName~TurnCursor|FullyQualifiedName~StatePack|FullyQualifiedName~TurnSources|FullyQualifiedName~UndeliveredSpokeTraffic"
```

- [ ] **Step 5: Commit** `feat(runner): a linked solo is woken by its siblings' outboxes, never while paused`.

---

### Task 14: ENGINE — lifecycle: survivors are told, promote and `/switch` refuse a linked orchestration (§2.3, §7.5, §7.6)

**Serial after Task 12.** Engine lines changed:
- `Execute_Close`: one post-step call inside its own try/catch, after the general-channel append (~5725);
- `Process_PromoteOrchestrationRequests`: one refusal block after the missing/closed check (~5392);
- `Switch_OrchestrationShape_Async`: one early reply after the missing/closed check (~8938).

**Files:**
- Create: `AIOrchestratorCoreLib/Bridge/Siblings/SiblingClose_Step.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/Siblings/SiblingNotice_Wording.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
- Create test: `AIOrchestratorCoreLib.Tests/Bridge/Siblings/SiblingLifecycleTests.cs`

**Interfaces:**
- Produces:
  - `SiblingClose_Step.Build_SurvivorNotices(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions, IOrchestrationSession closed) : IReadOnlyList<(string OrchId, string Subject, string Body)>`, with subject `sibling '<name>' closed — its outbox and PLAN.md stay on disk` and body `unfinished lines: …`, using `PlanProgress_Formatter.Describe_Unfinished` or `none`
  - `SiblingNotice_Wording.Describe_LinkedPromoteRefusal()` and `.Describe_LinkedSwitchRefusal()`. The latter is exactly `this topic is linked to siblings — close them or keep one session.` (§7.6).

**The survivor notice must never make a successful close read as a failure.** `Execute_Close`'s catch reports "close-orchestration FAILED" and rethrows. A throw from the post-step would therefore tell the owner a close failed that in fact succeeded. The post-step gets its own try/catch → `_log.Log_Warning`.

- [ ] **Step 1: Write the failing engine tests.**

```csharp
[Fact] public async Task ClosingOneSibling_TellsEachSurvivor_AsAnAgentEntry()     // survivor channel has the notice; the owner was not texted about it (Telegram sent nothing containing "stay on disk")
[Fact] public async Task ClosingOneSibling_NeverClosesAnother()
[Fact] public async Task ClosingOneSibling_DropsItFromTheSurvivorsList_NextTick()  // .siblings no longer names it
[Fact] public async Task ClosingASiblingWithOpenLines_IsNotRefused()                // §7.5: the owner may close anything
[Fact] public async Task ClosingTheLastSibling_LeavesNoArtefacts()                  // no .siblings / ENDEAVOUR.md anywhere; General has no 🔗 line
[Fact] public async Task APromoteRequestFromALinkedSolo_IsRefusedLinkedOrchestration()   // archive "linked-orchestration-…"; agent entry; nothing parked
[Fact] public async Task SwitchInALinkedTopic_RepliesAndChangesNothing()            // the exact §7.6 sentence; SupervisorSpawnedUtc still null
[Fact] public async Task SwitchInAnUnlinkedTopic_StillWorks()                       // today, pinned by reaching the existing promote path
```

- [ ] **Step 2: Run the tests and see them fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SiblingLifecycleTests"
```

- [ ] **Step 3: Implement** the step and the three engine edits. "Linked" means `session.EndeavourId != null`: v1 does not unlink, even after every other sibling has closed (§7.6). Promote's refusal uses `SiblingRefusals.LINKED_ORCHESTRATION` and the promote precedent's Agent audience.

- [ ] **Step 4: Run the filter until it passes.** Then run `--filter "FullyQualifiedName~PromoteToFullCrewTests|FullyQualifiedName~PromoteOrchestrationRequestTests|FullyQualifiedName~ClosingATopicReallyDeletesItTests|FullyQualifiedName~CloseTapArchiveProbeTests|FullyQualifiedName~Switch"`.

- [ ] **Step 5: Commit** `feat(siblings): survivors hear of a close; a linked orchestration cannot be promoted or switched`.

---

### Task 15: One question at a time stays per topic (O4, §8.5)

**This task is O4.** It runs after Task 14 (it uses the harness with two linked topics) and after Task 16 (it adds a paragraph to `siblings.md`). **It changes no production C#.** The behaviour already holds (`QuestionHold_Policy.Should_Hold` is "PER ORCHESTRATION, NOT GLOBAL"), and this task pins it so that a later "fix" turns red.

**Files:**
- Create test: `AIOrchestratorCoreLib.Tests/Bridge/Siblings/QuestionHoldIsPerTopicTests.cs`
- Modify: `kit/skills/solo/reference/siblings.md` (one paragraph), `AIOrchestratorCoreLib.Tests/Kit/SoloIsToldAboutSiblingsTests.cs` (one assertion)

- [ ] **Step 1: Write the engine pin.** Use `FailableTelegram_Fake`, two linked solos A and B with distinct topics, both in the default delivery mode:

```csharp
/// <summary>
/// O4: A QUESTION HELD IN ONE SIBLING'S TOPIC DOES NOT HOLD THE OTHER'S. The owner split the work
/// so that the jobs do not wait on each other (§8.5). A cross-topic hold would make 'limits' wait
/// for the owner's answer about 'settings', which is the single-thread bottleneck again.
/// </summary>
[Fact]
public async Task AQuestionPendingInA_DoesNotHoldBsEntries()
    // raise A's .awaiting-answer (AwaitingAnswerFlag_Marker) and append a second FROM solo entry to A;
    // append a FROM solo entry to B; drive until B's text is sent;
    // Assert B's entry was sent AND A's second entry was not (held)
```

- [ ] **Step 2: Run it.** It should pass at once, because this is a pin. If it is red, stop: the per-topic assumption is false on this tree, and that is a finding for the owner, not a fix.

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~QuestionHoldIsPerTopicTests"
```

**Prove the pin can fail.** Temporarily make `QuestionHold_Policy.Should_Hold`'s engine caller ask about *any* orchestration's flag, watch the test go red, and restore it. Record the mutant in the report.

- [ ] **Step 3: The prose paragraph** in `siblings.md`, under "Boundaries":

  > **One question at a time is per topic.** Your pending question holds your topic only. It does not hold your siblings', and theirs do not hold yours. A question that affects **both** jobs is asked **once**, by the sibling whose job it blocks. The others learn the answer from `ENDEAVOUR.md` at their next boundary. Never ask the owner the same decision in two topics.

  Add `Assert.Contains("asked **once**, by the sibling whose job it blocks", siblings)` to `SoloIsToldAboutSiblingsTests`.

- [ ] **Step 4: Run** `--filter "FullyQualifiedName~QuestionHoldIsPerTopicTests|FullyQualifiedName~SoloIsToldAboutSiblingsTests"`.

- [ ] **Step 5: Commit** `test(siblings): a question held in one sibling's topic never holds another's (O4)`.

---

### Task 16: Kit prose — the solo learns when and how to ask for a sibling (§8.1, §8.2, §8.4)

**Independent of the C# work.** It may run in wave 1. It must be **merged together with Task 9** (the solo must not be taught a request the app cannot yet execute), so its commit sits on the branch and nothing ships before Task 18.

**Files:**
- Modify: `kit/skills/solo/SKILL.md`: a new section after "When a basic orchestration outgrows itself — asking for a crew" (it ends around line 670, before "RUN TO THE END"), and one sentence in "Boot sequence — LEAN"
- Create: `kit/skills/solo/reference/siblings.md`
- Modify: `kit/skills/general-supervisor/SKILL.md` (one paragraph)
- Create test: `AIOrchestratorCoreLib.Tests/Kit/SoloIsToldAboutSiblingsTests.cs`

**Rules (`.claude/rules/kit-and-scripts.md`):** add, never rewrite an existing rule. `KitProseCarriesTheOwnersRulesTests` counts normative sentences, and it must keep passing untouched. The pointer to `reference/siblings.md` carries the `$REF` resolution command the print-runner pointer already uses, because "a bare `reference/...` is NOT a path your tools can open".

- [ ] **Step 1: Write the failing prose test.**

```csharp
public class SoloIsToldAboutSiblingsTests
{
    [Fact] public void TheThreeRoutes_AreKeptDistinct()
        // SKILL.md contains "Width → fan out", "an independent review or a crew → promote", "steer separately" and "sibling"
    [Fact] public void ASiblingIsOnlyOnTheOwnersWord()
        // "Only on the owner's word"
    [Fact] public void TheRecipe_NamesTheOutboxTheIndexAndTheRequestFields()
        // "HANDOVER", "sibling-outbox.md", "\"action\":   \"spawn-sibling\"" (or the exact JSON block), "Do not re-drop"
    [Fact] public void TheBootStep_PointsAtSiblingsMd_WhenTheListExists()
        // SKILL.md contains "$ORCH/.siblings" and "reference/siblings.md"
    [Fact] public void TheRelayRule_KeepsTheTrace()
        // siblings.md: "RELAY owner#" and "via <topic> #n"
    [Fact] public void ASiblingBlock_IsAMachineBlock()
        // siblings.md: "- [!] … (waiting on sibling"
    [Fact] public void NeverAcknowledgeAnAcknowledgement()
        // siblings.md contains that sentence
    [Fact] public void AnOutboxEntry_HasThePhoneBudget()
        // siblings.md: "600 characters"

    static string Read(string relative) { /* Kit.KitRepoFiles-based, REFUSING if not found, like SoloIsToldToFanOutTests */ }
}
```

- [ ] **Step 2: Run the test and watch it fail.** Also run `NoProtocolFileIsOrphanedTests`, which must still be green before the edit:

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SoloIsToldAboutSiblingsTests|FullyQualifiedName~NoProtocolFileIsOrphanedTests|FullyQualifiedName~KitProseCarriesTheOwnersRulesTests"
```

- [ ] **Step 3: Write the prose.**
  - **SKILL.md section "When the work splits in two — asking for a SIBLING"**, following §8.1:
    - the three-way test;
    - "Only on the owner's word", with the reason that it is a spend increase, like promotion;
    - the 5-step recipe (worktree and branch; `HANDOVER` in **your outbox** via `channel-append.sh --channel "$ORCH/sibling-outbox.md"`, noting the `[n]` the helper prints; the request JSON exactly as spec §4.1 with the file name `sibling-<orchId>-<timestamp>.json`; one line to the owner; back to work);
    - "Do not re-drop the request. The app answers with a `FROM app` entry either way";
    - the pointer to `reference/siblings.md` with the `$REF` command.
  - **Boot step**, one sentence after step 1: "If `$ORCH/.siblings` exists you are part of an endeavour: read `reference/siblings.md`, then `ENDEAVOUR.md`, then, if your first `FROM app` entry names one, your brief (the HANDOVER entry it names)."
  - **`reference/siblings.md`**, following §8.2 item by item: paths, shape, the ack rule, boundaries, owner questions about the other job, the relay rule, the `[!]` block marker, paused siblings, scope (decision 22), CLAIM/RELEASE for ambient files (§6), HANDBACK before closing (§6), and merging per topic (§6). Task 15 adds the O4 paragraph.
  - **general-supervisor**: one paragraph (§8.4). Siblings exist, General groups them under 🔗, and the general supervisor never starts one; it tells the owner to ask the solo in its topic.

- [ ] **Step 4: Run the filter until it passes.** `NoProtocolFileIsOrphanedTests` must now see `siblings.md` referenced.

- [ ] **Step 5: Commit** `docs(kit): the solo learns siblings — when to ask, the recipe, the outbox protocol`.

---

### Task 17: Kit watcher — the sibling half, and the harnesses that run it (§5.3)

**Independent of the C# work.** Merge it with Task 16.

**Files:**
- Modify: `kit/skills/solo/reference/watcher.md`
- Modify: `kit/hooks/watcher-behaviour-check.sh`, `kit/self-write-suppression-check.sh`

**The hard constraint.** `watcher-behaviour-check.sh` extracts the fenced bash block that defines `read_fp()` and rewrites exactly two lines: `^while true; do$` and `^  sleep 5$`. It **refuses to run** if either rewrite fails to apply. The sibling half must therefore live **inside the same loop, in the same block**, with those two lines untouched. The owner-channel half stays **byte-for-byte**, `self_write_suppresses` included (§5.3).

**The sibling half (append to the block, following §5.3's sketch):**
- `orch="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS"`; `sibs="$orch/.siblings"`, **re-read every iteration**, so a sibling born mid-loop joins.
- `read_sib_fp` builds one line per outbox: `<path>|<size> <hash>`. It reads each file the way `read_fp` does (`wc -c`, `md5sum` with the `md5 -q` fallback the kit rule requires, exit statuses checked, and trimming by parameter expansion, never a pipe into `tr`). A file that fails keeps its **previous** line: unknown, never a change.
- **Fire** for each path whose line changed and which was present in the previous set, with the text `SIBLING <id> WROTE — read its outbox from the last entry you saw, act if it asks you something.`
- **Never fire** for a path seen for the first time. Record it as baseline.
- **While `$orch/.paused` exists**, the sibling half neither fires **nor advances its baseline**, so traffic that arrived during the pause fires once after the pause lifts ("sibling traffic waits for them").
- **No self-write logic.** Your own outbox is never listed in your own `.siblings`. Say this in a comment, so a reviewer does not add suppression that would swallow a sibling.
- Twelve consecutive failed sibling reads print `WATCHER BLIND — a sibling outbox has been unreadable for about a minute …` once.

- [ ] **Step 1: Extend `watcher-behaviour-check.sh` first (red).** Add a `run_sibling_case` for the solo role only, reusing `extract_block` and the same two rewrites. It builds a fake tree with `$orch/.siblings` listing `B` → `$home/B/sibling-outbox.md`. Its `apply_step` gains:
  - `sibappend`: append an entry to B's outbox;
  - `sibadd`: add a line for C to `.siblings`, with C's outbox already holding one entry;
  - `pause` / `unpause`: create or remove `$orch/.paused`;
  - `sibfail`: make `md5sum` fail, using the existing shim.

  Stream and expectations:

  ```
  ok ok               → quiet
  sibappend ok        → 1 "SIBLING B WROTE"
  sibadd ok           → no fire for C (first sight is baseline)
  pause sibappend ok  → quiet (paused)
  unpause ok          → 1 fire for B (the waited traffic)
  sibfail ok          → quiet (a failed read is not a change)
  ```

  Checks: `SIBLING B WROTE` exactly 2; `SIBLING C WROTE` 0; `OWNER WROTE` 0 in this stream (the owner half is not disturbed). The existing five-role run must stay untouched and green.

- [ ] **Step 2: Extend `self-write-suppression-check.sh` (red).** Add a case with two solo channels. A appends to **its outbox** through the real `bin/channel-append.sh` with `AIORCH_ROLE=solo`, which writes `sibling-outbox.md.self-write.solo`. Assert:
  - B's sibling-half decision, transcribed like the harness's existing `self_write_suppresses`, **fires** despite the record existing;
  - A's **owner-channel** self-write is still suppressed.

  Put a comment at the top of the new case naming what it transcribes, as the file's header asks.

- [ ] **Step 3: Run both harnesses; the sibling cases fail.**

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
bash kit/hooks/watcher-behaviour-check.sh; echo "exit=$?"
bash kit/self-write-suppression-check.sh; echo "exit=$?"
```

- [ ] **Step 4: Write the sibling half into `watcher.md`,** plus two short paragraphs after the block:
  - "A sibling's append is traffic, and your own outbox is never listed": why there is no suppression here.
  - "Paused, you do not hear siblings; they wait for the owner": the baseline freeze.

  Each refers back to the existing "A failed read is not a change" paragraph rather than restating it.

- [ ] **Step 5: Run both harnesses until `0 failures` / exit 0.** Then run the C# kit guards that read the watcher: `--filter "FullyQualifiedName~Kit"`.

- [ ] **Step 6: Commit** `feat(kit): the solo watcher hears its siblings' outboxes — per file, baseline on first sight, silent while paused`.

---

### Task 18: The gate: focused runs, one full suite, a build, and the owner's phone

**Files:**
- Create: `docs/superpowers/plans/2026-09-23-sibling-solo-sessions-report.md`

- [ ] **Step 1: The focused filters, one at a time.** Isolate any red with `--filter` and re-run it alone before believing it:

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Siblings|FullyQualifiedName~Endeavour|FullyQualifiedName~SpawnSibling|FullyQualifiedName~SiblingLaunch|FullyQualifiedName~WorkingPath|FullyQualifiedName~GitHead|FullyQualifiedName~ProgressReportBuilder|FullyQualifiedName~SiblingTurnSources|FullyQualifiedName~SoloIsToldAboutSiblings|FullyQualifiedName~SupervisionPathsSiblingFiles"
```

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
bash kit/hooks/watcher-behaviour-check.sh; echo "exit=$?"
bash kit/self-write-suppression-check.sh; echo "exit=$?"
bash kit/hooks/hook-behaviour-check.sh; echo "exit=$?"
```

- [ ] **Step 2: The build, including the WPF host** (it gets siblings "for free", and this proves it still compiles):

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -5
```

- [ ] **Step 3: The suite, once, whole, alone.** Nothing else on this machine may run a suite meanwhile. Compare the **set of names** of any failures with the known flaky families, re-run each alone twice, and name each in the report:

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-siblings
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test AIOrchestratorCoreLib.Tests 2>&1 | tail -30
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"; env -u AIORCH_ID -u AIORCH_MEMBER -u AIORCH_ROLE -u AIORCH_SUPERVISION_ROOT -u AIORCH_RUNNER dotnet test tools/claude-contract/ClaudeContract.Tests 2>&1 | tail -10
```

- [ ] **Step 4: The owner's phone checklist.** The **owner** runs this after merging and rebuilding the app. The session writes it into the report and does **not** run it: sessions never start the WPF host (git-and-boundaries). Before starting, the owner checks two things:
  - **Which binary is live** (decision 23): `Get-Process AIOrchestrator | Select Path` must be the freshly built output, not `bin\Debug - Copia\…`.
  - **Which kit is installed** (decision 17): run `kit/install.ps1`, restart the app so `KitAssets_Bootstrapper` records a fresh verdict, and start a **new** solo, because a session that was running before the restart still holds the old skill text.

  | # | do | expect |
  |---|---|---|
  | 1 | In a solo topic: *"do X in parallel, I want to follow both"* | The solo creates a worktree, appends `HANDOVER` to its outbox, and drops `spawn-sibling`. One line tells you it asked. |
  | 2 | — | **One prompt in that topic**: 🔗 … New topic / Job / Why, with **✅ Start it / ✋ Keep one session** (O1) |
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

- [ ] **Step 5: Write the report.**
  - Per task: what was built, which copy of every file was read, the commands run and their output, every engine line changed, the two mutants (Tasks 9 and 15) and which assertion caught each, and the SET OF NAMES of any red and whether it repeated alone.
  - The O1–O4 answers as the owner finally gave them, and which task changed if an answer differed.
  - The deviations from the spec: the check order (Task 6), the parent stamped after launch (Task 7), and the `unspawnable` audience (Task 8).
  - The checklist, for the owner.
  - Kit verification status: "verified against a restarted session: NOT YET — owner step 4".

- [ ] **Step 6: Commit** `docs(plan): sibling solo sessions — gate report`.

---

### Task 19: CLAUDE.md decision 27 (only on the owner's instruction)

**Do not start this task without the owner saying so in their own words** (git-and-boundaries: CLAUDE.md is edited "on the owner's instruction").

**Files:** `CLAUDE.md`

- [ ] **Step 1:** Add **decision 27, "Sibling solos"**, stating what the tree now contains:
  - the outbox model (one writer per file, never tailed);
  - membership derived from `endeavourId`;
  - the summed bar, closed siblings included;
  - `WorkingPath` on every spawn and why resume depends on it;
  - the O1–O4 answers as given;
  - the refusal table's location (`Bridge/Siblings/SiblingRequest_Validator`).
- [ ] **Step 2:** In the PAUSE bullet's waker list, add "the print runner's **sibling sources** (`TurnSources_Resolver`) and the solo watcher's sibling half". A paused sibling is dormant only if both are gated.
- [ ] **Step 3: Commit** `docs(claude): decision 27 — sibling solo sessions`.

---

## PARKED

Found while reading for this plan. **None of it traces to an owner request, so none of it is a task** (decision 22). One line each.

- The spec's own four (§10): the solo's missing PreToolUse hold hook, `Clear_AwaitingAnswer_ForDeadSession` covering the supervisor only, `Build_SessionScript` not exporting `AIORCH_SUPERVISION_ROOT`, and decision 4's `[sup → imp-2]` tags not existing in `MirrorText_Formatter`.
- `ScriptedInbound_Fake.Create_ForumTopic_Async` returns `1L` for every topic, so any existing two-orchestration test built on it routes both to one topic without knowing it. Task 7 adds an opt-in and does not audit the existing users.
- `Tap_Json` now exists twice (in `TheInboundLoopSurvivesItsOwnBatchTests` and in the new harness). Folding the old one into `TestSupport` is a tidy-up nobody asked for.
- `OrchestrationRequests_Reader.Try_ParseInto_OrReason` takes one list per request type (nine after this plan), and every `Read_*_OrNull` re-declares all nine. That is a structure that grows by one parameter per feature, and not this plan's to reshape.
- `Execute_ConfirmedClose`'s promote path has no re-validation at tap time equivalent to the one Task 9 gives siblings. It relies on `Promote_ToFullCrew`'s own guard.

---

## Self-review (run by the plan's author, 2026-09-23)

**1. Spec coverage, section by section:**

| spec § | what | task |
|---|---|---|
| §0, §1 goal / non-goals | the proposal; not a crew, not fan-out, no shared files, cap, no crew siblings | Non-goals section; 3 (cap); 14 (no crew siblings) |
| §2.1 birth | worktree + HANDOVER + request (kit); prompt; ✅ → orchestration, topic, colour, birth note, parent line | 16 (the solo's recipe); 8, 9; 7 (words); topic and colour need no code, which checklist rows 3 verify |
| §2.2 talking to each | routing unchanged; ask either; relay; never texted | no code for routing; 16 (relay prose); 12 (O3 pin) |
| §2.3 closing | close never closes another; survivors told as agent | 14 |
| §2.4 General | grouping; golden; per-topic `/progress` unchanged; `/endeavour` | 11; 12 |
| §3.1 | why a sibling is an orchestration (design rationale) | no task; the reason Task 1's model works |
| §3.2 | four fields; derived membership | 1 |
| §3.3 | outbox, one writer, not tailed, compaction sweep | 2 (path + discovery pin), 10 (sweep), 12 (engine pin), 16/17 (kit) |
| §3.4 | `.siblings`, `ENDEAVOUR.md`, derived, write on change | 10 |
| §3.5 | summed bar, closed stays in sum, parser skips sections | 11 |
| §4.1 | JSON, field rules, no model/effort/branch | 4; 5 (copied overrides) |
| §4.2 | reader JSON-only, `known` list, executor in the spawning group, refusal table, re-check at tap | 4; 6; 8; 9 |
| §4.3 | `ParkedCloseKinds.Sibling`, prompt, named execute branch, `Execute_ConfirmedSibling` steps 1–6, decline, lapse | 8; 9; 7 |
| §4.4 | idempotency | 6 (rule), 9 (engine tests) |
| §5.1 | what a sibling reads, digest caps, freshness | 10 (digest); 16 (the boundary prose) |
| §5.2 | who wakes whom | 17 (watcher); 13 (runner); no app writes to outboxes (10's step writes only derived files) |
| §5.3 | watcher sibling half | 17 |
| §5.4 | print/stream: sibling sources, source-aware trigger, pause gate, state pack | 13 |
| §5.5 | no app nudges for sibling traffic | **no code by design.** `Nudge_IdleImplementers_Async` is untouched, and no task adds a nudge. The Task 18 checklist would show a stray one. |
| §6 | worktree before asking; spawn in worktree; CLAIM/RELEASE; staging; merge per topic; HANDBACK | 16 (prose); 5 (spawn cwd) |
| §7.1 | resume from the same cwd; live check | 5 (one path, tested on three respawn routes); 18 checklist row 9 |
| §7.2 | per-topic pause, `.siblings` paused column, watcher/dispatcher ignore sibling traffic | 10; 17; 13 |
| §7.3 | usage-limit pause holds spawn requests | 8 (test) |
| §7.4 | restart re-derives, stale files removed | 10 (`AStaleFileFromACrash…`) |
| §7.5 | close post-step, no refusal, last close needs nothing | 14 |
| §7.6 | promote and `/switch` refused | 14 |
| §8.1–§8.4 | skill prose, siblings.md, watcher.md, general-supervisor | 16; 17 |
| §8.5 / O4 | per-topic holds | 15 |
| §9 | O1–O4 | 9, 3, 12, 15, each isolated (Owner decisions table) |
| §10 | risks | resume → 5 + checklist 9; ambient conflicts → 16 prose; spend → O1/O2; wake storms → 17; digest growth → 10 caps; topic confusion → 4 (name rules) + 6 (`name-taken`) + 9 (birth note) |
| §11 | test strategy | every bullet maps to a named test above. The two reader bullets and "`spawn-sibling` in the `known` list" → 4; executor bullets → 8, 9; store → 1; launcher → 5; derived → 10; progress → 11; runner → 13; lifecycle → 14 (compaction → 10); bash harnesses → 17; skill prose → 16 |
| §12 items 11, 12 | live verification; CLAUDE.md decision 27 | 18; 19 |

**2. Deviations from the spec, each deliberate and recorded in the owning task:**
- **Check order (Task 6).** `handover-already-used` comes before `at-cap` and `no-handover-entry`, so a retry after a birth that filled the cap gets the idempotent answer rather than "close a sibling".
- **Parent stamped after the launch (Task 7).** A failed launch leaves no endeavour of one. The final state is identical to spec §4.3's order.
- **`unspawnable` goes to General as Agent, not Owner (Task 8).** Decision 15: the owner cannot act on a request from an orchestration that does not exist.
- **Task 4 accepts a one-word name after the code.** The spec says "2-4 words", and `solo/SKILL.md`'s own examples include one-word names. If the owner wants 2 as a hard floor, it is one constant.

**3. Placeholder scan.** No "TBD", no "similar to Task N". Every engine test names its fake, and every command is written out in full. Five test bodies are given as named cases with their arrange/assert line in a comment rather than full C#. That is deliberate for the engine tests, whose bodies are the shared harness's four helpers, and each case's assertion is stated.

**4. Type consistency** (checked by name across tasks):
- `Format_HandoverKey` (6) is the only producer of `"<orch>#<n>"`, consumed by 7 and 9.
- `WorkingPath_Resolver.Resolve` (5) is consumed by 6, 10 and 13.
- `WorkingPath_Comparer.Are_Same` (6) is consumed by 6 and 9.
- `EndeavourMembers_Resolver.Resolve_All` / `Resolve_OpenSiblings` / `Count_Open` (1) are consumed by 6, 10, 11, 12, 13 and 14.
- `SiblingRefusals.*` (6) is consumed by 8, 9 and 14.
- `IParkedCloseRequest.Sibling` (8) is consumed by 9.
- `ISupervisionPaths.Get_SiblingOutboxFile` / `Get_SiblingsListFile` / `Get_EndeavourDigestFile` (2) are consumed by 6, 10, 12 and 13.
- `IOrchestratorConfig.Endeavour.MaxOpenSiblings` (3) is consumed by 6.
- `Start_SiblingOrchestration(parentOrchId, displayName, workingPath, bornFromHandover)` (5) is consumed by 7.

**5. Review Focus.** Five lines, each with its test named in its owning task (6, 4+10, 9, 11, 6+10). Two classes considered and left out:
- Two taps from two devices: covered by the duplicate-callback test in Task 9.
- A sibling whose repo path is itself a worktree: the child's `RepoPath` is the parent's, and `git worktree list` answers the same set from any tree of the repo, so it needs no separate case.

**6. The engine chain is the plan's critical path.** Six serial tasks in one 16 000-line file. The pure halves (validator, birth step, builders, T10a) were deliberately pulled out so that each engine task is a few call sites plus engine-level tests, and so a reviewer can reject one engine task without its neighbours.

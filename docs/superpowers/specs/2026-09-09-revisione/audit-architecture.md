# AIOrchestrator — critical architecture audit (read-only)

**Copy read:** branch source of the working checkout `/Users/nvene/Visual Studio/AIOrchestrator`,
branch `ours/integration`, HEAD `926cc6bd44a240616275e201666da80d979af79f` (2026-09-08 22:31 +0200,
"Merge stage/4a"). Not the build output, not the installed `~/.claude` copy, not any running binary
(decisions 18 / 23). No file was modified; no git write command was run.
`dotnet` is **not installed on this machine** (`which dotnet` → not found), so no build and no test
run was possible; every test number below is a static count of attributes, marked as such.
Date: 2026-09-09.

---

## 1. Measured inventory

### LOC and file counts per project

Command: `find <dir> -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l` and the same
with `-exec cat {} + | wc -l`.

| Project | .cs files | LOC |
|---|---:|---:|
| `AIOrchestratorCoreLib/` | 428 | 51,393 |
| `AIOrchestratorCoreLib.Tests/` | 297 | 52,459 |
| `AIOrchestrator/` (WPF) | 14 | 1,982 |
| `AIOrchestrator.Daemon/` | 3 | 301 |
| `tools/` (claude-contract: FakeClaude + ClaudeContract.Tests) | 17 | 2,840 |
| `kit/` | 0 (markdown + shell only) | — |
| **Total (all `.cs`, excl. obj/bin)** | **759** | **108,975** |

Test code is 1.02× production code by line count. The WPF "app" is 1.8% of the solution — this is a
**library with two thin hosts**, not a desktop app.

### CoreLib by subsystem (`find <dir> -name '*.cs' -exec cat {} + | wc -l`)

| Folder | files | LOC |
|---|---:|---:|
| `Bridge/` | 39 | 17,804 |
| `Running/` | 77 | 6,598 |
| `Telegram/` | 36 | 3,930 |
| `Planning/` | 34 | 3,276 |
| `Status/` | 24 | 2,901 |
| `Channels/` | 24 | 2,424 |
| `GeneralSupervision/` | 44 | 1,998 |
| `Sessions/` | 18 | 1,714 |
| `Kit/` | 16 | 1,435 |
| `Configuration/` | 24 | 1,208 |
| `Limits/` | 7 | 970 |
| `WindowFocus/` | 4 | 871 |
| `Tailing/` | 11 | 822 |
| others (17 folders) | — | ~3,300 |

`Bridge/` alone is 35% of CoreLib, and **13,412 of its 17,804 lines are one file**.

### 15 largest files (`find . -name '*.cs' ... -exec wc -l {} + | sort -rn | head -25`)

| LOC | Path |
|---:|---|
| 13,412 | `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` |
| 1,147 | `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` |
| 967 | `AIOrchestratorCoreLib.Tests/Status/NudgeDeciderTests.cs` *(test)* |
| 824 | `AIOrchestrator/MainWindow.xaml.cs` |
| 664 | `AIOrchestratorCoreLib.Tests/Telegram/TopicStatusLinePlannerTests.cs` *(test)* |
| 660 | `AIOrchestratorCoreLib.Tests/Bridge/OwnerAnswerSurvivesFailedSendTests.cs` *(test)* |
| 659 | `AIOrchestratorCoreLib/Telegram/TelegramApiClient/TelegramApiClientModel.cs` |
| 603 | `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` |
| 600 | `AIOrchestratorCoreLib/Status/MemberState_Resolver.cs` |
| 542 | `AIOrchestratorCoreLib.Tests/Bridge/DecisionStateSurvivesARestartTests.cs` *(test)* |
| 535 | `AIOrchestratorCoreLib.Tests/Status/MemberStateResolverTests.cs` *(test)* |
| 534 | `AIOrchestratorCoreLib.Tests/Bridge/HighRiskAndDeadlineProbeTests.cs` *(test)* |
| 530 | `AIOrchestratorCoreLib.Tests/Telegram/TopicStatusLineBuilderTests.cs` *(test)* |
| 529 | `AIOrchestratorCoreLib/Channels/ChannelFile_Lock.cs` |
| 508 | `AIOrchestratorCoreLib/Telegram/TelegramHtml_Renderer.cs` |

Largest *production* files after the top two: `TelegramApiClientModel` 659, `OrchestrationLauncherModel`
603, `MemberState_Resolver` 600, `ChannelFile_Lock` 529, `TelegramHtml_Renderer` 508,
`EngineState_Serializer` 456. Note the **distribution is healthy apart from one outlier**: median
production file is well under 150 LOC. This is not a codebase of god-classes — it is a codebase with
*one* god-class.

### Tests

`dotnet` is absent, so `dotnet test` could not run. Static counts
(`grep -rho '\[Fact\]' | wc -l`, etc., over `AIOrchestratorCoreLib.Tests`):

- `[Fact]` = **1,881**
- `[Theory]` = **169**
- `[InlineData(...)]` = **666**
- Test files matching `*Tests.cs` = **290**
- Approximate executable cases = 1,881 + 666 = **~2,547** [estimate — assumes each `Theory` is
  driven only by `InlineData`; `MemberData`/`ClassData` cases are not counted]

Per-area test counts (`grep -rho '\[Fact\]\|\[Theory\]'` per folder):
Bridge 376 · Status 268 · Telegram 234 · Planning 202 · Running 178 · Channels 145 · Kit 122 ·
GeneralSupervision 104 · Sessions 71 · Limits 55 · Configuration 44 · Mirroring 40 · Tailing 35 ·
Formatting 28 · Spawning 23 · Launching 22 · Composition 16 · WindowFocus 14 · Logging 14 ·
Imaging 12 · Usage 7 · Translation 7 · Build 5 · Processes 4 · Storage 4 · Git 3 · Layout 8.
Plus `tools/claude-contract` (a separate xunit project: FakeClaude stub tests + `LiveFact` tests
against the real CLI).

`AIOrchestrator/` (WPF) and `AIOrchestrator.Daemon/` have **zero tests of their own**; the daemon's
composition is covered indirectly by `Tests/Composition/` (16 cases).

### Types

Commands: `grep -rhoE '^\s*(public|internal)\s+(sealed\s+)?interface\s+\w+'` etc. over `AIOrchestratorCoreLib`.

- interfaces: **68**
- classes: **344**
- records: **15**

Interface-to-class ratio 1:5. The `Xxx_Yyy` / `IFoo` + `FooModel` + `Foo_Factory` triple pattern is
followed consistently: `SupervisionPathsModel`, `ChannelTailerModel`, `BridgeEngineModel`,
`PrintTurnDispatcherModel` all sit behind an interface with a factory. Most of the 344 classes are
static single-purpose policy/parser classes (`PlanLedger_Parser`, `TelegramMessage_Chunker`,
`DispatchPause_Gate`, `Nudge_Decider`, …) — which is why the test count is so high and the median
file so small.

### God-classes

**`AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — 13,412 LOC, 1 class,
~282 members** (`grep -cE '^    [A-Za-z].*\('` = 298 lines, of which 16 are fields/records →
**282 methods/properties**), 111 `readonly`/`const` members, 97 mutable `_`-prefixed fields,
36 `const` tuning numbers, 109 `try` and 190 `catch` sites, 39 direct `File.` calls, 15 constructor
dependencies (primary constructor), 8 separate `Lock` objects
(`_buttonLock`, `_closeConfirmationLock`, `_stateLock`, `_deliveryLock`, `_receiptLock`,
`_knownMessageIdsLock`, `_ownerStateLock`, plus `_topicNameSyncGate` SemaphoreSlim).

Distinct responsibilities I can name in it (each is a separately-nameable concern, cited by member):

1. **Loop supervision / self-healing** — `Run_Async`, `Run_Supervised_Async`, `Alert_LoopAbandoned_BestEffort_Async`
2. **Mirror tick orchestration** (~35 ordered steps) — `Execute_MirrorTick_Async`
3. **Channel tailing → Telegram mirroring** — `Mirror_Append_Async`, `Send_MirrorPiece_Async`, `Select_MirrorableEntries`
4. **Telegram inbound long-poll + 29-branch bot-command router** — `Run_InboundLoop_Async`
5. **Owner message routing / aggregation / delivery buffer** — `Route_OwnerMessage_Async`, `Flush_OwnerDeliveries_Async`, `Deliver_OwnerMessage_Async`
6. **Request-file execution (9 actions)** — `Process_PendingRequests` and its 8 `Process_*Requests`
7. **Close/promote confirmation state machine with Telegram buttons** — `Ask_OwnerToConfirmClose_Async`, `Resolve_CloseConfirmationTap_Async`, `Execute_ConfirmedClose`, `Decline_CloseConfirmation`, `Expire_CloseConfirmation`
8. **Inline-button registry + callback tap dispatch** — `Register_Buttons`, `Handle_CallbackTap_Async`, `Try_HandleHoldTap_Async`, `Try_HandleTopicCommandTap_Async`
9. **High-risk question confirmation (read-back digits)** — `Begin_HighRiskConfirmation_Async`, `Try_CompleteHighRiskConfirmation_Async`
10. **Question deadline scheduler** — `Resolve_QuestionDeadlines_Async`, `Remind_AboutQuestion_Async`, `Close_QuestionOnDeadline_Async`
11. **Nudging / stall / crash-loop / silent-deadlock alerting** — `Send_StallAlerts_Async`, `Nudge_IdleImplementers_Async`, `Nudge_IdleSupervisor`, `Send_CrashLoopAlerts_Async`, `Break_SilentDeadlock_Async`
12. **Usage-limit + budget alerting and dispatch pausing** — `Check_UsageLimits_Async`, `Send_BudgetAlerts_Async`, `Update_DispatchPause_Async`, `Read_CurrentLimitWindows`
13. **Cost / token / context / git / limits report rendering (13 `/commands`)** — `Build_CostReportText`, `Build_TokensReportText`, `Build_ContextReportText`, `Build_GitReportText`, `Build_LimitsReportText`, `Build_ProgressReportText`, `Build_TaskListText`, `Build_TurnLogText`, `Build_ImplementerPeekText`, `Build_MemberStatusText`
14. **PLAN.md ledger health, shape policing and plan-backend sync** — `Check_LedgerHealth_Async`, `Report_LedgerShape`, `Report_StaleInProgress`, `Start_PlanBackendPass`, `Sync_PlanBackends`, `Refresh_ProgressArtefacts`
15. **Topic lifecycle: create/name/rename/delete/pin/status-line** — `Resolve_ThreadId_OrNull_Async`, `Sync_TopicNames_Inside_Gate_Async`, `Refresh_TopicStatusLines_Async`, `Delete_TelegramTopic_FireAndForget`, `Clear_Topic_Async`
16. **Presence / away / quiet / DND / silence-all mode machine** — `Check_AwayMode_Async`, `Enter_AwayMode_Async`, `Exit_AwayMode_Async`, `Enter_QuietMode_Async`, `Set_TelegramMuted`, `Set_SilenceAllTopics`, `Apply_PresenceCommand_Async`
17. **"Busy supervisor" narration / typing bubble / delivery receipts** — `Narrate_BusySupervisor_Async`, `Announce_SupervisorFree_Async`, `Show_Typing_BestEffort_Async`, `Publish_DeliveryReceipt_Async`
18. **Channel writing on behalf of the app (`FROM app`)** — `Append_AppEntry_Safe`, `Append_OwnerEntry_Safe`, `Announce`, `Drain_PendingAnnouncements`
19. **Channel shape/index policing and baselining** — `Check_ChannelShapes_Async`, `Screen_ChannelIndexSequences`, `Baseline_UnseenChannels_Silently`, `Coach_OnContractFaults`, `Refuse_Question`
20. **Compaction trigger and state persistence** — `Compact_LongChannels`, `Persist_BridgeState`, `Persist_EngineState`
21. **Session window control (screenshots, focus, organise, merge prompts)** — `Show_SessionWindow_Async`, `Send_SessionScreenshot_Async`, `Organize_SessionWindows_Async`, `Ask_SessionToMerge_Async`
22. **Voice transcription + photo/attachment ingestion** — `Build_VoiceEntryText_OrNull_Async`, `Build_PhotoEntryText_Async`, `Approve_OwnerFile`, `Send_EntryPhoto_BestEffort_Async`
23. **Translation layer toggle (Italian)** — `Set_ItalianLayer`, `Toggle_ItalianLayer_Async`, `Translate_LedgerText_Async`
24. **Orchestration shape switching / resume-all / done-toggle** — `Switch_OrchestrationShape_Async`, `Resume_AllSessions_Async`, `Toggle_Done_Async`, `Toggle_AwaitingTest_Async`

That is **24 distinct responsibilities in one class**. By the repo's own `CODING_PATTERNS`
(`IFoo`/`FooModel`/`Foo_Factory`, one concern per type) it is a standing, acknowledged violation.

**Second-order god-class:** `PrintTurnDispatcherModel` (1,147 LOC, ~35 members) mixes discovery,
scheduling, per-orchestration slot semaphores, turn execution, retry ladder, cursor advancement,
channel reply writing, failure reporting and shutdown draining — 8 responsibilities. Notably it does
this without any of the 24 above, so it is coherent by comparison.

**Third:** `AIOrchestrator/MainWindow.xaml.cs` (824 LOC) is the WPF code-behind doing card building,
timer refresh, request emission and dialog hosting — but it is UI-relaxed by project rule and small
in absolute terms.

---

## 2. Component map — the runtime architecture as the code has it

### Hosts

Two hosts build **the same graph** via `Composition/OrchestratorServices/OrchestratorServices_Factory.Create`
(config provider → log → session store → spawner → plugin gate → launcher → bridge engine):

- `AIOrchestrator/App.xaml.cs` (WPF, `net10.0-windows`, `WinExe`) — window + engine on a background task.
- `AIOrchestrator.Daemon/BridgeHost_Service.cs` (`net10.0`, `BackgroundService`) — headless, systemd
  `Type=notify` (`WATCHDOG=1` ping in `Keep_SystemdWatchdogFed_Async`), launchd KeepAlive, Windows SCM.
  Explicitly sets `Environment.ExitCode = 1` when the engine faults, because a faulted
  `BackgroundService` otherwise exits 0 and no service manager restarts it — a genuinely well-reasoned
  detail, documented in the class comment with the measurement date.

Mutual exclusion is `Composition/SingleInstance_Guard.Try_Acquire` — an exclusive `FileStream`
(`FileShare.None`) on `<root>/.instance.lock`, replacing the WPF named mutex. Its own doc comment
admits the honest gap: **the lock is per supervision root, so two hosts on different `--root` values
sharing one bot token will both long-poll** and nothing detects it.

### The three main loops

1. **Mirror loop** — `BridgeEngineModel.Run_MirrorLoop_Async` → `Execute_MirrorTick_Async`, every
   `MIRROR_TICK_MILLISECONDS = 2000`. One `Task`. This single method performs ~35 ordered steps:
   dispatch-pause update → request-file execution → session watchdog → **print-turn dispatcher tick** →
   meeting-flag sync → owner delivery flush → announcement drain → close-confirmation expiry →
   parked-member release → channel baselining → question deadlines → progress artefacts → plan-backend
   pass → **[DND gate: `return` if muted]** → close confirmations → crash/stall/budget alerts → idle
   nudges → pending owner replies → topic status lines → idle flags → guard report → `Find_ActiveChannels`
   → `_tailer.Poll` → per-append `Mirror_Append_Async` → usage limits → topic names → ledger health →
   channel shapes → awaiting-answer expiry → silent deadlock → away mode → periodic status → general
   dashboard → general topic name → `Compact_LongChannels` → `Persist_BridgeState`.
   The step *order* is load-bearing and is defended only by long comments ("Moving this call below the
   return seven lines down compiles, passes every test, and quietly reintroduces exactly the bug").
2. **Inbound loop** — `Run_InboundLoop_Async`, only when a Telegram client exists.
   `getUpdates` long-poll, `INBOUND_LONG_POLL_SECONDS = 20`, exponential backoff 5 s → 60 s
   (`INBOUND_ERROR_BACKOFF_START/MAX_MILLISECONDS`). Contains a **29-branch `if/else if (command == …)`
   bot-command router** inline (`grep -c 'command == '` over lines 5740–5980 = 29).
3. **Print-turn dispatcher** — `PrintTurnDispatcherModel.Tick(DateTime)`, *called from* the mirror
   tick, not its own loop. Non-blocking: it discovers `print-session.json` registrations, reads
   pending channel entries per source, and starts `Execute_Turn_Async` on background tasks, one
   in-flight per `orchId/memberId` key (`_inFlight`), throttled per orchestration by
   `Get_OrchestrationSlots` (`SemaphoreSlim`).

Both loops are wrapped in `Run_Supervised_Async`, which relaunches a loop that *returns* (a real
outage on 2026-08-11: an `HttpClient.Timeout` throws `TaskCanceledException`, the bare
`catch (OperationCanceledException)` read it as shutdown and returned, and the bridge became a live
process with a dead bridge for hours). The fix — `when (cancellationToken.IsCancellationRequested)`
filters plus a relaunch cap (`LOOP_RELAUNCH_CAP`, `LOOP_HEALTHY_RUN_MILLISECONDS = 30000`) and a
Telegram alert on abandonment — is one of the better pieces of engineering in the repo.

- **Session watchdog** — `Watchdog/SessionWatchdog/SessionWatchdogModel.Check_AndRestart_DeadSessions()`,
  called from the mirror tick (so effectively every 2 s), skipped while dispatch is paused.
- **Stream runner** — `Running/StreamTurn/*` (694 LOC): `StreamTurnCommand_Builder` +
  `StreamSessionProcess`, a resident `--input-format stream-json --output-format stream-json --verbose
  --include-hook-events` process, selected through `Running/RunnerFallback_Ladder.cs`.

### Threads / tasks

`Run_Async` starts 1–2 supervised loop `Task`s and awaits `Task.WhenAll`. Everything else is
in-line on those two tasks, except: print turns (`Task.Run` per turn), several
`*_FireAndForget` calls (`Remove_TopicCreationPin_FireAndForget`, `Delete_TelegramTopic_FireAndForget`),
`Start_PlanBackendPass` (off-thread) and the WPF dispatcher timer. There is **no work queue and no
scheduler** — cadence is "which tick am I on", and per-concern intervals are enforced by remembered
timestamps inside the god class (`_mirrorRetryLastAttemptUtc`, `Last_PeriodicStatusSlot_OrNull`,
`LIMIT_CHECK_INTERVAL_SECONDS = 60`, `TOPIC_NAME_REVALIDATE_MINUTES = 5`, …).

### Communication between components

- **Agent → app:** JSON files dropped in `<root>/.requests/*.json`, read by
  `GeneralSupervision/OrchestrationRequests_Reader.Read_Pending` on every 2 s tick. 9 actions
  (`start-orchestration`, `add-implementer`, `add-reviewer`, `close-implementer`,
  `promote-orchestration`, `close-orchestration`, `set-telegram-muted`, `set-orchestration-name`,
  `set-model`).
- **App → agent:** an appended `FROM app` channel entry (`Append_AppEntry_Safe` →
  `Channels/ChannelAppender`), which wakes the agent's own background file watcher.
- **Agent ↔ agent:** never direct. Supervisor ↔ member happens by both appending to
  `imp-<n>/channel.md`.
- **Owner ↔ supervisor:** Telegram topic ⇄ `owner-channel.md`, bridged both ways.
- **App internals:** direct method calls + a handful of C# events (`IOrchestrationLog.EntryLogged`,
  `IBridgeEngine.MutedChanged` / `SilenceAllChanged` / `ItalianLayerChanged`).

### Persistence — which file is truth

| File | Written by | Truth for |
|---|---|---|
| `<root>/config.json` | owner, agents, UI | repos, models, runner config, guardrails, Italian layer (persisted, decision 11) |
| `<root>/secrets.json` | installer/owner | bot token; read **once at startup** (`BridgeEngine_Factory.Create`) — token change needs a restart |
| `<root>/.bridge-state.json` | `Bridge/BridgeState_Store.Save` from `Persist_BridgeState` | per-file tail byte offsets + `lastUpdateId` |
| `<root>/.engine-state.json` | `Bridge/EngineState/EngineState_Serializer` | pending buttons, open questions, nudge memory, respawn counters |
| `<root>/.limit-alerts.json`, `.general-dashboard.json` | engine | alert dedup thresholds, dashboard message id |
| `<root>/.instance.lock` | `SingleInstance_Guard` | one host per root |
| `<root>/general/channel.md`, `<orch>/owner-channel.md`, `<orch>/<member>/channel.md` | **agents and the app** | **the conversation — the system of record** |
| `…/*.archive.md` | `Channels/Channel_Compactor` | entries beyond the recent window (live file is not monotonic — decision 13) |
| `…/*.lock` (directory) | `Channels/ChannelFile_Lock` | cross-process write lock (mkdir/move exclusivity, `STALE_SECONDS = 60`) |
| `<orch>/session.json` | `Sessions/OrchestrationSessionStore` | repo path, member roster, topic id, `ClosedUtc`, per-orch model overrides, mode |
| `<orch>/PLAN.md` | supervisor (agent) | the task ledger; parsed by `Planning/PlanLedger_Parser` |
| `<orch>/.progress.json`, `.plan-backend.json` | engine | ledger snapshot for the statusline / backend sync cursor |
| `<orch>/orchestrator.log.jsonl`, `<root>/orchestrator-global.log.jsonl` | `Logging/OrchestrationLog` | structured event log (also the hook advisory channel, decision 21) |
| `…/print-session.json` | `Running/PrintSessionState/PrintSessionState_Store` | which sessions are bridge-driven, transcript session id, per-source cursors |
| `…/turns.jsonl` | `Running/TurnLog` | per-turn record (request id, attempt, outcome, cost) |
| `…/.usage.json`, `.communicator.usage.json`, `.usage-lifetime.json`, `.limits.usage.json` | **the Claude Code status-line script**, scraped by `Usage/UsageTotals_Reader` | cost, tokens, limit windows |
| `…/.pid`, `.supervisor.pid`, `.communicator.pid` | the spawned shell itself | liveness for the watchdog / `Termination/SessionTerminator` |

Only `Storage/Atomic_FileWriter.Write_AllText` (temp + `File.Move(overwrite:true)`) is used for
whole-file replacement, and it states honestly that it does **not** fsync.

### Where the Claude CLI is invoked, and with which flags

Four call sites:

1. **`Running/PrintTurnCommand_Builder.Build_Arguments`** — the headless print runner:
   `-p --output-format json --name <orchId-memberId|general> (--session-id | --resume) <uuid>
   [--model <m>] [--settings <file>] (--permission-mode <m> | --dangerously-skip-permissions)
   [--disallowedTools Write Edit NotebookEdit --] [<role command>]`.
   The role command is passed **positionally on the first turn only**; later turns resume the
   transcript and take their prompt **on stdin** — "so no channel text ever meets a shell", which is
   the right call. `--settings` is re-passed every turn because `--resume` does not restore it
   (comment cites `MEASUREMENTS.md`). No `--max-budget-usd` anywhere in the tree
   (`grep -rn '--max-budget'` → no hits): budget control is the app's own
   `Send_BudgetAlerts_Async` / `DispatchPause_Gate`, not a CLI flag.
2. **`Running/StreamTurn/StreamTurnCommand_Builder`** — resident stream session:
   `-p --input-format stream-json --output-format stream-json --verbose --include-hook-events
   --name … (--session-id|--resume) … [--model] [--settings] (--permission-mode|--dangerously-skip-permissions)
   [--disallowedTools … --]`.
3. **`Spawning/SpawnCommand_Builder`** — the terminal spawn; `CLAUDE_LAUNCH_FLAGS =
   "--dangerously-skip-permissions"` plus `--model <m>`, wrapped in `wt new-tab` / `Start-Process`.
4. **`Translation/MessageTranslator/MessageTranslatorModel`** — `claude -p --model <model>` shelled
   out through `Processes/ShellCommand_Builder.Build_StartInfo` for the Italian layer.

**Env scrubbing** (`Running/PrintTurnRunner/PrintTurnRunnerModel:116` and
`Running/StreamTurn/StreamSessionProcess:118`): every `CLAUDECODE*` / `CLAUDE_CODE_*` variable is
removed from the child so a bridge-spawned session does not inherit the host's Claude Code identity.
`AIORCH_ROLE` / `AIORCH_ID` / `AIORCH_MEMBER` / `AIORCH_RUNNER` / `AIORCH_SUPERVISION_ROOT` /
`AIORCH_CLAUDE_HOME` are the six variables passed in. `BridgeHost_Service` sets
`AIORCH_SUPERVISION_ROOT` **on the host process itself** so both runners inherit it — a fix dated
2026-09-06 after a print-run implementer read the wrong root and burned a whole turn.

Sandboxing: `Running/SessionSandbox/MemoryLimitedInvocation_Builder` wraps the CLI in
`systemd-run --user --scope -p MemoryMax=… -p MemorySwapMax=…` when `SystemdRun_Probe` says it is
available — Linux only, probed at runtime rather than assumed.

### Where spec and code diverge

The design spec (`docs/superpowers/specs/2026-08-06-ai-orchestrator-design.md`, 197 lines, approved
2026-08-06) and `CLAUDE.md` are both **materially behind the code**:

1. **The spec has no headless runner at all.** It describes only terminal spawning ("The app spawns
   sessions in real terminals"). The entire `Running/` tree (77 files, 6,598 LOC — print runner,
   stream runner, turn executor, fallback ladder, sandbox, turn log) post-dates it and is the
   *primary* execution path now. Nothing in the spec covers turn cursors, drain-on-stop, or
   `print-session.json`.
2. **The spec has no daemon.** `AIOrchestrator.Daemon` and cross-platform hosting are absent; the
   spec's error-handling section says "App closed → protocol unaffected" and nothing about services.
3. **Kit delivery changed shape.** Spec §3 says role commands live in `kit/commands/` and are
   installed to `~/.claude/commands/`; `CLAUDE.md` decision 17 and 23 repeat this and describe
   `KitAssets_Installer` overwriting `~/.claude/commands` at every startup. **On disk at HEAD there
   is no `kit/commands/`** — it is `kit/skills/{supervisor,implementer,general-supervisor,solo,
   reviewer,communicator,subagents}/SKILL.md` plus `kit/.claude-plugin/`, and `AIOrchestrator.csproj`
   states "The app no longer copies any of it into `~/.claude` — it only checks the version that is
   installed there" (`Kit/KitAssets_Bootstrapper` + `Kit/PluginGate`). **`CLAUDE.md` decisions 17
   and 23 are stale on their central claim.** A reader following them will look for the wrong files.
4. **Roles the spec never mentions:** `solo`, `reviewer`, `communicator` (with its own pid and usage
   files), and the `basic` vs `full` orchestration mode with a `promote-orchestration` request.
   The spec's request table lists 4 actions; the code has 9.
5. **Single-instance mechanism changed** from the spec's named mutex to a per-root file lock, with a
   narrower guarantee (see above).
6. **Mute semantics moved.** The spec's DND freezes tailing; the code still does, but now with
   ~14 steps deliberately hoisted *above* the gate, each justified in a comment. That is a
   substantially different policy than "the engine skips tailing entirely".
7. **`CLAUDE.md`'s "Conventions" still says the repo root is
   `C:\Users\Gianpiero\source\repos\AIOrchestrator`** and prescribes PowerShell commit workflow, in a
   repo that now ships a Linux/macOS daemon and is checked out on macOS.
8. Spec §Testing says Telegram HTTP and process spawning "are exercised by a documented manual
   end-to-end script per machine". In the code they are exercised by `tools/claude-contract`
   (FakeClaude + `LiveFact`) for the CLI, and by engine-level test seams
   (`BridgeEngine_Factory.Create_WithTelegramClient` / `_WithTelegramClientAndTranslator` /
   `_WithDecisionState`) for Telegram. Better than the spec; the spec was never updated.


---

## 3. Architectural critique

Severity key: **HIGH** = can lose the owner's data or silently stop the system; **MED** = costs
correctness or maintainability in a way that has already bitten or will; **LOW** = real but bounded.

### 3.1 Coupling and god-classes

**HIGH — `Bridge/BridgeEngine/BridgeEngineModel` is a 13,412-line, 24-responsibility, 15-dependency
class, and it is the whole system.** Everything the product does that is not a pure policy function
happens in this one type. Concretely:

- `BridgeEngineModel:Execute_MirrorTick_Async` is ~190 lines of 35 sequential steps whose *order* is
  the specification. There is no declaration of that order anywhere — no pipeline, no ordered list of
  named stages, no test that asserts step N precedes step M. The order is asserted by prose comments
  ("**NOTHING IN THE SUITE PINS THIS LINE'S POSITION** … Moving this call below the return seven
  lines down compiles, passes every test, and quietly reintroduces exactly the bug described
  above"). That comment is an admission that the most important invariant in the system is untested
  and untestable in its current shape.
- 97 mutable fields + 8 lock objects in one type means **there is no unit of state small enough to
  reason about**. `Persist_EngineState` has to take three locks in a fixed order and says so.
- The class is `internal sealed`, and the repo has "twice refused `InternalsVisibleTo`", so testing
  it required adding four public factory overloads as test seams
  (`BridgeEngine_Factory.Create_WithTelegramClient`, `_WithTelegramClientAndTranslator`,
  `_WithDecisionState`). That is the design telling you the class is too big: the *only* way to
  observe its behaviour is to construct the whole engine.
- **312 of the repo's 675 commits touch this one file** — 46%
  (`git log --oneline -- AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs | wc -l` = 312;
  `git rev-list --count HEAD` = 675; 326 commits touch `Bridge/` at all, so nearly every Bridge
  commit is a commit to this file). Every feature and every fix lands in the same place. Merge
  conflicts between `stage/*` branches are structurally guaranteed.

**MED — feature routing is a 29-branch `if/else if` chain inside the network loop.**
`BridgeEngineModel:Run_InboundLoop_Async` inlines the whole bot-command surface
(`/dnd`, `/summary`, `/pending`, `/progress`, `/left`, `/tasks`, `/tokens`, `/cost`, `/italian`,
`/limits`, `/context`, `/git`, `/pc`, `/screens`, `/clear`, `/turnlog`, `/peek`, `/resume`,
`/status`, `/done`, `/close`, …). Each new command adds a branch to a method that is also the
long-poll error/backoff loop. A command table (`IReadOnlyDictionary<string, IOwnerCommand>`) would
be a mechanical extraction with no behaviour change and would make each command independently
testable.

**Counter-point, stated because it is real:** everything *around* the god class is unusually well
factored. `Planning/PlanLedger_Parser`, `Telegram/TokenBucket_Gate`, `Telegram/TelegramMessage_Chunker`,
`Limits/DispatchPause_Gate`, `Status/Nudge_Decider`, `Channels/ChannelFile_Lock`,
`Storage/Atomic_FileWriter` are small, pure, single-purpose and heavily tested. The problem is not
"this team writes god classes"; it is that **one class was never split and now absorbs everything**.

### 3.2 Concurrency model

**MED — the whole system runs on two tasks and a 2-second poll, and everything competes for one
tick.** `MIRROR_TICK_MILLISECONDS = 2000`. In that one tick the app does request-file execution,
watchdog respawns, print-turn dispatch, ~14 pre-mute steps, tailing of every channel, N Telegram
sends, usage-file scanning, topic renames, ledger parsing, channel-shape validation, compaction and
two state persists. `BridgeEngineModel:Execute_MirrorTick_Async` opens
`ChannelWrite_Lock.Open_TickAllowance(1500 ms)` precisely because the tick's worst case used to be
"appends × per-call budget" and "ten members could spend ~15 s of waiting inside a 2 s loop". That
fix caps the damage; it does not remove the structural problem, which is that **a slow Telegram
endpoint delays session respawns, request execution and ledger parsing**, because they are all
statements in one method.

**HIGH — `_lastUpdateId` is written on the inbound loop outside any lock and read on the mirror loop
inside `_stateLock`.** `BridgeEngineModel:5949` (`_lastUpdateId = batch.MaxUpdateId.Value;`) versus
`BridgeEngineModel:Persist_BridgeState` (`lock (_stateLock) { BridgeState_Store.Save(…, _lastUpdateId) }`).
The field is a plain `long`, not `volatile`, not `Interlocked`. On x64 the read will not tear in
practice, but there is **no memory barrier**, so the mirror loop can persist a stale offset and a
restart replays already-consumed Telegram updates — i.e. the owner's messages appended to a channel
twice. The lock gives the appearance of protection without providing it.

**HIGH (durability, see 3.5) — the getUpdates offset is acked before the owner's words are durable.**

**MED — three writers touch channel files and only two of them are provably serialised.**
`Channels/ChannelWrite_Lock` (in-process `Lock` per path) wraps `Channels/ChannelFile_Lock`
(cross-process lock *directory*, `mkdir`/`Directory.Move` exclusivity, `STALE_SECONDS = 60`), and
both `Channels/ChannelAppender` and `Channels/Channel_Compactor` take them. The doc comments are
admirably honest that this "binds writers that ASK" and that a session appending with a bare `>>`
redirect is unbound — and per CLAUDE.md decision 21 that is the correct posture. But the
tailer/compactor interaction remains the sharpest edge in the codebase: `ChannelTailer.Has_UndeliveredEntries`
exists purely to stop compaction rewriting a file whose bytes the tailer has read but not delivered,
and its own comment records **three successive silent-loss bugs** in that one predicate
(Unconfirmed-only, Pending-not-counted, bytes-past-cursor). A design where the guard needs a
three-clause proof and a "when it cannot tell, it refuses" escape hatch is a design that is fighting
its substrate.

**LOW — `ChannelFile_Lock` is not reentrant and cannot be**, documented ("A nested acquire on the
same channel does not deadlock outright — it burns its whole budget and returns false — but the
symptom is a mysterious failed write"). A 1,500 ms silent-failure-on-nesting primitive used from ~35
call sites in a 13k-line class is a trap, mitigated only by discipline.

**MED — atomic writes are partial.** `Storage/Atomic_FileWriter.Write_AllText` (temp + `File.Move`)
covers whole-file replacement and explicitly does not fsync. But `ChannelAppender:Append_Entry` uses
raw `File.AppendAllText` — an interrupted append leaves a half-written entry in the append-only
truth file, which the tailer will then either hold (no trailing newline → `HeldTrailingEntryFiles`,
never mirrored until the next append) or mis-parse. There is no per-entry checksum or terminator.

**LOW — `.bridge-state.json` is rewritten every tick**, ~30 times a minute (the comment in
`Persist_EngineState` says exactly this), each write being a temp-file create + rename. ~43k
file creates/day for a cursor. Works; it is also why the 2026-08-11 full-disk incident was so
dangerous.

### 3.3 Markdown channel files as both transport and state

This is the central architectural bet and it is **half right**.

What it buys, genuinely: the protocol works with the app down; the whole history is human-readable
and greppable; agents need no client library, just Read/Write and a `mkdir` lock; the audit trail is
the transport, so there is no reconciliation problem between "what happened" and "what was logged".
Those are large wins and the spec's "the file protocol IS the system" is a defensible principle.

What it costs:

**HIGH — the entry parser is line-oriented with no fence awareness.**
`Channels/ChannelEntry_Parser.Parse_All` splits on `\n` and treats any line matching
`^##\s*\[(\d+)\]\s*FROM\s+(\S+)` as a new entry. There is **no tracking of ``` fenced blocks**
(`grep -n 'fence\|```' ChannelEntry_Parser.cs` → only prose hits). A supervisor quoting an
implementer's channel entry — which the role skills actively encourage, and which is the single most
natural thing for these agents to do — **splits one entry into two phantom entries** with a
duplicate index. Downstream that corrupts: the Telegram mirror (two messages, wrong attribution),
`Channels/ChannelIndexSequence_Screen` (a duplicate-index alert about an offence nobody committed —
which is exactly the `option-lab-2` "two `[80]` and two `[81]`" incident CLAUDE.md decision 12
records), `Status/MemberState_Resolver` (state is derived from the *last* entry's author, so a quoted
`FROM implementer` line inside a supervisor's brief flips the member to "awaiting review"), and
`ChannelEntry_Parser.Get_NextIndex` (numbering off by the phantoms). **This is not a hypothetical:
decision 12 in CLAUDE.md describes the symptom and attributes it to agent carelessness. Fence-blind
parsing is at least as likely a cause.**

**MED — `int.Parse` on an unbounded `(\d+)` capture poisons a channel permanently.**
`ChannelEntry_Parser:Build_Entry` does `int.Parse(header.Groups[1].Value)` where the regex is
`(\d+)` with no length bound. A header like `## [99999999999999999999] FROM x` throws
`OverflowException`. Trace it: `ChannelTailerModel:Extract_CompleteEntries` calls `Parse_All` **after**
`state.Offset` has already been advanced (`state.Offset += byteCount`) and **before**
`state.Pending.Clear()`. The throw is caught per-channel in `ChannelTailerModel:Poll_AllChannels`
(good) and reported as `unreadableFiles` — but `Pending` still holds the poison text, so the same
throw recurs **every 2 seconds forever**. That channel never mirrors again, its `Pending` buffer
grows unboundedly, and the only symptom is one log warning per tick. The same parse is used by
`ChannelAppender:Append_Entry` via `Get_NextIndex`, so **nothing can append to that channel either**
— including the app's own `FROM app` error report about it.

**MED — agent-written headers are untrusted input by design, and the code knows it but only
defends some fields.** CLAUDE.md decision 12 is explicit: `[n]` and the timestamp are guesses.
The mitigations present: `Get_NextIndex` uses `Max(index)+1` not the tail; `Parse_Author` strips
markdown decoration (labelled *speculative* in its own docstring — an honest habit worth
preserving); `Describe_SinceStamp_OrNull` returns null for a future stamp; `ChannelIndexSequence_Screen`
screens duplicate indices. What is **not** defended: the date field is a free-form string
(`Split_DateAndSubject` returns `dateText` unparsed), the index is unbounded, the subject is
unbounded, and the author word falls to `ChannelAuthors.Unknown` with no alert. For print-run
sessions this is solved properly — `ChannelAppender.Append_SessionEntry` has the *bridge* write the
header on the member's behalf, which the docstring correctly notes "makes decision 12 moot". **That
is the right answer and it should be the only answer**: no agent should ever write a header.

**MED — compaction breaks monotonicity, and the codebase has already paid for it twice.**
`Channels/Channel_Compactor` (`COMPACT_ABOVE_ENTRIES = 90`, `KEEP_RECENT_ENTRIES = 45`) moves old
entries to `*.archive.md`. Consequences the code documents: (a) a live-file entry count is not
monotonic, which silently broke "has the supervisor answered the owner yet"
(CLAUDE.md decision 13; the fix is `ChannelHistory_Counter.Count_Entries_ByAuthor` spanning live +
archive — but *every* reader must remember to use it, and nothing enforces that);
(b) `PrintTurnDispatcherModel:Warn_IfEntriesWereArchivedUndelivered` exists because a turn's pending
entries can be archived out from under its cursor. Byte offsets into a file that gets rewritten is
the wrong cursor abstraction; the right one is a monotonic entry id.

**LOW — the offset/quiet-poll completeness heuristic.** An entry is "complete" when the next header
appears **or** after `QUIET_POLLS_TO_FLUSH = 2` quiet polls **and** the file ends in `\n`. So the
last entry of a conversation is mirrored ~4 seconds late, and if the writer did not terminate the
line it is **never** mirrored until someone else appends (`HeldTrailingEntryFiles`, warned once per
spell). The reasoning for not flushing anyway is sound; the fact that a trailing-newline convention
is load-bearing is the problem.

### 3.4 Error handling and observability

This is the **strongest** part of the codebase. Notable:

- `BridgeEngineModel:Run_Supervised_Async` relaunches a loop that returns *or* faults, with
  `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` so an
  `HttpClient` timeout is no longer read as shutdown. The docstring names the date, the duration and
  the blast radius of the outage it fixes. `Alert_LoopAbandoned_BestEffort_Async` texts the owner
  when the loop is given up on.
- `Daemon/BridgeHost_Service` forces `ExitCode = 1` on engine fault because a faulted
  `BackgroundService` exits 0 and no service manager restarts it (measured on Hosting 10.0.11).
- `Channels/ChannelLock_Diagnostics.Set_Sink` is wired in `Run_Async` so lock failures reach
  `orchestrator.log.jsonl` instead of being silent.
- Log de-duplication where a per-tick error would bury the log
  (`PrintTurnDispatcherModel:_warnedBrokenSessions`, `_warnedStaleRegistrations`,
  `_heldTrailingEntryFiles`).
- Decision 21's rule ("a hook that cannot evaluate its predicate SAYS SO and ALLOWS") is applied
  in code, e.g. `ChannelTailerModel:Has_BytesPastCursor`'s `unevaluableReason`.

Weaknesses:

**HIGH — `ChannelAppender` returns "did I write?" and most callers throw it away.** Its own
docstring: *"Today most call sites in the bridge ignore it, which is a known gap recorded with this
change rather than papered over: the entry is dropped and nothing says so."* With 1,500 ms budget
and ~35 call sites, a contended channel silently loses app entries — alerts, nudges, request
confirmations. The one path that does check is the owner-delivery path. This is a self-declared HIGH
and it is still open at HEAD.

**MED — 190 `catch` sites in one file, many `catch { }` with a comment.** The comments are good, but
`grep -c catch` = 190 against `grep -c 'try'` = 109 in `BridgeEngineModel.cs` means the dominant
control-flow idiom in the core class is "swallow and carry on". Best-effort is right for a Telegram
send; it is wrong as a default, and there is no way to tell from a call site which it is.

**MED — no metrics, only a log.** There is no counter for entries mirrored, sends failed, ticks that
overran 2 s, turns started/failed, or lock contention. `orchestrator.log.jsonl` is structured
(good), but every diagnosis is a `grep`. A system with a 2 s control loop and a 30-minute give-up
window needs a tick-duration histogram more than it needs another alert.

### 3.5 Persistence, durability, restart semantics, idempotency

**HIGH — the owner's Telegram message can be lost outright, and Telegram will not resend it.**
Sequence, all cited:
1. `Run_InboundLoop_Async` receives a batch and calls `Route_OwnerMessage_Async`.
2. `Route_OwnerMessage_Async` ends with `_ownerDeliveryBuffer.Add_Segment(channelFile, segmentText, …)` —
   **in-memory only**, with `OWNER_AGGREGATION_SECONDS = 6`.
3. Back in the inbound loop, `_lastUpdateId = batch.MaxUpdateId.Value; Persist_BridgeState();` —
   the offset is now durable, so `getUpdates` will never return that message again.
4. The append to `owner-channel.md` happens later, on the **mirror** loop, in
   `Flush_OwnerDeliveries_Async` → `Deliver_OwnerMessage_Async`.

Any host death, kill, or crash in that ≥6-second window destroys the message. `EngineStateSnapshot`
persists open questions, buttons, confirmations, nudge memory and respawn counters — but **not the
owner delivery buffer** (verified against every property of `Bridge/EngineState/EngineStateSnapshot.cs`).
Note the code *already* fixed the sibling defect (a *failed send* dropping the owner's answer, "R1",
with a put-back and a dedicated test seam) and *already* persists `_ownerAwaitingAnswer` mid-route
"because R1 survives a restart too". The buffer itself was missed. Fix: advance `_lastUpdateId` only
after the segment is durable, or persist the buffer in the snapshot.

**MED — request files are read non-atomically and a partial write is deleted.**
`OrchestrationRequests_Reader.Try_ParseInto_OrReason` does `File.ReadAllText` on every `*.json` in
`.requests/` **every 2 seconds**, and invalid JSON → `MalformedRequest` → `Delete_RequestFile` in
`BridgeEngineModel:Process_PendingRequests`. Agents are told (`kit/skills/*/SKILL.md`, e.g.
`supervisor/SKILL.md:703`) to "write `…/.requests/add-imp-<orch>-<ts>.json`" with **no atomic-rename
instruction**. A 2 s poll against a non-atomic write means a valid request can be truncated at read
time and destroyed. Mitigated (not eliminated) by the rejection entry being written back to the
requester's channel — so the agent finds out. Fix is one line of protocol: write `*.json.tmp`, then
`mv`; or have the reader require a stable mtime/size across two polls.

**MED — request files carry no id, so the protocol is not idempotent, only re-entrant.**
Delivery is "write file → app executes → app deletes → app appends confirmation". If the app
executes and dies before deleting, the action runs twice. `Process_StartRequests` allocates a fresh
`repo-slug-n` id each time, so a double execution produces **two orchestrations** — exactly the
duplicate-orchestration failure CLAUDE.md decision 8 records (attributed there to `--continue`
re-running a failed start; the file protocol has the same hole by a different route). The
mitigations present are all after-the-fact: `Archive_ResolvedRequest_BestEffort`, mandatory
`requester`/`reason` fields, and owner-tap confirmation for the destructive actions (close, promote).
A `requestId` written into a processed-ids set before execution would close it.

**LOW — no fsync anywhere.** `Atomic_FileWriter` says so explicitly and argues the hazard is out of
proportion. For `.bridge-state.json` that is fine (a lost cursor replays). For `session.json`
(the roster and `ClosedUtc`) and `PLAN.md` it is a real, if remote, hole.

**Restart semantics are a genuine strength.** Resume is "re-enter the role command, re-read the
channel", never `--continue` (decision 8, and the code honours it: `PrintTurnCommand_Builder` passes
the role command positionally on the first turn only). `EngineState_Serializer` restores buttons in
`_buttonOrder` so FIFO eviction survives a restart. `PrintTurnDispatcherModel:Stop_Async` drains
in-flight turns before cancelling (grace = configured `TurnTimeout` + 1 min), added after
"17 turns and 112 M tokens died within four minutes of a `Daemon stopping` line". These are the
decisions of people who have been burned and learned.

### 3.6 Testability

**What is under test (well):** every pure policy/parser/decider. 2,547 [estimate] cases over
290 files. `Planning` 202, `Status` 268, `Telegram` 234, `Channels` 145 — these are real behavioural
tests, and several test files are named after the *property* rather than the class
(`OwnerAnswerSurvivesFailedSendTests`, `DecisionStateSurvivesARestartTests`,
`TypedAnswerClearsTheQuestionProbeTests`, `MarkdownReachesThePhoneRenderedTests`), which is the
right instinct.

**What is not under test:**

- **HIGH — the mirror tick's step order.** 35 steps whose order encodes at least four separate bug
  fixes, defended only by comments. No test can observe "step X ran before the DND return".
- **HIGH — the process runner against the real CLI, in CI.** `tools/claude-contract` is the right
  idea — `FakeClaude` (a stub binary, 152-LOC `Program.cs` + scenario/responder/logger) plus
  `LiveFactAttribute`-gated tests against the installed `claude`. But the Live tests are opt-in and
  the whole thing is a separate solution folder, so the day-to-day suite validates the *argument
  list* (`PrintTurnCommand_Builder`) and the *stub*, not the CLI. Every CLI behaviour the code
  depends on is recorded as a dated comment ("MEASURED 2026-09-06 against 2.1.263: `claude -p
  --resume <uuid never created>` exits 1") — excellent practice, and precisely the kind of fact that
  rots without a scheduled live run.
- **MED — the Telegram client.** `TelegramApiClientModel` (659 LOC, 24 methods) is the only
  `HttpClient` holder and has no tests of its own (`Tests/Telegram/` covers the chunker, HTML
  renderer, status-line planner/builder/decider, token bucket — all pure). Engine-level Telegram
  behaviour is tested through `Create_WithTelegramClient` fakes, which is good, but the client's own
  429/chunk/edit/HTML-entity handling is unverified.
- **MED — timing.** `IClock` exists and `Create_WithDecisionState` takes one, so deadline logic is
  testable. But the loops use `Task.Delay` with hardcoded constants, so anything cadence-related
  (mirror retry backoff, quiet polls, narration repeat, topic revalidate) is either untested or
  tested by sleeping.
- **MED — both hosts.** Zero tests in `AIOrchestrator/` and `AIOrchestrator.Daemon/`. `MainWindow.xaml.cs`
  is 824 LOC of untested code-behind. Systemd notify/watchdog behaviour is untested (the pure
  `SystemdWatchdog_Schedule.Compute_PingInterval_OrNull` is, which is the testable half).
- **LOW — `WindowFocus/`** (871 LOC, 30 `DllImport`s) has 14 tests, necessarily of the pure parts only.

### 3.7 Portability

The daemon is a real, thought-through cross-platform host (systemd/launchd/SCM in one binary, no
`#if`, runtime detection). But the **feature set is still Windows-shaped**:

- **MED —** 16 CoreLib files reference `user32`/`kernel32`/`DllImport`/`powershell`/`wt.exe`/
  `IsWindows`. `WindowFocus/TerminalWindow_Focuser` (15 `DllImport`), `TerminalWindow_Capturer` (14),
  `SessionWindows_Organizer` (1) are pure Win32. The Telegram commands `/screens`, `/show`,
  `/organize` and the status-screenshot feature (`Set_StatusScreenshots`,
  `Build_StatusScreenshotMarker_OrEmpty_Async`) therefore **cannot work on the Linux VPS the fork
  actually deploys to** (`.claude/rules/git-and-boundaries.md`: "the VPS (`orch@…`) pulls
  `ours/integration`"). They are owner-facing commands that silently do nothing there.
- **MED — the terminal runner is Windows-only in practice.** `Spawning/SpawnCommand_Builder` builds
  `wt new-tab` / `Start-Process powershell`; the spec's "Mac later via `osascript`" was never built.
  On Linux/macOS the *only* working runner is the headless print/stream runner. That is arguably the
  right outcome, but it means the two runners are not interchangeable across platforms and the
  config key `runner: terminal|print|stream` is silently platform-conditional.
- **LOW — the statusline probe ships as both `statusline.ps1` and `statusline.sh`** (both in the
  csproj `Content`), so telemetry is portable. `Kit/StatusLineSettings_Wirer` still names PowerShell.
- **LOW — `CLAUDE.md` itself is Windows-only** ("The repo root is `C:\Users\Gianpiero\...`",
  "Multi-line git commits via `git commit -F <tempfile>` (Windows PowerShell mangles `-m`)"), in a
  repo checked out on macOS with a Linux deployment target. Also present in the tree:
  `bash.exe.stackdump`, `grep.exe.stackdump` — committed msys crash dumps.

### 3.8 Configuration sprawl

**MED — policy lives in 154 compile-time constants, not in configuration.**
`grep -rhoE 'const (int|double) [A-Z_]+' AIOrchestratorCoreLib | wc -l` = **154**, plus 14
`static readonly TimeSpan`. Thirty-six of them are in `BridgeEngineModel` alone:
`STALL_ALERT_MINUTES = 25`, `IMPLEMENTER_NUDGE_MINUTES = 8`, `OWNER_REPLY_GRACE_SECONDS = 150`,
`NARRATION_FIRST_DELAY_SECONDS = 45`, `NARRATION_REPEAT_SECONDS = 180`, `MIRROR_TICK_MILLISECONDS = 2000`,
`MIRROR_RETRY_BACKOFF_SECONDS = 30`, `MIRROR_RETRY_WINDOW_MINUTES = 30`, `SILENT_DEADLOCK_MINUTES = 5`,
`QUESTION_HOLD_CAP_MINUTES = 10`, `ORPHAN_CONFIRM_MINUTES = 6`, `OWNER_AGGREGATION_SECONDS = 6`,
`BUTTON_REGISTRY_CAP = 300`, plus `Channel_Compactor.COMPACT_ABOVE_ENTRIES = 90` /
`KEEP_RECENT_ENTRIES = 45`, `ChannelFile_Lock.STALE_SECONDS = 60`,
`ChannelWrite_Lock.DEFAULT_BUDGET = 1500 ms`. Several carry comments admitting the number is a guess
("**THIS NUMBER IS A GUESS and should be treated as one**").

Against that, `config.json` exposes ~15 top-level keys (`repos`, four `*Model` keys, three telegram
ids, `telegramItalianLayer`, `telegramStatusScreenshots`, `voiceTranscribeCommand`,
`orchestrationTokenBudget`, `planBackend`, plus nested `runners`, `guardrails`, `defaults`,
`telegram` prose). So the owner can change *which model* but not *how long before the app nudges an
idle implementer* — and the latter is the setting most likely to be wrong for a given owner.

The nested settings groups themselves are clean (`RunnerConfigs_Json`, `GuardrailSettings`,
`DefaultsSettings_Json`, `TelegramProseSettings_Json` — each with its own `_Json` reader), so the
mechanism to fix this exists; the constants just were never migrated. Two further wrinkles:
`telegramItalianLayer` is persisted while 🌙/🔕 are in-memory (decision 11 — an intentional
inconsistency, but an inconsistency), and `secrets.json` is read **once, at startup**
(`BridgeEngine_Factory.Create`), so a token change needs a restart while `repos`/models are read
live through `IOrchestratorConfigProvider`.

### 3.9 Telegram integration

**Right calls:** mirror-not-transport (the spec's three Bot-API constraints are correct and the
conclusion follows); one forum supergroup, one topic per orchestration; owner messages structurally
supervisor-only; `TokenBucket_Gate` centralising the 20-msg/min limit in the one choke point with
the burst arithmetic actually done right (capacity = half the ceiling, because a bucket's worst case
over a window is capacity + refill — a first version got this wrong and it is documented);
`TelegramMessage_Chunker` for the 4096 limit; HTML rather than Markdown for every send
(`Send_MirrorPiece_Async`: "a channel entry is Markdown … agents write `**bold**`, bullet lists and
fenced mockups because that is how they write"); edits rather than stacks for repeats (decision 14 —
`Publish_DeliveryReceipt_Async`, `Narrate_BusySupervisor_Async`); alerts the owner cannot act on
never reach Telegram (decision 15).

**MED — the single-poller invariant is enforced per supervision root, not per bot token.**
`SingleInstance_Guard` says so itself: two hosts with different `--root` sharing one token both
long-poll, one steals the other's updates, and nothing detects it. On a machine + VPS deployment
this is a live foot-gun. Detecting it is cheap: `getMe`/`getUpdates` returning 409 is unambiguous
and could be surfaced as a fatal rather than folded into the generic backoff.

**MED — mute freezes offsets, which conflates two things.** Mute = "skip tailing entirely, offsets
freeze, unmute delivers everything in one burst" is elegant for the away-from-PC case. But the
mirror tick now has **~14 steps deliberately hoisted above the DND return**, each with a paragraph
explaining why it is not a disturbance (dispatch pause, request files, watchdog, print turns, meeting
flags, owner delivery, announcements, close-confirmation expiry, parked releases, baselining,
question deadlines, progress artefacts, plan-backend). That is not a mute; it is a policy with 14
exceptions expressed as statement order in a 190-line method. Two flags — `pauseOutboundTelegram`
and a genuine `pauseEverything` — would say the same thing declaratively.

**LOW — at-least-once with no idempotency key means duplicates on partial multi-chunk failure.**
`Mirror_Append_Async` sends `foreach (var piece in pieces)` inside one try; any failure returns
false and the whole append is re-emitted next poll, re-sending pieces that landed. Explicitly
accepted in the comment ("A duplicate on the phone is a nuisance; a supervisor's message that never
arrives is what the owner reported today") — the right trade, but it is a consequence of having no
per-entry delivery ledger.

**LOW — after `MIRROR_RETRY_WINDOW_MINUTES = 30` the entries are dropped**
(`Settle_MirrorAttempt` calls `Confirm_Append` and logs Error). The channel file still has them, but
nothing will ever mirror them; there is no "replay from entry N" command.

### 3.10 Usage / cost telemetry

**MED — the whole cost and limit surface is scraped from a file the status line writes.**
`Usage/UsageTotals_Reader` (`SESSION_USAGE_FILE = ".usage.json"`) enumerates `*usage.json` under the
supervision root and sums fields matched by a **regex over key names**:
`^(total_)?(cache_creation_|cache_read_)?(input|output)_tokens$`. The design is `statusline.ps1`/`.sh`
doubling as a telemetry probe, because Claude Code hands the status line a JSON payload nobody else
gets. It is a clever hack and it is also the most fragile input in the system:

- The schema is not a contract. `Limits/LimitData_Parser` is deliberately "schema-tolerant" and the
  spec still carries **"⚠ NEEDS LIVE VERIFICATION: whether this Claude Code version's statusline
  payload carries limit data"** — unresolved, at HEAD, while `Check_UsageLimits_Async` texts the
  owner at 90/95/97/98/99/100% and `Update_DispatchPause_Async` **pauses spawning and respawns** on
  it. A silent schema change means the app stops launching sessions, or stops warning, and reports
  neither.
- It only exists while a status line runs. A headless print turn produces a `.usage.json` only if
  the CLI invokes the status line in `-p` mode — which is a CLI behaviour, not a guarantee.
- "No data ⇒ idle" is the documented fallback, so an absent probe is indistinguishable from an idle
  session. `BridgeEngineModel:Is_SessionMidTurn(usageFilePath)` and `Describe_Activity_Suffix` build
  *member state* on that, and member state drives nudges and stall alerts.
- Reads are tolerant ("a missing or half-written file contributes nothing rather than throwing"),
  which means a half-written probe silently under-reports cost rather than being retried.

**Right call:** `UsageTotals_Reader` is the *single* reader, with `Build_PerSourceTotals` and
`Build_OrchestrationTotals` (the latter being the former summed), so cards, detail window, `/tokens`
and `/cost` cannot disagree (decision 10). `UsageLifetime_Accumulator` folds respawned sessions that
reset their file. That is the correct structure around a bad input.

The real fix is the one the four uncommitted 2026-09-08 specs are reaching for: the CLI's
`--output-format json` result *already* carries usage and cost per turn, and the print runner already
parses it (`Running/TurnResult/ITurnResult`, `Describe_Cost`). **Cost should come from the turn
result the app already has, not from a file a status line happens to drop.** Note
`--max-budget-usd` appears **only** in those four untracked spec documents
(`grep -rn -- '--max-budget' .` → 10 hits, all in `docs/superpowers/specs/2026-09-08-*.md`), never in
code.

### 3.11 PLAN.md ledger parsing

`Planning/PlanLedger_Parser.Parse_OrNull` is a single `GeneratedRegex`
(`^(?<indent>[ \t]*)-\s*\[(?<marker>x|X| |>|!|\?|-)\]\s*(?<text>.*)$`) over `planText.Split('\n')`,
with a section flag so `PlanLedger_Sections` (PARKED, OWNER REQUESTS) are skipped.

**Right calls:** named regex groups, with the reason stated (positional groups shift silently when
one is added); `[-]` "not doing" so 100% is reachable (the owner's complaint that "no session has
ever finished at 100%"); the parked-section skip enforced **in the app** rather than left to the role
prompt, per decision 21/22 — this is the single best example in the repo of "hooks advise, the app
enforces"; file order retained alongside the buckets because `/progress` needs it.

**MED — same fence blindness as the channel parser.** No ``` tracking, so a supervisor pasting a
markdown checklist into a PLAN.md note section (or into a section that is not a listed non-ledger
heading) inflates the denominator. Given decision 22's whole point is that the denominator must
reflect owner requests only, this matters.

**LOW — the ledger is a markdown file an agent rewrites wholesale.** There is no lock named on the
PLAN.md path in `Channels/ChannelWrite_Lock` usage (the lock is per *channel* file); the supervisor
owns PLAN.md by protocol, and `Planning/PlanSeed_Writer` / `Sync_PlanBackends` also write it from the
app. Two writers, one file, no lock, and the app's writer runs on the 2 s tick.

**LOW — `PlanShape_Validator` / `Report_LedgerShape` police prose shape** ("lines that lump several
tasks together"). Correctly routed to the supervisor's channel and the log rather than Telegram
(decision 15). Fine — but it is the app grading an LLM's markdown style, which is a lot of machinery
for a soft rule.

### 3.12 Request-file protocol vs alternatives

The protocol: agent writes `.requests/<anything>.json`; app polls every 2 s, parses strictly, acts,
deletes, and appends a `FROM app` entry that wakes the requester's watcher. 9 actions. Malformed →
rejected **with a reason relayed to the agent's channel** and deleted, so it cannot wedge the loop.
Destructive actions (close, promote) are parked and require an owner button tap.

**What it gets right, and this is not a small list:** it needs no server, no port, no auth, no client
library; it works when the app is down (the file waits); the strict parse with a *named reason* is
better error reporting than most HTTP APIs give; forbidding `fable` at the reader
(`FORBIDDEN_MEMBER_MODEL`) and requiring `reason`/`requester` are policy enforced at the point of
effect, per decision 21; the "retries must reuse the SAME action" rule and the "known actions list
must BE the switch, not a subset" fix are the kind of details that only come from watching agents
fail.

**What it costs:** non-atomic reads (3.5), no idempotency key (3.5), 2 s latency floor, no request
status an agent can poll (it must wait for a channel entry), no schema the agent can validate
against locally, and — the structural one — **nine `Process_*Requests` methods, ~700 lines, live
inside the god class** and run inside the mirror tick, so a slow launcher spawn delays mirroring.

Alternatives and honest trade-offs:

| Option | Gains | Loses |
|---|---|---|
| Keep files, add `requestId` + processed-ids set + `.tmp`→`mv` | Idempotency, atomicity. ~50 lines. **No downside.** | — |
| Local HTTP/unix-socket API (`POST /requests`) | Sub-ms latency, synchronous result, real schema, agent gets an error immediately | Needs the app up (breaks "works with app down"), needs a token, agents need `curl` discipline, another surface to secure |
| SQLite queue | Atomic by construction, idempotent, queryable, one file | Loses human-readability and `cat`-ability; agents need a `sqlite3` binary; concurrent writers on network filesystems are a known hazard |
| Both: file drop as the **write** path, HTTP as an optional fast path | Best of both | Two paths to test |

**Recommendation: the first row.** The file protocol is the right primitive for this system; it is
just missing two properties it can have almost for free.

*(Correction to 3.11's last point: PLAN.md rewrites from the app do have a guard —
`Planning/PlanBackend/PlanFile_GuardedWriter.Write_IfUnchanged` compares `File.GetLastWriteTimeUtc`
against the stamp taken at read and refuses if it moved, then writes through `Atomic_FileWriter`. Its
own docstring says "It is NOT a lock, and does not pretend to be: the window between this check and
the rename is real, just very small." That is an honest optimistic-concurrency check, and correct in
spirit; the residual risks are (a) filesystems with coarse mtime granularity, where a same-tick
supervisor edit is invisible, and (b) `Planning/PlanSeed_Writer:63`, which uses raw
`File.WriteAllText` rather than the atomic writer.)*

---

## 4. What a greenfield redesign would do differently

### 4.1 What the current design gets RIGHT — keep these

1. **The file protocol as the system of record.** "If the app is not running, sessions still
   coordinate" is a real property, and it is why this thing survived 675 commits in 33 days of live
   use. Any redesign that makes a daemon load-bearing for agent↔agent coordination is worse.
2. **Human-readable audit trail = the transport.** There is no "what the log says" vs "what
   happened" gap. Debugging a multi-agent system is mostly reading what the agents said to each
   other, and `cat channel.md` is unbeatable for that.
3. **Telegram as mirror, never transport.** The three Bot-API constraints are correctly identified
   and the conclusion is sound. Do not revisit this.
4. **Hub-and-spoke with one duplex channel per member.** Implementers not seeing each other is a
   context-cost decision as much as an isolation one, and it is right.
5. **Resume = fresh role re-entry, never `--continue`.** Statelessness across launches, with the
   channel read as a log and never as a to-do list, is the correct answer to the duplicate-work
   failure mode.
6. **Enforcement at the point of effect, not in the prompt.** `PlanLedger_Parser` skipping PARKED
   sections, `OrchestrationRequests_Reader` refusing `fable`, requiring `reason`/`requester` — these
   are the app doing what a hook cannot (decision 21). This principle is worth more than most of the
   code that implements it.
7. **Owner-facing repeats edit, they never stack**, and **alerts the owner cannot act on never reach
   the phone** (decisions 14, 15). Most notification systems learn this too late.
8. **The commenting discipline.** Comments name the date, the measurement, the incident and the
   blast radius, and several explicitly downgrade themselves from "measured" to "speculative". That
   is rarer and more valuable than any of the code.
9. **`Xxx_Yyy` + `IFoo`/`FooModel`/`Foo_Factory` with pure policy classes.** 344 classes, median
   well under 150 LOC, 2,547 [estimate] test cases. Outside the god class this is a *good* codebase.
10. **Draining in-flight turns before shutdown**, and the drain grace derived from the configured
    turn timeout. Learned the expensive way; keep it.

### 4.2 The one structural change: an append-only event log as truth, markdown as a projection

**Today:** the markdown file is simultaneously (a) the transport, (b) the state, (c) the cursor
substrate, and (d) the archive. Every hard bug in this audit comes from one artefact carrying four
jobs — byte offsets into a file that gets rewritten, entry counts that are not monotonic because of
compaction, a parser that has to guess where an entry ends, a trailing-newline convention that is
load-bearing, and index numbers guessed by an LLM.

**Instead:** one `events.jsonl` per orchestration (or one SQLite file per supervision root), strictly
append-only, one JSON object per line, written **only by the app** — never by an agent:

```
{"seq":1841,"ts":"2026-09-09T10:14:22Z","orch":"crm-2","from":"implementer","member":"imp-1",
 "kind":"report","body":"…","meta":{"worktree":"…","cost":0.42}}
```

- `seq` is a monotonic integer the app allocates. No agent ever guesses an index, so CLAUDE.md
  decision 12 stops being a rule and becomes a non-issue. `ChannelIndexSequence_Screen`,
  `ChannelHistory_Counter` and `Describe_SinceStamp_OrNull`'s future-stamp guard all disappear.
- Cursors become `lastSeq` integers, not byte offsets. Compaction becomes "the projection is
  rebuilt from `seq > N`", so the tailer/compactor race (3.2), `Has_UndeliveredEntries`'s
  three-clause proof, and `Warn_IfEntriesWereArchivedUndelivered` all disappear.
- Delivery becomes idempotent: a `delivered_seq` per Telegram topic means partial multi-chunk
  failure re-sends only what did not land, so the accepted duplicate (3.9) goes away too.
- **`channel.md` stays**, generated from the log after every append — same path, same format, so
  agents' Read-based boot and the human `cat` both keep working unchanged. It becomes a *view*.

**How agents write, then:** they do not append to the markdown. They either (a) write one JSON line
to `outbox.jsonl` (still a plain file, still works with the app down, still `cat`-able, but with no
index to guess and no header format to get wrong — the app assigns `seq` when it ingests), or (b) for
bridge-driven sessions, they simply *return their turn's final message* and the app writes the event
— which is **already how the print runner works** (`ChannelAppender.Append_SessionEntry`, whose
docstring notes it "makes CLAUDE.md decision 12 moot for print-run sessions"). Option (b) already
exists and is already the better path; the redesign is to make it the only path.

**Trade-off, stated honestly:** the markdown file stops being the thing agents write, so a human
editing `channel.md` by hand no longer injects a message. That is a capability lost. It is worth
losing — a hand edit today is exactly the "writer that does not take the lock" the
`ChannelFile_Lock` docstring warns about.

### 4.3 Split the god class into an explicit stage pipeline

`Execute_MirrorTick_Async`'s 35 steps become a declared, ordered list of named stages, each a small
class with an interface:

```csharp
interface ITickStage { string Name { get; } bool RunsWhileMuted { get; } Task Run_Async(ITickContext ctx, CancellationToken ct); }
```

- The **order becomes data** (`TickPipeline_Order.STAGES`), so it can be asserted by a test — which
  is precisely what the "if you are that edit: don't" comment says is impossible today.
- `RunsWhileMuted` replaces the 14 hoisted-above-the-gate steps with a declaration per stage, and
  the DND policy becomes readable in one place instead of inferred from statement order (3.9).
- Per-stage timing, per-stage error isolation and per-stage log scoping come free — a stage that
  overruns is named, instead of "Mirror tick failed".
- The 97 mutable fields partition by stage. Most become private to one stage; the genuinely shared
  ones (open questions, buttons, dispatch pause) move behind a single `IEngineState` with one lock,
  killing the three-lock ordering rule in `Persist_EngineState`.
- Rough shape: ~24 stage classes of 100–500 LOC each, plus ~13 `IOwnerCommand` implementations for
  the bot-command chain. That is 13,412 lines becoming ~40 files with a median under 300 — the
  distribution the rest of the codebase already has.

This is mechanical, incremental (one stage extracted per commit, pipeline order asserted by a test
from commit one), and does not require the event-log change. **It is the highest-value refactor in
this audit and it is not risky.**

### 4.4 An explicit state machine for turns

`Running/` already has the pieces (`TurnCursor`, `TurnOutcomes`, `ResumeModes`, `RunnerFallback_Ladder`,
`TurnLog`, `PendingTraffic`) but the *machine* is implicit in `PrintTurnDispatcherModel:Consider_Session`
→ `Start_Turn` → `Execute_Turn_Async` → `Run_Turn_Async` → `Record_Failure` control flow, with
in-flight tracking in a dictionary keyed by string interpolation. Make it a declared FSM:
`Idle → PendingTraffic → Queued → Running → (Succeeded | Failed(attempt n) | TimedOut | TranscriptGone) → Idle`,
persisted per member in `print-session.json` with the state name. Gains: the retry ladder becomes a
transition table; "is this member working?" stops being three different predicates
(`Is_TurnInFlight`, `Is_SessionMidTurn(usageFile)`, `MemberWorking_Decider`) that can disagree; a
crashed host resumes a member in a known state instead of inferring it.

### 4.5 Drive sessions through the Agent SDK / `stream-json`, not terminals

The code has already made this journey: terminals → `claude -p --output-format json` → resident
`--input-format stream-json --output-format stream-json`. Finish it.

| Option | Gains | Loses |
|---|---|---|
| **Terminal spawn** (`wt new-tab`) | The owner can type directly into a session; visual presence; pid-as-liveness | Windows-only; PID files; window titles as identity (`TerminalWindow_Focuser`, 15 `DllImport`s); no structured output; usage only via statusline scraping |
| **`claude -p --output-format json`** (current default) | Cross-platform; structured result with real cost/usage/error; stdin for prompts so no shell quoting; testable via `FakeClaude` | One process per turn (boot cost per turn); no mid-turn visibility |
| **`--output-format stream-json`** (present, `Running/StreamTurn/`) | Mid-turn events, hook events, incremental output → real "what is it doing now" instead of `.usage.json` guessing; one resident process | More complex lifecycle; a wedged resident process is harder to reason about than a dead one |
| **Claude Agent SDK (TS/Python)** | First-class session objects, typed events, no CLI-shape archaeology, no `MEASUREMENTS.md` of flag behaviour | Rewrites the runner in another language, or forces a polyglot deployment; loses the C# type system across the boundary; the CLI is what the owner actually runs interactively, so behaviour would diverge |

**Recommendation:** make `stream-json` the only runner and delete the terminal path. That removes
`WindowFocus/` (871 LOC, 30 `DllImport`s), the pid-file liveness scheme, the window-title identity
scheme, the `/screens`/`/show`/`/organize` commands that cannot work on the deployment target, and —
the big one — **the entire statusline-scraping telemetry design**, because a stream session reports
its own usage and cost per turn in the event stream. Keep the terminal spawn only as a debugging
escape hatch, unsupported on non-Windows. Do **not** move to the SDK: the CLI is the thing the owner
uses by hand, and `tools/claude-contract` already gives the CLI a real contract test — that is the
right investment, and it is cheaper than a polyglot rewrite.

### 4.6 A local HTTP/IPC control API — as an addition, not a replacement

Add a loopback HTTP endpoint (or unix socket) for the *synchronous* half: request submission with an
immediate typed response, plus read-only status. Keep the file drop as the durable path that works
when the app is down. Concretely: `POST /requests` returns `{accepted, requestId}` or
`{rejected, reason}` in milliseconds instead of 2 seconds plus a channel entry, and the agent gets
its validation error without burning a turn waiting for a watcher wake. `GET /status` replaces
several of the `/`-commands the god class renders by hand. Cost: a token to manage, a port to bind, a
surface to keep off the network, and a second path to test. **Worth it only after 4.2/4.3;** the
`requestId` + processed-ids fix (3.12) delivers most of the value for 50 lines.

### 4.7 Web UI instead of WPF

The WPF app is 1,982 LOC of the 108,975 total (1.8%) and its only unique capability is the Win32
window-focus feature that does not work on the deployment target anyway. Meanwhile `net10.0-windows`
forces a second TFM, a `WinExe` output, and a stylus-thread workaround in the csproj for the only
crash the app has ever had. Replacing it with a small server-rendered page served by the daemon
(cards, log tail, buttons, mute checkbox — everything `MainWindow.xaml.cs` does, with a 5 s refresh
which is what `REFRESH_INTERVAL_SECONDS` already gives) would collapse the solution to **one
cross-platform host**, work from a phone browser as well as the desktop, and delete the WPF/daemon
divergence risk entirely. Trade-off: the owner loses a native window and gains a browser tab; the
Telegram interface already covers the mobile case, so the desktop UI's job is the log panel and the
buttons, which a web page does at least as well.

### 4.8 Smaller items a greenfield build would do differently

- **A parser that knows about fenced blocks**, or better, no markdown parsing at all (4.2).
- **Policy numbers in configuration**, with the 154 constants promoted to a `tuning` section that has
  defaults in code — so the owner can change `IMPLEMENTER_NUDGE_MINUTES` without a rebuild (3.8).
- **Metrics before more alerts**: a tick-duration histogram, per-stage timings, sends/failures/
  contention counters (3.4).
- **Cost from the turn result, never from a scraped status line** (3.10).
- **One `pauseOutbound` / `pauseAll` pair** instead of a mute with 14 ordering exceptions (3.9).
- **409 on `getUpdates` treated as fatal and named**, not folded into generic backoff (3.9).
- **Delete the committed `bash.exe.stackdump` / `grep.exe.stackdump`.**

---

## 5. Top 10 risks / defects actually visible in the code

Ordered by expected harm. Every one is cited; all are read from branch source at HEAD 926cc6b.

| # | Sev | Defect | Where |
|---|---|---|---|
| 1 | HIGH | **The owner's Telegram message is lost if the host dies within the 6 s aggregation window.** The getUpdates offset is persisted (`_lastUpdateId = batch.MaxUpdateId.Value; Persist_BridgeState();`) while the message exists only in the in-memory `_ownerDeliveryBuffer`; the channel append happens later on the mirror loop. `EngineStateSnapshot` does not persist the buffer, so Telegram never resends and the words are gone. | `BridgeEngineModel:Run_InboundLoop_Async` (≈5947–5950), `BridgeEngineModel:Route_OwnerMessage_Async` (last line), `BridgeEngineModel:Flush_OwnerDeliveries_Async`, `Bridge/EngineState/EngineStateSnapshot` |
| 2 | HIGH | **Most channel appends discard the "did I write?" result, so app entries are silently dropped under lock contention** (1,500 ms budget, ~35 call sites). Self-declared in the docstring: "most call sites in the bridge ignore it … the entry is dropped and nothing says so." | `Channels/ChannelAppender` (class docstring), `BridgeEngineModel:Append_AppEntry_Safe`, `:Announce` |
| 3 | HIGH | **Fence-blind entry parsing splits one entry into phantom entries with duplicate indices** whenever an agent quotes another entry — which the role skills encourage. Corrupts the mirror, the index screen, member state and next-index allocation. Very likely the true cause of the `option-lab-2` duplicate-`[80]` incident recorded in CLAUDE.md decision 12. | `Channels/ChannelEntry_Parser:Parse_All`, `:Find_HeaderLineIndexes`; downstream `Channels/ChannelIndexSequence_Screen`, `Status/MemberState_Resolver` |
| 4 | HIGH | **The mirror tick's 35-step order is the specification and is untested.** The code's own comment: "Moving this call below the return seven lines down compiles, passes every test, and quietly reintroduces exactly the bug described above." | `BridgeEngineModel:Execute_MirrorTick_Async` |
| 5 | HIGH | **Usage limits and cost — which gate spawning via `Update_DispatchPause_Async` — are scraped from a status-line JSON file whose schema is explicitly unverified.** The design spec still carries "⚠ NEEDS LIVE VERIFICATION: whether this Claude Code version's statusline payload carries limit data", unresolved at HEAD. A silent schema change stops session launches, or stops the 90–100% alerts, and reports neither. | `Usage/UsageTotals_Reader`, `Limits/LimitData_Parser`, `BridgeEngineModel:Check_UsageLimits_Async`, `:Update_DispatchPause_Async`, `:Read_CurrentLimitWindows` |
| 6 | MED | **An oversized index in a header permanently poisons one channel.** `int.Parse` on an unbounded `(\d+)` throws `OverflowException` *after* the tailer advanced its offset and *before* it cleared `Pending`, so the same throw recurs every 2 s forever: that channel never mirrors again, `Pending` grows unbounded, and nothing can append to it (`Get_NextIndex` throws too) — including the app's own error report about it. | `Channels/ChannelEntry_Parser:Build_Entry`, `Tailing/ChannelTailer/ChannelTailerModel:Extract_CompleteEntries`, `Channels/ChannelAppender:Append_Entry` |
| 7 | MED | **The request-file protocol has no idempotency key.** Execute-then-delete means a host death between the two re-runs the action; `start-orchestration` allocates a fresh id each time, so it produces **two orchestrations** — the same duplicate-orchestration failure decision 8 records, reached by a different route. | `BridgeEngineModel:Process_PendingRequests`, `:Process_StartRequests`, `:Delete_RequestFile`, `GeneralSupervision/OrchestrationRequests_Reader` |
| 8 | MED | **Request files are read non-atomically on a 2 s poll and a truncated read is deleted.** Agents are told to write the JSON directly, with no `.tmp`→`mv` instruction, so a valid request can be destroyed mid-write. | `GeneralSupervision/OrchestrationRequests_Reader:Try_ParseInto_OrReason`, `kit/skills/supervisor/SKILL.md:703`, `kit/skills/solo/SKILL.md:497` |
| 9 | MED | **`_lastUpdateId` is written on the inbound loop with no lock and no barrier, and read on the mirror loop under `_stateLock`** — the lock looks protective and is not. A stale persisted offset replays consumed updates, duplicating owner messages into channels after a restart. | `BridgeEngineModel:770` (field), `:5949` (write), `:Persist_BridgeState` (read) |
| 10 | MED | **Windows-only owner features ship enabled on a Linux/macOS deployment target.** `/screens`, `/show`, `/organize` and the status-screenshot marker depend on 30 `DllImport`s into `user32`/`kernel32`; the fork's stated deployment is a Linux VPS. They fail or no-op with no capability gate. | `WindowFocus/TerminalWindow_Focuser`, `:TerminalWindow_Capturer`, `:SessionWindows_Organizer`, `BridgeEngineModel:Show_SessionWindow_Async`, `:Send_SessionScreenshot_Async`, `:Organize_SessionWindows_Async` |

Runners-up (real, lower harm): raw `File.AppendAllText` for channel entries with no per-entry
terminator or checksum (`ChannelAppender:Append_Entry`); `.bridge-state.json` rewritten ~30×/min
(`Persist_BridgeState`); the single-poller invariant being per-root rather than per-token
(`Composition/SingleInstance_Guard`); mirror entries permanently dropped after 30 minutes with no
replay command (`Settle_MirrorAttempt`); `Planning/PlanSeed_Writer:63` using non-atomic
`File.WriteAllText`; the PLAN.md guarded write relying on mtime granularity
(`Planning/PlanBackend/PlanFile_GuardedWriter`); the 29-branch command chain inside the network loop
(`Run_InboundLoop_Async`); no fsync anywhere (`Storage/Atomic_FileWriter`, stated).

**Documentation defects worth fixing because they actively mislead:** `CLAUDE.md` decisions 17 and 23
describe `kit/commands/*.md` and `KitAssets_Installer` overwriting `~/.claude/commands` at every
startup; at HEAD there is no `kit/commands/` (it is `kit/skills/*/SKILL.md` + `kit/.claude-plugin/`)
and `AIOrchestrator.csproj` states the app "no longer copies any of it into `~/.claude`". A reader
following decision 17's verification procedure will verify the wrong artefact. `CLAUDE.md`'s
Conventions section still gives a `C:\Users\Gianpiero\...` repo root and PowerShell-specific git
advice. The design spec has no daemon, no headless runner, no `solo`/`reviewer`/`communicator` role,
and 4 request actions where the code has 9.

## Top 10 improvements ranked by VALUE (not effort)

1. **Make the owner's inbound message durable before acking Telegram.** Either persist the delivery
   buffer in `EngineStateSnapshot`, or move `_lastUpdateId` advancement to after the channel append.
   This is the one defect that destroys the owner's own words, in the one system whose entire purpose
   is carrying them. Small change, highest value.
2. **Extract `Execute_MirrorTick_Async` into a declared `ITickStage` pipeline with `RunsWhileMuted`,
   and assert the order in a test.** Turns the system's most important untested invariant into data,
   makes the DND policy readable, gives per-stage timing and error isolation, and is the wedge that
   makes the rest of the god class splittable. Mechanical and incremental.
3. **Stop agents writing channel headers.** Make `ChannelAppender.Append_SessionEntry` (bridge writes
   the header, index and time) the only path, for every runner. This alone retires CLAUDE.md
   decision 12, the index screen, the duplicate-index alerts, and the future-timestamp guard.
4. **Give entries a monotonic `seq` and make cursors `lastSeq`, not byte offsets.** Retires the
   tailer/compactor race, `Has_UndeliveredEntries`'s three-clause proof, the non-monotonic-count
   trap (decision 13), `Warn_IfEntriesWereArchivedUndelivered`, and the duplicate-on-partial-send
   trade-off. This is 4.2's core and can be done without the full event-log rewrite.
5. **Take cost and usage from the turn result the runner already parses; delete the statusline
   scrape.** `ITurnResult` already carries it. Removes the most fragile input in the system, and
   removes it from the path that gates spawning. Also makes cost correct for print/stream sessions
   regardless of whether the CLI runs a status line.
6. **Add `requestId` + a processed-ids set, and a `.tmp`→`mv` rule in the role skills.** ~50 lines
   and one sentence of protocol; closes defects 7 and 8 and makes the file protocol genuinely
   idempotent rather than merely re-entrant. No downside.
7. **Make fence-aware parsing (or no markdown parsing) the rule in both parsers.**
   `ChannelEntry_Parser` and `PlanLedger_Parser` both need ``` tracking while markdown remains the
   substrate. Cheap, and it protects the owner's progress bar (decision 22's whole point) and the
   member-state machine.
8. **Make `stream-json` the only runner; delete the terminal path and `WindowFocus/`.** Removes
   871 LOC and 30 `DllImport`s, the pid/window-title identity schemes, three owner commands that
   cannot work on the deployment target, and the `net10.0-windows` TFM's reason to exist. Also gives
   real mid-turn visibility instead of inferring activity from a file's mtime.
9. **Extract the 29-branch command chain into an `IOwnerCommand` table.** Each command becomes
   independently testable, the network loop goes back to being a network loop, and new commands stop
   growing the god class. Mechanical.
10. **Promote the 154 tuning constants into a `tuning` config section (defaults in code), and add a
    tick-duration/stage-timing metric.** The owner currently cannot change the nudge interval
    without a rebuild, and nobody can tell whether the 2 s tick is being met. Both are prerequisites
    for tuning this system rather than guessing at it — and several of the constants say in their own
    comments that they are guesses.

*(Also worth doing, outside the ranking because it is documentation: fix `CLAUDE.md` decisions 17/23
and the Conventions section, and either update the 2026-08-06 design spec or mark it superseded. A
context file that names the wrong artefact costs a session at a time.)*

---

### Provenance statement

Everything above was read from the branch source in `/Users/nvene/Visual Studio/AIOrchestrator` at
HEAD `926cc6b`. No build output, installed copy, or running binary was inspected — so no claim here
is a claim about what is deployed on the VPS or about what any built binary contains (decisions 18,
23). `dotnet` is not present on this machine, so no build or test run backs any of it: the test
figures are attribute counts, and the behavioural claims are readings of source, not observations of
execution. Line-number citations are as of this commit and will drift.

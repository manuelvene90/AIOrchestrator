# Fork Merge — Plan 03: The behavioural seams, and the `Bridge/` flakiness campaign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the catalogue's `phone.*`, `pulse.*`, `topic.*` and `general.buttons` entries DO something. Plan 02 registered 66 settings and resolved them through four layers; every one of the phone-facing ones is inert by design (`SettingsCatalog.INERT_NOTE`: *"REGISTERED BUT READ BY NOTHING YET — the engine starts obeying this key in plan 03"*). This plan is the "starts obeying". Each seam becomes one reviewable task: who rings, what rings, what the pulse carries, which buttons hang off it, how a receipt is drawn, where the mode glyphs live, whether an unprompted status exists, whether the reply keyboard is installed, and what closing an orchestration does to its topic. It also pays the debt `.claude/rules/git-and-boundaries.md` books to this plan by name: *"the file-lock / wall-clock family under `Bridge/` is a known flakiness campaign (plan 03), not a regression."*

**Architecture:** One plumbing task first (Task 1) creates two settings blocks — `Configuration/PhoneSettings/` and `Configuration/PulseSettings/` — shaped exactly on plan 02 Task 7's `EffortSettings_Json`: a triple whose `_Json.Parse(configRoot, presetTree)` resolves every row through `Settings_Resolver` and hands back typed values, hung on `IOrchestratorConfig` beside `Guardrails`, `Defaults`, `TelegramProse`, `Runners` and `Effort`. From then on a seam reads `_configProvider.Get_Current().Phone.<X>` / `.Pulse.<X>` — the same one-line shape the engine already uses in nineteen places — and the pure builders (`TopicStatusLine_Builder`, `TopicCommandButtons`, `TelegramDeliveryModes`) take the resolved values as PARAMETERS, so they stay static, pure and testable without a config provider. Tasks 2–10 are one seam each. Task 11 is the flakiness campaign. Task 12 is the gate.

**Tech Stack:** .NET 10 (`net10.0`, WPF app `net10.0-windows`), xUnit, `System.Text.Json.Nodes`, git, bash (msys on Windows), `jq`.

**Spec:** `docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md` — §7 in full (§7.1 who rings, §7.2 the hold, §7.3 pulse, §7.4 glyphs, §7.5 receipts, §7.6 reply keyboard), the `Phone` / `Pulse` rows of §6.4, the mode-parameterised and Windows-reds bullets of §9, and the phase-3 and phase-6 lines of §10. **The spec is NOT in this worktree** — it lives on branch `feat/fork-merge-and-profiles-spec`, checked out at `C:\Users\Gianpiero\source\repos\AIOrchestrator-spec`. That is the copy this plan was written against (see the provenance note below).

**Worktree:** create a fresh one off master once plan 02 has merged — `git worktree add ../AIOrchestrator-plan03 -b plan/03-behavioural-seams master`. **This plan was AUTHORED in `C:\Users\Gianpiero\source\repos\AIOrchestrator-plan02` (branch `plan/02-settings-catalogue`) while another agent held that worktree for plan 02 Task 7**; the author wrote this file and nothing else there, ran no git and no build. Never commit to `master`; the owner merges.

**Which copy every statement here was read from (CLAUDE.md decision 18):**

| document | copy read |
|---|---|
| `CLAUDE.md`, `.claude/rules/*.md` | branch source, worktree `AIOrchestrator-plan02`, branch `plan/02-settings-catalogue` |
| the design spec (§6, §7, §8, §9, §10, §11, §12) | branch source, worktree `AIOrchestrator-spec`, branch `feat/fork-merge-and-profiles-spec` |
| plan 02 and the plan-01 gate report | branch source, worktree `AIOrchestrator-plan02` |
| every production and test file quoted below, and the two preset JSONs | branch source, worktree `AIOrchestrator-plan02`, read 2026-09-12 **while Task 7 was in flight in that same worktree** — so `Configuration/EffortSettings/`, `OrchestratorConfig_Loader.cs`, `SpawnCommand_Builder.cs`, `OrchestrationLauncherModel.cs` and `IOrchestratorConfig.cs` may have moved since. Re-read those five before Task 1. |
| build output, installed `~/.claude`, the running app | **NOT READ.** Nothing in this plan is a claim about them, and no task here needs the app restarted. |

---

## Depends on plan 02

- **Tasks 1–6 of plan 02 are landed** (catalogue, path reader, registry, presets, resolver, model defaults). This plan consumes `SettingsCatalog.Find_OrNull`, `Presets_Loader.Resolve_ForConfig`, `Settings_Resolver.Resolve*`, `SettingOrigins`, `PulseField_Names`, `SettingValidators` and the two `kit/presets/*.json`.
- **Task 7 of plan 02 (effort) is IN FLIGHT at the time of writing, and Task 1 below MUST NOT START UNTIL IT HAS MERGED.** They modify the same four files — `Configuration/OrchestratorConfig/IOrchestratorConfig.cs`, `OrchestratorConfigModel.cs`, `OrchestratorConfig_Factory.cs` and `Configuration/OrchestratorConfig_Loader.cs` — and Task 1's whole shape is a copy of Task 7's `EffortSettings_Json`. Starting first buys a hand-merge of the file that every other task depends on.
- **Task 8 of plan 02 (the gate) should also have landed**, because it fixes the four `OrchestratorConfigFactoryTests` reds that plan 01 held open by ruling. **This plan inherits NO named reds.** If those four are still red when Task 1 starts, stop and say so: plan 03 must not be the plan that normalises them.
- Tasks 2–10 depend on Task 1. **Task 11 depends on nothing** and can be run in parallel with the whole plan by a separate session — it touches only `AIOrchestratorCoreLib.Tests/`, and it is the one task whose value grows the earlier it lands, because every other task here is verified by a test run.

## Non-goals — say no to these out loud

Every one of these is a different plan. A task that finds itself editing the files below has left its scope.

- **The three renderers (plan 04).** No WPF Settings window change, no `/settings` Telegram command, no `HttpListener`, no `web/` folder, no `SettingValidators.LISTEN_ADDRESS` implementation. This plan adds no way to CHANGE a setting — only ways for the engine to OBEY one. A setting is changed by hand-editing `config.json` or by naming a preset, exactly as today.
- **The kit's per-user values and the language prose (plan 05).** No `AIORCH_OWNER_NAME` / `AIORCH_OWNER_LANGUAGE` / `AIORCH_PLATFORM_CODES` export, no `repos[].code`, no skill prose edit, no role-command edit. The `Kit` category stays empty and `SettingsCatalogTests`' by-name exemption stays.
- **The translation layer.** Settled and gone (owner, 2026-09-12). Nothing here re-ports it.
- **The model and effort dials.** Plan 02 Task 6 and Task 7 own them. `models.*` and `effort.*` are read by the loader and the launcher before this plan starts, and no task here touches `SpawnCommand_Builder`, `OrchestrationLauncherModel` or `Apply_Dial`.
- **The one-question hold (spec §7.2) is ALREADY DONE and is not a setting.** `QuestionHold_Policy.Should_Hold` is consulted in `Mirror_Append_Async` at line ~3880, before the owner-channel push block, and `MirrorOutcomes.Held` skips `Settle_MirrorAttempt`. Task 2 must compose with it and must not re-implement, re-order or gate it.
- **Splitting `BridgeEngineModel.cs`.** The rule stands (`.claude/rules/code-conventions.md`): when a stage touches a piece of that file, that piece MOVES OUT into its own component under `Bridge/`; no new lines land in the big file by inertia. Every task below says which piece it moves. It is not a refactor task of its own.
- **Anything the engine does that this plan finds wrong and nobody asked about.** Decision 22: it goes in `## PARKED` at the end of this document, as one line, outside the denominator.

## Global Constraints

- **`jq` lives only on a login shell's PATH.** Every task that runs `dotnet` must first run, in the same bash invocation:
  `export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"`
  Without it the 13 statusline parity fixtures fail and you will diagnose a seam change from their noise.
- **`AIOrchestratorCoreLib` is strict** (`.claude/rules/code-conventions.md`): the triple `IXxx` + `internal sealed class XxxModel : IXxx` + `static Xxx_Factory`; `Xxx_Yyy.cs` with the underscore only when the second word is a role (`_Parser`, `_Builder`, `_Factory`, `_Reader`, `_Resolver`, `_Decider`, `_Policy`); methods `Verb_Object[_Modifier]`; anything that may not resolve ends `_OrNull`; get-only properties from a primary constructor; **no `record` types** (the one existing exception, `TelegramDeliveryModes.TopicNameFlags`, is a `readonly record struct` already in the tree — extend it, do not add a second); ad-hoc multi-value returns are value tuples; XML docs argue the WHY with dated incidents. Tests: xUnit `[Fact]`, folders mirror production namespaces 1:1, class `<Subject>Tests` with the underscore stripped, methods `Verb_Scenario_Outcome`, **stubs not mocks**.
- **NEVER run the full suite except at Task 11 and Task 12.** One suite at a time on this machine. A red under load is isolated with `--filter` and re-run alone before it is believed; compare the SET OF NAMES of failing tests against the known set, never the count.
- **The engine tests are real engine tests and they are the only way most of this plan can be proven.** The harness is `BridgeEngine_Factory.Create_WithTelegramClient(paths, configProvider, store, launcher, log, telegramClient)` with `FailableTelegram_Fake` + `RecordingLog_Fake` + `RecordingSpawner_Fake` — 28 test files already use it; `AIOrchestratorCoreLib.Tests/Bridge/OwnerAnswerSurvivesFailedSendTests.cs` is the canonical one and the two fakes live inside it. **Copy its constructor verbatim**, including the temp-root/`ConfigFile`/`SecretsFile` setup, and write the preset or the keys you are testing into that `ConfigFile` — that is how a seam test selects a mode. Do NOT register a print-runner session in an engine test: `BridgeEngine_Factory` hard-wires the real per-OS `claude` into the print dispatcher and a test that registers one can spawn a LIVE process.
- **Every seam is read ONCE, at the point of effect, from `_configProvider.Get_Current()`.** Never cache a resolved value in a field of the engine: the provider re-reads `config.json` on its write stamp, and a cached copy is the hot-reload defect this catalogue exists to prevent. Never pass a config provider into a pure builder either — resolve at the engine call site and pass the value.
- **Decision 12 — never a second copy of a formatter or of a fact.** Two of these tasks are specifically at risk: Task 4 (the pulse step, shared by the member-row duration and the heartbeat) and Task 5 (the hold toggle, which must exist in exactly one of two places). Each says so in its own steps.
- **Decision 15 — an alert the owner cannot act on does not go to Telegram.** Every "could not honour this setting" line in this plan goes to `orchestrator.log.jsonl` via `_log.Log_Warning`, never to a topic.
- **Windows:** `python3` is native Windows Python and cannot open msys paths — hand it Windows paths. Bash heredocs over ~6 KB die as a fake quote error; write the script with the Write tool and run the file. Quote `git show "ref:path"` whole.
- **Say which copy you read** in every report: branch source, build output, installed (`~/.claude`), or the running app's folder (`Get-Process AIOrchestrator | Select Path`). Nothing in this plan touches the installed kit or the running app.
- **Stage by explicit path**; never `git add -A` / `.` / `commit -a`. Multi-line messages via `git commit -F <tempfile>`. One commit per task (or per defect inside a task). Every commit ends with:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## OPEN DECISIONS — the owner or the coordinator answers these BEFORE the named task starts

The spec left each of these open, or the tree contradicts it. **Do not guess.** Each row names the task it blocks; a task whose decision is unanswered stops and asks rather than picking the recommendation silently. The recommendation is what the plan's author would do and why; it is not an answer.

**D1 — `phone.status.periodic` ships `true`, and the owner deleted that feature ten weeks after saying they wanted it. Blocks Task 8.**
Spec §6.4 registers `phone.status.periodic` default `true`, classic `true`, quiet `false`. The engine's own comment at `BridgeEngineModel.cs:14100` records the opposite ruling, dated and quoted: *"THE HALF-HOURLY STATUS IS GONE (owner's decision, 2026-09-09). It sent a fresh fifteen-line message every thirty minutes — ten of them in five and a half hours in one topic, three of them identical at 19:00, 19:30 and 20:00 with every member closed… ONE STATUS SURFACE PER TOPIC, and it is PULSE."* `Build_PeriodicStatusText` does not exist anywhere in this tree. So Task 8 as specified re-creates, on the owner's own machine by default, a waterfall the owner personally killed.
*Three ways out, and the owner picks:* **(a)** confirm §6.4 and re-port the fifteen-line status from master's history (`git log -S Build_PeriodicStatusText master`); **(b)** keep the key but make it re-post PULSE's own text on the slot instead of a second format, so "one status surface" survives and the key only controls whether it is announced; **(c)** flip the shipped default and classic to `false`, leaving the key as the way to ask for it back. **Recommended: (c)**, with (b) as the implementation if they ever say yes — a default that restores a deleted feature is a default nobody chose.

**D2 — `topic.onClose` ships `close`, and the only close path in the tree is DELETE. Blocks Task 10.**
Spec §6.4 and §11.5 say default `close` (master's behaviour), quiet `delete`. But `closeForumTopic` appears nowhere in this repo — `TelegramApiClientModel` has `deleteForumTopic` and nothing else — and `TopicDelete_Decider`'s own docstring records an owner decision going the other way: *"the owner's decision, recorded in the same brief, is that topics ARE deleted (they will have thousands, and a list full of finished ones is noise)"*. Two owner statements, two dates, opposite directions.
*Decision:* which statement stands, and therefore whether Task 10 adds a `closeForumTopic` call (roughly 60 lines across client, interface, two fakes and the engine branch) or whether `close` is dropped from the enum and `topic.onClose` becomes a bool-shaped "delete or leave it alone". **Recommended: keep both values and default `delete`** — it matches the newer statement, it matches the code that exists, and `close` then costs the implementation only when someone asks for it.

**D3 — a configured button verb has no label. Blocks Task 5.**
`pulse.buttons` is a list of VERBS; the emoji-and-slash LABELS live in `TopicCommandButtons`' two private arrays, which cover eleven verbs in total. `classic` names `["screen","show","merge","test","pc","close","pause","progress"]` — six of those eight have no label anywhere. `BotCommandMenu.ALL` carries `(Command, Description)` and no emoji.
*Decision:* where a label comes from. **(a)** a verb→label table in `TopicCommandButtons` covering every verb in `BotCommandMenu.ALL`, which needs an emoji chosen for `/screen`, `/show`, `/test`, `/pc`, `/pause`, `/progress` and everything else the owner might name; **(b)** fall back to the bare `/verb` for any verb with no table entry, so a configured button always renders and the emoji is a nicety. **Recommended: (b) with (a) for the eleven that already have one** — a button that renders as `/screen` is usable; a button that cannot render at all makes a legal preset un-loadable.

**D4 — `general.buttons: []` in classic. Blocks Task 5.**
Telegram will not accept an empty `inline_keyboard`. An empty list must mean "send the General message with no `reply_markup` at all", which is what master did. *Decision: confirm that reading* (it is almost certainly right — master had no General bar and classic is master's way — but it is the difference between a working General topic and a 400 from Telegram on every status edit). **Recommended: confirm.**

**D5 — `phone.replyKeyboard` ships `off`, and classic turns it `on`. Blocks Task 9.**
Spec §11.2 records the default as `off` and asks the owner to confirm, and §7.6 says `on` cannot be master's code: the fork MEASURED on 2026-09-06 that deleting the carrier message deletes the bar, so `on` means **one permanent "⌨️ shortcuts ready" line living for ever in General**. `TopicCommandButtons`' own class doc is blunter: *"IT NEVER WORKED… Do not reintroduce it without re-measuring that premise first."* `ReplyKeyboardMarkupTests.NothingInstallsTheKeyboardYet_BecauseTheCarrierDeleteRemovesTheBar` currently asserts, by reading the engine's source, that nothing installs it.
*Decision:* does the owner accept a permanent junk line in General in exchange for the bar — given classic (their own preset) says `on`? **Recommended: ask, and if the answer is anything but an enthusiastic yes, set `classic`'s value to `off` and leave Task 9 implemented but unexercised by either preset.**

**D6 — which verbs the reply keyboard carries. Blocks Task 9 (only if D5 is yes).**
§7.6 says the rows must be lexer-legal (`/verb`, so `tail sup` renders `/tail sup`) but names no source list. *Decision:* `pulse.buttons`, `general.buttons`, or a third setting. **Recommended: `general.buttons`** — the bar lives in General, it is the General bar's verbs in another rendering, and it needs no new catalogue entry. Note that under classic `general.buttons` is empty (D4), which would mean `on` installs an empty bar — so D5 and D6 have to be answered together.

**D7 — `phone.appMessagesRing = false` may be a no-op on this tree. Blocks Task 3.**
The catalogue says the key governs *"whether the app's own messages arrive with a notification or silently"*. On this tree every app-written message is ALREADY `TelegramSendSounds.Silent` — the receipt (*"A RECEIPT NEVER RINGS"*), the busy narration, the turn-ended line, PULSE's edits. The only thing that rings is an AGENT's entry, through `Resolve_EntrySound`. So the literal reading makes the key do nothing, and quiet — which sets it `false` — would get exactly the phone it has today.
*Decision:* what `false` silences. **(a)** literally the app's own messages: a no-op today, honest, and the key is a promise for later; **(b)** agent narration that is not a question, a BLOCKED, a file or the answer — i.e. the `filtered` distinction applied to SOUND rather than to DELIVERY, so quiet's phone is "everything arrives, quietly" and classic's is "only what matters arrives, and it rings". **Recommended: (b)**, because it is the only reading under which the key changes anything and it is the only one that makes `phone.push=everything` + `appMessagesRing=false` a coherent pair; it reuses Task 2's predicate, so it costs almost nothing.

**D8 — does `phone.push` apply to the GENERAL channel? Blocks Task 2.**
§7.1 names "the owner-channel block of `Mirror_Append_Async`", which is reached for `append.Channel.IsOwnerChannel`. The general supervisor's channel is an owner channel too. Filtering it would mean the concierge's narration stops reaching the phone, which is most of what General is for.
*Decision.* **Recommended: orchestration owner channels only** — General is exempt, stated in code with this reason, and pinned by a test.

**D9 — does `phone.status.intervalMinutes` also move the AWAY digest? Blocks Task 8.**
One planner (`PeriodicStatusSlot_Planner`, `SLOT_MINUTES = 30`) governs both the away digest and the deleted periodic status; `AwayDigest_Decider`'s docstring records a 30-minute limit cycle that ran all night, locked to exactly that number. Making the interval configurable moves both unless they are separated.
*Decision.* **Recommended: the setting governs the PERIODIC STATUS only; the away digest keeps `SLOT_MINUTES`**, and the planner takes the slot length as a parameter so the two are visibly different numbers rather than accidentally the same one.

**D10 — `pulse.holdToggle = false` with `phone.receipts = reactions` leaves the owner no hold toggle at all. Blocks Task 5.**
`false` puts the toggle on the receipt (master); `reactions` means there IS no receipt message to hang it on. Classic (`false` + `ticks`) and quiet (`true` + `reactions`) are both coherent; the two cross combinations are not.
*Decision:* refuse the combination at load with a warning and fall back, or let it silently produce no toggle. **Recommended: fall back to the PULSE bar and log one warning line naming both keys** — decision 21's rule, and "one toggle in two places" is still respected because the fallback picks one.

**D11 — may a `_deferred` test be edited as well as moved? Blocks Task 2.**
`AIOrchestratorCoreLib.Tests/_deferred/README.md` says *"A file leaves this folder by being MOVED into its proper namespace folder, not by being rewritten: each one is master's file and master's assertions."* But `AStatusLineDoesNotSpendTheOwnersWaitTests.cs` writes `"telegramItalianLayer":false` into its config fixture — a key deleted with the translator. It is harmless (the loader ignores unknown keys) but it is a lie in a file a future session will read as authority.
*Decision.* **Recommended: move verbatim, then a SECOND commit deletes the dead key with a one-line message saying so** — the move stays auditable as a move.

**D12 — what "done" means for the flakiness campaign. Blocks Task 11's exit.**
Spec §9 says *"Done means five consecutive green runs on each OS."* This machine's own rule is ONE SUITE AT A TIME, and the plan-01 gate measured the family landing harder on a 2-core CI runner than locally. Five consecutive local Windows runs is roughly an hour of wall-clock during which no other session may test.
*Decision:* five local + CI green, or a smaller local bar with CI carrying the repetition. **Recommended: three consecutive local full runs green on this box AND two consecutive green runs of both CI legs**, with the set-of-names comparison recorded for each. State the actual bar in the report whatever it is.

---

## File Structure

**Created**

```
AIOrchestratorCoreLib/Configuration/PhoneSettings/
  IPhoneSettings.cs
  PhoneSettingsModel.cs
  PhoneSettings_Factory.cs
  PhoneSettings_Json.cs                 ← Parse(configRoot, presetTree), resolver-backed, never written
AIOrchestratorCoreLib/Configuration/PulseSettings/
  IPulseSettings.cs
  PulseSettingsModel.cs
  PulseSettings_Factory.cs
  PulseSettings_Json.cs

AIOrchestratorCoreLib/Bridge/PhonePushModes.cs          ← Filtered | Everything (+ Parse_OrFiltered)
AIOrchestratorCoreLib/Bridge/OwnerPushDecisions.cs      ← SendNow | HoldForDigest | Drop
AIOrchestratorCoreLib/Bridge/SuppressedEntries/         ← the digest store the filter needs (Task 2)
  ISuppressedEntries.cs
  SuppressedEntriesModel.cs
  SuppressedEntries_Factory.cs
AIOrchestratorCoreLib/Telegram/ReceiptStyles.cs         ← Ticks | Reactions (+ Parse_OrTicks)
AIOrchestratorCoreLib/Telegram/ModeGlyphPlacements.cs   ← Name | PulseHeader (+ Parse_OrPulseHeader)
AIOrchestratorCoreLib/Telegram/TopicCloseActions.cs     ← Delete | Close (+ Parse_OrDefault)
AIOrchestratorCoreLib/Telegram/ReplyKeyboardModes.cs    ← Off | On (+ Parse_OrOff)
AIOrchestratorCoreLib/Telegram/CommandButton_Labels.cs  ← verb -> label, with the `/verb` fallback (Task 5, D3)
AIOrchestratorCoreLib/Bridge/PeriodicStatus/            ← only if D1 answers (a) or (b)
  PeriodicStatus_Builder.cs

AIOrchestratorCoreLib.Tests/Configuration/PhoneSettings/PhoneSettingsJsonTests.cs
AIOrchestratorCoreLib.Tests/Configuration/PulseSettings/PulseSettingsJsonTests.cs
AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingValidatorsTests.cs
AIOrchestratorCoreLib.Tests/Bridge/OwnerPushDeciderTests.cs
AIOrchestratorCoreLib.Tests/Bridge/WhoRingsUnderEachPresetTests.cs
AIOrchestratorCoreLib.Tests/Bridge/ReceiptStyleTests.cs
AIOrchestratorCoreLib.Tests/Bridge/TopicOnCloseTests.cs
AIOrchestratorCoreLib.Tests/Telegram/PulseFieldsAreConfigurableTests.cs
AIOrchestratorCoreLib.Tests/Telegram/ConfigurableCommandButtonsTests.cs
AIOrchestratorCoreLib.Tests/TestSupport/TestFile_Reader.cs          ← Task 11
AIOrchestratorCoreLib.Tests/TestSupport/AiorchEnvironment_Scrub.cs  ← Task 11
AIOrchestratorCoreLib.Tests/TestSupport/NoRawFileReadsGuardTests.cs ← Task 11

docs/superpowers/plans/2026-09-12-behavioural-seams-03-report.md    ← Task 12
```

**Moved (not rewritten — see D11)**

```
AIOrchestratorCoreLib.Tests/_deferred/AStatusLineDoesNotSpendTheOwnersWaitTests.cs
  -> AIOrchestratorCoreLib.Tests/Bridge/                            ← Task 2
AIOrchestratorCoreLib.Tests/_deferred/ModelOnTheStatusLineTests.cs
  -> AIOrchestratorCoreLib.Tests/Telegram/                          ← Task 4
AIOrchestratorCoreLib.Tests/_deferred/README.md                     ← deleted by Task 12 once both are out
```

**Modified**

| file | change | task |
|---|---|---|
| `Configuration/OrchestratorConfig/IOrchestratorConfig.cs`, `OrchestratorConfigModel.cs`, `OrchestratorConfig_Factory.cs` | `Phone` and `Pulse` blocks beside `Effort` | 1 |
| `Configuration/OrchestratorConfig_Loader.cs` | two `*_Json.Parse(configRoot, preset)` calls on the existing preset rung | 1 |
| `Configuration/TelegramProseSettings/TelegramProseSettings_Json.cs` | resolves through the catalogue, so `phone.foldLongEntriesAbove` works and `telegram.*` keeps working as the alias | 1 |
| `Configuration/SettingsCatalog/SettingValidators.cs` | `PULSE_FIELDS` and `BOT_COMMANDS` implemented; `LISTEN_ADDRESS` stays a registered name | 1 |
| `Configuration/SettingsCatalog/SettingsCatalog.cs` | every `INERT_NOTE` removed from a row this plan wires; two descriptions gain a cost sentence (Tasks 4, 5) | each |
| `Bridge/OwnerPush_Policy.cs` | `Decide(mode, …) → OwnerPushDecisions`; `Should_Push` becomes the `Everything` arm | 2 |
| `Bridge/BridgeEngine/BridgeEngineModel.cs` | the owner-channel block (~3901–3950), `Resolve_EntrySound` (~4163), `Build_TurnEndedText` (~15321), `Send_ReceivedAck_Async` (~13822), `Push_AwayDigests_Async` (~14026), the two button call sites (8480, 8540), the pulse call site (10386), the close path (~6146, ~6661) | 2–10 |
| `Telegram/TopicStatusLine_Builder.cs`, `TopicStatusLine_Planner.cs` | `Build` takes the field list and the step; one private per field | 4 |
| `Formatting/UnchangedFor_Formatter.cs` | `Describe_OrNull(TimeSpan, int stepMinutes)`; `STEP_MINUTES` stays as the default value only | 4 |
| `Telegram/TopicCommandButtons.cs` | builders take the resolved verb list; `KNOWN_COMMANDS` becomes the wider set | 5 |
| `Telegram/TelegramDeliveryModes.cs` | `TopicNameFlags` regains the four mode inputs; `Compose_TopicName` / `Strip_Glyph` / `Build_HeaderLine` honour the placement | 7 |
| `Telegram/TelegramApiClient/ITelegramApiClient.cs`, `TelegramApiClientModel.cs` | `Send_MessageWithReplyKeyboard_Async` (9), `Close_ForumTopic_Async` (10, only under D2) | 9, 10 |
| the Telegram fakes in `Tests/Bridge/OwnerAnswerSurvivesFailedSendTests.cs` and every other `ITelegramApiClient` stub | the new interface members | 9, 10 |
| `Tests/Bridge/OwnerPushPolicyTests.cs`, `OwnerAnswerSurvivesFailedSendTests.cs`, `ThePhoneRingsOnlyForTheSupervisorTests.cs`, `TypingBubbleReplacesTheStatusMessagesTests.cs`, `ClosingATopicReallyDeletesItTests.cs` | parameterised over the mode they now depend on | 2–10 |
| `Tests/Telegram/TopicStatusLineBuilderTests.cs`, `TopicCommandButtonsTests.cs`, `TopicCommandButtonsHoldToggleTests.cs`, `TelegramDeliveryModeGlyphsTests.cs`, `GeneralDashboardTests.cs`, `EveryTopicButtonIsWiredTests.cs`, `ReplyKeyboardMarkupTests.cs` | same | 4–9 |
| `Tests/Configuration/SettingsCatalog/PresetProbeTests.cs` | extended from "the resolved catalogue" to "the phone" | 12 |
| ~34 files under `Tests/Bridge/` and the rest of the test project | raw `File.ReadAllText` → the tolerant test reader | 11 |
| `Tests/Running/REAL_TIME_COLLECTION.cs` + the named wall-clock classes | collection membership | 11 |

---

### Task 1: The two settings blocks — the one way a seam reads a value

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/PhoneSettings/IPhoneSettings.cs`, `PhoneSettingsModel.cs`, `PhoneSettings_Factory.cs`, `PhoneSettings_Json.cs`
- Create: `AIOrchestratorCoreLib/Configuration/PulseSettings/IPulseSettings.cs`, `PulseSettingsModel.cs`, `PulseSettings_Factory.cs`, `PulseSettings_Json.cs`
- Create: `AIOrchestratorCoreLib/Bridge/PhonePushModes.cs`, `AIOrchestratorCoreLib/Telegram/ReceiptStyles.cs`, `ModeGlyphPlacements.cs`, `TopicCloseActions.cs`, `ReplyKeyboardModes.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig/IOrchestratorConfig.cs`, `OrchestratorConfigModel.cs`, `OrchestratorConfig_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/TelegramProseSettings/TelegramProseSettings_Json.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingValidators.cs`
- Create: `AIOrchestratorCoreLib.Tests/Configuration/PhoneSettings/PhoneSettingsJsonTests.cs`, `AIOrchestratorCoreLib.Tests/Configuration/PulseSettings/PulseSettingsJsonTests.cs`, `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingValidatorsTests.cs`

**Interfaces:**
- Consumes: `SettingsCatalog.Find_OrNull(path)`, `Settings_Resolver.Resolve` / `Resolve_String_OrNull` / `Resolve_Bool` / `Resolve_Long`, `PulseField_Names.ALL`, `BotCommandMenu.ALL`, `TopicCommandButtons.Commands` / `.GeneralCommands`, `OwnerMessage_Folder.DEFAULT_FOLD_THRESHOLD`, `OwnerDocument_Builder.DEFAULT_ATTACH_ABOVE_CHUNKS`, and plan 02 Task 7's `EffortSettings_Json` **as the shape to copy**.
- Produces:
  - `IOrchestratorConfig.Phone : IPhoneSettings` — `Push`, `PeriodicStatus`, `PeriodicStatusIntervalMinutes`, `AppMessagesRing`, `ReplyKeyboard`, `Receipts`, `TopicOnClose`, `TopicModeGlyphs`
  - `IOrchestratorConfig.Pulse : IPulseSettings` — `Fields`, `StepMinutes`, `Buttons`, `GeneralButtons`, `HoldToggle`
  - `SettingValidators.Validate_OrNull` answering for `PULSE_FIELDS` and `BOT_COMMANDS`
  - `TelegramProseSettings_Json.Parse(configRoot, presetTree)` — signature gains the preset tree

**Why the split is by PATH PREFIX and not strictly by category:** the catalogue files `phone.receipts` and `pulse.holdToggle` under `Receipts`, but a block is a JSON neighbourhood, not a menu section — so `phone.*` and `topic.*` go on `IPhoneSettings`, `pulse.*` and `general.buttons` on `IPulseSettings`. Write that sentence in `IPhoneSettings`' own doc: a later reader will otherwise "fix" it back to three blocks and break both.

- [ ] **Step 1: Re-read the five files plan 02 Task 7 touched.** `Configuration/EffortSettings/EffortSettings_Json.cs` (the shape), `OrchestratorConfig_Loader.cs` (the preset rung and where `EffortSettings_Json.Parse` is called from), `IOrchestratorConfig.cs`, `OrchestratorConfigModel.cs`, `OrchestratorConfig_Factory.cs`. **Confirm Task 7 has merged** — `git log --oneline master | head -20` must show it. If it has not, STOP and say so; do not start.

- [ ] **Step 2: Write the failing tests — the two blocks, both presets, the four rungs**

Create `AIOrchestratorCoreLib.Tests/Configuration/PhoneSettings/PhoneSettingsJsonTests.cs`. Use the same temp-`ISupervisionPaths` fixture `PerRoleModelDefaultsTests` uses, and write real JSON to `ConfigFile` — this is a loader test, not a resolver test, and the resolver already has its own.

```csharp
/// <summary>
/// A MACHINE THAT SAYS NOTHING GETS classic (Presets_Loader: `preset` absent means classic), and
/// classic is Manu's phone. These four values are the ones classic actually STATES — every other
/// row falls through to the catalogue's shipped default, which is the next test.
/// </summary>
[Fact]
public void WithNoConfigFileAtAll_ThePhoneBlockIsClassics()
{
    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    Assert.Equal(ReplyKeyboardModes.On, config.Phone.ReplyKeyboard);
    Assert.Equal(ModeGlyphPlacements.Name, config.Phone.TopicModeGlyphs);

    // Not stated by classic — the catalogue's own defaults.
    Assert.Equal(PhonePushModes.Filtered, config.Phone.Push);
    Assert.Equal(ReceiptStyles.Ticks, config.Phone.Receipts);
    Assert.True(config.Phone.AppMessagesRing);
    Assert.Equal(30, config.Phone.PeriodicStatusIntervalMinutes);
}

/// <summary>Nathan's phone, and the row-for-row inverse of the one above.</summary>
[Fact]
public void UnderTheQuietPreset_ThePhoneBlockIsTheForks()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    Assert.Equal(PhonePushModes.Everything, config.Phone.Push);
    Assert.False(config.Phone.PeriodicStatus);
    Assert.False(config.Phone.AppMessagesRing);
    Assert.Equal(ReceiptStyles.Reactions, config.Phone.Receipts);
    Assert.Equal(TopicCloseActions.Delete, config.Phone.TopicOnClose);
    Assert.Equal(ReplyKeyboardModes.Off, config.Phone.ReplyKeyboard);
}

/// <summary>config.json beats the preset — the third rung, proven through the loader.</summary>
[Fact]
public void AValueInConfigJson_BeatsTheNamedPreset()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet","phone":{"receipts":"ticks"}}""");

    Assert.Equal(ReceiptStyles.Ticks, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.Receipts);
}

/// <summary>
/// A MISSPELLED VALUE COSTS THAT KEY ITS DEFAULT, NEVER THE LOAD — the rule EffortSettings_Json
/// states and the reason OrchestratorConfig_Loader catches a bad `preset` word: the provider calls
/// this on every tick with no try/catch above it, so a config the app refuses to load is a bridge
/// that does not start.
/// </summary>
[Fact]
public void AMisspelledReceiptStyle_FallsToTheDefault_AndDoesNotThrow()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"phone":{"receipts":"emoji"}}""");

    Assert.Equal(ReceiptStyles.Ticks, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.Receipts);
}

/// <summary>
/// THE RE-HOMED PAIR. `telegram.foldLongEntriesAbove` is what both machines' config.json already
/// says; `phone.foldLongEntriesAbove` is what the catalogue registers. Both must resolve, and the
/// new spelling must win when a file carries both — the resolver's stated order, proven here at the
/// loader so the alias cannot quietly stop being read.
/// </summary>
[Fact]
public void TheOldTelegramSpelling_StillResolves_AndTheNewOneWins()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"telegram":{"foldLongEntriesAbove":100}}""");
    Assert.Equal(100, OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramProse.FoldLongEntriesAbove);

    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"telegram":{"foldLongEntriesAbove":100},"phone":{"foldLongEntriesAbove":250}}""");
    Assert.Equal(250, OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramProse.FoldLongEntriesAbove);
}
```

Create `PulseSettingsJsonTests.cs` with the same four shapes: classic's `fields` are `["supervisor","members","modelEffort","merged","updated"]` and `holdToggle` is `false`; quiet states neither, so both fall to the catalogue's seven-field list and `true`; `pulse.stepMinutes` is `UnchangedFor_Formatter.STEP_MINUTES` under both; an out-of-range `stepMinutes` (0, or 61) falls to the default.

Create `SettingValidatorsTests.cs`:

```csharp
[Fact] public void PulseFields_RefusesAWordThatIsNotAField()       // ["supervisor","suprvisor"] -> message names the word
[Fact] public void PulseFields_RefusesARepeat()                    // ["updated","updated"]
[Fact] public void PulseFields_AcceptsTheShippedDefault()          // and accepts the empty list — a pulse with no fields is legal
[Fact] public void BotCommands_ChecksOnlyTheFirstToken()           // "tail sup" passes; "tale sup" does not
[Fact] public void BotCommands_AcceptsBothShippedDefaults()        // TopicCommandButtons.Commands and .GeneralCommands
[Fact] public void BotCommands_AcceptsTheEmptyList()               // classic's general.buttons
[Fact] public void BotCommands_RefusesARepeatedVerb()
[Fact] public void ListenAddress_IsStillARegisteredNameOnly()      // pins the remaining gap as deliberate (plan 04)
```

Run them; they fail to compile. That is the red.

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PhoneSettings|FullyQualifiedName~PulseSettings|FullyQualifiedName~SettingValidators"
```

- [ ] **Step 3: The five enums and their parsers.** Each is `Telegram/` or `Bridge/` scoped, each a plain enum plus one `Parse_Or<Default>(string? word)` that is ordinal-case-insensitive and answers the default for anything it does not know — the shape `TelegramInbound_Modes.Parse_OrPoll` already uses. **The words are the catalogue's** (`"filtered"`, `"everything"`, `"ticks"`, `"reactions"`, `"name"`, `"pulseHeader"`, `"delete"`, `"close"`, `"off"`, `"on"`), and each parser's doc names the catalogue path it parses so the two spellings cannot drift.

- [ ] **Step 4: The two triples.** `PhoneSettingsModel` / `PulseSettingsModel` are `internal sealed`, get-only properties from a primary constructor; only `PhoneSettings_Factory.Create(...)` calls `new`. `_Json.Parse(JsonObject? configRoot, JsonObject? presetTree)` resolves each row through `SettingsCatalog.Find_OrNull(path)!` + the matching `Settings_Resolver` accessor and hands the values to the factory. **READ AND NEVER WRITTEN** — no `Write` method on either class, for the reason `EffortSettings_Json` gives in full (a save would materialise this build's answer into the owner's file as if they had chosen it). Copy that paragraph's reasoning, not its words.

- [ ] **Step 5: Hang them on the config and parse them in the loader.** `IOrchestratorConfig` gains `Phone` and `Pulse`; `OrchestratorConfig_Factory.Create` gains two parameters at the end beside `Effort`; `OrchestratorConfig_Loader.Load_OrEmpty` calls `PhoneSettings_Json.Parse(configRoot, preset)` and `PulseSettings_Json.Parse(configRoot, preset)` on the SAME `preset` tree the model and effort rungs use. `OrchestratorConfig_Factory.Create_Empty` supplies both blocks from a null tree, which is the catalogue's own defaults — so an absent config and an empty one still resolve identically (the ruling that removed the early return).

- [ ] **Step 6: `TelegramProseSettings_Json` resolves instead of parsing.** Its two keys are already registered with `LegacyPath_OrNull` pointing at `telegram.*`; the class currently reads the old spelling directly. Make it resolve through the catalogue definitions, the same two lines `EffortSettings_Json` uses. This is what turns the `INERT_NOTE` on those two rows into a lie that must be deleted — do that in the same commit.

- [ ] **Step 7: Implement the two validators.** In `SettingValidators.Validate_OrNull`, add the `PULSE_FIELDS` and `BOT_COMMANDS` cases, and **shorten the three "NOT YET IMPLEMENTED" docstrings to name only `LISTEN_ADDRESS`** — leaving them would be the stale-comment failure this codebase keeps paying for. `BOT_COMMANDS` checks the FIRST space-delimited token against `BotCommandMenu.ALL` and refuses a repeat of that token; it does NOT check what follows the space (`"tail sup"` is a verb with its target, and `ALL` holds the bare `tail`). Both accept the empty list.

- [ ] **Step 8: Verify.** Green for the three new files, and nothing else moves.

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PhoneSettings|FullyQualifiedName~PulseSettings|FullyQualifiedName~SettingValidators"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Configuration"
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
```

Report: the two blocks' property lists, the validators' rules, and the exact `INERT_NOTE` rows deleted.

---

### Task 2: Who rings — `phone.push`, the suppressed-entry digest, and the credit that was waiting for it

**Blocked by D8 and D11.** This is the task CLAUDE.md decision 25 names: *"The filter, the suppression list and the turn-end digest are plan 03's (`phone.push = filtered`) to restore as a per-user setting — the protective half of the credit landed here precisely so it is correct the day they return."*

**Files:**
- Create: `AIOrchestratorCoreLib/Bridge/OwnerPushDecisions.cs`, `AIOrchestratorCoreLib/Bridge/SuppressedEntries/ISuppressedEntries.cs`, `SuppressedEntriesModel.cs`, `SuppressedEntries_Factory.cs`
- Create: `AIOrchestratorCoreLib.Tests/Bridge/OwnerPushDeciderTests.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/OwnerPush_Policy.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — the owner-channel block (~3901–3950) and `Build_TurnEndedText` (~15321)
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/OwnerPushPolicyTests.cs`, `OwnerAnswerSurvivesFailedSendTests.cs`
- Move: `AIOrchestratorCoreLib.Tests/_deferred/AStatusLineDoesNotSpendTheOwnersWaitTests.cs` → `AIOrchestratorCoreLib.Tests/Bridge/`

**Interfaces:**
- Consumes: Task 1's `IOrchestratorConfig.Phone.Push`; the predicates `OwnerPush_Policy` ALREADY HAS and which nothing currently calls — `Carries_Question`, `Asks_InProse`, `Carries_FileForTheOwner`, `Is_OnlineGreeting`, `Is_OwnerRestatement`, `Is_TurnEndDeclaration`; the engine's `_ownerAwaitingAnswer` / `Raise_OwnerWait`; `PendingOwnerReply`.
- Produces:
  - `OwnerPushDecisions { SendNow, HoldForDigest, Drop }`
  - `OwnerPush_Policy.Decide(PhonePushModes mode, string rawEntryText, bool ownerIsWaitingForAReply, string? subject) : OwnerPushDecisions`
  - `ISuppressedEntries` — `File(orchId, subject, text)`, `Drain(orchId) : IReadOnlyList<(string? Subject, string Text)>`, `Forget(orchId)`
  - `Build_TurnEndedText` carrying the drained entries again

**The spec asks for an injected `IOwnerPushDecider`; this plan makes it a static `Decide` on the policy that already exists.** Reason: `OwnerPush_Policy` is a pure static with 25 tests against it and no state to inject; an interface would add a triple whose only implementation is the static. The seam the spec wanted — "the decision is a value, not a branch inline in the engine" — is delivered by `OwnerPushDecisions`. Recorded in the self-review; if the coordinator wants the interface, it is a 20-line wrapper and the tests do not move.

- [ ] **Step 1: Write the failing decider tests.** `OwnerPushDeciderTests.cs`, one class, `[Theory]` over the two modes:

```csharp
// Everything (quiet): exactly what Should_Push does today, in OwnerPushDecisions clothing.
[Fact] public void UnderEverything_ProgressNarration_IsSentNow()
[Fact] public void UnderEverything_AnOwnerRestatement_IsDropped()
[Fact] public void UnderEverything_AnEmptyBody_IsDropped()

// Filtered (classic): master's semantics, rebuilt from the predicates that are already here.
[Fact] public void UnderFiltered_AQuestion_IsSentNow()
[Fact] public void UnderFiltered_AProseQuestion_IsSentNow()
[Fact] public void UnderFiltered_BlockedOnOwner_IsSentNow()
[Fact] public void UnderFiltered_AnEntryCarryingAPicture_IsSentNow()
[Fact] public void UnderFiltered_TheBootGreeting_IsSentNow()
[Fact] public void UnderFiltered_TheAnswerWhileTheCreditIsOpen_IsSentNow()
[Fact] public void UnderFiltered_ProgressNarration_IsHeldForTheDigest()

/// <summary>
/// AN EMPTY BODY IS DROPPED, NEVER HELD, IN BOTH MODES — and this is the sharpest line in the file.
/// The engine's own comment records why the old filing had to go rather than merely stop mattering:
/// an empty entry "was filed and then released five minutes later, RINGING, wearing 'nothing has
/// moved for 5 min — sending you the last thing it said'. A blank message, with a notification,
/// about nothing."
/// </summary>
[Theory] public void AnEmptyBody_IsDroppedInEitherMode(PhonePushModes mode)

/// <summary>
/// A TURN-END DECLARATION IS NOT AN ANSWER AND MUST NOT BE ONE HERE EITHER. Is_TurnEndDeclaration
/// already stops the credit being CONSUMED in the engine (decision 25); under `filtered` it must
/// also stop a "WAITING ON …" subject being SENT as if it were the thing the owner is waiting for.
/// </summary>
[Fact] public void UnderFiltered_AWaitingOnSubject_IsHeld_EvenWhileTheOwnerWaits()
```

- [ ] **Step 2: `Decide`.** `Everything` returns `Drop` for an empty body or a restatement and `SendNow` otherwise — byte-for-byte what `Should_Push` answers today, so quiet's phone cannot move. `Filtered` returns `Drop` for the same two, `SendNow` for question ∪ prose-question ∪ `BLOCKED ON OWNER` ∪ file ∪ boot greeting ∪ (`ownerIsWaitingForAReply` AND NOT `Is_TurnEndDeclaration(subject)`), and `HoldForDigest` for the rest. **Keep `Should_Push` as a thin call into `Decide(Everything, …)`** with its docstring rewritten to say that it is now one arm of two — it has callers this task has not read, and deleting it is a wider change than this task's concern.

- [ ] **Step 3: The suppressed-entry store, OUT of the engine.** `Bridge/SuppressedEntries/` — a triple around a `Dictionary<string, List<(string? Subject, string Text)>>` behind its own lock. **It is a LIST, not a slot**, and that is the whole point: decision 25 records that *"the suppressed memo was one slot, so the status line written after the answer overwrote it and the turn-ended receipt delivered the wrong text."* Cap it (say 20 entries per orchestration, oldest dropped with a log line) — an orchestration that runs for a day with the owner away must not grow a digest nobody can read. `Forget(orchId)` on close.

- [ ] **Step 4: Wire the engine's owner-channel block.** Resolve the mode ONCE per append from `_configProvider.Get_Current().Phone.Push` (D8: `Everything` unconditionally when `append.Channel.OrchId` is the general channel, with the reason in a comment). Switch on the decision:
  - `Drop` → `deliveredHere++; continue;` — **keep the existing increment and its comment verbatim.** The held-append memo is POSITIONAL; counting only the sent ones leaves the prefix short by one and the resume re-sends an entry the owner already had.
  - `HoldForDigest` → file into the store, then the same `deliveredHere++; continue;`, for exactly the same reason.
  - `SendNow` → `answersTheOwnersWait = true` and fall through, unchanged.
  Delete the empty `lock (_ownerStateLock) { }` block below it while you are in there — it is a leftover from the filter's removal and it locks nothing.

- [ ] **Step 5: Re-attach the digest to `Build_TurnEndedText`.** The method's own comment names the re-attachment point: *"THE 'LAST WORDS' HALF IS GONE WITH THE FILTER (2026-09-09) … `string? lastWords = null;`"*. Under `Filtered`, `lastWords` becomes the drained store joined into one message; under `Everything` it stays null and the method behaves exactly as today. **A completion is SENT, never edited** — that rule is already written at the call site and must survive. Drain on turn end whether or not anything is sent, so a mode change mid-turn cannot leave a stale digest to arrive hours later.

- [ ] **Step 6: Parameterise the two existing test files.** `OwnerPushPolicyTests.ProgressNarration_IsPushed_NowThatTheFilterIsGone` becomes mode-parameterised and keeps its name's meaning under `Everything`. `OwnerAnswerSurvivesFailedSendTests` — read the file's own comment recording that its *"narration after the answer is not pushed"* oracle was RETIRED with the filter — **restore that oracle under `Filtered` and keep the current one under `Everything`**, both in the same class, each saying which mode it measures. Do not delete the current one: "the channel is not wedged afterwards" is a real property that quiet still needs.

- [ ] **Step 7: Un-park the deferred oracle.** `git mv AIOrchestratorCoreLib.Tests/_deferred/AStatusLineDoesNotSpendTheOwnersWaitTests.cs AIOrchestratorCoreLib.Tests/Bridge/` — **verbatim, in its own commit** (D11). Its fixture writes no `preset`, so it runs under classic, which is `filtered` — which is exactly the mode its five oracles were written for. Then, in a SECOND commit, delete the dead `"telegramItalianLayer":false` from its config fixture with a one-line message saying so. If it goes red, that is the task failing, not the test being stale: read its docstring before touching an assertion.

- [ ] **Step 8: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~OwnerPushDeciderTests|FullyQualifiedName~OwnerPushPolicyTests"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~OwnerAnswerSurvivesFailedSendTests|FullyQualifiedName~AStatusLineDoesNotSpendTheOwnersWait"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Bridge" 2>&1 | tail -20
```

The last one is the wide net for this task, and it is the run most exposed to the flake family — **isolate any red with `--filter` and re-run it alone before believing it**, and name it in the report either way.

Report: the piece moved out of `BridgeEngineModel.cs` (the suppressed store), every line changed in the owner-channel block, and which mode each restored oracle measures.

---

### Task 3: What rings — `phone.appMessagesRing`

**Blocked by D7.** Small on purpose, and separate from Task 2 because §7.1 says so: *"Sound is separate."* Delivery and notification are two questions and an owner may want either answer with either.

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — `Resolve_EntrySound` (~4163) and its four call sites (4029, 4035, 4060, 4063)
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/ThePhoneRingsOnlyForTheSupervisorTests.cs`
- Create: `AIOrchestratorCoreLib.Tests/Bridge/WhoRingsUnderEachPresetTests.cs`

**Interfaces:**
- Consumes: Task 1's `Phone.AppMessagesRing`; Task 2's `OwnerPush_Policy.Decide` (under D7 answer (b), the SendNow/HoldForDigest distinction is the thing that rings or does not).
- Produces: `Resolve_EntrySound(entry, bool appMessagesRing, PhonePushModes mode)` — one helper, still the ONLY thing the four mirror sites call.

- [ ] **Step 1: Measure before you change anything.** Grep every `TelegramSendSounds.Rings` in `AIOrchestratorCoreLib/` and write the list into the report. The spec claims about thirty `TelegramSendSounds` call sites of which only four plus three are in scope; verify that number on THIS tree and say what you found. If the honest finding is that no app-written message rings today, D7 answer (a) is a no-op and you must say so rather than inventing a ring to silence.

- [ ] **Step 2: Write the failing test.** `WhoRingsUnderEachPresetTests` drives the engine with the fake under each preset and asserts the SOUND of each sent message — the fake records it. Under classic: a question rings, narration is not sent at all (Task 2). Under quiet: everything is sent and, under D7(b), only a question / BLOCKED / file / answer rings. Under both: the receipt, the busy narration and the turn-ended line are silent, which is already true and is pinned here so a later task cannot quietly make the app ring.

- [ ] **Step 3: Implement, then delete the row's `INERT_NOTE`.**

- [ ] **Step 4: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~WhoRingsUnderEachPreset|FullyQualifiedName~ThePhoneRingsOnlyForTheSupervisor"
```

---

### Task 4: The pulse — `pulse.fields`, `pulse.stepMinutes`, and the `modelEffort` field

**Files:**
- Modify: `AIOrchestratorCoreLib/Telegram/TopicStatusLine_Builder.cs`, `TopicStatusLine_Planner.cs`
- Modify: `AIOrchestratorCoreLib/Formatting/UnchangedFor_Formatter.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — the single pulse call site (~10386)
- Modify: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs` — `pulse.fields`' description gains the `updated` sentence
- Modify: `AIOrchestratorCoreLib.Tests/Telegram/TopicStatusLineBuilderTests.cs`
- Create: `AIOrchestratorCoreLib.Tests/Telegram/PulseFieldsAreConfigurableTests.cs`
- Move: `AIOrchestratorCoreLib.Tests/_deferred/ModelOnTheStatusLineTests.cs` → `AIOrchestratorCoreLib.Tests/Telegram/`

**Interfaces:**
- Consumes: Task 1's `Pulse.Fields` / `Pulse.StepMinutes`; `PulseField_Names` (the eight words); `ModelReading_Formatter.Describe_OrNull`; `ITopicStatusMember.Model` — **which exists and which `TopicStatusLine_Builder` never reads**, and that missing read is the whole of the `modelEffort` field.
- Produces:
  - `TopicStatusLine_Builder.Build(..., IReadOnlyList<string>? fields = null, int? stepMinutes = null)` iterating the field list
  - `UnchangedFor_Formatter.Describe_OrNull(TimeSpan unchangedFor, int stepMinutes)`
  - `Build_ModelEffortLine_OrNull` / the member-row model field

- [ ] **Step 1: Un-park the oracle FIRST, and watch it fail.** `git mv AIOrchestratorCoreLib.Tests/_deferred/ModelOnTheStatusLineTests.cs AIOrchestratorCoreLib.Tests/Telegram/`, verbatim. It calls `Build` with no field list at all (`aMessageIsAlreadyPosted: false` is its last argument), so it measures the SHIPPED default order — and the shipped default does NOT include `modelEffort`. **Read the file before assuming what it asserts**: its first case expects `"• imp-1 · wiring the context field · 30 min · Fable 5.1 xhigh"`, i.e. the member row carries the reading AFTER the duration, and the supervisor's rides the LEAD line. If master drew it unconditionally and the catalogue makes it opt-in, those are different behaviours and the honest resolution is a decision, not an edited assertion — raise it before touching the file.

- [ ] **Step 2: Write the failing configurability tests.** `PulseFieldsAreConfigurableTests`:

```csharp
[Fact] public void TheShippedList_ProducesExactlyTodaysLine()      // the regression guard: every other test in TopicStatusLineBuilderTests still passes
[Fact] public void ClassicsList_DropsWaitingOnYouAndClosedCount_AndAddsModelEffort()
[Fact] public void AFieldNamedTwice_IsDrawnTwice_OrIsRefused()     // decide and pin it; the validator already refuses repeats, so this asserts the validator's reach
[Fact] public void AnEmptyFieldList_LeavesTheHeaderAndNothingElse()
[Fact] public void TheFieldsAreDrawnInTheListedOrder_NotTheBuildersOrder()
[Fact] public void hasSubstance_StillGatesTheWholeMessage_WhateverTheList()

/// <summary>
/// ONE STEP, TWO READERS. The member row's duration and the heartbeat both round to the same
/// granularity, and a build where they disagree is CLAUDE.md decision 12's second copy arriving in
/// the one file that has already paid for it (SessionRows_Builder's duplicate duration wording).
/// </summary>
[Fact] public void TheStep_MovesTheMemberDurationAndTheHeartbeatTogether()
```

- [ ] **Step 3: Iterate the list.** `Build` keeps `Build_HeaderLine` first and unconditional, then walks the resolved list calling one private per word, then returns. Every field keeps its own "omit myself when I have nothing to say" rule — `hasSubstance` still gates the whole message and `Strip_Heartbeat` still makes the repost rule content-only. `null` fields means the shipped list, so the 30-odd existing call sites in tests compile unchanged; the engine always passes the resolved one.

- [ ] **Step 4: The step.** `UnchangedFor_Formatter` gains the parameterised overload; `STEP_MINUTES` survives **as the catalogue's default value and nothing else** — grep for every other reader and convert it. Do not add a second rounding helper: the class doc already explains why the step exists (an edit per minute across every open topic is a 429, measured 2026-09-10), and that reasoning gains a sentence saying the floor is now the owner's to lower and what it costs.

- [ ] **Step 5: The `modelEffort` field.** One private that reads `member.Model` through `ModelReading_Formatter.Describe_OrNull` for the member rows and the supervisor's reading for the lead line. It is in `PulseField_Names.ALL`, legal, and NOT in the shipped list — classic asks for it, quiet does not. Add to `pulse.fields`' catalogue description the one cost sentence: **omitting `updated` removes the heartbeat, which is what tells the owner a quiet orchestration from a dead app** — a frozen status line looks exactly like a correct one.

- [ ] **Step 6: The engine call site.** `TopicStatusLine_Planner.Plan` takes the two values and passes them through; the engine resolves them at ~10386 from `_configProvider.Get_Current().Pulse`. One call site, one read.

- [ ] **Step 7: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PulseFieldsAreConfigurable|FullyQualifiedName~ModelOnTheStatusLine"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~TopicStatusLine|FullyQualifiedName~UnchangedFor|FullyQualifiedName~ContextOnTheStatusLine"
```

---

### Task 5: The bars — `pulse.buttons`, `general.buttons`, `pulse.holdToggle`

**Blocked by D3, D4 and D10.**

**Files:**
- Create: `AIOrchestratorCoreLib/Telegram/CommandButton_Labels.cs`
- Modify: `AIOrchestratorCoreLib/Telegram/TopicCommandButtons.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — the two button call sites (8480, 8540) and the receipt's hold button (~13840)
- Modify: `AIOrchestratorCoreLib.Tests/Telegram/TopicCommandButtonsTests.cs`, `TopicCommandButtonsHoldToggleTests.cs`, `EveryTopicButtonIsWiredTests.cs`
- Create: `AIOrchestratorCoreLib.Tests/Telegram/ConfigurableCommandButtonsTests.cs`

**Interfaces:**
- Consumes: Task 1's `Pulse.Buttons` / `Pulse.GeneralButtons` / `Pulse.HoldToggle` and the `BOT_COMMANDS` validator; `BotCommandMenu.ALL`; `HoldButton_Data`.
- Produces:
  - `TopicCommandButtons.Build_ForTopic(IReadOnlyList<string> verbs, long threadId, bool isHolding, int heldCount, bool holdToggleOnTheBar)`
  - `TopicCommandButtons.Build_ForGeneral(IReadOnlyList<string> verbs, long threadId)` — empty list returns an empty result, and the CALLER sends no `reply_markup`
  - `CommandButton_Labels.For(verb) : string` — the table, with the `/verb` fallback
  - `TopicCommandButtons.Commands` / `.GeneralCommands` unchanged as the SHIPPED defaults the catalogue reads

- [ ] **Step 1: Write the failing tests.**

```csharp
/// <summary>
/// THE PARSER MUST ACCEPT WHAT THE BUILDER DREW. KNOWN_COMMANDS is a static HashSet of the eleven
/// verbs the two private arrays name; the moment `pulse.buttons` is configurable, a tap on a
/// perfectly legal configured button parses to null, falls through every handler, and NOTHING
/// HAPPENS WITH NOTHING LOGGED — which is the exact failure Parse_OrNull's own docstring says it
/// refuses to cause.
/// </summary>
[Fact] public void ATapOnAConfiguredVerb_ParsesBack()              // "cmd:pause:5" under classic
[Fact] public void ATapOnAVerbThatIsNotACommandAtAll_StillParsesToNull()
[Fact] public void ClassicsBar_RendersEightButtons_InTheStatedOrder()
[Fact] public void AVerbWithNoEmoji_RendersAsItsSlashCommand()     // D3(b)
[Fact] public void AnEmptyGeneralList_ProducesNoButtonsAtAll()     // D4
[Fact] public void TheHoldToggle_IsOnTheBar_WhenHoldToggleIsTrue()
[Fact] public void TheHoldToggle_IsOnTheReceipt_WhenHoldToggleIsFalse()
[Fact] public void TheHoldToggle_IsNeverInBothPlaces_UnderEitherPreset()
[Fact] public void WithHoldToggleFalseAndReactionReceipts_ItFallsBackToTheBar_AndLogsOnce()  // D10
[Fact] public void TheCallbackPayload_StaysUnder64Bytes_ForTheLongestConfigurableVerb()
```

The 64-byte case is not ceremony: the existing doc pins the worst case at 33 bytes for `cmd:tail sup:` plus a 20-character long, and a configurable verb list moves that ceiling into the owner's hands. Telegram rejects an over-long `callback_data` at SEND time, on the phone, where nothing here can see it.

- [ ] **Step 2: `KNOWN_COMMANDS` widens.** It becomes the union of `BotCommandMenu.ALL`'s verbs, the two shipped arrays and `"tail sup"` — i.e. every verb an owner may legally configure — rather than a list derived from the two arrays. State in its doc that the membership test is now "is this a command at all", not "is this on a bar", and why.

- [ ] **Step 3: `EveryTopicButtonIsWiredTests` changes what it walks.** Today it walks `Commands ∪ GeneralCommands` to prove each button has a case in the tap handler. With a configurable bar the guard must cover every verb the owner MAY put there — walk `BotCommandMenu.ALL` plus `"tail sup"`. **If that turns up an unwired verb, that is a real finding**: report it, and do not put the verb on a bar. Widening a guard is allowed; narrowing one to stay green is not.

- [ ] **Step 4: The labels.** `CommandButton_Labels` carries the eleven that exist today, verbatim (including the two reasons already written down: ⏳ for `/pending` matches PULSE's "waiting on you" field, 🌙 for `/dnd_all` is the deferred glyph), plus whatever D3 answers for the rest, plus the `/verb` fallback.

- [ ] **Step 5: The hold toggle, in exactly one place.** `Build_ForTopic` appends it only when `holdToggleOnTheBar`; `Send_ReceivedAck_Async`'s tick carries it only when NOT. Both sites read the same one value from `Pulse.HoldToggle`, and the test above pins that they are never both. Under D10's fallback, the warning line goes to `orchestrator.log.jsonl` once per process, naming both keys — never Telegram (decision 15).

- [ ] **Step 6: Delete the `INERT_NOTE` from the three rows, and delete the "NOTE FOR WHOEVER IMPLEMENTS THE botCommands VALIDATOR" paragraph from `pulse.buttons`' description** — Task 1 implemented it; the note is now a instruction to a person who has been.

- [ ] **Step 7: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ConfigurableCommandButtons|FullyQualifiedName~TopicCommandButtons|FullyQualifiedName~EveryTopicButtonIsWired"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~GeneralDashboard|FullyQualifiedName~DndKeepsTheSilentSurfacesCurrent"
```

---

### Task 6: The receipt — `phone.receipts`

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — `Send_ReceivedAck_Async` (~13822), `Publish_DeliveryReceipt_Async`, the `ReceiptWasReaction` writer (~4329)
- Create: `AIOrchestratorCoreLib.Tests/Bridge/ReceiptStyleTests.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/TypingBubbleReplacesTheStatusMessagesTests.cs`

**Interfaces:**
- Consumes: Task 1's `Phone.Receipts`; `OwnerReaction_Emoji.RECEIVED`; `PendingOwnerReply.ReceiptWasReaction`.
- Produces: nothing new — **the branch point already exists and the setting only chooses it.** `Send_ReceivedAck_Async` tries the reaction and falls back to the `"✓"` message; `Narrate_BusySupervisor_Async` already branches on `ReceiptWasReaction`; `Build_TurnEndedText` already keys its `✓✓` prefix off it. This is the cheapest seam in the plan and it should stay that way.

- [ ] **Step 1: Write the failing test.** `ReceiptStyleTests` drives the engine with the fake under each preset and asserts, for one owner message: under `reactions`, `setMessageReaction` was called with 👀 and no `"✓"` message was sent; under `ticks`, no reaction was attempted at all and a silent `"✓"` message was sent. Then pin the FALLBACK: under `reactions` with the fake refusing the reaction, the `"✓"` still arrives — *"an acknowledgement that silently did not happen is the owner watching their message vanish."*

- [ ] **Step 2: Implement.** `ticks` skips the reaction attempt entirely — do not attempt-and-discard, which would spend a Telegram call and a rate-limit slot per owner message. Keep the fallback exactly as it is; it is not optional and it is not the setting.

- [ ] **Step 3: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ReceiptStyle|FullyQualifiedName~TypingBubbleReplacesTheStatusMessages"
```

---

### Task 7: The glyphs — `topic.modeGlyphs`, and the two pauses on the name

**Files:**
- Modify: `AIOrchestratorCoreLib/Telegram/TelegramDeliveryModes.cs` — `TopicNameFlags`, `Compose_TopicName`, `Strip_Glyph`
- Modify: `AIOrchestratorCoreLib/Telegram/TopicStatusLine_Builder.cs` — `Build_HeaderLine`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — the `TopicNameFlags` construction site and the rename path
- Modify: `AIOrchestratorCoreLib.Tests/Telegram/TelegramDeliveryModeGlyphsTests.cs`

**Interfaces:**
- Consumes: Task 1's `Phone.TopicModeGlyphs`. **`TopicNameFlags.IsPausedByOwner` already exists** — §7.4's flag addition landed in plan 01, so this task is only the PLACEMENT.
- Produces: `TopicNameFlags` carrying the four mode inputs the fork removed (delivery mode, away, quiet, owner presence) and `Compose_TopicName` / `Build_HeaderLine` each drawing them only under their placement.

- [ ] **Step 1: Write the failing tests.** Under `pulseHeader`: the topic name carries only 🏁 💤 ✅ 🧪 ⏸ and the header carries 🌙 🔕 ✈ 🤐 💻 — today's behaviour, pinned as the default. Under `name`: the inverse, with the precedence `Build_HeaderLine`'s docstring records ("AWAY SUPERSEDES QUIET… TERMINAL REPLACES THE DELIVERY GLYPH") **moved verbatim**, because it was right and changing it in the same commit as a move makes a behaviour change look like a relocation. And in both: `Strip_Glyph` takes every glyph off a name an older build wrote, including 💤 and the five mode ones — the constants are deliberately kept for exactly this and the test must prove it.

- [ ] **Step 2: Implement.** The four inputs return to `TopicNameFlags`; the engine fills them; `Compose_TopicName` draws them only under `Name`, `Build_HeaderLine` only under `PulseHeader`. Never both — a test for that, in the file, next to the toggle test of Task 5.

- [ ] **Step 3: Record the cost in the catalogue's own description.** It is already there and it is unusually good — *"each rename is an editForumTopic call and EACH RENAME WRITES A SERVICE MESSAGE, so an app-wide mode change wrote a line into every one of the owner's threads to tell them something they had just done themselves."* Add only what changes: under `name`, an app-wide mode change (`/dnd_all`) renames every open topic in one sweep. Delete the `INERT_NOTE`.

- [ ] **Step 4: Check the rename path does not now fire per tick.** The mode inputs are reconciled every tick; a name composed from them must be compared against the CURRENT name before any `editForumTopic`. If that comparison does not already exist, it is part of this task, not a follow-up: without it `name` costs a service message per topic per tick. Say in the report which you found.

- [ ] **Step 5: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~TelegramDeliveryModeGlyphs|FullyQualifiedName~TopicStatusLine"
```

---

### Task 8: The periodic status — `phone.status.periodic`, `phone.status.intervalMinutes`

**Blocked by D1 and D9. If D1 answers (c), this task is two lines and a test: the key is read, it is false everywhere, and a test pins that nothing is ever sent. Do not build (a) or (b) on a guess.**

**Files:**
- Create (only under D1 (a)/(b)): `AIOrchestratorCoreLib/Bridge/PeriodicStatus/PeriodicStatus_Builder.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/PeriodicStatusSlot_Planner.cs` — the slot length becomes a parameter
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — `Push_AwayDigests_Async` (~14026), the `continue` at the non-away branch
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/` — the away-digest tests

**Interfaces:**
- Consumes: Task 1's `Phone.PeriodicStatus` / `Phone.PeriodicStatusIntervalMinutes`; `AwayDigest_Decider.Should_Send`; `Post_StatusEntry`.
- Produces: the non-away branch of `Push_AwayDigests_Async` doing something again, under the setting only.

- [ ] **Step 1: Read the two docstrings in full before writing a line** — `Push_AwayDigests_Async`'s and `AwayDigest_Decider`'s. Between them they record the 30-minute limit cycle that woke a session all night, the ten identical status messages in five and a half hours, and the reason the method is still named for something it no longer does. This task is capable of recreating both failures.

- [ ] **Step 2: Write the failing tests, in this order.**

```csharp
[Fact] public void UnderQuiet_NoPeriodicStatusIsEverSent()
[Fact] public void UnderClassic_<whatever D1 answers>()
/// <summary>
/// THE AWAY DIGEST DOES NOT MOVE (D9). One planner governed both and AwayDigest_Decider's own
/// docstring records a limit cycle locked to exactly SLOT_MINUTES. Changing the status interval
/// must leave the away cadence where it is, and the two numbers must be visibly different values
/// rather than accidentally the same one.
/// </summary>
[Fact] public void ChangingTheStatusInterval_LeavesTheAwayDigestOnItsOwnSlot()
/// <summary>
/// AND IT STILL DOES NOT WAKE A SESSION FOR NOTHING. A status entry is APPENDED TO THE CHANNEL,
/// and an append is what a session's watcher fires on. Whatever D1 answers, an unchanged status
/// must not be posted — the no-change guard is a correctness guard, not a politeness one.
/// </summary>
[Fact] public void AnUnchangedStatus_IsNotPosted()
[Fact] public void APausedOrMeetingOrhestration_IsSkippedBeforeTheSlotIsStamped()  // unchanged, pinned
```

- [ ] **Step 3: Implement to D1's answer**, and **rename `Push_AwayDigests_Async` to what it does now** — the method's own docstring complains that it is named for a deleted feature. The rename is the piece this task moves; if the method grows a second branch it moves out of `BridgeEngineModel.cs` entirely, per the code-conventions rule.

- [ ] **Step 4: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~AwayDigest|FullyQualifiedName~PeriodicStatus"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Bridge" 2>&1 | tail -20
```

---

### Task 9: The reply keyboard — `phone.replyKeyboard`

**Blocked by D5 and D6. If D5 answers no, implement nothing: set `classic`'s value to `off`, delete the row's `INERT_NOTE`, and leave `ReplyKeyboard_Markup` and its guard test exactly as they are. Say so in the report; that is a complete task.**

**Files:**
- Modify: `AIOrchestratorCoreLib/Telegram/TelegramApiClient/ITelegramApiClient.cs`, `TelegramApiClientModel.cs`
- Modify: every `ITelegramApiClient` stub in the test project (start from `FailableTelegram_Fake` in `Tests/Bridge/OwnerAnswerSurvivesFailedSendTests.cs`; `grep -rl "ITelegramApiClient" AIOrchestratorCoreLib.Tests/` for the rest)
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — the launch path
- Modify: `AIOrchestratorCoreLib/Telegram/TopicCommandButtons.cs` — the class doc's "IT NEVER WORKED" paragraph
- Modify: `AIOrchestratorCoreLib.Tests/Telegram/ReplyKeyboardMarkupTests.cs`

**Interfaces:**
- Consumes: Task 1's `Phone.ReplyKeyboard`; `ReplyKeyboard_Markup.Build` (**already in the tree, compiled, tested and deliberately uncalled**); D6's verb list.
- Produces: `ITelegramApiClient.Send_MessageWithReplyKeyboard_Async`, `Build_ReplyKeyboardRows`, and ONE carrier message in General at launch that is **never deleted**.

- [ ] **Step 1: Retire the guard honestly.** `ReplyKeyboardMarkupTests.NothingInstallsTheKeyboardYet_BecauseTheCarrierDeleteRemovesTheBar` reads the engine's SOURCE and asserts `Install_CommandKeyboard` does not appear in it. It is a good guard and it becomes false the moment this task lands. **Replace it, do not delete it**: the new guard asserts that the install site does NOT delete its carrier — `Delete_Message_Async` must not appear inside the install method. Keep its `Read_EngineSource` helper exactly as it is, including the throw: *"a harness that cannot find what it tests must refuse to run rather than certify the absence of the thing it never read"* (decision 20).

- [ ] **Step 2: Write the failing tests.** Under `off`: launch sends no carrier and no keyboard. Under `on`: exactly one carrier message reaches General, it is silent, its markup is `ReplyKeyboard_Markup.Build`'s output, it is never deleted, and a second launch does not add a second carrier (an app restart must not accumulate junk lines in General — this is the failure mode the fork actually measured, in reverse).

- [ ] **Step 3: Row text must be lexer-legal.** `/verb`, so `tail sup` renders `/tail sup` — the bar sends literal TEXT, which goes through the owner-message lexer, and a row that is not a legal command is a message the bridge cannot route.

- [ ] **Step 4: Rewrite the two stale paragraphs.** `TopicCommandButtons`' class doc says the bar was removed and *"Do not reintroduce it without re-measuring that premise first"* — the premise WAS re-measured (the fork, 2026-09-06: the delete is what killed it), so the paragraph becomes the record of that measurement and the pointer to this setting. `ReplyKeyboard_Markup`'s doc gains the carrier rule.

- [ ] **Step 5: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ReplyKeyboard"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Telegram" 2>&1 | tail -20
```

---

### Task 10: Closing a topic — `topic.onClose`

**Blocked by D2.**

**Files:**
- Modify (only under D2 "keep `close`"): `AIOrchestratorCoreLib/Telegram/TelegramApiClient/ITelegramApiClient.cs`, `TelegramApiClientModel.cs`, and every stub
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — the close path (~6146) and the delete retry (~6661)
- Modify: `AIOrchestratorCoreLib/Bridge/TopicDeletion/TopicDelete_Decider.cs` — its docstring's owner quote
- Create: `AIOrchestratorCoreLib.Tests/Bridge/TopicOnCloseTests.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Bridge/ClosingATopicReallyDeletesItTests.cs`

**Interfaces:**
- Consumes: Task 1's `Phone.TopicOnClose`; `TopicDelete_Decider`, `TopicDeleteSweep_Planner`, `IOrchestrationSession.PendingTopicDeleteUtc`.
- Produces: `ITelegramApiClient.Close_ForumTopic_Async` (under D2 only) and one branch in the close path.

- [ ] **Step 1: Write the failing tests.** Under `delete`: today's behaviour, unchanged — `deleteForumTopic`, the four-attempt backoff, the pending stamp, the reconciliation sweep. Under `close`: `closeForumTopic`, no pending stamp, no sweep, and **the topic is still in the owner's list afterwards** (that is the point of `close`). Under both: closing twice is idempotent.

- [ ] **Step 2: Implement, and do not let `close` inherit the delete's retry machinery by accident.** `TopicDelete_Decider`'s bounded retry exists because a failed delete leaves an orphan topic with no record; a failed close leaves a topic that is merely still open, which the next close attempt fixes. Say which of the two gets the retry and why, in code.

- [ ] **Step 3: Correct the docstring.** `TopicDelete_Decider` says flatly *"the owner's decision … is that topics ARE deleted"*. After this task that is one of two configured behaviours; the docstring says so and cites D2's answer with its date. Delete the `INERT_NOTE` from the row.

- [ ] **Step 4: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~TopicOnClose|FullyQualifiedName~ClosingATopicReallyDeletesIt|FullyQualifiedName~TopicDelete"
```

---

### Task 11: The `Bridge/` flakiness campaign — a method, not a wish

**Independent of every other task. Can start on day one, in its own worktree, by its own session.** `.claude/rules/git-and-boundaries.md` books this to plan 03 by name; spec §9's last bullet and §10 phase 6 are its scope.

**The three mechanisms, from `docs/superpowers/plans/2026-09-11-fork-merge-01-report.md` ("THE OTHER REDS ARE A FLAKE FAMILY, AND THE PROOF IS THAT THEY NEVER REPEAT"):**

| mechanism | named victims | fix |
|---|---|---|
| **Windows file sharing, on the TEST side** — a bare `File.ReadAllText` while the engine is mid-`Atomic_FileWriter` rename gives *"The process cannot access the file … owner-channel.md because it is being used by another process"* | `AttachmentsReachThePhoneTests.Channel_Text`, `QuestionContractProbeTests.Channel` (CI) | the test helpers read through the production reader that already fixed this for production (`Storage/Tolerant_FileReader`, `e72cc84`) |
| **Wall-clock overshoot** — `Drive_Until` polls a deadline while work continues behind it, so a loaded box lets one more turn land between two polls. *"Never a wrong answer, always an extra one."* | `WakeUpDigestSecondReviewTests`, `ClosingTurnTests` (CI), `PrintRunnerReviewFixTests` (CI, Linux), `AStalledWriterNeverTearsATrailingEntryTests` | serialise the measuring classes into `REAL_TIME_COLLECTION`; **never raise a budget, never loosen an assertion** — `REAL_TIME_COLLECTION`'s own doc explains why both are refusals |
| **Environment leak** — `AIORCH_MEMBER` reaching the append tool from the session running the suite; seen once, with a scrubbed shell, *"most likely through a reused MSBuild node started before the scrub"* | `ChannelAppendTypedEntriesTests.AWellFormedQuestionIsWritten_…` | the scrub moves from one test file into a shared fixture |

**Measured on this branch, 2026-09-12, branch source in `AIOrchestrator-plan02`** (the gate report said "141 raw `File.ReadAllText` calls … 34 files of them under `Bridge/`"; the file count matches exactly and the call count does not — **re-measure with the commands below and report both numbers before converting anything**; a campaign that cannot count its own surface cannot report progress):

```bash
cd AIOrchestratorCoreLib.Tests
grep -rn "File.ReadAllText" --include=*.cs . | wc -l      # 281 occurrences
grep -rl "File.ReadAllText" --include=*.cs . | wc -l      # 133 files
grep -rn "File.ReadAllText" --include=*.cs ./Bridge | wc -l   # 68 occurrences
grep -rl "File.ReadAllText" --include=*.cs ./Bridge | wc -l   # 34 files  <- matches the report exactly
grep -rn "File.ReadAllLines" --include=*.cs . | wc -l    # 15
grep -rn "Drive_Until" --include=*.cs . | wc -l          # 149 call sites
```

**Files:**
- Create: `AIOrchestratorCoreLib.Tests/TestSupport/TestFile_Reader.cs`, `AiorchEnvironment_Scrub.cs`, `NoRawFileReadsGuardTests.cs`
- Modify: ~34 files under `AIOrchestratorCoreLib.Tests/Bridge/`, then the rest of the project
- Modify: `AIOrchestratorCoreLib.Tests/Running/REAL_TIME_COLLECTION.cs` and the named wall-clock classes
- Modify: `AIOrchestratorCoreLib.Tests/Channels/ChannelAppendHelperInteropTests.cs`, `ChannelAppendTypedEntriesTests.cs`

- [ ] **Step 1: Re-measure and record.** Run the block above and put the numbers in the report. If they differ from the gate's, say so plainly and give both — the discrepancy is data, not an embarrassment.

- [ ] **Step 2: `TestSupport/TestFile_Reader.cs`.** `Read_AllText(path)` and `Read_AllLines(path)` delegating to `Storage.Tolerant_FileReader.Read_AllText` — **production's reader, not a second implementation**. It throws when it gives up, exactly as `File.ReadAllText` does, so a converted test's failure mode is unchanged except for the race. Its doc cites `e72cc84` and the three tests the race landed on.

- [ ] **Step 3: Convert `Bridge/` first, in one commit, mechanically.** 34 files, 68 occurrences. Write the rewrite as a script file (`Write` then run it — a heredoc over ~6 KB dies as a fake quote error) and make it a plain textual substitution of `File.ReadAllText(` → `TestFile_Reader.Read_AllText(` plus the `using`. **Then read the diff** — a sub-agent's report is not evidence and neither is a script's. Build, then run the `Bridge` filter.

- [ ] **Step 4: Convert the rest, in a second commit.** Same method, the remaining ~99 files. Two commits, because `Bridge/` is the named family and the rest is prophylaxis; a reviewer must be able to see which is which.

- [ ] **Step 5: The guard, written so it cannot certify nothing.** `NoRawFileReadsGuardTests` walks the test project's own sources and fails on a raw `File.ReadAllText` under the converted folders. **It locates the sources by walking up from `AppContext.BaseDirectory` and THROWS if it cannot find them** — copy `ReplyKeyboardMarkupTests.Read_EngineSource` exactly, including its exception message, because decision 20's whole lesson is that a harness which cannot find what it tests reported 16 confident failures about code it never executed. Assert on ONE state with ONE route to it: the guard passes because the folder was found AND contained no raw read, never because either.

- [ ] **Step 6: Serialise the wall-clock classes.** Add `[Collection(REAL_TIME_COLLECTION.NAME)]` to the four named classes that are not already in it (measure first — `MemberTrafficRidesOneDigestedTurnTests`, `PrintTurnLimitResetTests`, `ClosingTurnReviewFixTests` and `WakeUpDigestReviewFixTests` already are). Extend `REAL_TIME_COLLECTION`'s docstring with the names added and the CI evidence from the gate report. **Do not raise a budget and do not loosen an assertion** — both refusals are already written in that file and both must survive this task.

- [ ] **Step 7: The env scrub, in a fixture.** Move the `AIORCH_*` scrub out of `ChannelAppendHelperInteropTests` into `TestSupport/AiorchEnvironment_Scrub.cs` and apply it to `ChannelAppendTypedEntriesTests` as well — the gate report's own one-line follow-up. The scrub must clear `AIORCH_ID` and `AIORCH_MEMBER`, not only `AIORCH_ROLE`.

- [ ] **Step 8: The bar (D12).** Run the full suite the number of times D12 settles on, **one suite at a time on this box**, and record for each run: the count, the elapsed, and the SET OF NAMES of anything red. Then CI on both legs. Compare the sets, never the counts.

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Bridge" 2>&1 | tail -20
dotnet test AIOrchestratorCoreLib.Tests 2>&1 | tail -30     # repeat per D12, one at a time
dotnet test tools/claude-contract/ClaudeContract.Tests 2>&1 | tail -10
```

Report: both measurement sets (before and after), the two commits' file counts, every name that went red in any run and whether it repeated, and the exact bar met.

---

### Task 12: The gate — the preset probes reach the phone

**Files:**
- Modify: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetProbeTests.cs`
- Delete: `AIOrchestratorCoreLib.Tests/_deferred/README.md` and the now-empty `_deferred/` exclusion in `AIOrchestratorCoreLib.Tests.csproj`
- Create: `docs/superpowers/plans/2026-09-12-behavioural-seams-03-report.md`

**Interfaces:**
- Consumes: everything Tasks 1–11 produced.
- Produces: the phase-3 gate evidence of spec §10.

- [ ] **Step 1: Extend the probes from the catalogue to the phone.** Plan 02's `PresetProbeTests` measures the RESOLVED VALUE of every entry under each preset, and its own docstring says plan 03 **extends** it rather than replacing it. Add one end-to-end probe per preset that drives the real engine with the fake Telegram client through: an owner message → a supervisor narration entry → a supervisor answer → a question with options → a tap. Assert **the phone-visible sequence**: what was sent, what was edited, what was reacted to, in what order, and with which sound. Spell the expectations out rather than deriving them from the preset files — spec §12 names *"`quiet` must reproduce the fork's phone exactly"* as a top risk, and a risk is not mitigated by a tautology.

- [ ] **Step 2: Close `_deferred/`.** Both files are out (Tasks 2 and 4). Delete the README and the `<Compile Remove="_deferred\**" />` item — a parking lot that outlives its cars becomes a place to park more.

- [ ] **Step 3: Every `INERT_NOTE` is gone.** `grep -n "INERT_NOTE" AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs` must return the constant's declaration and nothing else, or it must return the rows a decision deliberately left unwired — in which case delete the constant and inline the note on those rows with the reason. A stale "the engine starts obeying this key in plan 03" surviving plan 03 is the worst single line this document could leave behind.

- [ ] **Step 4: The suite, once, whole, alone.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
dotnet test AIOrchestratorCoreLib.Tests 2>&1 | tail -30
dotnet test tools/claude-contract/ClaudeContract.Tests 2>&1 | tail -10
dotnet build AIOrchestrator/AIOrchestrator.csproj -c Debug -warnaserror:CS 2>&1 | tail -5
```

- [ ] **Step 5: Write the report.** Per task: what was built, which copy of every file was read, the commands and their output, the SET OF NAMES of any red and whether it repeated alone. Per decision D1–D12: the answer and who gave it. Plus the standing questions this plan could not close, so the next plan inherits them written down rather than rediscovered.

---

## PARKED

Found while reading for this plan; **none of it traces to an owner request**, so none of it is a task (decision 22). One line each, written down so it is not lost, outside the denominator so it cannot move the owner's bar.

- `Break_SilentDeadlock_Async` — named by CLAUDE.md's PAUSE decision as one of the wakers that must be gated, and by `OwnerPush_Policy`'s docstring as the net that releases a suppressed entry — **does not exist in this tree.** Only the two prose references remain. Task 2 restores a suppression list whose release net is a docstring; that is a real gap and it is deliberately not this plan's to close.
- The CLAUDE.md PAUSE decision's gating list is stale for the same reason: it enumerates methods that the merge renamed or deleted.
- `SettingValidators.LISTEN_ADDRESS` stays a registered name with no check (plan 04's, and Task 1 keeps it visible in the switch).
- `runners.<role>.permission_mode` and `runners.<role>.settings` are read and written by `RunnerConfigs_Json` and registered nowhere — the catalogue's own doc calls this a deliberate omission; nobody has said whether it should stay one.
- `TopicStatusLine_Builder` is 852 lines and `Build` already dispatches to one private per field, which is why Task 4 is cheap. It is also the second-largest file this plan touches and nobody has asked for it to move.
- The gate report's "141 raw `File.ReadAllText` calls" does not reconcile with 281 occurrences measured today; the file count under `Bridge/` matches exactly. Task 11 reports both; explaining the difference is not work anyone asked for.
- `PresetProbeTests`' shipped `[InlineData("models.supervisor", "\"claude-fable-5-1\"")]` rows describe `classic` BEFORE plan 02's task-6 fix round 1 removed the four `models.*` rows from it. If plan 02 Task 8 lands with them, they are plan 02's to correct, not plan 03's.

---

## Self-review (run by the plan's author, 2026-09-12)

Checked against spec §7 in full, the `Phone`/`Pulse` rows of §6.4, §9's mode-parameterised and Windows-reds bullets, §10 phases 3 and 6, and §11's open questions. Gaps found and recorded rather than papered over.

1. **The authoritative seam list is §10 phase 3, not the brief's summary, and it has NINE entries: *"who rings; pulse fields and step; buttons and hold toggle; receipts; glyphs and the two pauses; periodic status; reply keyboard; topic close; model/effort defaults."*** The last is plan 02's (Tasks 6 and 7), leaving eight. This plan has eight seam tasks in exactly that order, plus one plumbing task (Task 1), the flake campaign (Task 11) and the gate (Task 12). Cross-checked against the catalogue's own categories: every `Phone`, `Receipts` and `Pulse` row is claimed by exactly one task, and `Models`, `Kernel`, `Owner` and `Kit` are claimed by none — correct, because those are plan 02's, the fork's already-live kernel, and plan 05's. Two rows the brief's summary did not mention are wired here because the catalogue registers them as inert: `phone.foldLongEntriesAbove` and `phone.attachEntriesAbove`, in Task 1 (they need the resolver, not a seam).
2. **§7.1 asks for an injected `IOwnerPushDecider`; Task 2 makes it a static `Decide` returning `OwnerPushDecisions`.** Recorded as a deliberate deviation with the reason (the policy is a pure static with 25 tests and nothing to inject; the interface's only implementation would be the static). The spec's real requirement — the decision is a VALUE the engine switches on, not a boolean branch inline — is met. A 20-line wrapper restores the interface if the coordinator wants it.
3. **Three settings ship a default that this tree cannot currently produce,** and each is flagged rather than assumed: `phone.status.periodic = true` with no `Build_PeriodicStatusText` anywhere (D1), `topic.onClose = close` with no `closeForumTopic` anywhere (D2), and `phone.replyKeyboard` whose `on` arm needs a client method and a launch call site that do not exist (D5). Any of the three could have been written as "just re-port it"; two of them contradict a dated owner ruling recorded in the code, which is why they are decisions and not steps.
4. **`phone.appMessagesRing` may be a no-op under its literal reading (D7).** Every app-written message on this tree is already silent. The plan says so instead of inventing a behaviour to silence, and Task 3 Step 1 makes MEASURING it the first action rather than an assumption.
5. **The three-way interaction of the hold, the filter and the credit was checked and is not disturbed.** `QuestionHold_Policy.Should_Hold` is consulted at ~3880, BEFORE the owner-channel block at ~3901, and returns `MirrorOutcomes.Held` without settling the cursor; Task 2's `Drop` and `HoldForDigest` both keep the existing `deliveredHere++` and its positional-memo comment verbatim. The one genuinely new hazard — an entry that is BOTH held (question outstanding) and held-for-digest (narration) — cannot arise, because the hold returns before the push decision is ever asked. Stated in Task 2's non-goals so a later reader does not "simplify" the order.
6. **§7.3's `pulse.fields` omitting `updated` is legal and costs the dead-app signal.** Neither preset omits it, so nothing in this plan exercises the case; Task 4 puts the cost in the catalogue's description rather than refusing the value, because refusing it would be the renderers deciding what an owner may want.
7. **§7.5's "never both" for the hold toggle has two coherent presets and two incoherent cross combinations** (D10). The spec did not consider them. The recommendation (fall back to the bar, log once) follows decision 21 — the app enforces at the point of effect and a hook-like refusal would just leave the owner with no toggle.
8. **`KNOWN_COMMANDS` is the sharpest hidden defect a naive implementation of Task 5 would ship:** a configurable bar whose parser still only accepts eleven verbs means a tap on a legal configured button parses to null, falls through every handler, and nothing happens with nothing logged — which `Parse_OrNull`'s own docstring says it exists to prevent. Called out as its own step, with its own test.
9. **`EveryTopicButtonIsWiredTests` must WIDEN, and widening may turn up an unwired verb.** The plan says to report that as a finding and keep the verb off the bar, rather than narrowing the guard to stay green — which is the "never assert on a state with two routes to it" failure (decision 20) wearing a different hat.
10. **Task 11 is sized from a real measurement, and the measurement disagrees with the gate report.** 281 occurrences / 133 files today against the report's "141 raw calls"; the `Bridge/` file count (34) matches exactly. Both numbers are in the task, the re-measure is Step 1, and the discrepancy is PARKED rather than investigated — explaining it is not work anyone asked for, but shipping a campaign that cannot count its own surface would be.
11. **The flake campaign deliberately fixes the MECHANISM, not the tests.** `REAL_TIME_COLLECTION`'s docstring already refuses the two easy fixes (raise the budget, loosen the assertion) with reasons; both refusals are restated as constraints in Task 11 so the campaign cannot quietly become a green-washing exercise.
12. **Every task's verification is a `--filter` run, and only Tasks 11 and 12 run the whole suite.** That is this machine's rule, and it is also the only way the flake family stays legible: a wide run under load produces names that are not defects, and a plan that asked for one per task would teach its own implementers to discount red.
13. **Sequencing is stated honestly and has one hard edge:** Task 1 must not start until plan 02 Task 7 has MERGED, because they modify the same four files and Task 1 copies Task 7's shape. Tasks 2–10 depend on Task 1 and on nothing else, so they can run in parallel by disjoint file sets — except that seven of them touch `BridgeEngineModel.cs`, which makes them parallel WRITERS on a shared file. Per decision 16 that is allowed only on disjoint file sets, so **in practice Tasks 2, 3, 6, 8 and 10 must be serialised** (all five edit the engine) while Tasks 4, 5, 7 and 9 can overlap with them only to the extent that their engine edits are single call sites. The safe reading, and the one this plan recommends: one engine-touching task at a time, Task 11 in parallel throughout.
14. **Twelve decisions is a lot to put in front of an owner.** They are ordered by the task they block, and eight of the twelve (D3, D4, D6, D7, D8, D9, D10, D11) are implementation judgements a coordinator can settle without the owner. Only D1, D2, D5 and D12 are genuinely the owner's: two of them are cases where the spec and a dated owner ruling in the code disagree, one is a permanent junk line in their General topic, and one is how much of their machine's time the test campaign may have.

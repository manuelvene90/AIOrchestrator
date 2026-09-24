# Plan 04 — the settings renderers — GATE REPORT

**Date:** 2026-09-24 · **Branch:** `plan/04-settings-renderers` · **Base:** `03e9db2` (plan/03 = master + plan 03
Tasks 1-4). Plan 03 was merged in four times; the last merge is `3fe77ae`, which brings plan 03 through `baa3d76`
(its Task 12 gate and Tasks 17-19). **HEAD at the gate:** `3fe77ae`, plus two commit files from this task
(`task-10-fix1-commit.txt` and then `task-10-commit.txt`).
**Written by:** the Task 10 implementer (Opus 5.5) for the plan's controller, the solo session of
`ai-orchestrator-29`. The rulings (P1-P41) and the deferred minors are copied from the ledger
`.superpowers/sdd/2026-09-12-settings-renderers-04/progress.md`, not reconstructed.

**Which copy (CLAUDE.md decision 18):** everything here is the **branch source** in the worktree
`C:\Users\Gianpiero\source\repos\AIOrchestrator-plan04`, built and run in that worktree's own `bin\Debug`. The
one exception is the ADeleteTelegram investigation (§1.5), which also built a `git archive` of `f9f0cd9` in a temp
folder. **The running app** is `C:\Users\Gianpiero\source\repos\AIOrchestrator\AIOrchestrator\bin\Debug - Copia\net10.0-windows\AIOrchestrator.exe`
(PID 21156, started 2026-09-23 23:26), which is the manual "- Copia" copy in the MAIN checkout. `Get-Process`
read it at about 10:55 on 2026-09-24. **It contains none of this plan.** No task of this plan rebuilt it, restarted it or
started any app or daemon. The installed `~/.claude` kit was not read or touched.

---

## 1. The gate (Task 10)

### 1.1 Every setting reaches every renderer

`AIOrchestratorCoreLib.Tests/Configuration/EverySettingReachesEveryRendererTests.cs` is new. It has thirteen facts.
All of them walk `SettingsCatalog.ALL` rather than a count. The two rows plan 03 added last, `away.afterMinutes`
and `pulse.unchangedFor`, are named explicitly in the WPF, Telegram and web checks.

What each renderer means in this test:
- **WPF:** `SettingsRow_Builder.Build_Sections` and `SettingEditor_Factory` over the snapshot. That is what the
  window binds to.
- **Telegram:** `SettingsMenu_Builder.Build` over that same snapshot. The engine only sends what it returns.
- **Web:** the body of `SettingsRequest_Handler.Handle("GET", "/settings")`. It is everything the page draws from.

The config.json used is deliberately varied, so that the three origins actually differ from row to row. It holds:
a Phone toggle, both new rows, a nullable Choice set to null, a free-text list containing `<b>&amp;`, and a
sentinel `web.token`.

| fact | what it holds |
|---|---|
| `EveryDefinition_ProducesExactlyOneWpfRow` | every path is in exactly one section, that section is its own category's, and the editor picks a kind without throwing |
| `EveryDefinition_AppearsOnExactlyOneTelegramMenuPage` | the Categories view offers exactly the non-empty sections; paging every category to its last page reaches every catalogue index exactly once |
| `EveryDefinition_AppearsInTheWebGetResponse` | every path appears exactly once in the GET |
| `EveryDefinition_HasARendererHint_AndNoneIsDrawnByAFallback` | every hint is a defined `SettingRenderers` value. `SettingsWindow.xaml` (read as text) has a DataTemplate keyed on every editor kind in use. Every editable hint has its own `case` in the page's `renderControl`, whose `default` draws nothing. Every non-editable row is ReadOnly in the catalogue and in the editor. The Telegram Setting view draws every row. |
| `AllThreeRenderers_ShowTheSameValueText_ForEveryDefinition` | these match: WPF `DisplayValue`, the GET's `displayValue`, and the menu's `Now: <value> — `. The restart label matches on the web and the phone. Three values are checked as literals, so the comparison cannot pass as three copies of one wrong answer. The mask holds in all three: the GET's `value` is null, the sentinel appears in neither the GET nor the menu, and the editor's box is empty. |
| `AllThreeRenderers_ShowTheSameOriginLabel_ForEveryDefinition` | these match: the WPF `OriginLabel`, the GET's `originLabel` and `origin`, and the menu's ` — <label>`. The rows cover shipped-default, preset and set-here origins, and the preset is classic. |
| `TheWebPageAndTheDesktop_DrawTheSameSentences` | the sentences `settings.html` and C# both draw. Checked: `READ_ONLY_NOTE`, `EMPTY_SECTION_NOTE`, "Saved.", the reset note (`result.message \|\| 'Reset.'`), `'preset: ' + data.preset` against `Describe_PresetHeader`, the "Not applied (X)." shape, and the page's `rangeHint` rebuilt in C# and compared with `RangeHint_OrNull` for every bounded Number row |
| `EveryReadOnlyDefinition_IsUneditableInAllThree` | every ReadOnly row. WPF: kind ReadOnly, no Reset. Phone: `READ_ONLY_NOTE` and a single Back button, and `Is_EditableOnThePhone` is false. Web: `editable:false`, and a PUT and a DELETE each get 422 `RefusedReadOnly`. config.json does not change. |
| `AnInvalidValue_IsRefusedIdenticallyByTheTelegramTap_TheWebPut_AndTheWindowsCommit` | the value 500 for `phone.status.intervalMinutes`. The window's commit chain (editor → writer → note formatter), the PUT's `results[0].message`, and the phone's reply-step answer (through the real inbound loop, per ruling P24) each show exactly the definition's `Validate_OrNull` sentence. Nothing is written by any of the three. |
| `RoundTrip_TheWindowsCommit_…` | the window's Number box writes `away.afterMinutes` = 45. `Read_All_FromDisk` then gives 45, `ConfigFile` and "set here". Inverse: an unknown path is refused with the writer's message and the file does not change. |
| `RoundTrip_ASettingsTap_…` | a real engine: `/settings`, then a tap on `phone.appMessagesRing` → off. Re-read gives false, `ConfigFile` and "set here". Inverse: a payload whose row check names no row is answered as stale and writes nothing. |
| `RoundTrip_AWebPut_…` | a PUT of `pulse.unchangedFor` = false. Re-read gives false, `ConfigFile` and "set here". Inverse: an unknown path gets 422 `RefusedUnknownPath` with the writer's own message, and the file does not change. |
| `NoRenderer_ReadsTheUnmaskedTrees` | ruling P30. The code of the menu builder, the menu model, the handler, the editor factory and `SettingsWindow.xaml.cs` (comments stripped) never calls `Read_Trees_FromDisk` or `Settings_Resolver.Resolve`. |

**RED, recorded as mutations.** The facts passed on their first run, so each one was proved to be able to fail.
The script is `p04-t10-mutate.sh` in the session scratchpad. For each mutation it backed up the file, applied the
mutation and confirmed it changed the file, rebuilt, ran the filter, then restored the file and checked it with
`cmp`. Every mutation printed RESTORED, and `git status` afterwards showed only this task's two files.

| # | mutation | went red |
|---|---|---|
| M1 | the page's empty-section note reverted to "Nothing to set here yet." | `TheWebPageAndTheDesktop_DrawTheSameSentences` |
| M2 | the page's reset note ignores the writer's message | `TheWebPageAndTheDesktop_DrawTheSameSentences` |
| M3 | a menu page drops its last row (`Take(ROWS_PER_PAGE - 1)`) | `EveryDefinition_AppearsOnExactlyOneTelegramMenuPage` |
| M4 | the GET drops each section's first row | `…AppearsInTheWebGetResponse`, `…SameValueText…`, `…SameOriginLabel…` |
| M5 | SettingsWindow.xaml loses the Secret template key | `EveryDefinition_HasARendererHint_AndNoneIsDrawnByAFallback` |
| M6 | the page loses `case 'Number':` | `EveryDefinition_HasARendererHint_AndNoneIsDrawnByAFallback` |
| M7 | the `web.token` mask is lifted | `AllThreeRenderers_ShowTheSameValueText_ForEveryDefinition` |
| M8 | the GET prints its own origin word instead of `OriginLabel` | `AllThreeRenderers_ShowTheSameOriginLabel_ForEveryDefinition` |
| M9 | `IsEditable` is always true | `EveryReadOnlyDefinition_IsUneditableInAllThree` + 3 others |

### 1.2 The one fix the gate found: two page sentences had drifted (`task-10-fix1-commit.txt`)

Task 9's report listed where the page and the window use different words. Of the items on that list, two were the
same situation worded two ways. They are drift, and the fix is to the page, which is plan 04's own file (Task 8):
- **The empty section (Kit).** The page said "Nothing to set here yet.". The window and the phone say
  `EMPTY_SECTION_NOTE`: "Nothing to set here yet — this section fills in with a later plan." The page now uses
  the longer sentence.
- **A Reset of a row that was never set.** The page drew a bare "Reset.". The window and the phone draw the
  writer's "nothing to reset" sentence, through `SettingWriteNote_Formatter`. The page now draws `result.message`
  when the writer sent one, which is the formatter's own rule.

The remaining items on Task 9's list are situations that exist in only one renderer, so they are not drift and
are not pinned:
- the page's network failure;
- the page's token-lock note and banner;
- the window's `UNCHANGED` line, `IMMEDIATE_APPLY_NOTE` and `SECRET_NOTE`;
- the page's "The list is empty." placeholder (the row's value still reads "none" through the one formatter);
- the page's "everything is already in the list".

### 1.3 The measured catalogue

Measured by the gate test's output: **72 rows**. By category: Models 12, Kernel 38, Phone 13, Receipts 2, Pulse 5,
Owner 2, Kit 0. The plan assumed 66 (Models 12, Kernel 37, Phone 9, Receipts 2, Pulse 4, Owner 2, Kit 0). Task 9
measured the same 66. The six extra rows are plan 03's, all registered after Task 9:
- `highRiskConfirmation` (Kernel)
- `phone.aggregationSeconds`, `phone.finishedMessageSeconds`, `topic.repoColours`, `away.afterMinutes` (Phone)
- `pulse.unchangedFor` (Pulse)

Every one of them reaches all three renderers with no UI code. That is the claim spec §10 phase 4 makes, and here
it is observed, not asserted. **Consequence for the owner's checklist:** Task 9's item 3 ("Kernel has 37") now
reads **38**, and Phone reads **13**.

### 1.4 The suite, once, whole, alone (Step 3)

The suite ran on 2026-09-24 from 10:53 to 11:04, at HEAD `3fe77ae` plus this task's working tree (the gate test
and the page fix). The shell was clean: `AIORCH_*` unset and WinGet `jq` on PATH. No other `dotnet test` was
running. `tasklist` at the start did list 17 `dotnet.exe` processes. They were not this task's; most likely they
were build servers or other sessions' hosts. So "alone" means that no other suite was started, not that the
machine was idle.

```
dotnet build AIOrchestrator.slnx -c Debug                                  → Avvisi: 0 · Errori: 0
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug                → Non superati: 2 · Superati: 4527 · Ignorati: 6 · Totale: 4535 · 10 m 46 s
                                                                             (log: C:\Users\Gianpiero\source\repos\plan04-gate.log)
dotnet test tools/claude-contract/ClaudeContract.Tests -c Debug            → Non superati: 0 · Superati: 31 · Ignorati: 8 · Totale: 39
dotnet build AIOrchestrator/AIOrchestrator.csproj -c Debug -warnaserror:CS → Avvisi: 0 · Errori: 0
```

**The set of reds, and each one alone.** Each was run three times on its own with
`--no-build --filter "FullyQualifiedName~<name>"`.

| red in the full run | failure | alone | classification | traces to |
|---|---|---|---|---|
| `Storage.TolerantFileReaderTests.AReaderHoldingTheFileForAMoment_DoesNotCostTheAtomicWriterItsRename` | `UnauthorizedAccessException` from `File.Move` in `Atomic_FileWriter.Move_TolerantOfAReader` | green ×3 | **flake** (file lock under load) | no plan-04 task: `git diff 03e9db2 HEAD -- AIOrchestratorCoreLib/Storage AIOrchestratorCoreLib.Tests/Storage` is empty. It is already named in plan 03's §5.1 family. |
| `Storage.TolerantFileReaderTests.AFileLockedExclusivelyForAMoment_IsReadOnceTheLockGoes` | `IOException` "being used by another process" from `Tolerant_FileReader.Read_AllText` | green ×3 | **flake** (file lock under load) | no plan-04 task (same empty diff). It failed one second after its neighbour, so both look like one lock episode. |

**No red is real, and the gate is not stopped.** `ADeleteTelegramWillNeverAccept_…` PASSED in the full run.

**The six skips:**
- `SettingsRequestHandlerTests.Put_WhenTheFolderRefusesTheWrite_…`: POSIX-only (Task 2b).
- `SettingsWebHostSmokeTests.HeldOpen_ForABrowserCheck_OnATempRoot`: the opt-in fixture (P9).
- The three live smokes (`MultiSourceLiveSmokeTests`, `FirstRoundFromTheGeneralLiveSmokeTests`, `StreamLiveSmokeTests`).
- `PrintTurnLimitResetTests.AStateFileThatCannotBeWritten_…`: an OS-split skip that predates this plan.

These are exactly the honest skips the brief expects.

### 1.5 `ClosingATopicReallyDeletesItTests.ADeleteTelegramWillNeverAccept_TellsTheOwnerOnce_AndNeverAgainAfterARestart`

The controller saw it fail twice out of two runs, run alone, on plan/04 before the merge. The sibling branch passed
it twice out of two. This gate re-ran it, **alone, with no other test host on the machine**:

| tree | how | runs | result |
|---|---|---|---|
| HEAD `3fe77ae` (this worktree's build) | `--no-build --filter …`, `AIORCH_*` unset | 5 | **5/5 green** (0.58-1.0 s each) |
| HEAD `3fe77ae` | the same, with the session's `AIORCH_ID/MEMBER/ROLE/SUPERVISION_ROOT` LEFT SET (the P29 leak, tested as a hypothesis) | 2 | 2/2 green |
| `f9f0cd9` (plan/04 just before the merge), from `git archive f9f0cd9` into a temp folder, then built | `--filter …`, `AIORCH_*` unset | 3 | **3/3 green** |

**Finding: the red does not reproduce on either tree when the machine is idle, so there is no plan-04 commit to
blame, and the bisect-style read has no red to bisect.** A diff-read backs this up. The test drives `Close_Orchestration_ByOwner` → `deleteForumTopic` refused → a General
alert, then a restart sweep. None of the plan-04 commits touches that path: the close, the delete sweep, the
General alert, or `ClosingATopicReallyDeletesItTests`. The only `BridgeEngineModel.cs` changes in plan 04 are:
- the settings-menu wiring (`d63cd76`, `f9f0cd9`, §4);
- the `/screens` save moved out (`44b6e0d`).

The test's history also fits a load race. Plan 03's report (§2, Task 7 row) saw it "red under load, 4/4 alone", and the test file's own
docstring on `TheCloseAndTheStartUpSweep_NeverAttemptTheSameDeleteTwice` records it failing "about one run in
four under a full suite". Plan 03 lists it in its §5.1 flake family (Task 11, deferred). **The most likely
explanation of the controller's 2/2** is that the "alone" runs were not alone on the machine. Plan 03's Task 12
full suite ran in the plan03 worktree that same evening, and this test's `until` waits are wall-clock (15 s). That
is an inference; it was not measured. **Classification: flake (load), known family. Not fixed, and nothing to
fix in plan 04's code.** The controller's own `2/2` logs were not available to this task. If they carry a
failure message that is not a timeout (for example `DeleteAttempts` = 2 or two General alerts), that would point
at the collision race the sibling test pins, and it would be worth one look.

---

## 2. Per task

Every task read and edited the **branch source** of this worktree only (each task-N-report.md says so). "Controller
re-ran" means a `--filter` run before committing. No task before this one ran the whole suite.

| task | what was built | commits | review |
|---|---|---|---|
| 3 — `web.listen` validated | `ListenAddress` + validator (P18) | `2245934` | clean |
| 1 — one reading | `SettingsSnapshot_Reader`, `ISettingReading`, formatter / origin / restart labels, `SettingsRow_Builder`, `SettingValue_Parser` (P13); masks `web.token` (P2); fix: blank list word visible, P31 sentences | `b8df0dc`, `a38dad5` | clean after 1 round |
| 2 — one writer | `Settings_Writer` (D6, D10 lock, P12 every spelling); `Save` stops writing the model keys (D1); `Save_BotToken` (P11) | `eb5e6c3` | clean |
| 2b / 2c — never wipe an unreadable file | writer, `Save` and `Save_BotToken` refuse an unreadable or unparsable file (P33, P35, P36); `/screens` save moved to `StatusScreenshots_Writer` | `cb03f4e`, `44b6e0d` | clean |
| 6 — the handler | GET / PUT / DELETE as a pure function (D4 open on loopback, P1, P3); Host allowlist + fence of 6 rows while the token is empty (P32, P34) | `649a38c`, `a6f3c4d`, `52c82b3` | clean after 2 rounds |
| 8 — the page | `settings.html`, embedded only (D14), drawn entirely from the GET | `9f522ce` | clean |
| 7 — the listener | `SettingsWebHost` in the app and the daemon (P21); caller must be loopback; fail-closed token; body cap; no CORS; P37 / P38 | `d91a2da`, `c4f2436` | clean after 1 round |
| 9 — the WPF window | generic over the catalogue; `SettingEditor` + `SettingEditorKinds` in CoreLib; immediate apply (D5); Connection tab (D11); Secret kind | `8be367a`, `0687209` | clean after 1 round |
| merge fix | offers are candidates, not a list (P39) | `3597013` | controller-read |
| 4 — menu builder | pure paged builder, P6 confirm, P7 payload with FNV check, D2 fence, D3 orchestration view | `7763a6b`, `2cac660` | clean after 1 round |
| 5 — menu wired | `Bridge/SettingsMenu/*`, D7 exemption, D9 reply step, P15, P23; fix: app-composed messages and held secrets | `d63cd76`, `f9f0cd9` | clean after 1 round |
| 10 — the gate | §1 | this task's two commit files | — |

## 3. Decisions D1-D15, and who answered each

| # | answer | by |
|---|---|---|
| D1 | yes: `Save` stops writing the four model keys; they are written only by `Settings_Writer` when their row is edited | **owner**, 2026-09-14 |
| D2 | (b) the phone refuses `telegramInbound`, `telegramSupergroupChatId`, `telegramOwnerUserId`; (a) two-tap confirm for the rest of Kernel (P6) | **owner** |
| D3 | machine settings in General only; in a topic, a read-only view of that orchestration's rows, pointing at `/model` `/effort` | **owner** |
| D4 | open read AND write on loopback while `web.token` is empty (overrides the recommendation), then tightened by the controller: P32 (Host allowlist + fence) and P34 (the three D2 keys fenced too). The owner was told these tighten D4 and may relax them | **owner**; P32/P34 **controller** |
| D5 | immediate apply per row, Cancel removed, the token keeps its own Save | **owner** |
| D6 | a new generic `Settings_Writer`, not `Save` | recommendation (coordinator), per the ANSWERS row |
| D7 | (a) the live menu is exempt from the 30 s per-message edit gap | recommendation (coordinator) |
| D8 | the index into `SettingsCatalog.ALL` plus a stale answer, sharpened by P7 (category + FNV check of the path) | recommendation + **controller** P7 |
| D9 | reply step per (chat, topic), 5 min, `/cancel`, any command ends it, persisted (P23) | recommendation + **controller** P15, P23 |
| D10 | a process-wide lock around read-edit-write | recommendation (coordinator) |
| D11 | Connection tab hand-written for the token only; the two ids drawn as catalogue rows | recommendation (coordinator) |
| D12 | Composite rows read-only with a sentence naming where they are changed. The premise was stale: the descriptions did not say it, so P31 added one sentence to each | recommendation + **controller** P31 |
| D13 | the preset is shown in every header and never offered as a row | recommendation (coordinator) |
| D14 | `Web/Assets/settings.html`, embedded only | recommendation (coordinator) |
| D15 | the picker exists; offers are drawable verbs / `PulseField_Names.ALL` (P5, P39) | recommendation + **controller** P5, P39 |

**Answered by an implementer rather than the owner or the coordinator (findings, not footnotes):**
- Task 4 made a toggle two taps from the list, to keep Reset reachable.
- Task 4 settled the `label: value` caption.
- Task 9 split OrderedList into picker vs typed list as a new `SettingEditorKinds` enum. The controller accepted it.
- Task 7 registered both `127.0.0.1` and `localhost` prefixes on Windows. The controller kept this for Windows under P38.

Each was reviewed and accepted, and none of them was put to the owner.

## 4. Every line changed in `BridgeEngineModel.cs`

Only two plan-04 commits touched the engine's settings wiring: `d63cd76` (+53/−1) and `f9f0cd9` (±1). A third
plan-04 commit, `44b6e0d` (Task 2c), moved the `/screens` save out of the file.

`d63cd76` changed these places:
1. The class declaration gains `, SettingsMenu.ISettingsMenuHost`.
2. A new field `_settingsMenu`, built by `SettingsMenu_Factory.Create(paths, store, log, clock, sendBudget,
   SettingsMenuState_Factory.Create_Restored(restoredState.SettingsMenuMessageId, restoredState.SettingsReplySteps))`.
3. In the inbound loop, before the command chain:
   `if (command != null && await _settingsMenu.Try_EndReplyStep_OnCommand_Async(...)) continue;`
4. In the routable loop, after the high-risk read-back and before `Apply_HoldControlWord_Async`:
   `if (await _settingsMenu.Try_TakeReply_Async(...)) continue;` (P23).
5. In `Try_RunOwnerCommand_Async`: `else if (command == "settings") await _settingsMenu.Send_Menu_Async(...)`.
6. In `Handle_CallbackTap_Async`, a fifth handler after ModelEffort and before the generic `opt-` path:
   `Try_HandleSettingsTap_Async`.
7. The new private `Try_HandleSettingsTap_Async`: `Is_Ours`, then away-mode exit, then delegate.
8. Two `ISettingsMenuHost` adapters (`Remember_TopicMessage`, `Persist_EngineState`).
9. `Persist_EngineState` gains `SettingsMenuMessageId` and `SettingsReplySteps`.

`f9f0cd9` changed one line: the persisted steps come from `_settingsMenu.Read_PersistableSteps()` instead of
`State.Read_Steps()`. A lapsed step or a held secret never reaches disk.

`44b6e0d` deleted `Set_StatusScreenshots` and moved the save and its wording to `Bridge/StatusScreenshots_Writer`.

**What moved out:** all the menu's state and behaviour live in `Bridge/SettingsMenu/`:
- `ISettingsMenu` / `SettingsMenuModel` / `SettingsMenu_Factory`;
- `ISettingsMenuState` / `SettingsMenuStateModel` / `SettingsMenuState_Factory`;
- `ISettingsReplyStep` / `SettingsReplyStepModel` / `SettingsReplyStep_Factory`;
- `ISettingsMenuHost`.

The pure parts are in `Telegram/SettingsMenu/` (`SettingsReplyStep_Decider`, `SettingsMenu_Builder`,
`SettingsButton_Data`). No `case "settings":` is in the tap switch (P25).

## 5. D7's exemption, exactly as implemented

- `ITelegramSendBudget` gains `Exempt_FromEditGap(long)` and `Release_EditGapExemption(long)`.
- `TelegramSendBudgetModel` holds a single `long? _editGapExemptMessageId`:
  - only ONE message id is ever exempt;
  - exempting a new one replaces the old one;
  - a release naming another id is a no-op.
- `Reserve_MessageEdit` returns zero for the exempt id and still stamps the edit time, so a released message owes
  the gap from its last edit.
- The control bucket (60/min) and Telegram's own cooldown still apply to every edit.
- The exemption is released or moved when a new menu is posted, when an older menu is adopted by a tap, and is
  re-exempted for a restored live id at startup.

**The test that proves it is narrow:**
`TelegramSendBudgetTests.TheLiveSettingsMenu_IsNeverHeld_ButAnyOtherMessageStillGetsTheThirtySecondGap`. It asserts
that another message owes exactly 30 s − 1 s. Alongside it:
- `OnlyOneMessageIsEverExempt_AndAReleaseNamingAnotherIsIgnored`;
- `ReleasingTheExemption_PutsTheMessageBackUnderTheGap`;
- the wire test `TelegramApiClientWireTests.TheLiveSettingsMenu_ReachesTheWireOnEveryEdit_AndAnotherMessagesSecondEditIsHeldBeforeIt`;
- the engine pair `FiveRapidTaps_…` / `AMessageThatIsNotTheLiveMenu_StillGetsTheThirtySecondGap`, on a budget with
  production's 30 s gap.

A deferred minor: the exemption is not tied to a chat id.

## 6. Which renderer is covered how ("Honest testability")

| renderer | covered by | not covered |
|---|---|---|
| shared reading + writing layers | fully: pure CoreLib tests, and this gate | — |
| Telegram `/settings` | fully: the pure builder, and the REAL engine through `ScriptedInbound_Fake` (Task 5, and this gate's refusal and round-trip) | what a phone actually renders; Telegram's acceptance of a 64-byte payload |
| web JSON layer | fully: the handler as a pure function | — |
| web host | partly: smokes that bind a real loopback port, skipped by attribute when they cannot (P22) | a tunnel, a second machine, Linux (reasoned only, P38) |
| the HTML page | barely: embedded, names no catalogue path, calls GET/PUT, loads nothing external; plus this gate's same-sentences check; plus Task 8's headless-Edge checklist on a temp root (all PASS) | everything a browser does, beyond that one run |
| **WPF window** | **its CONTENT only**: every row, its section, its control kind and a template key for that kind; its sentences come from CoreLib constants. **Its APPEARANCE has no automated coverage.** | the whole drawing layer |

**The WPF manual checklist.** It was run by Task 9, twice (on `8be367a` and on `0687209`), through an offscreen
harness in that task's scratchpad. The harness hosted the real `SettingsWindow` at (−32000, −32000) over a temp
root. `App.Run()` was never called, so no bridge was started. Result: **61 passed, 0 failed, 0 binding errors**.
It covered checklist items 1-14. At that time the counts were Kernel 37 and Phone 9; they are now 38 and 13 (§1.3).
It ran against **that worktree's own build output**, not the owner's running app. The owner's own checklist
(Task 9 report, items 1-14, with item 3 now reading Kernel 38) is **still to do**, on a rebuilt app. The running
binary is the "- Copia" copy and contains none of this plan (decision 23).

**The web-host smoke** **RAN on Windows 10.0.19045** in the full run. Every `[RequiresLoopbackListenerFact]` smoke executed, and the only
`SettingsWebHostSmokeTests` skip was the opt-in `HeldOpen_ForABrowserCheck_OnATempRoot`. It has not run on
Linux or macOS.

## 7. Editable and not yet obeyed

Plan 03 deleted every `INERT_NOTE`. `grep INERT_NOTE|READ BY NOTHING` on `SettingsCatalog.cs` at HEAD returns
nothing, and Task 7 removed the two `web.*` "READ BY NOTHING YET" lines (P17). **One row is still legal and has no
effect: `phone.replyKeyboard = on` installs nothing** (plan 03 D5, both presets off). Its description says so in
capitals ("'on' INSTALLS NOTHING YET"), and all three renderers show that description. Every other row that the
renderers offer is read by the engine, the launcher, or the web host.

## 8. What spec §8 asked for that this plan did not deliver

- **§8 "all three renderers write through the fork's merging `Save`."** They write through `Settings_Writer`
  instead (D6). This is the plan's largest deviation from the spec's words, and it is recorded as a decision.
- **§8.1 the WPF window.** It gained no Cancel (D5, the owner's answer). The Connection tab writes secrets.json
  only (P11), not the full `Save`.
- **§8.3 "a second hosted service."** The listener starts inside `BridgeHost_Service.Run_Host_Async` instead (P21).
- **§8.3 the web page.** The first `web.token` cannot be set from the page, because the row is fenced (P32). The
  banner says how to set it by hand, and the phone can set it (P40).
- **Linux.** Neither CI leg has run this branch (it needs a push, which is the owner's call). The Linux
  listener's `localhost` handling is reasoned, not measured (P38).

## 9. Standing items inherited by the owner / next plan

1. The owner's WPF checklist on a rebuilt app, and Task 8's owner-side checks (a phone through the tunnel; the
   token box once a token is set).
2. Plan 03's Task 11 flake campaign is still deferred. ADeleteTelegram (§1.5) and the reds in §1.4 belong to it.
3. Deferred minors from the ledger that a reader of settings should know about:
   - `web.token` typed in the Kernel tab is a Secret kind: a blank box does nothing, and only Reset clears it.
   - A typed `web.token` sits in Telegram history (P40, accepted).
   - The `session.*` rows read "shipped default" in the machine view.
   - A Linux `localhost` tunnel needs `127.0.0.1` there.
   - `Decide_Bind_OrNull` reads `web.listen` leniently at startup.
4. PARKED (plan document, unchanged): the `RunnerConfigs_Json.Write` materialisation; the repos
   reorderer/colour writers outside the lock; cross-process config writes; `permission_mode`/`settings` in no
   catalogue; `OrchestratorConfig_Factory.Create`'s parameter count; the two spellings of `TelegramInbound`;
   `Read_JsonObject_ForEditing`'s silent empty tree; the stackdump files.

## 10. Verdict

**Phase 4 of spec §10 is met on its own terms, with three stated limits.** All 72 catalogue rows reach the Telegram
menu, the web GET and the WPF window's content. That includes the six rows plan 03 added while this plan ran,
none of which needed a line of UI code. All three renderers print the same value, origin and restart words; they
refuse the same rows and the same values in the definition's own sentence; and they read back their own writes
with the right origin. The one full run has two reds, both in the known `Storage` file-lock family, and each
passed three times alone. The gate's only production change is two page sentences that had drifted from the
desktop's (§1.2), committed separately.

The limits:
- The WPF window's appearance has no automated coverage. Only its content does.
- Neither Linux nor CI has run this branch.
- The owner's running app is the "- Copia" copy and holds none of this. Nothing here is live until the branch is
  merged, rebuilt and re-copied, and the app is restarted (decision 23).

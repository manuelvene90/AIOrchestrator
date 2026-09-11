# Re-port ledger — master changes dropped by the 2026-09-11 merge resolution

Every conflicted file took the fork's version whole (Task 2). Each row below is a master change that
was in that file and is restored by the named task. A row is ticked by the task that restores it,
with the commit hash. Nothing here is optional: an unticked row at the end of plan 01 is a
regression the owner will meet on the phone.

| master change (commit) | file(s) | restored by | done |
|---|---|---|---|
| `--resume` on respawn for terminal supervisor/solo (5e7ed6b) | SpawnCommand_Builder, OrchestrationLauncherModel, ResumableSession_Resolver | Task 3 | [ ] |
| effort flag + role default xhigh for supervisor/solo (60671ac, 39fc6b3) | SpawnCommand_Builder, session model/serializer/store | Task 3, Task 4 | [ ] |
| `/model` `/effort` from the phone, Apply_Dial, role picker (7bdc55e, 81038e4, 7f658e0) | BridgeEngineModel, Telegram/ModelEffort*, BotCommandMenu | Task 4 | [ ] |
| owner `/pause` and 💤 (a2c9a3d) | session model/serializer/store, PausedFlag_Marker, TelegramDeliveryModes, run-to-the-end hook | Task 5 | [ ] |
| question hold in the mirror loop (e637cb2) | BridgeEngineModel (MirrorOutcomes, heldChannels), QuestionHold_Policy, awaiting-answer hook (solo) | Task 6 | [ ] |
| owner-answer credit: Raise_OwnerWait, Is_TurnEndDeclaration, /merge opens the tracker (58ff547) | BridgeEngineModel, OwnerPush_Policy | Task 7 | [ ] |
| model + effort reading types and formatter (7f658e0, 411fa21) | Status/SessionModelReading, Formatting/ModelReading_Formatter, statusline effort | Task 8 | [ ] |
| reply keyboard markup (65107e9) | ReplyKeyboard_Markup, TopicCommandButtons rows | Task 9 (compiles, not wired — spec §7.6) | [ ] |
| kit prose: ONE QUESTION (solo, general-supervisor), "the app holds the channel" (supervisor), RESUMED paragraphs (supervisor, solo) | kit/skills | Task 10 | [ ] |
| pictures as pictures (da8f66c) | superseded by the fork's EntryAttachment_Policy; master's tests re-homed | Task 11 | [ ] |
| Fable 5.1 default (60671ac) | moved into the `classic` preset by plan 02; the shipped default is Opus (spec §11.4) | plan 02 | n/a |
| collapsing command bar / button catalogue (65107e9) | superseded by `pulse.buttons` in plan 03 | plan 03 | n/a |

## What the resolution actually dropped, file by file

The 19 conflicted paths matched Appendix B of the spec exactly (see Task 2's report). Beyond those,
the textual auto-merge left master additions standing against a fork API that no longer has them.
Those did not conflict — git merged them cleanly and the COMPILER found them. Each was resolved the
same way (master's addition removed, the fork's shape kept) and each is listed here so the task that
re-ports the feature knows where to look.

### Production files corrected after the resolution (Task 2, Step 6)

| file | what was removed | why it did not compile | restored by |
|---|---|---|---|
| `AIOrchestratorCoreLib/Mirroring/MirrorText_Formatter.cs` | master's `(string Prefix, string Content) Format_Parts` overload | CS0111 — the auto-merge left both signatures, and glued master's `Format` body under the fork's `Format_Parts` signature. Master's body is character-identical to the fork's apart from the tuple element name, so NOTHING was folded; the fork's version already lifts `QUESTION:`/`OPTION:`/`IMAGE:` marker lines. File is now byte-identical to `fork/ours/integration`. | — (no loss) |
| `AIOrchestratorCoreLib/Sessions/OrchestrationSession/IOrchestrationSession.cs` | `SupervisorEffortOverride`, `ImplementerEffortOverride`, `Paused` | CS0535 ×3 — the interface auto-merged with master's members while its partner `OrchestrationSessionModel.cs` was conflicted and took the fork's version. Taken from the fork whole. | Task 3/Task 4 (effort), Task 5 (`Paused`) |
| `AIOrchestratorCoreLib/Sessions/OrchestrationSessionStore/OrchestrationSessionStoreModel.cs` | `Set_Paused`, `Set_SupervisorEffortOverride`, `Set_ImplementerEffortOverride` | CS0117 ×3 — they call `OrchestrationSession_Factory.CreateFrom_Existing_With{Paused,SupervisorEffortOverride,ImplementerEffortOverride}`, which the fork's factory does not have. Removing exactly these three made the file byte-identical to the fork's. | Task 3/Task 4 (effort), Task 5 (`Set_Paused`) |
| `AIOrchestratorCoreLib/Bridge/EffectiveMode_Resolver.cs` | the `bool paused` parameter and the `if (paused) return Deferred;` branch | CS7036 at `BridgeEngineModel.cs(4035)` — master alone changed this file, so the merge kept master's 6-parameter `Resolve`, and the fork's `BridgeEngineModel` (which won its conflict) calls the 5-parameter one. Taken from the fork whole. **This is the outbound half of PAUSE (decision "PAUSE — asleep, not closed"): re-port it with the rest of `/pause`.** | Task 5 |
| `AIOrchestratorCoreLib/Watchdog/SessionWatchdog/SessionWatchdogModel.cs` | the `if (session.Paused) continue;` respawn guard (with its comment) | CS1061 — `IOrchestrationSession` no longer has `Paused`. **Minimal fix: only the guard was removed.** Master's two `--resume` log-wording changes in the same file compile and were KEPT, so this file is NOT the fork's version. | Task 5 (the guard) |
| `AIOrchestrator/MainWindow.xaml.cs` | `Describe_CardGlyph` / `Describe_CardTooltip` and their two call sites (back to `Describe_ModeGlyph(session.TelegramMode)` / `Describe_ModeTooltip(...)`) | CS1061 `Paused` ×2 and CS0117 `TelegramDeliveryMode_Glyphs.PAUSED` — the glyph went with the fork's `TelegramDeliveryModes.cs`. The card will read 🔔 for a paused orchestration until Task 5 restores this. | Task 5 |

### Master-only test files PARKED in `AIOrchestratorCoreLib.Tests/_pending-reports/` (Task 2, Step 7)

They do not exist in `fork/ours/integration` at all. The `<Compile Remove="_pending-reports\**" />`
item in `AIOrchestratorCoreLib.Tests.csproj` keeps them out of the build. **Each named task moves its
own files back**; Task 14 deletes the folder and the item.

| parked file | covers | un-parked by |
|---|---|---|
| `SessionJsonSerializerTests.cs` | effort overrides through `session.json` (`CreateFrom_Existing_With*EffortOverride`) | Task 3 / Task 4 |
| `ModelOnTheStatusLineTests.cs` | `supervisorModel` on the topic status line (`TopicStatusLine_Builder.Build` / `_Planner.Plan`) | Task 4 / Task 8 |
| `AStatusLineDoesNotSpendTheOwnersWaitTests.cs` | the owner-answer credit vs a `STATUS`/`WAITING ON` line | Task 7 |
| `MergeCommandOpensTheReplyTrackerTests.cs` | `/merge` opens `Track_OwnerReply` and its turn end is announced | Task 7 |
| `QuestionWaterfallProbeTests.cs` | the nine-questions-in-five-minutes waterfall probe (its own `WaterfallTelegram_Fake` no longer satisfies the fork's larger `ITelegramApiClient`) | Task 6 / Task 7 |
| `PicturesReachTheOwnerTests.cs` | pictures as pictures — superseded by the fork's `EntryAttachment_Policy`; re-home what still applies | Task 11 |

### Shared test files RESET to the fork's version (Task 2, Step 7)

These exist on both sides; the auto-merge stacked master's cases on top of a fork API that no longer
has them, so the fork's version was taken whole. Nothing is parked here — **the dropped cases are
recovered with `git show master:<path>`** by the task that re-ports the feature.

| file | master cases dropped (lines added vs base `50a6d8d`) | why they stopped compiling | restored by |
|---|---|---|---|
| `Bridge/EffectiveModeResolverTests.cs` | +77 | `Resolve(..., paused: …)` — the parameter is gone | Task 5 |
| `Bridge/OwnerPushPolicyTests.cs` | +53 | `OwnerPush_Policy.Is_TurnEndDeclaration` does not exist on the fork's policy | Task 7 |
| `Launching/OrchestrationLauncherTests.cs` | +156 | `SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL`, `Set_{Supervisor,Implementer}EffortOverride` | Task 3 / Task 4 |
| `Sessions/OrchestrationSessionStoreTests.cs` | +138 | effort overrides and `Set_Paused` / `session.Paused` | Task 3 / Task 4 (effort), Task 5 (pause) |
| `Sessions/ReviewerMemberKindTests.cs` | +2 / −2 | `Build_ForReviewer` / `Build_ForImplementer` take 7 args on master, 6 on the fork | Task 3 |
| `Spawning/SessionWindowTitleTests.cs` | +10 / −10 | `Build_ForSolo` (8 args), `Build_For{Implementer,Reviewer,Supervisor}` (7 args) — master's extra argument | Task 3 |
| `Telegram/TelegramDeliveryModeGlyphsTests.cs` | +141 | `TelegramDeliveryMode_Glyphs.Decorate_TopicName` (the 💤 decoration) | Task 5 |

### Master-only production files that survived the merge — present, compiling, and UNWIRED

Nothing had to be done to these: they compile against the fork's CoreLib. But every engine/launcher
call site that USED them lived in a conflicted file that took the fork's version, so each is now
referenced only by its own unit tests. **The type is not the feature — a task that finds the file
already there still has to wire it.**

`Bridge/QuestionHold_Policy.cs` (Task 6) · `Formatting/ModelReading_Formatter.cs` (Task 8) ·
`Spawning/ResumableSession_Resolver.cs` (Task 3 — still reached from `Limits/RateLimits_Reader.cs`,
which only master changed) · `Status/PausedFlag_Marker.cs` (Task 5) ·
`Status/SessionModelReading/*` (Task 8 — still reached from `Usage/UsageTotals_Reader.cs`) ·
`Telegram/EffortLevels.cs`, `Telegram/ModelChoices.cs`, `Telegram/ModelEffortButton_Data.cs`,
`Telegram/ModelEffortCommand_Parser.cs`, `Telegram/ModelEffortPrompt_Builder.cs` (Task 4) ·
`Telegram/ReplyKeyboard_Markup.cs` (Task 9).

### The one file resolved by hand rather than taken whole

`kit/skills/solo/SKILL.md` — the fork's file, with master's **ONE OPEN QUESTION AT A TIME** block
re-inserted right after the brevity rules, as the brief directs. Master's hand-written
`QUESTION:`/`OPTION:` template bullet was deliberately NOT brought across: the fork's skills say to
call the tool with `--question --option`, and that wording stays. `kit/hooks/run-to-the-end-check.sh`
and `kit/hooks/supervisor-awaiting-answer-check.sh` auto-merged carrying BOTH sides (the fork's
`SUPERVISION_ROOT="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"` and master's `.paused`
exit and `case "$AIORCH_ROLE" in supervisor|solo)`) — Tasks 5 and 6 verify them. `CLAUDE.md`
auto-merged; Task 13 rewrites the merged decisions.

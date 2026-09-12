# Re-port ledger — master changes dropped by the 2026-09-11 merge resolution

Every conflicted file took the fork's version whole (Task 2). Each row below is a master change that
was in that file and is restored by the named task. A row is ticked by the task that restores it,
with the commit hash. Nothing here is optional: an unticked row at the end of plan 01 is a
regression the owner will meet on the phone.

| master change (commit) | file(s) | restored by | done |
|---|---|---|---|
| `--resume` on respawn for terminal supervisor/solo (5e7ed6b) | SpawnCommand_Builder, OrchestrationLauncherModel, ResumableSession_Resolver | Task 3 | [x] 27a83c7 |
| effort flag + role default xhigh for supervisor/solo (60671ac, 39fc6b3) | SpawnCommand_Builder, session model/serializer/store | Task 3, Task 4 | [x] 27a83c7 (Task 3) + f392567 (Task 4) — `SUPERVISION_EFFORT_LEVEL`, the `--effort` flag on every builder and `ISessionLaunch.Effort` came with Task 3; `Supervisor/ImplementerEffortOverride` on the session triple, serializer and store, the two reads at the launcher call sites (`session.SupervisorEffortOverride` / `session.ImplementerEffortOverride`, no longer `effort: null`) and master's `Respawn_PassesTheStoredEffortOverride_ToTheSupervisorAndToEveryMemberKind` came with Task 4. `SessionJsonSerializerTests` un-parked with it — **it carries no `Paused` case**, so the brief's `!~Paused` filter was never needed and Task 5 inherits nothing here. |
| `/model` `/effort` from the phone, Apply_Dial, role picker (7bdc55e, 81038e4, 7f658e0) | BridgeEngineModel, Telegram/ModelEffort*, BotCommandMenu | Task 4 | [x] d2e8865 — the five `Telegram/ModelEffort*` types were in the tree and UNWIRED (they were never parked); they are now reached from the engine's dispatch chain, `Handle_CallbackTap_Async` (after the topic bar, before the generic `opt-` path) and the `set-model` request branch, which now goes through the same `Apply_Dial`. **One deliberate difference from master:** `Apply_Dial` asks `_launcher.Resolve_RunnerKind(role, orchId)` and SKIPS the kill+respawn for a bridge-driven session — master predates the runner seam and killed unconditionally. `Send_DialPrompt_Async` keeps master's `now:` line (`UsageTotals_Reader.Read_ModelReading_OrNull` + `ModelReading_Formatter`, both already in the tree) but drops master's Italian-layer translate: the fork abolished that layer on 2026-09-09. Master's `/italian` command is NOT re-ported for the same reason. |
| owner `/pause` and 💤 (a2c9a3d) | session model/serializer/store, PausedFlag_Marker, TelegramDeliveryModes, run-to-the-end hook | Task 5 | [x] b4fe9b2 — `Paused` on the session triple, serializer and store; `EffectiveMode_Resolver.Resolve` widened with `paused`, checked AHEAD of presence (the outbound half, one site for all fifteen send gates); the pushing half gated waker by waker (see the table in task-5-report.md); `Sync_PausedFlags` reconciles `.paused` on the tick; `TopicNameFlags.IsPausedByOwner` draws 💤 BESIDE the fork's ⏸, precedence 🏁 → 💤 → ✅ → 🧪 → ⏸; `/pause` in `BotCommandMenu` and the typed dispatch; the watchdog guard restored; `MainWindow`'s card glyph restored. **Two deliberate differences from master:** the `.paused` exit is added to `supervisor-ledger-check.sh` as well (master has none there — its app-side gate cannot clear a `.ledger-behind` raised BEFORE the pause), and master's `case "pause"` in the callback-tap switch is NOT re-ported, because the fork's `TopicCommandButtons` renders no pause button (Task 9 / plan 03). `PausedFlagMarkerTests` was never parked — it is already at `Status/` and green. |
| question hold in the mirror loop (e637cb2) | BridgeEngineModel (MirrorOutcomes, heldChannels), QuestionHold_Policy, awaiting-answer hook (solo). **The PROSE is already in the tree and the WIRING is not:** `kit/skills/solo/SKILL.md` promises the solo *"the app HOLDS this channel"* (it came in with this merge, §"The one file resolved by hand") while `QuestionHold_Policy` has no production caller. They must land together — a role command that promises a hold nobody performs is worse than silence. | Task 6 | [x] c7f948d — `MirrorOutcomes {Delivered,Failed,Held}`, `_deliveredEntriesOfHeldAppend`, the per-ENTRY hold inside `Mirror_Append_Async` and the `heldChannels` guard in the tick loop are back; `Find_ActiveChannels`' comment now says both halves apply (hold at the mirror + the hook's stop), citing the owner's 2026-09-11 ruling. `Would_BeASecondOpenQuestion` and its coaching entry DELETED — its text ("both are live, a typed reply binds neither") is false for the ordinary Remote case under the hold; `Find_QuestionsToSupersede` and the SUPERSEDED path untouched and still reachable. The hook needed NOTHING: the merge auto-merged master's `supervisor|solo` gate onto the fork's `$SUPERVISION_ROOT`, and `hook-behaviour-check.sh` now pins it with a `SOLO is stopped like a supervisor  DENY` case (master has no such case). Prose and wiring now agree — `solo/SKILL.md:224` and `supervisor/SKILL.md:436`. **FIX ROUND (15d85c5):** the hold left FOUR fixtures stale, each of which MANUFACTURED "two questions open with no owner reply in between" as its starting condition — a state the app now deliberately withholds. Fixed as FIXTURES, never as the rule, and no assertion weakened: `AnEntryWrittenAfterAnAppEntryStillReachesThePhoneTests.AnEntryAppendedAfterTheAppsOwnEntry_IsStillMirrored`, `QuestionContractProbeTests.ATapOnOneQuestion_LeavesTheOtherOpen_TappableAndUnstamped` (both InlineData cases) and `QuestionContractProbeTests.ANewQuestion_AfterTheOwnerRepliedInWords_SupersedesTheOlderOnes` now reach two-open through the app's own TEN-MINUTE CAP (`Expire_StaleAwaitingAnswerFlags`), the same honest route `DecisionStateSurvivesARestartTests` uses. **And one route was wrong everywhere it was written:** TERMINAL PRESENCE is NOT a route to two open questions — it raises no flag but also SILENCES the topic, so no question is ever sent and none is ever registered open. Corrected in `DecisionStateSurvivesARestartTests`' comment and in `task-6-report.md`'s composition table; two docstrings that still said "returns false" of `Mirror_Append_Async` (`Telegram/TelegramProse_Sender.cs:14`, `BridgeEngineModel.cs:4153`) now say FAILED. |
| owner-answer credit: Raise_OwnerWait, Is_TurnEndDeclaration, /merge opens the tracker (58ff547) | BridgeEngineModel, OwnerPush_Policy | Task 7 | [x] 8ad7756 — the PROTECTIVE half only. `Is_TurnEndDeclaration` (subject only) is on the fork's policy and the engine refuses to CONSUME the credit on a turn-end subject; `Raise_OwnerWait` is the one raise site and runs at DELIVERY (`Flush_OwnerDeliveries_Async`, and the start-request task, which is already in the channel by then) — the route-site raise at buffering is gone; `Track_OwnerReply` is the one `PendingOwnerReply` construction site and `/merge` opens it; the busy notice is gated on `!pending.Answered`. **NOT re-ported, deliberately: `_suppressedEntries`, the turn-end digest and the periodic STATUS — they are the `phone.push = filtered` half and belong to plan 03.** Note for whoever takes that: on this build the credit is INERT at the push (`Should_Push` ignores `ownerIsWaitingForAReply`), so what landed here is the correctness the filter will need the moment it returns. |
| model + effort reading types and formatter (7f658e0, 411fa21) | Status/SessionModelReading, Formatting/ModelReading_Formatter · **statusline effort was ALREADY IN THE TREE via rename-detected auto-merge — `kit/statusline/statusline.ps1` carries master's whole effort block (`$effort` at line 20, `$json.effort.level` at 24, `$effortSuffix` at 168-170 and every role line that uses it). VERIFIED, not re-added.** `kit/statusline/statusline.sh` (the fork's script) had no effort at all — ported in 11ea3eb (`effort`/`effort_suffix`, five of six role lines) but 11ea3eb's fallback (`*)`) branch was MISSED and stayed without effort until a review caught it; fixed in a follow-up commit alongside a second fixture (`unorchestrated-with-effort.json`) that pins the fallback branch specifically, since no prior fixture combined `AIORCH_ROLE=""` with an `effort` field. New fixtures `supervisor-with-effort.json` and `unorchestrated-with-effort.json`; bash side green on both, PowerShell leg of each joins the known `·`-encoding reds (Task 12). `SessionModelReadingFactoryTests`/`ModelReadingFormatterTests` were never parked (already under `Status/`/`Formatting/`); `ModelOnTheStatusLineTests` stays parked (plan 03, PULSE line). | Task 8 | [x] |
| reply keyboard markup (65107e9) | ReplyKeyboard_Markup, TopicCommandButtons rows | Task 9 (compiles, not wired — spec §7.6) | [x] c02e6ed — `ReplyKeyboard_Markup.cs` and `ReplyKeyboardMarkupTests.cs` were already in the tree, untouched by the merge, and green (`Build_ReplyKeyboardRows` was never referenced by the tests, so no re-add was needed on `TopicCommandButtons`); added `NothingInstallsTheKeyboardYet_BecauseTheCarrierDeleteRemovesTheBar`, which reads `BridgeEngineModel.cs`'s source (the same `Read_EngineSource` pattern as `EveryTopicButtonIsWiredTests`) and fails if `Install_CommandKeyboard` appears — proven RED by a temporary insertion, reverted. **NOT done here:** `Telegram/TopicCommandButtonsTests.cs`'s `TheCommandCount_StaysEven_SoNoRowIsLeftHalfEmpty` / `PauseAndProgress_AreTheLastRow_AndPauseComesFirst` / the `defenestrate` unknown-verb case (row above, "Task 9 / plan 03") concern a PAUSE button the fork's `TopicCommandButtons` does not render at all (see the Task 5 row) — out of this task's brief, left for plan 03. |
| kit prose. **Only ONE of these was inserted by hand and the rest ALREADY LANDED via rename-detected auto-merge (`kit/commands/*.md` -> `kit/skills/*/SKILL.md`) — VERIFY, DO NOT RE-ADD:** ONE OPEN QUESTION in `solo/SKILL.md` = the hand-inserted one (Task 2); ONE OPEN QUESTION AT A TIME in `general-supervisor/SKILL.md` (line 324) = already there; the RESUMED paragraph in `supervisor/SKILL.md` (lines 79-80) and in `solo/SKILL.md` (auto-merged) = already there; *"And now the APP HOLDS THE CHANNEL as well"* in `supervisor/SKILL.md` (line 436) = already there. That last one describes the hold Task 6 wires, so Task 10 should not tick it before Task 6 is in. | kit/skills | Task 10 | [x] 7a600a9 — all five cases VERIFIED already present, none re-added: solo `ONE OPEN QUESTION AT A TIME` (line 212), general-supervisor `ONE OPEN QUESTION AT A TIME` (line 324), supervisor `You may also have been RESUMED` (line 79), solo `You may also have been RESUMED` (line 65), supervisor "the APP HOLDS THE CHANNEL as well" (line 436, Task 6 landed first as noted). Pinned with `AIOrchestratorCoreLib.Tests/Kit/KitProseCarriesTheOwnersRulesTests.cs` (five `[Theory]` cases via `KitRepoFiles.Find_RoleProtocol`, throws if a skill file is missing). No `normative`-sentence counter test exists in this tree (searched whole repo; only two docs mention the convention) so no count needed updating; no prose was inserted, so no sentence count moved regardless. |
| pictures as pictures (da8f66c) | superseded by the fork's EntryAttachment_Policy; master's tests re-homed | Task 11 | [x] 7dae7b7 (tests) + 5857928 (fix) — **initially incomplete, fixed in a second round.** `da8f66c` also added a two-line guard in the mirror loop (`if (content.Trim().Length == 0) content = entry.Subject;`, after marker extraction, before the speaker prefix) so a picture-only entry captions its photo with the subject instead of sending a bare "🟠 ". Merge `91d3402` dropped it while promising a re-port; the first round of this task misjudged the gap as an optional, out-of-contract preservation and deleted master's test for it instead of re-porting the guard. Reviewer traced it with `git log -m -S "content = entry.Subject;"`, ruled it an unfinished re-port (owner-facing, not deferrable), and it is now re-ported in `BridgeEngineModel.cs` (matching master's condition over the fork's `text` variable) with the test restored — RED confirmed with the guard absent, GREEN with it back. |
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
| `AIOrchestratorCoreLib/Sessions/OrchestrationSession/IOrchestrationSession.cs` | `SupervisorEffortOverride`, `ImplementerEffortOverride`, `Paused` | CS0535 ×3 — the interface auto-merged with master's members while its partner `OrchestrationSessionModel.cs` was conflicted and took the fork's version. Taken from the fork whole. | Task 3/Task 4 (effort) — done; Task 5 (`Paused`) — **done, b4fe9b2** |
| `AIOrchestratorCoreLib/Sessions/OrchestrationSessionStore/OrchestrationSessionStoreModel.cs` | `Set_Paused`, `Set_SupervisorEffortOverride`, `Set_ImplementerEffortOverride` | CS0117 ×3 — they call `OrchestrationSession_Factory.CreateFrom_Existing_With{Paused,SupervisorEffortOverride,ImplementerEffortOverride}`, which the fork's factory does not have. Removing exactly these three made the file byte-identical to the fork's. | Task 3/Task 4 (effort) — done; Task 5 (`Set_Paused`) — **done, b4fe9b2** |
| `AIOrchestratorCoreLib/Bridge/EffectiveMode_Resolver.cs` | the `bool paused` parameter and the `if (paused) return Deferred;` branch | CS7036 at `BridgeEngineModel.cs(4035)` — master alone changed this file, so the merge kept master's 6-parameter `Resolve`, and the fork's `BridgeEngineModel` (which won its conflict) calls the 5-parameter one. Taken from the fork whole. **This is the outbound half of PAUSE (decision "PAUSE — asleep, not closed"): re-port it with the rest of `/pause`.** | Task 5 — **done, b4fe9b2**, as a SIXTH parameter on the fork's five-argument `Resolve`, checked ahead of presence; the one call site in `BridgeEngineModel` passes `session?.Paused ?? false` and master's four paused cases are back in `EffectiveModeResolverTests` |
| `AIOrchestratorCoreLib/Watchdog/SessionWatchdog/SessionWatchdogModel.cs` | the `if (session.Paused) continue;` respawn guard (with its comment) | CS1061 — `IOrchestrationSession` no longer has `Paused`. **Minimal fix: only the guard was removed.** Master's two `--resume` log-wording changes in the same file compile and were KEPT, so this file is NOT the fork's version. | Task 5 (the guard) — **done, b4fe9b2** |
| `AIOrchestrator/MainWindow.xaml.cs` | `Describe_CardGlyph` / `Describe_CardTooltip` and their two call sites (back to `Describe_ModeGlyph(session.TelegramMode)` / `Describe_ModeTooltip(...)`) | CS1061 `Paused` ×2 and CS0117 `TelegramDeliveryMode_Glyphs.PAUSED` — the glyph went with the fork's `TelegramDeliveryModes.cs`. The card will read 🔔 for a paused orchestration until Task 5 restores this. | Task 5 — **done, b4fe9b2**; the constant is spelled `PAUSED_BY_OWNER` on the fork, beside its `PAUSED_FOR_LIMIT` |

### Master-only test files PARKED in `AIOrchestratorCoreLib.Tests/_pending-reports/` (Task 2, Step 7)

They do not exist in `fork/ours/integration` at all. The `<Compile Remove="_pending-reports\**" />`
item in `AIOrchestratorCoreLib.Tests.csproj` keeps them out of the build. **Each named task moves its
own files back**; Task 14 deletes the folder and the item.

| parked file | covers | un-parked by |
|---|---|---|
| ~~`SessionJsonSerializerTests.cs`~~ **UN-PARKED (Task 4, f392567)** → `Sessions/SessionJsonSerializerTests.cs` | effort overrides through `session.json` (`CreateFrom_Existing_With*EffortOverride`) — three cases, and **no `Paused` case at all**, contrary to the brief | Task 4 — done |
| `ModelOnTheStatusLineTests.cs` | `supervisorModel` on the topic status line (`TopicStatusLine_Builder.Build` / `_Planner.Plan`) | Task 4 / Task 8 |
| `AStatusLineDoesNotSpendTheOwnersWaitTests.cs` | the owner-answer credit vs a `STATUS`/`WAITING ON` line | Task 7 — **STILL PARKED, moved to plan 03.** Every one of its five oracles is the narration filter's: the two status lines must NOT be pushed, the narration after the turn must NOT be pushed, and the one filed after the answer must arrive inside a *turn-ended completion* — which is the suppression digest. This build pushes everything, so the file cannot go green until `phone.push = filtered` exists. |
| ~~`MergeCommandOpensTheReplyTrackerTests.cs`~~ **UN-PARKED (Task 7, 8ad7756)** → `Bridge/` | `/merge` opens `Track_OwnerReply`. Adapted twice: the harness takes `BridgeTestTiming.Fast()`, and step 3 reads the resolver's *"Turn ended after the owner was answered — nothing further to say"* log line instead of a phone-visible "turn ended" message, because `Build_TurnEndedText` returns null once answered on this build (the wording master asserted is the suppression digest's). Master's step 4 (narration not pushed afterwards) is dropped for the same reason. | Task 7 — done |
| ~~`QuestionWaterfallProbeTests.cs`~~ **UN-PARKED (Task 6, c7f948d)** → `Bridge/QuestionWaterfallProbeTests.cs` | the nine-questions-in-five-minutes waterfall probe. Its `WaterfallTelegram_Fake` was re-implemented against the fork's larger `ITelegramApiClient` (it KEEPS its own fake: the fork's `RecordingTelegram_Fake` does not record a BUTTON message's text, and the button message is the only place a question's sentinel lands). Also adapted: `BridgeTestTiming.Fast()` and computed windows, and the question bodies now carry all five contract lines with `RECOMMEND/RISK/ROW` ABOVE the `QUESTION:` line — an incomplete question is refused and grows no buttons, and prose after the question draws an App coaching entry into the channel the probe counts. **`QuestionHoldPolicyTests.cs` was never parked** — it was already in `Bridge/`. | Task 6 — done |
| `PicturesReachTheOwnerTests.cs` | pictures as pictures — superseded by the fork's `EntryAttachment_Policy`; re-home what still applies | Task 11 |

### Test files that took the FORK'S VERSION and dropped master coverage (twelve)

Two routes to the same place, and both lose master's cases. The first five were **CONFLICTED** and
took `--theirs` at Step 3, by the spec's rule. The other seven auto-merged, stacked master's cases on
a fork API that no longer has them, and were reset at Step 7. Nothing is parked in either group —
**every dropped case is recovered with `git show master:<path>`**, and the NAMES below are what to
look for. Regenerate a list with
`git diff 50a6d8d..master -- <file> | grep '^+.*public void'`.

**A — conflicted, resolved to the fork at Step 3:**

| file | master cases dropped (vs base `50a6d8d`) | the case names | restored by |
|---|---|---|---|
| `Spawning/SpawnCommandBuilderTests.cs` | +181 / -9 | `Build_SupervisorAndSolo_ThinkAtXHighEffort_ByDefault`, `Build_RolesTheOwnerDidNotName_CarryNoEffortFlag`, `Build_ForSupervisor_WithEffortOverride_EmitsEffortRightAfterTheModel`, `Build_EveryOverridableRole_WithEffort_CarriesTheFlagBeforeTheLaunchFlags`, `Build_WithoutEffortOverride_TheRoleDefaultDecides`, `Build_SupervisorAndSolo_AnOverrideBeatsTheRoleDefault`, `Build_ForImplementer_EffortWithoutModel_StillEmitsTheEffortFlag`, `Build_SupervisorAndSolo_WithAResumableConversation_ResumeIt_AheadOfEveryOtherFlag`, `Build_SupervisorAndSolo_WithNothingToResume_StartFresh_ExactlyAsBefore`, `Build_WithAResumeIdThatIsNotAUuid_Throws` | Task 3 |
| `Telegram/TopicCommandButtonsTests.cs` | +35 / -3 | `TheCommandCount_StaysEven_SoNoRowIsLeftHalfEmpty`, `PauseAndProgress_AreTheLastRow_AndPauseComesFirst`, plus an `[InlineData("cmd:defenestrate:5")]` unknown-verb case | Task 9 / plan 03 |
| `Bridge/OwnerAnswerSurvivesFailedSendTests.cs` | +26 / -3 | no new `[Fact]` — master (a) moved the assertion from `"Owner message buffered"` to `"Owner message delivered"` with a 40 s budget, because the credit is raised at DELIVERY (decision 25), and (b) **gave `FailableTelegram_Fake` its `_sentPhotoPaths`, `Has_SentPhoto(...)` and `Sent_Texts()` members.** Those three are a HARD DEPENDENCY of the parked `PicturesReachTheOwnerTests`, `AStatusLineDoesNotSpendTheOwnersWaitTests` and `MergeCommandOpensTheReplyTrackerTests` — their compile errors were exactly `FailableTelegram_Fake` has no `Sent_Texts` / `Has_SentPhoto`. **Restore the fake here BEFORE un-parking any of those three.** | Task 7 (fake + delivery assertion); Task 11 consumes the fake |
| `Configuration/OrchestratorConfigFactoryTests.cs` | +15 | `Create_Empty_UsesTheOwnersModelLadder` (asserts the Fable ladder) | plan 02 |
| `Bridge/AwaySuppressesAppAlertsScanTests.cs` | +1 / -1 | a one-line SOURCE-SCAN string: master looks for `_suppressedEntries.Remove`, the fork's file looks for `_lastSuppressedEntry.Remove`. It is the `_suppressedEntries` LIST of decision 25, so it belongs to Task 7 — **verify at Task 7**, and if the fork's engine keeps a single-slot field the row is satisfied by the fork's wording, not master's. | Task 7 — **VERIFIED, nothing to do.** The fork's engine has NEITHER field (`grep` for `_suppressedEntries` and `_lastSuppressedEntry` over `AIOrchestratorCoreLib/` returns only this test file), because it has no suppression at all. The scan asserts `DoesNotContain("_lastSuppressedEntry")` and passes on its own terms. Re-read it when plan 03 brings the list back. |

**B — auto-merged, reset at Step 7:**

| file | master cases dropped (lines added vs base `50a6d8d`) | why they stopped compiling | restored by |
|---|---|---|---|
| `Bridge/EffectiveModeResolverTests.cs` | +77 | `Resolve(..., paused: …)` — the parameter is gone | Task 5 — **done, b4fe9b2** (master's four paused cases restored; the seven fork cases carry `paused: false`) |
| `Bridge/OwnerPushPolicyTests.cs` | +53 | `OwnerPush_Policy.Is_TurnEndDeclaration` does not exist on the fork's policy | Task 7 — **done, 8ad7756**, PREDICATE ONLY: master's four turn-end cases are restored as `Is_TurnEndDeclaration` assertions. Their `Should_Push(..., ownerIsWaitingForAReply: true) == false` halves are NOT restored — they assert the filter, and on this build they would assert the opposite of what the owner ruled. Plan 03 restores them with the filter. |
| `Launching/OrchestrationLauncherTests.cs` | +156 | `SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL`, `Set_{Supervisor,Implementer}EffortOverride` | Task 3 / Task 4 |
| `Sessions/OrchestrationSessionStoreTests.cs` | +138 | effort overrides and `Set_Paused` / `session.Paused` | Task 3 / Task 4 (effort) — done; Task 5 (pause) — **done, b4fe9b2**: master's `Set_Paused_SurvivesAReload_AndDoesNotDisturbItsNeighbours` and `ASessionWrittenBeforeThePausedFlagExistedIsNotPaused` |
| `Sessions/ReviewerMemberKindTests.cs` | +2 / −2 | `Build_ForReviewer` / `Build_ForImplementer` take 7 args on master, 6 on the fork | Task 3 |
| `Spawning/SessionWindowTitleTests.cs` | +10 / −10 | `Build_ForSolo` (8 args), `Build_For{Implementer,Reviewer,Supervisor}` (7 args) — master's extra argument | Task 3 |
| `Telegram/TelegramDeliveryModeGlyphsTests.cs` | +141 | `TelegramDeliveryMode_Glyphs.Decorate_TopicName` (the 💤 decoration) | Task 5 — **done, b4fe9b2**; master's cases could not be copied (its `Decorate_TopicName` took eight positional arguments and the fork composes from a record), so the 💤 claim is re-stated on the fork's entry point and `All_FlagCombinations` sweeps the sixth flag |

### Master-only production files that survived the merge — present, compiling, and UNWIRED

Nothing had to be done to these: they compile against the fork's CoreLib. But every engine/launcher
call site that USED them lived in a conflicted file that took the fork's version, so each is now
referenced only by its own unit tests. **The type is not the feature — a task that finds the file
already there still has to wire it.**

~~`Bridge/QuestionHold_Policy.cs`~~ **WIRED (Task 6, c7f948d — read per ENTRY inside `Mirror_Append_Async`, tri-state `Held` honoured by the tick loop)** · `Formatting/ModelReading_Formatter.cs` (Task 8) ·
`Spawning/ResumableSession_Resolver.cs` (Task 3 — still reached from `Limits/RateLimits_Reader.cs`,
which only master changed) · ~~`Status/PausedFlag_Marker.cs`~~ **WIRED (Task 5, b4fe9b2 — `Sync_PausedFlags`
on the tick, `Sync_PausedFlag` from `/pause` and from the wake)** ·
`Status/SessionModelReading/*` (Task 8 — still reached from `Usage/UsageTotals_Reader.cs`) ·
~~`Telegram/EffortLevels.cs`, `Telegram/ModelChoices.cs`, `Telegram/ModelEffortButton_Data.cs`,
`Telegram/ModelEffortCommand_Parser.cs`, `Telegram/ModelEffortPrompt_Builder.cs`~~ **WIRED (Task 4)** ·
`Telegram/ReplyKeyboard_Markup.cs` (Task 9).

### Master changes that auto-merged into SHARED files and SURVIVED — do NOT re-port these

The counterpart of the section above, and the more dangerous one: a task that re-adds something
already in the tree produces a duplicate, not a fix. Every file here already carries master's change
in `91d3402`.

**Regenerate this list** (it is cheap, and a later task should re-run it rather than trust this
paragraph):

```bash
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-merge
for f in $(git diff --name-only 50a6d8d..master); do
  [ -e "$f" ] || { echo "GONE (renamed/parked)  $f"; continue; }
  m=$(git diff --name-only master..91d3402 -- "$f")      # empty  => identical to master
  k=$(git diff --name-only dbb6e4e..91d3402 -- "$f")     # non-empty => differs from the fork
  if   [ -z "$m" ] && [ -n "$k" ]; then echo "MASTER-WHOLE  $f"
  elif [ -n "$m" ] && [ -n "$k" ]; then echo "BLENDED       $f"
  elif [ -z "$m" ] && [ -z "$k" ]; then echo "IDENTICAL-BOTH $f"
  else                                  echo "FORK-WHOLE    $f"; fi
done | sort
```

**IDENTICAL TO MASTER** (master's file won outright — the fork never touched it):

- `AIOrchestratorCoreLib/Tailing/Channel_CompactionStep.cs` — **half of Task 7's row is already
  landed.** This is the compaction-guard-inside-the-gate step (decision 25); the C-group red
  `CompactionAsksItsGuardInsideTheGateTests` fails on the OTHER half, which lives in the fork's
  `BridgeEngineModel`.
- `AIOrchestratorCoreLib/Limits/RateLimits_Reader.cs` + `AIOrchestratorCoreLib.Tests/Limits/RateLimitsReaderTests.cs`
  — including `Read_SessionId_OrNull`, which is why `ResumableSession_Resolver` still compiles (Task 3).
- `AIOrchestratorCoreLib/Telegram/TopicStatusMember/` — all three files (`ITopicStatusMember.cs`,
  `TopicStatusMemberModel.cs`, `TopicStatusMember_Factory.cs`): master's model-reading members are
  on the type already (Task 4 / Task 8 wire them, they do not re-declare them).
- every master-only production type and its tests (the UNWIRED list above), plus
  `docs/superpowers/specs/2026-08-06-ai-orchestrator-design.md`.

**BLENDED — both sides' changes are in the file** (so neither side's diff is the whole story; read
the file, not a diff against either parent):

- `kit/statusline/statusline.ps1` — master's whole effort block on the fork's script (Task 8 row).
- `kit/skills/supervisor/SKILL.md` — via RENAME DETECTION from master's `kit/commands/supervisor.md`:
  master's RESUMED paragraph (lines 79-80) and *"And now the APP HOLDS THE CHANNEL as well"* (436)
  are already in the fork's file (Task 10 row).
- `kit/skills/general-supervisor/SKILL.md` — same route: master's `## ONE OPEN QUESTION AT A TIME`
  section (line 324) is already there (Task 10 row).
- `AIOrchestratorCoreLib/Channels/Channel_Compactor.cs` — master's `Compact_IfNeeded(path, mayRewrite)`
  overload alongside the fork's fast path.
- `AIOrchestratorCoreLib/Usage/UsageTotals_Reader.cs` and
  `AIOrchestratorCoreLib/Watchdog/SessionWatchdog/ISessionWatchdog.cs`.
- `AIOrchestratorCoreLib.Tests/Kit/RunToTheEndHookTests.cs` — master's `.paused` cases on the fork's
  file, which is why Task 5 must check this file rather than write it fresh. **VERIFIED (Task 5,
  b4fe9b2): `APausedOrchestrationLetsTheTurnEnd_AndUnpausingTakesThatBack` was already there and
  green, and `kit/hooks/run-to-the-end-check.sh` already carried its `.paused` block on the fork's
  `$SUPERVISION_ROOT`. Nothing re-added. What WAS added is the same exit in
  `kit/hooks/supervisor-ledger-check.sh`, which master does not have, plus
  `APausedOrchestrationEndsItsTurn_ForTheLedgerHookToo` in this class (the fork has no
  `SupervisorLedgerHookTests`).**
- `AIOrchestratorCoreLib/Configuration/OrchestratorConfig/OrchestratorConfig_Factory.cs` — master's
  `claude-fable-5-1` constants on the fork's factory (the four group-B reds).
- `kit/hooks/run-to-the-end-check.sh`, `kit/hooks/supervisor-awaiting-answer-check.sh`, `CLAUDE.md`,
  and `AIOrchestratorCoreLib/Watchdog/SessionWatchdog/SessionWatchdogModel.cs` (master's `--resume`
  log wording kept, the `Paused` guard removed — see the corrections table).

`AIOrchestrator/MainWindow.xaml.cs` was BLENDED and is now **byte-identical to the fork**: master's
only other change there was the stripped UTF-8 BOM, restored in this round, so the paused card glyph
(Task 5) is the single thing that file is missing.

### The one file resolved by hand rather than taken whole

`kit/skills/solo/SKILL.md` — the fork's file, with master's **ONE OPEN QUESTION AT A TIME** block
re-inserted right after the brevity rules, as the brief directs. Master's hand-written
`QUESTION:`/`OPTION:` template bullet was deliberately NOT brought across: the fork's skills say to
call the tool with `--question --option`, and that wording stays. `kit/hooks/run-to-the-end-check.sh`
and `kit/hooks/supervisor-awaiting-answer-check.sh` auto-merged carrying BOTH sides (the fork's
`SUPERVISION_ROOT="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"` and master's `.paused`
exit and `case "$AIORCH_ROLE" in supervisor|solo)`) — Tasks 5 and 6 verify them. `CLAUDE.md`
auto-merged; Task 13 rewrites the merged decisions.

## Red after the merge commit (Windows, 2026-09-11)

One run, on the merge commit **plus** the Task 1 cherry-pick (`40c8b2d`) — the cherry-pick was taken
BEFORE this run on purpose, because its fixture scrubs `AIORCH_*` and the session running the merge
carries `AIORCH_ROLE=solo`; without it the baseline would have carried eleven
`ChannelAppendHelperInteropTests` refusals that no later task will ever see. Command:

```
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug   # full log: ../merge-baseline-tests.log
```

**Non superati: 22 · Superati: 3428 · Ignorati: 4 · Totale: 3454 · 6 m 9 s.**
(The fork's own convention note claims 0 red / 9 skipped — that is its author's macOS. Here the six
macOS file-lock cases RUN, which is most of the difference in the skip count. The four skipped are
the three live smokes that self-skip without `CLAUDE_CONTRACT_LIVE=1`, plus
`PrintTurnLimitResetTests.AStateFileThatCannotBeWritten_CostsOneSession_NotTheWholeResume`.)

**22 recorded in this run; expect 21 in a clean shell** — one of them
(`ChannelAppendTypedEntriesTests.TheOldUntypedCallStillWrites_AndParsesAsUntyped`, group E) is the
`AIORCH_*` leak of the session that ran the merge, proven by re-running the class with those
variables unset. The groups below hold 2 + 4 + 7 + 7 + 2 = 22 names, and they are `comm`-equal to
`../merge-baseline-red.txt`.

**This is the list every later task compares against. Compare the NAMES, never the count.**

---

#### RUN AFTER TASK 12 (Windows, 2026-09-12, HEAD `b932f9d`, a CLEAN shell — `AIORCH_*` unset)

**Non superati: 7 · Superati: 3522 · Ignorati: 4 · Totale: 3533 · 7 m 40 s.** Full log
`../after-task12.log`, red names `../after-task12-red.txt`.

`comm -13` against the baseline names printed **one line**, and it is a flake, not a regression:
`Bridge.APausedOrchestrationIsDormantTests.WhilePaused_NothingIsPushedToTheSupervisor_AndOutboundIsDeferred_UntilTheOwnerWrites`
failed with *"the `.paused` marker was never raised"* — a wall-clock marker written on the engine
tick — and is **green 3/3 run alone**, immediately after. It belongs to Task 5's family and to the
convention note's "re-run a red that had company before attributing it"; nothing Task 12 changed
goes near it. **NO NEW RED.**

**Sixteen of the baseline's 22 names are now green:** all of group C (7 + Task 8's 2), all of group D
(7), group E's environment leak (gone with the clean shell), and Task 7's two in group A. The seven
that remain are:

- **four of group B** — the Fable-vs-Opus default, by the coordinator's ruling above: plan 02 moves
  the constant, nobody in plan 01 touches it;
- **one of group E** — `AWellFormedQuestionIsWritten_WithTheToolsOwnIndexAndStamp`, which the section
  below already records as red in a clean shell too (red 3/3 alone here, unchanged and unowned by
  Task 12);
- **`PrintTurnLimitResetTests.ResumeClear_…`**, fixed in `b932f9d` AFTER this run — the run is the
  one full pass this task was budgeted, so the fix is pinned by twelve isolated runs instead;
- **the `APaused` flake above.**

### A. Master-only tests of features this merge dropped — expected red, they go green when re-ported

```
AIOrchestratorCoreLib.Tests.Bridge.BusyNoticeRespectsAnAnswerScanTests.TheBusyNotice_IsGuardedByTheAnsweredFlag
AIOrchestratorCoreLib.Tests.Tailing.CompactionAsksItsGuardInsideTheGateTests.AnEntryAppendedWhileCompactionWaitsForTheGate_IsStillMirrored
```

Both belong to the owner-answer-credit row (58ff547) — **Task 7**. The first is a SOURCE SCAN: it
greps `BridgeEngineModel.cs` for `!pending.Answered`, which the fork's engine does not contain.

**BOTH GREEN (Task 7, 8ad7756).** The first: the `!pending.Answered` gate is now on the busy notice in
`Resolve_PendingOwnerReplies_Async`. The second was NOT the compaction guard at all — `Channel_CompactionStep`
is master's file and its guard behaves — it was the TAILER: the fork releases a file's trailing entry after
a wall-clock quiet stretch (`TRAILING_ENTRY_QUIET_MILLISECONDS = 4000`), so the fixture's six back-to-back
polls emitted nothing and the "was it still mirrored" assertion read an empty collection. The fixture now
builds its tailer with a 20 ms quiet window and spreads the polls across it; the guard assertions and the
positive control are untouched.

### B. The Fable-5.1-vs-Opus default — the ledger's `plan 02` row, seen as four reds

`Configuration/OrchestratorConfig/OrchestratorConfig_Factory.cs` auto-merged and kept master's
`DEFAULT_SUPERVISOR_MODEL = DEFAULT_IMPLEMENTER_MODEL = "claude-fable-5-1"`; the fork's tests expect
`"opus"` (spec §11.4 keeps Opus as the shipped default and moves Fable into the `classic` preset).

```
AIOrchestratorCoreLib.Tests.Configuration.OrchestratorConfigLoaderGuardrailsTests.Save_OverACorruptConfigJson_StillSucceeds_AndWritesTheKnownKeys
AIOrchestratorCoreLib.Tests.Configuration.PerRoleModelDefaultsTests.AMistypedModelValue_DoesNotTakeDownTheProviderOnTheStartupPath
AIOrchestratorCoreLib.Tests.Configuration.PerRoleModelDefaultsTests.AnEmptyImplementerModel_IsAbsentForEveryRoleThatRidesIt
AIOrchestratorCoreLib.Tests.Configuration.PerRoleModelDefaultsTests.WithNoConfigFileAtAll_EveryRoleGetsItsShippedDefault
```

**RULING (coordinator, 2026-09-11) — these four stay red for the whole of plan 01, by decision.**
`OrchestratorConfig_Factory` ships master's auto-merged `claude-fable-5-1` and the fork's factory
tests expect `opus`. Nobody in plan 01 changes the constant: plan 02 delivers the model catalogue
with Opus shipped and the `classic` preset carrying Fable, and the constant moves THEN. Changing it
now would move the owner's live default before the preset that gives Fable back to him exists. A
later task seeing these four must leave them alone, not "fix" them.

### C. Statusline PowerShell-vs-C# parity, Windows only — seven fixtures (NINE by the time Task 12 ran)

Every one differs on the same thing: the `·` separator and the accented run come back mangled from
the PowerShell reference under this machine's console code page.

**Precisely: `kit/statusline/statusline.ps1` is NOT the fork's file — it auto-merged and carries
master's whole effort block (Task 8 row).** The parity reds are still not a merge effect, and the
reason is checkable rather than assumed: **no fixture under `kit/statusline/fixtures/` contains
`effort`** (15 fixtures, `grep -rli effort` returns nothing), so master's block never executes on any
parity path. What differs is the encoding, and it stays in the baseline until someone fixes that.

**ALL NINE GREEN (Task 12, 797e891) — and it was NINE, not seven.** Task 8's two new fixtures
(`supervisor-with-effort`, `unorchestrated-with-effort`) landed after this baseline was taken and
their PowerShell leg joined this family exactly as that row predicted. The seven above plus those
two: `communicator-green`, `implementer-never-shows-the-ledger`, `reviewer-without-member-falls-back`,
`supervisor-named-short-progress`, `supervisor-stale-progress-hidden`, `supervisor-with-effort`,
`unorchestrated-posix-path-full-context`, `unorchestrated-windows-path`, `unorchestrated-with-effort`.

The cause was one line's absence. `powershell.exe` leaves stdout in the console's OEM code page
unless told otherwise, and the harness reads the child's stdout as UTF-8, so `·` arrived as U+FFFD.
`[Console]::OutputEncoding = UTF8` at the top of `statusline.ps1`, before any output, and the class
went 36/36 (17 bash + 17 PowerShell + the 2 shape facts). **Note for delivery (decision 17): this is
a `kit/` change, so it reaches `~/.claude` only after the MAIN checkout is rebuilt and the app
restarted — the test proves the script, not the installed copy.**

```
AIOrchestratorCoreLib.Tests.Kit.StatusLineScriptParityTests.ThePowerShellReference_RendersTheSameLine_WhereItCanRun(fixtureName: "communicator-green")
AIOrchestratorCoreLib.Tests.Kit.StatusLineScriptParityTests.ThePowerShellReference_RendersTheSameLine_WhereItCanRun(fixtureName: "implementer-never-shows-the-ledger")
AIOrchestratorCoreLib.Tests.Kit.StatusLineScriptParityTests.ThePowerShellReference_RendersTheSameLine_WhereItCanRun(fixtureName: "reviewer-without-member-falls-back")
AIOrchestratorCoreLib.Tests.Kit.StatusLineScriptParityTests.ThePowerShellReference_RendersTheSameLine_WhereItCanRun(fixtureName: "supervisor-named-short-progress")
AIOrchestratorCoreLib.Tests.Kit.StatusLineScriptParityTests.ThePowerShellReference_RendersTheSameLine_WhereItCanRun(fixtureName: "supervisor-stale-progress-hidden")
AIOrchestratorCoreLib.Tests.Kit.StatusLineScriptParityTests.ThePowerShellReference_RendersTheSameLine_WhereItCanRun(fixtureName: "unorchestrated-posix-path-full-context")
AIOrchestratorCoreLib.Tests.Kit.StatusLineScriptParityTests.ThePowerShellReference_RendersTheSameLine_WhereItCanRun(fixtureName: "unorchestrated-windows-path")
```

### D. Print-runner and watcher — seven, of which THREE are diagnosed and FOUR are unattributed

**Three carry an exception class that says what happened**, and they are the class the fork's own
`.claude/rules/code-conventions.md` documents (`Drive_Until` polls a wall-clock deadline while every
tick spawns a fake-CLI process; Windows file-lock semantics are why six cases are honest skips on
macOS):

```
AIOrchestratorCoreLib.Tests.Bridge.ChannelChangeWakerTests.ARootDeletedAndRecreated_IsNoticedOnceAndTheWatchComesBack
AIOrchestratorCoreLib.Tests.Running.PrintTurnDispatcherTests.SecondTurn_ResumesTheTranscript_WithThePromptOnStdin
AIOrchestratorCoreLib.Tests.Running.WakeUpDigestReviewFixTests.AHeldReport_IsNotDeliveredOnceItsMemberIsClosed
```

`ChannelChangeWakerTests` = a `Win32Exception` reported by the watcher; the other two =
`System.IO.IOException : The process cannot access the file` on
`…\repo-1\im…` and on `…\repo-1\.supervisor.print-session.json`.

**ALL THREE GREEN (Task 12).**

- The watcher (**aa44435**): Windows raises `FileSystemWatcher.Error` when the watched root is
  deleted and inotify does not, so the handler logged its line and `Check_WatchStillValid` then
  logged "armed again" — two lines where the contract is one. The error line is now for a watcher
  that failed with its folder STILL THERE; a root that is gone is the deletion, which the validity
  check already owns. A new test raises the private handler by name (nothing else can reach that
  branch) and asserts BOTH halves, so the suppression cannot grow into silence.
- The two IOExceptions (**e72cc84**) were never two tests' bugs: across five runs of the print-runner
  filter the same collision landed on THREE different names, wandering. Windows honours `FileShare`
  and Linux does not, so `Atomic_FileWriter`'s rename and an ordinary read lock each other out here
  and nowhere else. `Storage/Tolerant_FileReader` is the read half (`FileShare.Delete` so the rename
  may proceed, plus a short bounded backoff; it THROWS when it gives up, which is the difference from
  `Safe_FileReader` — an empty string for a print session's identity reads as "there is no session").
  `PrintSessionState_Store` was the reader in the trace; `Safe_FileReader` and
  `UsageTotals_Reader.Read_Text_Safe` go through it too, their swallow kept as the LAST resort.
  **The rename got the same backoff, because the new test found the other direction rather than
  assuming it away:** a replacing rename needs DELETE on the target and an open reader refuses it, so
  `File.Move(overwrite: true)` throws `UnauthorizedAccessException` even against a reader that had
  granted `FileShare.Delete`. Fixing only the reader would have moved the failure onto the writer and
  looked like a fix. Five runs after: zero IOExceptions.

**Four are ORDINARY ASSERTION FAILURES and are NOT attributed to anything.** No exception class
excuses them and nobody has diagnosed them — they are recorded as ground state, and **the first task
to touch `Running/` (Task 12) diagnoses them**:

```
AIOrchestratorCoreLib.Tests.Running.ClosingTurnReviewFixTests.AClosingTurnThatSaysNothing_IsAFailedClosingTurn_AndTheBriefIsStillPending
AIOrchestratorCoreLib.Tests.Running.MemberTrafficRidesOneDigestedTurnTests.TwoMembersReportingInsideTheWindow_BuyOneSupervisorTurn
AIOrchestratorCoreLib.Tests.Running.MultiSourceSupervisorTests.AfterABridgeRestart_NoEntryIsDeliveredTwice_AndNoneIsLost
AIOrchestratorCoreLib.Tests.Running.PrintTurnLimitResetTests.ResumeClear_LeavesANonDeferredSessions_StateFileUntouched_AndLogsNothingForIt
```

What each actually said: `MultiSourceSupervisorTests` — `Assert.Contains() … Not found: "REPORT — two"`;
`PrintTurnLimitResetTests` — `Assert.Equal() … Expected: 1 / Actual: 0`; `ClosingTurnReviewFixTests` —
`Assert.Empty() Failure: Collection was not empty`; `MemberTrafficRidesOneDigestedTurnTests` — its own
message, *"the supervisor never took its boot turn, so nothing below is measuring the digest"*.

**ALL FOUR GREEN (Task 12). They were three different things, and only one was a defect in anything
that ships.**

1. `MultiSourceSupervisorTests.AfterABridgeRestart_…` — the ONLY deterministic one (red every run,
   alone or in company). Dumping the prompt showed the turn DID carry the entry, as
   `REPORT ÔÇö two`: **the fake CLI was the faulty instrument**, not the app. `Console.In` decodes
   with the console's code page on Windows, so the UTF-8 the bridge writes on stdin was mangled
   before it was ever logged. Nothing in the product was wrong — `PrintTurnRunnerModel` and
   `StreamSessionProcess` both set `StandardInputEncoding` to UTF-8. It showed on exactly one test
   because the STREAM path hides it: `System.Text.Json` escapes non-ASCII to `\uXXXX`, so a stream
   message is pure ASCII on the wire and survives any code page, and only the print path sends the
   prompt as plain text. FakeClaude now replaces all three streams with UTF-8 (**9870c7e**);
   `MultiSourceSupervisorTests` 9/9.
2. `ClosingTurnReviewFixTests`, `MemberTrafficRidesOneDigestedTurnTests`,
   `WakeUpDigestReviewFixTests` — LOAD REDS: each passes alone, repeatedly, in a fraction of its
   60-second budget, and ten runs of the print-runner filter put the failure on five DIFFERENT names
   across them. Never a wrong answer, always an extra one (`Assert.Empty` holding one executed turn,
   `Assert.Single` holding two app entries): `Drive_Until` polls a wall-clock deadline every 100 ms
   while each tick spawns a fake-CLI process, so between two polls a loaded box lets a further turn
   complete. The convention note already prescribes the practice; the four classes now sit in
   `Running/REAL_TIME_COLLECTION` (`DisableParallelization`), which is the half that can be enforced
   (**7fda591**). **Not a budget increase** — raising a deadline only makes a genuine failure take
   longer to arrive and would not touch an overshoot at all. Cost: the print-runner filter goes from
   1 m 12 s to 2 m 39 s; the full suite from 6 m 09 s to 7 m 40 s.
   One case in `ClosingTurnReviewFixTests` survived the collection —
   `WithNoPrintRungBeneathIt_TheKilledTurnsRecordSaysTheEntriesAreRetried`, which is NOT in the list
   above and so was a flake this one-run baseline happened to miss. Its two "matching items" were
   turn 1's TIMEOUT record and turn 1's successful RETRY record, i.e. the system working; the
   predicate now names the killed turn by its outcome, which is what the sibling case ten lines below
   already did (**ed0fe0d**). The three assertions on the record's wording are untouched.
3. `PrintTurnLimitResetTests.ResumeClear_…` — **not a load red**: about one run in EIGHT with the
   class alone. The product is right and the test read too early. A refused turn writes
   `RetryNotBeforeUtc` and only THEN drops its `_inFlight` entry, while `Clear_LimitDeferrals`
   deliberately skips a session whose turn is still running (F6 — a snapshot taken before that turn's
   own writes is not something to act on), so "the state file says deferred" is not "the deferral has
   settled". It now waits on `Is_TurnInFlight`, which is on `IPrintTurnDispatcher` for exactly this
   (**b932f9d**). Twelve isolated runs after: green. No assertion changed.

### E. The channel-append tool — two, and ONE of them is this session's own environment

```
AIOrchestratorCoreLib.Tests.Kit.ChannelAppendTypedEntriesTests.AWellFormedQuestionIsWritten_WithTheToolsOwnIndexAndStamp
AIOrchestratorCoreLib.Tests.Kit.ChannelAppendTypedEntriesTests.TheOldUntypedCallStillWrites_AndParsesAsUntyped
```

**Verified, not guessed:** re-run alone with `AIORCH_ROLE`/`AIORCH_ID`/`AIORCH_MEMBER` unset, the
class goes 12 passed / 1 failed — `TheOldUntypedCallStillWrites_AndParsesAsUntyped` is GREEN and only
`AWellFormedQuestionIsWritten_WithTheToolsOwnIndexAndStamp` remains. So the second name above is an
environment leak from the session that ran the merge, not a property of the tree. **Task 1's fixture
scrub covers `ChannelAppendHelperInteropTests` only; `ChannelAppendTypedEntriesTests` sets
`AIORCH_ROLE` per case but lets `AIORCH_ID` and `AIORCH_MEMBER` through from the parent process.** A
later task running from a clean shell should expect 21 red here, not 22.

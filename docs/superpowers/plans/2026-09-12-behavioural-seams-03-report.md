# Plan 03 — the behavioural seams — GATE REPORT

**Date:** 2026-09-23 · **Branch:** `plan/03-behavioural-seams` · **Base:** `e623e5c` (master) + `ad75d02`
(plan docs), master merged in again as `03e9db2` · **HEAD at the gate:** `a9050e9` + the Task 12 commit
**Written by:** the Task 12 implementer (Opus 5.5) for the plan's controller, the solo session of
`ai-orchestrator-29`. The ledger this report is built from is
`.superpowers/sdd/2026-09-12-behavioural-seams-03/progress.md`; every ruling R1–R24 and every deferred
minor below is copied from it, not reconstructed.

**Which copy (CLAUDE.md decision 18):** everything here is the **branch source** in the worktree
`C:\Users\Gianpiero\source\repos\AIOrchestrator-plan03`, built and run in that worktree. Every task
report says the same of itself (the task-N-report.md files in the sdd folder). The running app, the
installed `~/.claude` kit and the main checkout were not read, built or touched by any task of this
plan, and no claim is made about them — a merge is not live until the owner's running binary is
rebuilt from it (decision 23).

---

## 1. The gate (Task 12)

### 1.1 The preset probes reach the phone

`AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetPhoneProbeTests.cs` (new) extends
`PresetProbeTests` (whose docstring now points at it). Four facts, each driving the REAL engine
(`BridgeEngine_Factory.Create_WithTelegramClient`, the aggregation window taken from the settings via
`Create_Custom_WindowFromSettings`) with a fake Telegram client that keeps ONE ordered timeline of
sends (with sound and buttons), edits, reactions, topic creations (with colour), renames and tap
answers. Every expectation is a literal — no value is derived from `classic.json` or `quiet.json`.

**The conversation** (owner message → a `WAITING ON …` narration → the answer → a high-risk question
with a deadline and a default → a tap), the phone as asserted:

| classic (Manu's; also a machine naming no preset) | quiet (Nathan's) |
|---|---|
| `send [Silent] ✓ {⏸ Wait \| ▶ Send now}` | `react owner message 👀` |
| `edit ✓ → ✓✓` — no sooner than 5 s later (6 s window, no finished-message discount) | `react owner message 👌` — no sooner than 1.5 s later |
| — the narration is never sent (held, then forgotten at the answer, R7) | `send [Silent] narration` (D7 b) |
| `send [Rings] answer` | `send [Rings] answer` |
| `send [Rings] question prose` | `send [Rings] question prose` |
| — | `send [Silent] 🤐 going quiet` (third unanswered message) |
| `send [Rings] question {Advanced \| Ultimate \| 💬 Let's talk}` — no 🔐, "this is DENIED (timeout)" | same buttons — 🔐 and "this is DENIED (timeout)" |
| `tap answered: ✓` | `tap answered: 🔐 type the code shown` |
| — | `edit question → 🔐 read-back code` (the code is then typed back) |
| `edit question → ✅ Advanced` | `edit question → ✅ Advanced` |

Both: the choice reaches the owner channel as the owner's entry, the topic name reads `❓ repo-1`
while the question is open and `repo-1` after; quiet's read-back code never reaches the channel;
classic's log says "high-risk confirmation is off".

**The topic** (a new orchestration on a registered repo creates its topic, is silenced, then the
owner types `/done`):

| | classic | quiet |
|---|---|---|
| topic colour (Task 14) | none, nothing written to config.json | `7322096` (0x6FB9F0, blue), persisted |
| General's dashboard bar (D4) | no buttons | `📊 /summary \| ⏳ /pending \| 📉 /limits \| ▶ /resume \| 🌙 /dnd_all` |
| PULSE first line (Task 16) | `0/1 (0%)`, then `PULSE` | `PULSE`; the count reads `0/1 merged · 0 %` |
| PULSE bar (Task 5, R2) | `📸 /screen \| 👁 /show \| 🔀 /merge \| 🧪 /test \| 💻 /pc \| 🏁 /close \| 💤 /pause \| 📊 /progress` — no ⏸ | `⏳ /pending \| 📋 /left \| 👀 /tail sup \| 📉 /limits \| 🔀 /merge \| 🏁 /close \| ⏸ Wait` |
| renames (Task 7, R23) | `crm bug` → `🔕 crm bug` → `✅ crm bug` (never `🔕 ✅`) | `crm bug` → `✅ crm bug` |
| PULSE header | never `🔕 PULSE` | `🔕 PULSE` once silenced |

**Not reached by an engine test, and where it is pinned instead.** The periodic status (Task 8): the
engine hands its sweep `DateTime.Now`, not the injected clock, so no engine test can cross a
half-hour boundary. Its preset half is `PeriodicStatusSweepTests.UnderClassic_ThePeriodicStatusIsPostedOnItsSlot_WhenTheStatusChanged`
and `…UnderQuiet_NoPeriodicStatusIsEverSent`, which drive the real sweep with the phone block
resolved from the real presets through the real loader.

**RED, recorded.** The probes were first run as exploration (the timeline dumped, then every line
checked against the ruling or request that asked for it), then GREEN (4/4). The meaningful RED is that
they catch a preset changing under them — four mutations, each run and restored (`git status` clean
on `kit/` afterwards):

| mutation (embedded preset, rebuilt) | probe that went red | how |
|---|---|---|
| classic `pulse.holdToggle` → `true` | classic conversation + classic topic | `✓ {▶ Send now}` instead of `{⏸ Wait \| ▶ Send now}`; `⏸ Wait` appears on PULSE's bar |
| classic `highRiskConfirmation` removed | classic conversation | `tap answered: 🔐 type the code shown` + a read-back edit |
| classic `phone.finishedMessageSeconds` removed | classic conversation | "the ✓ became ✓✓ after 2.1 s" |
| quiet `phone.appMessagesRing` removed | quiet conversation | `send [Rings] narration` |

### 1.2 `_deferred/` is closed

`AIOrchestratorCoreLib.Tests/_deferred/README.md` deleted and the `<Compile Remove="_deferred\**" />`
item group removed from `AIOrchestratorCoreLib.Tests.csproj`. Both files it listed were moved out by
Task 2 (`f9c9813`) and Task 4 (`d5dddaf`); no file anywhere references `_deferred` any more. This also
closes Task 4's deferred minor "`_deferred/README.md` lists a moved file".

### 1.3 Every `INERT_NOTE` is gone

`grep -n "INERT_NOTE" AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs` returns
nothing: the constant and its last two uses (`topic.onClose`, `topic.modeGlyphs`) were deleted by
`dc163d5` (Tasks 7+10), whose message says so. The rows still marked "READ BY NOTHING YET" are the
settings web page's listen address and secret, deliberately plan 04's and inlined with that reason.
One stale future tense outside the catalogue was corrected in this commit: `PulseField_Names`' summary
said "plan 03 makes `TopicStatusLine_Builder.Build` iterate the list" — it does since Task 4.

### 1.4 The suite, once, whole, alone (Step 4)

Run 2026-09-23 23:29–23:40, HEAD `a9050e9` + this commit's working tree, a clean shell (`AIORCH_*`
unset, WinGet `jq` on PATH), after waiting for another worktree's filtered test host to exit so the
box ran nothing else. (A first attempt at 22:55 was cut off when the controller process restarted;
it is not counted. Before it died it had logged two reds, `ClosingTurnTests.ATransportThatCannotRunAClosingTurn_SaysSo_AndFallsBackToTodaysBehaviour`
and `SoftBoundaryHookTests.ItSpeaksOnceAndThenNeverAgain`; neither repeated in the counted run.)

```
dotnet build AIOrchestrator.slnx -c Debug                          → Avvisi: 0 · Errori: 0
dotnet test AIOrchestratorCoreLib.Tests                            → Non superati: 3 · Superati: 4068 · Ignorati: 4 · Totale: 4075 · 10 m 18 s
dotnet test tools/claude-contract/ClaudeContract.Tests             → Non superati: 0 · Superati: 31 · Ignorati: 8 · Totale: 39
dotnet build AIOrchestrator/AIOrchestrator.csproj -c Debug -warnaserror:CS → Avvisi: 0 · Errori: 0
```

**The set of reds, and each one alone** (`--no-build --filter "FullyQualifiedName~<name>"`, three runs):

| red in the full run | alone | classification |
|---|---|---|
| `Running.WakeUpDigestSecondReviewTests.FiveCycles_WithAnOwnerMessageAndARestart_EachReportWaitsItsOwnWindow` ("cycle 5 was not held by the restarted dispatcher") | green ×3 | flake |
| `Running.ClosingTurnTests.ASilenceKill_KeepsTodaysRetryPath_AndRunsNoClosingTurn` (a second stream attempt started inside the window) | green ×3 | flake |
| `Running.WakeUpDigestReviewFixTests.ASecondReportIsHeldToo_TheDigestIsNotSpentOncePerProcess` ("the first report was not digested at all") | green ×3 | flake |

All three are print-runner tests under `Running/` that race the FakeClaude process and wall-clock
windows. No file under `Running/` was changed by plan 03. They are not on the family list below by
name, but they are in its class (`ClosingTurnReviewFixTests` is their neighbour) — Task 11's to add.
**No red is real; the gate is not stopped.** The four new phone probes passed in the full run.

---

## 2. Per task

Every task read and edited the **branch source** of this worktree only (each task-N-report.md opens
by saying so). "Controller re-ran" is the controller's own `--filter` run after the implementer's,
before committing; per the global constraints no task before Task 12 ran the whole suite.

| task | what was built | commits | verification (controller) | reds met, and whether they repeated alone |
|---|---|---|---|---|
| 1 — the two settings blocks | `IPhoneSettings` / `IPulseSettings` resolved catalogue → preset → config.json; resolver Int32→Int64 fix; fold/attach floor 1→0; R1 (classic stops stating `replyKeyboard: on`) | `f05db47` | review clean | none |
| 2 — who rings (`phone.push`) | `OwnerPush_Policy.Decide` → `OwnerPushDecisions`; suppressed-entry store; turn-end digest; General exempt (D8); D11 move + key drop; fix round: R9 overflow once at Info, R7 clear-on-send, R8 a completion carrying held words rings | `f9c9813`, `ca9d932`, `5bde25b`, `88fb379`, `1351548`, `3a42720`, `fffca37`, `2a68a47` | review clean after 1 round | none; `f9c9813` does not compile alone (D11's verbatim move) |
| 3 — what rings (`phone.appMessagesRing`) | D7 (b): narration that is not question/BLOCKED/file/answer is silent when off | `cfddd6b` | review clean | wide Bridge runs had one different single red each, green alone — flake family |
| 4 — the pulse | `pulse.fields`, `pulse.stepMinutes`, the `modelEffort` field; R10 port of `ModelOnTheStatusLineTests` (old→new strings in `b01df5d`'s body) | `a028f99`, `d5dddaf`, `b01df5d` | 21/21 + 125/125 | 8/11 red at the R10 port step by design; C: disk hit 0 bytes during a commit (recovered) |
| 0 (session 2) | master merged in | `03e9db2` | no conflicts | — |
| 5 — the bars | `pulse.buttons`, `general.buttons`, `pulse.holdToggle` (never both, D10 fallback), `KNOWN_COMMANDS` widened; fix: `general.buttons` hot reload via the render key | `2eca01d`, `6107216` | 129/129, then 60/60 | none |
| 6 — the receipt | `phone.receipts`: classic ✓ → ✓✓, quiet 👀 → 👌 | `97fce96` | 110/110 (with 6b) | `97fce96` alone has 4 red ticks tests, green at `07ead9e` (the branch merges as a unit) |
| 6b — Send now | `▶ Send now` beside `⏸ Wait` on the ✓ (R2) | `07ead9e` | 110/110 | none |
| 6c — the held ✓✓ | per-message edit slot drained on the tick; one hold, one receipt; R19 | `4c5356f`, `b2eefcf` | 163/164 once under load (name not captured), then 164/164 ×2; 167/167 | one unnamed red under load, not repeated |
| 13 — aggregation window | `phone.aggregationSeconds` / `phone.finishedMessageSeconds`; classic 6 / 6 | `3435680` | 198/198 | `EntriesTheMirrorGaveUpOn_ArriveAsOneDigest…` intermittent (2/56 vs 0/60 on HEAD, also once on 6c) — pre-existing, Task 11 family |
| 5b — any command on a bar | a tap on an unlabelled verb dispatches as the typed command; master's six emoji labels restored (R16); tapped `/dnd_all` toggle fix | `8ff90ac` | 128/128 | none |
| 16 — compact progress | `progress` field, drawn above the header when first; classic leads with it | `4a47532` | 209/209 | none |
| 8 — periodic status | master's status re-ported, change-gated (R6), away digest on its own slot (D9), sweep out of the engine; fix: key without sup/solo working state, seeded on adopt, skill prose | `8d548f0`, `eda11a6` | 199/199, clean rebuild + 303/303 | none |
| 15 — high-risk code | `highRiskConfirmation` (classic off); R21 a high-risk question never takes a default; R22 | `5ec162b`, `ada04bb` | 247/247, 501/501 | none |
| 9 — reply keyboard | D5 off for both: nothing wired, the INERT note replaced by the reason | `834e56b` | with 15 | none |
| 14 / 14b — topic colour | `topic.repoColours` (classic off); `/pending` draws 🔐 only when a code will be asked | `256d340`, `c9fc0be` | 257/257 | none |
| 7 — mode glyphs | `topic.modeGlyphs` name / pulseHeader, one composer; R23 ✅/🧪 replace the delivery glyph | `d54954b`, `a9050e9` | 338/338; 371/372 | `ADeleteTelegramWillNeverAccept_TellsTheOwnerOnce…` red under load, 4/4 alone |
| 10 — topic close | `topic.onClose` default `delete` (D2, R4), `close` via `closeForumTopic`; a kept closed topic renamed once (`TelegramTopicFinalNameUtc`) | `dc163d5`, `a9050e9` | with 7 | same as 7 |
| 11 — flakiness campaign | **DEFERRED, not run** (R13, R24): it needs ~1 h of exclusive machine time; every red seen during the plan passed alone | — | — | — |
| 12 — the gate | §1 above | `3b65b05` | §1.4 | §1.4 |
| 17 — PULSE only when changed (owner [94], "Fix it") | fixes §5.3: the engine remembers the text AND the render key (`WrittenTopicStatusLine`); the planner compares text with text and sees a bar-only change itself, under its one back-off; fix round (R27): a third value, what the owner last saw at the bottom — an answer that buries and changes PULSE brings it back once | `64f1b27`, `d415e33` (probe), `6276616` (R27 fix, shared files) | 232/232; fix round 236/237 | `UnderQuiet_TheTopicLooksLikeTheForksTopic` duplicate rename under load, 3/3 alone; `64f1b27` and `d415e33` each red alone on their engine probes until the next commit (shared engine file) |
| 18 — away delay (owner [95]) | `away.afterMinutes` (0–1440, 0 = never by itself), shipped 15, classic 60 (R25); read at the point of effect; HOLD prose built by `AwayMode_Policy.Build_HoldNotice`; the away check and the owner-silence stamp on the injected clock | `891e5fb` | 312/312 | `HighRiskAndDeadlineProbeTests…WithNoCode` (file lock) and `TheSameCallbackDeliveredTwice_IsActedOnOnce` under load, 2/2 alone |
| 19 — "unchanged N min" back (owner [100]) | `pulse.unchangedFor`, shipped on, rides whichever of `progress` / `merged` is drawn — the count on top included; fix round: R28, neither preset states it | `6276616`, this fix commit | 423/423 | `EntriesTheMirrorGaveUpOn…` under load, alone green (known flake) |

---

## 3. Decisions D1–D12

| # | answer | by |
|---|---|---|
| D1 | (a) re-port master's periodic STATUS; `phone.status.periodic` shipped/classic `true`, quiet `false`; interval 30 | owner, 2026-09-14 |
| D2 | `topic.onClose` default `delete`; `close` kept and implemented (R4) | owner |
| D3 | the labelled verbs keep their emoji, others render `/verb` — then R16 (Task 5b) restored master's six labels and made every verb tappable | coordinator; R16 on the owner's request |
| D4 | an empty `general.buttons` sends General's message with no `reply_markup` | coordinator |
| D5 | reply keyboard off for both presets; Task 9 wires nothing | owner |
| D6 | moot while D5 is off (`general.buttons` if ever on) | coordinator |
| D7 | (b) `appMessagesRing = false` silences narration that is not a question, BLOCKED, file or answer | coordinator |
| D8 | `phone.push` applies to orchestration owner channels only; General exempt | coordinator |
| D9 | the interval moves the periodic status only; the away digest keeps `SLOT_MINUTES` | coordinator |
| D10 | the incoherent hold-toggle cross falls back to the PULSE bar with one warning | coordinator |
| D11 | move verbatim, then a second commit drops `telegramItalianLayer` | coordinator |
| D12 | three consecutive local full runs + two CI runs of both legs — R3: CI not run by this plan (needs a push, the owner's call); the local bar belongs to Task 11, which is deferred (R24) | coordinator; R3/R24 controller |

## 4. Rulings (controller, binding) — R1–R28

R1 classic stops stating `replyKeyboard: on` in Task 1 · R2 receipt buttons follow the hold-toggle
placement ([Wait, Send now] or [Send now]) · R3 CI legs not run by this plan · R4 `onClose` default
`delete`, `close` implemented · R5 (superseded by R10) · R6 the no-change guard applies to the
re-ported status · R7 Send clears what was held before it · R8 a completion carrying held words rings ·
R9 digest overflow logged once per fill at Info · R10 port, don't move, `ModelOnTheStatusLineTests` ·
R11 the owner's three 2026-09-23 settings become Tasks 13–15 · R12 commit trailer Opus 5.5 · R13 Task
11 deferred to after 12 · R14 new rows ship today's behaviour, classic states the owner's way · R15
execution order · R16 any command on a bar, master's labels back · R17 no build between `2eca01d` and
Task 6 goes into the running app · R18 the held-✓✓ gap is Task 6c · R19 a typed GO releasing a hold
gets no ✓ of its own · R20 the periodic status keeps master's channel path · R21 a high-risk question
never takes a default, code or not · R22 history not rewritten; the fix commit corrects the record ·
R23 ✅/🧪 replace the delivery glyph on the name · R24 Task 11 to its own session · R25
`away.afterMinutes` is 60 under classic — the owner said only "too soon"; announced in entry [97] and
changeable from /settings · R26 (SUPERSEDED by R28) quiet stated `pulse.unchangedFor` false · R27
"buried AND changed" means changed since the owner last saw PULSE at the bottom: the line remembers the
rendering it had when last unburied, in-place edits while buried do not move it, and at quiet a buried
line that differs is reposted once · R28 neither preset states `pulse.unchangedFor`: the fork never
removed the clause (quiet's shipped list draws `merged`, which carries it); it was lost only on classic,
through Task 16's `progress`.

## 5. Standing questions this plan could not close — inherited by the next plan

1. **Task 11 (the `Bridge/` flakiness campaign) was not run** (R13, R24). Its known family, all green
   alone whenever seen: `TolerantFileReaderTests.AReaderHoldingTheFileForAMoment_DoesNotCostTheAtomicWriterItsRename`,
   `ClosingATopicReallyDeletesItTests.ADeleteTelegramWillNeverAccept_TellsTheOwnerOnce…`,
   `EntriesTheMirrorGaveUpOn_ArriveAsOneDigest…` (two 1-s wall-clock timers racing a fake-clock give-up),
   `ClosingTurnReviewFixTests`, `TheSameCallbackDeliveredTwice_IsActedOnOnce`, `APausedOrchestrationIsDormant`.
   D12's bar (three local full runs + CI) is therefore NOT met by this plan.
2. **CI has not run on this branch** (R3): a Linux-only red would surface at merge.
3. **FIXED (2026-09-24, Task 17: `64f1b27` + `6276616`, ruling R27) — kept here as the record of what
   the gate found.** The owner answered "Fix it" (entry [94]); the engine now remembers the text and the
   render key and hands the planner the text, and R27 made "changed" mean "since the owner last saw it
   at the bottom". The original finding follows. **PULSE is re-edited on every tick and reposted after
   every burst even when unchanged — found by this gate, pre-existing (on master since the fork's
   `2143db8`).** The
   engine stores `TopicStatusLine_RenderKey.Build(text, buttons)` in `_statusLineTextByOrchId` and
   hands it to `TopicStatusLine_Planner.Plan` as `lastWrittenText`; the planner's
   `TopicStatusLine_Decider.Decide` compares it with the RAW text, so they never match: every tick
   answers Edit, and `somethingNewToSay` is always true, so the repost gate reduces to "buried and
   quiet 10 s". Measured in the probes: an identical PULSE edit every ~60 ms at the test's 20 ms tick, and a
   delete + repost 10 s after every supervisor message with the same content. In production the real
   client's per-message edit gap throttles the edits to one every 30 s per topic (each answered "not
   modified"), but the repost is owner-visible and contradicts the owner's 2026-09-09 rule ("deleted and
   re-posted only when it is buried AND its content changed"). Live damage → a question for the owner,
   not a ledger line: fix it (small: compare render key to render key in the planner) or park it.
4. **A tap still lifts app-wide DND where the typed command would not** (Task 5b) — needs an owner
   ruling on whether any command counts as presence. **`/clear` becomes a one-tap destructive button**
   if the owner ever puts it on a bar (no preset does).
5. **Held narration outside any reply turn never reaches the phone under `filtered`** (Task 2): the
   release net `Break_SilentDeadlock_Async` does not exist in this tree (plan PARKED line 1). Surfaced
   to the owner, not closed.
6. **Owner-visible change with no preset:** PULSE becomes classic's field list (drops waiting-on-you,
   N closed, last; adds model readings, and since Task 16 the count on top) — spec §6.4, reported.
7. **`phone.replyKeyboard = on` installs nothing** (D5): a legal value with no effect.
8. The WPF close dialog and the close prompt still say "deleted" under `topic.onClose = close` (PARKED).
9. `CLAUDE.md`'s PAUSE bullet enumerates wakers the merge renamed or deleted (plan PARKED line 2).

## 6. Deferred minors (from the ledger, not fixed)

- T1: `pulse.buttons` description note (closed by Task 5); `attachEntriesAbove` says "instead" but sends in addition; above-max fold/attach falls to the default, class doc names only negatives; enum words case-sensitive at the validator; four enum parsers restate the catalogue default; the find-definition helper ×4 and `(int)Resolve_Long` ×3; the resolver calls a static on an internal model.
- T2: `f9c9813` does not compile alone; `AWaitingOnSubjectDoesNotSpendTheCreditTests` docstring false under filtered; `AnEmptyBody` pins `''`; `Format_ForTurnEndDigest` strips only `STATE:`; a test name contradicting its Filtered rows; `ISuppressedEntries` verbs; a drained digest discarded with no log line; R8's Silent half pinned only as "nothing sent"; clear-on-send loses a closing report followed by a question (as master); a duplicated harness.
- T3: another copy of the engine-source helpers; the catalogue does not say General is not exempt from the sound rule; filtered + ring-off keeps R8's ring (no preset uses it).
- T4: the PULSE call site grew ~10 lines in the engine; case 9's name; another polling helper; `.usage.json` read twice per member per tick; no hot-reload engine test for pulse settings; `d5dddaf` does not build alone; two stale docs.
- T5: a `💤` comment vs D3; `MACHINE_LOG_SCOPE` fifth copy of `GLOBAL_ORCH_ID`; a synthetic tap under classic without `holdToggle`; a stale doc reason; `AnEmptyKeyboard…` does not assert the send; `TAP_ROUTED_COMMANDS` a second listing held by a two-way test.
- T6/6b: `97fce96` red alone; `Is_TheTickTheDeliveryWillEdit` restates a precedence; `phone.receipts` wording; copied harness helpers.
- T6c: a newer text staged mid-drain lands one gap later; the six-field attempt tuple ×6; quiet typed WAIT before any message ends as ✓✓ text; no test pins that a typed GO with no hold still gets its receipt; the put-back Release race (pre-existing).
- T13: `WINDOW_SECONDS=3` documented as production's; a doc points at a deleted constant; "2.5 s after the ✓" can false-red under >3.5 s load.
- T5b: a mode tap + another tap in one batch can re-toggle DND; the `/switch` tap test is loose.
- T16: `figuresUnchangedFor` still computed under classic.
- T15/9: `replyKeyboard on` installs nothing; four older guardrail keys a preset could state and the loader ignores; `HighRiskLockPolicyTests` folder drift; `UnderClassic_` tests can false-red on two config writes in one clock tick.
- T7/10: `/clear` on a closed-kept topic writes `_appliedTopicNames` directly (self-heals).
- T19 (M1): the engine's `pulse.UnchangedFor` argument to the planner is untested — the unchanged-for tracker (`_figuresSinceByOrchId`) runs on the wall clock, so an engine test would wait 10 real minutes; the planner and builder threading is pinned.

## 7. Verdict

**Phase 3 of spec §10 is met on its own terms, with two stated exceptions.** The engine obeys the
catalogue for every `Phone`, `Receipts` and `Pulse` row. The probes prove the two presets give two
different phones: classic's is the owner's, quiet's is the fork's. Nothing in the catalogue is left
saying "plan 03", and `_deferred/` is gone. The one full run has three reds, and each one passed three
times alone.

The two exceptions:
- D12's flake bar is not met. Task 11 is deferred (R24), and CI has not run (R3).
- A pre-existing PULSE defect is now measured and is waiting on the owner (§5.3).

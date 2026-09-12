# Plan 02 — the settings catalogue, the resolver, the two presets — GATE REPORT

**Date:** 2026-09-12 · **Branch:** `plan/02-settings-catalogue` · **Base:** `master` at `dd68771`
**Written by:** the solo session of `ai-orchestrator-24`, after task 8's implementer was killed
mid-run by the account's weekly usage limit (resets 2026-09-15 13:00 Europe/Rome). It had completed
the preset probes and full-suite run 1; runs 2, the isolation probes and this report are the
session's own.

**Which copy (CLAUDE.md decision 18):** everything below is the **branch source** in the worktree
`C:\Users\Gianpiero\source\repos\AIOrchestrator-plan02`, built and run in that worktree. The running
app (`bin\Debug - Copia\…`, decision 23), the installed `~/.claude` kit, and the main checkout were
not touched and no claim is made about any of them.

---

## 1. What plan 02 delivered

Eighteen commits, in order:

| # | commit | what |
|---|---|---|
| 1 | `741cb70` | the plan itself |
| 2 | `59b1647` | task 1 — the catalogue's vocabulary and the setting-definition triple |
| 3 | `0ce53c0` | task 2 — one walk over the raw config tree for every catalogue path |
| 4 | `7014a23` | task 1 fix — the named validator applies where it is read |
| 5 | `9970240` | task 3 — catalogue v1, every key the merged tree reads, registered as data |
| 6 | `1fe64ff` | task 3 fix round 1 — the CRITICAL long-key gap and six rulings |
| 7 | `4a4939d` | task 4 — `classic` and `quiet` ship as embedded data |
| 8 | `6c544e6` | task 5 — one resolver, four layers, no disk |
| 9 | `31aede1` | task 6 — the shipped model default becomes opus |
| 10 | `5c45653` | task 6 fix round 1 — the four model rows come out of `classic` |
| 11 | `39a5584` | task 5 fix round 1 — both shapes, long ids, pinned existence |
| 12 | `1d2a216` | task 6 fix round 2 — a mistyped preset word cannot take config loading down |
| 13 | `015b6dd` | task 7 — effort is data; the role default leaves the binary |
| 14 | `636c39f` | CLAUDE.md's effort bullet corrected, which task 7 made untrue |
| 15 | `2ce8dcc` | plan 03 written |
| 16 | `41a82a6` | plan 04 written |
| 17 | `c4f37da` | task 7 fix round 1 — a resolved effort reaches every role; blank is absent in ONE place |
| 18 | `e9494dc` | plan 05 written |

Plus this task: the preset probes and this report.

## 2. The four tests held red BY RULING for the whole of plan 01 are GREEN

This was plan 02's central promise. Plan 01's gate report
(`2026-09-11-fork-merge-01-report.md:483-491`) named exactly four, called them deterministic —
*"They fail identically in every run, on both OSes, in 1 ms each"* — and recorded that **plan 02
moves the constant**. It did.

| test | state |
|---|---|
| `Configuration.OrchestratorConfigLoaderGuardrailsTests.Save_OverACorruptConfigJson_StillSucceeds_AndWritesTheKnownKeys` | **Passed** |
| `Configuration.PerRoleModelDefaultsTests.AMistypedModelValue_DoesNotTakeDownTheProviderOnTheStartupPath` | **Passed** |
| `Configuration.PerRoleModelDefaultsTests.AnEmptyImplementerModel_IsAbsentForEveryRoleThatRidesIt` | **Passed** |
| `Configuration.PerRoleModelDefaultsTests.WithNoConfigFileAtAll_EveryRoleGetsItsShippedDefault` | **Passed** |

Read out of `TestResults/plan02-gate-run1.trx` by name, not inferred from a green namespace.

**This was not free, and the reason is worth recording.** Task 6 hit a conflict in which two tests
could not both pass, and the cause was that `kit/presets/classic.json` restated the old model on all
four judging roles. The catalogue's registered Opus default was therefore **unreachable in practice
on any machine naming no preset** — the common case, since an absent `preset` means classic. The
four rows were ruled out of `classic` (`5c45653`), which is what let the default actually arrive.

**And a layer above that, the same trap was live on the owner's own machine.** Their
`~/.claude/supervision/config.json` stated `supervisorModel` and `implementerModel` as
`claude-fable-5-1`, and a stated key beats both the preset and the catalogue. So the Opus default
they had asked for could not have reached them even with this branch merged. Raised as a question
(owner channel entry 216), answered "remove them, take Opus" (entry 217), and removed with a
timestamped backup kept beside the file.

## 3. The preset probes

`AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetProbeTests.cs` — 46 cases.

- A machine on `classic` resolves to master's way; a machine on `quiet` to the fork's.
- **Values are spelled out, not derived from the preset files.** Deriving them would assert that a
  file equals itself, and spec §12 names `quiet` reproducing the fork's phone as a top risk — a risk
  is not mitigated by a tautology.
- **A missing catalogue path THROWS** rather than measuring nothing (decision 20). A null expectation
  therefore means exactly one thing: the entry resolved to JSON null.
- `EveryCatalogueEntry_ResolvesUnderBothPresets` asserts the catalogue is **non-empty before walking
  it**, so an empty registry cannot pass the walk vacuously — the same nothing-is-ALLOW shape.
- The `classic` arm passes a null config tree, so it exercises "absent `preset` means classic"
  rather than naming the word.

The plan's own draft of this file carried `[InlineData("models.supervisor", "\"claude-fable-5-1\"")]`
under `classic`. That was written before `5c45653` and is **corrected here**: both presets are now
silent about models, so both resolve to the shipped default, and a row reading the old model again
would mean a preset had quietly taken the default back.

## 4. The suite — two full runs, and a NAMED account of every red

Totals were **3537** at plan 01's gate and are **3650** now: plan 02 added 113 tests.

| | total | passed | failed | skipped |
|---|---|---|---|---|
| run 1 | 3650 | 3641 | 5 | 4 |
| run 2 | 3650 | 3637 | 9 | 4 |

**Run 1's five:** `AttachmentsReachThePhoneTests.AMissingFile_…`,
`AttachmentsReachThePhoneTests.AnHtmlFileSentAsAPicture_…`,
`QuestionContractProbeTests.AnIncompleteQuestion_…`,
`PrintTurnDispatcherTests.ATurnThatOutlivesTheTimeout_…`,
`TolerantFileReaderTests.AnUnauthorizedAccess_…`.

**Run 2's nine:** `AttachmentsReachThePhoneTests.AMissingFile_…`,
`EffortDialOnABridgeDrivenSupervisorTests.SlashEffort_…`,
`TheBridgeNeverLiesAboutDeliveryTests.ADocumentWithNoCaption_…`,
`ChannelAppendTypedEntriesTests.AWellFormedQuestionIsWritten_…`,
`ChannelAppendTypedEntriesTests.TheOldUntypedCallStillWrites_…`,
`OrchestrationLauncherTests.TruePids_AreSyncedFromPidFiles_…`,
`TolerantFileReaderTests.AFileLockedExclusivelyForAMoment_…`,
`TolerantFileReaderTests.AReaderHoldingTheFileForAMoment_…`,
`TolerantFileReaderTests.AnUnauthorizedAccess_…`.

Every name falls in one of the three families plan 01's gate report documents by mechanism
(`…-01-report.md:506-526`): bridge-tick wall-clock overshoot, Windows file-sharing collisions on the
**test** side, and the `AIORCH_MEMBER` environment leak. **No red is in `Configuration`,
`SettingsCatalog` or `Spawning`** — that is, none is in the code plan 02 wrote. `OrchestrationLauncherTests`
appears once, but the case is `TruePids_AreSyncedFromPidFiles_OnceTheShellsWriteThem`, a pid-file
timing case, not an effort case; the effort cases in that class passed in both runs.

### 4.1 THE INTERSECTION IS NOT EMPTY, AND THAT IS STATED RATHER THAN BURIED

Two names failed in **both** runs:

- `Bridge.AttachmentsReachThePhoneTests.AMissingFile_IsAWarningAndACoaching_NeverAThrow`
- `Storage.TolerantFileReaderTests.AnUnauthorizedAccess_ThatGoesAway_IsRetriedRatherThanThrown`

Plan 01's gate used an empty intersection as its flake proof, so a non-empty one cannot simply be
waved at the same word. Both were investigated.

**Both fail on Windows temp-file access, and neither fails inside production code plan 02 touched:**

- `AMissingFile_…` throws `IOException: The process cannot access the file … owner-channel.md
  because it is being used by another process`, **inside the test's own helper `Channel_Text`**
  (`AttachmentsReachThePhoneTests.cs:202`) — a raw `File.ReadAllText`. The plan-01 report names
  `AttachmentsReachThePhoneTests.Channel_Text` **by name** as the mechanism of this family and counts
  "141 raw `File.ReadAllText` calls in the test project, 34 files of them under `Bridge/`".
- `AnUnauthorizedAccess_ThatGoesAway_IsRetriedRatherThanThrown` throws
  `UnauthorizedAccessException` on a temp file. The test asserts that an access which **goes away**
  is retried; it fails when the access does not go away inside the retry window. That is a
  wall-clock-window case by construction.

**The decisive evidence is the isolation probe.** Running `TolerantFileReaderTests` and
`AttachmentsReachThePhoneTests` alone, twice:

| isolation run | result |
|---|---|
| 1 | **15 / 15 passed** |
| 2 | 14 / 15 — and the failure was `AnHtmlFileSentAsAPicture_…`, a **third** case, in neither intersection |

So the family's failing **member varies run to run**, including in isolation. The two-run
intersection is coincidence: with roughly eight unstable members and five to nine firing per run, two
runs will share names by chance. The intersection is evidence about sample size, not about a
regression.

**Honest qualification:** `AnUnauthorizedAccess_…` appeared in three of the five full runs made on
this branch today, which is more often than any member in plan 01's five-run sample (*"none appeared
three times"*). Today's runs were made with several agents and a build competing for the same disk,
which is exactly the load this family is sensitive to. That is an explanation, not a dismissal —
recorded here so that whoever runs plan 03 task 11 knows this member is the loudest one.

**Plan 03 task 11 is the campaign** — the tolerant test reader, `Bridge/` converted first in its own
commit, and a guard that refuses to run if it cannot find its sources. It is independent of every
other task in that plan and should run throughout it.

## 5. What plan 02 did NOT do, and who picks it up

- **Behaviour.** Plan 02 registers, resolves and ships values; it wires no seam. Every
  `phone.*`, `pulse.*` and `topic.*` value the probes pin is a resolved value with no reader yet.
  **Plan 03** wires them.
- **Renderers.** Nothing displays or edits a setting yet. **Plan 04** — which also inherits plan 02's
  self-review item 6 by name: `OrchestratorConfig_Loader.Save` writes the four model keys
  unconditionally, so it materialises a resolved value as a stated one. That is the defect that
  pinned the owner's config.json. It is plan 04 task 2's, and it needs `Save` to know a value's
  ORIGIN, which `IOrchestratorConfig` does not carry.
- **The `Kit` catalogue category is registered EMPTY** and exempted by name in
  `SettingsCatalogTests`. **Plan 05** fills it with `repos[].code` and deletes the exemption.
- **`effort.communicator` and `effort.general` reach a terminal command line** as of `c4f37da`, but
  **bridge-driven runners still ignore `ISessionLaunch.Effort` entirely**, and nothing spawns a
  communicator any more. Recorded, not fixed.

## 6. NOTICED (not fixed)

- **The MODEL overrides carry the identical blank drift** that `c4f37da` closed for effort:
  `OrchestrationLauncherModel` uses a bare `??` on `SupervisorModelOverride` /
  `ImplementerModelOverride`, while `SessionScoped_Reader` maps the same keys blank-is-absent. A
  hand-edited `"supervisorModelOverride": ""` spawns with **no** `--model` flag rather than the
  config default — the shape of the 2026-09-10 `reviewerModel: ""` incident, one layer up. Predates
  this plan; not reachable from the phone. `SessionScoped_Reader.Stated_OrNull` now exists and makes
  it a one-token change at each site.
- **A scope mismatch waiting for plan 03:** the launcher applies `ImplementerEffortOverride` to a
  solo and a reviewer, but `effort.solo` and `effort.reviewer` are `SettingScopes.Machine`, so the
  resolver's session tier can never report that override. A renderer asking "what will this solo
  spawn with" would answer with the role default and miss the owner's dial.
- **`kit/install.ps1` rebuilds `config.json` from six properties** and overwrites. Today that already
  drops `voiceTranscribeCommand`, `telegramStatusScreenshots` and `orchestrationTokenBudget`; after
  this plan it would also take `preset`, `effort`, `runners` and every `phone.*` key. `install.sh`
  was fixed for exactly this and carries a dated comment; the fix never crossed. **Plan 05 task 5.**
  Until then the installer is the hazard, not the app.
- **`AIOrchestrator.Daemon.csproj` does not ship `kit/grammar/*.json`** while the WPF app does, so on
  the Linux host the kit's channel tool is installed without the grammar it refuses typed entries
  without. **Plan 05 task 7.**

## 7. Verdict

Plan 02 is complete. Its central promise — the model and effort defaults become DATA, and the four
tests held red by ruling go green — is kept and verified by name. The two preset probes pass, the
catalogue resolves under both presets with every value satisfying its own definition, and no red in
either full run is in code this plan wrote.

It is ready for the owner to merge. The merge is theirs to ask for and they batch them deliberately.

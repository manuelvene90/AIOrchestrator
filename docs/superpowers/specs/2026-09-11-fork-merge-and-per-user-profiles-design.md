# Fork merge and per-user profiles — design

**Date:** 2026-09-11 · **Status:** draft for the owner's review · **Orchestration:** `ai-orchestrator-24` (solo)
**Inputs:** `nathanthegrey/AIOrchestrator` at `dbb6e4e` (branch `ours/integration`, 295 commits past the
merge base `50a6d8d` of 2026-09-07); master at `a58ef7e` (20 commits past the same base); the owner's
rulings in the orchestration channel (entries 8, 14, 15, 17); the brothers' conversation of 2026-09-11.

Every code citation below names the copy it was read from: **[M]** = master's working tree, **[F]** = the
fork clone. Symbols are cited in preference to line numbers, because both trees move.

---

## 1. The problem

Two people use one app in two ways, and the codebase has split in two.

- **Manu (upstream, master)** runs the WPF app on Windows with a screen. Sessions are Windows Terminal
  tabs that arm their own file watchers. He wants a periodic STATUS message, a PULSE with model and
  effort, a reply keyboard, ✓ → ✓✓ receipts carrying the hold button and the busy counter, an Italian
  translation layer, and — above all — **one question at a time**, which he enforces with a hold in the
  mirror loop and a hook.
- **Nathan (fork)** runs a headless daemon on a Linux VPS with no screen, using only Claude and
  Telegram. He built runners that drive `claude -p` and stream-json turns from the daemon, a
  state pack for fresh sessions, drain-before-shutdown, usage-limit appointments, per-task models,
  real Telegram rate limiting, and a much quieter phone: no STATUS, one PULSE with six fields and the
  command bar under it, reactions instead of receipts, everything the supervisor writes rings, nothing
  the app writes does, no translator.

The fork is disciplined — sixty-odd `stage/*` branches, each kept mergeable on its own, and a 1500-line
record of every change with the reasoning (`docs/MODIFICHE-DEL-FORK.md` [F]) — but a plain `git merge`
no longer works: master moved too. A trial `git merge-tree` yields **19 conflicted files, 60 hunks**
(24 in `BridgeEngineModel.cs`), and worse, several files merge *clean* and then do not compile or
silently regress (§5.2).

**The owner's ruling (channel entry 8):** one codebase, one kernel; each brother keeps running it on
his own machine; things that are objectively better merge as the only way (the question hold stays);
everything that is taste becomes a per-user setting; and the settings need a UI that does not require
Windows, because Nathan has no screen.

## 2. Goals

1. **One repository, one kernel.** The fork's `ours/integration` is integrated into master such that
   every kernel improvement lands and every master feature since the base survives.
2. **Both ways of working are first-class.** Manu's terminal sessions on Windows with the WPF app;
   Nathan's daemon-driven turns on Linux. Neither is a fallback.
3. **Every taste divergence is a setting**, with two shipped presets — `classic` (Manu's way) and
   `quiet` (Nathan's way) — and the freedom to mix.
4. **One settings catalogue, three renderers.** The WPF Settings window, a `/settings` menu in
   Telegram, and a small web page served by the daemon and the WPF app. A setting is defined once, as
   data; the renderers read the definition.
5. **Default model and default effort are settings**, per role, with **Opus as the shipped default**
   (owner, entry 15). Effort stops being a compiled constant.
6. **Green on Windows and on Linux is the gate.** The fork's suite has never run on Windows in CI;
   the first run here gave ~31 real reds (§4.4). "Merged" means both suites green.

## 3. Non-goals (explicitly parked)

- **Server-hosted settings sync** between one person's machines. The catalogue is designed so a later
  `profile.source` can fetch the same JSON from a URL, keyed by bot token; nothing in this spec depends
  on it. Each kernel reads its own `config.json`.
- **A Mac app.** The web page replaces it.
- **One kernel shared by both brothers** (multi-tenant). Each has his own bot, Claude login, repos.
- **The `bg` runner**, named but unimplemented on the fork. Stays a named rung.
- **Splitting `BridgeEngineModel.cs`** (15k lines on the fork). Both sides decided not to; the
  fork's rule — a stage that touches a piece moves that piece out — continues.

## 4. Findings the design rests on

### 4.1 The fork's kernel is a superset by construction

`Running/SessionRunners.cs` [F] declares `Terminal | Print | Stream | Bg`. `ISessionRunner` is the
seam ("between 'a session exists' and 'how it runs'"); `TerminalRunnerModel` wraps master's
`ISessionSpawner` + `SpawnCommand_Builder` unchanged, `BridgeDrivenRunnerModel` starts no process and
lets the dispatcher drive turns. The runner is chosen **per role** from `config.json`
(`runners.<role>.runner`) in `OrchestrationLauncherModel.Resolve_Runner`, and
`RoleRunnerConfig_Factory.Create_Default` returns `Terminal` for every role — **an unconfigured fork
behaves exactly like master.** The WPF app is kept, rewritten onto the shared composition root
(`Composition/OrchestratorServices_Factory` [F]) and built on a Windows CI runner at every push.

### 4.2 Both hosts already share one startup

`AIOrchestrator/App.xaml.cs` [F] and `AIOrchestrator.Daemon/BridgeHost_Service.cs` [F] call the same
`KitAssets_Bootstrapper.Ensure_Installed(...)`: unwire legacy hooks from `settings.json`, move legacy
`~/.claude/commands` files aside (never delete), install only the statusline, verify the plugin
(`PluginVerdicts`: version + `gitCommitSha` + content digest) and record the verdict on an
`IPluginGate` that refuses **spawning**, never the bridge. Master's `KitAssets_Installer` copies
commands into `~/.claude/commands` at every start — the exact files the fork's installer moves aside,
because a local command shadows a plugin skill. **The two delivery paths cannot coexist; the fork's
wins** (§7.6).

### 4.3 Configuration today

Neither side has a profile, a preset, or a user. Master: a flat 12-field record, a `Save` that rebuilds
the object and **deletes unknown keys**, three UI toggles of which two never persist, and ~60
behavioural `const`s that need a rebuild. Fork: the same hot-reloading provider (`OrchestratorConfigProviderModel`,
byte-identical on both sides), plus four structured blocks (`runners`, `printRunner`, guardrails,
`telegram` prose, `defaults`, `planBackend`), a `Save` that **merges onto the raw JSON tree** through
`Atomic_FileWriter` and writes only the keys the UI owns, and one tested precedence resolver
(`HostOptions_Factory.Create_FromArguments`: CLI > env > default, environment injected as a function).
The full fork schema is in Appendix A.

### 4.4 Test health

| | master | fork (Linux, brother's numbers) | fork on this Windows machine |
|---|---|---|---|
| tests | 1827 green (last commit message) | 3307 total, 0 red, 9 skipped | 3323 total, **42 red**, 12 skipped, 8 min |

Of the 42 Windows reds, 11 were this session's own `AIORCH_*` variables leaking into the test host
(the interop fixture does not scrub the child environment). The honest number is **~31**: 13 statusline
parity fixtures need `jq`, which the machine lacks; 7 are a real PowerShell output-encoding gap in
`statusline.ps1` (the brother's own comment marked it "UNVERIFIED outside Windows"); ~5–8 are
Windows file-sharing races in the print-runner family that wander between tests; one is a
`FileSystemWatcher` semantic (Windows raises an Error event when the watched root is deleted, inotify
does not); 3 persist and are unexplained. The fork's own record admits the suite is "not reliable
under load" and has a plan ("G") for it.

### 4.5 What master has that the fork lacks

`--resume` on respawn for terminal supervisor/solo (`Spawning/ResumableSession_Resolver` [M]),
`/model` and `/effort` from the phone with per-orchestration overrides (`Telegram/ModelEffort*` [M],
`Apply_Dial`), owner `/pause` (`IOrchestrationSession.Paused`, `Status/PausedFlag_Marker` [M], the
`.paused` exit in `run-to-the-end-check.sh`), the question hold (`Bridge/QuestionHold_Policy` [M]),
the owner-answer credit fixes of 2026-09-10 (`Raise_OwnerWait`, `Is_TurnEndDeclaration`,
`_suppressedEntries` as a list, `Build_TurnEndedText` digest), solo coverage in the awaiting-answer
hook, the effort suffix on the statusline, the Fable 5.1 + xhigh defaults, pictures as pictures (the
fork built its own equivalent).

### 4.6 What the fork changed that is not taste

Additive and always on after the merge: Markdown rendered to Telegram HTML with a plain fallback;
long entries folded behind an expandable quote and attached as a document past a threshold; retried,
recorded topic deletion; persisted rate limiters and a Telegram error table; per-target 429 backoff
("one door"); inbound fenced by supergroup id; no false ✓ (receipt only on routed); undelivered
entries parked and delivered as one digest; per-repo topic colours; typed channel entries with a
grammar file read by both bash and .NET; the kit as a plugin; reviewer scratch folder; state pack;
closing turn; drain on shutdown; usage-limit appointments; per-task model on the member card; the
`ChannelChangeWaker`; tick hygiene; fake-clock tests; the `FakeClaude` contract harness.

## 5. The merge

### 5.1 Strategy: merge the fork, resolve toward its structure, re-port master's features by hand

Replaying the fork's 295 commits onto master would fight the same 24 engine hunks sixty times; the
stages were kept mergeable against a master that no longer exists. Replaying master's 20 commits onto
the fork's structure is ~6.6k lines of intent against a structure built to hold it (the runner seam,
the composition root). So:

1. `git merge fork/ours/integration` on an integration branch off master. Resolve the 19 conflicts
   **in favour of the fork's structure** (both histories preserved; nothing rewritten).
2. Re-port master's features (§5.3) as separate commits on the same branch, each with its tests.
3. Fix the silent breaks (§5.2).
4. Gate: both suites green (§9), both hosts start, the WPF app runs terminal sessions, the daemon runs
   print/stream sessions, on the same build.

### 5.2 The silent breaks a clean merge hides (all confirmed on the merged tree `71de997`)

- `Mirroring/MirrorText_Formatter.cs`: both sides added `Format_Parts` with identical parameters and
  different tuple names → CS0111 with no conflict marker. Keep one (the fork's, which also handles
  `QUESTION:`/`OPTION:`), fold master's `IMAGE:` case in.
- `Configuration/OrchestratorConfig_Factory.cs`: master's `claude-fable-5-1` default auto-merges to the
  fork's `opus`. Under §6 the shipped default IS Opus (owner), so this is right by accident — but the
  `classic` preset must carry Fable + xhigh, or Manu silently loses his model.
- Master's 32 new files land as pure additions and fail against the fork's engine:
  `Mirror_Append_Async` returns `bool` on the fork, `MirrorOutcomes` on master. Restore a tri-state
  outcome (§7.2).
- `AIOrchestrator.csproj` [F] no longer ships `kit/channel-append.sh` (moved to `kit/bin/`); master's
  csproj still does. Take the fork's item list.
- `Telegram/TelegramDeliveryModes.cs`: master's `PAUSED 💤` (owner pause) and the fork's
  `PAUSED_FOR_LIMIT ⏸` are different facts in the same slot. Merged record in §7.4.
- `Translation/*`, `Formatting/MonospaceBlocks_Formatter.cs`, `Planning/LedgerTranslation_Verifier.cs`
  vanish without a marker (fork deleted, master untouched). Master's `BridgeEngineModel` still
  references all three — resolved inside the 24-hunk conflict; do **not** reinstate the translator
  calls (§7.8).
- `kit/commands/*.md` → `kit/skills/*/SKILL.md` is a rename in git's eyes; master's edits to
  `solo.md`, `general-supervisor.md`, `supervisor.md` land as rename-modify. `solo` conflicts; the other
  two auto-merge and must be re-read for the ONE-QUESTION block and the RESUMED paragraph.

### 5.3 Re-port checklist (master feature → where it attaches on the fork)

| master feature | attaches to (fork) | notes |
|---|---|---|
| `ResumableSession_Resolver` + `--resume` for terminal supervisor/solo | `Running/SessionLaunch/ISessionLaunch` gains `ResumeSessionId` and `Effort`; `TerminalRunnerModel.Build_Command` passes them; `OrchestrationLauncherModel.Respawn_Supervisor/Respawn_Member` resolve the id **before** the stale-pid delete, only when the resolved runner is `Terminal` and `runners.<role>.resume == transcript` | keep the fork's `Validate_Model`; add master's `Validate_ResumeSessionId`; `ResumeModes` docstring widened from "print-run" to "any runner" |
| effort per role + per-orchestration override | `SpawnCommand_Builder.Build_ClaudeInvocation(resumeId, model, effort)`; `IOrchestrationSession.{Supervisor,Implementer}EffortOverride` + serializer + store; `Apply_Dial` | the role default moves from the constant `SUPERVISION_EFFORT_LEVEL` to the catalogue (§6.4); `Apply_Dial`'s kill+respawn is gated on `!Runner_Support.Is_BridgeDriven(kind)` — a print session has no pid file |
| `/model` `/effort` commands, `ModelChoices`, `EffortLevels`, `ModelEffortButton_Data`, role-picker | `BotCommandMenu.ALL` (+2 entries); text dispatch chain; `Try_HandleModelEffortTap_Async` placed before the generic `opt-` path in `Handle_CallbackTap_Async` | the fork's per-member `model` on the card is the middle rung of the same ladder: owner override > member card > role default |
| owner `/pause` (`Paused`, `PausedFlag_Marker`, `Wake_PausedTopic_IfNeeded`, `.paused` hook exit) | session model/serializer/store; `Status/PausedFlag_Marker`; every waker listed in CLAUDE.md's PAUSE decision; `run-to-the-end-check.sh` and `supervisor-ledger-check.sh` regain the `.paused` exit | the fork has no owner-pause state at all |
| `QuestionHold_Policy` in the mirror loop | §7.2 | pure predicate, merges unchanged; `AwaitingAnswerFlag_Marker` is byte-identical on both sides |
| owner-answer credit (decision 25) | `Raise_OwnerWait` (the fork inlines the add at two sites and has no method), `Is_TurnEndDeclaration` (absent on the fork — a `WAITING ON` line spends the credit there), `_suppressedEntries` list + `Build_TurnEndedText` drain | on the fork today everything pushes, so the defect is dormant; under `phone.push = filtered` it is live |
| awaiting-answer hook covers solo; `AIORCH_SUPERVISION_ROOT` path | merged hook = master's `case supervisor|solo` + the fork's root variable | |
| effort suffix on `statusline.ps1` | both statusline scripts | the fork's `statusline.sh` needs the same field |
| `ModelOnTheStatusLineTests` / model+effort PULSE field | a `modelEffort` field in `TopicStatusLine_Builder` (§7.3) | |
| pictures as pictures | the fork's `EntryAttachment_Policy` supersedes; keep the fork's, port master's tests that still apply | |

### 5.4 What is dropped, deliberately

- Master's translation layer (§7.8, owner to confirm).
- Master's `KitAssets_Installer` copy path and the four global `AgentHookSettings_Wirer.Ensure_Wired`
  calls (hooks now travel in skill frontmatter; the fork's bootstrapper unwires them).
- Master's `Decorate_TopicName` 9-positional-argument signature (replaced by the record, §7.4).
- The fork's `Would_BeASecondOpenQuestion` coaching text: under the hold its wording ("both are
  live, a typed reply binds neither") is false for the Remote case.
- The fork's hard-coded "Nathan" in `kit/skills/subagents/SKILL.md` (two sites) → `owner.name`.

## 6. The settings catalogue

### 6.1 Shape

One registry, `Configuration/SettingsCatalog/` (CoreLib, strict triples): a list of
`ISettingDefinition` records, each with:

| field | meaning |
|---|---|
| `Path` | the JSON path in `config.json`, e.g. `phone.receipts`, `models.supervisor`, `runners.implementer.runner` |
| `Kind` | `Bool`, `Enum(values)`, `Int(min,max)`, `String`, `StringList(validator)`, `Composite(parser)` |
| `Default` | the shipped value, the bottom of the precedence chain |
| `Scope` | `Machine` (config.json) or `Orchestration` (session.json — model/effort overrides, delivery mode, paused, presence) |
| `Category` | Phone · Pulse · Receipts · Models · Kernel · Kit · Owner — the menu structure of every renderer |
| `Label`, `Description` | one line each, English; the renderers show them |
| `RestartRequired` | `None` (hot), `NextSpawn`, `Host` — shown by the renderers, enforced by nobody |
| `Renderer` | a hint: `Toggle`, `Choice`, `Number`, `Text`, `OrderedList(source)`, `ReadOnly` |

Existing structured blocks (`runners`, `printRunner`, guardrails, `telegram` prose, `planBackend`,
`repos`) are registered as `Composite` entries that keep their current parsers; the catalogue gives
them a category, a label and a renderer hint so the three surfaces can list and edit them without a
second parser being written. New scalar keys are read by one generic `SettingsReader` that walks the
raw `JsonObject` (the fork's loader already keeps the tree for `Save`).

**Tests that the catalogue exists for:** every path is unique; every definition has a default of the
declared kind, a label and a description; both shipped presets parse and reference only catalogue
paths; a `Composite` entry names a parser that exists.

### 6.2 Precedence — one resolver, four layers

```
shipped default  <  preset (kit/presets/<name>.json)  <  config.json  <  session.json (orchestration-scope keys only)
```

- `SettingsResolver.Resolve(definition, presetTree, configTree, sessionOrNull)` follows the pattern of
  `HostOptions_Factory.Create_FromArguments` [F]: the layers are parameters, so precedence is tested
  without touching disk. One `[Fact]` per rung, one for "absent is absent, blank is absent", one for
  "an unknown preset name throws rather than defaulting" (the fork's rule for `planBackend.kind`).
- `config.json` gains one key, `preset` (`"classic"` | `"quiet"` | a file path). Absent means
  `classic`, because that is what master did before the merge.
- A key **absent** from `config.json` follows the preset. The renderers show the origin of every
  value ("from preset quiet" / "set here" / "shipped default") and offer **Reset**, which deletes the
  key. `Save` never materialises a preset value into `config.json` — the fork's "read, never written"
  rule, generalised.
- Hot reload is the provider's existing write-stamp check; `RestartRequired` is display only.

### 6.3 The presets

`kit/presets/classic.json` and `kit/presets/quiet.json` are partial `config.json` objects, shipped in
the app's output beside the kit, embedded as resources like the grammar, and tested to contain only
catalogue paths with valid values. They are **data**: the renderers never special-case a preset name.

### 6.4 The catalogue (v1)

Values are `classic` / `quiet`. "Only way" means not a setting: the merged behaviour.

**Models and effort** (owner: settings, Opus shipped)

| path | kind | shipped default | classic | quiet | seam |
|---|---|---|---|---|---|
| `models.{supervisor,implementer,reviewer,solo,general,communicator}` | String (validated by the fork's `Validate_Model`) | `opus`; `sonnet` for general and communicator | Fable 5.1 for supervisor/implementer/reviewer/solo | as shipped | `IOrchestratorConfig.Get_ModelForRole` [F] — the ladder reviewer→implementer→default stays |
| `effort.{same six}` | Enum(low, medium, high, xhigh) or null = no `--effort` flag | null | `xhigh` for supervisor and solo | null | `SpawnCommand_Builder.Build_ClaudeInvocation(..., effort)`; replaces `SUPERVISION_EFFORT_LEVEL` |
| per-orchestration overrides (`/model`, `/effort`) | Orchestration scope | — | — | — | `session.json`, `Apply_Dial` [M] — unchanged semantics |

**Kernel** (already settings on the fork; registered so the renderers can show them)

| path | kind | default | classic | quiet | seam |
|---|---|---|---|---|---|
| `runners.<role>.runner` | Enum(terminal, print, stream) | terminal | terminal | supervisor stream, members print, communicator terminal | `Resolve_Runner` [F] |
| `runners.<role>.resume` | Enum(transcript, fresh) | transcript; general fresh | as default | members fresh | extended to the terminal runner (§5.3) |
| `runners.sessionMemoryMax` | String (size) | 3G | — | — | Linux cgroup only |
| `printRunner.*` (`maxConcurrentTurns`, `…PerOrchestration`, `turnTimeoutMinutes`, `coalesceSeconds`, `streamSilenceSeconds`, `memberDigestMinutes`) | Int/Number | 10 / 3 / 30 / 3 / 120 / 5 | — | — | `RunnerConfigs_Json` [F]; `memberDigestMinutes` 0 = wake immediately |
| `telegramInbound` | Enum(poll, off) | poll | — | — | per host |
| `web.listen` | String (`host:port` or `off`) | `127.0.0.1:7391` | — | — | the HTTP listener of §8.3; restart: Host |
| `web.token` | String | "" (no header required) | — | — | §8.3 |

**Phone — what reaches the owner and how it sounds**

| path | kind | default | classic | quiet | seam |
|---|---|---|---|---|---|
| `phone.push` | Enum(filtered, everything) | filtered | filtered | everything | one decider at the owner-channel block of `Mirror_Append_Async` (§7.1) |
| `phone.status.periodic` | Bool | true | true | false | the non-away branch of `Push_AwayDigests_Async` [F] regains master's `Build_PeriodicStatusText` |
| `phone.status.intervalMinutes` | Int(5–120) | 30 | 30 | — | `PeriodicStatusSlot_Planner` |
| `phone.appMessagesRing` | Bool | true | true | false | `Resolve_EntrySound` [F] plus the three receipt/turn-end sites (§7.1) |
| `phone.receipts` | Enum(ticks, reactions) | ticks | ticks | reactions | `Send_ReceivedAck_Async` [F], `PendingOwnerReply.ReceiptWasReaction` (§7.5) |
| `phone.replyKeyboard` | Enum(off, on) | off | on | off | §7.6 — `on` accepts one permanent carrier line |
| `phone.foldLongEntriesAbove`, `phone.attachEntriesAbove` | Int | 900 / 3 | — | — | existing `telegram.*` keys, re-homed under `phone` with the old path read as an alias |
| `topic.onClose` | Enum(delete, close) | close | close | delete | the fork's `TopicDeletion` vs master's close path — both exist |
| `topic.modeGlyphs` | Enum(name, pulseHeader) | pulseHeader | name | pulseHeader | `Compose_TopicName` vs `Build_HeaderLine` (§7.4) |

**Pulse**

| path | kind | default | classic | quiet | seam |
|---|---|---|---|---|---|
| `pulse.fields` | OrderedList(waitingOnYou, supervisor, members, closedCount, lastEvent, merged, modelEffort, updated) | waitingOnYou, supervisor, members, closedCount, lastEvent, merged, updated | supervisor, members, modelEffort, merged, updated | as shipped | one private builder per field, called in list order (§7.3); master's lead line is `supervisor` + `merged` |
| `pulse.stepMinutes` | Int(1–60) | 5 | 5 | 5 | `UnchangedFor_Formatter.STEP_MINUTES` becomes a resolved value; the 429 evidence of 2026-09-10 is the reason the floor is not lower by default |
| `pulse.buttons` | OrderedList(validated against `BotCommandMenu.ALL`; host-gated verbs allowed but rendered "not on this host" off Windows) | the fork's six | screen, show, merge, test, pc, close, pause, progress | pending, left, tail sup, limits, merge, close | `TopicCommandButtons.TOPIC_BUTTONS` → built from the list; `Build_CommandButtonRows` |
| `pulse.holdToggle` | Bool | true | false | true | `Build_ForTopic(id, isHolding, heldCount)` [F]; `false` means the hold button rides the receipt as on master |
| `general.buttons` | OrderedList | the fork's five | (empty) | summary, pending, limits, resume, dnd_all | `Build_ForGeneral` |

**Owner and kit**

| path | kind | default | seam |
|---|---|---|---|
| `owner.name` | String | "" | exported as `AIORCH_OWNER_NAME` at both spawn points (§7.7); replaces "Nathan" in the subagents skill |
| `owner.language` | Enum(auto, it, en, …) | auto | exported as `AIORCH_OWNER_LANGUAGE`; `auto` = the fork's rule, "write to the owner in the language they used" |
| `repos[].code` | String | "" | the platform code (`SL`, `AI-Orch`, …); exported as `AIORCH_PLATFORM_CODES` one line; replaces the hard-coded table in the general-supervisor skill |
| `defaults.orchestrationMode`, guardrail keys, `planBackend` | existing | — | registered as-is |

**Only way (not settings), with the side that won**

| behaviour | winner | why |
|---|---|---|
| one question at a time (hold + hook + flag) | master | owner's ruling; conflicting answers when 3–4 arrive |
| question shape (question, 2–4 options, recommend, risk, row) refused before the write | fork | it is what makes one-at-a-time affordable; fix the `medium` risk mismatch between the grammar and `OwnerQuestion_Contract.Parse_Risk_OrNull` |
| answer binding (bind only when unambiguous), "Let's talk" single button, high-risk code, SUPERSEDED closure | fork | complementary to the hold (§7.2) |
| stall alert once per real unanswered question, never during a limit pause | fork | strictly more honest than "who spoke last" |
| Markdown → HTML, folding, chunking, undelivered digest, error table, rate limiters, one door | fork | plumbing |
| typed channel entries + grammar file, kit as plugin, hooks in skill frontmatter | fork | one protocol for both |
| resume own conversation for terminal supervisor/solo; fresh per turn for bridge-driven members | both | different runners, same config key |
| owner-answer credit raised at delivery, one-shot, turn-end digest | master | decision 25; needed by `phone.push = filtered` |

## 7. Seams — where each setting plugs in

### 7.1 Who rings (`phone.push`, `phone.appMessagesRing`)

The narrowest seam exists and is the same line on both sides: the block
`if (append.Channel.IsOwnerChannel && ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author))` inside
`Mirror_Append_Async`, where the fork still calls `OwnerPush_Policy.Should_Push` (now deciding only
"empty" and "restatement"). That call becomes an injected `IOwnerPushDecider.Decide(entry,
ownerIsWaiting, subject) → SendNow | HoldForDigest | Drop`:

- `everything` (quiet): `SendNow` for whatever the fork's `Should_Push` allows today. Nothing else
  changes.
- `filtered` (classic): master's `Should_Push` semantics — `SendNow` for a question, a `BLOCKED ON
  OWNER`, a picture, or THE answer while the credit is open; `HoldForDigest` otherwise, filed into the
  re-ported `_suppressedEntries` list and drained by `Build_TurnEndedText` at turn end (the fork's
  `lastWords` stub is the re-attachment point). The periodic STATUS returns through the non-away
  branch of `Push_AwayDigests_Async`.

Sound is separate: `Resolve_EntrySound` (one helper feeding all four mirror sites) plus the
turn-ended, receipt and status sites read `phone.appMessagesRing`. The remaining `TelegramSendSounds`
call sites (about thirty, all app alerts, bookkeeping and PULSE edits) are not touched. Master's `OwnerAnswerSurvivesFailedSendTests` and the fork's
`ThePhoneRingsOnlyForTheSupervisorTests` become parameterised over the mode.

**The fork's own defect that this makes live:** without `Is_TurnEndDeclaration`, a `WAITING ON …`
subject consumes the credit. It is re-ported with the credit (§5.3).

### 7.2 The hold (only way)

`QuestionHold_Policy.Should_Hold(isOwnerChannel, Is_AwaitingAnswer(orchId))` is consulted per entry
before marker extraction in the fork's `Mirror_Append_Async`; the method returns a tri-state
`MirrorOutcomes` again, and the loop skips `Settle_MirrorAttempt_Async` on `Held` so
`_tailer.Confirm_Append` never advances the cursor (master's `heldChannels` +
`_deliveredEntriesOfHeldAppend` bookkeeping). The fork's comment at `Find_ActiveChannels` stating
the opposite decision ("a pending question does NOT freeze this channel") is rewritten to cite the
owner's ruling.

It composes with the fork's pieces rather than fighting them. A held second question never reaches
`Send_QuestionWithButtons_Async`, so it never registers in `_openQuestions` and never triggers
`Supersede_OlderQuestions_Async`. `AnswerBinding_Decider` keys off the open count, which the hold keeps
at one — its happy path. The ambiguous branch stays reachable through three real routes (terminal
presence raises no flag; the 10-minute cap expires it; `/pc` lifts it), which is exactly what the
decider guards. SUPERSEDED stays reachable: the owner typing prose both clears the flag and stamps
`_ownerRepliedInWordsUtcByOrchId`, so "prose that binds nothing → hold releases → next question closes
the old one as superseded" is the ordinary path.

Two flags, two meanings, kept apart in the code and in this spec: the **file** `.awaiting-answer`
(one bit per orchestration, 10-minute TTL — the hook's and the hold's gate) and the **in-memory
set** `_ownerAwaitingAnswer` (the session owes the owner an answer — the push credit). The PULSE
"waiting on you" field reads neither; it reads the pending-decision store (`_openQuestions` in the
engine snapshot), which is the single source of truth for open questions.

### 7.3 Pulse (`pulse.fields`, `pulse.stepMinutes`)

`TopicStatusLine_Builder.Build` [F] calls one private static per field in a fixed order
(`Build_WaitingOnYouLine`, `Build_SupervisorRow_OrNull`, `Build_MemberLines`, `Build_ClosedCountLine`,
`Build_LastLine`, `Build_MergedLine`, `Build_UpdatedLine`, plus `Build_HeaderLine`). The setting is the
list of those names in order; `Build` iterates the list. Master's model/effort field
(`ModelReading_Formatter.Describe_OrNull` on the member row and the lead line) is re-ported as the
`modelEffort` field. `hasSubstance` keeps gating the whole message; `Strip_Heartbeat` keeps the repost
rule content-only. `STEP_MINUTES` is read from the resolved setting at build time; the member-row
duration and the heartbeat still share it, so they cannot drift apart.

### 7.4 Glyphs (`topic.modeGlyphs`) and the two pauses

`TopicNameFlags` [F] gains `IsPausedByOwner` beside `IsPausedForUsageLimit`:

```
TopicNameFlags(OwnerReply, IsPausedByOwner /*💤*/, IsPausedForUsageLimit /*⏸*/, IsClosed, IsAwaitingTest, IsDone)
```

Precedence in `Compose_TopicName`: reply prefix, then 🏁 → 💤 → ✅ → 🧪 → ⏸. `Strip_Glyph` learns 💤.
With `topic.modeGlyphs = name`, the four mode inputs the fork removed (mode, away, quiet, presence)
are restored to the record and drawn on the name as master did; with `pulseHeader` they render only in
`Build_HeaderLine`. The fork's reason for moving them (each rename writes a service message) is
recorded in the setting's description.

### 7.5 Receipts (`phone.receipts`, `pulse.holdToggle`)

`ticks`: master's path — `"✓"` with the hold button, edited to `"✓✓ …"` by delivery, by the busy
narration and by the stale-receipt updater. `reactions`: the fork's path — 👀 on the owner's bubble,
👌 on pickup, `"✓"` only as fallback, and the busy counter / handoff line on a **separate silent
self-editing narration message** (the fork's `Narrate_BusySupervisor_Async` branch on
`pending.ReceiptWasReaction`). The branch point already exists; the setting chooses it.
`pulse.holdToggle = true` puts ⏸/▶ on the PULSE bar (fork), `false` on the receipt (master) — never
both (CLAUDE.md decision 12's "one toggle in two places").

### 7.6 Reply keyboard (`phone.replyKeyboard`)

The fork measured, live on 2026-09-06, that deleting the carrier message deletes the bar; master
deletes it. So `on` cannot be master's code as-is: the carrier stays, one permanent "⌨️ shortcuts
ready" line in General. Re-port `ReplyKeyboard_Markup`, `Send_MessageWithReplyKeyboard_Async` (client
and interface), `Build_ReplyKeyboardRows`, and the launch call site, minus the delete. Row text must
be lexer-legal (`/verb`), so `tail sup` renders as `/tail sup`. Default `off` (recommended; owner to
confirm — §11).

### 7.7 Kit delivery and per-user values in the kit

- **Delivery:** the fork's `KitAssets_Bootstrapper` in both hosts. Master's `Ensure_KitAssetsInstalled`,
  the `kit/commands` and `kit/hooks` csproj items and the four global wirer calls are deleted.
  CLAUDE.md decision 17 is rewritten: the app **verifies** the plugin from its build output; the
  installer (`install.ps1` / `install.sh`) registers the checkout as the local marketplace.
- **jq on Windows:** the typed-entry gate in `kit/bin/channel-append.sh` refuses to write without
  `jq`. `install.ps1` installs it (`winget install jqlang.jq`) instead of warning; the bootstrapper
  checks `jq` beside the plugin and records a verdict that reaches General once ("typed entries will
  be refused on this machine until jq is on the PATH bash sees"); CI installs jq on the Windows runner.
- **Per-user values:** the launcher exports `AIORCH_OWNER_NAME`, `AIORCH_OWNER_LANGUAGE`,
  `AIORCH_PLATFORM_CODES` at **both** spawn points — `SpawnCommand_Builder.Build_SessionScript`
  (terminal) and `PrintTurnDispatcherModel`'s environment dictionary (headless) — following the
  `AIORCH_SOFT_BOUNDARY_CALLS` precedent (a settable value with a literal default in the script). The
  skills read them; the hard-coded name and table go.
- **Brevity ceilings** stay global in `kit/grammar/channel-grammar.json` (5 lines, 600 characters on
  both sides — no divergence to configure); the byte-equality test between the bash and .NET copies
  stays.

### 7.8 Language (recommendation: drop the translator)

The fork deleted the app-side translation layer after measuring 183 failed translations a day,
double processes per exchange, and correct Italian text flagged as "untranslated". Its replacement is
one sentence in the role skills: with the owner, write in the owner's language; everything on disk or
addressed to another agent stays English. Master persisted `/italian` two days ago, so this is a
real decision for the owner (§11). If confirmed: delete `Translation/*`, the 15 `Translate_ToItalian_Async`
guards and the one inbound `Translate_ToEnglish_Async`, `Set_ItalianLayer`, `Toggle_ItalianLayer_Async`,
the `/italian` branch, `Translate_LedgerText_Async`, `LedgerTranslation_Verifier` and its tests, the
config key and factory method, the two UI controls; rewrite `OwnerDeliverySurvivesAFailedFlushTests`
(its subject is flush survival; the translator was only its failure injector); edit the eleven "ENGLISH
always" sentences in master's surviving role prose to the fork's wording; fix the two stale "even when
the traffic is Italian" lines in the fork's implementer and reviewer skills. `owner.language` gives the
rule a machine-readable home for the first time.

## 8. The three renderers

All three read the catalogue, resolve through the same resolver, show the origin of each value, and
write through the fork's merging `Save`. None knows a preset by name.

### 8.1 WPF Settings window

Today: a hand-written `StackPanel` with named controls and a 12-argument factory call. It becomes a
tabbed window with one tab per catalogue category, an `ItemsControl` over the category's definitions
and a `DataTemplateSelector` keyed on `Renderer` (Toggle → CheckBox, Choice → ComboBox, Number →
numeric TextBox with the range, Text → TextBox, OrderedList → a two-list picker with up/down,
Composite → the existing hand-written panel for that block). Connection settings (token, chat id,
owner id) stay on their own tab. The status-bar checkboxes (DND, Silence) stay engine setters, as now.

### 8.2 Telegram `/settings`

One line in `BotCommandMenu.ALL`; one branch in the text dispatch chain; a `Try_HandleSettingsTap_Async`
inserted **before** the generic `opt-` path in `Handle_CallbackTap_Async`, exactly where the command
bar sits and for the same reason (permanent buttons must never be "expired"). One message that edits
itself (`Edit_MessageTextWithButtonRows_Async`): the categories first; a tap shows that category's
settings as buttons labelled with their current value and origin; a tap on a Toggle flips it, on a
Choice cycles or opens the values as buttons; `⬅ Back`, `↺ Reset`. Callback data `set:<id>:<value>`
with a short numeric id per definition (Telegram's 64-byte cap; `CallbackToken` asserts it). Numbers,
free text and ordered lists use a "reply with the value" step: the bot asks, the next owner message in
that topic is taken as the value and validated. Edits ride the fork's send budget, so a burst of taps
cannot start a 429 storm.

### 8.3 Web page

A second hosted service in the daemon (`Microsoft.Extensions.Hosting` is already there) and the same
class started by the WPF app: a `System.Net.HttpListener` on `web.listen` (default `127.0.0.1:7391`,
`off` disables), serving one embedded static page and two JSON endpoints — `GET /settings` (catalogue
+ resolved values + origins) and `PUT /settings` (a partial object, validated against the catalogue,
written through `Save`). Optional `web.token` required as a header when set. Localhost only by default;
on the VPS it is reached through an SSH tunnel, which is the deliberate limit — no public surface, no
auth system. The page renders the same category/definition structure as the other two, so a new
catalogue entry appears in all three renderers with no UI code.

## 9. Testing

- **Catalogue tests** (§6.1) and **resolver tests** (§6.2), all in-memory.
- **Round-trip tests per renderer:** WPF view-model → `Save` → reload shows the value and origin;
  `/settings` tap → `Save` (drive the real engine with the fake Telegram client as the fork's probe
  tests do); `PUT /settings` → `Save`; and the inverse: an unknown path or invalid value is refused
  with the catalogue's message and nothing is written.
- **Mode-parameterised behaviour tests:** the tests each seam reader listed (`OwnerPushPolicyTests`
  on both sides assert opposite things today; `TopicStatusLineBuilderTests`, `TopicCommandButtonsTests`
  + `HoldToggleTests`, `TelegramDeliveryModeGlyphsTests`, `TypingBubbleReplacesTheStatusMessagesTests`,
  `GeneralDashboardTests`, `ThePhoneRingsOnlyForTheSupervisorTests`, `ReplyKeyboardMarkupTests`,
  `ModelOnTheStatusLineTests`) run once per preset.
- **Preset probes:** one end-to-end probe per preset drives the real engine through an owner message,
  a supervisor answer, a question and a tap, and asserts the phone-visible sequence (what was sent,
  edited, reacted, and with which sound).
- **Two OSes in CI.** The fork's workflow builds on `windows-latest` and runs no tests. It gains
  `dotnet test` on `windows-latest` and `ubuntu-latest`, with `jq` installed on both, and the interop
  fixture scrubs `AIORCH_*` from the child environment.
- **Windows reds (§4.4)** are ledger lines of the merge, not follow-ups: the `statusline.ps1` output
  encoding (`[Console]::OutputEncoding`), the `FileSystemWatcher` error-event semantic in
  `ChannelChangeWaker`, the file-sharing races (the fork's "G light" — guard the 144 unguarded temp
  deletes, put the real-time probes in a serial collection), and the 3 unexplained persistent reds,
  each diagnosed before being called flaky. Done means five consecutive green runs on each OS.

## 10. Phases (each a separately reviewable branch; the plan follows this order)

0. **Prepare.** jq on this machine and in CI; the two-OS test workflow; the interop fixture scrub;
   the integration branch off master with the fork fetched.
1. **Merge and re-port.** §5.1–5.3. Gate: both suites green on both OSes, both hosts start, one
   terminal orchestration on Windows and one print orchestration through the daemon exchange an owner
   message end to end.
2. **Catalogue, resolver, presets.** §6. `preset` key, the two preset files, every existing key
   registered, `Save` unchanged. Gate: the app behaves exactly as after phase 1 with `classic`, and
   exactly as the fork with `quiet`, measured by the preset probes.
3. **Seams.** §7.1–7.6, one branch per seam, in this order: who rings; pulse fields and step; buttons
   and hold toggle; receipts; glyphs and the two pauses; periodic status; reply keyboard; topic
   close; model/effort defaults.
4. **Renderers.** §8 — Telegram first (it works for both brothers from day one), then the web page,
   then the WPF tabs.
5. **Kit and language.** §7.7–7.8, the CLAUDE.md alignment (decisions 8, 11, 17 rewritten; the fork's
   note that CLAUDE.md is Manu's is honoured by doing this on master, once), the general-supervisor
   platform-codes export, the `README` for a new machine on each OS.
6. **Windows test health.** The §9 last bullet, if not fully paid in phase 1.

## 11. Decisions for the owner (answered at spec review; the recommendation is the default)

1. **Drop the translator** and adopt "write in the owner's language" with `owner.language` (§7.8).
   Recommended: yes. Cost of keeping it instead: resurrect ~16 call sites into the fork's engine and
   reconcile with the fork's rule that app strings are English.
2. **Reply keyboard default `off`**, `on` meaning one permanent carrier line in General (§7.6).
3. **`preset` absent means `classic`** (master's behaviour); the installer asks on a fresh machine.
4. **Shipped model default is Opus for every role except general and communicator (`sonnet`)**, and
   `classic` carries Fable 5.1 + xhigh. Owner said Opus; the two cheap roles were `sonnet` on both
   sides and stay so unless told otherwise.
5. **`topic.onClose` default `close`** (master), `quiet` = `delete` (the fork's choice).
6. The fork's orphan branch `stage/4c-closing-turn` is Nathan's to merge or drop; the merge takes
   `ours/integration` as it is.

## 12. Risks

- **The 24-hunk engine conflict is resolved by hand once.** Mitigation: phase 1 is its own reviewed
  branch with the probes of §9 run before anything else lands on it; Nathan reviews it, since half of
  it is his.
- **A setting that exists in three renderers and one probe** is the shape most likely to drift.
  Mitigation: the catalogue test that every definition has a renderer hint and appears in each
  renderer's smoke test.
- **`quiet` must reproduce the fork's phone exactly**, or Nathan gets a third behaviour nobody
  chose. Mitigation: the preset probe is written from the fork's own tests before the seams change.
- **Two people editing `BridgeEngineModel.cs`** on one repo. Mitigation: the fork's rule stays —
  declare the piece you are about to touch, move it out when you touch it.

## Appendix A — the fork's `config.json` as read by `OrchestratorConfig_Loader` [F]

Top level: `repos[] {name, path, topicColor?}`, `supervisorModel`, `implementerModel`, `reviewerModel`
(ladder → implementer → default), `soloModel` (same ladder), `generalSupervisorModel`,
`communicatorModel`, `telegramSupergroupChatId`, `telegramOwnerUserId`, `telegramStatusScreenshots`,
`voiceTranscribeCommand`, `orchestrationTokenBudget`, `telegramInbound` (poll|off), `highRiskPatterns[]`
(empty ≠ absent), `highRiskCodeExpiryMinutes` (10), `dispatchPauseThresholdPercent` (95),
`buttonExpiryMinutes` (720), `defaults.orchestrationMode` (basic|full), `telegram.foldLongEntriesAbove`
(900), `telegram.attachEntriesAbove` (3), `planBackend {kind, assembly, type}`,
`runners.<role> {runner, resume, permission_mode, settings}` + `runners.sessionMemoryMax` (3G),
`printRunner {maxConcurrentTurns 10, maxConcurrentTurnsPerOrchestration 3, turnTimeoutMinutes 30,
coalesceSeconds 3, streamSilenceSeconds 120, memberDigestMinutes 5}`. Secrets in `secrets.json`:
`telegramBotToken`. Written by `Save`: repos, the four UI models, the two chat ids, screenshots, voice
command, token budget, `runners` + `printRunner` (wholesale, effective values). Read and never
written: everything else.

## Appendix B — the trial merge, for the record

`git merge-tree --write-tree master fork/ours/integration` → tree `71de997`, 19 conflicted paths, 60
hunks: `BridgeEngineModel.cs` 24; `OrchestrationLauncherModel.cs` 7; `OrchestrationSession_Factory.cs`
4; `OwnerPush_Policy.cs`, `TelegramDeliveryModes.cs`, `TopicStatusLine_Builder.cs`,
`TopicCommandButtonsTests.cs` 3 each; `TopicStatusLine_Planner.cs` 2; twelve files with one hunk
including `SpawnCommand_Builder.cs`, `TelegramApiClientModel.cs`, `TopicCommandButtons.cs`,
`SessionJson_Serializer.cs`, `kit/skills/solo/SKILL.md`. Master's 20 commits: 7 merges; `--resume`
(contradicted by the runner registry — re-homed), owner-reply delivery (re-ported), decision 24 docs,
`/model` `/effort` (absent on the fork — re-ported), per-orchestration effort (re-ported), role-picker
buttons (re-ported), catalogues + collapsing command bar (superseded by `pulse.buttons`), effort on the
statusline (re-ported), one question at a time (kept, the fork's shape contract kept beside it), pause
(re-ported), Fable 5.1 + xhigh (moved into `classic`), pictures as pictures (the fork's equivalent
kept).

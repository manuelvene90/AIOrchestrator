# Fork Merge — Plan 04: The three settings renderers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the 66 settings plan 02 registered EDITABLE, from three places, without any of the three
growing its own idea of what a key is called. Plan 02 built the catalogue, the four-layer resolver and
the two presets; plan 03 makes the engine OBEY them. This plan is the other half of plan 02's promise:
`SettingsCatalog.cs`'s own class doc already says *"the three user interfaces this plan and the next
ones grow (the WPF window, the web page, the Telegram `/settings` menu) all read THIS — none of them
carries its own idea of what a key is called, what it defaults to, or where its value may live."* Plan
04 is the task series that has to make that sentence true, and the way it stays true is that the three
renderers are DRAWING LAYERS ONLY: one shared reading layer and one shared writing layer live in
`AIOrchestratorCoreLib`, and a renderer that computes anything about a setting for itself has already
failed the design.

**Architecture:** Two CoreLib components first, and everything else hangs off them.
`Configuration/SettingsPresentation/` turns the catalogue plus the resolved layers into a list of
READINGS — value, origin, the one display string, the one origin label, the one restart label — so all
three renderers show the same words. `Configuration/SettingsWriting/Settings_Writer` is the only thing
that writes a catalogue path: it validates through **the definition's own `Validate_OrNull`** (never its
own check — CLAUDE.md decision 21: a renderer that writes a setting must not be the thing that validates
it), merges into the raw `config.json` tree with `SettingsJson_Path`, and writes it atomically; Reset
DELETES the key. Then three thin renderers: the Telegram `/settings` menu (a pure paged menu builder
plus one engine wiring task), the web page (a pure request handler plus one `HttpListener` host started
by both hosts through the existing `OrchestratorServices_Factory`), and the WPF window (tabs per
category, a `DataTemplateSelector` keyed on `SettingRenderers`, rows built by the same CoreLib builder
the other two use). The gate is the one test spec §12 asks for: every definition reaches every renderer.

**Tech Stack:** .NET 10 (`net10.0`, WPF app `net10.0-windows`), xUnit, `System.Text.Json.Nodes`,
`System.Net.HttpListener`, git, bash (msys on Windows), `jq`.

**Spec:** `docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md` — **§8 in full**
(§8.1 WPF, §8.2 Telegram, §8.3 web), the origin/Reset rules of §6.2, the renderer-hint row of §6.1, the
round-trip and drift bullets of §9, and phase 4 of §10. **The spec is NOT in this worktree** — it lives
on branch `feat/fork-merge-and-profiles-spec`, checked out at
`C:\Users\Gianpiero\source\repos\AIOrchestrator-spec`. That is the copy this plan was written against.

**Worktree:** create a fresh one off master once plan 02 has merged —
`git worktree add ../AIOrchestrator-plan04 -b plan/04-settings-renderers master`. **This plan was
AUTHORED in `C:\Users\Gianpiero\source\repos\AIOrchestrator-plan02` (branch `plan/02-settings-catalogue`)
while other agents held that worktree**; the author wrote this one file and nothing else there, ran no
git, no build and no test. Never commit to `master`; the owner merges.

**Which copy every statement here was read from (CLAUDE.md decision 18):**

| document | copy read |
|---|---|
| `CLAUDE.md`, `.claude/rules/code-conventions.md`, `.claude/rules/git-and-boundaries.md` | branch source, worktree `AIOrchestrator-plan02`, branch `plan/02-settings-catalogue` |
| the design spec (§6, §8, §9, §10, §11, §12, Appendix A) | branch source, worktree `AIOrchestrator-spec`, branch `feat/fork-merge-and-profiles-spec` |
| plan 02 and plan 03 | branch source, worktree `AIOrchestrator-plan02` |
| `SettingsCatalog/` (all 13 files), `OrchestratorConfig_Loader.cs`, `OrchestratorConfig/`, `Storage/`, `Composition/`, `Telegram/` (`BotCommandMenu`, `TopicCommandButtons`, `CallbackToken`, `ModelEffortButton_Data`, `HoldButton_Data`, `ITelegramApiClient`, `TelegramSendBudget/`, `TokenBucket_Gate`), `Bridge/BridgeEngine/BridgeEngineModel.cs`, `AIOrchestrator/SettingsWindow.*`, `AIOrchestrator.Daemon/`, all three `.csproj`, `kit/presets/*.json` | branch source, worktree `AIOrchestrator-plan02`, read 2026-09-12 **while plan 02 Task 8 was still open in that worktree** — `PresetProbeTests.cs` and the plan-02 report did not exist yet. Re-read `SettingsCatalog.cs`, `Settings_Resolver.cs` and `OrchestratorConfig_Loader.cs` before Task 1. |
| build output, installed `~/.claude`, the running app (`Get-Process AIOrchestrator \| Select Path`) | **NOT READ.** One task needs the running app: Task 9's manual WPF smoke, which must be run from the freshly built binary and must say so (decision 23 — the running app is a fourth copy). Nothing else here is a claim about them. |

---

## Depends on plan 02 and plan 03

- **Plan 02 must be MERGED IN FULL, Task 8 included.** Tasks 1 and 2 consume `SettingsCatalog.ALL` /
  `Find_OrNull` / `In_Category`, `ISettingDefinition.Validate_OrNull`, `Settings_Resolver.Resolve`,
  `SettingOrigins`, `SettingsJson_Path.Write` / `.Remove`, `Presets_Loader.Resolve_ForConfig`. Task 8
  also turns the last four plan-01 reds green; **this plan inherits NO named reds.** If any of the four
  `OrchestratorConfigFactoryTests` / `PerRoleModelDefaultsTests` cases is still red when Task 1 starts,
  STOP and say so — plan 04 must not be the plan that normalises them.
- **Plan 03 is NOT a hard dependency for Tasks 1, 2, 3, 6, 7, 8, 9** — none of them touches
  `BridgeEngineModel.cs`, `OwnerPush_Policy`, `TopicStatusLine_Builder`, `TopicCommandButtons` or
  `TelegramDeliveryModes`. Those seven tasks can run in a separate worktree while plan 03 is in flight,
  and that is the recommended shape, because plan 03 carries twelve open decisions of its own and four of
  them are the owner's.
- **Tasks 4 and 5 (Telegram) DO collide with plan 03.** Task 5 edits `BridgeEngineModel.cs` (the text
  dispatch chain at ~7068-7216 and `Handle_CallbackTap_Async` at ~11208) and `BotCommandMenu.ALL`; plan
  03's Tasks 2, 3, 5, 6, 8 and 10 all edit the same engine file. Per CLAUDE.md decision 16 parallel
  WRITERS are allowed only on disjoint file sets, so **Task 5 must be serialised against every
  engine-touching task of plan 03** — one engine-touching task at a time, whichever plan it belongs to.
- **Two concrete hand-offs from plan 03 to here, both named in plan 03's own text:**
  1. `SettingValidators.LISTEN_ADDRESS` — plan 03's PARKED section says *"stays a registered name with
     no check (plan 04's, and Task 1 keeps it visible in the switch)"*. That is this plan's Task 3.
  2. `SettingValidators.PULSE_FIELDS` and `BOT_COMMANDS` are plan 03 Task 1's, **not this plan's.** If
     plan 04 lands first they are still inert, which means the OrderedList renderer can write a word
     `pulse.fields` will later refuse. Task 1 handles that honestly: the OrderedList control offers the
     known words as a PICKER rather than free text, so a typo is not reachable through any of the three
     renderers even while the validator says nothing. A hand-edit still is, and that is plan 03's to
     close, not this one's to work around twice. **See D15.**
- **One interaction to notice, not to fix:** adding `/settings` to `BotCommandMenu.ALL` makes `settings`
  a legal `pulse.buttons` verb once plan 03 Task 5 implements the `BOT_COMMANDS` validator. Neither
  shipped preset names it and nothing here puts it on a bar. Say so in the Task 5 report; do not add it.

---

## Non-goals — say no to these out loud

Every one of these belongs to a different plan or to nobody. A task that finds itself editing the files
below has left its scope.

- **New catalogue entries, or changed defaults, labels, descriptions or scopes.** The registry is plan
  02's and its content is settled. This plan READS `SettingsCatalog.cs` and does not open it. The one
  exception is `SettingValidators.cs` in Task 3, which implements a name plan 02 registered and plan 03
  handed here by name.
- **Every behavioural seam (plan 03).** No `INERT_NOTE` is deleted by this plan. A setting being
  editable and a setting being obeyed are two different facts, and plan 04 delivers only the first. A
  renderer that shows `phone.receipts` on a build where plan 03 has not landed is showing a value
  nothing reads, and that is correct and intended — exactly as plan 02 registered keys nothing read.
- **The kit's per-user values and the language prose (plan 05).** The `Kit` category ships EMPTY and the
  three renderers must therefore render an empty category without crashing — that is a test in Task 10,
  not a reason to fill the category.
- **The translation layer.** Settled and gone (owner, 2026-09-12). The renderers' strings are English,
  and `owner.language` is a row like any other.
- **Writing `session.json` from a renderer.** `Apply_Dial` is the ONE apply path for the per-orchestration
  model and effort dials (CLAUDE.md decision 24: store the override → kill → respawn → owner-facing app
  entry). No renderer gets a second one. See D3.
- **An authentication system, a public web surface, TLS, or a reverse proxy.** §8.3 is explicit: loopback
  by default, reached from elsewhere through an SSH tunnel, *"which is the deliberate limit — no public
  surface, no auth system."*
- **Splitting `BridgeEngineModel.cs`.** The rule stands (`.claude/rules/code-conventions.md`): when a
  stage touches a piece of that file, that piece MOVES OUT. Task 5 says which piece it moves (the
  settings-menu state and its handler) and moves it; it is not a refactor task of its own.
- **Fixing anything this plan finds wrong that nobody asked about.** CLAUDE.md decision 22: one line in
  `## PARKED` at the end of this document, outside the denominator.

---

## Global Constraints

- **`jq` lives only on a login shell's PATH.** Every task that runs `dotnet` must first run, in the same
  bash invocation:
  `export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"`
  Without it the 13 statusline parity fixtures fail and you will diagnose a renderer change from their noise.
- **`AIOrchestratorCoreLib` is strict** (`.claude/rules/code-conventions.md`): the triple `IXxx` +
  `internal sealed class XxxModel : IXxx` + `static Xxx_Factory`; `Xxx_Yyy.cs` with the underscore only
  when the second word is a role (`_Parser`, `_Builder`, `_Factory`, `_Reader`, `_Writer`, `_Formatter`,
  `_Resolver`, `_Decider`, `_Policy`, `_Composer`, `_Table`); methods `Verb_Object[_Modifier]`; anything
  that may not resolve ends `_OrNull`; get-only properties from a primary constructor; **no `record`
  types**; ad-hoc multi-value returns are value tuples; XML docs argue the WHY with dated incidents.
  Tests: xUnit `[Fact]`, folders mirror production namespaces 1:1, class `<Subject>Tests` with the
  underscore stripped, methods `Verb_Scenario_Outcome`, **stubs not mocks**.
  **One suffix is added by this plan and it is named here rather than smuggled in:** `_Handler`, for
  `SettingsRequest_Handler` (Task 6) — "handler" is a role in the same sense `_Composer` already is, and
  the alternative (`SettingsApi` with no suffix) would read as a data type. No other new suffix.
- **THE WPF PROJECT IS UI-RELAXED AND UNTESTABLE BY THIS SUITE.** `AIOrchestratorCoreLib.Tests` targets
  `net10.0`; `AIOrchestrator` targets `net10.0-windows` with `UseWPF`. The test project **cannot
  reference the app project** and there is no STA/UI harness anywhere in the repo. Therefore: **every
  decision a renderer makes lives in CoreLib.** The XAML and its code-behind may contain layout, binding
  and event plumbing and NOTHING ELSE — no formatting, no validation, no origin logic, no "which control
  for which kind". If a WPF file grows a `switch`, it is in the wrong project. See "Honest testability"
  below.
- **NEVER run the full suite except at Task 10.** One suite at a time on this machine. A red under load
  is isolated with `--filter` and re-run alone before it is believed; compare the SET OF NAMES of failing
  tests against the known set, never the count.
- **Decision 21 — the catalogue validates, the renderer does not.** Every refusal message a renderer
  shows comes from `ISettingDefinition.Validate_OrNull`. A renderer may CONSTRAIN what it offers (a
  ComboBox over `EnumValues`, a picker over `PulseField_Names.ALL`) — that is drawing, not validating —
  but it may never decide that a value is acceptable, and it may never invent a message. The one place a
  value becomes acceptable is `Settings_Writer`, and the one thing it asks is the definition.
- **Decision 12 — never a second copy of a formatter.** Three renderers showing "from preset classic" is
  exactly the shape that drifts. There is ONE `SettingValue_Formatter`, ONE `SettingOrigin_Labels`, ONE
  `RestartKind_Labels`, and a test in Task 10 that walks all three renderers asserting they print the
  same strings for the same reading.
- **Decision 15 — an alert the owner cannot act on does not go to Telegram.** Every "could not honour
  this" line in this plan goes to `orchestrator.log.jsonl` via `_log.Log_Warning`. The exception is a
  refusal the owner CAUSED by tapping or typing: that is answered where they typed it, because a silent
  refusal is decision 21's other failure.
- **Decision 22 governs scope.** Every task below traces to a line of spec §8, which is an owner-approved
  design. Anything else is one line in `## PARKED`.
- **Windows:** `python3` is native Windows Python and cannot open msys paths — hand it Windows paths.
  Bash heredocs over ~6 KB die as a fake quote error; write the script with the Write tool and run the
  file. Quote `git show "ref:path"` whole.
- **Say which copy you read** in every report: branch source, build output, installed (`~/.claude`), or
  the running app's folder. Task 9 is the only one that touches the running app, and only to say whether
  it was rebuilt.
- **Stage by explicit path**; never `git add -A` / `.` / `commit -a`. Multi-line messages via
  `git commit -F <tempfile>`. One commit per task (or per defect inside a task). Every commit ends with:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## Honest testability — which renderer the suite can actually hold

Stated up front because it decides the shape of every task and the shape of the gate.

| renderer | headless? | how | what is NOT covered |
|---|---|---|---|
| **Shared reading + writing layers** (Tasks 1-3) | **Fully.** | Pure CoreLib over `JsonObject` trees and a temp `ISupervisionPaths`, exactly like `SettingsResolverTests` and `PerRoleModelDefaultsTests`. Runs on both OSes. | Nothing. This is where the whole design is pinned. |
| **Telegram `/settings`** (Tasks 4-5) | **Fully.** | Task 4 is a pure builder (text + button rows) — plain `[Fact]`s. Task 5 drives the REAL engine with `FailableTelegram_Fake` + `RecordingLog_Fake`, the harness 28 test files already use (`Tests/Bridge/OwnerAnswerSurvivesFailedSendTests.cs` is canonical). Both OSes. | What a phone actually renders. Emoji width, button wrap, and whether a 64-byte payload is *really* accepted — the byte-cap assert is ours, the acceptance is Telegram's. |
| **Web JSON layer** (Task 6) | **Fully.** | `SettingsRequest_Handler` takes a method, a path, a header bag and a body STRING and returns a status and a body string. No socket, no `HttpListener`, no port. Both OSes. | Nothing about HTTP itself. |
| **Web host** (Task 7) | **Partly, and it must SKIP loudly when it cannot.** | One smoke test binds a loopback port, does a real GET and a real PUT, and stops. `HttpListener` cannot bind port 0, so the test probes a free port and, if it cannot bind, **skips with a message naming why** — decision 20: a harness that cannot find what it tests must refuse to run rather than certify the absence of the thing it never ran. Linux CI: `HttpListener` on `127.0.0.1` needs no privileges on either OS; on Windows a non-loopback prefix would need a URL ACL, which is why the default is loopback and the test never leaves it. | A tunnel, a second machine, a browser. |
| **The HTML page** (Task 8) | **Barely, and the plan says so.** | The only assertions available without a browser: the embedded resource exists and is non-empty, it references `GET /settings` and `PUT /settings`, and — the one that matters — **it contains no catalogue path and no setting label**, proving it renders from the API rather than from a second copy of the catalogue. | Everything a browser does. This is verified once by hand and the checklist goes in the report. |
| **WPF window** (Task 9) | **NO. Not at all, by construction.** | The test project is `net10.0` and cannot reference a `net10.0-windows` project; there is no UI harness; CI's Linux leg cannot even build the app project (`ci(workflow): the Linux leg builds what Linux can build`). | The entire drawing layer. |

**What that means for the gate (Task 10).** The §12 mitigation — *"the catalogue test that every
definition has a renderer hint and appears in each renderer's smoke test"* — is only achievable because
the WPF renderer's DECISIONS live in CoreLib. `SettingsRow_Builder` (Task 1) is what the WPF window
binds to, and it is `net10.0`, so the gate test can assert "every definition produces a WPF row" without
WPF. **The gate covers the WPF renderer's content and not its appearance**, and the report must say that
in those words. The appearance is covered by exactly two things and no more: a
`dotnet build AIOrchestrator/AIOrchestrator.csproj -c Debug -warnaserror:CS` that is clean, and a
written manual smoke checklist executed once on this box against the freshly built binary — with the
report stating whether the owner's RUNNING app was rebuilt or is still the fourth copy (decision 23).

---

## OPEN DECISIONS — the owner or the coordinator answers these BEFORE the named task starts

The spec left each of these open, or the tree contradicts it. **Do not guess.** Each row names the task
it blocks; a task whose decision is unanswered stops and asks rather than picking the recommendation
silently. The recommendation is what this plan's author would do and why; it is not an answer.

### The owner's — five

**D1 — `Save` materialises the four UI model keys into `config.json` on every press. Does that stop?
Blocks Task 2 and Task 9.**
Plan 02's own self-review item 6 hands this here by name: *"`Save` already writes `supervisorModel` and
`implementerModel` because they have Settings fields, so after any save a `classic` machine's Fable is in
the file as the owner's own… Flagged for plan 04, which gives every catalogue key a renderer and will
have to decide whether the four UI models keep their special status."* Read
`OrchestratorConfig_Loader.Save` (lines 248-259): it unconditionally assigns `supervisorModel`,
`implementerModel`, `generalSupervisorModel` and `communicatorModel` from the RESOLVED config object — so
a preset-origin or shipped-default value becomes a stated value the first time the owner presses Save,
and the origin the renderers display becomes "set here" for something nobody set. This is not theoretical:
CLAUDE.md's model-ladder bullet records the owner's live `config.json` pinning a stale model and defeating
the Opus default they had asked for (found 2026-09-12, entry 216).
*Decision:* do the six model keys join every other catalogue key under `Settings_Writer` — written only
when the owner actually edits that row — and does `Save` stop writing the four? **Recommended: yes.**
`Save` keeps `repos`, the two chat ids, the token, `telegramStatusScreenshots`, `voiceTranscribeCommand`,
`orchestrationTokenBudget` and the `runners`/`printRunner` block, and drops the four model lines; the
models are then read-and-never-written exactly like `reviewerModel` and `soloModel` already are, which is
the rule `Save`'s own comment argues for at length two paragraphs further down. Cost of NOT doing it: the
two remaining renderers write models through one path and the WPF window through another, and pressing
Save in the window still freezes a default that is meant to move. **The owner's, because it changes what
pressing Save does to their file.**

**D2 — does the phone see the `Kernel` category, and what is fenced? Blocks Task 4.**
37 of the 66 settings are `Kernel`, most of them `Restart = Host`, and three of them can lock the owner
out of the surface they are typing on: `telegramInbound: off` stops this host long-polling `getUpdates`
(no more inbound from the phone at all, recoverable only at the desktop or by hand-editing
`config.json`); `telegramSupergroupChatId` and `telegramOwnerUserId` decide which chat and which human
the bridge answers. `web.token` and `web.listen` are the web page's own keys.
*Decision:* **(a)** the phone shows every category, with a two-tap confirm on those five; **(b)** the
phone shows every category but REFUSES those five with a line saying "change this at the desktop or in
config.json, it is how this menu reaches you"; **(c)** the phone shows `Phone`, `Pulse`, `Receipts`,
`Models`, `Owner` and a read-only `Kernel`.
**Recommended: (b) for `telegramInbound`, `telegramSupergroupChatId` and `telegramOwnerUserId`, and (a)
for everything else in Kernel** — a foot-gun whose recovery path is not the phone is not a confirm
dialog's problem, it is a thing the phone should not do; the rest is the owner's machine to configure
from their machine's remote control, which is what this whole system is. **The owner's, because it is
about their own foot-guns and their own convenience.**

**D3 — where does `/settings` live: General, an orchestration topic, or both? Blocks Task 4 and Task 5.**
§8.2 does not say. General is the concierge's topic and has no session of its own; an orchestration topic
is the supervisor's line to the owner, already carrying PULSE, the command bar, the receipt and the
question hold (`QuestionHold_Policy` parks an owner channel whose orchestration is awaiting an answer — a
paged menu competing for that topic's attention is a real interaction, not a cosmetic one).
*Decision.* **Recommended: machine settings in GENERAL only. `/settings` typed in an orchestration topic
answers with that orchestration's own three `session.*` state rows plus its two dials, read-only, and
points at `/model` and `/effort`** — which is exactly what `Scope = Orchestration` means and exactly what
decision 24 says owns those two. That keeps the paged menu out of a topic where a question may be held,
and it still answers a reasonable question asked in a reasonable place. **The owner's, because it is
where their thumb goes.**

**D4 — the web page's security posture. Blocks Task 7.**
§8.3 ships `web.listen` = `127.0.0.1:7391` and `web.token` = `""`. Read literally that means: on every
machine where this lands, the app opens a loopback HTTP port that will rewrite `config.json` with no
secret at all. On a single-user desktop that is defensible; it is still a new door that did not exist.
*Decision, two halves:* (i) does `web.listen` stay ON by default or does the shipped default become
`off`, with the installer turning it on? (ii) does `PUT` require a non-empty `web.token`?
**Recommended: keep `127.0.0.1:7391` (do not change a plan-02 default — that is out of scope here
anyway), and REFUSE every `PUT` with `401` while `web.token` is empty, answering a body that says so, and
say it on the page.** `GET` stays open on loopback, so the page is useful for READING immediately and
becomes useful for writing the moment the owner sets a token. Cost: one more step on a fresh machine.
**The owner's, because it is their security posture and their convenience being traded.**

**D5 — does the WPF window become immediate-apply, and does "Cancel" disappear? Blocks Task 9.**
Today `SettingsWindow` is a batch form: edit fields, press Save, everything is written at once; Cancel
discards. A catalogue-generic window over 66 rows across seven tabs cannot sensibly hold 66 pending
edits, and the other two renderers are inherently immediate (a Telegram tap writes; a `PUT` writes).
*Decision:* immediate-apply per row (each control writes on commit, the origin label updates in place, a
per-row Reset deletes the key), with Save/Cancel removed — or keep the batch form for the catalogue tabs.
**Recommended: immediate-apply, Cancel removed, and the Connection tab keeps its own explicit Save**
(the token is not a catalogue key and lives in `secrets.json`; typing half a token and having it written
per keystroke is not wanted). Three renderers behaving the same way is the whole point; a window whose
Cancel undoes what the phone already saw is a lie. **The owner's, because a button they use disappears.**

### The coordinator's — ten

**D6 — the write path is a NEW generic writer, not `OrchestratorConfig_Loader.Save`. Blocks Task 2.**
§8 says all three renderers *"write through the fork's merging `Save`"*. That is not possible as written:
`Save(IOrchestratorConfig, ISupervisionPaths)` takes a fully-built typed config object and assigns a
fixed list of ~10 keys plus `RunnerConfigs_Json.Write`. There is no expression of it that writes
`phone.receipts`, `pulse.fields` or `runners.implementer.resume` alone. What §8 actually needs from
`Save` is its MERGE PROPERTY — `Read_JsonObject_ForEditing` re-reads the file and only assigns known
keys, so unknown and hand-edited keys survive — and that property is `SettingsJson_Path.Write` over a
freshly read tree, which is what plan 02 built it for.
*Decision.* **Recommended: `Settings_Writer` is new, owns every catalogue path, and reuses
`Read_JsonObject_ForEditing`'s merge semantics and `Atomic_FileWriter`. `Save` keeps its existing job for
the token, ids and repos.** Recorded here because it is a visible deviation from the spec's own sentence.

**D7 — the per-message 30-second edit gap makes a self-editing paged menu impossible as specified.
Blocks Task 4 and Task 5.**
§8.2 asks for *"one message that edits itself"*. `TelegramApiClientModel.Edit_MessageTextWithButtonRows_Async`
calls `Hold_UnlessThisMessageMayBeEdited(messageId)` first, which consults
`ITelegramSendBudget.Reserve_MessageEdit` and its
`TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE = 30 s` — a hard floor between two edits of
the SAME message id, measured from a production 429 storm on 2026-09-10 — and **throws
`TelegramHeldException` rather than sleeping**. A paged menu takes several taps in a few seconds: category
→ page 2 → a setting → a value → back. On this tree, taps two through five would each throw.
*Decision, three ways out:* **(a)** exempt one message id — the live settings menu — from the per-message
gap, leaving the 60/min `Control` bucket in charge; **(b)** delete and re-send a new message per page
(two calls per tap, and each new message spends the `Message` bucket, whose capacity is 10 with a 20/min
refill — a dozen taps drains it); **(c)** abandon self-editing: one message per navigation step, no
deletes, and a chat full of dead menus.
**Recommended: (a).** The gap exists because an APP-DRIVEN loop was editing every open topic's status
line once a minute; a settings menu is one message, edited only when a human taps, bounded by human
speed, and still governed by the shared control bucket. The exemption must be narrow (one message id,
registered while the menu is live, dropped when it closes), argued in `TelegramSendBudgetModel`'s own doc
with the 2026-09-10 incident named and the distinction stated, and pinned by a test that a NON-exempt
message still gets the 30 seconds. **If the coordinator will not grant the exemption, the menu design
changes materially and that becomes the owner's to see.**

**D8 — the stateless setting id. Blocks Task 4.**
§8.2 asks for *"callback data `set:<id>:<value>` with a short numeric id per definition (Telegram's
64-byte cap; `CallbackToken` asserts it)"*. Two things to settle. First, `CallbackToken` is scoped to the
`opt-` question-option family and has its own prefix and parser; `ModelEffortButton_Data` and
`HoldButton_Data` each carry their OWN prefix and their own byte assert, which is the tree's actual
pattern — so `/settings` gets its own `SettingsButton_Data` with its own prefix and its own assert, not a
reuse of `CallbackToken`. Second, what the numeric id IS.
*Decision.* **Recommended: the index into `SettingsCatalog.ALL`, plus a stale-id answer.** An index is
one to three characters and needs no registry, so the payload survives a restart exactly as
`ModelEffortButton_Data` does. Its one hazard is that a catalogue edit renumbers, so a menu message left
open across an app upgrade could target the wrong row — closed by answering, for any id whose definition
does not match the category the payload also carries, *"this menu is from an older version — send
/settings again"*, and by NEVER acting on a mismatch. Prefix: `"set:"` — verified not to collide with
`cmd:`, `hold:`, `go:`, `close-yes-`, `close-no-`, `model:`, `effort:` or `opt-`.

**D9 — the "reply with the value" step: lifetime, cancellation, and what it swallows. Blocks Task 5.**
§8.2: *"Numbers, free text and ordered lists use a 'reply with the value' step: the bot asks, the next
owner message in that topic is taken as the value and validated."* **No such primitive exists in this
tree.** `AnswerBinding_Decider` binds a typed reply to an AGENT-asked decision and only when exactly one
is open; `PendingOwnerReply` tracks the opposite direction (the owner waiting on a session); the
`/model` `/effort` dials avoid the problem entirely by putting the value on the command line or in a
stateless tap. So this step is new state, and new state that can EAT AN OWNER'S MESSAGE is the most
dangerous thing in this plan: a message swallowed by a forgotten settings prompt never reaches the
supervisor, and nothing says so.
*Decision:* the window, the cancel, and the precedence against every other inbound path.
**Recommended:** the pending step is keyed per (chat, topic), **expires after 5 minutes**, is cleared by
any message beginning with `/` (which is then handled normally, so `/pending` still works), is cleared by
an explicit `/cancel`, and is cleared whenever the menu message is closed or replaced. The prompt says
all of that in its own text. It is consulted **after** the bot-command chain and **before**
`Route_OwnerMessage_Async`, and when it fires it SWALLOWS the message and says so in the topic — an
acknowledged swallow is a decision, a silent one is decision 21's silence. It is persisted with the rest
of the bridge state so an app restart does not leave an invisible trap. **A test must pin that an expired
step routes the message to the supervisor normally.**

**D10 — concurrent writers to `config.json`. Blocks Task 2.**
Today three code paths write that file with an unsynchronised read-modify-write:
`OrchestratorConfig_Loader.Save` (the WPF window and the engine's `/screenshots` toggle),
`ConfigRepos_Reorderer.Persist_Order`, `ConfigRepoColor_Writer`. This plan adds three more (a Telegram
tap, a web `PUT`, a WPF row commit), and two of them can fire while the owner is dragging repos in the
main window. A lost update here is silent.
*Decision.* **Recommended: `Settings_Writer` holds a process-wide lock and does the read, the edit and
the atomic write INSIDE it; `OrchestratorConfig_Loader.Save` joins the same lock (a one-line change and
the only existing writer that writes many keys at once).** `ConfigRepos_Reorderer` and
`ConfigRepoColor_Writer` are left alone — nobody asked, and they are PARKED. **Cross-process is NOT
solved and must be named in the report:** the WPF app and the daemon are two processes and can both hold
a supervision root; a lock in one does not restrain the other. That is a real, stated limit, not an
oversight.

**D11 — the Connection tab and the two Telegram ids. Blocks Task 9.**
§8.1: *"Connection settings (token, chat id, owner id) stay on their own tab."* But `telegramSupergroupChatId`
and `telegramOwnerUserId` ARE catalogue rows (Kernel, `Kind = Int`, nullable) while the bot token is
**deliberately absent** from the catalogue and must stay so — `SettingsCatalogTests.NoDefinition_ExposesTheBotToken`
is an assertion precisely because every renderer lists the catalogue.
*Decision.* **Recommended: the Connection tab is hand-written for the TOKEN only (it writes
`secrets.json` through `OrchestratorConfig_Loader.Save`, with its own explicit Save button per D5), and
it renders the two id rows through the SAME `SettingsRow_Builder` every other tab uses.** One definition
of an id, one validator, one origin label — a second hand-written id field is exactly the drift §12 names.

**D12 — Composite rows: `repos` and `planBackend`. Blocks Task 9 and Task 6.**
§8.1 says *"Composite → the existing hand-written panel for that block"*. There is no existing panel for
`planBackend` anywhere, and `repos` is edited in `MainWindow`, not in `SettingsWindow`.
*Decision.* **Recommended: a Composite row renders as a read-only value preview plus one sentence naming
where it IS edited** — `repos` points at the main window's list, `planBackend` says "hand-edited in
config.json; `PlanBackend_Loader` is the authority on its shape", which is what its own catalogue
description already says. `Settings_Writer` refuses Composite outright, by the same rule that refuses
every `Renderer == ReadOnly` row, so the web `PUT` and the Telegram tap need no special case.

**D13 — is `preset` itself editable from a renderer? Blocks Task 1.**
`preset` is a `config.json` key (`Presets_Loader.PRESET_KEY`) and it is **not** a catalogue entry, by
plan 02's design; §6.3 says the renderers *"never special-case a preset name"* and §10 phase 2 says the
installer asks on a fresh machine.
*Decision.* **Recommended: DISPLAY the active preset name in every renderer's header — the reading layer
already has to know it to label origins — and do not offer to change it.** Changing a preset changes
dozens of resolved values at once and is a machine-identity decision, not a settings row; registering it
would be plan 02's registry work, not this plan's.

**D14 — where the web page's HTML lives. Blocks Task 8.**
The grammar and the two presets follow a pattern: a file under `kit/`, embedded as a resource AND shipped
as `Content` beside the kit, with a test asserting the two copies are byte-identical. That pattern exists
because those files have a SECOND consumer (a bash tool reads the grammar with `jq`; an installer may
seed from a preset). The HTML page has exactly one consumer: the listener, which serves it from the
resource.
*Decision.* **Recommended: `AIOrchestratorCoreLib/Web/Assets/settings.html`, embedded only, with NO
`Content` item and NO `kit/` copy** — and the reason written in the `.csproj` comment, because the next
reader will otherwise "fix" it to match the grammar and create the two-copies problem the grammar's test
exists to police. One copy cannot drift.

**D15 — may Task 1's OrderedList picker exist while `PULSE_FIELDS` and `BOT_COMMANDS` are inert?
Blocks Task 1.**
Plan 03 Task 1 implements those two validators. If plan 04 lands first, `Settings_Writer` will ask the
definition, the definition will ask `SettingValidators`, and `SettingValidators` will say nothing — so a
renderer offering free text would let the owner write `"suprvisor"` into `pulse.fields`.
*Decision.* **Recommended: the OrderedList renderer offers `PulseField_Names.ALL` / `BotCommandMenu.ALL`
as a PICKER in all three surfaces, so a typo is not reachable through any renderer, and plan 04
implements NEITHER validator** (they are plan 03's, and writing them here would be two implementations of
one check racing to land). The picker is drawing, not validating — decision 21's line. A hand-edit can
still write a bad word and that stays plan 03's to refuse; say so in the Task 1 report.

---

## File Structure

**Created**

```
AIOrchestratorCoreLib/Configuration/SettingsPresentation/
  SettingReading/
    ISettingReading.cs
    SettingReadingModel.cs
    SettingReading_Factory.cs
  SettingsSnapshot_Reader.cs        ← every definition -> a reading, from three trees + an optional session
  SettingValue_Formatter.cs         ← THE one way a value reads as text
  SettingOrigin_Labels.cs           ← THE one way an origin reads as text
  RestartKind_Labels.cs             ← THE one way a RestartKinds reads as text
  SettingsRow_Builder.cs            ← readings grouped into category sections; what all three renderers bind to
AIOrchestratorCoreLib/Configuration/SettingsWriting/
  SettingsWriteOutcomes.cs          ← Applied | Reset | RefusedUnknownPath | RefusedReadOnly | RefusedInvalid
  Settings_Writer.cs                ← the ONE thing that writes a catalogue path
AIOrchestratorCoreLib/Telegram/SettingsMenu/
  SettingsButton_Data.cs            ← "set:" payloads, own prefix, own 64-byte assert (D8)
  SettingsMenuViews.cs              ← Categories | Category | Setting | Values  (which screen)
  SettingsMenu_Builder.cs           ← (Text, ButtonRows) for a view — pure
  SettingsReplyStep_Decider.cs      ← pure: does this inbound message answer a pending prompt? (D9)
AIOrchestratorCoreLib/Web/
  ListenAddress.cs                  ← Parse_OrNull(host:port | "off"); backs SettingValidators.LISTEN_ADDRESS
  SettingsRequest_Handler.cs        ← (method, path, headers, body) -> (status, contentType, body) — pure
  SettingsWebHost/
    ISettingsWebHost.cs
    SettingsWebHostModel.cs
    SettingsWebHost_Factory.cs
  Assets/settings.html              ← embedded only (D14)

AIOrchestrator/Views/
  SettingRowTemplate_Selector.cs    ← DataTemplateSelector keyed on SettingRenderers

AIOrchestratorCoreLib.Tests/Configuration/SettingsPresentation/
  SettingsSnapshotReaderTests.cs
  SettingValueFormatterTests.cs
  SettingsRowBuilderTests.cs
AIOrchestratorCoreLib.Tests/Configuration/SettingsWriting/
  SettingsWriterTests.cs
AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/
  ListenAddressValidatorTests.cs
AIOrchestratorCoreLib.Tests/Telegram/SettingsMenu/
  SettingsButtonDataTests.cs
  SettingsMenuBuilderTests.cs
AIOrchestratorCoreLib.Tests/Bridge/
  SettingsMenuOnTheEngineTests.cs
  SettingsReplyStepTests.cs
AIOrchestratorCoreLib.Tests/Web/
  SettingsRequestHandlerTests.cs
  SettingsWebHostSmokeTests.cs
  SettingsPageAssetTests.cs
AIOrchestratorCoreLib.Tests/Configuration/
  EverySettingReachesEveryRendererTests.cs     ← Task 10, the §12 mitigation

docs/superpowers/plans/2026-09-12-settings-renderers-04-report.md
```

**Modified**

| file | change | task |
|---|---|---|
| `Configuration/SettingsCatalog/SettingValidators.cs` | `LISTEN_ADDRESS` implemented; its "NOT YET IMPLEMENTED" docstring shortened to name only the two that remain | 3 |
| `Configuration/OrchestratorConfig_Loader.cs` | `Save` joins `Settings_Writer`'s lock; the four model assignments removed (D1) | 2 |
| `Telegram/BotCommandMenu.cs` | one `("settings", "…")` tuple | 5 |
| `Telegram/TelegramSendBudget/ITelegramSendBudget.cs`, `TelegramSendBudgetModel.cs` | the narrow per-message-gap exemption (D7) | 5 |
| `Bridge/BridgeEngine/BridgeEngineModel.cs` | one `else if` in the text chain (~7068-7216); one `Try_HandleSettingsTap_Async` call inserted before the generic `opt-` path (~11230); the reply-step consultation before `Route_OwnerMessage_Async` (~7256); the menu STATE moves out into `Bridge/SettingsMenu/` per the code-conventions rule | 5 |
| `Composition/OrchestratorServices/IOrchestratorServices.cs`, `OrchestratorServicesModel.cs`, `OrchestratorServices_Factory.cs` | `SettingsWebHost` beside `Engine` | 7 |
| `AIOrchestrator/App.xaml.cs`, `AIOrchestrator.Daemon/BridgeHost_Service.cs` | start and stop the web host | 7 |
| `AIOrchestratorCoreLib/AIOrchestratorCoreLib.csproj` | one `<EmbeddedResource>` for `settings.html` | 8 |
| `AIOrchestrator/SettingsWindow.xaml`, `SettingsWindow.xaml.cs` | rewritten: tabs per category, `ItemsControl` + template selector, Connection tab keeps the token | 9 |
| `AIOrchestratorCoreLib.Tests/Telegram/BotCommandMenuTests.cs` | `ALL.Count` 35 → 36 and `EXPECTED_ORDER` gains `settings` | 5 |
| every `ITelegramApiClient` stub in the test project | nothing, unless D7 changes the interface — **check before assuming** | 5 |

---

### Task 1: The reading layer — one value, one origin label, one row, three renderers

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/SettingsPresentation/SettingReading/ISettingReading.cs`, `SettingReadingModel.cs`, `SettingReading_Factory.cs`
- Create: `AIOrchestratorCoreLib/Configuration/SettingsPresentation/SettingValue_Formatter.cs`, `SettingOrigin_Labels.cs`, `RestartKind_Labels.cs`, `SettingsSnapshot_Reader.cs`, `SettingsRow_Builder.cs`
- Test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsPresentation/SettingsSnapshotReaderTests.cs`, `SettingValueFormatterTests.cs`, `SettingsRowBuilderTests.cs`

**Blocked by D13 and D15.**

**Interfaces:**
- Consumes: `SettingsCatalog.ALL` / `In_Category` / `Find_OrNull`; `ISettingDefinition` (all thirteen fields); `Settings_Resolver.Resolve`; `SettingOrigins`; `Presets_Loader.Resolve_ForConfig`; `PulseField_Names.ALL`; `Telegram.BotCommandMenu.ALL`; `IOrchestrationSession`.
- Produces:
  - `ISettingReading` — `ISettingDefinition Definition`, `JsonNode? Value_OrNull`, `SettingOrigins Origin`, `string DisplayValue`, `string OriginLabel`, `string RestartLabel`, `bool IsEditable`, `string? SessionNote_OrNull`, `IReadOnlyList<string> OfferedValues`
  - `SettingsSnapshot_Reader.Read_All(JsonObject? configTree, JsonObject? presetTree, string presetName, IOrchestrationSession? session) : IReadOnlyList<ISettingReading>`
  - `SettingsSnapshot_Reader.Read_All_FromDisk(ISupervisionPaths paths, IOrchestrationSession? session) : (IReadOnlyList<ISettingReading> Readings, string PresetName)`
  - `SettingsSnapshot_Reader.Read_One_OrNull(string path, …) : ISettingReading?`
  - `SettingsRow_Builder.Build_Sections(IReadOnlyList<ISettingReading>) : IReadOnlyList<(SettingCategories Category, string Title, IReadOnlyList<ISettingReading> Rows)>`
  - `SettingValue_Formatter.Describe(ISettingDefinition, JsonNode?) : string`
  - `SettingOrigin_Labels.Describe(SettingOrigins, string presetName) : string`
  - `RestartKind_Labels.Describe(RestartKinds) : string`

- [ ] **Step 1: Re-read what plan 02 actually shipped.** `SettingsCatalog.cs`, `Settings_Resolver.cs`,
  `SettingDefinition/ISettingDefinition.cs` and `SettingsJson_Path.cs` in the NEW worktree. Confirm plan 02
  Task 8 has merged (`docs/superpowers/plans/2026-09-12-settings-catalogue-02-report.md` exists and
  `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetProbeTests.cs` exists). If either is
  missing, STOP and say so. Record `SettingsCatalog.ALL.Count` and the per-category counts in the report —
  every later task's expected numbers come from this one measurement, not from this document.

- [ ] **Step 2: Write the failing tests.**

`SettingValueFormatterTests.cs` — the formatter is the thing three renderers would otherwise each write:

```csharp
/// <summary>
/// ONE WAY A VALUE READS AS TEXT, because the alternative is three. A WPF label, a Telegram button
/// caption and a web table cell showing "30", "30 min" and "30 minutes" for the same row is CLAUDE.md
/// decision 12's drift wearing three hats, and unlike a formatter inside one file it cannot be found by
/// reading any one of them.
/// </summary>
[Fact] public void ABool_ReadsAsOnOrOff()
[Fact] public void AnEnum_ReadsAsItsOwnWord()
[Fact] public void ANullableEnumWithNoValue_ReadsAsTheMeaningOfNull_NotAsBlank()   // effort.* -> "no --effort flag"
[Fact] public void AnInt_ReadsAsItsNumber_AndANullableAbsentOne_AsNotSet()
[Fact] public void AnEmptyString_ReadsAsTheEmptyMeaning_NotAsAnEmptyCell()          // voiceTranscribeCommand, owner.name
[Fact] public void AStringList_ReadsAsItsWordsInOrder_AndAnEmptyOne_AsNone()        // general.buttons: [] under classic
[Fact] public void AComposite_ReadsAsAOneLineSummary_NeverAsRawJsonOfUnboundedLength()
[Fact] public void ALongStringList_IsTruncatedWithACount_SoAButtonCaptionCannotGrowWithoutLimit()
```

`SettingsSnapshotReaderTests.cs` — the origin is the load-bearing half:

```csharp
/// <summary>
/// THE ORIGIN IS WHAT THE OWNER IS ACTUALLY ASKING (spec §6.2: the renderers show the origin of every
/// value and offer Reset). "Why is my supervisor on Fable when the catalogue says Opus" is answered by
/// the word "from preset classic" and by nothing else on the screen.
/// </summary>
[Fact] public void EveryCatalogueEntry_ProducesExactlyOneReading()
[Fact] public void AShippedDefault_SaysShippedDefault()
[Fact] public void APresetValue_NamesThePresetItCameFrom()                 // "from preset classic", not "from a preset"
[Fact] public void AConfigFileValue_SaysSetHere()
[Fact] public void AnOrchestrationScopedKeyWithASessionOverride_SaysSoAndIsNotEditable()
[Fact] public void AReadOnlyRow_IsNotEditable_AndSaysWhereItIsChangedInstead()      // D12: repos, planBackend, session.*

/// <summary>
/// A PICKER, NOT FREE TEXT, FOR THE TWO LIST SETTINGS (D15). pulse.fields' and pulse.buttons' validators
/// are registered names with no check until plan 03 Task 1, so what stops a typo reaching config.json
/// today is that no renderer OFFERS one. That is drawing, not validating — the catalogue is still the
/// only thing that says yes (decision 21) — and the day plan 03 lands, the picker and the validator agree
/// because both read PulseField_Names.ALL.
/// </summary>
[Fact] public void PulseFields_OffersTheEightKnownWords_AndNothingElse()
[Fact] public void PulseButtons_OffersEveryVerbInBotCommandMenu()
[Fact] public void AnEnumRow_OffersItsEnumValues_AndANullableOne_AlsoOffersTheNullMeaning()
[Fact] public void ATextRow_OffersNothing_BecauseItIsFreeText()

/// <summary>
/// THE PRESET NAME IS SHOWN, NEVER OFFERED (D13). The reader has to know it to label an origin, so it
/// hands it back; making it a row would be plan 02's registry work and would put a machine-identity
/// decision on a settings page beside a notification toggle.
/// </summary>
[Fact] public void TheSnapshot_CarriesTheActivePresetName_AndNoReadingForThePresetKeyItself()

/// <summary>
/// A CONFIG FILE THAT WILL NOT PARSE READS AS AN EMPTY ONE, never as a throw. This reader is called on
/// the app's startup path through the same provider Load_OrEmpty is, and by an HTTP GET that must answer.
/// </summary>
[Fact] public void ACorruptConfigFile_ReadsAsNobodyHavingSaidAnything_AndDoesNotThrow()
```

`SettingsRowBuilderTests.cs`:

```csharp
[Fact] public void EveryCategory_BecomesOneSection_InCatalogueOrder()
[Fact] public void TheKitCategory_ProducesAnEmptySection_AndDoesNotThrow()   // plan 05 fills it; a renderer must survive it today
[Fact] public void EverySection_CarriesOnlyItsOwnCategorysRows()
[Fact] public void TheSectionsCoverEveryReading_WithNoneDuplicatedAndNoneLost()
```

Run them; they fail to compile. That is the red.

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-plan04
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsPresentation"
```

- [ ] **Step 3: The three label helpers.** Each is a pure static with one `Describe`. `SettingOrigin_Labels`
  takes the preset NAME as a parameter rather than looking it up — a formatter that reads a file is a
  formatter with a disk dependency, and the reason is the same one `Settings_Resolver` gives for taking its
  layers as parameters. `RestartKind_Labels` says what `RestartKinds`' own doc says: hot / next spawn /
  needs a host restart, **and never implies it is enforced** (spec §6.1: "shown by the renderers, enforced
  by nobody").

- [ ] **Step 4: The reading triple.** `ISettingReading` carries exactly the members in this task's
  Interfaces block. `OfferedValues` is how a renderer knows what to draw as choices without asking the
  catalogue a second question — empty for Text and Number, `EnumValues` (plus the null meaning for a
  nullable Enum) for Choice, the known-word list for the two OrderedList rows (D15). `IsEditable` is
  `Renderer != SettingRenderers.ReadOnly` and NOTHING ELSE — one rule, read off the catalogue, so the
  writer's refusal and the renderer's greying-out can never disagree.

- [ ] **Step 5: `SettingsSnapshot_Reader`.** The pure overload takes the three trees plus the preset name
  plus an optional session and walks `SettingsCatalog.ALL` calling `Settings_Resolver.Resolve` once per
  definition. The `_FromDisk` overload reads `config.json` through the loader's own tolerant path (a
  parse failure is an empty tree, with one `Log_Warning` line naming the file — decision 21's corollary),
  resolves the preset through `Presets_Loader.Resolve_ForConfig` inside a try/catch that falls back to
  `classic` exactly as `OrchestratorConfig_Loader.Resolve_Preset_OrClassic` does, and hands both to the
  pure one. **Do not duplicate that fallback — call the loader's behaviour or move it to one place and
  have both call it**; a second copy of "a mistyped preset word means classic" is the drift this file is
  supposed to end.

- [ ] **Step 6: `SettingsRow_Builder`.** Sections in the catalogue's own order (Models, Kernel, Phone,
  Receipts, Pulse, Owner — plus the empty Kit), each with a title. This is what the WPF window binds to,
  what the Telegram menu pages over, and what the web `GET` serialises. **Because it lives here and not in
  `AIOrchestrator/`, the WPF renderer's content is testable by this suite** — that is the whole reason the
  class exists and its doc must say so.

- [ ] **Step 7: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsPresentation"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsCatalog|FullyQualifiedName~Configuration"
```
Expected: green, and nothing in `Configuration` moved.

- [ ] **Step 8: Commit.** `feat(settings): one reading of a setting — value, origin and row, for all three renderers`

Report: `SettingsCatalog.ALL.Count` and the per-category counts as MEASURED; the exact strings the three
label helpers produce; and the honest sentence about D15 — a typo in `pulse.fields` is unreachable through
a renderer and still reachable by hand-edit until plan 03 Task 1.

---

### Task 2: The writing layer — the catalogue validates, this writes

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/SettingsWriting/SettingsWriteOutcomes.cs`, `Settings_Writer.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs` (the lock; the four model assignments under D1)
- Test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsWriting/SettingsWriterTests.cs`

**Blocked by D1, D6 and D10.**

**Interfaces:**
- Consumes: `SettingsCatalog.Find_OrNull`; `ISettingDefinition.Validate_OrNull`; `SettingsJson_Path.Write` / `.Remove`; `Atomic_FileWriter.Write_AllText`; `ISupervisionPaths`; `IOrchestrationLog`.
- Produces:
  - `enum SettingsWriteOutcomes { Applied, Reset, RefusedUnknownPath, RefusedReadOnly, RefusedInvalid }`
  - `Settings_Writer.Apply(ISupervisionPaths paths, string path, JsonNode? value, IOrchestrationLog? log) : (SettingsWriteOutcomes Outcome, string? Message_OrNull)`
  - `Settings_Writer.Reset(ISupervisionPaths paths, string path, IOrchestrationLog? log) : (SettingsWriteOutcomes Outcome, string? Message_OrNull)`
  - `Settings_Writer.Apply_Many(ISupervisionPaths paths, IReadOnlyList<(string Path, JsonNode? Value)> edits, IOrchestrationLog? log) : IReadOnlyList<(string Path, SettingsWriteOutcomes Outcome, string? Message_OrNull)>` — one file read and one atomic write for a whole `PUT` body
  - `Settings_Writer.CONFIG_WRITE_LOCK` — the object `OrchestratorConfig_Loader.Save` also takes (D10)

- [ ] **Step 1: Write the failing tests.** Temp `ISupervisionPaths` fixture, copied from
  `PerRoleModelDefaultsTests`' constructor verbatim (GUID folder under `Path.GetTempPath()`,
  `SupervisionPaths_Factory.Create`, delete in `Dispose`).

```csharp
/// <summary>
/// THE DEFINITION SAYS YES OR NO, AND THIS CLASS SAYS WHERE THE BYTES GO (CLAUDE.md decision 21: hooks
/// advise, the app enforces at the point of effect — and the point of effect for a setting is the write).
/// Three renderers each carrying their own "is 240 a legal interval" is three answers to one question;
/// the catalogue already knows, and its message is the one the owner sees.
/// </summary>
[Fact] public void AValidValue_IsWritten_AtItsCataloguePath()
[Fact] public void AValueTheDefinitionRefuses_IsNotWritten_AndTheMessageIsTheCataloguesOwn()
[Fact] public void AnUnknownPath_IsRefused_WithoutTouchingTheFile()
[Fact] public void ALegacySpelling_ResolvesToItsDefinition_AndIsWrittenUnderTheNEWPath()

/// <summary>
/// A ReadOnly ROW IS REFUSED BY ONE RULE READ OFF THE CATALOGUE, never by a list of paths kept here.
/// repos, planBackend and the three session.* state rows are all ReadOnly for different reasons and the
/// writer needs to know none of them — a second list would be a place for the fifth one to be forgotten.
/// </summary>
[Theory]
[InlineData("repos")] [InlineData("planBackend")]
[InlineData("session.paused")] [InlineData("session.telegramMode")] [InlineData("session.ownerPresence")]
public void AReadOnlyRow_IsRefused_AndSaysWhereItIsChangedInstead(string path)

/// <summary>
/// RESET DELETES THE KEY, IT DOES NOT WRITE THE DEFAULT (spec §6.2). A materialised default is a default
/// that can never move again, frozen on the first button press — the rule the loader already keeps for
/// reviewerModel, now enforced for all 66.
/// </summary>
[Fact] public void Reset_DeletesTheKey_AndTheValueFallsBackToThePresetOrTheShippedDefault()
[Fact] public void Reset_OnAKeyThatWasNeverSet_IsHarmless_AndSaysSo()

/// <summary>
/// UNKNOWN KEYS SURVIVE, which is the property spec §8 was actually asking for when it said "write
/// through the fork's merging Save" (D6). Agents edit config.json at runtime; a settings write that
/// dropped planBackend would be the defect Save's own docstring records being fixed twice.
/// </summary>
[Fact] public void AHandEditedKeyThisBuildDoesNotKnow_SurvivesAWrite()
[Fact] public void EveryOtherCatalogueKey_IsUntouchedByAWriteToOne()

/// <summary>
/// ATOMIC, AND NEVER A ZERO-LENGTH config.json — Atomic_FileWriter's own reason, which a settings page
/// reaches far more often than the Settings window ever did.
/// </summary>
[Fact] public void TheWrite_GoesThroughTheAtomicWriter_AndLeavesNoTempFileBehind()

/// <summary>
/// A CORRUPT config.json IS REPLACED RATHER THAN REFUSED, and it is LOGGED. Read_JsonObject_ForEditing
/// already swallows a parse failure to an empty object, for the stated reason that refusing would strand
/// the owner with a corrupt config and no way to fix it from the app. That reasoning holds here, and the
/// silence does not: one warning line naming the file, because the owner is about to lose hand-edited
/// keys they cannot see (decision 21's corollary — say which predicate failed and why).
/// </summary>
[Fact] public void ACorruptConfigFile_IsReplaced_AndOneWarningLineNamesIt()

/// <summary>
/// Apply_Many IS ONE READ AND ONE WRITE. A PUT body of twelve settings must not be twelve
/// read-modify-write cycles: eleven of them would be racing the other ten.
/// </summary>
[Fact] public void ApplyMany_WritesOnce_AndReportsPerPath()
[Fact] public void ApplyMany_WithOneInvalidPath_AppliesTheRest_AndNamesTheOneItRefused()
```

Plus, under **D1** (add to `AIOrchestratorCoreLib.Tests/Configuration/PerRoleModelDefaultsTests.cs`, the
file that owns "what a role gets when the owner says nothing"):

```csharp
/// <summary>
/// SAVE STOPS MATERIALISING THE FOUR UI MODELS (D1, owner). Until 2026-09-12 pressing Save in the
/// Settings window wrote whatever the four models had RESOLVED to — including a preset's value and,
/// before plan 02, a shipped default — into config.json as the owner's own, which is how the owner's
/// live config.json came to pin a stale model and defeat the Opus default they had asked for (CLAUDE.md,
/// entry 216). With a renderer for every key, a model is written when the owner edits that row and at no
/// other time, exactly like reviewerModel and soloModel already were.
/// </summary>
[Fact] public void Save_NoLongerWritesAnyModelKey_SoAPresetValueStaysAPresetValue()
```

- [ ] **Step 2: Run to verify it fails.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsWriterTests|FullyQualifiedName~PerRoleModelDefaultsTests"
```
Expected: compile errors for `Settings_Writer`, and the D1 case red.

- [ ] **Step 3: Write `Settings_Writer`.** The whole method is short and every line of it is a rule:
  resolve the definition (`Find_OrNull`, which matches the legacy spelling too) → refuse if null → refuse
  if `Renderer == ReadOnly` → **ask `definition.Validate_OrNull(value)` and refuse with ITS message** →
  take the lock → read the file with the loader's merge semantics → `SettingsJson_Path.Write(root,
  definition.Path, value)` (the NEW path, never the legacy one — a write under the old spelling would
  re-create the alias forever) → `Atomic_FileWriter.Write_AllText`. `Reset` is the same with `.Remove`,
  and it removes the legacy spelling too, because leaving it would make Reset a no-op on the machines
  that most need it. The class doc states, with the date, that this is the ONLY writer of a catalogue
  path and why the validation is the definition's (decision 21).

- [ ] **Step 4: The lock, and `Save` joins it (D10).** `public static readonly Lock CONFIG_WRITE_LOCK`
  on `Settings_Writer`; `OrchestratorConfig_Loader.Save` wraps its read-edit-write in the same lock. Its
  doc gains one paragraph naming the six writers that now exist and stating plainly that **this is
  in-process only** — the WPF app and the daemon are two processes and a lock in one restrains nothing in
  the other. `ConfigRepos_Reorderer` and `ConfigRepoColor_Writer` are NOT changed (PARKED).

- [ ] **Step 5: D1's removal.** Delete the four `configRoot["…Model"] = …` lines from `Save` and move the
  reasoning into the paragraph immediately below that already explains why `reviewerModel` and `soloModel`
  are read and never written — it now covers all six, and the sentence that used to distinguish them
  ("the four have a Settings field") is deleted rather than left to read as true. **Grep every caller of
  `Save` and check none of them depended on the four being written:** `AIOrchestrator/SettingsWindow.xaml.cs`
  (rewritten in Task 9), `BridgeEngineModel.cs:1214` (`Create_WithStatusScreenshots`), and the tests.
  Name every hit in the commit body.

- [ ] **Step 6: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsWriterTests"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Configuration"
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
```
Expected: 0 failed. A red in `Configuration` here is a real consequence of D1 — isolate it by name, then
fix it; do not widen an assertion to absorb it.

- [ ] **Step 7: Commit.** `feat(settings): one writer for every catalogue path — the definition decides, this only writes`

---

### Task 3: `web.listen` gets the validator plan 03 handed here

**Files:**
- Create: `AIOrchestratorCoreLib/Web/ListenAddress.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingValidators.cs`
- Test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/ListenAddressValidatorTests.cs`

Small, separate, and first among the web tasks on purpose: the listener of Task 7 must never be the thing
that discovers `web.listen` is nonsense.

**Interfaces:**
- Consumes: nothing from Tasks 1-2.
- Produces:
  - `ListenAddress.OFF = "off"`, `ListenAddress.Parse_OrNull(string?) : (string Host, int Port)?`
  - `ListenAddress.Is_Off(string?) : bool`
  - `SettingValidators.Validate_OrNull` answering for `LISTEN_ADDRESS`

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void TheShippedDefault_IsAccepted()                    // "127.0.0.1:7391"
[Fact] public void TheWordOff_IsAccepted_AndParsesToNoEndpoint()
[Fact] public void AHostWithNoPort_IsRefused_NamingWhatIsMissing()
[Fact] public void APortOutsideOneToSixtyFiveThousandFiveThirtyFive_IsRefused()
[Fact] public void APortThatIsNotANumber_IsRefused_WithoutThrowing()
[Fact] public void AnIpV6Literal_InBrackets_IsAccepted()             // "[::1]:7391" — decide and pin it
[Fact] public void ANonStringValue_IsRefused_AsExpectedAString()

/// <summary>
/// THE CATALOGUE'S SHIPPED DEFAULT MUST SATISFY ITS OWN VALIDATOR, and until this task it did so only
/// because the validator accepted everything. SettingsCatalogTests.EveryShippedDefault_SatisfiesItsOwn
/// Definition has been passing web.listen vacuously; after this task it passes for a reason.
/// </summary>
[Fact] public void TheCatalogueEntrysOwnDefault_PassesTheRealCheck()
```

- [ ] **Step 2: Write `ListenAddress` and wire the case.** In `SettingValidators.Validate_OrNull`, add
  `LISTEN_ADDRESS => Validate_ListenAddress_OrNull(value)`. **Shorten the fallthrough comment and the
  three docstrings to name only `PULSE_FIELDS` and `BOT_COMMANDS`** — leaving `LISTEN_ADDRESS` in a "NOT
  YET IMPLEMENTED" paragraph after implementing it is the stale-comment failure this codebase keeps paying
  for, and plan 03 Task 1 gives the same instruction from the other side.

- [ ] **Step 3: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~ListenAddress|FullyQualifiedName~SettingValidators|FullyQualifiedName~SettingsCatalogTests"
```

- [ ] **Step 4: Commit.** `feat(settings): web.listen is validated — the name plan 02 registered now has its check`

---

### Task 4: The Telegram menu, as a pure builder

**Files:**
- Create: `AIOrchestratorCoreLib/Telegram/SettingsMenu/SettingsButton_Data.cs`, `SettingsMenuViews.cs`, `SettingsMenu_Builder.cs`
- Test: `AIOrchestratorCoreLib.Tests/Telegram/SettingsMenu/SettingsButtonDataTests.cs`, `SettingsMenuBuilderTests.cs`

**Blocked by D2, D3 and D8.** Pure on purpose: the engine wiring is Task 5, and a menu whose text and
buttons are decided by a static function is a menu that can be proven without a bot.

**Interfaces:**
- Consumes: Task 1's `ISettingReading` / `SettingsRow_Builder`; `SettingsCatalog.ALL`; `SettingCategories`; `SettingRenderers`.
- Produces:
  - `enum SettingsMenuViews { Categories, Category, Setting, Values }`
  - `SettingsButton_Data` — `PREFIX = "set:"`, `Build(SettingsMenuViews view, int categoryOrSettingId, int page, string? value) : string`, `Parse_OrNull(string?) : (…)?`, its OWN `TELEGRAM_CALLBACK_DATA_BYTE_LIMIT = 64` assert (the pattern `ModelEffortButton_Data` establishes)
  - `SettingsMenu_Builder.Build(SettingsMenuViews view, …, IReadOnlyList<ISettingReading> readings, string presetName) : (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows)`
  - `SettingsMenu_Builder.ROWS_PER_PAGE`

- [ ] **Step 1: Write the failing payload tests.**

```csharp
/// <summary>
/// A STATELESS PAYLOAD, LIKE THE DIALS AND UNLIKE THE OPTIONS (CLAUDE.md decision 24). ModelEffortButton_Data
/// carries its whole answer so it survives a restart; the opt- registry does not and answers "expired".
/// A settings menu is a control the owner may come back to, so it must be the first kind.
/// </summary>
[Fact] public void APayload_RoundTrips()
[Fact] public void ThePrefix_CollidesWithNoOtherFamily()      // cmd: hold: go: close-yes- close-no- model: effort: opt-
[Fact] public void SomeoneElsesPayload_ParsesToNull_SoItFallsThroughToTheNextHandler()

/// <summary>
/// THE LONGEST PAYLOAD THE CATALOGUE CAN PRODUCE FITS IN 64 BYTES, and this is computed from the
/// catalogue rather than from a guess: the widest id, the widest page number, and the longest enum word
/// of any Enum row. Telegram refuses an over-long callback_data at SEND time, on the phone, where nothing
/// in this suite can see it.
/// </summary>
[Fact] public void TheLongestPayloadTheCatalogueCanProduce_FitsUnderTheCap()
[Fact] public void Build_Throws_RatherThanReturnAnOversizedPayload()

/// <summary>
/// A STALE ID FROM AN OLDER BUILD IS REFUSED, NOT ACTED ON (D8). The id is an index into
/// SettingsCatalog.ALL, so a catalogue edit renumbers it and a menu message left open across an upgrade
/// would otherwise toggle a different setting than the one whose label the owner read.
/// </summary>
[Fact] public void AnIdOutsideTheCatalogue_ParsesButIsAnsweredAsStale_AndChangesNothing()
[Fact] public void AnIdWhoseCategoryDisagreesWithThePayload_IsAnsweredAsStale()
```

- [ ] **Step 2: Write the failing builder tests.**

```csharp
[Fact] public void TheCategoriesView_ListsEveryCategoryThatHasRows_WithItsCount()
[Fact] public void TheKitCategory_IsNotOffered_BecauseItIsEmpty()          // and the day plan 05 fills it, it is
[Fact] public void ACategoryView_ShowsEachSettingsLabelAndItsCurrentValue()
[Fact] public void ACategoryView_ShowsTheOriginBesideTheValue()            // the same words the other two renderers use

/// <summary>
/// KERNEL IS THIRTY-SEVEN ROWS AND THE OWNER ASKED WHETHER THIS WAS EVEN POSSIBLE. It is, by paging: no
/// view ever emits more than ROWS_PER_PAGE setting buttons plus one navigation row, so the tallest
/// keyboard is a fixed size no matter how the catalogue grows.
/// </summary>
[Fact] public void TheBiggestCategory_IsPaged_AndNoPageExceedsTheRowLimit()
[Fact] public void EverySettingInACategory_AppearsOnExactlyOnePage()
[Fact] public void ThePageRow_OffersPreviousAndNext_OnlyWhereThereIsOne()
[Fact] public void EveryViewExceptCategories_OffersBack()
[Fact] public void AnEditableSettingView_OffersReset()
[Fact] public void AReadOnlySettingView_OffersNeitherAValueNorReset_AndSaysWhereItIsChanged()

/// <summary>A TOGGLE FLIPS IN ONE TAP; A CHOICE OPENS ITS WORDS (spec §8.2).</summary>
[Fact] public void ABoolSetting_IsOneButtonThatCarriesTheOppositeValue()
[Fact] public void AnEnumSetting_OpensAValuesViewWithOneButtonPerWord_AndMarksTheCurrentOne()
[Fact] public void ANullableEnum_AlsoOffersTheNullMeaning_AsItsOwnButton()
[Fact] public void ANumberOrTextOrListSetting_OffersTheReplyStep_NotAValueButton()

/// <summary>THE FENCED KEYS (D2). A refusal that says why is a decision; a greyed button is a mystery.</summary>
[Fact] public void TelegramInbound_IsShownAndRefused_WithALineSayingItIsHowThisMenuReachesYou()
[Fact] public void TheTwoTelegramIds_AreShownAndRefused_ForTheSameReason()

[Fact] public void TheHeader_NamesTheActivePreset()                        // D13
[Fact] public void TheTextStaysUnderTelegramsMessageLimit_ForTheBiggestCategory()
```

- [ ] **Step 3: Implement.** `ROWS_PER_PAGE` is a named constant with its reason in a comment (a phone
  screen, not an API limit). `Build` is a `switch` over `SettingsMenuViews` with one private per view; it
  takes readings rather than reading anything, so the engine resolves once and the builder is a function.
  Labels come from Task 1's formatter — **not a second copy** (decision 12), and Task 10's test proves it.

- [ ] **Step 4: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsMenu"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Telegram" 2>&1 | tail -20
```

- [ ] **Step 5: Commit.** `feat(telegram): the settings menu as a pure paged builder`

---

### Task 5: The Telegram menu, wired — `/settings`, the tap, the reply step

**Files:**
- Modify: `AIOrchestratorCoreLib/Telegram/BotCommandMenu.cs` (one tuple)
- Modify: `AIOrchestratorCoreLib/Telegram/TelegramSendBudget/ITelegramSendBudget.cs`, `TelegramSendBudgetModel.cs` (D7's exemption)
- Create: `AIOrchestratorCoreLib/Bridge/SettingsMenu/` — the live-menu state, moved OUT of the engine file per the code-conventions rule
- Create: `AIOrchestratorCoreLib/Telegram/SettingsMenu/SettingsReplyStep_Decider.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` — the text chain (~7068-7216), `Handle_CallbackTap_Async` (~11230), the reply-step consultation before `Route_OwnerMessage_Async` (~7256)
- Modify: `AIOrchestratorCoreLib.Tests/Telegram/BotCommandMenuTests.cs`
- Create: `AIOrchestratorCoreLib.Tests/Bridge/SettingsMenuOnTheEngineTests.cs`, `SettingsReplyStepTests.cs`

**Blocked by D2, D3, D7 and D9. Serialised against every engine-touching task of plan 03.**

**Interfaces:**
- Consumes: Task 1's reader, Task 2's writer, Task 4's builder and payload; `ITelegramApiClient.Send_MessageWithButtonRows_Async` / `Edit_MessageTextWithButtonRows_Async` / `Answer_CallbackQuery_Async`; `TelegramHeldException`; `Remember_TopicMessage`; `Persist_BridgeState`.
- Produces:
  - `BotCommandMenu.ALL` gains `("settings", "…")` — **`BotCommandMenuTests` asserts `ALL.Count == 35` and an `EXPECTED_ORDER`; both must move to 36 in the same commit**
  - `Try_HandleSettingsTap_Async(client, tap, cancellationToken) : Task<bool>` — inserted as the FIFTH `Try_Handle…` in `Handle_CallbackTap_Async`, **before the generic `opt-` path**
  - `ISettingsMenuState` — the live menu message id per chat, and the pending reply step (D9), persisted with the bridge state

- [ ] **Step 1: Read the five things this task can break, before writing a line.**
  `Handle_CallbackTap_Async`'s handler order (the four existing `Try_Handle…` calls and the comment on each
  saying why it must precede the generic path); `Get_BotCommand_OrNull` and the `if/else if` chain's final
  `else` that makes an unrecognised message routable; `Route_OwnerMessage_Async`;
  `Hold_UnlessThisMessageMayBeEdited` and `TokenBucket_Gate`'s three constants; and the topic status
  line's edit-with-repost-fallback at ~10455-10512, which is the tree's own precedent for a self-editing
  message. Record in the report what each one currently does.

- [ ] **Step 2: Write the failing engine tests.** Copy the harness from
  `Tests/Bridge/OwnerAnswerSurvivesFailedSendTests.cs` VERBATIM — the temp root, the `ConfigFile` /
  `SecretsFile` setup, `FailableTelegram_Fake`, `RecordingLog_Fake`,
  `BridgeEngine_Factory.Create_WithTelegramClient`. **Do NOT register a print-runner session in an engine
  test**: `BridgeEngine_Factory` hard-wires the real per-OS `claude` into the print dispatcher and a test
  that registers one can spawn a LIVE process.

```csharp
[Fact] public void SlashSettings_InGeneral_SendsOneMessageWithTheCategories()
[Fact] public void SlashSettings_InAnOrchestrationTopic_AnswersWithThatOrchestrationsStateAndPointsAtTheDials()  // D3
[Fact] public void ATapOnACategory_EDITS_TheSameMessage_AndDoesNotSendASecondOne()
[Fact] public void ATapOnAToggle_WritesTheKey_AndTheEditedMenuShowsTheNewValueAndItsNewOrigin()
[Fact] public void ATapOnReset_DeletesTheKey_AndTheMenuShowsThePresetOrShippedValueAgain()
[Fact] public void ATapOnAFencedKernelKey_ChangesNothing_AndAnswersWhy()                                          // D2
[Fact] public void EverySettingsMessage_IsSilent()                                                                // a settings menu is not news

/// <summary>
/// FIVE TAPS IN FIVE SECONDS ALL LAND (D7). MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE is 30 seconds and
/// Hold_UnlessThisMessageMayBeEdited THROWS TelegramHeldException rather than sleeping, so without the
/// exemption taps two through five are swallowed by a rate limit measured against a background edit loop
/// — a different traffic shape entirely. This test is the one that proves the exemption is narrow AND
/// that it works.
/// </summary>
[Fact] public void FiveRapidTaps_AllTakeEffect_BecauseTheLiveMenuIsExemptFromThePerMessageGap()
[Fact] public void AMessageThatIsNotTheLiveMenu_StillGetsTheThirtySecondGap()
[Fact] public void WhenTheEditIsRefusedAnyway_TheMenuRepostsRatherThanVanishing()   // the status line's own fallback shape

/// <summary>
/// A TAP ON THE SETTINGS MENU NEVER BECOMES A SYNTHETIC OWNER MESSAGE. That is exactly what decision 24
/// records happening to the dials through the generic opt- path, and the handler order is the only thing
/// that prevents it.
/// </summary>
[Fact] public void ASettingsTap_IsHandledBeforeTheGenericOptionPath_AndReachesNoChannel()
```

`SettingsReplyStepTests.cs` — D9's rules, and they are the sharpest in the task:

```csharp
[Fact] public void APromptedNumber_IsTakenAsTheValue_Validated_AndWritten()
[Fact] public void APromptedValueTheCatalogueRefuses_IsNotWritten_AndTheOwnerIsToldTheCataloguesReason()
[Fact] public void AMessageStartingWithASlash_CancelsTheStep_AndIsHandledAsTheCommandItIs()
[Fact] public void SlashCancel_ClearsTheStep_AndSaysSo()

/// <summary>
/// AN EXPIRED STEP MUST NOT EAT THE OWNER'S MESSAGE. A settings prompt left open five minutes ago and
/// forgotten, swallowing a message meant for the supervisor, is the worst failure this feature can have:
/// the message is gone, nothing says so, and the owner is waiting for an answer to something that never
/// arrived.
/// </summary>
[Fact] public void AfterTheWindowPasses_TheNextMessage_RoutesToTheSupervisorNormally()
[Fact] public void WhileTheStepIsLive_TheSwallowedMessage_IsAcknowledgedInTheTopic_NeverSilently()
[Fact] public void TheStep_SurvivesAnAppRestart_OrIsClearedByIt_ButIsNeverInvisible()
[Fact] public void AStepInGeneral_DoesNotSwallowAMessageTypedInAnOrchestrationTopic()
```

- [ ] **Step 3: The state moves OUT of the engine.** `Bridge/SettingsMenu/` gets the triple holding the
  live menu message id per chat and the pending reply step. `.claude/rules/code-conventions.md`: *"when a
  stage touches a piece of that file, that piece moves out into its own component under `Bridge/` — no new
  lines land in the big file by inertia."* The engine keeps the three call sites and nothing else. Name
  every line changed in `BridgeEngineModel.cs` in the report.

- [ ] **Step 4: The command, the tap and the step.** One `else if (command == "settings")` before the
  final `else` of the text chain. One `Try_HandleSettingsTap_Async` call as the fifth in
  `Handle_CallbackTap_Async`, with a comment giving the SAME reason the four above it give — it is a
  control the app owns, and through the generic path it would become a synthetic owner message in an
  agent's channel. The reply step is consulted after the command chain and before
  `Route_OwnerMessage_Async`, per D9.

- [ ] **Step 5: D7's exemption, narrowly.** `ITelegramSendBudget` gains a way to register and release ONE
  exempt message id. `TelegramSendBudgetModel`'s doc gains the paragraph that distinguishes the two traffic
  shapes, naming the 2026-09-10 429 incident and stating that the control bucket (60/min) still governs
  every settings edit. **Release the exemption when the menu is replaced or closed** — an exemption that
  outlives its message is a permanently un-gapped message id.

- [ ] **Step 6: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsMenuOnTheEngine|FullyQualifiedName~SettingsReplyStep|FullyQualifiedName~BotCommandMenuTests"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~TelegramSendBudget|FullyQualifiedName~TokenBucket"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Bridge" 2>&1 | tail -20
```
The last one is the wide net and the run most exposed to the `Bridge/` flake family — **isolate any red
with `--filter` on its single name and re-run it alone before believing it**, and name it in the report
either way.

- [ ] **Step 7: Commit.** `feat(telegram): /settings — a self-editing paged menu over the catalogue`

Report: the piece moved out of `BridgeEngineModel.cs`; every engine line changed; the handler order after
insertion; and the exact shape of D7's exemption with the test name that proves a non-exempt message still
gets its 30 seconds.

---

### Task 6: The web JSON layer — two endpoints, no socket

**Files:**
- Create: `AIOrchestratorCoreLib/Web/SettingsRequest_Handler.cs`
- Test: `AIOrchestratorCoreLib.Tests/Web/SettingsRequestHandlerTests.cs`

Pure on purpose, and it is the reason the web renderer is as well covered as the Telegram one: everything
that decides anything happens here, and Task 7 only moves bytes.

**Interfaces:**
- Consumes: Task 1's reader and row builder; Task 2's `Apply_Many` / `Reset`; `ISupervisionPaths`.
- Produces:
  - `SettingsRequest_Handler.Handle(string method, string path, Func<string, string?> header, string body, ISupervisionPaths paths, string configuredToken, IOrchestrationLog? log) : (int Status, string ContentType, string Body)`
  - `SettingsRequest_Handler.SETTINGS_PATH = "/settings"`, `TOKEN_HEADER = "X-Aiorch-Token"`

- [ ] **Step 1: Write the failing tests.**

```csharp
/// <summary>
/// GET /settings HANDS THE BROWSER THE CATALOGUE ITSELF (spec §8.3, and SettingValidators' own doc says
/// this is why a validator is a NAME rather than a delegate). The page then renders from data, which is
/// what makes "a new catalogue entry appears in all three renderers with no UI code" true rather than
/// aspirational.
/// </summary>
[Fact] public void Get_CarriesEveryCatalogueEntry_WithItsValueOriginLabelDescriptionRendererAndRestart()
[Fact] public void Get_CarriesTheCategorySections_InCatalogueOrder_IncludingTheEmptyKitOne()
[Fact] public void Get_CarriesTheActivePresetName()
[Fact] public void Get_CarriesNoBotToken_AndNoSecretsAtAll()          // the assertion, not the comment

[Fact] public void Put_APartialObject_AppliesEachPath_AndReportsPerPath()
[Fact] public void Put_AnUnknownPath_IsRefused_AndNothingIsWritten()
[Fact] public void Put_AnInvalidValue_IsRefusedWithTheCataloguesOwnMessage()
[Fact] public void Put_ANullValue_MeansResetForThatPath_AndDeletesTheKey()
[Fact] public void Put_AReadOnlyPath_IsRefused()
[Fact] public void Put_ABodyThatIsNotJson_IsFourHundred_NotAThrow()

/// <summary>D4: a PUT with no configured token is refused while GET stays open on loopback.</summary>
[Fact] public void Put_WithNoConfiguredToken_IsFourOhOne_AndSaysWhatToSet()
[Fact] public void Put_WithTheWrongToken_IsFourOhOne()
[Fact] public void Put_WithTheRightToken_Applies()
[Fact] public void Get_WithNoToken_StillAnswers()

[Fact] public void AnUnknownPath_IsFourOhFour()
[Fact] public void AnUnsupportedMethod_IsFourOhFive_AndNamesWhatIsAllowed()

/// <summary>
/// INVARIANT CULTURE, EXPLICITLY. The daemon sets InvariantGlobalization=true and the WPF host does not,
/// so the same PUT body must parse the same way in both hosts — a number formatted or parsed under the
/// machine's culture is a setting that means one thing on one host and another on the other.
/// </summary>
[Fact] public void ANumber_IsParsedAndRenderedInvariantly_InEitherHost()
```

- [ ] **Step 2: Implement.** A `switch` on method and path; `System.Text.Json.Nodes` throughout (the repo
  uses no source generation and no POCO binding anywhere in the config surfaces, deliberately, because
  merge-preserving reads are the whole point). Every refusal body carries the catalogue's own message.
  **Token comparison is fixed-time** — it is a shared secret, and a naive `==` on a short string is a
  gratuitous side channel on a page that rewrites `config.json`.

- [ ] **Step 3: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsRequestHandler"
```

- [ ] **Step 4: Commit.** `feat(web): GET and PUT /settings as a pure handler, no socket in sight`

---

### Task 7: The web host — one `HttpListener`, started by both hosts

**Files:**
- Create: `AIOrchestratorCoreLib/Web/SettingsWebHost/ISettingsWebHost.cs`, `SettingsWebHostModel.cs`, `SettingsWebHost_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Composition/OrchestratorServices/IOrchestratorServices.cs`, `OrchestratorServicesModel.cs`, `OrchestratorServices_Factory.cs`
- Modify: `AIOrchestrator/App.xaml.cs`, `AIOrchestrator.Daemon/BridgeHost_Service.cs`
- Test: `AIOrchestratorCoreLib.Tests/Web/SettingsWebHostSmokeTests.cs`

**Blocked by D4.**

**Interfaces:**
- Consumes: Task 3's `ListenAddress`; Task 6's handler; `IOrchestratorConfigProvider.Get_Current()`; `IOrchestrationLog`.
- Produces: `ISettingsWebHost` — `Task Run_Async(CancellationToken)`, `bool IsListening`, `string? ListeningOn_OrNull`.

- [ ] **Step 1: Write the smoke test, and make it skip rather than lie.**

```csharp
/// <summary>
/// ONE REAL SOCKET, AND IT REFUSES TO RUN RATHER THAN CERTIFY NOTHING. HttpListener cannot bind port 0,
/// so this test probes for a free loopback port; if it cannot get one, or cannot bind, it SKIPS with a
/// message naming why. CLAUDE.md decision 20: a harness that cannot find what it tests must fail loudly
/// rather than certify the absence of the thing it never ran — a green "web host works" from a test that
/// never bound a socket is exactly the sixteen confident failures that rule was written for.
/// </summary>
[Fact] public void TheHost_ServesTheSettingsJson_OverARealLoopbackSocket()
[Fact] public void TheHost_ServesThePage_AtTheRoot()
[Fact] public void APut_OverTheSocket_ReachesTheWriter_AndTheFileChanges()

/// <summary>web.listen = "off" means NO LISTENER, not a listener that refuses.</summary>
[Fact] public void WithListenOff_NothingIsBound_AndTheHostSaysSo()

/// <summary>
/// A PORT ALREADY IN USE IS ONE WARNING LINE AND A HOST THAT KEEPS RUNNING, never a host that dies. The
/// WPF app and the daemon can both hold a supervision root on one machine and they ship the same default
/// port; the second one must not take the bridge down with it.
/// </summary>
[Fact] public void APortAlreadyInUse_LogsOnce_AndDoesNotStopTheHost()
```

- [ ] **Step 2: Implement.** `SettingsWebHostModel` reads `web.listen` and `web.token` from
  `_configProvider.Get_Current()` — **at start, and again whenever the provider's instance changes**, since
  `web.listen` is `Restart = Host` but `web.token` is not and an owner setting a token should not have to
  restart the app to use it. A request loop with `GetContextAsync`; every response goes through Task 6's
  handler; `GET /` returns the embedded page (Task 8); everything else is the handler's 404.
  **Never bind a non-loopback prefix by accident:** on Windows a non-loopback `HttpListener` prefix needs a
  URL ACL and fails with `HttpListenerException` — log one line naming that, and do not try to add an ACL.

- [ ] **Step 3: Both hosts, one class.** Add it to `IOrchestratorServices` beside `Engine`.
  **`OrchestratorServices_Factory.Create`'s construction order is documented as load-bearing — read that
  comment before inserting a line**, and put the web host after the engine, since it needs the config
  provider and nothing needs it. `App.xaml.cs` starts it as a background `Task` beside
  `services.Engine.Run_Async` and cancels it in `OnExit`; `BridgeHost_Service.Run_Host_Async` does the
  same beside its engine task and awaits it in the shutdown grace.

- [ ] **Step 4: Verify.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsWebHost"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Composition"
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
```
Report whether the smoke test RAN or SKIPPED, and on which OS. A skip is an acceptable outcome and an
unreported skip is not.

- [ ] **Step 5: Commit.** `feat(web): one settings listener, started by the app and the daemon alike`

---

### Task 8: The page — one embedded file that knows nothing

**Files:**
- Create: `AIOrchestratorCoreLib/Web/Assets/settings.html`
- Modify: `AIOrchestratorCoreLib/AIOrchestratorCoreLib.csproj` (one `<EmbeddedResource>`)
- Test: `AIOrchestratorCoreLib.Tests/Web/SettingsPageAssetTests.cs`

**Blocked by D14.**

- [ ] **Step 1: Write the guard test — the one assertion that matters.**

```csharp
/// <summary>
/// THE PAGE CONTAINS NO SETTING. Spec §8.3's promise is that "a new catalogue entry appears in all three
/// renderers with no UI code", and for this renderer that promise is exactly one testable property: the
/// HTML must mention no catalogue path, no setting label and no category name, because it renders
/// everything from GET /settings. This is the only thing about this file a test can honestly say, and it
/// happens to be the important thing.
/// </summary>
[Fact] public void ThePage_IsEmbedded_AndIsNotEmpty()
[Fact] public void ThePage_NamesNoCataloguePath_AndNoSettingLabel()
[Fact] public void ThePage_CallsGetAndPutSettings()
[Fact] public void ThePage_LoadsNothingFromTheInternet()     // no <script src="http…">, no CDN, no font URL
```

- [ ] **Step 2: Write the page.** One file: HTML, one `<style>`, one `<script>`. No framework, no build
  step, no external request — it is served from loopback to a browser that may have no internet, and a CDN
  reference would make the settings page fail in the one situation it exists for. It fetches
  `GET /settings`, renders one section per category and one row per definition keyed on `renderer`
  (`Toggle`/`Choice`/`Number`/`Text`/`OrderedList`/`ReadOnly`), shows the origin label and the restart
  label beside each value, offers Reset per row, and `PUT`s one path at a time with the token from a field
  the owner fills in. Under D4 it says plainly, at the top, that editing needs `web.token` set.
  **Dark, matching the app** — the WPF app ships its own dark theme for a reason and a white page is a
  different product.

- [ ] **Step 3: Embed it, with the reason for the single copy.** In `AIOrchestratorCoreLib.csproj`,
  beside the grammar and the presets, with a comment saying that unlike those two this one is **embedded
  only**: the grammar and the presets ship as `Content` as well because a bash tool and an installer read
  them from disk, and their two copies are kept honest by a byte-equality test. The page has one consumer
  — the listener — so a second copy would have nothing to keep it honest and would be the drift that
  test exists to catch. (D14.)

- [ ] **Step 4: Verify, and then look at it.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsPageAsset"
```
Then open `http://127.0.0.1:7391/` against a running daemon or app and **write the checklist result into
the report**: every category rendered, a toggle flipped and the origin label changed to "set here", a
Reset returned it, a refused value showed the catalogue's message, and the page was legible at a phone
width. This is the only verification this file has and an unreported one is no verification.

- [ ] **Step 5: Commit.** `feat(web): the settings page, rendered entirely from the catalogue it is served`

---

### Task 9: The WPF window becomes generic

**Files:**
- Modify: `AIOrchestrator/SettingsWindow.xaml`, `AIOrchestrator/SettingsWindow.xaml.cs`
- Create: `AIOrchestrator/Views/SettingRowTemplate_Selector.cs`
- Modify: `AIOrchestrator/MainWindow.xaml.cs` (the constructor call at line 438, if its arguments change)

**Blocked by D1, D5, D11 and D12.**

**Interfaces:**
- Consumes: Task 1's `SettingsRow_Builder` and `ISettingReading`; Task 2's `Settings_Writer`; `ISupervisionPaths`; `IOrchestratorConfigProvider`.
- Produces: nothing CoreLib depends on. **This task adds NO logic to `AIOrchestrator/`** — see the Global
  Constraint. If a `switch` on `SettingKinds` or a string-building method appears in this project, it is in
  the wrong project and belongs in Task 1's formatter.

- [ ] **Step 1: There is no failing test to write, and the plan says so rather than pretending.** The test
  project is `net10.0` and cannot reference this project; there is no UI harness. **What replaces the red
  is Task 1's coverage plus a written checklist**, and the checklist is written FIRST, in this step, into
  the report's draft — before any XAML — so it is a specification and not a description of whatever got
  built:

  1. Seven tabs, in catalogue order, plus Connection.
  2. The `Kit` tab renders empty with a line saying it fills in a later plan, and does not throw.
  3. `Models` shows twelve rows; `Kernel` thirty-seven; the counts match `SettingsCatalog.In_Category`.
  4. A Toggle flips and the origin label changes from "from preset classic" / "shipped default" to "set here".
  5. A per-row Reset returns the value AND the origin label.
  6. A Number row out of range shows the catalogue's own message and writes nothing.
  7. A Choice row offers exactly `EnumValues`, and a nullable one also offers the null meaning.
  8. An OrderedList row offers the known words as a picker with up/down, never free text (D15).
  9. A ReadOnly row (`repos`, `planBackend`, the three `session.*`) is not editable and says where it IS changed (D12).
  10. The Connection tab still saves a bot token, and the two ids on it are catalogue rows (D11).
  11. The active preset name is in the window's header (D13).
  12. Under D5: no Cancel button on the catalogue tabs; edits are already saved when the window closes.

- [ ] **Step 2: Rewrite the XAML.** A `TabControl` bound to `SettingsRow_Builder.Build_Sections`; each tab
  an `ItemsControl` over its rows; a `DataTemplateSelector` keyed on `ISettingReading.Definition.Renderer`
  choosing among six `DataTemplate`s (CheckBox, ComboBox, numeric TextBox with the range shown, TextBox,
  two-list picker with up/down, read-only text). Label, description, origin label and restart label come
  from the reading — the XAML binds, it does not compute.

- [ ] **Step 3: The code-behind.** Construct the readings, commit an edit through `Settings_Writer`,
  re-read the one row on success or show the refusal message on failure. The Connection tab keeps its
  token field and its own Save through `OrchestratorConfig_Loader.Save` (D11, D5). **The old 13-argument
  `OrchestratorConfig_Factory.Create` call goes away entirely** — that shape is precisely how a config
  object gets rebuilt with some blocks defaulted and others dropped, and with a generic writer there is no
  reason to rebuild a config object to change a setting.

- [ ] **Step 4: Verify — build clean, then smoke by hand.**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet build AIOrchestrator/AIOrchestrator.csproj -c Debug -warnaserror:CS 2>&1 | tail -5
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsPresentation|FullyQualifiedName~SettingsWriting"
```
Expected: `0 Warning(s)` `0 Error(s)`, and the CoreLib layers still green.

Then run the checklist from Step 1 against the **freshly built binary**, and in the report state — in
these terms — which copy you ran and whether the owner's running app is still the fourth copy
(`Get-Process AIOrchestrator | Select Path`, CLAUDE.md decision 23). A checklist run against a stale
`bin\Debug - Copia\` is a checklist about last week.

- [ ] **Step 5: Commit.** `feat(app): the Settings window is generic over the catalogue`

---

### Task 10: The gate — every setting reaches every renderer

**Files:**
- Create: `AIOrchestratorCoreLib.Tests/Configuration/EverySettingReachesEveryRendererTests.cs`
- Create: `docs/superpowers/plans/2026-09-12-settings-renderers-04-report.md`

**Interfaces:**
- Consumes: everything Tasks 1-9 produced.
- Produces: the phase-4 gate evidence of spec §10 and the §12 drift mitigation.

- [ ] **Step 1: The one test this whole plan exists to make possible.** Spec §12: *"A setting that exists
  in three renderers and one probe is the shape most likely to drift. Mitigation: the catalogue test that
  every definition has a renderer hint and appears in each renderer's smoke test."*

```csharp
/// <summary>
/// SIXTY-SIX SETTINGS, THREE RENDERERS, ONE LIST. This is the test the design is for: it walks
/// SettingsCatalog.ALL and asserts that each definition reaches each surface, so a future catalogue entry
/// cannot appear in two renderers and be invisible in the third — the failure mode nobody would notice,
/// because each renderer looks complete on its own.
///
/// <para>
/// IT COVERS THE WPF RENDERER'S CONTENT AND NOT ITS APPEARANCE, and that limit is the reason
/// SettingsRow_Builder lives in CoreLib rather than in the app project: the test project is net10.0 and
/// cannot reference a net10.0-windows project, so the only WPF thing testable here is the thing the
/// window BINDS to. The appearance is a manual checklist in Task 9's report and it is not pretended
/// otherwise anywhere.
/// </para>
/// </summary>
[Fact] public void EveryDefinition_ProducesExactlyOneWpfRow()
[Fact] public void EveryDefinition_AppearsOnExactlyOneTelegramMenuPage()
[Fact] public void EveryDefinition_AppearsInTheWebGetResponse()
[Fact] public void EveryDefinition_HasARendererHint_AndNoneIsDrawnByAFallback()

/// <summary>
/// AND ALL THREE PRINT THE SAME WORDS. One formatter, one origin label, one restart label (decision 12).
/// A WPF label reading "30 minutes", a Telegram button reading "30 min" and a web cell reading "30" would
/// each look right in its own file.
/// </summary>
[Fact] public void AllThreeRenderers_ShowTheSameValueText_ForEveryDefinition()
[Fact] public void AllThreeRenderers_ShowTheSameOriginLabel_ForEveryDefinition()

/// <summary>
/// AND ALL THREE REFUSE THE SAME THINGS, for the same reason and with the same message — because none of
/// them decides: Settings_Writer asks the definition (CLAUDE.md decision 21).
/// </summary>
[Fact] public void EveryReadOnlyDefinition_IsUneditableInAllThree()
[Fact] public void AnInvalidValue_IsRefusedIdenticallyByTheTelegramTap_TheWebPut_AndTheWindowsCommit()
```

- [ ] **Step 2: The three round-trips of spec §9.** *"WPF view-model → `Save` → reload shows the value and
  origin; `/settings` tap → `Save`; `PUT /settings` → `Save`; and the inverse: an unknown path or invalid
  value is refused with the catalogue's message and nothing is written."* Three `[Fact]`s, each writing
  through its own renderer's entry point and re-reading through `SettingsSnapshot_Reader.Read_All_FromDisk`,
  asserting BOTH the value and the origin. The origin half is the one that catches a write landing under
  the wrong spelling or the wrong layer.

- [ ] **Step 3: The suite, once, whole, alone.** Close every other worktree's test run first.

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-plan04
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug 2>&1 | tee /c/Users/Gianpiero/source/repos/plan04-gate.log | tail -6
grep -E "^\s+Failed " /c/Users/Gianpiero/source/repos/plan04-gate.log | sort
dotnet test tools/claude-contract/ClaudeContract.Tests/ClaudeContract.Tests.csproj -c Debug 2>&1 | tail -4
dotnet build AIOrchestrator/AIOrchestrator.csproj -c Debug -warnaserror:CS 2>&1 | tail -5
```
Expected: **0 failed**, plus the honest skips (the three live smokes; the web-host smoke if it could not
bind). This plan inherits no named reds. For every name in the grep output, re-run it alone with
`--filter` before calling it real, and record: the name, whether it passed alone, and which task's change
it traces to.

- [ ] **Step 4: Write the report.** At minimum:
  - **Per decision D1-D15: the answer, and who gave it.** A decision answered by the implementer rather
    than the owner or the coordinator is a finding, not a footnote.
  - **The measured catalogue size and per-category counts**, from Task 1 Step 1, against the 66 this plan
    assumed.
  - **Which renderer is covered how**, in the words of the "Honest testability" table, and specifically:
    that the WPF renderer has no automated coverage of its appearance, what the manual checklist found,
    and which copy of the binary it was run against (decision 23).
  - **Whether the web-host smoke ran or skipped**, and on which OS.
  - **Every line changed in `BridgeEngineModel.cs`** (Task 5) and the piece that moved out of it.
  - **D7's exemption**, exactly as implemented, and the test that proves it is narrow.
  - **What is editable and not yet obeyed** — every row whose `INERT_NOTE` plan 03 has not deleted — so
    nobody reads a settings page as a description of behaviour.
  - **Which copy you read** for every claim about the kit, the build output or the running app.
  - **Anything spec §8 asked for that this plan did not deliver**, and why.

- [ ] **Step 5: Commit.** `test(settings): every setting reaches every renderer — the plan-04 gate`

---

## PARKED

Found while reading for this plan; **none of it traces to an owner request**, so none of it is a task
(CLAUDE.md decision 22). One line each, written down so it is not lost, outside the denominator so it
cannot move the owner's bar.

- `RunnerConfigs_Json.Write` writes the whole `runners` and `printRunner` blocks with EFFECTIVE values on
  every `Save` — the same materialise-a-moving-default shape D1 is about, reached through a different key,
  and nobody has asked.
- `ConfigRepos_Reorderer.Persist_Order` and `ConfigRepoColor_Writer` do an unsynchronised read-modify-write
  of `config.json` and are deliberately left outside D10's lock.
- Cross-process config writes (the WPF app and the daemon on one machine) are unsynchronised by anything,
  before this plan and after it.
- `runners.<role>.permission_mode` and `runners.<role>.settings` are read and written by `RunnerConfigs_Json`
  and registered in no catalogue, so no renderer can show them — plan 03 parked the same line; it is still
  nobody's.
- `OrchestratorConfig_Factory.Create`'s two overloads now take fourteen-plus parameters and every new
  settings block adds one; `Create_WithStatusScreenshots` has already had the "silently dropped a block"
  defect once.
- `IOrchestratorConfig.TelegramInbound` is typed `TelegramInboundModes` while the parser is
  `TelegramInbound_Modes` — two spellings of one idea, harmless, nobody's.
- `Read_JsonObject_ForEditing` silently replaces an unparseable `config.json` with an empty object; Task 2
  adds a warning line, which is as far as this plan goes without a request.
- The spec worktree root carries `bash.exe.stackdump` and `grep.exe.stackdump`; the plan-02 worktree
  carries one too. Not ours, not now.

---

## Self-review (run by the plan's author, 2026-09-12)

Checked against spec §8 in full, §6.1's renderer-hint row, §6.2's origin and Reset rules, §9's round-trip
and drift bullets, §10 phase 4, and §12's four risks. Gaps found and recorded rather than papered over.

1. **§8 says all three renderers "write through the fork's merging `Save`", and none of them can.**
   `Save` assigns a fixed list of typed keys; there is no call to it that writes `phone.receipts`. What §8
   wanted is the MERGE property, which `SettingsJson_Path` over a freshly read tree delivers. Recorded as
   D6 rather than silently substituted, because it is the plan's largest deviation from the spec's own
   words and a reviewer must see it as a decision.
2. **§8.2's "one message that edits itself" is blocked on this tree by a 30-second floor between two edits
   of one message.** This is the finding most likely to derail the task if it is met during implementation
   instead of before: `Hold_UnlessThisMessageMayBeEdited` THROWS rather than waiting, and it throws inside
   the 2-second mirror tick, so a naive menu would not be slow — it would be broken, intermittently, in a
   way that reads as Telegram flakiness. D7 puts it in front of the coordinator with three exits and a
   recommendation, and the recommended exit changes a rate-limit rule the owner paid for with a real 429
   storm, so it is argued rather than assumed.
3. **§8.2's "reply with the value" step has no primitive in this tree and is the single most dangerous
   thing in the plan.** Three near-misses were checked and rejected as reuse candidates —
   `AnswerBinding_Decider` (binds a typed reply to an AGENT-asked decision, and only when exactly one is
   open), `PendingOwnerReply` (the opposite direction), and the `/model` `/effort` dials (which avoid the
   problem by putting the value on the command line). So it is new state that can EAT AN OWNER'S MESSAGE.
   D9 pins the window, the cancel, the acknowledgement and the precedence, and Task 5 has a test whose only
   job is to prove an expired step routes to the supervisor normally.
4. **The WPF renderer cannot be tested by this suite and that fact drove the architecture, not the
   reverse.** `AIOrchestratorCoreLib.Tests` is `net10.0` and `AIOrchestrator` is `net10.0-windows`; the
   test project cannot reference it and there is no STA harness. Rather than write an untestable window
   and call it covered, `SettingsRow_Builder` lives in CoreLib and the window binds to it — which is what
   lets the gate assert "every definition produces a WPF row" honestly. The limit is stated three times on
   purpose (the Global Constraint, the testability table, the gate test's own docstring), because a later
   reader who moves that class into the app project would silently delete a third of the gate.
5. **Five owner decisions and ten coordinator ones is a lot, and it is one fewer than plan 03's twelve
   plus its seven parked findings.** Four of the five owner decisions exist because the spec and the code
   disagree or because the spec left a security posture unstated; the fifth (D5) removes a button they
   press. The ten coordinator decisions are implementation judgements a coordinator can settle in an hour,
   and eight of them have a recommendation the author would defend without further evidence.
6. **The 66 settings and the per-category counts in this document are the AUTHOR'S count of
   `SettingsCatalog.cs` as it stood on 2026-09-12 while plan 02 Task 8 was open** (Models 12, Kernel 37,
   Phone 9, Receipts 2, Pulse 4, Owner 2, Kit 0). Task 1 Step 1 makes MEASURING them the first action of
   the plan rather than trusting this line, because plan 02's own report had not been written when this was
   counted.
7. **Nothing in this plan deletes an `INERT_NOTE`, and that is deliberate and slightly uncomfortable.**
   A renderer that offers `phone.receipts` on a build where plan 03 has not landed lets the owner change a
   value nothing reads. The alternative — gating the renderers on plan 03 — would make this plan
   un-startable while plan 03 carries four unanswered owner decisions. The compromise is honesty: the
   `INERT_NOTE` is part of the description, so every renderer already shows the sentence "READ BY NOTHING
   YET" to the owner, in the description field, without this plan writing a word of it. Task 10's report
   lists the rows.
8. **`Save` stops writing the four model keys (D1) is the one change here that alters existing behaviour
   the owner can feel**, and it is the one item plan 02 explicitly deferred to this plan. It is sequenced
   into Task 2 rather than Task 9 so that it lands with the writer that replaces it, not with the window
   that used to do it — a window rewrite and a persistence-semantics change in one commit would be
   impossible to review separately.
9. **§12's third risk — "`quiet` must reproduce the fork's phone exactly" — is untouched by this plan and
   that is correct.** Plan 04 changes no default and no preset; `PresetProbeTests` is not extended here.
   If a probe line moves because of this plan, something in Tasks 1-3 has written where it should have
   read, and the gate's full-suite run is what catches it.
10. **The `Kit` category is empty and all three renderers must survive it.** It is tested in Task 1
    (`TheKitCategory_ProducesAnEmptySection_AndDoesNotThrow`), in Task 4 (not offered as a menu category)
    and in Task 9's checklist. The day plan 05 fills it, none of the three needs a line of UI code — which
    is the claim §8.3 makes for the web page and which this plan extends to all three, because they share
    one row builder.
11. **Task ordering follows spec §10 phase 4 — "Telegram first (it works for both brothers from day one),
    then the web page, then the WPF tabs" — with the two shared layers hoisted in front of all three.**
    Hoisting them is not a deviation: they are what makes the phase-4 sentence "a new catalogue entry
    appears in all three renderers with no UI code" true, and building them inside the Telegram task would
    have made the web and WPF tasks refactors of it.
12. **Sequencing has one hard edge and it is stated twice:** Tasks 1, 2, 3, 6, 7, 8 and 9 touch no engine
    file and can run while plan 03 is in flight; Tasks 4 and 5 must be serialised against every
    engine-touching task of plan 03, because seven of plan 03's tasks and one of this plan's edit
    `BridgeEngineModel.cs`, which makes them parallel WRITERS on a shared file (decision 16).

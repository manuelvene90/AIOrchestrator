# Fork Merge — Plan 05: Kit delivery and per-user kit values — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make kit DELIVERY and the kit's PER-USER VALUES first-class — installable, verifiable, and configurable through the catalogue everything else now uses. Two people run this system: the owner on Windows with the WPF app, his brother headless on a Linux VPS. Today the kit ships as a local marketplace registration that the running binary verifies (CLAUDE.md decision 17), and three things inside it are per-user in ways nothing models: the platform codes a topic name starts with (a hard-coded table of the OWNER's OWN eighteen repos, pasted into three role skills and shipped to both machines), the owner's name (the literal word `Nathan`, in a house skill both brothers load), and the language rule (prose with no machine-readable home). This plan gives each of them a catalogue entry, one exported environment variable, and one place that builds it — then closes the delivery gaps the two installers and the two host csprojs have drifted into, and fills the `Kit` category that plan 02 shipped deliberately empty.

**Architecture:** Three small pieces and a lot of alignment. (a) `IRepoEntry` grows `Code` (and, under D2, sub-product codes), `Parse_Repos` reads it and `Save` carries it — without which a hand-written `code` is deleted the first time anything presses a button. (b) One new builder, `Kit/PerUserEnvironment_Builder`, turns an `IOrchestratorConfig` into the `AIORCH_OWNER_NAME` / `AIORCH_OWNER_LANGUAGE` / `AIORCH_PLATFORM_CODES` dictionary, and is called at **both** spawn points — `SpawnCommand_Builder.Build_SessionScript` (terminal, via `ISessionLaunch`, resolved at the launcher exactly as plan 02 Task 7 resolved effort) and `PrintTurnDispatcherModel`'s environment dictionary (bridge-driven, resolved from its own `_configProvider`). One builder, two carriers, so decision 12's "never a second copy of a fact" holds across the two runners. (c) The three role skills and the `subagents` house skill stop pasting the table and the name and read the variables instead; `PlatformCodesAreTaughtTests` reverses direction. Then: the installers stop diverging (install.ps1 still rebuilds `config.json` from a six-key list and would delete every key plans 02–04 added), the gate's refusal reaches a person rather than only a log line, and the two hosts ship the same kit folder.

**Tech Stack:** .NET 10 (`net10.0`, WPF app `net10.0-windows`), xUnit, `System.Text.Json.Nodes`, PowerShell 5.1, bash (msys on Windows), `jq`, `git`, the `claude` CLI's plugin commands.

**Spec:** `docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md` — **§7.7 in full** (delivery, jq on Windows, per-user values, the brevity ceilings), **§7.8** (language, and what `owner.language` replaces), the `Owner and kit` rows of **§6.4** (`owner.name`, `owner.language`, `repos[].code`), **§10 phase 5** (*"§7.7–7.8, the CLAUDE.md alignment (decisions 8, 11, 17 rewritten), the general-supervisor platform-codes export, the `README` for a new machine on each OS"*), **§11.3** (the installer asks for the preset on a fresh machine), and §9's two-OS bullet. This plan implements phase 5 and nothing else.

**Worktree:** create a fresh one off master once plans 02–04 have merged — `git worktree add ../AIOrchestrator-plan05 -b plan/05-kit-delivery master`. **This plan was AUTHORED in `C:\Users\Gianpiero\source\repos\AIOrchestrator-plan02` (branch `plan/02-settings-catalogue`) while two other agents held that worktree** — one editing code for a fix round, one writing plan 04. The author wrote this file and nothing else there, ran no git write command, no build and no test. Never commit to `master`; the owner merges.

**Which copy every statement here was read from (CLAUDE.md decision 18):**

| document | copy read |
|---|---|
| `CLAUDE.md`, `.claude/rules/code-conventions.md`, `git-and-boundaries.md`, `kit-and-scripts.md` | branch source, worktree `AIOrchestrator-plan02`, branch `plan/02-settings-catalogue` |
| the design spec (§1–§12 and both appendices) | branch source, worktree `C:\Users\Gianpiero\source\repos\AIOrchestrator-spec`, branch `feat/fork-merge-and-profiles-spec` |
| plan 02 (`…-settings-catalogue-02.md`) and plan 03 (`…-behavioural-seams-03.md`, commit `2ce8dcc`) | branch source, worktree `AIOrchestrator-plan02` |
| plan 04 (`…-settings-renderers-04.md`) | **NOT READ — it did not exist yet.** It was being written in the same worktree while this file was. Every statement below about plan 04 is derived from spec §8 and from plan 02's and plan 03's non-goals, never from plan 04's text. Re-read it before Task 1. |
| every `kit/**` file quoted below (`install.ps1`, `install.sh`, `README.md`, the seven hooks, the seven `skills/*/SKILL.md`, both manifests, both statusline scripts, both presets) | **branch source**, worktree `AIOrchestrator-plan02` |
| every production and test `.cs` file quoted below | branch source, worktree `AIOrchestrator-plan02`, read 2026-09-12 **while a fix round was in flight in that same worktree** — so anything under `Configuration/` may have moved since. Re-read `SettingsCatalog.cs`, `SettingsJson_Path.cs` and `OrchestratorConfig_Loader.cs` before Task 1. |
| the app's **build output** (`AIOrchestrator*/bin/…/kit/`) | **listed only** (to establish which files each csproj copies). No content claim below is about a build-output copy. |
| the **installed** kit (`~/.claude/plugins/…`, `~/.claude/commands`, `~/.claude/settings.json`) | **NOT READ.** No claim here is about the installed copy. |
| the **running app** (`Get-Process AIOrchestrator \| Select Path`) | **NOT READ.** See "What cannot be verified without a human" — this is the one plan of the five where that matters most. |

---

## Depends on plans 02, 03 and 04

- **Plan 02 must be MERGED.** This plan consumes `SettingsCatalog` (`ALL`, `Find_OrNull`, `In_Category`, `Build_Owner`'s two rows), `SettingDefinition_Factory`, `SettingCategories.Kit`, `SettingsJson_Path`, `Settings_Resolver`, `Presets_Loader` and the two `kit/presets/*.json`. Task 1 also DELETES an exemption plan 02 put in by name (`SettingsCatalogTests.EveryCategory_ExceptKit_HasAtLeastOneEntry`), which is only a coherent act once plan 02's own tests are the baseline.
- **Plan 03 must be MERGED.** No file is shared with it in production — plan 03 owns `Bridge/`, `Telegram/` and `Configuration/PhoneSettings|PulseSettings/`; this plan owns `Kit/`, `Spawning/`, `Running/PrintTurnDispatcher/`, `Configuration/RepoEntry/` and `kit/`. The dependency is the SUITE: plan 03 Task 11 is the `Bridge/` flakiness campaign, and every verification step below is a `--filter` run whose red must be legible. **If plan 03 Task 11 has not landed, say so in the report and treat any `Bridge/` red as plan 03's, not as this plan's.**
- **Plan 04 should have merged, and Task 1 changes shape if it has not.** Plan 04 gives every catalogue definition a renderer. `repos[].code` is the first catalogue path that is not a scalar at a dotted path (it names a field of an ARRAY ELEMENT), so it is the first entry a renderer cannot draw from the definition alone. Task 1 registers it as `Composite` + `ReadOnly` precisely so plan 04's renderers list it and do not try to edit it; **if plan 04 has already shipped a repo editor, Task 1 Step 4 must wire the code field into that editor instead of asserting `ReadOnly`.** Check first; do not assume.
- **This plan inherits NO named reds.** Plan 02 Task 8 closed the four `OrchestratorConfigFactoryTests` cases plan 01 held open by ruling. If any of them is red when Task 1 starts, stop and say so.
- Task order: **1 → 2 → 3** are a chain (the catalogue value, the export, the prose that reads it) and must be serialised. **4, 5, 6, 7** are independent of that chain and of each other by file set — 4 and 5 both edit `kit/install.ps1` and must be serialised with each other. **8** depends on 1–7 having landed. **9** is the gate.

---

## Non-goals — say no to these out loud

Every one of these is a different plan, or a decision already taken. A task that finds itself editing the files below has left its scope.

- **The three renderers (plan 04).** No WPF Settings window change, no `/settings` Telegram command, no `HttpListener`. Task 1 REGISTERS catalogue entries; drawing them is plan 04's.
- **Every behavioural seam (plan 03).** No `Mirror_Append_Async`, no `OwnerPush_Policy`, no `TopicStatusLine_Builder`, no `TopicCommandButtons`, no receipts, no glyphs. `phone.*`, `pulse.*` and `topic.*` are not touched here.
- **Re-porting the translation layer.** Settled and gone (owner, 2026-09-12). `owner.language` is its REPLACEMENT, not its return: this plan exports a variable and edits prose, and adds no `Translate_*` call anywhere. CLAUDE.md decision 11's "still open" paragraph is stale as of that ruling; Task 8 Step 5 says so rather than acting on it.
- **Changing the brevity ceilings.** §7.7's last bullet is explicit: 5 lines / 600 characters stay GLOBAL in `kit/grammar/channel-grammar.json`, with no divergence to configure, and the byte-equality test between the bash and .NET copies stays. Do not register them.
- **The `bg` runner, a POSIX terminal runner, or a Mac app.** Spec §3. The terminal spawn path is `wt.exe`/PowerShell and stays Windows-only in this plan; Linux reaches sessions through the print and stream runners. Task 2 wires BOTH, which is what makes that acceptable.
- **Bumping `KitPlugin.EXPECTED_VERSION`.** `kit/README.md` states the rule: bump it when the CONTRACT between host and protocols changes, and leave it alone for ordinary protocol edits, because the content check catches those. Tasks 3 and 4 edit protocol prose and hook behaviour; neither changes the contract. **If an implementer thinks a bump is needed, that is a decision to raise, not to take** — `KitPluginIdentityTests` holds the constant equal to `plugin.json`, so a bump is two files or a red suite.
- **Splitting `BridgeEngineModel.cs`.** No task here touches it.
- **Anything this plan finds wrong that nobody asked about.** Decision 22: one line in `## PARKED`, outside the denominator.

---

## Global Constraints

- **`jq` lives only on a login shell's PATH on this machine.** Every task that runs `dotnet` must first run, in the same bash invocation:
  `export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"`
  Without it the 13 statusline parity fixtures fail and you will diagnose a kit change from their noise. **Task 4 is about this exact fact and must not "fix" it by making the tests tolerant.**
- **`AIOrchestratorCoreLib` is strict** (`.claude/rules/code-conventions.md`): the triple `IXxx` + `internal sealed class XxxModel : IXxx` + `static Xxx_Factory`; `Xxx_Yyy.cs` with the underscore only when the second word is a role (`_Builder`, `_Reader`, `_Parser`, `_Factory`, `_Verifier`, `_Remover`, `_Store`); methods `Verb_Object[_Modifier]`; anything that may not resolve ends `_OrNull`; get-only properties from a primary constructor; **no `record` types**; ad-hoc multi-value returns are value tuples; XML docs argue the WHY with dated incidents. Tests: xUnit `[Fact]`, folders mirror production namespaces 1:1, class `<Subject>Tests` with the underscore stripped, methods `Verb_Scenario_Outcome`, **stubs not mocks**.
- **`.claude/rules/kit-and-scripts.md` governs every `kit/**` edit**, and three of its lines bind hard here: *"the role protocols are Manu's method: reorganise, never rewrite a rule"*; shell is `#!/usr/bin/env bash` + `set -euo pipefail` with macOS/BSD portability (`md5sum` → `md5 -q`, `date -d` is GNU-only, `stat -c` → `stat -f`, the lock is `mkdir` never `flock`); **no `python3` in the statusline** (decision 19).
- **NO BASH FROM C#.** `.claude/rules/code-conventions.md`: *"Three OS are first-class. No `#if` that excludes an OS from the daemon; `Path.Combine`; `ProcessStartInfo` portable; no bash required from C#."* Verified 2026-09-12: production starts bash NOWHERE (`AIOrchestratorCoreLib/`, `AIOrchestrator/`, `AIOrchestrator.Daemon/` contain no `"bash"` process start; `Bash_Locator` lives in `Tests/TestSupport` only). **D4 exists because §7.7 asks the bootstrapper to check jq, and the only honest check is one bash runs.**
- **NEVER run the full suite except at Task 9.** One suite at a time on this machine. A red under load is isolated with `--filter` and re-run alone before it is believed; compare the SET OF NAMES against the known set, never the count.
- **Say which copy you read, in every report, for every kit claim** — branch source (`kit/…`), build output (`*/bin/…/kit/`), installed (`~/.claude/plugins/`, `~/.claude/commands`, `~/.claude/settings.json`), or the RUNNING app's own folder. This is the plan where decision 18 earns its keep five times over: every assertion this plan's tests make is about **branch source**, and a green suite says nothing about the other three.
- **Decision 23 is the central hazard of this plan and it is named again in Task 6 and Task 9.** `KitAssets_Bootstrapper` runs its verdict at STARTUP, in the RUNNING binary. A kit edit is not verified by editing `kit/` and re-running the installer: neither the marketplace registration nor a `git diff` on `kit/` tells you what verdict the currently-running process already recorded. **Before believing anything you built is live: `Get-Process AIOrchestrator | Select Path`.**
- **Decision 21 — the app enforces at the point of effect; a check that cannot evaluate its predicate SAYS SO and allows.** Task 4's jq check and Task 6's refusal both live under this rule. Neither may invent a denial it cannot justify, and neither may grant silent consent: the line goes to `orchestrator.log.jsonl`, naming WHICH predicate failed and why.
- **Decision 20 — a harness that cannot find what it tests must refuse to run**, and **never assert on a state with two routes to it.** Task 3 reverses the direction of a content test; the reversed test must fail when the export is missing AND fail when the prose stops naming it, by two separate cases, not one case that passes for either reason.
- **Decision 15 — an alert the owner cannot act on does not go to Telegram.** Task 6's refusal is the rare one they CAN act on (re-run the installer), which is why it is owner-facing; Task 4's jq notice is arguable and is D5.
- **Windows:** `python3` is native Windows Python and cannot open msys paths — hand it Windows paths. Bash heredocs over ~6 KB die as a fake quote error; write the script with the Write tool and run the file. Quote `git show "ref:path"` whole. PowerShell 5.1 has no `&&`, no ternary, and mangles `-m` with here-strings.
- **Stage by explicit path**; never `git add -A` / `.` / `commit -a`; never `--no-verify`. Multi-line messages via `git commit -F <tempfile>`. One commit per task (or per defect inside a task). Every commit ends with:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## OPEN DECISIONS — answered BEFORE the named task starts

**Do not guess.** Each row names the task it blocks and whose decision it is. The recommendation is what the plan's author would do and why; it is not an answer.

**D1 — May this plan edit `CLAUDE.md`? Blocks Task 8. THE OWNER'S.**
Spec §10 phase 5 lists *"the CLAUDE.md alignment (decisions 8, 11, 17 rewritten), … honoured by doing this on master, once"* as part of this phase. But `.claude/rules/git-and-boundaries.md` retires that prohibition with a caveat, and **CLAUDE.md decision 26 flags the contradiction by name**: *"`git-and-boundaries.md` says 'Do not modify … `CLAUDE.md` — they are Manu's' … yet this very decision (and 8, 11, 17, 23) were written directly into `CLAUDE.md` by task on the owner's own instruction. Do not read that rule as still binding on `CLAUDE.md` without asking — it needs the owner's explicit call."* Meanwhile the work spec §10 assigns is **already done**: decisions 8, 11, 17, 23 and 26 all carry post-merge rewrites in the copy read for this plan, and decision 11's "still open" paragraph was overtaken by the owner's 2026-09-12 translator ruling.
*Decision:* (a) this plan edits `CLAUDE.md` to close the two residual staleness points (decision 11's open question, and decision 26's own contradiction); (b) it reports them and edits nothing. **Recommended: ask.** If the answer is (a), the edit is two paragraphs and no more — this plan must not become a CLAUDE.md rewrite.

**D2 — Do sub-product codes live in `repos[]`, and how? Blocks Task 1. THE OWNER'S.**
`Platform_Abbreviations.ALL` carries eighteen codes, **seven of which are SUB-PRODUCTS with a parent** (`IS`→`SL`, `PB`→`SL`, …), and the owner's own clarification is quoted in its docstring: *"if I say I want to work on IS the general supervisor should understand that I mean on SL, and the topic name should still indicate IS."* A sub-product is NOT a repo — there is no `repos[]` entry for Invest Studio — so `repos[].code` alone cannot express it, and spec §6.4 registers only `repos[].code`.
*Three shapes:* **(a)** `repos[].code` plus `repos[].subProducts: [{code, name}]`, so one repo entry carries its own family and `Resolve_RepoCode_OrNull` reads config instead of a constant; **(b)** `repos[].code` only, and sub-products are dropped from the exported line — the general supervisor then asks when the owner says "IS", which is a regression in a behaviour the owner asked for twice; **(c)** `repos[].code` only, with `Platform_Abbreviations` kept as a shipped fallback table when no repo states a code — which ships the owner's eighteen private codes to his brother's VPS for ever. **Recommended: (a)**, because it is the only shape that keeps the owner's stated behaviour and removes his private table from the other machine's kit. It costs a nested array in `Parse_Repos` and `Save`.

**D3 — What happens to `Platform_Abbreviations` once the codes are data? Blocks Task 1 and Task 3. THE COORDINATOR'S.**
Today the class has **no production caller at all** — `Describe_Legend()` and `Resolve_RepoCode_OrNull()` are read only by `PlatformCodesAreTaughtTests` and `RoleCommandMarkerTests`; the codes reach a session only by being PASTED into three `SKILL.md` files.
*Decision:* **(a)** delete the constant table, move `Describe_Legend` and `Resolve_RepoCode_OrNull` onto the repo list (the class becomes a pure formatter over `IReadOnlyList<IRepoEntry>`), and the owner's codes move into his `config.json` — a one-time migration he performs, or the installer offers; **(b)** keep the table as a seed the installer WRITES into `config.json` on a fresh machine, then never reads again; **(c)** keep it as a runtime fallback (see D2(c)). **Recommended: (a) with (b)'s seeding offered by `install.ps1` only** — the codes are the owner's, the installer is the place a machine is told who it belongs to, and a fallback that runs at runtime is a second source of truth by another name (decision 12). Note that (a) makes `RoleCommandMarkerTests.NOT_MARKERS`, which splices `Platform_Abbreviations.ALL`, read the config-shaped list instead; that test's whole point is not to hand-copy eighteen codes, so it must splice whatever the new single source is.

**D4 — How does the host check for `jq`, given that production may not start bash? Blocks Task 4. THE COORDINATOR'S.**
§7.7: *"the bootstrapper checks `jq` beside the plugin and records a verdict that reaches General once."* But the question that matters is **"is jq on the PATH that BASH sees"** — `kit/bin/channel-append.sh` runs in msys bash on Windows, and on this very machine jq is reachable from a login shell and not from the app's own environment (see Global Constraints). A `Process.Start("jq")` from C# answers a different question and would be confidently wrong in both directions. And `.claude/rules/code-conventions.md` says no bash is required from C#.
*Three ways:* **(a)** probe `bash -lc 'command -v jq'` from the bootstrapper, treating "no bash found" as CANNOT TELL (logged, never a denial — decision 21's corollary), and accept that this is the first bash the app runs; **(b)** do not check from the host at all: `channel-append.sh` already refuses in one line naming the fix, and `BothInstallersCheckForJqTests.TheToolRefusesInOneLineWhenJqIsMissing` pins that refusal — so the only thing missing is the installer half, which Task 4 delivers anyway; **(c)** check the HOST's own PATH and label the answer as weaker than the question in the line itself. **Recommended: (b), with (a) as a second commit if the owner wants the early warning** — (c) is the shape that produces a confident wrong answer, which is the failure mode decisions 18 and 20 were both written after.

**D5 — Does a missing-jq notice go to the OWNER or only to the supervisor channel? Blocks Task 4 (only under D4(a) or (c)). THE COORDINATOR'S.**
§7.7's wording is *"reaches General once"*, and General is mirrored to the phone. Decision 15 says an alert the owner cannot act on does not go to Telegram — but this one they CAN act on (`winget install jqlang.jq`). Against: on a machine where Task 4's installer half has run, the notice can only fire when the install FAILED, which is already printed by the installer they just ran.
*Decision.* **Recommended: agent-facing** (`AppEntryAudiences.Agent`, the `[agent]` tag), like `RECOVERY_SUBJECT` already is — the general supervisor is the thing that must stop promising typed entries, and the owner learns it the moment they try.

**D6 — Do `grammar/` and `presets/` join the content digest? Blocks Task 7. THE COORDINATOR'S.**
`KitContent_Digest.DIGESTED_FOLDERS` is `["skills", "hooks", "bin"]` plus `.claude-plugin/plugin.json`. `kit/grammar/channel-grammar.json` is **read by a session** — `channel-append.sh` resolves it at `../grammar/channel-grammar.json` relative to itself and refuses typed entries without it — so a grammar drift is exactly the class of drift the digest exists to catch, and it is currently invisible to it. `kit/presets/*.json` is read by the HOST, not by a session, and is also embedded in the CoreLib assembly (so the host's copy cannot go missing).
*Decision:* **Recommended: add `grammar`, leave `presets` out**, and say in `KitContent_Digest`'s own doc why the two split — "what a session reads" is the digest's stated subject, and presets are not that. Note the cost: adding a folder to the digest means every machine's next startup reports `ContentMismatch` until it reinstalls, because the installed copy's digest changes. That is one reinstall, and it is the correct outcome; **it must be in the report and in the README so the owner is not surprised by a refusal to spawn.**

**D7 — Does `install.ps1` install jq, or only tell the owner how? Blocks Task 4. THE OWNER'S.**
§7.7 is explicit: *"`install.ps1` installs it (`winget install jqlang.jq`) instead of warning."* But `install.ps1`'s current comment argues the opposite on purpose and dates it: *"A WARNING, NOT AN EXIT, and the difference from install.sh is deliberate … a machine without it still gets a working app, working role commands, and untyped appends."* Installing a package the owner did not ask for, from a setup script, is a different kind of act from warning — and `winget` prompts for an agreement on a fresh machine, which a scripted run cannot answer.
*Decision:* **(a)** run `winget install jqlang.jq --accept-source-agreements --accept-package-agreements` when jq is absent, report what happened, and fall back to the current warning if winget is absent or the install fails; **(b)** offer it (`Read-Host "Install jq now? [Y/n]"`), install on yes; **(c)** keep the warning. **Recommended: (b)** — it delivers §7.7's intent (the machine ends up with jq) without a setup script silently installing software, and it degrades to (c) on a non-interactive run, which is what `install.sh` already does for its own prompts (`if [ -t 0 ]`).

**D8 — Does the gate's refusal reach Telegram directly when there is no general channel? Blocks Task 6. THE OWNER'S.**
`KitAssets_Bootstrapper.Tell_Owner_Once` returns silently when `paths.GeneralChannelFile` does not exist — which is precisely a FRESH MACHINE, the one this plan exists to make installable. So on the machine most likely to have a bad kit, the refusal reaches the log and nothing else, and the general supervisor that would have read it is itself refused a spawn.
*Decision:* **(a)** create the general channel file if absent and append there (it is created by `GeneralChannel_Initializer.Ensure_Exists` at the first general spawn anyway); **(b)** hand the refusal to the bridge to send to the General topic directly, which needs a seam from the bootstrapper into the engine that does not exist; **(c)** leave it, and make the WPF app and the daemon console say it loudly at startup instead. **Recommended: (a) plus (c)** — (a) is three lines and reuses the existing path; (c) is the only surface a headless first boot with no Telegram configured has at all.

---

## File Structure

**Created**

```
AIOrchestratorCoreLib/Kit/PerUserEnvironment/
  IPerUserEnvironment.cs          ← the three values, resolved
  PerUserEnvironmentModel.cs
  PerUserEnvironment_Factory.cs
  PerUserEnvironment_Builder.cs   ← IOrchestratorConfig -> IReadOnlyDictionary<string,string>, the ONE builder
AIOrchestratorCoreLib/Kit/PerUserEnvironment_Names.cs
                                  ← AIORCH_OWNER_NAME / AIORCH_OWNER_LANGUAGE / AIORCH_PLATFORM_CODES, once

AIOrchestratorCoreLib/Sessions/PlatformCodes_Formatter.cs
                                  ← Describe_Legend over the REPO LIST (replaces Platform_Abbreviations' formatter half, D3)

AIOrchestratorCoreLib.Tests/Kit/PerUserEnvironmentBuilderTests.cs
AIOrchestratorCoreLib.Tests/Kit/BothSpawnPointsExportThePerUserValuesTests.cs
AIOrchestratorCoreLib.Tests/Kit/TheSkillsReadTheExportsTests.cs
AIOrchestratorCoreLib.Tests/Kit/BothHostsShipTheSameKitTests.cs
AIOrchestratorCoreLib.Tests/Kit/TheRefusalReachesAPersonTests.cs
AIOrchestratorCoreLib.Tests/Configuration/RepoEntry/RepoCodeSurvivesASaveTests.cs

docs/superpowers/plans/2026-09-12-kit-delivery-05-report.md    ← Task 9
```

**Modified**

| file | change | task |
|---|---|---|
| `AIOrchestratorCoreLib/Configuration/RepoEntry/IRepoEntry.cs`, `RepoEntryModel.cs`, `RepoEntry_Factory.cs` | `Code` (+ `SubProducts` under D2(a)) | 1 |
| `AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs` | `Parse_Repos` reads `code`; **`Save` writes it back** — without this a hand-written code is deleted on the first button press | 1 |
| `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs` | `Build_Kit()`: `repos[].code`, `owner.name` and `owner.language` MOVE or are cross-listed into `Kit`; the "Kit is empty in v1" paragraph is rewritten | 1 |
| `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsCatalogTests.cs` | `EveryCategory_ExceptKit_HasAtLeastOneEntry` → `EveryCategory_HasAtLeastOneEntry`; the by-name exemption is DELETED | 1 |
| `AIOrchestratorCoreLib/Running/SessionLaunch/ISessionLaunch.cs`, `SessionLaunchModel.cs`, `SessionLaunch_Factory.cs` | `PerUserEnvironment` carried like `Model` and `Effort` | 2 |
| `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` | resolves the per-user environment once per launch, beside the model and the effort | 2 |
| `AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs` | `Build_SessionScript` emits the resolved pairs; the values are VALIDATED or quoted, per `Validate_Model`'s precedent | 2 |
| `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` | the environment dictionary gains the same three, from `_configProvider` | 2 |
| `kit/skills/general-supervisor/SKILL.md`, `supervisor/SKILL.md`, `solo/SKILL.md` | the pasted 18-code legend → read `$AIORCH_PLATFORM_CODES` | 3 |
| `kit/skills/subagents/SKILL.md` | two sentences lose the literal `Nathan` | 3 |
| the six role `SKILL.md` files | one sentence for `$AIORCH_OWNER_LANGUAGE` (§7.8's rule), reorganised not rewritten | 3 |
| `AIOrchestratorCoreLib.Tests/Kit/PlatformCodesAreTaughtTests.cs` | direction reversed: the skills name the EXPORT; the app builds the legend from one source | 3 |
| `AIOrchestratorCoreLib.Tests/Kit/RoleCommandMarkerTests.cs` | `NOT_MARKERS` splices the new single source (D3) | 3 |
| `kit/install.ps1` | jq (D7); **`config.json` MERGED not rebuilt**; the `preset` prompt (§11.3); the stale suite-checkout build reminder | 4, 5 |
| `kit/install.sh` | the `preset` prompt, so the two stay in step | 5 |
| `AIOrchestratorCoreLib.Tests/Kit/BothInstallersCheckForJqTests.cs`, `InstallersRefreshAStaleCacheTests.cs` | widened to the new parity claims | 4, 5 |
| `AIOrchestratorCoreLib/Kit/KitAssets_Bootstrapper.cs` | `Tell_Owner_Once` no longer silently returns on a fresh machine (D8); the jq notice (D4) | 4, 6 |
| `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` | the refusal is stated ONCE per host run, not once per watchdog tick (decision 14) | 6 |
| `AIOrchestrator.Daemon/AIOrchestrator.Daemon.csproj` | the missing `kit/grammar/*.json` content item | 7 |
| `AIOrchestratorCoreLib/Kit/KitContent_Digest.cs` | `grammar` joins `DIGESTED_FOLDERS` (D6) | 7 |
| `kit/README.md` | "delete" → "move aside"; the fresh-machine recipe per OS; the D6 reinstall note | 8 |
| `.claude/rules/kit-and-scripts.md` | the normative-sentence-counter claim (see PARKED) | 8 |
| `AIOrchestratorCoreLib.Tests/Kit/TheSkillsQuoteTheAppsOwnNumbersTests.cs` | its docstring still cites `KitAssets_Installer` installing from the build output — retired by decision 17's rewrite | 8 |
| `CLAUDE.md` | **only under D1(a)** | 8 |

---

### Task 1: `repos[].code` becomes data, and the `Kit` category stops being empty

**Source:** spec §6.4 (`Owner and kit` table, row `repos[].code`) and §7.7 ("Per-user values"). Plan 02's self-review point 5 deferred it here by name, and `SettingsCatalogTests.EveryCategory_ExceptKit_HasAtLeastOneEntry` carries the exemption that this task retires — plan 02's own comment says *"naming it here means the day that lands, this test is the thing that notices the exemption is stale."*

**Files:**
- Modify: `AIOrchestratorCoreLib/Configuration/RepoEntry/IRepoEntry.cs`, `RepoEntryModel.cs`, `RepoEntry_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs` (`Parse_Repos`, `Save`)
- Modify: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs` (new `Build_Kit()`; the class doc's `Kit` paragraph)
- Create: `AIOrchestratorCoreLib/Sessions/PlatformCodes_Formatter.cs` (D3)
- Modify/Delete: `AIOrchestratorCoreLib/Sessions/Platform_Abbreviations.cs` (D3)
- Test: `AIOrchestratorCoreLib.Tests/Configuration/RepoEntry/RepoCodeSurvivesASaveTests.cs`
- Modify test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsCatalogTests.cs`

**Interfaces:**
- Consumes: `SettingDefinition_Factory.Create_Composite/Create_String`, `SettingCategories.Kit`, `RepoEntry_Factory`, `OrchestratorConfig_Loader.Save`.
- Produces, for Tasks 2 and 3:
  - `IRepoEntry.Code : string` (empty string when unstated, never null — a code is a word or the absence of one, and `IRepoEntry.TopicColor` is already the nullable one in this interface)
  - under D2(a): `IRepoEntry.SubProducts : IReadOnlyList<(string Code, string Name)>`
  - `PlatformCodes_Formatter.Describe_Legend(IReadOnlyList<IRepoEntry>) : string`
  - `PlatformCodes_Formatter.Resolve_RepoCode_OrNull(IReadOnlyList<IRepoEntry>, string code) : string?`
  - `SettingsCatalog.In_Category(SettingCategories.Kit)` non-empty

- [ ] **Step 1: Re-read the three files that moved under you**

Plan 02's fix round was in flight when this plan was written. Before touching anything, read the CURRENT `SettingsCatalog.cs` (its `Build_All` list and its class doc), `SettingsJson_Path.cs` (whether it grew any array support) and `OrchestratorConfig_Loader.cs` (`Parse_Repos` and `Save`). **Record in the report what had moved.**

- [ ] **Step 2: Write the failing save test FIRST — the hazard, not the feature**

The feature is a field. The DEFECT this task would otherwise ship is that `OrchestratorConfig_Loader.Save` rebuilds the `repos` array element by element from `IRepoEntry`:

```csharp
var reposArray = new JsonArray();
foreach (var repo in config.Repos)
{
    var repoObject = new JsonObject { ["name"] = repo.Name, ["path"] = repo.Path };
    if (repo.TopicColor != null)
        repoObject["topicColor"] = repo.TopicColor.Value;
    reposArray.Add(repoObject);
}
```

Everything else in `Save` merges onto the raw tree — its own docstring records that as a dated fix — but `repos` does not: an element key the model does not carry is gone on the next save. So a `code` the owner hand-writes into `config.json` survives exactly until they press a button in the Settings window. **That is the same failure `topicColor` was given a model field to avoid**, and it is why `Code` must be on `IRepoEntry` rather than left to the raw tree.

Create `AIOrchestratorCoreLib.Tests/Configuration/RepoEntry/RepoCodeSurvivesASaveTests.cs`:

- `AHandWrittenCode_SurvivesASave` — write a `config.json` with a repo carrying `code`, load, `Save`, reload, assert the code is still there.
- `ARepoWithNoCode_StaysExactlyAsCleanAsItWas` — the `topicColor` rule (brief F1): an absent code is NOT written as `""`.
- Under D2(a): `SubProductsSurviveASave` and `ARepoWithNoSubProducts_WritesNoArray`.
- `AnUnknownKeyOnARepoElement_IsStillLost_AndThatIsRecorded` — **honest negative**: any OTHER hand-written repo-element key is still dropped by `Save`. Assert it, so the limitation is documented by a test rather than discovered later, and name it in PARKED.

Run: `dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~RepoCodeSurvivesASave"` → expect red.

- [ ] **Step 3: Add `Code` to the triple, the parser and the writer**

- `IRepoEntry.Code` with an XML doc arguing the why: it is the **platform code the owner speaks and the topic name starts with** (owner, 2026-08-19, quoted in `Platform_Abbreviations`' current docstring), and it is per-user — two machines with the same app have different codes, which is the whole reason it moved out of a compiled table.
- `RepoEntry_Factory.Create` gains the parameter with a default, so existing call sites compile; **validate it**, because it travels through a shell command in Task 2: a code is letters, digits and `-` (the shipped table has `SK-C`, `SK-M`, `AI-Orch`). An invalid code is dropped to empty with a comment saying why that is the safe direction — this is external, hand-edited data, and `.claude/rules/code-conventions.md` puts parsers of untrusted data on the swallow-and-say-why side. **Do not throw:** `Parse_Repos` already skips a malformed entry silently and the app must start.
- `Parse_Repos` reads `code` (and D2(a)'s `subProducts`).
- `Save` writes it back, only when non-empty.

- [ ] **Step 4: Register the `Kit` category**

In `SettingsCatalog.cs`, add `Build_Kit()` to `Build_All()` and register:

| path | kind | renderer | why this shape |
|---|---|---|---|
| `repos[].code` | `Composite`, parser `AIOrchestratorCoreLib.Configuration.OrchestratorConfig_Loader` | `ReadOnly` (unless plan 04 shipped a repo editor — check) | it names a field of an ARRAY ELEMENT, which `SettingsJson_Path` cannot walk; `Composite` is the catalogue's existing word for "a structure whose named parser is the authority", and `repos` is already registered that way |
| `owner.name` | (existing `String`) | Toggle/Text as registered | **cross-listed, not moved** — see Step 5 |
| `owner.language` | (existing `Enum`) | as registered | same |

Rewrite the class doc's `Kit` paragraph: it currently says the category *"is empty in v1 … `repos[].code` and the per-user environment exports land there in plan 05"*. Replace it with what the category now holds and **why `repos[].code` is a Composite rather than a scalar** — a future reader will otherwise "fix" it into a dotted path that the reader cannot walk.

- [ ] **Step 5: Decide `Owner` vs `Kit` and state it, rather than moving rows silently**

Spec §6.4 puts all three in one table headed **"Owner and kit"**, and plan 02 registered `owner.name` / `owner.language` under `SettingCategories.Owner`. Moving them to `Kit` would empty `Owner` and re-break the invariant from the other side.

**The catalogue has one category per definition** (`ISettingDefinition.Category` is a single value), so a row cannot be in both. The decision this task takes, and states in the code: **`owner.*` stays in `Owner`; `Kit` holds `repos[].code`** and any later kit-delivery key. If `Kit` with one entry reads thin to a renderer, that is plan 04's layout problem and not a reason to shuffle a taxonomy. Write this reasoning into `Build_Kit`'s doc; it is exactly the kind of decision a later reader reverses for free if nobody wrote down why.

- [ ] **Step 6: Retire the exemption**

In `SettingsCatalogTests`, rename `EveryCategory_ExceptKit_HasAtLeastOneEntry` → `EveryCategory_HasAtLeastOneEntry` and delete the `if (category == SettingCategories.Kit) continue;` branch and the paragraph of the docstring that explains it. **Do not leave the branch with a different condition.** Plan 02 named the exemption so that this moment would be noticed; a renamed exemption is the noticing wasted.

- [ ] **Step 7: D3 — decide what happens to `Platform_Abbreviations`, and do it**

Under the recommendation (a): the constant table goes; `PlatformCodes_Formatter` takes `IReadOnlyList<IRepoEntry>` and provides `Describe_Legend` and `Resolve_RepoCode_OrNull`. Note that **`Describe_Legend`'s current output is byte-identical to the line pasted into three `SKILL.md` files** — that is what Task 3 removes, and Task 2 exports.

Move the docstring's owner quotations onto the new formatter verbatim. They are dated owner statements (2026-08-19 and the sub-product clarification) and they are the only record of why the codes exist at all.

- [ ] **Step 8: Verify**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-plan05
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~RepoCodeSurvivesASave|FullyQualifiedName~SettingsCatalogTests|FullyQualifiedName~PresetProbeTests|FullyQualifiedName~OrchestratorConfig|FullyQualifiedName~PlatformCodes|FullyQualifiedName~RoleCommandMarker"
```
Expected: 0 failed. `PresetProbeTests.EveryCatalogueEntry_ResolvesUnderBothPresets` is in the list on purpose — it skips `Composite` entries, so it should be UNAFFECTED; if it moves, the new entry was registered with the wrong `Kind`.

- [ ] **Step 9: Commit** — `feat(settings): a repo carries its platform code, and the Kit category is no longer empty`

---

### Task 2: One per-user environment, exported at both spawn points

**Source:** spec §7.7, "Per-user values": *"the launcher exports `AIORCH_OWNER_NAME`, `AIORCH_OWNER_LANGUAGE`, `AIORCH_PLATFORM_CODES` at **both** spawn points — `SpawnCommand_Builder.Build_SessionScript` (terminal) and `PrintTurnDispatcherModel`'s environment dictionary (headless) — following the `AIORCH_SOFT_BOUNDARY_CALLS` precedent (a settable value with a literal default in the script)."*

**Files:**
- Create: `AIOrchestratorCoreLib/Kit/PerUserEnvironment_Names.cs`, `Kit/PerUserEnvironment/` (the triple + `PerUserEnvironment_Builder.cs`)
- Modify: `AIOrchestratorCoreLib/Running/SessionLaunch/ISessionLaunch.cs` + model + factory
- Modify: `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs`
- Modify: `AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs`
- Modify: `AIOrchestratorCoreLib/Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs`
- Test: `AIOrchestratorCoreLib.Tests/Kit/PerUserEnvironmentBuilderTests.cs`, `BothSpawnPointsExportThePerUserValuesTests.cs`

**Interfaces:**
- Consumes: Task 1's `IRepoEntry.Code` and `PlatformCodes_Formatter.Describe_Legend`; `IOrchestratorConfig` (`Repos`, and the two `owner.*` values — see Step 2 on how a catalogue-only key is read).
- Produces:
  - `static class PerUserEnvironment_Names { OWNER_NAME, OWNER_LANGUAGE, PLATFORM_CODES }`
  - `PerUserEnvironment_Builder.Build(IOrchestratorConfig) : IReadOnlyDictionary<string, string>` — **the one place these three are computed**
  - `ISessionLaunch.PerUserEnvironment : IReadOnlyDictionary<string, string>`

- [ ] **Step 1: Write the failing builder tests**

`PerUserEnvironmentBuilderTests` pins what the dictionary is, not how it is spelled at a call site:

- `AnUnconfiguredMachine_ExportsNothingItCannotAnswer` — empty `owner.name` and an empty repo list produce **no key at all** for those two, rather than a key with an empty value. A skill's `${AIORCH_OWNER_NAME:-the owner}` default cannot fire against an exported empty string, which is exactly the `AIORCH_SOFT_BOUNDARY_CALLS:-35` precedent §7.7 names: the default lives in the SCRIPT and only works when the variable is absent.
- `TheCodesLine_IsOneLine` — `Describe_Legend`'s output contains no newline. It travels through a PowerShell single-quoted literal and a process environment; a newline in either is a different bug in each.
- `TheCodesLine_IsBuiltFromTheRepoList_NotFromAConstant` — two different repo lists produce two different lines. Guards D3's whole point.
- `owner.language = auto` — assert what `auto` exports. **Decide and state it:** `auto` is the fork's RULE ("write in the language they used"), not a language, so exporting the literal word `auto` is meaningful to a skill that reads it and is NOT a language tag. Pin the word.
- `AValueCarryingAQuoteOrASemicolon_IsRefusedOrNeutralised` — see Step 4.

- [ ] **Step 2: Read the two `owner.*` values — and notice they have no `IOrchestratorConfig` property**

Plan 02 registered `owner.name` and `owner.language` in the catalogue and wired them to NOTHING: its own descriptions say *"Nothing reads this key until plan 05 exports it."* They are catalogue rows over the raw tree, not fields on `IOrchestratorConfig`.

Two ways, and this task takes the second:
- (a) add two properties to `IOrchestratorConfig` and parse them in the loader, like `voiceTranscribeCommand`;
- (b) read them through `Settings_Resolver` against the same preset/config trees the loader already holds, in a small `OwnerSettings_Json.Parse(configRoot, presetTree)` shaped exactly on plan 02 Task 7's `EffortSettings_Json` and plan 03 Task 1's `PhoneSettings_Json`.

**(b), and the reason is precedence:** (a) skips the preset rung, so a preset could never carry an owner value and the four-layer chain would have a hole in it that nothing declares. `EffortSettings_Json` has deliberately no `Write`; copy that too — a written-back value becomes a stated value, which is how the owner's live `config.json` came to pin a stale model (CLAUDE.md's model-ladder bullet, entry 216).

- [ ] **Step 3: Resolve ONCE per launch, in the launcher**

`OrchestrationLauncherModel` already resolves the model and (since plan 02 Task 7) the effort at launch time, because it is the one place holding both the config provider and the per-orchestration overrides. Resolve the per-user environment there too and put it on `ISessionLaunch` beside `Model` and `Effort`.

**Do not pass the config provider into `SpawnCommand_Builder`.** It is a pure static builder with no provider today, and plan 03's Global Constraints state the rule in the same words for its own builders: resolve at the call site, pass the value.

- [ ] **Step 4: Emit it in the terminal script — and treat the values as hostile**

`Build_SessionScript` today is:

```csharp
$"$env:AIORCH_ROLE='{role}'; " +
$"$env:AIORCH_ID='{orchId}'; " +
$"$env:AIORCH_MEMBER='{memberId}'; " +
$"Set-Content -LiteralPath '{pidFilePath}' -Value $PID; " + claudeCommand;
```

…base64-encoded into a `wt.exe -EncodedCommand`. The three existing values are app-generated; **all three new ones are owner-typed text**, and the codes line contains backticks and `·` by construction. `SpawnCommand_Builder`'s own docstring records what happened the last time an owner-typed word went into this script unprotected: *"until 2026-09-10 it was the only part of this script that went in unprotected … A value carrying a quote or a semicolon would have become PowerShell in every session spawned with it."*

Follow that precedent exactly: **validate, do not quote** — a single-quoted PowerShell literal is ended by a single quote in the value, and doubling it is a second escaping rule to get wrong. `owner.name` and each repo code get an alphabet (letters, digits, space, `-`, `_`, `.` for the name; letters, digits and `-` for a code); anything outside it is DROPPED by the parser at Task 1 Step 3 and by `PerUserEnvironment_Builder` as a second lock on the same door. **The codes LINE is assembled by us from already-validated codes plus our own separators**, so it needs no validation of its own — state that in the doc, because it is the non-obvious half.

- [ ] **Step 5: Emit it in the bridge-driven dispatcher**

`PrintTurnDispatcherModel` builds its environment dictionary inline (`AIORCH_ROLE`, `AIORCH_ID`, `AIORCH_MEMBER`, `AIORCH_RUNNER`, `AIORCH_SUPERVISION_ROOT`). It holds `_configProvider`, so it calls `PerUserEnvironment_Builder.Build(_configProvider.Get_Current())` and merges. No shell is involved here — the values go into a process environment directly — so **the validation above is not load-bearing on this path and must not be relied on as if it were**: say so in the comment, or a later reader will remove it from the builder and break only Windows.

- [ ] **Step 6: The cross-runner test**

`BothSpawnPointsExportThePerUserValuesTests` is the decision-12 guard — one fact, two carriers:

- `TheTerminalScript_CarriesEveryNameTheBuilderProduced` — build a launch with a populated config, `Decode_SessionScript`, assert every key/value pair appears.
- `TheDispatcherEnvironment_CarriesEveryNameTheBuilderProduced` — drive the dispatcher's environment construction (follow `EffortDialOnABridgeDrivenSupervisorTests`' harness; **do not register a print session in a full engine test** — `BridgeEngine_Factory` hard-wires the real per-OS `claude` and a test that registers one can spawn a LIVE process).
- `NeitherSpawnPoint_KnowsAnyNameTheOtherDoesNot` — iterate `PerUserEnvironment_Names` and assert both paths carry the same SET. This is the case that fails the day someone adds a fourth variable to one side.

**Two separate cases, not one that passes for either reason** (decision 20's second half).

- [ ] **Step 7: Verify**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~PerUserEnvironment|FullyQualifiedName~BothSpawnPointsExport|FullyQualifiedName~SpawnCommandBuilderTests|FullyQualifiedName~OrchestrationLauncherTests|FullyQualifiedName~PrintTurn"
```
Expected: 0 failed. `SpawnCommandBuilderTests` has cases that assert the decoded script's exact text — they will need the new lines and **their expected values must MOVE, never be loosened to a substring match**.

- [ ] **Step 8: Commit** — `feat(kit): the owner's name, language and platform codes reach a session, on both runners`

---

### Task 3: The skills read the exports; the pasted table and the literal name go

**Source:** spec §7.7 (*"The skills read them; the hard-coded name and table go"*), §7.8 (the language sentence and the two stale "even when the traffic is Italian" lines), §10 phase 5 (*"the general-supervisor platform-codes export"*).

**Files:**
- Modify: `kit/skills/general-supervisor/SKILL.md`, `kit/skills/supervisor/SKILL.md`, `kit/skills/solo/SKILL.md` (the legend)
- Modify: `kit/skills/subagents/SKILL.md` (two sentences carrying `Nathan`)
- Modify: the six role `SKILL.md` files (the language rule)
- Modify: `AIOrchestratorCoreLib.Tests/Kit/PlatformCodesAreTaughtTests.cs`, `RoleCommandMarkerTests.cs`
- Create: `AIOrchestratorCoreLib.Tests/Kit/TheSkillsReadTheExportsTests.cs`

**Interfaces:** consumes Task 2's `PerUserEnvironment_Names` and Task 1's `PlatformCodes_Formatter`. Produces nothing for later tasks.

- [ ] **Step 1: Measure before editing — count the normative sentences**

`.claude/rules/kit-and-scripts.md`: *"The role protocols are Manu's method: **reorganise, never rewrite a rule**; a test counts normative sentences before and after."* **No such test exists in this tree** (verified: nothing under `AIOrchestratorCoreLib.Tests/` or `tools/` mentions a normative-sentence count). So the rule is currently honoured by hand.

Honour it by hand, visibly: before editing, record for each of the four files the count of lines containing a normative marker (`MUST`, `NEVER`, `ALWAYS`, `do not`, `never`, an all-caps imperative opener). After editing, record it again, and **put both numbers in the report**. A drop is not automatically wrong — this task deletes an 18-item legend — but an unexplained drop is.

- [ ] **Step 2: Replace the legend in the three role skills**

The pasted line is byte-identical to `Describe_Legend()`'s output in all three files, and it is **the owner's own eighteen repos** — shipped to the brother's VPS, where none of them exists.

Replace with a pointer and the RULE, keeping every owner quotation:

```
- **PLATFORM CODES — the owner speaks in them, so you must read them** (their rule, 2026-08-19).
  The codes for THIS machine are in `$AIORCH_PLATFORM_CODES`, exported for you by the app from the
  repo list. Read it: `printf '%s\n' "${AIORCH_PLATFORM_CODES:-}"`. An EMPTY or absent variable
  means this machine has no codes configured — then a code the owner speaks is a QUESTION, exactly
  as an unrecognised one always was.
```

Keep, verbatim, the sub-product paragraph and its owner quotation (*"if I say I want to work on IS the general supervisor should understand that I mean on SL, and the topic name should still indicate IS"*) — that is a RULE, and the rule survives the table.

**The absent-variable branch is load-bearing**: without it a session on a machine with no codes will invent them, which is the exact failure the current text's "a code you do not recognise is a QUESTION, never a guess" exists to prevent.

- [ ] **Step 3: Remove the literal `Nathan`**

`kit/skills/subagents/SKILL.md`, two sentences:
- *"The only way Fable runs is because **Nathan** asked for it or chose it as the session model."*
- *"If you coordinate other sessions and your turn is **Nathan's** only channel, do not block it…"*

This is a HOUSE skill — any role may load it — so both brothers read the other's name. Replace with `${AIORCH_OWNER_NAME:-the owner}`'s meaning in prose: **"the owner"**, with a pointer to `$AIORCH_OWNER_NAME` for when a session wants to address them by name. Do not change either sentence's rule.

- [ ] **Step 4: The language rule, once, in each role skill**

§7.8's replacement for the deleted translator is one sentence, and `owner.language`'s catalogue description already carries its exact wording (plan 02 wrote it there deliberately, calling itself *"the only machine-readable home that rule has"*): **write to the owner in the language they used; everything on disk or addressed to another agent stays English — channel entries, commit messages, PLAN.md, code and comments included.**

Add it once per role skill, next to whatever that skill already says about writing to the owner, with the `$AIORCH_OWNER_LANGUAGE` pointer: `auto` means the rule, `en`/`it` pin the owner-facing half.

**Check for, and fix, the two stale lines §7.8 names**: *"the two stale 'even when the traffic is Italian' lines in the fork's implementer and reviewer skills"*, and master's *"ENGLISH always"* sentences. Grep before writing; report how many you found, because the spec's count was taken on a different tree.

- [ ] **Step 5: Reverse `PlatformCodesAreTaughtTests`**

It currently asserts the OPPOSITE of what this task delivers — that every code in `Platform_Abbreviations.ALL` appears backticked in each of `general-supervisor.md`, `supervisor.md`, `solo.md`. After Step 2 that is false by design, and deleting the file would leave the property unguarded.

The property is now two facts, and they need **two cases, because a single case that could pass for either reason pins neither**:

1. `EveryRoleThatResolvesACode_NamesTheExport` — each of the three skills contains `AIORCH_PLATFORM_CODES`.
2. `TheExportedLegend_IsBuiltFromTheRepoList` — `PlatformCodes_Formatter.Describe_Legend` over a two-repo fixture produces both codes and neither of any other.
3. `NoRoleSkill_StillPastesALegend` — **the deletion guard**: no `SKILL.md` contains more than N backticked short-caps tokens on one line. Without it, the old table can come back and cases 1 and 2 stay green. Pick N by measuring, and say in the doc what was measured.
4. Keep `ASubProductResolvesToItsParentRepo` and `AnUnknownCodeIsNotGuessed`, retargeted at `PlatformCodes_Formatter` over a fixture list (under D2(b) the sub-product case is DELETED, and that deletion must be called out in the report as a behaviour the owner asked for and no longer gets).

`Read_RoleCommand`'s refusal-to-run (`throw` when the file is not found) stays exactly as it is.

- [ ] **Step 6: `RoleCommandMarkerTests.NOT_MARKERS`**

It splices `Platform_Abbreviations.ALL.Select(entry => entry.Code)` with a docstring explaining that *"a hand-copied list of eighteen codes would be the second copy the whole `Platform_Abbreviations` class exists to avoid"*. Under D3(a) that class is gone; splice the new single source instead. **Do not hand-copy the codes**, which is the one thing that docstring forbids.

- [ ] **Step 7: Verify**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~Kit"
```
Expected: 0 failed. `KitProseCarriesTheOwnersRulesTests`, `EverySpawnableRoleIsInTheKitTests`, `NoProtocolFileIsOrphanedTests`, `EveryWorkingRoleRunsToTheEndTests`, `SoloIsToldToFanOutTests` and `EveryOwnerFacingRoleCanCloseTests` all read this prose and are the guard on the guard.

**State which copy** in the report: these assertions are about **branch source**. They say nothing about the installed plugin, and nothing about what a running session reads — see Task 9.

- [ ] **Step 8: Commit** — `refactor(kit): the skills read the machine's own codes, name and language instead of one owner's`

---

### Task 4: `jq` on Windows — the installer gets it, and the host is honest about it

**Source:** spec §7.7, "jq on Windows": *"the typed-entry gate in `kit/bin/channel-append.sh` refuses to write without `jq`. `install.ps1` installs it (`winget install jqlang.jq`) instead of warning; the bootstrapper checks `jq` beside the plugin and records a verdict that reaches General once …; CI installs jq on the Windows runner."*

**Blocked by D4, D5, D7.**

**Files:**
- Modify: `kit/install.ps1`
- Modify (only under D4(a)/(c)): `AIOrchestratorCoreLib/Kit/KitAssets_Bootstrapper.cs`
- Modify: `AIOrchestratorCoreLib.Tests/Kit/BothInstallersCheckForJqTests.cs`

- [ ] **Step 1: Establish what is already done, and report it**

Three of the four clauses may already be satisfied by plan 01. Check each and write the answer down rather than re-doing it:
- `channel-append.sh` refuses without jq, in one line naming the fix — **pinned already** by `BothInstallersCheckForJqTests.TheToolRefusesInOneLineWhenJqIsMissing` (asserts `REFUSED`, `NOTHING WAS WRITTEN`, `--body-file`).
- CI installs jq on BOTH runners — **already true** in `.github/workflows/windows-build.yml` (`choco install jq` on Windows, `apt-get install -y jq` on Linux). Verify, do not re-add.
- `install.sh` requires jq and EXITS without it — already true.
- `install.ps1` WARNS — this is the clause §7.7 asks to change (D7).

- [ ] **Step 2: `install.ps1` — D7's answer**

Under the recommendation (b): when `Get-Command jq` finds nothing, offer the install, run it on yes, and **re-check afterwards rather than assuming** — `install.ps1` already carries that lesson twice (*"Re-checked rather than assumed: the reinstall can fail, and 'reinstalled' printed over a cache that did not move is the same lie one turn later"*). On a non-interactive run, keep the current warning verbatim.

PowerShell 5.1 traps to remember: `$ErrorActionPreference = 'Stop'` does NOT stop on a native command's exit code — `install.ps1`'s own comment records that defect and the `$LASTEXITCODE` check that fixed it. Check `$LASTEXITCODE` after `winget`.

**Say the msys sentence again in the success path too.** The current warning ends with *"channel-append.sh runs in msys bash, so jq must be on the PATH that bash sees"* — that remains true after a successful `winget install`, and it is the thing that is actually wrong on this machine today (jq is reachable from a login shell and not from the app's environment; every `dotnet` invocation in this plan carries a PATH export because of it).

- [ ] **Step 3: The host check — D4's answer**

Under the recommendation (b) this step is **DELETED and the deletion is reported**, with the reason: the tool already refuses in one line, that refusal is pinned, and the only honest host-side probe would be the first bash this app ever runs from C#, against a stated convention.

Under (a): probe `bash -lc 'command -v jq'` with a short timeout; **no bash found ⇒ CANNOT TELL**, logged naming which predicate could not be evaluated (decision 21's corollary), never a denial. It is **not** a `PluginVerdicts` value and must never gate spawning — a missing jq degrades typed entries; it does not make a session read the wrong protocol, which is the only thing `IPluginGate` exists to stop. The notice goes to the general channel once per host run, audience per D5.

- [ ] **Step 4: Widen the installer test**

`BothInstallersCheckForJqTests.EachInstaller_ChecksForJq_AndSaysHowToGetIt` asserts a check (`command -v jq` / `Get-Command jq`) and a named fix (`install jq` / `jqlang.jq`). Both survive Step 2. Add one case for whatever D7 chose, anchored on the capability word (`winget`), not on a sentence — the file's own doc explains why: *"Asserted on capability words rather than on sentences … A rewording stays green; removing either stops being green."*

Keep the docstring's honest paragraph about install.ps1 being asserted BY SHAPE — **but correct its premise**: it says *"there is no PowerShell on this machine, so `install.ps1` cannot be run"*, which was true on the fork's Linux box and is false here. Task 5 Step 6 runs it.

- [ ] **Step 5: Verify**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~BothInstallersCheckForJq|FullyQualifiedName~KitAssetsBootstrapper|FullyQualifiedName~ChannelAppendTypedEntries"
```
Plus, by hand, in a shell with jq NOT on PATH:
```bash
env -u PATH PATH="/usr/bin:/bin" bash kit/bin/channel-append.sh --help 2>&1 | head -5
```
Expected: the one-line refusal naming `--body-file`. **This is the only jq assertion in the plan that exercises the real code path rather than reading a file.**

- [ ] **Step 6: Commit** — `feat(kit): install.ps1 can get jq, and the host stops pretending it can tell`

---

### Task 5: The two installers stop diverging — and `install.ps1` stops deleting the catalogue

**Source:** spec §7.7's delivery bullet (the installers are the delivery mechanism) and §11.3 (*"`preset` absent means `classic`; the installer asks on a fresh machine"*).

**This task also carries a defect that is LIVE DAMAGE, which is decision 22's second admission** — it is not a discovery being turned into work, it is a thing that will destroy the owner's plan-02/03/04 settings the next time they run setup.

**Blocked by nothing; serialise with Task 4 (same file).**

**Files:**
- Modify: `kit/install.ps1` (the `config.json` write; the `preset` prompt; the stale build reminder)
- Modify: `kit/install.sh` (the `preset` prompt)
- Modify: `AIOrchestratorCoreLib.Tests/Kit/InstallersRefreshAStaleCacheTests.cs` (widened to the parity claims)

- [ ] **Step 1: Name the defect precisely, in the commit and the report**

`install.sh` MERGES onto the existing config and its comment records why, dated:

> *"MERGED ONTO WHAT IS THERE, NEVER REBUILT FROM A FIELD LIST. The previous shape named six keys and silently dropped every other one the app persists — `communicatorModel`, `voiceTranscribeCommand`, `orchestrationTokenBudget` and `telegramStatusScreenshots`, which the owner toggles from their phone. A bootstrap re-run changed settings and said nothing."*

`install.ps1` **still does exactly that** — it builds `$config = New-Object PSObject` and adds six properties (`repos`, `supervisorModel`, `implementerModel`, `generalSupervisorModel`, `telegramSupergroupChatId`, `telegramOwnerUserId`), then overwrites. Everything else is gone. After plans 02–04 that list includes `preset`, the whole `effort` block, `runners`, `printRunner`, `phone.*`, `pulse.*`, `topic.*`, `web.*`, `owner.*`, `reviewerModel`, `soloModel`, `communicatorModel`, `telegramInbound`, `planBackend`, the guardrail keys — **and `repos[].code`, which Task 1 just added.** The fix landed on the POSIX twin and never crossed.

- [ ] **Step 2: Merge, back up, write atomically**

Mirror `install.sh`'s shape in PowerShell:
- read the existing file, tolerate unparseable (`install.ps1` currently starts from an empty object when `settings.json` will not parse — `install.sh` REFUSES instead and says so; **that asymmetry is deliberate for `settings.json` and must NOT be copied for `config.json`**: refusing is right when the file is the user's hooks, and wrong when it is our own config the app can rebuild. Decide, and write the reason in the file);
- back up to `$configFile.aiorch-backup` before writing, as `install.sh` does;
- write to `.tmp` and `Move-Item` into place, so a failure leaves the existing file untouched rather than truncated — `install.sh`'s comment records a config.json TRUNCATED TO ZERO BYTES, *"repos, models and every Telegram setting gone, and the next start silently in file-only mode"*;
- `Out-File -Encoding utf8` explicitly.

- [ ] **Step 3: Ask for the preset on a fresh machine**

§11.3: *"`preset` absent means `classic` (master's behaviour); the installer asks on a fresh machine."*

- Ask only when `config.json` does not already state `preset`, and only on a terminal (`[Environment]::UserInteractive` / `[ -t 0 ]`).
- Offer `classic` and `quiet` with one line each, taken from the presets' own `_comment` fields rather than retyped.
- **Enter means "do not write the key"**, never "write `classic`". An absent key follows the preset chain; a written `classic` is a stated value that can never move — the same rule `EffortSettings_Json`'s missing `Write` enforces on the other side, and the rule spec §6.2 states as *"`Save` never materialises a preset value into `config.json`"*.
- A typed word that is neither must re-ask, not default: `Presets_Loader` THROWS on an unknown name, and an installer that writes `quite` produces an app that will not load its config.

- [ ] **Step 4: The stale build reminder**

`install.ps1` ends with:
> `'  - the Da-Vinci-Fintech-Suite repo checked out at ..\..\manuelvene90\Da-Vinci-Fintech-Suite (for LoggingLib)'`

CLAUDE.md's resolved decisions retired that in the same sentence that records why: *"the app now ships its own dark log view … **The repo currently builds standalone — no suite checkout required.**"* `AIOrchestrator.csproj`'s own comment says the same. Delete the line; it sends a new machine after a checkout it does not need.

- [ ] **Step 5: A parity test that is about the LIST, not about two files**

`InstallersRefreshAStaleCacheTests` already asserts, per installer, that it uninstalls-before-installing and compares by content (`diff -rq` / `Get-FileHash`). Widen it into the claim the two scripts actually owe each other — **each does the same things, in its own idiom**:

| claim | install.sh marker | install.ps1 marker |
|---|---|---|
| merges config.json rather than rebuilding | `jq` `'. + {` | `Add-Member`/merge over the parsed object — pick a marker that a rebuild cannot satisfy |
| backs up before writing | `.aiorch-backup` | `.aiorch-backup` |
| writes via temp + move | `.tmp` + `mv` | `.tmp` + `Move-Item` |
| asks for the preset, and Enter writes nothing | `preset` | `preset` |
| moves legacy commands aside, never deletes | `.aiorch-removed` | `.aiorch-removed` |

**Keep the test's honest paragraph about shape-vs-run**, and update it with Step 6's result.

- [ ] **Step 6: RUN `install.ps1` end to end, in a throwaway HOME — the ceiling the fork could not reach**

The fork asserted `install.ps1` by shape because its machine had no PowerShell. **This machine is Windows.** Do what `install.sh` got on macOS on 2026-09-07 (fresh install, a no-change re-run, a committed kit change, an uncommitted kit change):

```powershell
$throwaway = Join-Path $env:TEMP "aiorch-install-probe-$(Get-Random)"
New-Item -ItemType Directory -Force $throwaway | Out-Null
$env:USERPROFILE = $throwaway          # the script derives ~/.claude from this
powershell -NoProfile -ExecutionPolicy Bypass -File kit\install.ps1
```

**Before you run it, read the script for every path it touches and confirm each derives from `$env:USERPROFILE`.** If ANY path is absolute or derives from something else, do not run it — you would be editing the owner's live `~/.claude` while their app is running. Report what you found either way.

Then, with a config.json in the throwaway home carrying `preset`, `effort`, `runners` and a `repos[].code`: re-run and assert every one survives. **That is the real proof of Step 2**, and it is worth more than the five shape assertions above put together.

- [ ] **Step 7: Verify**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~Installers|FullyQualifiedName~BothInstallers"
```

- [ ] **Step 8: Commit** — `fix(kit): install.ps1 merges config.json instead of deleting every key it does not know`

---

### Task 6: The refusal reaches a person

**Source:** the plan's own brief (*"how the plugin gate's verdict is surfaced to a user whose spawning has just been refused"*), read against decision 21 (the app enforces at the point of effect), decision 15 (an alert the owner CAN act on) and decision 14 (a repeat edits, it never stacks).

**Blocked by D8.**

**Files:**
- Modify: `AIOrchestratorCoreLib/Kit/KitAssets_Bootstrapper.cs` (`Tell_Owner_Once`)
- Modify: `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` (the refusal line)
- Modify: `AIOrchestrator/App.xaml.cs`, `AIOrchestrator.Daemon/BridgeHost_Service.cs` (the startup surface, under D8(c))
- Test: `AIOrchestratorCoreLib.Tests/Kit/TheRefusalReachesAPersonTests.cs`

- [ ] **Step 1: Establish the three gaps by reading, and write them down**

As read on branch source, 2026-09-12:

1. **`Tell_Owner_Once` returns silently when there is no general channel file.** `if (!File.Exists(paths.GeneralChannelFile)) return;` — which is a FRESH MACHINE, the one most likely to have a bad kit, and the one this plan exists to make installable. The refusal then exists only in `orchestrator.log.jsonl`.
2. **The launcher's refusal is per-spawn.** `if (!_pluginGate.Spawning_Allowed) { _log.Log_Error(launch.OrchId, $"REFUSED to start '{launch.MemberId}' ({launch.Role}) — {_pluginGate.Refusal}", null); return null; }` — and `SessionWatchdog` reaches it on every tick for every dead session. That is a waterfall in the log, which is the thing the `Unchecked_WasReported` flag right above it already exists to prevent for the OTHER branch. The gate interface has one such flag; the refusal path has none.
3. **Nothing in either host's UI shows the verdict.** `MainWindow.xaml.cs` contains no reference to `PluginGate`, `Verdict` or `Kit` (verified by grep). The WPF app's live log panel will carry the `Log_Error`, which scrolls.

- [ ] **Step 2: Write the failing tests**

`TheRefusalReachesAPersonTests`:
- `OnAFreshMachineWithNoGeneralChannel_TheRefusalIsStillWrittenSomewhereAPersonLooks` — bootstrap against a temp supervision root with NO general channel, a bad kit; assert the refusal lands (per D8's answer).
- `TheLauncherStatesTheRefusalOnce_NotOncePerSpawn` — a gate recording a refusal, ten `Start_Session` calls, ONE error line. This is decision 14 applied to the log; the flag is the same shape as `Unchecked_WasReported`.
- `TheRefusalNamesWhatToRun` — the text contains the command (`KitPlugin.REINSTALL_COMMAND` / `INSTALL_COMMAND`). `PluginVersion_Verifier.Describe` already does this; pin it, because this is the one property that makes the alert actionable and therefore owner-facing at all (decision 15).
- `ARecoveredKit_StillClearsTheBlocker` — `Tell_Channel_ItRecovered` must keep working after the change. Its docstring records why it exists: *"the refusal it saw at the last boot stays true for it until something newer says otherwise — which is how a cleared blocker went on being reported for hours."*

- [ ] **Step 3: Implement D8's answer**

Under (a)+(c): `Tell_Owner_Once` calls `GeneralChannel_Initializer.Ensure_Exists(paths)` before appending (the general spawn already does), and both hosts print the refusal to their own startup surface — `Console.Error` for the daemon (which is what systemd's journal captures), and the WPF log panel plus a **non-scrolling** surface for the app. **Do not add a modal dialog**: the app is a bridge, it starts unattended after a reboot, and a dialog blocks the bridge that is how the owner gets told.

Keep the split the gate's own docstring states and do not weaken it: *"nothing spawns with the wrong kit, and nothing already running is killed for it."* The bridge keeps running.

- [ ] **Step 4: Add the once-flag**

`IPluginGate` already has `Unchecked_WasReported` with a docstring explaining the pattern (*"Read and set by the launcher so it is said once per host run instead of once per spawn"*). Add its twin for the refusal, with the same reasoning and a reference to decision 14. **Do not make it a field of the launcher** — the launcher is not a singleton in every host.

- [ ] **Step 5: Verify**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~TheRefusalReachesAPerson|FullyQualifiedName~KitAssetsBootstrapper|FullyQualifiedName~PluginVersionVerifier|FullyQualifiedName~PluginContentIsVerified|FullyQualifiedName~KitCheckProvesContent|FullyQualifiedName~OrchestrationLauncherTests"
```

- [ ] **Step 6: Commit** — `fix(kit): a refused spawn is stated to a person, once, with the command that fixes it`

---

### Task 7: Both hosts ship the same kit, and the digest covers what a session reads

**Source:** spec §7.7's delivery bullet — the kit that ships beside the binary IS the delivery, and `KitContent_Digest` is what makes it verifiable. **Blocked by D6.**

**Files:**
- Modify: `AIOrchestrator.Daemon/AIOrchestrator.Daemon.csproj`
- Modify: `AIOrchestratorCoreLib/Kit/KitContent_Digest.cs` (D6)
- Test: `AIOrchestratorCoreLib.Tests/Kit/BothHostsShipTheSameKitTests.cs`

- [ ] **Step 1: The asymmetry, as read from the two csproj files**

| kit folder | `AIOrchestrator.csproj` | `AIOrchestrator.Daemon.csproj` |
|---|---|---|
| `.claude-plugin/*.json` | ✔ | ✔ |
| `skills/**/*.md` | ✔ | ✔ |
| `bin/*.sh` | ✔ | ✔ |
| `hooks/*.sh` | ✔ | ✔ |
| `presets/*.json` | ✔ | ✔ |
| **`grammar/*.json`** | ✔ | **✘ MISSING** |
| `statusline/statusline.ps1` | ✔ | ✔ |
| `statusline/statusline.sh` | ✘ (deliberate — WPF is Windows-only) | ✔ |

`kit/bin/channel-append.sh` resolves its grammar at `${AIORCH_CHANNEL_GRAMMAR:-$(dirname $BASH_SOURCE)/../grammar/channel-grammar.json}`, and refuses typed entries without it. The WPF csproj's own comment says the point of shipping the kit whole is *"so `claude plugin marketplace add` can point at the running app's own copy"* — **on the daemon, that copy is a channel tool without its grammar.** The Linux host is the one where this lands.

Add the item. Then write the test that makes the class of defect impossible rather than fixing one instance of it.

- [ ] **Step 2: `BothHostsShipTheSameKitTests`**

Read the two `.csproj` files as XML, collect the `Include` globs of every `<Content>` item whose `Link` starts with `kit\`, and assert:
- `NeitherHostShipsAKitFolderTheOtherDoesNot` — the SETS agree, **except for one named exception**: `statusline.sh`, absent from the WPF app because it is `net10.0-windows`. Name it in a constant with the reason, so a second exception is a decision somebody makes on purpose.
- `EveryFolderTheDigestReads_IsShippedByBothHosts` — `KitContent_Digest.DIGESTED_FOLDERS` ∪ `DIGESTED_FILES` ⊆ what each host copies. **This is the guard that matters:** a digested folder the host does not ship makes `Compute_OrNull` return a different number from the installed copy for ever, and the host refuses every spawn with a message about content that is actually about packaging.
- Refuse to run if either csproj cannot be found (decision 20).

- [ ] **Step 3: D6 — `grammar` joins the digest**

If the answer is yes: add `"grammar"` to `DIGESTED_FOLDERS` and extend the class doc — its stated subject is *"the text the sessions will actually read — the role protocols, the hooks, the channel helper and the plugin manifest"*, and the grammar is read by the channel helper at every typed append. Say why `presets` stays out (read by the HOST; also embedded in CoreLib, so the host's copy cannot go missing).

**Report the cost in the same breath:** every existing machine's next startup reports `ContentMismatch` — correctly, because its installed digest was computed without `grammar` — and refuses to spawn until the installer is re-run. That is one reinstall on each of two machines, it is the intended behaviour of the check, and **it must be in the README (Task 8) and in the report, or the owner meets it as a mystery.**

- [ ] **Step 4: Verify**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~BothHostsShipTheSameKit|FullyQualifiedName~KitCheckProvesContent|FullyQualifiedName~PluginContentIsVerified"
ls AIOrchestrator.Daemon/bin/Debug/net10.0/kit/grammar/
```
The `ls` is the point: the test reads the csproj, the `ls` reads the **build output**. Both, and say which is which.

- [ ] **Step 5: Commit** — `fix(kit): the daemon ships the grammar its channel tool reads, and a test keeps the two hosts in step`

---

### Task 8: The README for a new machine on each OS, and the decision-17/18 alignment

**Source:** spec §10 phase 5, the two clauses not yet claimed: *"the CLAUDE.md alignment (decisions 8, 11, 17 rewritten)"* and *"the `README` for a new machine on each OS."* **D1 gates the CLAUDE.md half.**

**Files:**
- Modify: `kit/README.md`
- Modify: `.claude/rules/kit-and-scripts.md`
- Modify: `AIOrchestratorCoreLib.Tests/Kit/TheSkillsQuoteTheAppsOwnNumbersTests.cs` (docstring only)
- Modify (only under D1(a)): `CLAUDE.md`

- [ ] **Step 1: Audit the alignment before writing a word of it**

Decisions 8, 11, 17, 23 and 26 all carry post-merge rewrites in the copy read for this plan. **So most of what §10 phase 5 asks for is already done, and this task's real work is the residue.** List it, verify each, and report:

- decision 17 — rewritten for `KitAssets_Bootstrapper`, with the "a kit edit is not verified by editing `kit/` and re-running the installer" caution. ✔
- decision 23 — carries the `KitAssets_Bootstrapper` replaces `KitAssets_Installer` paragraph. ✔
- decision 11 — still reads *"Whether that stays permanent is still open … the owner has not answered."* **The owner ANSWERED on 2026-09-12** (plan 02's non-goals record it and name the commit that corrected it: `7c56be1` on `docs/translator-decision-answered`). Whether that correction is on master by the time this plan runs is a thing to CHECK, not assume.
- decision 26 — contains its own flagged contradiction about whether `CLAUDE.md` may be edited. That is D1.
- decision 8 — the resume rule; nothing in this plan touches it. Verify it is current and move on.

- [ ] **Step 2: Three prose drifts found while reading, each one line**

1. `kit/README.md`, "Upgrading from the pre-plugin kit": *"Both installers, and the host at startup, **delete** the role protocols and hooks the old builds copied into `~/.claude`."* **They move them aside.** `LegacyKit_Remover`, `install.sh` and `install.ps1` all say so emphatically and all give the same reason (*"these names are GENERIC, so a `reviewer.md` somebody wrote for something else entirely can carry one, and unlinking it would destroy their work"*). The README promises a destructive act the code refuses to perform.
2. `AIOrchestratorCoreLib.Tests/Kit/TheSkillsQuoteTheAppsOwnNumbersTests.cs` docstring: *"the app build copies the kit to its output and `KitAssets_Installer` installs from THERE at startup (decision 17)"* — retired by decision 17's rewrite; the app VERIFIES and never copies commands. The rest of that paragraph (a green run here says the branch source agrees, and says nothing about any installed copy) is right and must stay.
3. `.claude/rules/kit-and-scripts.md`: *"a test counts normative sentences before and after"* — **no such test exists** (verified). Either write it or say it is a practice; do not leave a rule citing a guard that is not there. **Recommended: say it is a practice**, and point at Task 3 Step 1's by-hand count as the method. Writing a sentence classifier is not work anyone asked for (decision 22).

- [ ] **Step 3: The fresh-machine recipe, per OS**

`kit/README.md` has Install/Update/Why-not-update sections but no "here is a machine with nothing on it" path. Add one section per OS, each an ordered list that ends in a session actually starting, and each naming what it CANNOT do:

**Windows (the owner's machine):** .NET 10 SDK · Claude Code on PATH · Windows Terminal (optional; sessions fall back to plain PowerShell) · git bash, because `channel-append.sh` and the hooks run in msys bash · **jq on the PATH THAT BASH SEES** (Task 4) · `powershell -ExecutionPolicy Bypass -File kit\install.ps1` · `dotnet build AIOrchestrator.slnx` · run `AIOrchestrator\bin\Debug\net10.0-windows\AIOrchestrator.exe`. Note that the terminal runner is Windows-only (`wt.exe`), which is why this is the host with a screen.

**Linux / macOS (the VPS):** .NET 10 SDK · Claude Code · bash + jq + `md5sum`/`md5` · `bash kit/install.sh` · `dotnet publish AIOrchestrator.Daemon -c Release -r <linux-x64|osx-arm64> --self-contained -o <dir>` · the unit from `deploy/systemd` or `deploy/launchd` (see `README-daemon.md`). **Name the thing that is different and not obvious: there is no terminal runner here**, so `runners.<role>.runner` must be `print` or `stream` for anything to run at all — which is what the `quiet` preset already sets, and what an untouched `classic` machine does not.

Add D6's one-time `ContentMismatch` note if D6 said yes.

- [ ] **Step 4: Where the README says which copy**

Put decision 18's habit into the README itself, once, at the top of the update section: **the kit exists as branch source, build output, installed plugin, and whatever binary is running** — and the only way to know what a SESSION reads is to start one and ask it. That sentence is the honest summary of this whole plan and it belongs where a person setting up a machine will read it.

- [ ] **Step 5: D1 — the `CLAUDE.md` half**

Under (a), and ONLY under (a): two paragraphs, no more.
- decision 11's "still open" sentence becomes the answer, dated, naming the commit that carried it.
- decision 26's flagged contradiction gets the owner's call written into it.
Under (b): both go into the report as facts for the owner, and `CLAUDE.md` is not touched. **Either way, say in the report which it was and who decided.**

- [ ] **Step 6: Verify**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~Kit"
```
Nothing here is code, so the suite is a regression check only. **The real verification is a second reader**: the README's Windows recipe followed on a machine that is not this one. Say plainly in the report that this was not done, if it was not.

- [ ] **Step 7: Commit** — `docs(kit): a fresh machine on each OS, and the prose catches up with decision 17`

---

### Task 9: The gate

**Files:**
- Create: `docs/superpowers/plans/2026-09-12-kit-delivery-05-report.md`

- [ ] **Step 1: The full suite, once, alone**

Close every other worktree's test run first.

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-plan05
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug 2>&1 | tee /c/Users/Gianpiero/source/repos/plan05-gate.log | tail -6
grep -E "^\s+Failed " /c/Users/Gianpiero/source/repos/plan05-gate.log | sort
dotnet test tools/claude-contract/ClaudeContract.Tests/ClaudeContract.Tests.csproj -c Debug 2>&1 | tail -4
```

Expected: **0 failed.** Compare the SET OF NAMES against plan 04's gate report, never the count. Two known non-defects to check for before diagnosing anything:
- `ChannelAppendTypedEntriesTests` failing with an entry written `FROM solo-1` instead of `FROM supervisor` is this session's own `AIORCH_MEMBER` leaking into the test process. Re-run with `AIORCH_ROLE` and `AIORCH_MEMBER` unset. **This plan makes it worse before it makes it better:** Task 2 adds three more `AIORCH_*` variables, and if plan 03's env scrub has not landed, a session running with them set can leak them into a test child. Say so.
- Any red in the `PrintTurnDispatcher` / `Drive_Until` / `ChannelAppendHelperInterop` family under load: isolate with `--filter` on the single name and re-run alone before believing it.

- [ ] **Step 2: Both CI legs**

Push the branch and read both jobs. `windows-build.yml` already triggers on `integration/**`, `stage/**` and `master` — **a `plan/**` branch is not in that list.** Either push through an `integration/` name or add the trigger; do not conclude "CI is green" from a workflow that never ran. Plan 01's gate hit exactly this.

- [ ] **Step 3: The live proof, and the honest line about it**

Everything above is branch source and build output. The three things that are only true of a LIVE machine, each verified by hand or declared unverified:

1. **`Get-Process AIOrchestrator | Select Path`** — record it. If it is not this worktree's build output, then nothing in this plan is running, and the report says so in those words. This is decision 23, and it is the reason the 2026-08-21 evening is in `CLAUDE.md`.
2. **A session actually sees the exports.** Start one terminal session and one bridge-driven session from a build of this branch and have each run `env | grep '^AIORCH_' | sort` (the role skills already tell sessions to do this). **That output is the only proof Task 2 works.** A green `BothSpawnPointsExportThePerUserValuesTests` proves the app builds the right string; it does not prove `wt.exe` carried it, that PowerShell's `-EncodedCommand` survived it, or that the plugin the session loaded is the one Task 3 edited.
3. **The installed plugin holds Task 3's prose.** `claude plugin list --json`, then read `installPath`'s `skills/general-supervisor/SKILL.md` and grep for `AIORCH_PLATFORM_CODES`. Per decision 17: a kit edit is verified against a **RESTARTED** session, because the bootstrapper records its verdict at startup.

- [ ] **Step 4: Write the report**

`docs/superpowers/plans/2026-09-12-kit-delivery-05-report.md`, at minimum:

- **Every open decision, its answer, and who gave it.** D1–D8.
- **The `Kit` category's contents** and the exact diff to `SettingsCatalogTests` — the exemption plan 02 planted, and that it is gone.
- **Which copy every claim is about**, per decision 18, as a table. This is the plan where that table is the report's spine, not a footer.
- **The three live proofs of Step 3**, with their raw output, or a plain statement that a human has not done them yet and that the change is therefore unproven on a live machine.
- **The normative-sentence counts** from Task 3 Step 1, before and after, per file.
- **`install.ps1`'s throwaway-HOME run** (Task 5 Step 6): what it did, what survived, and whether every path really derived from `$env:USERPROFILE`.
- **The D6 reinstall**, if D6 said yes: that both machines will report `ContentMismatch` once and must re-run their installer.
- **What the spec asked for that this plan did not deliver**, and why.
- **Deferred minors and PARKED items**, in plan 01's format.

- [ ] **Step 5: Commit** — `docs(ledger): plan 05's gate — kit delivery, the per-user exports, and what only a human can prove`

---

## PARKED

Found while reading for this plan; **none of it traces to an owner request** (decision 22). One line each, written down so it is not lost, outside the denominator so it cannot move the owner's bar.

- `OrchestratorConfig_Loader.Save` rebuilds the `repos` array element by element, so **any** hand-written repo-element key other than `name`, `path`, `topicColor` and (after Task 1) `code` is still deleted on the next save. Task 1 Step 2 pins the limitation with a test; closing it needs a raw-tree merge for array elements, and nobody has asked for one.
- `.claude/rules/kit-and-scripts.md` cites a test that counts normative sentences in the role protocols. No such test exists anywhere in the tree. Task 8 corrects the rule's wording; writing the classifier is not work anyone asked for.
- `Platform_Abbreviations` has **no production caller at all** today — the codes reach a session only by being pasted into three skills by hand. That is the defect Task 3 fixes; that it survived this long unnoticed is the finding, and it is not separately actionable.
- `InstalledPlugin_Reader`, `KitCheckHistory_Store` and `PluginVersion_Verifier` between them carry five verdict states and one history file, and nothing ever prunes the history file. Harmless; unasked.
- `KitAssets_Bootstrapper.Ensure_Installed` takes `IPluginGate` as an OPTIONAL parameter (`IPluginGate? gate = null`), so a host that forgets to pass it gets `Unchecked`, which ALLOWS. Both hosts do pass it today. The optionality is a footgun that a required parameter would remove; nobody has asked.
- `kit/self-write-suppression-check.sh` sits at `kit/` root rather than under `hooks/`, unlike the other two harnesses. Cosmetic.
- `install.sh` REFUSES to proceed on an unparseable `~/.claude/settings.json` while `install.ps1` starts from an empty object and overwrites it — a deliberate asymmetry per install.sh's own comment, but the PowerShell side destroys a user's hooks where the POSIX side protects them. Task 5 Step 2 decides it for `config.json` only; the `settings.json` half is left alone.
- The `windows-build.yml` trigger list does not include `plan/**`, so every plan branch of this series pushes without CI. Task 9 Step 2 works around it per-run.

---

## Self-review (run by the plan's author, 2026-09-12)

Checked against spec §7.7 clause by clause, §7.8, the `Owner and kit` rows of §6.4, §10 phase 5, §11.3 and §9's two-OS bullet. Gaps found and recorded rather than papered over.

1. **§7.7 has four bullets and this plan has nine tasks, so the mapping is stated rather than implied.** Delivery → Tasks 5, 6, 7, 8. jq on Windows → Task 4. Per-user values → Tasks 1, 2, 3. Brevity ceilings → a NON-GOAL, because that bullet's instruction is "stay as they are". §10 phase 5 adds two clauses §7.7 does not carry — the CLAUDE.md alignment and the per-OS README — and both are Task 8. Nothing in §7.7 is unclaimed, and no task here lacks a clause.
2. **Three of §7.7's clauses were ALREADY DELIVERED by plan 01, and the plan says so instead of re-doing them.** `KitAssets_Bootstrapper` in both hosts, `channel-append.sh`'s one-line jq refusal, and jq on both CI runners are all live in the tree as read. Task 4 Step 1 makes VERIFYING them a mandatory action rather than an assumption — the failure mode of a phase-5 plan written from a phase-0 spec is re-implementing what phase 1 already shipped.
3. **The spec's `repos[].code` cannot express sub-products, and the owner asked for sub-products twice.** §6.4 registers one string per repo; `Platform_Abbreviations` carries seven codes whose whole meaning is a PARENT (`IS`→`SL`), with the owner's own words quoted in the docstring. Taking §6.4 literally would silently delete a behaviour the owner asked for. That is D2, it is the owner's, and it is the single most consequential open question in this plan.
4. **`repos[].code` is the first catalogue path that is not a dotted scalar, and `SettingsJson_Path` cannot walk it.** Plan 02's self-review said so in advance (point 5: *"registering an array-element path would also need a path grammar that nothing else in v1 uses"*). This plan does NOT add that grammar — it registers the row as `Composite` with the loader as its named parser, which is the catalogue's existing word for exactly this, and which `repos` itself already uses. The cost is that plan 04's renderers cannot draw it from the definition; that is stated in the Depends section rather than discovered by plan 04.
5. **The defect in `install.ps1` is not a discovery being turned into work.** Decision 22 admits two things: something that BLOCKS a requested line, and live damage. `install.ps1` rebuilding `config.json` from six keys deletes `preset`, `effort`, `runners`, `phone.*` and — after Task 1 — `repos[].code`, which is this plan's own deliverable. It is both admissions at once. It is stated that way in Task 5 Step 1 so a reviewer can check the reasoning rather than take the scope on trust.
6. **§7.7 says the bootstrapper should check jq; the code conventions say no bash from C#; and the only honest check is one bash runs.** This is a genuine three-way conflict, not a preference, and it is D4 rather than a step. The recommendation is to NOT add the check — which is the plan declining a thing the spec asked for, stated out loud with its reasoning, because a check that answers the wrong question confidently is the failure decisions 18 and 20 were both written after.
7. **`PlatformCodesAreTaughtTests` asserts the OPPOSITE of what this plan delivers**, and deleting it would leave the property unguarded. Task 3 Step 5 reverses it into three cases and adds the deletion guard (`NoRoleSkill_StillPastesALegend`) that the obvious two-case version would lack — without it, the old table can come back and both new cases stay green. That is decision 20's "never assert on a state with two routes to it", applied to a guard rather than to a hook.
8. **Task 2's validation is load-bearing on ONE of the two spawn paths and inert on the other**, and the plan says so at the point where a later reader would delete it. The terminal path base64-encodes a PowerShell script (owner-typed text in a single-quoted literal — `SpawnCommand_Builder`'s own docstring records the 2026-09-10 defect of exactly that shape); the print path hands a dictionary to `ProcessStartInfo` and needs none. A comment that only says "validated" invites its own removal.
9. **The plan's own three live proofs are enumerated, and two of them the plan CANNOT perform.** Task 9 Step 3: which binary is running, whether a real session sees the exports, and whether the installed plugin holds the new prose. Every automated check in this plan is about branch source or build output. This is the plan where decision 23's trap is not a caution but the central fact, and the gate is written so that "we did not do this" is a legible outcome rather than an omission.
10. **Eight decisions, four of them the owner's** (D1, D2, D7, D8), four the coordinator's (D3, D4, D5, D6). The owner's four are: may we edit his context file; does his sub-product rule survive; may a setup script install software; and where a refusal should appear on a machine that has nothing yet. None of them is a preference — each is a place where the spec and the tree disagree, or where the act has a cost outside the code.
11. **Task 7 exists because of a one-line csproj omission, which is a suspiciously small thing to make a task.** It is a task because the FIX is one line and the GUARD is the deliverable: `EveryFolderTheDigestReads_IsShippedByBothHosts` makes the class of defect impossible, and the class is "the host refuses every spawn with a message about content that is actually about packaging" — which is the 2026-09-07 VPS evening in a new costume.
12. **Task 5 Step 6 is the one place this plan can do better than the fork could**, and it is written as an instruction rather than a suggestion. `install.ps1` has never been run end to end by anyone — the test that asserts it by shape says so, and names the reason (the fork's machine had no PowerShell). This machine is Windows. The step also carries a refusal condition: read every path in the script first, and if any of them does not derive from `$env:USERPROFILE`, do not run it — the alternative is editing the owner's live `~/.claude` while their app is running.
13. **Sequencing has one hard chain and one shared file.** 1→2→3 is a chain (the value, the export, the prose). Tasks 4 and 5 both edit `kit/install.ps1` and must be serialised with each other — parallel WRITERS need disjoint file sets (decision 16), and two agents editing one PowerShell script is exactly the case that rule names. Tasks 6 and 7 are disjoint from everything. Task 8 is prose and lands last.
14. **What this plan deliberately does NOT do, so a reviewer can check the omissions are chosen:** it does not add a path grammar for array elements; it does not write a normative-sentence classifier; it does not bump the plugin version; it does not add a POSIX terminal runner; it does not restore the periodic status, the reply keyboard, or anything else plan 03 holds open; and it does not touch `CLAUDE.md` unless the owner says so. Each of those was considered and each is either a different plan's, a PARKED line, or a decision.

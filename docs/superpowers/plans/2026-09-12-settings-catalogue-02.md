# Fork Merge — Plan 02: The settings catalogue, the resolver, the two presets — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every taste divergence between the two brothers a piece of DATA. One registry of setting definitions, one resolver with four layers (shipped default < preset < `config.json` < `session.json`), two shipped presets (`classic` = Manu's way, `quiet` = Nathan's way), and every key the merged tree already reads registered so the three renderers of plan 04 can list and edit them without a second parser. The model and effort defaults stop being compiled constants and become catalogue entries — which is what turns the four tests that plan 01 deliberately left red back to green.

**Architecture:** A new folder `AIOrchestratorCoreLib/Configuration/SettingsCatalog/` holding (a) the vocabulary enums, (b) the `ISettingDefinition` triple, (c) a dotted-path reader over `System.Text.Json.Nodes.JsonObject`, (d) the static registry `SettingsCatalog`, (e) `Presets_Loader` reading two embedded JSON resources exactly as `ChannelGrammar` reads the channel grammar, and (f) `Settings_Resolver`, a pure function over three trees and an optional session — the shape of `HostOptions_Factory.Create_FromArguments`, whose layers are parameters so precedence is tested without touching disk. Then two wiring tasks: `OrchestratorConfig_Loader` gains the preset rung for the six model keys and a new `effort` block, and `OrchestrationLauncherModel` resolves the effort ROLE DEFAULT from the config instead of `SpawnCommand_Builder` owning it as a constant.

**Tech Stack:** .NET 10 (`net10.0`, WPF app `net10.0-windows`), xUnit, `System.Text.Json.Nodes`, git, bash (msys on Windows), `jq`.

**Spec:** `docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md` — §6 in full (§6.1 shape, §6.2 precedence, §6.3 the presets, §6.4 the catalogue v1 table), the catalogue and resolver bullets of §9, and the phase-2 gate of §10. This plan implements phase 2 and nothing else.

**Worktree:** `C:\Users\Gianpiero\source\repos\AIOrchestrator-plan02`, branch `plan/02-settings-catalogue`, off master `dd68771` (the merged tree plan 01 delivered). Never commit to `master`; the owner merges.

## Non-goals — say no to these out loud

Every one of these is a later plan. A task that finds itself editing the files below has left its scope.

- **The three renderers (plan 04).** No WPF Settings window change, no `/settings` Telegram command, no `HttpListener`, no `web/` folder. `web.listen` and `web.token` are REGISTERED as catalogue entries in this plan and read by nothing — that is correct and intended.
- **Every behavioural seam (plan 03).** Who rings (`Mirror_Append_Async`, `OwnerPush_Policy`, `Resolve_EntrySound`), pulse fields (`TopicStatusLine_Builder`), buttons (`TopicCommandButtons`), receipts (`Send_ReceivedAck_Async`), glyphs (`TelegramDeliveryModes`, `Compose_TopicName`), the periodic status (`Push_AwayDigests_Async`), the reply keyboard, `topic.onClose`. Their catalogue entries exist after this plan and NOTHING reads them. `phone.*`, `pulse.*`, `topic.*` and `general.buttons` are registered and inert.
- **The kit's per-user values and the language prose (plan 05).** `owner.language` is a catalogue ENTRY here; no `AIORCH_OWNER_LANGUAGE` export, no skill prose edit, no `repos[].code`. The `Kit` category ships EMPTY in v1 and the catalogue test that asserts "every category has entries" exempts it by name.
- **The test-flakiness campaign.** 141 raw `File.ReadAllText` calls in the suite are known and explicitly deferred. Do not "fix one while passing by".
- **The translation layer.** Settled and gone (owner, 2026-09-12: *"agents write in my language, drop it"*). Nothing in this plan re-ports it, and `CLAUDE.md` decision 11 was already corrected on branch `docs/translator-decision-answered` (`7c56be1`) — **do not touch `CLAUDE.md`.**

## Global Constraints

- **`jq` lives only on a login shell's PATH.** Every task that runs `dotnet` must first run, in the same bash invocation:
  `export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"`
  Without it the 13 statusline parity fixtures fail and you will diagnose a catalogue change from their noise.
- **`AIOrchestratorCoreLib` is strict** (`.claude/rules/code-conventions.md`): the triple `IXxx` + `internal sealed class XxxModel : IXxx` + `static Xxx_Factory`; `Xxx_Yyy.cs` with the underscore only when the second word is a role (`_Parser`, `_Builder`, `_Factory`, `_Reader`, `_Resolver`); methods `Verb_Object[_Modifier]`; anything that may not resolve ends `_OrNull`; get-only properties from a primary constructor; **no `record` types**; ad-hoc multi-value returns are value tuples; XML docs argue the WHY with dated incidents. Tests: xUnit `[Fact]`, folders mirror production namespaces 1:1, class `<Subject>Tests` with the underscore stripped (`Settings_Resolver` → `SettingsResolverTests`), methods `Verb_Scenario_Outcome`, **stubs not mocks** (no Moq/NSubstitute).
- **NEVER run the full suite except at Task 8.** One suite at a time on this machine. A red under load is isolated with `--filter` and re-run alone before it is believed; compare the SET OF NAMES of failing tests against the known set, never the count.
- **The engine test harness has no fake-CLI seam.** `Bridge/BridgeEngine_Factory.cs` hard-wires the real per-OS `claude` into the print dispatcher, so an engine test that registers a print session can spawn a LIVE process. No task in this plan needs the engine — if one seems to, that is a signal it has wandered into plan 03. If you must, read `AIOrchestratorCoreLib.Tests/Bridge/EffortDialOnABridgeDrivenSupervisorTests.cs` first and copy its harness exactly.
- **Windows:** `python3` is native Windows Python and cannot open msys paths — hand it Windows paths. Bash heredocs over ~6 KB die as a fake quote error; write the script with the Write tool and run the file. Quote `git show "ref:path"` whole.
- **Say which copy you read** in every report: branch source (`kit/…`), build output, installed (`~/.claude`), or the running app's folder. The running app is a fourth copy (`Get-Process AIOrchestrator | Select Path`); nothing in this plan touches it, and no task needs the app restarted.
- **Stage by explicit path**; never `git add -A` / `.` / `commit -a`. Multi-line messages via `git commit -F <tempfile>`. Every commit ends with:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## File Structure

**Created**

```
AIOrchestratorCoreLib/Configuration/SettingsCatalog/
  SettingKinds.cs            ← Bool | Enum | Int | String | StringList | Composite
  SettingScopes.cs           ← Machine | Orchestration
  SettingCategories.cs       ← Phone | Pulse | Receipts | Models | Kernel | Kit | Owner
  SettingRenderers.cs        ← Toggle | Choice | Number | Text | OrderedList | ReadOnly
  RestartKinds.cs            ← None | NextSpawn | Host
  SettingOrigins.cs          ← ShippedDefault | Preset | ConfigFile | Session
  PulseField_Names.cs        ← the eight field words pulse.fields orders (plan 03 consumes)
  SettingDefinition/
    ISettingDefinition.cs
    SettingDefinitionModel.cs
    SettingDefinition_Factory.cs
  SettingsJson_Path.cs       ← Read_OrNull / Write / Remove over a dotted path on a JsonObject
  SettingsCatalog.cs         ← the registry: ALL, Find_OrNull, In_Category
  Presets_Loader.cs          ← the two embedded presets, the `preset` key, absent = classic
  SessionScoped_Reader.cs    ← catalogue path -> IOrchestrationSession field, one switch
  Settings_Resolver.cs       ← Resolve(definition, presetTree, configTree, sessionOrNull)

kit/presets/classic.json
kit/presets/quiet.json

AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/
  SettingDefinitionFactoryTests.cs
  SettingsJsonPathTests.cs
  SettingsCatalogTests.cs
  PresetsLoaderTests.cs
  SettingsResolverTests.cs
  PresetProbeTests.cs
```

**Modified**

| file | change |
|---|---|
| `AIOrchestratorCoreLib/AIOrchestratorCoreLib.csproj` | two `<EmbeddedResource>` items for the presets, beside the grammar |
| `AIOrchestrator/AIOrchestrator.csproj`, `AIOrchestrator.Daemon/AIOrchestrator.Daemon.csproj` | one `<Content>` item each so `kit/presets/*.json` ships beside the kit |
| `AIOrchestratorCoreLib/Configuration/OrchestratorConfig/OrchestratorConfig_Factory.cs` | the six `DEFAULT_*_MODEL` constants read the catalogue instead of stating a literal |
| `AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs` | the preset rung for the six model keys; the new `effort` block |
| `AIOrchestratorCoreLib/Configuration/OrchestratorConfig/IOrchestratorConfig.cs`, `OrchestratorConfigModel.cs`, `OrchestratorConfig_Factory.cs` | `Get_EffortForRole_OrNull(SessionRoles)` beside `Get_ModelForRole` |
| `AIOrchestratorCoreLib/Configuration/EffortSettings/` (new triple + `_Json`) | the `effort` block's parser, shaped on `DefaultsSettings_Json` |
| `AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs` | `SUPERVISION_EFFORT_LEVEL` and `Resolve_Effort_OrDefault` deleted; `First_InvalidModelCharacter_OrNull` extracted from `Validate_Model` and made public so the catalogue validates model words through ONE implementation |
| `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` | the role effort default is resolved from the config, not left to the builder |
| `AIOrchestratorCoreLib.Tests/Spawning/SpawnCommandBuilderTests.cs`, `AIOrchestratorCoreLib.Tests/Launching/OrchestrationLauncherTests.cs` | the named cases move the role default from the builder to the launcher |

---

### Task 1: The catalogue's vocabulary and the definition triple

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingKinds.cs`, `SettingScopes.cs`, `SettingCategories.cs`, `SettingRenderers.cs`, `RestartKinds.cs`, `SettingOrigins.cs`, `PulseField_Names.cs`
- Create: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingDefinition/ISettingDefinition.cs`, `SettingDefinitionModel.cs`, `SettingDefinition_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs` (extract `First_InvalidModelCharacter_OrNull`)
- Test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingDefinitionFactoryTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks. `AIOrchestratorCoreLib.Telegram.EffortLevels.ALL` (`["low","medium","high","xhigh","max"]`, verified in the tree) and `AIOrchestratorCoreLib.Running.SessionRole_Names.ALL`.
- Produces, for every later task:
  - `enum SettingKinds { Bool, Enum, Int, String, StringList, Composite }`
  - `enum SettingScopes { Machine, Orchestration }`
  - `enum SettingCategories { Phone, Pulse, Receipts, Models, Kernel, Kit, Owner }`
  - `enum SettingRenderers { Toggle, Choice, Number, Text, OrderedList, ReadOnly }`
  - `enum RestartKinds { None, NextSpawn, Host }`
  - `enum SettingOrigins { ShippedDefault, Preset, ConfigFile, Session }`
  - `static class PulseField_Names { IReadOnlyList<string> ALL }`
  - `ISettingDefinition` with: `string Path`, `string? LegacyPath_OrNull`, `SettingKinds Kind`, `JsonNode? Default_OrNull`, `IReadOnlyList<string> EnumValues`, `int? Minimum`, `int? Maximum`, `SettingScopes Scope`, `SettingCategories Category`, `string Label`, `string Description`, `RestartKinds Restart`, `SettingRenderers Renderer`, `string? CompositeParser_OrNull`, `string? Validate_OrNull(JsonNode? value)`
  - `SettingDefinition_Factory.Create_Bool / Create_Enum / Create_Int / Create_String / Create_StringList / Create_Composite`
  - `SpawnCommand_Builder.First_InvalidModelCharacter_OrNull(string model) : char?` (public)

- [ ] **Step 1: Write the failing factory tests**

Create `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingDefinitionFactoryTests.cs`:

```csharp
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// A DEFINITION IS DATA THAT THREE RENDERERS AND ONE RESOLVER READ, so what it refuses to be built
/// with is the whole of its contract: a definition with no label renders as a blank row, and a
/// definition whose default is not of its own kind makes the bottom of the precedence chain a lie.
/// Both are cheaper to refuse at construction than to find in a WPF tab, a Telegram tap and an
/// HTTP PUT separately (spec §12: "a setting that exists in three renderers and one probe is the
/// shape most likely to drift").
/// </summary>
public class SettingDefinitionFactoryTests
{
    [Fact]
    public void Create_Bool_CarriesItsDefaultAsAJsonBoolean_AndRendersAsAToggle()
    {
        var definition = SettingDefinition_Factory.Create_Bool(
            path: "phone.appMessagesRing",
            shippedDefault: true,
            scope: SettingScopes.Machine,
            category: SettingCategories.Phone,
            label: "App messages ring",
            description: "Whether the app's own notices make the phone sound.",
            restart: RestartKinds.None);

        Assert.Equal(SettingKinds.Bool, definition.Kind);
        Assert.Equal(SettingRenderers.Toggle, definition.Renderer);
        Assert.True(definition.Default_OrNull!.GetValue<bool>());
        Assert.Null(definition.Validate_OrNull(JsonValue.Create(false)));
        Assert.NotNull(definition.Validate_OrNull(JsonValue.Create("yes")));
    }

    [Fact]
    public void Create_Enum_RefusesAValueOutsideItsList_AndNamesTheAllowedWords()
    {
        var definition = SettingDefinition_Factory.Create_Enum(
            path: "phone.receipts",
            values: ["ticks", "reactions"],
            shippedDefault: "ticks",
            scope: SettingScopes.Machine,
            category: SettingCategories.Receipts,
            label: "Receipts",
            description: "Ticks on a message, or reactions on the owner's own bubble.",
            restart: RestartKinds.None);

        Assert.Null(definition.Validate_OrNull(JsonValue.Create("reactions")));

        var message = definition.Validate_OrNull(JsonValue.Create("emoji"));

        Assert.NotNull(message);
        Assert.Contains("ticks", message);
        Assert.Contains("reactions", message);
    }

    /// <summary>A null default is legal ONLY where null is a meaning — effort's "no --effort flag".</summary>
    [Fact]
    public void Create_Enum_WithNullableTrue_AcceptsNullAsItsShippedDefault()
    {
        var definition = SettingDefinition_Factory.Create_Enum(
            path: "effort.supervisor",
            values: ["low", "medium", "high", "xhigh", "max"],
            shippedDefault: null,
            scope: SettingScopes.Orchestration,
            category: SettingCategories.Models,
            label: "Supervisor effort",
            description: "null means no --effort flag at all: the CLI's own default.",
            restart: RestartKinds.NextSpawn,
            nullable: true);

        Assert.Null(definition.Default_OrNull);
        Assert.Null(definition.Validate_OrNull(null));
        Assert.Null(definition.Validate_OrNull(JsonValue.Create("xhigh")));
        Assert.NotNull(definition.Validate_OrNull(JsonValue.Create("enormous")));
    }

    [Fact]
    public void Create_Int_RefusesOutsideItsRange_AndNamesBothBounds()
    {
        var definition = SettingDefinition_Factory.Create_Int(
            path: "phone.status.intervalMinutes",
            shippedDefault: 30,
            minimum: 5,
            maximum: 120,
            scope: SettingScopes.Machine,
            category: SettingCategories.Phone,
            label: "Periodic status interval",
            description: "Minutes between periodic STATUS messages.",
            restart: RestartKinds.None);

        Assert.Equal(SettingRenderers.Number, definition.Renderer);
        Assert.Null(definition.Validate_OrNull(JsonValue.Create(30)));

        var message = definition.Validate_OrNull(JsonValue.Create(240));

        Assert.NotNull(message);
        Assert.Contains("5", message);
        Assert.Contains("120", message);
    }

    /// <summary>
    /// THE MODEL WORD IS VALIDATED THROUGH THE SPAWN BUILDER'S OWN CHARSET, not a second copy of it
    /// (CLAUDE.md decision 12). The builder throws because a spawn must not happen; the catalogue
    /// returns a message because a renderer must say why it refused — one implementation, two
    /// reactions.
    /// </summary>
    [Fact]
    public void Create_String_WithTheModelValidator_RefusesAWordTheSpawnBuilderWouldRefuse()
    {
        var definition = SettingDefinition_Factory.Create_String(
            path: "models.supervisor",
            shippedDefault: "opus",
            scope: SettingScopes.Orchestration,
            category: SettingCategories.Models,
            label: "Supervisor model",
            description: "The model a supervisor session spawns with.",
            restart: RestartKinds.NextSpawn,
            legacyPath: "supervisorModel",
            validator: SettingValidators.MODEL_WORD);

        Assert.Equal("supervisorModel", definition.LegacyPath_OrNull);
        Assert.Null(definition.Validate_OrNull(JsonValue.Create("claude-fable-5-1")));
        Assert.NotNull(definition.Validate_OrNull(JsonValue.Create("opus; rm -rf /")));
    }

    [Fact]
    public void Create_Composite_NamesItsParser_AndRendersReadOnly()
    {
        var definition = SettingDefinition_Factory.Create_Composite(
            path: "planBackend",
            parserTypeName: "AIOrchestratorCoreLib.Planning.PlanBackend.PlanBackend_Loader",
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Plan backend",
            description: "Which component reads the ledger; hand-edited, read and never written.",
            restart: RestartKinds.Host);

        Assert.Equal(SettingKinds.Composite, definition.Kind);
        Assert.Equal(SettingRenderers.ReadOnly, definition.Renderer);
        Assert.Equal("AIOrchestratorCoreLib.Planning.PlanBackend.PlanBackend_Loader", definition.CompositeParser_OrNull);
    }

    [Fact]
    public void Create_Bool_WithABlankLabel_Throws_NamingThePath()
    {
        var thrown = Assert.Throws<ArgumentException>(() => SettingDefinition_Factory.Create_Bool(
            path: "phone.appMessagesRing",
            shippedDefault: true,
            scope: SettingScopes.Machine,
            category: SettingCategories.Phone,
            label: "   ",
            description: "…",
            restart: RestartKinds.None));

        Assert.Contains("phone.appMessagesRing", thrown.Message);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-plan02
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingDefinitionFactoryTests"
```
Expected: **compile errors** — none of `SettingKinds`, `SettingDefinition_Factory`, `SettingValidators` exists yet.

- [ ] **Step 3: Write the six vocabulary enums and `PulseField_Names`**

All in namespace `AIOrchestratorCoreLib.Configuration.SettingsCatalog`, one file each, each with an XML doc that says what the value MEANS rather than restating the word. Exact members:

```csharp
public enum SettingKinds { Bool, Enum, Int, String, StringList, Composite }
public enum SettingScopes { Machine, Orchestration }
public enum SettingCategories { Phone, Pulse, Receipts, Models, Kernel, Kit, Owner }
public enum SettingRenderers { Toggle, Choice, Number, Text, OrderedList, ReadOnly }
public enum RestartKinds { None, NextSpawn, Host }
public enum SettingOrigins { ShippedDefault, Preset, ConfigFile, Session }
```

`RestartKinds` carries this doc, verbatim (spec §6.1: "shown by the renderers, enforced by nobody"):

```csharp
/// <summary>
/// How far a change has to travel before it takes effect: <c>None</c> — the provider's write-stamp
/// reload picks it up on the next tick; <c>NextSpawn</c> — the sessions running now keep the old
/// value, the next one spawned gets the new one; <c>Host</c> — the app or the daemon has to be
/// restarted.
///
/// <para>
/// SHOWN BY THE RENDERERS AND ENFORCED BY NOBODY, deliberately (spec §6.1). An app that refused to
/// save a Host-level change until it could restart itself would be an app the owner cannot configure
/// from their phone, which is the thing this catalogue exists to make possible.
/// </para>
/// </summary>
```

`PulseField_Names.cs`:

```csharp
/// <summary>
/// THE EIGHT FIELDS A PULSE MAY CARRY, in the order the shipped default lists them. The words, not
/// the builders: `pulse.fields` is an ORDERED LIST setting whose values are these, and plan 03 makes
/// TopicStatusLine_Builder.Build iterate the resolved list calling one private per word. Registered
/// here in plan 02 so the setting can be validated before anything reads it — a list validated
/// against a builder that does not iterate it yet is still a list that cannot hold a typo.
/// </summary>
public static class PulseField_Names
{
    public const string WAITING_ON_YOU = "waitingOnYou";
    public const string SUPERVISOR = "supervisor";
    public const string MEMBERS = "members";
    public const string CLOSED_COUNT = "closedCount";
    public const string LAST_EVENT = "lastEvent";
    public const string MERGED = "merged";
    public const string MODEL_EFFORT = "modelEffort";
    public const string UPDATED = "updated";

    public static readonly IReadOnlyList<string> ALL =
        [WAITING_ON_YOU, SUPERVISOR, MEMBERS, CLOSED_COUNT, LAST_EVENT, MERGED, MODEL_EFFORT, UPDATED];
}
```

- [ ] **Step 4: Extract the model charset from `SpawnCommand_Builder`, one implementation**

In `AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs`, the private `Validate_Model` currently loops the characters itself (around line 268). Replace its loop with a call, and make the check public:

```csharp
/// <summary>
/// The first character of <paramref name="model"/> that a model word may not hold, or null when
/// every character is legal: letters, digits, '-', '_' and '.'. PUBLIC and separate from
/// <see cref="Validate_Model"/> because there are now two reactions to the same fact and only one
/// may own the charset (CLAUDE.md decision 12, the rule about a second copy of a formatter): a
/// SPAWN throws, because a model word travels through a shell command line and a spawn that would
/// break out of its quoting must not happen; a SETTINGS RENDERER returns a message naming the
/// character, because refusing the owner's typing without saying why is the silence decision 21 is
/// about. Added 2026-09-12 with the settings catalogue, whose `models.*` entries validate through it.
/// </summary>
public static char? First_InvalidModelCharacter_OrNull(string model)
{
    foreach (var character in model)
    {
        var valid = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_' || character == '.';

        if (!valid)
            return character;
    }

    return null;
}

static string Validate_Model(string model)
{
    var invalid = First_InvalidModelCharacter_OrNull(model);

    if (invalid != null)
        throw new ArgumentException($"Model '{model}' contains invalid character '{invalid}' — a model name may hold letters, digits, '-', '_' or '.' (it travels through a shell command)");

    return model;
}
```

Keep the existing XML doc on `Validate_Model` exactly as it is; only its body changes.

- [ ] **Step 5: Write `SettingValidators` and the definition triple**

`SettingsCatalog/SettingDefinition/SettingDefinition_Factory.cs` also hosts the named validators, as a sibling static class in the same namespace (`SettingsCatalog`), in its own file `SettingsCatalog/SettingValidators.cs`:

```csharp
/// <summary>
/// The named validators a String or StringList definition may point at. A NAME rather than a
/// delegate in the registry so a definition stays plain data that a renderer can serialise — the
/// web page's GET /settings (plan 04) hands the browser the catalogue itself.
/// </summary>
public static class SettingValidators
{
    public const string NONE = "none";

    /// <summary>Letters, digits, '-', '_' and '.', through <see cref="Spawning.SpawnCommand_Builder.First_InvalidModelCharacter_OrNull"/>.</summary>
    public const string MODEL_WORD = "modelWord";

    /// <summary>`host:port`, or the literal `off`. Used by `web.listen`.</summary>
    public const string LISTEN_ADDRESS = "listenAddress";

    /// <summary>Every element must be one of `PulseField_Names.ALL`, with no repeats.</summary>
    public const string PULSE_FIELDS = "pulseFields";

    /// <summary>Every element must be a command in `Telegram.BotCommandMenu.ALL`, with no repeats.</summary>
    public const string BOT_COMMANDS = "botCommands";

    /// <summary>The message, or null when the value is acceptable.</summary>
    public static string? Validate_OrNull(string validatorName, JsonNode? value) { /* switch on the name */ }
}
```

`ISettingDefinition` carries exactly the members listed in this task's Interfaces block. `SettingDefinitionModel` is `internal sealed`, get-only properties from a primary constructor, and implements `Validate_OrNull` by switching on `Kind` first (type check), then the range / enum list, then `SettingValidators.Validate_OrNull(_validatorName, value)`. The factory's `Create_*` methods each set `Kind` and `Renderer` together (Bool→Toggle, Enum→Choice, Int→Number, String→Text, StringList→OrderedList, Composite→ReadOnly), throw `ArgumentException` naming `path` when `label` or `description` is blank or when `path` is blank, and take `legacyPath` and `validator` as optional trailing parameters defaulting to `null` / `SettingValidators.NONE`.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingDefinitionFactoryTests"
```
Expected: `Passed! - Failed: 0, Passed: 7`.

- [ ] **Step 7: Prove the extraction changed nothing at the spawn**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SpawnCommandBuilderTests"
```
Expected: all pass, unchanged — including the `Validate_Model` cases. If any fail, the extraction changed behaviour and must be redone before continuing.

- [ ] **Step 8: Commit**

```bash
git add AIOrchestratorCoreLib/Configuration/SettingsCatalog AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog
git commit -F <tempfile>   # "feat(settings): the catalogue's vocabulary and the setting-definition triple"
```

---

### Task 2: The dotted-path reader over the raw JSON tree

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsJson_Path.cs`
- Test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsJsonPathTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 (pure `System.Text.Json.Nodes`). Written second only so Task 3 can use it.
- Produces:
  - `SettingsJson_Path.Read_OrNull(JsonObject? root, string path) : JsonNode?`
  - `SettingsJson_Path.Write(JsonObject root, string path, JsonNode? value) : void` (creates intermediate objects)
  - `SettingsJson_Path.Remove(JsonObject root, string path) : bool` (the renderers' **Reset**)

- [ ] **Step 1: Write the failing tests**

Create `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsJsonPathTests.cs`:

```csharp
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// ONE WALK OVER THE RAW TREE, because the alternative is a parser per block. The fork's loader
/// already keeps the JsonObject for Save (spec §6.1: "New scalar keys are read by one generic
/// SettingsReader that walks the raw JsonObject"), so every catalogue key — flat like
/// `telegramInbound`, nested like `phone.status.periodic`, three deep like `runners.supervisor.runner`
/// — is the same operation on the same object.
/// </summary>
public class SettingsJsonPathTests
{
    static JsonObject Tree() => (JsonObject)JsonNode.Parse("""
        {
          "telegramInbound": "poll",
          "phone": { "push": "filtered", "status": { "periodic": true, "intervalMinutes": 30 } },
          "runners": { "supervisor": { "runner": "stream" } }
        }
        """)!;

    [Fact]
    public void Read_AFlatKey_ReturnsItsValue()
    {
        Assert.Equal("poll", SettingsJson_Path.Read_OrNull(Tree(), "telegramInbound")!.GetValue<string>());
    }

    [Fact]
    public void Read_AThreeSegmentPath_WalksEveryObject()
    {
        Assert.True(SettingsJson_Path.Read_OrNull(Tree(), "phone.status.periodic")!.GetValue<bool>());
        Assert.Equal("stream", SettingsJson_Path.Read_OrNull(Tree(), "runners.supervisor.runner")!.GetValue<string>());
    }

    /// <summary>
    /// AN ABSENT KEY AND A NULL ROOT ARE THE SAME ANSWER, and that is what makes the resolver's
    /// layers composable: "this layer says nothing" must not need a try/catch at four call sites.
    /// </summary>
    [Fact]
    public void Read_AnAbsentKey_AnAbsentParent_OrANullRoot_AllReadAsAbsent()
    {
        Assert.Null(SettingsJson_Path.Read_OrNull(Tree(), "phone.receipts"));
        Assert.Null(SettingsJson_Path.Read_OrNull(Tree(), "pulse.fields"));
        Assert.Null(SettingsJson_Path.Read_OrNull(Tree(), "pulse.holdToggle.deeper"));
        Assert.Null(SettingsJson_Path.Read_OrNull(null, "telegramInbound"));
    }

    /// <summary>
    /// A SEGMENT THAT IS NOT AN OBJECT STOPS THE WALK RATHER THAN THROWING. `{"phone": 3}` is a
    /// plausible hand-edit, and the loader's own readers were made tolerant for exactly this reason
    /// on 2026-09-10: a typo costs that ONE setting its default, never the app's startup.
    /// </summary>
    [Fact]
    public void Read_ThroughAScalarSegment_IsAbsent_NotAThrow()
    {
        var tree = (JsonObject)JsonNode.Parse("""{"phone": 3}""")!;

        Assert.Null(Record.Exception(() => SettingsJson_Path.Read_OrNull(tree, "phone.push")));
        Assert.Null(SettingsJson_Path.Read_OrNull(tree, "phone.push"));
    }

    [Fact]
    public void Write_CreatesEveryMissingParent_AndLeavesSiblingsAlone()
    {
        var tree = Tree();

        SettingsJson_Path.Write(tree, "pulse.holdToggle", JsonValue.Create(false));
        SettingsJson_Path.Write(tree, "phone.receipts", JsonValue.Create("reactions"));

        Assert.False(SettingsJson_Path.Read_OrNull(tree, "pulse.holdToggle")!.GetValue<bool>());
        Assert.Equal("reactions", SettingsJson_Path.Read_OrNull(tree, "phone.receipts")!.GetValue<string>());
        Assert.Equal("filtered", SettingsJson_Path.Read_OrNull(tree, "phone.push")!.GetValue<string>());
        Assert.True(SettingsJson_Path.Read_OrNull(tree, "phone.status.periodic")!.GetValue<bool>());
    }

    /// <summary>
    /// RESET DELETES THE KEY, it does not write the default — the rule of spec §6.2, and the same
    /// rule the loader already keeps for reviewerModel: a materialised default is a default that can
    /// never move again, frozen on the first button press.
    /// </summary>
    [Fact]
    public void Remove_DeletesTheKey_AndReportsWhetherThereWasOne()
    {
        var tree = Tree();

        Assert.True(SettingsJson_Path.Remove(tree, "phone.status.intervalMinutes"));
        Assert.Null(SettingsJson_Path.Read_OrNull(tree, "phone.status.intervalMinutes"));
        Assert.True(SettingsJson_Path.Read_OrNull(tree, "phone.status.periodic")!.GetValue<bool>());
        Assert.False(SettingsJson_Path.Remove(tree, "phone.status.intervalMinutes"));
        Assert.False(SettingsJson_Path.Remove(tree, "nothing.here"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsJsonPathTests"
```
Expected: compile errors — `SettingsJson_Path` does not exist.

- [ ] **Step 3: Write `SettingsJson_Path`**

`public static class SettingsJson_Path` in namespace `AIOrchestratorCoreLib.Configuration.SettingsCatalog`. Split on `'.'`; walk with `node as JsonObject` (a non-object segment returns null / stops); `Write` creates a `new JsonObject()` for each missing intermediate; `Remove` walks to the parent and calls `parent.Remove(lastSegment)`. Use `JsonNode.DeepClone()` on the value before assigning in `Write` — a `JsonNode` already attached to another tree throws `InvalidOperationException` when re-parented, and the resolver hands values between trees. Its class doc states that last fact with the date (2026-09-12) as the reason the clone is there.

- [ ] **Step 4: Run to verify it passes**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsJsonPathTests"
```
Expected: `Failed: 0, Passed: 7`.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsJson_Path.cs AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsJsonPathTests.cs
git commit -F <tempfile>   # "feat(settings): one walk over the raw config tree for every catalogue path"
```

---

### Task 3: The registry — every key, existing and new

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs`
- Test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsCatalogTests.cs`

**Interfaces:**
- Consumes: Task 1's `SettingDefinition_Factory`, the enums, `PulseField_Names`, `SettingValidators`. Task 2's `SettingsJson_Path` (not needed here, but the registry lives beside it).
- Consumes from the existing tree, for the defaults — **read each constant, never retype the number**: `RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS` (10), `DEFAULT_MAX_CONCURRENT_TURNS_PER_ORCHESTRATION` (3), `DEFAULT_TURN_TIMEOUT` (30 min), `DEFAULT_COALESCE_WINDOW` (3 s), `DEFAULT_SILENCE_LIMIT` (2 min), `DEFAULT_MEMBER_DIGEST_WINDOW` (5 min), `DEFAULT_SESSION_MEMORY_MAX`; `GuardrailSettings_Factory.DEFAULT_HIGH_RISK_PATTERNS`, `DEFAULT_HIGH_RISK_CODE_EXPIRY_MINUTES` (10), `DEFAULT_DISPATCH_PAUSE_THRESHOLD_PERCENT` (95), `DEFAULT_BUTTON_EXPIRY_MINUTES` (720); `OwnerMessage_Folder.DEFAULT_FOLD_THRESHOLD` (900) and `OwnerDocument_Builder.DEFAULT_ATTACH_ABOVE_CHUNKS` (3) through `TelegramProseSettings_Factory`; `TopicCommandButtons.Commands` (the fork's six: pending, left, tail sup, limits, merge, close) and `TopicCommandButtons.GeneralCommands` (summary, pending, limits, resume, dnd_all); `UnchangedFor_Formatter.STEP_MINUTES` (5); `EffortLevels.ALL`; `SessionRunner_Names` / `ResumeMode_Names` words; `RoleRunnerConfig_Factory.Create_Default(role)` (terminal for all; resume `fresh` for General, `transcript` for the rest); `DefaultsSettings_Factory.DEFAULT_ORCHESTRATION_IS_BASIC` (true → the word `basic`).
- Produces:
  - `SettingsCatalog.ALL : IReadOnlyList<ISettingDefinition>`
  - `SettingsCatalog.Find_OrNull(string path) : ISettingDefinition?` (matches `Path`, then `LegacyPath_OrNull`)
  - `SettingsCatalog.In_Category(SettingCategories category) : IReadOnlyList<ISettingDefinition>`
  - `SettingsCatalog.MODELS_PATH_PREFIX = "models."`, `EFFORT_PATH_PREFIX = "effort."`
  - `SettingsCatalog.Get_ModelPath(SessionRoles role)` and `Get_EffortPath(SessionRoles role)` — the two Task 6 and Task 7 read their defaults through.

- [ ] **Step 1: Write the failing catalogue-invariant tests**

Create `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsCatalogTests.cs`:

```csharp
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE INVARIANTS SPEC §6.1 NAMES, and each one is a defect that would otherwise surface in three
/// renderers separately: "every path is unique; every definition has a default of the declared kind,
/// a label and a description; both shipped presets parse and reference only catalogue paths; a
/// Composite entry names a parser that exists."
/// </summary>
public class SettingsCatalogTests
{
    [Fact]
    public void EveryPath_IsUnique_AndSoIsEveryLegacyPath()
    {
        var paths = SettingsCatalog.ALL.Select(definition => definition.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.Ordinal).Count());

        var legacy = SettingsCatalog.ALL.Where(d => d.LegacyPath_OrNull != null).Select(d => d.LegacyPath_OrNull!).ToList();
        Assert.Equal(legacy.Count, legacy.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(legacy.Intersect(paths, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryDefinition_HasALabelAndADescription()
    {
        foreach (var definition in SettingsCatalog.ALL)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Label), $"{definition.Path} has no label");
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), $"{definition.Path} has no description");
        }
    }

    /// <summary>
    /// THE BOTTOM OF THE PRECEDENCE CHAIN CANNOT BE A LIE. A default of the wrong kind, or one the
    /// definition's own validator refuses, means every layer above it is resolving against nonsense.
    /// </summary>
    [Fact]
    public void EveryShippedDefault_SatisfiesItsOwnDefinition()
    {
        foreach (var definition in SettingsCatalog.ALL)
        {
            if (definition.Kind == SettingKinds.Composite)
                continue;

            Assert.Null(definition.Validate_OrNull(definition.Default_OrNull));
        }
    }

    [Fact]
    public void EveryCompositeEntry_NamesATypeThatExists()
    {
        foreach (var definition in SettingsCatalog.ALL.Where(d => d.Kind == SettingKinds.Composite))
        {
            var type = typeof(SettingsCatalog).Assembly.GetType(definition.CompositeParser_OrNull!);
            Assert.True(type != null, $"{definition.Path} names parser '{definition.CompositeParser_OrNull}', which is not a type in this assembly");
        }
    }

    /// <summary>
    /// A CATEGORY IS THE MENU STRUCTURE OF EVERY RENDERER (spec §6.1), so an empty one is a blank
    /// tab. `Kit` is the one exemption and it is named rather than skipped by a predicate: it fills
    /// in plan 05 with `repos[].code` and the per-user exports, and naming it here means the day
    /// that lands, this test is the thing that notices the exemption is stale.
    /// </summary>
    [Fact]
    public void EveryCategory_ExceptKit_HasAtLeastOneEntry()
    {
        foreach (var category in Enum.GetValues<SettingCategories>())
        {
            if (category == SettingCategories.Kit)
                continue;

            Assert.NotEmpty(SettingsCatalog.In_Category(category));
        }
    }

    [Fact]
    public void EveryRole_HasAModelEntryAndAnEffortEntry()
    {
        foreach (var role in SessionRole_Names.ALL)
        {
            Assert.NotNull(SettingsCatalog.Find_OrNull(SettingsCatalog.Get_ModelPath(role)));
            Assert.NotNull(SettingsCatalog.Find_OrNull(SettingsCatalog.Get_EffortPath(role)));
        }
    }

    /// <summary>
    /// THE OLD SPELLING STILL FINDS THE SETTING. Six model keys and two telegram keys are re-homed
    /// under a block by spec §6.4 (`telegram.foldLongEntriesAbove` → `phone.foldLongEntriesAbove`,
    /// "with the old path read as an alias"), and every config.json on both brothers' machines is
    /// written in the old spelling. A rename that loses the owner's value is a model change nobody
    /// asked for — the exact failure spec §5.2 warns about for the model default.
    /// </summary>
    [Fact]
    public void TheLegacySpellings_StillResolveToTheirDefinitions()
    {
        Assert.Equal("models.supervisor", SettingsCatalog.Find_OrNull("supervisorModel")!.Path);
        Assert.Equal("models.general", SettingsCatalog.Find_OrNull("generalSupervisorModel")!.Path);
        Assert.Equal("phone.foldLongEntriesAbove", SettingsCatalog.Find_OrNull("telegram.foldLongEntriesAbove")!.Path);
        Assert.Equal("phone.attachEntriesAbove", SettingsCatalog.Find_OrNull("telegram.attachEntriesAbove")!.Path);
    }

    /// <summary>
    /// THE BOT TOKEN IS NOT IN THE CATALOGUE, and this is an assertion rather than a comment because
    /// every renderer lists the catalogue: a Telegram /settings menu that prints the token posts the
    /// token into the chat the token controls. It lives in secrets.json, which the loader reads
    /// separately and this registry never names.
    /// </summary>
    [Fact]
    public void NoDefinition_ExposesTheBotToken()
    {
        Assert.DoesNotContain(SettingsCatalog.ALL, d =>
            d.Path.Contains("token", StringComparison.OrdinalIgnoreCase) && d.Path.Contains("bot", StringComparison.OrdinalIgnoreCase));
        Assert.Null(SettingsCatalog.Find_OrNull("telegramBotToken"));
    }

    /// <summary>
    /// THE FOUR ORCHESTRATION-SCOPE DIALS, and only those four plus the three session states: Scope
    /// says WHERE the topmost layer of a value may live, and a renderer that writes a Machine-scope
    /// key into session.json would write it where no reader looks.
    /// </summary>
    [Fact]
    public void OnlyTheDialsAndTheSessionStates_AreOrchestrationScoped()
    {
        var orchestrationScoped = SettingsCatalog.ALL
            .Where(d => d.Scope == SettingScopes.Orchestration)
            .Select(d => d.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["effort.implementer", "effort.supervisor", "models.implementer", "models.supervisor",
             "session.ownerPresence", "session.paused", "session.telegramMode"],
            orchestrationScoped);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsCatalogTests"
```
Expected: compile errors — `SettingsCatalog` does not exist.

- [ ] **Step 3: Write the registry — this is the whole of catalogue v1**

`public static class SettingsCatalog` in namespace `AIOrchestratorCoreLib.Configuration.SettingsCatalog`, with `ALL` built once in a static initialiser. Every row below is one `SettingDefinition_Factory.Create_*` call. **Register exactly these, no more and no fewer.** The `default` column is the SHIPPED default (the bottom rung); the preset columns are Task 4's, not this task's.

**Category `Models`** (`Restart = NextSpawn` on all twelve)

| path | legacy path | kind | shipped default | scope | renderer |
|---|---|---|---|---|---|
| `models.supervisor` | `supervisorModel` | String (`MODEL_WORD`) | `"opus"` | Orchestration | Text |
| `models.implementer` | `implementerModel` | String (`MODEL_WORD`) | `"opus"` | Orchestration | Text |
| `models.reviewer` | `reviewerModel` | String (`MODEL_WORD`) | `"opus"` | Machine | Text |
| `models.solo` | `soloModel` | String (`MODEL_WORD`) | `"opus"` | Machine | Text |
| `models.general` | `generalSupervisorModel` | String (`MODEL_WORD`) | `"sonnet"` | Machine | Text |
| `models.communicator` | `communicatorModel` | String (`MODEL_WORD`) | `"sonnet"` | Machine | Text |
| `effort.supervisor` | — | Enum(`EffortLevels.ALL`), nullable | `null` | Orchestration | Choice |
| `effort.implementer` | — | Enum(`EffortLevels.ALL`), nullable | `null` | Orchestration | Choice |
| `effort.reviewer`, `effort.solo`, `effort.general`, `effort.communicator` | — | Enum(`EffortLevels.ALL`), nullable | `null` | Machine | Choice |

`models.*` descriptions must record the ladder that already exists — an absent `models.reviewer` or `models.solo` falls to `models.implementer` before the shipped default (`OrchestratorConfig_Factory.First_StatedModel`), and that ladder is UNCHANGED by this plan. `effort.*` descriptions must say that `null` means **no `--effort` flag at all**, the CLI's own default, and that the per-orchestration `/effort` dial (`session.json`'s `supervisorEffortOverride` / `implementerEffortOverride`) is the layer above — a solo sits on the implementer slot, as it does for the model.

**Category `Kernel`** (`Restart = Host` unless noted)

| path | kind | shipped default | renderer | notes |
|---|---|---|---|---|
| `runners.<role>.runner` ×6 | Enum(`terminal`,`print`,`stream`) | `terminal` | Choice | `Restart = NextSpawn`. `bg` is in the enum `SessionRunners` and NOT offered: the fork names it and has not implemented it (spec §3) |
| `runners.<role>.resume` ×6 | Enum(`transcript`,`fresh`) | `transcript`, `fresh` for `general` | Choice | `Restart = NextSpawn`; read `RoleRunnerConfig_Factory.Create_Default(role)` for each |
| `runners.sessionMemoryMax` | String | `RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX` | Text | Linux cgroup only — say so in the description |
| `printRunner.maxConcurrentTurns` | Int(1,100) | 10 | Number | |
| `printRunner.maxConcurrentTurnsPerOrchestration` | Int(1,50) | 3 | Number | |
| `printRunner.turnTimeoutMinutes` | Int(1,600) | 30 | Number | |
| `printRunner.coalesceSeconds` | Int(0,600) | 3 | Number | |
| `printRunner.streamSilenceSeconds` | Int(1,3600) | 120 | Number | |
| `printRunner.memberDigestMinutes` | Int(0,5) | 5 | Number | 0 = wake immediately; the 5-minute CEILING is the existing refusal's, see `RunnerConfigs_Json.MEMBER_DIGEST_MINUTES_KEY` |
| `telegramInbound` | Enum(`poll`,`off`) | `poll` | Choice | |
| `telegramSupergroupChatId` | Int, nullable | `null` | Number | `Restart = None` |
| `telegramOwnerUserId` | Int, nullable | `null` | Number | `Restart = None` |
| `telegramStatusScreenshots` | Bool | `false` | Toggle | `Restart = None` |
| `voiceTranscribeCommand` | String | `""` | Text | `Restart = None` |
| `orchestrationTokenBudget` | Int, nullable | `null` | Number | `Restart = None` |
| `highRiskPatterns` | StringList | `GuardrailSettings_Factory.DEFAULT_HIGH_RISK_PATTERNS` | OrderedList | an EMPTY list is not absent — it is "nothing is high risk", which is the owner's to say |
| `highRiskCodeExpiryMinutes` | Int(1,1440) | 10 | Number | |
| `dispatchPauseThresholdPercent` | Int(1,100) | 95 | Number | |
| `buttonExpiryMinutes` | Int(1,10080) | 720 | Number | |
| `defaults.orchestrationMode` | Enum(`basic`,`full`) | `basic` | Choice | `Restart = None` |
| `web.listen` | String (`LISTEN_ADDRESS`) | `"127.0.0.1:7391"` | Text | **read by nothing in this plan** — the listener is plan 04 |
| `web.token` | String | `""` | Text | as above |
| `repos` | Composite → `AIOrchestratorCoreLib.Configuration.OrchestratorConfig_Loader` | — | ReadOnly | |
| `planBackend` | Composite → `AIOrchestratorCoreLib.Planning.PlanBackend.PlanBackend_Loader` | — | ReadOnly | |

**Category `Phone`** (every one of these is INERT until plan 03 — say so in each description)

| path | legacy | kind | shipped default | restart |
|---|---|---|---|---|
| `phone.push` | — | Enum(`filtered`,`everything`) | `filtered` | None |
| `phone.status.periodic` | — | Bool | `true` | None |
| `phone.status.intervalMinutes` | — | Int(5,120) | 30 | None |
| `phone.appMessagesRing` | — | Bool | `true` | None |
| `phone.replyKeyboard` | — | Enum(`off`,`on`) | `off` | Host |
| `phone.foldLongEntriesAbove` | `telegram.foldLongEntriesAbove` | Int(1,10000) | 900 | None |
| `phone.attachEntriesAbove` | `telegram.attachEntriesAbove` | Int(1,100) | 3 | None |
| `topic.onClose` | — | Enum(`delete`,`close`) | `close` | None |
| `topic.modeGlyphs` | — | Enum(`name`,`pulseHeader`) | `pulseHeader` | None |

`topic.modeGlyphs`'s description must carry the fork's reason for moving the glyphs off the name: **each rename writes a service message** (spec §7.4).

**Category `Receipts`**

| path | kind | shipped default | restart |
|---|---|---|---|
| `phone.receipts` | Enum(`ticks`,`reactions`) | `ticks` | None |
| `pulse.holdToggle` | Bool | `true` | None |

`pulse.holdToggle`'s description states the constraint it encodes: `true` puts ⏸/▶ on the PULSE bar, `false` on the receipt — **never both** (CLAUDE.md decision 12's "one toggle in two places").

**Category `Pulse`**

| path | kind | shipped default | restart |
|---|---|---|---|
| `pulse.fields` | StringList (`PULSE_FIELDS`) | `[waitingOnYou, supervisor, members, closedCount, lastEvent, merged, updated]` — the seven of spec §6.4, **`modelEffort` legal but not shipped** | None |
| `pulse.stepMinutes` | Int(1,60) | `UnchangedFor_Formatter.STEP_MINUTES` (5) | None |
| `pulse.buttons` | StringList (`BOT_COMMANDS`) | `TopicCommandButtons.Commands` | None |
| `general.buttons` | StringList (`BOT_COMMANDS`) | `TopicCommandButtons.GeneralCommands` | None |

`pulse.stepMinutes`'s description cites the reason the floor is not lower by default: the 429 evidence of 2026-09-10.

**Category `Owner`**

| path | kind | shipped default | restart |
|---|---|---|---|
| `owner.name` | String | `""` | NextSpawn |
| `owner.language` | Enum(`auto`,`en`,`it`) | `auto` | NextSpawn |

`owner.language`'s description carries the decision in full, because it is the only machine-readable home the rule has (spec §7.8, owner 2026-09-12): the app-side translation layer is GONE; `auto` means the fork's rule — *write to the owner in the language they used; everything on disk or addressed to another agent stays English*. Nothing reads this key until plan 05 exports it as `AIORCH_OWNER_LANGUAGE`.

**Category `Kit`** — EMPTY in v1. Add a comment in the registry saying `repos[].code` and the per-user exports land here in plan 05, and that `SettingsCatalogTests.EveryCategory_ExceptKit_HasAtLeastOneEntry` names the exemption.

**Orchestration state, `Kernel` category, `ReadOnly`:** `session.paused` (Bool, `false`), `session.telegramMode` (Enum over `TelegramDeliveryModes`' words — read the enum in `Telegram/TelegramDeliveryModes.cs` and use its own names, default the live/"Live" word), `session.ownerPresence` (Enum over `Telegram/OwnerPresenceModes`, same treatment). All three `Scope = Orchestration`, `Restart = None`, description saying they are per-orchestration STATE the app writes, shown so a renderer can display an orchestration's current mode — never edited through the catalogue.

`Get_ModelPath(role)` returns `"models." + SessionRole_Names.Get_ConfigKey(role)`; `Get_EffortPath(role)` returns `"effort." + SessionRole_Names.Get_ConfigKey(role)`. `Find_OrNull` matches `Path` with `StringComparison.Ordinal`, then `LegacyPath_OrNull`.

- [ ] **Step 4: Run to verify it passes**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsCatalogTests"
```
Expected: `Failed: 0, Passed: 9`. If `OnlyTheDialsAndTheSessionStates_AreOrchestrationScoped` fails, the message names the extra or missing path — fix the registry, not the test.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsCatalogTests.cs
git commit -F <tempfile>   # "feat(settings): catalogue v1 — every key the merged tree reads, registered as data"
```

---

### Task 4: The two presets, shipped as embedded data

**Files:**
- Create: `kit/presets/classic.json`, `kit/presets/quiet.json`
- Create: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/Presets_Loader.cs`
- Modify: `AIOrchestratorCoreLib/AIOrchestratorCoreLib.csproj`, `AIOrchestrator/AIOrchestrator.csproj`, `AIOrchestrator.Daemon/AIOrchestrator.Daemon.csproj`
- Test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetsLoaderTests.cs`

**Interfaces:**
- Consumes: Task 2's `SettingsJson_Path`, Task 3's `SettingsCatalog`.
- Produces:
  - `Presets_Loader.PRESET_KEY = "preset"`, `CLASSIC = "classic"`, `QUIET = "quiet"`
  - `Presets_Loader.Load_Embedded(string name) : JsonObject` — throws `ArgumentException` naming the word for anything else
  - `Presets_Loader.Resolve_ForConfig(JsonObject? configRoot) : (JsonObject Tree, string Name)` — reads `preset`, absent/blank = `classic`, a value containing a directory separator or ending `.json` is a FILE PATH read from disk
  - `Presets_Loader.Embedded_Json(string name) : string` — the raw text, for the byte-equality test
  - `Presets_Loader.KIT_RELATIVE_FOLDER = "kit/presets"`

- [ ] **Step 1: Write the two preset files**

`kit/presets/classic.json` — Manu's way. Only keys whose value differs from the shipped default (a preset that restates a default is a default that can never move):

```json
{
  "models.supervisor": "claude-fable-5-1",
  "models.implementer": "claude-fable-5-1",
  "models.reviewer": "claude-fable-5-1",
  "models.solo": "claude-fable-5-1",
  "effort.supervisor": "xhigh",
  "effort.solo": "xhigh",
  "phone.replyKeyboard": "on",
  "topic.modeGlyphs": "name",
  "pulse.fields": ["supervisor", "members", "modelEffort", "merged", "updated"],
  "pulse.buttons": ["screen", "show", "merge", "test", "pc", "close", "pause", "progress"],
  "pulse.holdToggle": false,
  "general.buttons": []
}
```

`kit/presets/quiet.json` — Nathan's way:

```json
{
  "phone.push": "everything",
  "phone.status.periodic": false,
  "phone.appMessagesRing": false,
  "phone.receipts": "reactions",
  "topic.onClose": "delete",
  "runners.supervisor.runner": "stream",
  "runners.implementer.runner": "print",
  "runners.reviewer.runner": "print",
  "runners.solo.runner": "print",
  "runners.general.runner": "print",
  "runners.implementer.resume": "fresh",
  "runners.reviewer.resume": "fresh",
  "runners.solo.resume": "fresh"
}
```

**Two notes the implementer must act on, not skip:**

1. `pulse.buttons` in `classic` names eight verbs (spec §6.4). `SettingValidators.BOT_COMMANDS` validates against `Telegram.BotCommandMenu.ALL`. **Read `AIOrchestratorCoreLib/Telegram/BotCommandMenu.cs` and check all eight are there.** Any word that is NOT in `ALL` is dropped from `classic.json` and recorded in the commit body with the exact list — a preset that names a verb the bot does not have would fail Step 4's own test, and inventing the verb is plan 03's job, not this one's.
2. Keys are written as **flat dotted paths**, which is legal `config.json` only because `SettingsJson_Path` reads a dotted path and a config file may also nest. The presets use the flat form deliberately: a preset is a list of catalogue paths, and the flat form makes "does this preset name only catalogue paths" a one-line test. Record that in a `"_comment"` key at the top of each file — **and register nothing for `_comment`**: Step 4's test skips any key starting with `_`.

- [ ] **Step 2: Write the failing loader tests**

Create `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetsLoaderTests.cs`:

```csharp
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Tests.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE PRESETS ARE DATA AND THE RENDERERS NEVER SPECIAL-CASE A NAME (spec §6.3), so everything that
/// could make a preset wrong has to be caught here: a path that is not in the catalogue, a value the
/// catalogue would refuse, and an embedded copy that has drifted from the kit copy — the same
/// two-copies problem `ChannelGrammarTests` closes for the channel grammar.
/// </summary>
public class PresetsLoaderTests
{
    [Theory]
    [InlineData(Presets_Loader.CLASSIC)]
    [InlineData(Presets_Loader.QUIET)]
    public void EveryPresetKey_IsACataloguePath_WithAValueTheCatalogueAccepts(string name)
    {
        var preset = Presets_Loader.Load_Embedded(name);

        foreach (var (key, value) in preset)
        {
            if (key.StartsWith('_'))
                continue;

            var definition = SettingsCatalog.Find_OrNull(key);

            Assert.True(definition != null, $"preset '{name}' names '{key}', which is not a catalogue path");
            Assert.Null(definition!.Validate_OrNull(value));
        }
    }

    /// <summary>
    /// A PRESET THAT RESTATES A SHIPPED DEFAULT IS A DEFAULT THAT CAN NEVER MOVE — the same reason
    /// the loader refuses to materialise reviewerModel into config.json. It also makes the origin the
    /// renderers show a lie: "from preset classic" for a value nobody chose.
    /// </summary>
    [Theory]
    [InlineData(Presets_Loader.CLASSIC)]
    [InlineData(Presets_Loader.QUIET)]
    public void NoPresetKey_RestatesItsShippedDefault(string name)
    {
        foreach (var (key, value) in Presets_Loader.Load_Embedded(name))
        {
            if (key.StartsWith('_'))
                continue;

            var shipped = SettingsCatalog.Find_OrNull(key)!.Default_OrNull;

            Assert.False(
                JsonNode.DeepEquals(value, shipped),
                $"preset '{name}' sets '{key}' to the shipped default — delete the line instead");
        }
    }

    /// <summary>
    /// ABSENT MEANS CLASSIC, because that is what master did before the merge (owner, spec §11.3).
    /// A blank value means the same: a cleared field is the owner saying nothing, which is the rule
    /// the model ladder already follows.
    /// </summary>
    [Fact]
    public void AConfigWithNoPresetKey_OrABlankOne_ResolvesToClassic()
    {
        Assert.Equal(Presets_Loader.CLASSIC, Presets_Loader.Resolve_ForConfig(null).Name);
        Assert.Equal(Presets_Loader.CLASSIC, Presets_Loader.Resolve_ForConfig((JsonObject)JsonNode.Parse("""{"repos":[]}""")!).Name);
        Assert.Equal(Presets_Loader.CLASSIC, Presets_Loader.Resolve_ForConfig((JsonObject)JsonNode.Parse("""{"preset":"  "}""")!).Name);
    }

    [Fact]
    public void AConfigNamingQuiet_ResolvesToTheQuietTree()
    {
        var resolved = Presets_Loader.Resolve_ForConfig((JsonObject)JsonNode.Parse("""{"preset":"quiet"}""")!);

        Assert.Equal(Presets_Loader.QUIET, resolved.Name);
        Assert.Equal("everything", resolved.Tree["phone.push"]!.GetValue<string>());
    }

    /// <summary>
    /// AN UNKNOWN NAME THROWS RATHER THAN DEFAULTING (spec §6.2) — the fork's own rule for
    /// planBackend.kind, and for its reason: "externa1" once read as the default and produced no
    /// error at all. A preset silently falling back to classic would give Nathan Manu's phone with
    /// nothing anywhere saying why.
    /// </summary>
    [Fact]
    public void AnUnknownPresetName_Throws_NamingTheWordAndTheKnownOnes()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => Presets_Loader.Resolve_ForConfig((JsonObject)JsonNode.Parse("""{"preset":"quite"}""")!));

        Assert.Contains("quite", thrown.Message);
        Assert.Contains("classic", thrown.Message);
        Assert.Contains("quiet", thrown.Message);
    }

    /// <summary>
    /// THE EMBEDDED COPY IS THE KIT COPY, byte for byte with line endings normalised — exactly what
    /// ChannelGrammarTests asserts for the grammar, and for the same reason: two files that are meant
    /// to be one drift silently, and only a test can tell them apart.
    /// </summary>
    [Theory]
    [InlineData(Presets_Loader.CLASSIC)]
    [InlineData(Presets_Loader.QUIET)]
    public void TheEmbeddedPresetIsTheKitsPreset(string name)
    {
        var onDisk = KitRepoFiles.Find(Path.Combine("kit", "presets", $"{name}.json"));

        Assert.True(onDisk != null, $"could not locate kit/presets/{name}.json walking up from '{AppContext.BaseDirectory}' — this test compares two copies, so a missing one means it compared nothing.");
        Assert.Equal(Normalise(File.ReadAllText(onDisk!)), Normalise(Presets_Loader.Embedded_Json(name)));
    }

    static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd();
}
```

- [ ] **Step 3: Run to verify it fails**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PresetsLoaderTests"
```
Expected: compile errors — `Presets_Loader` does not exist.

- [ ] **Step 4: Embed the presets and ship them beside the kit**

In `AIOrchestratorCoreLib/AIOrchestratorCoreLib.csproj`, in the `<ItemGroup>` that already carries the grammar:

```xml
<!-- THE PRESETS ARE EMBEDDED for the reason the grammar is: Presets_Loader reads them as resources,
     so a missing preset is a broken BUILD rather than an app that starts with nobody's settings.
     The same two files also ship in the kit (AIOrchestrator.csproj, AIOrchestrator.Daemon.csproj)
     so an installer on a fresh machine can seed config.json from one; PresetsLoaderTests fails if
     the two copies differ by a byte. -->
<EmbeddedResource Include="..\kit\presets\classic.json" LogicalName="AIOrchestratorCoreLib.kit.presets.classic.json" />
<EmbeddedResource Include="..\kit\presets\quiet.json" LogicalName="AIOrchestratorCoreLib.kit.presets.quiet.json" />
```

In BOTH `AIOrchestrator/AIOrchestrator.csproj` and `AIOrchestrator.Daemon/AIOrchestrator.Daemon.csproj`, in the kit `<ItemGroup>`, after the grammar line (the daemon's group has no grammar line — put it after `kit/bin/*.sh`):

```xml
<Content Include="..\kit\presets\*.json" Link="kit\presets\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 5: Write `Presets_Loader`**

Copy the shape of `AIOrchestratorCoreLib/Channels/ChannelGrammar.cs` — `Assembly.GetExecutingAssembly().GetManifestResourceStream(RESOURCE_NAME)`, a static cache, and a THROW rather than a default when the resource is missing (its doc gives the same argument: the resource is embedded at build time, so a throw is a broken build, never a runtime surprise on the owner's machine). `Resolve_ForConfig` reads `configRoot?["preset"]` through a try/catch (`GetValue<string?>()` throws for a number — the loader's own readers were made tolerant for this on 2026-09-10; a mistyped `preset` reads as ABSENT, which is `classic`); a value that contains `/`, `\` or ends `.json` is read from disk with `File.ReadAllText` and parsed, and a file that does not exist or does not parse THROWS naming the path — the owner typed a path, and silently ignoring it would be the `externa1` failure again.

- [ ] **Step 6: Run to verify it passes**

```bash
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PresetsLoaderTests"
```
Expected: build 0 errors; `Failed: 0, Passed: 10`.

- [ ] **Step 7: Commit**

```bash
git add kit/presets AIOrchestratorCoreLib/Configuration/SettingsCatalog/Presets_Loader.cs AIOrchestratorCoreLib/AIOrchestratorCoreLib.csproj AIOrchestrator/AIOrchestrator.csproj AIOrchestrator.Daemon/AIOrchestrator.Daemon.csproj AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetsLoaderTests.cs
git commit -F <tempfile>   # "feat(settings): classic and quiet ship as embedded data, validated against the catalogue"
```

---

### Task 5: The resolver — four layers, no disk

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/SessionScoped_Reader.cs`
- Create: `AIOrchestratorCoreLib/Configuration/SettingsCatalog/Settings_Resolver.cs`
- Test: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsResolverTests.cs`

**Interfaces:**
- Consumes: Task 1's enums and `ISettingDefinition`; Task 2's `SettingsJson_Path`; Task 3's `SettingsCatalog`; `AIOrchestratorCoreLib.Sessions.OrchestrationSession.IOrchestrationSession` — the real properties, verified in the tree: `SupervisorModelOverride`, `ImplementerModelOverride`, `SupervisorEffortOverride`, `ImplementerEffortOverride` (all `string?`), `Paused` (`bool`), `TelegramMode` (`Telegram.TelegramDeliveryModes`), `OwnerPresence` (`Telegram.OwnerPresenceModes`).
- Produces:
  - `Settings_Resolver.Resolve(ISettingDefinition definition, JsonObject? presetTree, JsonObject? configTree, IOrchestrationSession? session) : (JsonNode? Value, SettingOrigins Origin)`
  - `Settings_Resolver.Resolve_String_OrNull(...)`, `Resolve_Bool(...)`, `Resolve_Int(...)` — typed conveniences over the same call, so Tasks 6 and 7 do not each write a cast
  - `SessionScoped_Reader.Read_OrNull(ISettingDefinition definition, IOrchestrationSession session) : JsonNode?`

- [ ] **Step 1: Write the failing resolver tests**

Create `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsResolverTests.cs`. `TestSession` is a hand-rolled stub implementing `IOrchestrationSession` — **stubs, not mocks**; read `AIOrchestratorCoreLib.Tests/Sessions/` for an existing stub before writing a new one, and if none exists, write one in this file with every member throwing `NotSupportedException` except the seven the reader touches.

```csharp
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// PRECEDENCE IS TESTED WITHOUT TOUCHING DISK, which is the whole reason the layers are parameters
/// (spec §6.2, following HostOptions_Factory.Create_FromArguments, whose environment reader is a
/// function for the same reason). One [Fact] per rung, so a broken rung names itself.
/// </summary>
public class SettingsResolverTests
{
    static JsonObject Tree(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void WithEveryLayerSilent_TheShippedDefaultWins_AndSaysSo()
    {
        var definition = SettingsCatalog.Find_OrNull("phone.receipts")!;

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree: null, configTree: null, session: null);

        Assert.Equal("ticks", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.ShippedDefault, origin);
    }

    [Fact]
    public void ThePreset_BeatsTheShippedDefault()
    {
        var definition = SettingsCatalog.Find_OrNull("phone.receipts")!;

        var (value, origin) = Settings_Resolver.Resolve(definition, Tree("""{"phone.receipts":"reactions"}"""), configTree: null, session: null);

        Assert.Equal("reactions", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.Preset, origin);
    }

    [Fact]
    public void TheConfigFile_BeatsThePreset()
    {
        var definition = SettingsCatalog.Find_OrNull("phone.receipts")!;

        var (value, origin) = Settings_Resolver.Resolve(
            definition,
            Tree("""{"phone.receipts":"reactions"}"""),
            Tree("""{"phone":{"receipts":"ticks"}}"""),
            session: null);

        Assert.Equal("ticks", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.ConfigFile, origin);
    }

    /// <summary>
    /// THE SESSION IS THE TOP RUNG AND ONLY FOR ORCHESTRATION-SCOPE KEYS. A Machine-scope key read
    /// with a session present must not go looking in session.json — there is nothing there, and a
    /// resolver that looked would make every renderer's origin label unreliable.
    /// </summary>
    [Fact]
    public void TheSession_BeatsTheConfigFile_ForAnOrchestrationScopedKey()
    {
        var definition = SettingsCatalog.Find_OrNull("models.supervisor")!;
        var session = new TestSession { SupervisorModelOverride = "haiku" };

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree: null, Tree("""{"supervisorModel":"opus"}"""), session);

        Assert.Equal("haiku", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.Session, origin);
    }

    [Fact]
    public void AMachineScopedKey_IgnoresTheSessionEntirely()
    {
        var definition = SettingsCatalog.Find_OrNull("phone.receipts")!;

        var (_, origin) = Settings_Resolver.Resolve(definition, presetTree: null, configTree: null, new TestSession { SupervisorModelOverride = "haiku" });

        Assert.Equal(SettingOrigins.ShippedDefault, origin);
    }

    /// <summary>
    /// ABSENT IS ABSENT AND BLANK IS ABSENT (spec §6.2). Proven on this codebase 2026-09-10:
    /// {"reviewerModel":""} handed the reviewer the empty string and the spawn carried NO --model
    /// flag at all — the CLI's own default, neither the owner's answer nor the app's, with no line
    /// anywhere saying so. A cleared field is the owner saying nothing.
    /// </summary>
    [Fact]
    public void ABlankValueInALayer_IsAbsent_AndTheLayerBelowAnswers()
    {
        var definition = SettingsCatalog.Find_OrNull("models.supervisor")!;

        var (value, origin) = Settings_Resolver.Resolve(
            definition,
            Tree("""{"models.supervisor":"claude-fable-5-1"}"""),
            Tree("""{"supervisorModel":"   "}"""),
            session: null);

        Assert.Equal("claude-fable-5-1", value!.GetValue<string>());
        Assert.Equal(SettingOrigins.Preset, origin);
    }

    /// <summary>
    /// THE OLD SPELLING IS READ IN THE SAME LAYER, after the new one. Every config.json on both
    /// machines is written in the old spelling, and a re-homed path that stopped reading it would
    /// silently reset the owner's value to the shipped default.
    /// </summary>
    [Fact]
    public void TheLegacySpellingInTheConfigFile_IsReadWhenTheNewOneIsAbsent()
    {
        var definition = SettingsCatalog.Find_OrNull("phone.foldLongEntriesAbove")!;

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree: null, Tree("""{"telegram":{"foldLongEntriesAbove":1500}}"""), session: null);

        Assert.Equal(1500, value!.GetValue<int>());
        Assert.Equal(SettingOrigins.ConfigFile, origin);
    }

    [Fact]
    public void TheNewSpelling_BeatsTheLegacyOne_InTheSameLayer()
    {
        var definition = SettingsCatalog.Find_OrNull("phone.foldLongEntriesAbove")!;

        var (value, _) = Settings_Resolver.Resolve(
            definition,
            presetTree: null,
            Tree("""{"phone":{"foldLongEntriesAbove":700},"telegram":{"foldLongEntriesAbove":1500}}"""),
            session: null);

        Assert.Equal(700, value!.GetValue<int>());
    }

    /// <summary>
    /// A VALUE THE DEFINITION REFUSES FALLS THROUGH TO THE LAYER BELOW rather than being resolved.
    /// The same direction the loader's tolerant readers already take (2026-09-10): a typo costs that
    /// one setting its default, never the app — and a resolver that returned an out-of-range number
    /// would hand it to a spawn or a timer.
    /// </summary>
    [Fact]
    public void AnInvalidValueInALayer_IsSkipped_AndTheLayerBelowAnswers()
    {
        var definition = SettingsCatalog.Find_OrNull("phone.status.intervalMinutes")!;

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree: null, Tree("""{"phone":{"status":{"intervalMinutes":9000}}}"""), session: null);

        Assert.Equal(30, value!.GetValue<int>());
        Assert.Equal(SettingOrigins.ShippedDefault, origin);
    }

    /// <summary>
    /// NULL IS A REAL ANSWER FOR A NULLABLE ENUM, and it has to be distinguishable from "this layer
    /// said nothing" — `effort.supervisor: null` in the quiet preset MEANS "emit no --effort flag",
    /// which is not the same as the key being absent from a preset that sets xhigh.
    /// </summary>
    [Fact]
    public void AnExplicitJsonNull_ForANullableSetting_IsAnAnswer_NotAnAbsence()
    {
        var definition = SettingsCatalog.Find_OrNull("effort.supervisor")!;

        var (value, origin) = Settings_Resolver.Resolve(
            definition,
            Tree("""{"effort.supervisor":"xhigh"}"""),
            Tree("""{"effort":{"supervisor":null}}"""),
            session: null);

        Assert.Null(value);
        Assert.Equal(SettingOrigins.ConfigFile, origin);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsResolverTests"
```
Expected: compile errors — `Settings_Resolver` does not exist.

- [ ] **Step 3: Write `SessionScoped_Reader`**

One `switch` on `definition.Path`, exhaustive over the seven Orchestration-scope paths, `default => null`:

```csharp
/// <summary>
/// The session.json value behind an Orchestration-scope catalogue path, or null when the session
/// says nothing. A SWITCH rather than reflection or a name convention: session.json's field names
/// and the catalogue's paths are two vocabularies that agree today by hand, and a convention would
/// make a rename on either side fail silently at runtime instead of loudly at compile time.
///
/// <para>
/// A SOLO SITS ON THE IMPLEMENTER SLOT, as it does for the model dial — that is the existing
/// semantic of ImplementerEffortOverride (one implementer-side override covers every member kind),
/// and it is restated here rather than changed.
/// </para>
/// </summary>
public static JsonNode? Read_OrNull(ISettingDefinition definition, IOrchestrationSession session)
{
    return definition.Path switch
    {
        "models.supervisor" => Text_OrNull(session.SupervisorModelOverride),
        "models.implementer" => Text_OrNull(session.ImplementerModelOverride),
        "effort.supervisor" => Text_OrNull(session.SupervisorEffortOverride),
        "effort.implementer" => Text_OrNull(session.ImplementerEffortOverride),
        "session.paused" => JsonValue.Create(session.Paused),
        "session.telegramMode" => JsonValue.Create(session.TelegramMode.ToString()),
        "session.ownerPresence" => JsonValue.Create(session.OwnerPresence.ToString()),
        _ => null,
    };
}
```

`Text_OrNull` returns null for null-or-whitespace (blank is absent, the rule of Step 1's test). Use `SettingsCatalog`'s own path constants if you prefer, but the literals must match the registry exactly — a mismatch is caught by `SettingsCatalogTests.OnlyTheDialsAndTheSessionStates_AreOrchestrationScoped` plus this task's session test.

`session.telegramMode` / `session.ownerPresence` are serialised as the enum's own `ToString()`; the catalogue's enum values for those two entries must be spelled the same way — check `Telegram/TelegramDeliveryModes.cs` and `Telegram/OwnerPresenceModes.cs` and make Task 3's registry agree if it does not.

- [ ] **Step 4: Write `Settings_Resolver`**

```csharp
public static (JsonNode? Value, SettingOrigins Origin) Resolve(
    ISettingDefinition definition,
    JsonObject? presetTree,
    JsonObject? configTree,
    IOrchestrationSession? session)
```

Highest first: session (only when `definition.Scope == SettingScopes.Orchestration` and `session != null`), then `configTree`, then `presetTree`, then `definition.Default_OrNull` with `SettingOrigins.ShippedDefault`. In each JSON layer, read `definition.Path`, then `definition.LegacyPath_OrNull`, both through `SettingsJson_Path.Read_OrNull`. A layer **answers** when its node is present and `definition.Validate_OrNull(node)` returns null; a node that is JSON null answers only when the definition is nullable; a blank string, an absent key and a refused value all mean "this layer says nothing". The class doc states the fall-through direction and its date, and the "absent vs blank vs invalid" rule, so the next reader does not have to derive it from the tests.

The three typed conveniences (`Resolve_String_OrNull`, `Resolve_Bool`, `Resolve_Int`) call `Resolve` and cast, and throw `InvalidOperationException` naming the path if the definition's `Kind` does not match the accessor — a caller asking for a bool from an enum is a bug at the call site, not a value to coerce.

- [ ] **Step 5: Run to verify it passes**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SettingsResolverTests"
```
Expected: `Failed: 0, Passed: 10`.

- [ ] **Step 6: Commit**

```bash
git add AIOrchestratorCoreLib/Configuration/SettingsCatalog/Settings_Resolver.cs AIOrchestratorCoreLib/Configuration/SettingsCatalog/SessionScoped_Reader.cs AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/SettingsResolverTests.cs
git commit -F <tempfile>   # "feat(settings): one resolver, four layers, no disk"
```

---

### Task 6: The model defaults move into the catalogue — and four red tests go green

**Files:**
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig/OrchestratorConfig_Factory.cs` (the six `DEFAULT_*_MODEL` constants)
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs` (`Load_OrEmpty` gains the preset rung)
- Test: `AIOrchestratorCoreLib.Tests/Configuration/PerRoleModelDefaultsTests.cs` (existing — 3 of its cases are RED before this task and green after; do not rewrite them)
- Test: `AIOrchestratorCoreLib.Tests/Configuration/OrchestratorConfigLoaderGuardrailsTests.cs` (existing — 1 case, same)

**Interfaces:**
- Consumes: Task 3's `SettingsCatalog.Get_ModelPath(role)`; Task 4's `Presets_Loader.Resolve_ForConfig`; Task 5's `Settings_Resolver.Resolve_String_OrNull`.
- Produces: `OrchestratorConfig_Factory.DEFAULT_SUPERVISOR_MODEL` etc. now DERIVED (`static readonly string`, read from the catalogue) rather than `const` — every existing reader compiles unchanged except any `switch` case or attribute using them as a compile-time constant. **Grep for that before editing:** `grep -rn "DEFAULT_SUPERVISOR_MODEL\|DEFAULT_IMPLEMENTER_MODEL\|DEFAULT_REVIEWER_MODEL\|DEFAULT_SOLO_MODEL\|DEFAULT_GENERAL_SUPERVISOR_MODEL\|DEFAULT_COMMUNICATOR_MODEL" --include=*.cs .` and report every hit in the commit body.

- [ ] **Step 1: Record the four reds, by name, before touching anything**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-plan02
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PerRoleModelDefaultsTests|FullyQualifiedName~OrchestratorConfigLoaderGuardrailsTests"
```
Expected: exactly **4 failed**, and they are these four and no others (plan 01's Task 2 ruling: the constant stayed `claude-fable-5-1` for all of plan 01 and these four were left red on purpose):

1. `PerRoleModelDefaultsTests.WithNoConfigFileAtAll_EveryRoleGetsItsShippedDefault`
2. `PerRoleModelDefaultsTests.AnEmptyImplementerModel_IsAbsentForEveryRoleThatRidesIt`
3. `PerRoleModelDefaultsTests.AMistypedModelValue_DoesNotTakeDownTheProviderOnTheStartupPath`
4. `OrchestratorConfigLoaderGuardrailsTests.Save_OverACorruptConfigJson_StillSucceeds_AndWritesTheKnownKeys`

If a fifth name appears, STOP and report it — something else moved.

- [ ] **Step 2: Write the failing test for the new behaviour: the preset rung**

Add to `AIOrchestratorCoreLib.Tests/Configuration/PerRoleModelDefaultsTests.cs`:

```csharp
/// <summary>
/// THE SHIPPED DEFAULT IS OPUS AND CLASSIC IS WHERE FABLE LIVES NOW (owner, spec §11.4). Spec §5.2
/// named this precisely: master's claude-fable-5-1 auto-merged away to the fork's opus, which is
/// right by accident — "but the `classic` preset must carry Fable + xhigh, or Manu silently loses
/// his model". `preset` absent means classic (§11.3), so a config.json that says nothing at all
/// still spawns the model master spawned, and the value's ORIGIN is the preset rather than a
/// materialised key.
/// </summary>
[Fact]
public void WithNoPresetKeyAtAll_TheFourJudgingRoles_StillGetTheClassicModel()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    Assert.Equal("claude-fable-5-1", config.Get_ModelForRole(SessionRoles.Supervisor));
    Assert.Equal("claude-fable-5-1", config.Get_ModelForRole(SessionRoles.Implementer));
    Assert.Equal("claude-fable-5-1", config.Get_ModelForRole(SessionRoles.Reviewer));
    Assert.Equal("claude-fable-5-1", config.Get_ModelForRole(SessionRoles.Solo));

    // Routing and narration are cheap on BOTH sides and neither preset touches them.
    Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.General));
    Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Communicator));
}

/// <summary>The quiet preset names no model at all, so every role falls to the shipped default: opus.</summary>
[Fact]
public void UnderTheQuietPreset_EveryRoleGetsTheShippedOpus()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Supervisor));
    Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Implementer));
    Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Reviewer));
    Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Solo));
}

/// <summary>
/// A KEY IN config.json BEATS THE PRESET, and the preset is not written back. The owner who typed
/// a model into the Settings window has said something; the preset is what applies when they have
/// not — which is exactly the reviewerModel rule, one layer down.
/// </summary>
[Fact]
public void AModelInTheConfigFile_BeatsThePreset_AndTheSaveDoesNotMaterialiseThePresetsValue()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet"}""");

    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Implementer));
    Assert.Equal("claude-fable-5-1", config.Get_ModelForRole(SessionRoles.Supervisor));

    OrchestratorConfig_Loader.Save(config, _paths);

    var written = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject;

    Assert.Null(written![OrchestratorConfig_Loader.REVIEWER_MODEL_KEY]);
    Assert.Null(written[OrchestratorConfig_Loader.SOLO_MODEL_KEY]);
    Assert.Null(written["preset"]);
}
```

**Note on the last assertion:** `Save` DOES write `supervisorModel` and `implementerModel` today (they have Settings fields), so after a save the supervisor's Fable is materialised into the file as the owner's own — that is existing behaviour and is not changed here. What must not happen is `preset` being written, or `reviewerModel`/`soloModel` appearing. Assert only that.

- [ ] **Step 3: Run to verify the three new cases fail**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PerRoleModelDefaultsTests"
```
Expected: 3 pre-existing reds + 3 new reds = 6 failed.

- [ ] **Step 4: Move the six model defaults into the catalogue**

In `OrchestratorConfig_Factory`, replace the six `public const string DEFAULT_*_MODEL = "…";` with reads, keeping each existing XML doc and adding the sentence that says where the value went:

```csharp
/// <summary>
/// THE SHIPPED DEFAULT IS NOW A CATALOGUE ENTRY, not a literal here (spec §6.4, owner §11.4:
/// "Opus for every role except general and communicator"). Moved 2026-09-12: the same number had to
/// be stateable by a preset, editable by three renderers and readable by the resolver, and a `const`
/// in this file is none of those. `classic` carries claude-fable-5-1 for the four judging roles, so
/// a machine whose config.json says nothing spawns exactly what it spawned before the merge —
/// `preset` absent means classic.
///
/// STILL NAMED HERE because six call sites and four tests read these by name, and because this file
/// is where the LADDER lives: an absent reviewerModel or soloModel falls to implementerModel before
/// any default applies, and that is unchanged.
/// </summary>
public static readonly string DEFAULT_SUPERVISOR_MODEL = Read_ShippedModel(SessionRoles.Supervisor);
public static readonly string DEFAULT_IMPLEMENTER_MODEL = Read_ShippedModel(SessionRoles.Implementer);
public static readonly string DEFAULT_REVIEWER_MODEL = Read_ShippedModel(SessionRoles.Reviewer);
public static readonly string DEFAULT_SOLO_MODEL = Read_ShippedModel(SessionRoles.Solo);
public static readonly string DEFAULT_GENERAL_SUPERVISOR_MODEL = Read_ShippedModel(SessionRoles.General);
public static readonly string DEFAULT_COMMUNICATOR_MODEL = Read_ShippedModel(SessionRoles.Communicator);

static string Read_ShippedModel(SessionRoles role)
{
    var definition = SettingsCatalog.Find_OrNull(SettingsCatalog.Get_ModelPath(role))
        ?? throw new Exception($"No catalogue entry for {SettingsCatalog.Get_ModelPath(role)} — a role without a registered model default cannot spawn");

    return definition.Default_OrNull!.GetValue<string>();
}
```

Keep `DEFAULT_TELEGRAM_STATUS_SCREENSHOTS` as it is — it is registered in the catalogue but its literal stays here, because `Create` uses it in a `??` on a `bool?` and nothing else reads it.

- [ ] **Step 5: Add the preset rung to `Load_OrEmpty`**

In `OrchestratorConfig_Loader.Load_OrEmpty`, after `configRoot` is read and before the `OrchestratorConfig_Factory.Create(...)` call:

```csharp
// THE PRESET RUNG, between the shipped default and this file (spec §6.2). It is applied HERE and
// only to the six model keys, because the factory's ladder (reviewer/solo → implementer → shipped)
// must see "the owner said nothing" as null — a preset value read one layer lower would arrive as
// a stated value and shorten the ladder. Nothing is written back: Save() merges, so a preset value
// stays a preset value and the renderers can still show its origin (spec §6.2, the reviewerModel
// rule generalised).
var preset = Presets_Loader.Resolve_ForConfig(configRoot).Tree;
```

and replace each of the six `Get_String_OrNull(configRoot, "<key>")` arguments with `Read_Model_OrNull(configRoot, preset, SessionRoles.<Role>)`, where:

```csharp
/// <summary>
/// The model this file or the preset states for a role, or null when neither does — which is what
/// the factory's ladder needs to hear. Blank is null for the reason
/// <see cref="OrchestratorConfig_Factory"/> gives: a cleared field is the owner saying nothing, and
/// an empty string reaching a spawn emits no --model flag at all (proven 2026-09-10).
/// </summary>
static string? Read_Model_OrNull(JsonObject? configRoot, JsonObject? presetTree, SessionRoles role)
{
    var definition = SettingsCatalog.Find_OrNull(SettingsCatalog.Get_ModelPath(role))!;
    var (value, origin) = Settings_Resolver.Resolve(definition, presetTree, configRoot, session: null);

    return origin == SettingOrigins.ShippedDefault ? null : value?.GetValue<string>();
}
```

Returning null at the ShippedDefault origin is the load-bearing line: the factory owns the shipped default AND the ladder, so the loader must hand it an absence rather than the default it would have chosen anyway.

- [ ] **Step 6: Run the two classes to verify every case passes**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PerRoleModelDefaultsTests|FullyQualifiedName~OrchestratorConfigLoaderGuardrailsTests|FullyQualifiedName~OrchestratorConfigFactoryTests|FullyQualifiedName~OrchestratorConfigLoaderTests|FullyQualifiedName~OrchestratorConfigProviderTests"
```
Expected: **0 failed** — the four plan-01 reds are green, and so are the three new cases.

- [ ] **Step 7: Prove nothing else in the configuration or spawn family moved**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Configuration|FullyQualifiedName~SpawnCommandBuilderTests|FullyQualifiedName~SettingsCatalog"
```
Expected: 0 failed. Any red here is a real consequence of the constant change — isolate it with `--filter` on its own name before believing it, then fix it.

- [ ] **Step 8: Commit**

```bash
git add AIOrchestratorCoreLib/Configuration/OrchestratorConfig/OrchestratorConfig_Factory.cs AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs AIOrchestratorCoreLib.Tests/Configuration/PerRoleModelDefaultsTests.cs
git commit -F <tempfile>   # "feat(settings): the shipped model default is opus and classic carries Fable — the four plan-01 reds are green"
```

---

### Task 7: The effort default stops being a compiled constant

**Files:**
- Create: `AIOrchestratorCoreLib/Configuration/EffortSettings/IEffortSettings.cs`, `EffortSettingsModel.cs`, `EffortSettings_Factory.cs`, `EffortSettings_Json.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig/IOrchestratorConfig.cs`, `OrchestratorConfigModel.cs`, `OrchestratorConfig_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs`
- Modify: `AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs` (delete `SUPERVISION_EFFORT_LEVEL` and `Resolve_Effort_OrDefault`)
- Modify: `AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs` (`Respawn_Supervisor` ~line 339, the member path ~line 446, and every other `SessionLaunch_Factory.Create` call site that passes an effort)
- Modify: `AIOrchestratorCoreLib.Tests/Spawning/SpawnCommandBuilderTests.cs`, `AIOrchestratorCoreLib.Tests/Launching/OrchestrationLauncherTests.cs`

**Interfaces:**
- Consumes: Task 3's `SettingsCatalog.Get_EffortPath(role)`; Task 4's `Presets_Loader`; Task 5's `Settings_Resolver`; the existing `IOrchestrationSession.SupervisorEffortOverride` / `ImplementerEffortOverride`; `SessionLaunch_Factory.Create(role, orchId, memberId, workingDirectory, model, pidFilePath, displayName, effort, resumeSessionId)` (verified signature — effort is the 8th parameter).
- Produces:
  - `IOrchestratorConfig.Get_EffortForRole_OrNull(SessionRoles role) : string?` — the ROLE DEFAULT, resolved through catalogue → preset → `config.json`; null means no `--effort` flag
  - `IOrchestratorConfig.Effort : IEffortSettings`
  - `EffortSettings_Json.EFFORT_KEY = "effort"`, `Parse(JsonObject? configRoot, JsonObject? presetTree)`
  - **Deleted:** `SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL`, `SpawnCommand_Builder.Resolve_Effort_OrDefault`

- [ ] **Step 1: Write the failing tests — the default comes from the config, the builder just carries it**

Add to `AIOrchestratorCoreLib.Tests/Configuration/PerRoleModelDefaultsTests.cs` (it is the file that owns "what a role gets when the owner says nothing"):

```csharp
/// <summary>
/// EFFORT STOPS BEING A COMPILED CONSTANT (spec §2.5, §6.4). Until 2026-09-12 the xhigh for
/// supervisor and solo lived in SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL — CODE, needing a
/// rebuilt app running (CLAUDE.md decision 23) — while the model beside it was DATA. Same dial, two
/// different places to change it. The value is unchanged for a machine that says nothing: classic
/// carries xhigh for the two roles the owner named, and `preset` absent means classic.
/// </summary>
[Fact]
public void WithNoConfigFileAtAll_OnlyTheSupervisorAndTheSolo_CarryARoleEffort()
{
    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    Assert.Equal("xhigh", config.Get_EffortForRole_OrNull(SessionRoles.Supervisor));
    Assert.Equal("xhigh", config.Get_EffortForRole_OrNull(SessionRoles.Solo));
    Assert.Null(config.Get_EffortForRole_OrNull(SessionRoles.Implementer));
    Assert.Null(config.Get_EffortForRole_OrNull(SessionRoles.Reviewer));
    Assert.Null(config.Get_EffortForRole_OrNull(SessionRoles.General));
    Assert.Null(config.Get_EffortForRole_OrNull(SessionRoles.Communicator));
}

/// <summary>Nathan's phone: the quiet preset names no effort, so no role carries the flag.</summary>
[Fact]
public void UnderTheQuietPreset_NoRoleCarriesAnEffortFlag()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    foreach (var role in SessionRole_Names.ALL)
        Assert.Null(config.Get_EffortForRole_OrNull(role));
}

/// <summary>
/// A HAND-EDITED effort BLOCK BEATS THE PRESET, and an explicit null in it means "no flag" rather
/// than "say nothing" — the one place where a JSON null is an answer, because null IS the CLI's
/// own default and the owner may want it back from under classic.
/// </summary>
[Fact]
public void AnEffortBlockInTheConfigFile_BeatsThePreset_AndAnExplicitNullMeansNoFlag()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"effort":{"implementer":"high","supervisor":null}}""");

    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    Assert.Equal("high", config.Get_EffortForRole_OrNull(SessionRoles.Implementer));
    Assert.Null(config.Get_EffortForRole_OrNull(SessionRoles.Supervisor));
    Assert.Equal("xhigh", config.Get_EffortForRole_OrNull(SessionRoles.Solo));
}

/// <summary>A word that is not an effort level costs that one key its default, never the load.</summary>
[Fact]
public void AMistypedEffortLevel_CostsThatOneKeyItsDefault_NotTheWholeLoad()
{
    File.WriteAllText(_paths.ConfigFile, """{"repos":[],"effort":{"supervisor":"enormous"}}""");

    var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

    Assert.Equal("xhigh", config.Get_EffortForRole_OrNull(SessionRoles.Supervisor));
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PerRoleModelDefaultsTests"
```
Expected: compile errors — `Get_EffortForRole_OrNull` does not exist.

- [ ] **Step 3: Write the `EffortSettings` triple and its `_Json`**

Shape it on `Configuration/DefaultsSettings/` (read all four of its files first — same namespace layout, same `_Json` conventions). `IEffortSettings` exposes `string? Get_ForRole_OrNull(SessionRoles role)`; the model holds one `IReadOnlyDictionary<SessionRoles, string?>` built from `SessionRole_Names.ALL`; `EffortSettings_Json.Parse(JsonObject? configRoot, JsonObject? presetTree)` resolves each role through `Settings_Resolver.Resolve(SettingsCatalog.Find_OrNull(SettingsCatalog.Get_EffortPath(role))!, presetTree, configRoot, session: null)` and takes the resolved value's string (or null). `EffortSettings_Json.Write` does **not exist** — the block is read and never written, the same contract the guardrails and `defaults` blocks keep, and the class doc must say so and why (writing this build's answer materialises a default that is meant to move).

- [ ] **Step 4: Hang it off `IOrchestratorConfig`**

`IOrchestratorConfig` gains `IEffortSettings Effort { get; }` and `string? Get_EffortForRole_OrNull(SessionRoles role);` (the model implements the latter as `Effort.Get_ForRole_OrNull(role)` — one reader, beside `Get_ModelForRole`, so no call site invents a second ladder). `OrchestratorConfig_Factory.Create`'s FULL overload gains a trailing optional `IEffortSettings? effort = null`, defaulted with `effort ?? EffortSettings_Factory.Create_Default()` and the same XML-doc argument the guardrails and defaults parameters carry ("DEFAULTED, NEVER NULL — every caller that predates this parameter keeps compiling and keeps getting the shipped behaviour"). The short overload passes it through. `Create_WithStatusScreenshots` passes `source.Effort`. **Grep for every other `OrchestratorConfig_Factory.Create(` call site and check none of them silently drops it** — `Create_WithStatusScreenshots` is the one that already had this bug shape for `runners`.

`OrchestratorConfig_Loader.Load_OrEmpty` passes `EffortSettings_Json.Parse(configRoot, preset)` (the `preset` local Task 6 added).

- [ ] **Step 5: Run the config tests to verify Step 1's four cases pass**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~Configuration"
```
Expected: 0 failed.

- [ ] **Step 6: Write the failing launcher test — the role default now comes from the config**

Add to `AIOrchestratorCoreLib.Tests/Launching/OrchestrationLauncherTests.cs` (reuse the fixture's existing `_launcher` / `_store` / `_spawner` harness — read the class's constructor first; it already has a config provider stub):

```csharp
/// <summary>
/// THE ROLE DEFAULT MOVED FROM THE BUILDER TO THE CONFIG (2026-09-12, spec §6.4). The emitted
/// command line is byte-identical under `classic`, which is the point: what changed is WHERE the
/// xhigh is decided, so a brother on `quiet` can have none without a rebuild. The launcher is the
/// place because it is the one that already holds both the per-orchestration override and the
/// config provider; the builder holds neither and had to be handed a constant.
/// </summary>
[Fact]
public void Spawn_TakesTheRoleEffortFromTheConfig_NotFromTheCommandBuilder()
{
    var session = _launcher.Start_Orchestration("Repo", _tempRepo);

    Assert.Contains("--effort xhigh ", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
    Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[1]));
    Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[2]));
}
```

Then **change the two existing assertions that name the deleted constant** — `OrchestrationLauncherTests` lines ~232 and ~259, inside `Respawn_PassesTheStoredEffortOverride_ToTheSupervisorAndToEveryMemberKind`:

- `Assert.Contains($"--effort {SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL} ", …)` → `Assert.Contains("--effort xhigh ", …)` (both sites).

The literal is correct here rather than a catalogue read: this test is about the LAUNCHER passing through what the config said, and re-deriving the expected value from the same source the production code reads would make it assert nothing.

- [ ] **Step 7: Run to verify it fails**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~OrchestrationLauncherTests"
```
Expected: the new case passes ALREADY (the builder still applies the default) — that is expected and is why the next step is the one that could break it. Record the pass, then continue; Step 10 re-runs it after the constant is gone, and THAT is the run that proves the wiring.

- [ ] **Step 8: Resolve the role default in the launcher**

`OrchestrationLauncherModel.Respawn_Supervisor` (~line 339) — replace the existing comment and argument:

```csharp
// THE ROLE DEFAULT IS A SETTING NOW (`effort.supervisor`, spec §6.4): the owner's per-orchestration
// dial wins (/effort, decision 24), and when there is none the config answers — xhigh under
// `classic`, nothing under `quiet`. It is resolved HERE rather than in SpawnCommand_Builder because
// this is the one place that holds both the session and the config provider; the builder held
// neither and had to be handed a compiled constant (deleted 2026-09-12).
session.SupervisorEffortOverride ?? _configProvider.Get_Current().Get_EffortForRole_OrNull(SessionRoles.Supervisor),
```

The member path (~line 446): `var effort = session.ImplementerEffortOverride ?? _configProvider.Get_Current().Get_EffortForRole_OrNull(role);` — note it resolves by the MEMBER's own role, so a solo gets `effort.solo` and a reviewer `effort.reviewer`, while the per-orchestration override stays the single implementer-side slot it already is. Keep the existing comment about one override covering every member kind and add the sentence about the role now being consulted.

Then grep for every other `SessionLaunch_Factory.Create(` in the launcher (`grep -n "SessionLaunch_Factory.Create(" AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs`) — the first spawn paths, the general supervisor, the communicator — and give each the same resolution. A path left passing `null` is a role whose default silently disappears.

- [ ] **Step 9: Delete the constant and the builder's resolution**

In `SpawnCommand_Builder`: delete `SUPERVISION_EFFORT_LEVEL` and `Resolve_Effort_OrDefault`, and in `Build_ForSupervisor` (~line 66) and `Build_ForSolo` (~line 104) replace `Resolve_Effort_OrDefault(effort, SUPERVISION_EFFORT_LEVEL)` with `effort`. Move the constant's XML doc — the owner directive of 2026-09-09 and the "verified against the installed CLI, whose --help lists --effort as (low, medium, high, xhigh, max)" sentence — into `SettingsCatalog`'s `effort.supervisor` description and into `Build_ClaudeInvocation`'s doc, which already explains that a null effort emits no flag. Update `Build_ClaudeInvocation`'s `<see cref="SUPERVISION_EFFORT_LEVEL"/>` reference (~line 216) to name the catalogue entry in prose instead — **a dangling `cref` is a build warning and this project builds the app with `-warnaserror:CS`.**

Then fix the builder tests that asserted the builder's own default. In `AIOrchestratorCoreLib.Tests/Spawning/SpawnCommandBuilderTests.cs`, these cases pass `effort: null` and expect `--effort xhigh`; each must now pass `effort: "xhigh"` explicitly, and its docstring must say the default is the LAUNCHER's:

| test | line (approx) | change |
|---|---|---|
| the first case at line 24 | 24 | pass `effort: "xhigh"` |
| `Build_SupervisorAndSolo_ThinkAtXHighEffort_ByDefault` | 33 | **rename** to `Build_SupervisorAndSolo_CarryTheEffortTheyAreHanded` and pass `"xhigh"`; docstring: the ROLE DEFAULT moved to `effort.*` in the catalogue (2026-09-12), so this pins the CARRYING, not the choosing |
| `Build_WithoutEffortOverride_TheRoleDefaultDecides` | 105 | **rename** to `Build_WithoutAnEffort_EmitsNoFlagForAnyRole` and assert `DoesNotContain("--effort")` for supervisor and solo too — that is the new truth of the builder |
| `ASupervisorRespawn_ResumesItsOwnConversation_AndCarriesTheRoleEffort` | 297 | pass `effort: "xhigh"`; keep the order assertion (`--resume` then `--model` then `--effort`), which is the case's real subject |
| the respawn case at line 254/257/271 | 243–272 | pass the effort explicitly wherever it expected the default |

`Build_RolesTheOwnerDidNotName_CarryNoEffortFlag` (line 52) and `Build_ForImplementer_EffortWithoutModel_StillEmitsTheEffortFlag` (line 136) are unchanged — they already pass what they assert.

- [ ] **Step 10: Run the three families to verify**

```bash
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~SpawnCommandBuilderTests|FullyQualifiedName~OrchestrationLauncherTests|FullyQualifiedName~Configuration|FullyQualifiedName~SettingsCatalog"
```
Expected: 0 failed. If `Spawn_TakesTheRoleEffortFromTheConfig_NotFromTheCommandBuilder` fails now, the launcher wiring is incomplete — find the `SessionLaunch_Factory.Create` call site that still passes a bare override.

- [ ] **Step 11: Build the app project, which is the one that fails on a dangling cref**

```bash
dotnet build AIOrchestrator/AIOrchestrator.csproj -c Debug -warnaserror:CS 2>&1 | tail -5
```
Expected: `0 Warning(s)` `0 Error(s)`.

- [ ] **Step 12: Commit**

```bash
git add AIOrchestratorCoreLib/Configuration/EffortSettings AIOrchestratorCoreLib/Configuration/OrchestratorConfig AIOrchestratorCoreLib/Configuration/OrchestratorConfig_Loader.cs AIOrchestratorCoreLib/Spawning/SpawnCommand_Builder.cs AIOrchestratorCoreLib/Launching/OrchestrationLauncher/OrchestrationLauncherModel.cs AIOrchestratorCoreLib.Tests/Configuration/PerRoleModelDefaultsTests.cs AIOrchestratorCoreLib.Tests/Spawning/SpawnCommandBuilderTests.cs AIOrchestratorCoreLib.Tests/Launching/OrchestrationLauncherTests.cs
git commit -F <tempfile>   # "feat(settings): effort is data — the role default moves from a compiled constant into the catalogue"
```

---

### Task 8: The gate — the two preset probes, the named reds, the suite

**Files:**
- Create: `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetProbeTests.cs`
- Create: `docs/superpowers/plans/2026-09-12-settings-catalogue-02-report.md`

**Interfaces:**
- Consumes: everything Tasks 1–7 produced.
- Produces: the phase-2 gate evidence of spec §10.

- [ ] **Step 1: Write the two preset probes**

Spec §10's phase-2 gate is: *"the app behaves exactly as after phase 1 with `classic`, and exactly as the fork with `quiet`, measured by the preset probes."* Nothing behavioural is wired yet (plan 03), so at this phase a probe measures the RESOLVED CATALOGUE — every entry, under each preset, with its origin. That is the honest form of the gate now, and plan 03's probes extend it to the phone.

Create `AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetProbeTests.cs`:

```csharp
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE PHASE-2 GATE (spec §10): with `classic` the machine resolves to what master did before the
/// merge, with `quiet` to what the fork does. Plan 02 wires no behaviour, so what a probe can
/// measure here is the RESOLVED VALUE of every catalogue entry under each preset — which is exactly
/// the surface plan 03's seams will read. A seam that later disagrees with one of these lines has
/// changed the owner's phone without anyone deciding to.
///
/// <para>
/// SPELLED OUT RATHER THAN COMPUTED. Deriving the expected values from the preset files would make
/// this test assert that a file equals itself; spec §12 names `quiet` reproducing the fork's phone
/// exactly as a top risk, and a risk is not mitigated by a tautology.
/// </para>
/// </summary>
public class PresetProbeTests
{
    static string? Resolve(string presetName, string path)
    {
        var definition = SettingsCatalog.Find_OrNull(path)!;
        var config = presetName == Presets_Loader.CLASSIC ? null : (JsonObject)JsonNode.Parse($$"""{"preset":"{{presetName}}"}""")!;
        var preset = Presets_Loader.Resolve_ForConfig(config).Tree;

        return Settings_Resolver.Resolve(definition, preset, config, session: null).Value?.ToJsonString();
    }

    [Theory]
    // Manu's phone, as master shipped it.
    [InlineData("models.supervisor", "\"claude-fable-5-1\"")]
    [InlineData("models.solo", "\"claude-fable-5-1\"")]
    [InlineData("models.general", "\"sonnet\"")]
    [InlineData("effort.supervisor", "\"xhigh\"")]
    [InlineData("effort.solo", "\"xhigh\"")]
    [InlineData("effort.implementer", null)]
    [InlineData("phone.push", "\"filtered\"")]
    [InlineData("phone.status.periodic", "true")]
    [InlineData("phone.appMessagesRing", "true")]
    [InlineData("phone.receipts", "\"ticks\"")]
    [InlineData("phone.replyKeyboard", "\"on\"")]
    [InlineData("pulse.holdToggle", "false")]
    [InlineData("topic.onClose", "\"close\"")]
    [InlineData("topic.modeGlyphs", "\"name\"")]
    [InlineData("runners.supervisor.runner", "\"terminal\"")]
    [InlineData("runners.implementer.runner", "\"terminal\"")]
    public void UnderClassic_TheMachineResolvesToMastersWay(string path, string? expected)
    {
        Assert.Equal(expected, Resolve(Presets_Loader.CLASSIC, path));
    }

    [Theory]
    // Nathan's phone, as the fork shipped it.
    [InlineData("models.supervisor", "\"opus\"")]
    [InlineData("models.solo", "\"opus\"")]
    [InlineData("models.general", "\"sonnet\"")]
    [InlineData("effort.supervisor", null)]
    [InlineData("effort.solo", null)]
    [InlineData("phone.push", "\"everything\"")]
    [InlineData("phone.status.periodic", "false")]
    [InlineData("phone.appMessagesRing", "false")]
    [InlineData("phone.receipts", "\"reactions\"")]
    [InlineData("phone.replyKeyboard", "\"off\"")]
    [InlineData("pulse.holdToggle", "true")]
    [InlineData("topic.onClose", "\"delete\"")]
    [InlineData("topic.modeGlyphs", "\"pulseHeader\"")]
    [InlineData("runners.supervisor.runner", "\"stream\"")]
    [InlineData("runners.implementer.runner", "\"print\"")]
    [InlineData("runners.communicator.runner", "\"terminal\"")]
    [InlineData("runners.implementer.resume", "\"fresh\"")]
    public void UnderQuiet_TheMachineResolvesToTheForksWay(string path, string? expected)
    {
        Assert.Equal(expected, Resolve(Presets_Loader.QUIET, path));
    }

    /// <summary>
    /// EVERY CATALOGUE ENTRY RESOLVES UNDER BOTH PRESETS, without throwing and to a value its own
    /// definition accepts. The two [Theory] blocks above pin the values that differ; this one pins
    /// that nothing in the registry is unresolvable — the shape spec §12 calls "most likely to
    /// drift", caught before three renderers each meet it separately.
    /// </summary>
    [Theory]
    [InlineData(Presets_Loader.CLASSIC)]
    [InlineData(Presets_Loader.QUIET)]
    public void EveryCatalogueEntry_ResolvesUnderBothPresets(string presetName)
    {
        foreach (var definition in SettingsCatalog.ALL)
        {
            if (definition.Kind == SettingKinds.Composite)
                continue;

            var preset = Presets_Loader.Load_Embedded(presetName);
            var (value, _) = Settings_Resolver.Resolve(definition, preset, configTree: null, session: null);

            Assert.Null(definition.Validate_OrNull(value));
        }
    }
}
```

- [ ] **Step 2: Run the probes**

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
dotnet test AIOrchestratorCoreLib.Tests --filter "FullyQualifiedName~PresetProbeTests"
```
Expected: 0 failed. A failure here is either a preset file that is wrong or a catalogue default that is wrong — fix the DATA, never the expected value, unless you can name the spec line that says otherwise.

- [ ] **Step 3: Run the full suite, once, alone**

Close every other worktree's test run first. Then:

```bash
export PATH="$PATH:/c/Users/Gianpiero/AppData/Local/Microsoft/WinGet/Links"
cd /c/Users/Gianpiero/source/repos/AIOrchestrator-plan02
dotnet build AIOrchestrator.slnx -c Debug 2>&1 | tail -3
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug 2>&1 | tee /c/Users/Gianpiero/source/repos/plan02-gate.log | tail -6
grep -E "^\s+Failed " /c/Users/Gianpiero/source/repos/plan02-gate.log | sort
```

Expected: **0 failed.** Plan 01's final verification on `dd68771` recorded the true red set as exactly the four config tests this plan turns green, so the target here is a clean run.

Two known non-defects, both from plan 01's record — check for them before diagnosing anything:
- `ChannelAppendTypedEntriesTests` failing with an entry written `FROM solo-1` instead of `FROM supervisor` is **this session's own `AIORCH_MEMBER` leaking into the test process** (the deferred env-scrub minor). Re-run that class with `AIORCH_ROLE` and `AIORCH_MEMBER` unset; it is 13/13.
- Any red in the `PrintRunnerTestHarness.Drive_Until` family, `MeetingDefersAlertsProbeTests`, or `ChannelAppendHelperInteropTests` under load: isolate with `--filter` on the single name and re-run alone before believing it.

- [ ] **Step 4: Re-run any isolated red, and only then call it real**

For each name in the grep output:
```bash
dotnet test AIOrchestratorCoreLib.Tests --no-build -c Debug --filter "FullyQualifiedName~<TheExactName>"
```
Record in the report: the name, whether it passed alone, and — if it did not — which task's change it traces to.

- [ ] **Step 5: Run the contract suite**

```bash
dotnet test tools/claude-contract/ClaudeContract.Tests/ClaudeContract.Tests.csproj -c Debug 2>&1 | tail -4
```
Expected: 0 failed (the live smokes self-skip without `CLAUDE_CONTRACT_LIVE=1`).

- [ ] **Step 6: Write the report**

Create `docs/superpowers/plans/2026-09-12-settings-catalogue-02-report.md` with, at minimum:

- **The four plan-01 reds, by name, with before/after.** `WithNoConfigFileAtAll_EveryRoleGetsItsShippedDefault`, `AnEmptyImplementerModel_IsAbsentForEveryRoleThatRidesIt`, `AMistypedModelValue_DoesNotTakeDownTheProviderOnTheStartupPath`, `Save_OverACorruptConfigJson_StillSucceeds_AndWritesTheKnownKeys`.
- **The catalogue's size**: `SettingsCatalog.ALL.Count`, and the count per category.
- **What is registered and read by nothing** — the inert entries, so plan 03 and plan 04 know their inbox: every `phone.*`, `pulse.*`, `topic.*`, `general.buttons`, `web.*`, `owner.*`.
- **What moved out of code into data**: the six model constants, `SUPERVISION_EFFORT_LEVEL`, `Resolve_Effort_OrDefault`.
- **Which copy you read** for every claim about the kit (branch source vs build output vs installed).
- **Anything the spec asked for that this plan did not deliver**, and why.
- **Deferred minors**, in plan 01's format, so plan 03 can triage them.

- [ ] **Step 7: Commit**

```bash
git add AIOrchestratorCoreLib.Tests/Configuration/SettingsCatalog/PresetProbeTests.cs docs/superpowers/plans/2026-09-12-settings-catalogue-02-report.md
git commit -F <tempfile>   # "test(settings): the phase-2 preset probes, and the plan-02 gate report"
```

---

## Self-review (run by the plan's author, 2026-09-12)

Checked against spec §6 in full and §10 phase 2. Gaps found and fixed inline, recorded here so a reviewer can see what was decided rather than overlooked.

1. **§6.1 lists eight definition fields; this plan's `ISettingDefinition` has thirteen.** The five extra are `LegacyPath_OrNull`, `EnumValues`, `Minimum`, `Maximum`, `CompositeParser_OrNull` — four of them are the CONTENTS of the spec's `Kind` (`Enum(values)`, `Int(min,max)`, `Composite(parser)`), flattened because C# has no parameterised enum and `ISettingDefinition` must stay plain serialisable data for plan 04's `GET /settings`. Recorded rather than silently expanded.
2. **`LegacyPath_OrNull` is the one field with no counterpart in §6.1.** The spec asks for aliasing only for the two telegram keys ("the old path read as an alias"), but §6.4 also re-homes all six model keys from `supervisorModel` to `models.supervisor`, and both brothers' `config.json` is written in the old spelling. One mechanism serves both; the alternative — registering the old spelling as the `Path` — would make the catalogue disagree with its own spec table and leave plan 04's renderers showing `generalSupervisorModel` under a `Models` tab. Pinned by `SettingsCatalogTests.TheLegacySpellings_StillResolveToTheirDefinitions` and two resolver cases.
3. **§6.4 writes the effort enum as `Enum(low, medium, high, xhigh)`; the tree's `EffortLevels.ALL` has five values, including `max`.** The catalogue registers the real five. A catalogue that refused a level the CLI accepts and the `/effort` dial already offers would be a second source of truth for the same word — the thing this registry exists to end.
4. **§6.4's `pulse.buttons` classic row names eight verbs.** Whether all eight are in `BotCommandMenu.ALL` on this tree is not verified by the plan's author; Task 4 Step 1 makes checking them and recording the result a mandatory action rather than an assumption, because inventing a missing verb is plan 03's work.
5. **§6.4 lists `repos[].code`; this plan does not register it.** It is the kit's per-user value and belongs to plan 05 with `AIORCH_PLATFORM_CODES`; registering an array-element path would also need a path grammar (`repos[].code`) that nothing else in v1 uses. The `Kit` category ships empty and `SettingsCatalogTests` exempts it BY NAME so the exemption cannot quietly outlive its reason.
6. **§6.2's "`Save` never materialises a preset value into `config.json`" is only partly assertable today.** `Save` already writes `supervisorModel` and `implementerModel` because they have Settings fields, so after any save a `classic` machine's Fable is in the file as the owner's own. This plan does not change that (it is existing, deliberate behaviour for the four UI-owned model keys) and Task 6 Step 2's test asserts only what is true: `preset`, `reviewerModel` and `soloModel` are never written. Flagged for plan 04, which gives every catalogue key a renderer and will have to decide whether the four UI models keep their special status.
7. **§6.2's origin display and Reset** are produced here (`SettingOrigins`, `SettingsJson_Path.Remove`) and consumed by nobody until plan 04. That is intended; both are tested directly.
8. **§10's phase-2 gate says "measured by the preset probes".** No behaviour is wired in phase 2, so Task 8's probes measure the resolved catalogue rather than the phone. Stated in the probe class's own doc so plan 03 extends it rather than replacing it, and stated again in the report.
9. **Effort resolution moves to the launcher, which is arguably a seam.** §10 phase 3's seam list ends with "model/effort defaults", but the brief puts "moving the model and effort defaults into the catalogue" in plan 02's scope, and the four red tests cannot go green without the model half. The effort half is done in the same plan because leaving `SUPERVISION_EFFORT_LEVEL` as code while `classic` claims to carry `xhigh` would make the catalogue's first entry a lie. Under `classic` the emitted command line is byte-identical; Task 7 Step 6's test pins that, and Step 9 enumerates every test whose expected value must move.
10. **`session.telegramMode` and `session.ownerPresence` are registered with their enums' `ToString()` spelling**, which Task 5 Step 3 makes the implementer verify against the two enum files rather than assume. Both are `ReadOnly` — a renderer must never write a delivery mode through the catalogue, because the engine owns that state.

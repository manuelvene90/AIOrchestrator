using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

// THE CATALOGUE IS REACHED THROUGH AN ALIAS: this file's own namespace
// (AIOrchestratorCoreLib.Tests.Configuration) has a nested sibling namespace
// AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog (PresetsLoaderTests, SettingsCatalogTests,
// SettingsResolverTests), so the bare word "SettingsCatalog" resolves to that nested namespace before
// the using directive importing the class is ever consulted (CS0234) — the same trap those sibling
// files already worked around.
namespace AIOrchestratorCoreLib.Tests.Configuration;

/// <summary>
/// THE REVIEWER AND THE SOLO STOP SHARING THE IMPLEMENTER'S DEFAULT (owner 2026-09-09). Both rode
/// <c>implementerModel</c>, which made the owner's decision — implementer sonnet eventually, reviewer
/// opus — unstateable rather than merely unset.
///
/// <para>
/// The whole risk of the change is in the COMPATIBILITY LADDER, so most of this file is about a
/// config.json written before the keys existed: an absent <c>reviewerModel</c> has to keep meaning
/// exactly what the reviewer got until now, INCLUDING on a box whose owner had set an implementer
/// model by hand. A split that quietly moved those reviewers onto the shipped default would be a
/// model change nobody asked for, on every existing machine, announced by nothing.
/// </para>
/// </summary>
public class PerRoleModelDefaultsTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PerRoleModelDefaultsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-role-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>The owner's ladder as shipped: routing and narration cheap, everything that judges opus.</summary>
    [Fact]
    public void WithNoConfigFileAtAll_EveryRoleGetsItsShippedDefault()
    {
        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Supervisor));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Implementer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Solo));
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.General));
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Communicator));
    }

    /// <summary>
    /// THE CASE THAT MATTERS. A config.json written before these keys existed, whose owner had set
    /// an implementer model by hand: the reviewer and the solo were running THAT model, and they
    /// must go on running it. This is the assertion that fails if the ladder is ever shortened to
    /// "reviewerModel or the shipped default".
    /// </summary>
    [Fact]
    public void AFileWrittenBeforeTheseKeysExisted_KeepsGivingTheReviewerAndSoloTheImplementerModel()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"supervisorModel":"opus","implementerModel":"haiku"}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("haiku", config.ImplementerModel);
        Assert.Equal("haiku", config.ReviewerModel);
        Assert.Equal("haiku", config.SoloModel);
        Assert.Equal("haiku", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("haiku", config.Get_ModelForRole(SessionRoles.Solo));
    }

    /// <summary>An explicit key beats the implementer it used to inherit from — the point of the split.</summary>
    [Fact]
    public void AnExplicitReviewerModel_BeatsTheImplementerModel()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet","reviewerModel":"opus","soloModel":"haiku"}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Implementer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("haiku", config.Get_ModelForRole(SessionRoles.Solo));
    }

    /// <summary>
    /// The owner's stated destination, spelled as they would spell it: implementer sonnet, reviewer
    /// opus. Impossible to express at all before the split, which is why it is pinned as a shape
    /// rather than left to the two assertions above.
    /// </summary>
    [Fact]
    public void TheOwnersDestination_IsExpressible()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet","reviewerModel":"opus"}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Implementer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Reviewer));

        // ...and the solo, which says nothing, follows the implementer as it always has. Stated
        // here so that a future decision to pin the solo to opus is a DELIBERATE change to this
        // line rather than a surprise.
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Solo));
    }

    /// <summary>
    /// A HAND-EDITED VALUE SURVIVES A SAVE. Save() merges rather than replaces, so keys it does not
    /// write are carried through untouched — which is the whole reason these two can safely be
    /// read-only.
    /// </summary>
    [Fact]
    public void Save_LeavesAHandEditedReviewerModelAlone()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet","reviewerModel":"opus"}""");

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Loader.Load_OrEmpty(_paths), _paths);

        var written = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject;

        Assert.Equal("opus", written![OrchestratorConfig_Loader.REVIEWER_MODEL_KEY]!.GetValue<string>());
        Assert.Equal("opus", OrchestratorConfig_Loader.Load_OrEmpty(_paths).Get_ModelForRole(SessionRoles.Reviewer));
    }

    /// <summary>
    /// AND A SAVE NEVER MATERIALISES THEM. The two keys have no Settings field and their default is
    /// one that is MEANT TO MOVE — an absent reviewerModel tracks implementerModel by design. Writing
    /// this build's answer would cut that ladder for good, on the first button press, on every box
    /// that had never heard of the keys: exactly what the loader already refuses to do for the
    /// guardrail keys and the defaults block.
    /// </summary>
    [Fact]
    public void Save_DoesNotMaterialiseTheseKeysIntoAFileThatLacksThem()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet"}""");

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Loader.Load_OrEmpty(_paths), _paths);

        var written = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject;

        Assert.Null(written![OrchestratorConfig_Loader.REVIEWER_MODEL_KEY]);
        Assert.Null(written[OrchestratorConfig_Loader.SOLO_MODEL_KEY]);

        // ...so the ladder still applies after the save, which is the behaviour the absence buys.
        Assert.Equal("sonnet", OrchestratorConfig_Loader.Load_OrEmpty(_paths).Get_ModelForRole(SessionRoles.Reviewer));
    }

    /// <summary>Both keys round-trip through a config the app built itself, not only through a hand-edited file.</summary>
    [Fact]
    public void AConfigBuiltInMemory_CarriesBothKeysThroughEveryCopy()
    {
        var config = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Arb Studio", "/repos/arb")],
            "opus",
            "sonnet",
            "opus",
            "haiku",
            "sonnet",
            "sonnet",
            null,
            null,
            null,
            telegramStatusScreenshots: false,
            null,
            null);

        Assert.Equal("opus", config.ReviewerModel);
        Assert.Equal("haiku", config.SoloModel);

        // The /screenshots toggle rebuilds a config from an existing one and must not drop them.
        var flipped = OrchestratorConfig_Factory.Create_WithStatusScreenshots(config, true);

        Assert.Equal("opus", flipped.ReviewerModel);
        Assert.Equal("haiku", flipped.SoloModel);
    }

    /// <summary>
    /// A MISTYPED VALUE COSTS THAT ONE SETTING ITS DEFAULT, NEVER THE LOAD. Proven 2026-09-10 on
    /// this branch: <c>{"reviewerModel": 5}</c> threw <c>InvalidOperationException</c> straight out
    /// of <see cref="OrchestratorConfig_Loader.Load_OrEmpty"/>, because the string reader called
    /// <c>GetValue&lt;string?&gt;()</c> unguarded while its numeric and boolean neighbours had
    /// already been made tolerant for exactly this reason. Two of these keys are documented as
    /// hand-edited and have no Settings field to get them right, so the typo is the expected input.
    /// </summary>
    [Fact]
    public void AMistypedModelValue_CostsThatOneSettingItsDefault_NotTheWholeLoad()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet","reviewerModel":5,"soloModel":true}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        // The two mistyped keys read as ABSENT, so each takes the ladder it would have taken had the
        // owner never written it — the implementer's model, which is the compatibility promise.
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Implementer));
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Solo));
    }

    /// <summary>
    /// AND IT DOES NOT TAKE THE APP DOWN EITHER, which is the half that made this a defect rather
    /// than an annoyance: <see cref="OrchestratorConfigProvider.IOrchestratorConfigProvider.Get_Current"/>
    /// is on the startup path AND on every tick, with no try/catch anywhere above it. Proven
    /// 2026-09-10: <c>{"reviewerModel": true}</c> threw out of Get_Current.
    /// </summary>
    [Fact]
    public void AMistypedModelValue_DoesNotTakeDownTheProviderOnTheStartupPath()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"supervisorModel":true,"reviewerModel":true}""");

        var provider = OrchestratorConfigProvider_Factory.Create(_paths);

        Assert.Equal("opus", provider.Get_Current().Get_ModelForRole(SessionRoles.Supervisor));
        Assert.Equal("opus", provider.Get_Current().Get_ModelForRole(SessionRoles.Reviewer));
    }

    /// <summary>
    /// AN EMPTY VALUE IS ABSENT, and it has to be said out loud because <c>??</c> does not say it.
    /// Proven 2026-09-10: <c>{"implementerModel":"sonnet","reviewerModel":""}</c> gave the reviewer
    /// the empty string — and both command builders add <c>--model</c> only when the value is not
    /// whitespace, so the reviewer was spawned with NO model flag at all: the CLI's own default,
    /// neither the ladder's answer nor this app's. A cleared field is the owner saying nothing.
    /// </summary>
    [Fact]
    public void AnEmptyModelValue_IsAbsent_AndTakesTheLadder()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":"sonnet","reviewerModel":"","soloModel":"   "}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Solo));
    }

    /// <summary>
    /// THE SAME RULE ON THE RUNG BELOW, which is the one that would otherwise have leaked past a fix
    /// applied only to the two new keys: an empty <c>implementerModel</c> is the SECOND rung of the
    /// reviewer's and the solo's ladder, so a fix that stopped at the first rung would still hand
    /// them an empty string. Every role resolves to a real word here, or no spawn carries a model.
    /// </summary>
    [Fact]
    public void AnEmptyImplementerModel_IsAbsentForEveryRoleThatRidesIt()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"implementerModel":""}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Implementer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Solo));
    }

    /// <summary>
    /// ONE READER FOR SIX ROLES, so the cards, the launcher and every future call site cannot
    /// disagree — the rule CLAUDE.md decision 12 states about formatters. A role added to the enum
    /// and forgotten here must THROW naming itself rather than spawn a session on a model nobody
    /// chose, so every role in SessionRole_Names.ALL is asked for.
    /// </summary>
    [Fact]
    public void EveryRoleTheKitShips_HasAModelDefault()
    {
        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        foreach (var role in SessionRole_Names.ALL)
            Assert.False(string.IsNullOrWhiteSpace(config.Get_ModelForRole(role)), $"role {role} resolved to no model");
    }

    /// <summary>
    /// RULED 2026-09-12 (task-6 fix round 1): THE SHIPPED DEFAULT IS OPUS, AND IT MUST ACTUALLY BE
    /// REACHABLE. This test used to pin the opposite — that <c>classic</c> restated Fable on all four
    /// judging roles, so a config.json naming no preset never got the catalogue's own answer. That was
    /// the exact defect the controller ruled on: the owner's own request is that Opus is the SHIPPED
    /// default, and a preset that restates an older model on every judging role made that default
    /// unreachable in practice — satisfied on paper, nowhere else. <c>classic</c>'s four
    /// <c>models.*</c> rows are gone now, so <c>preset</c> absent (which still means classic, §11.3)
    /// really does spawn the catalogue's Opus.
    ///
    /// <para>
    /// THE SECOND HALF PROVES THE PRESET LAYER IS STILL GENUINELY CONSULTED, not merely that deleting
    /// it would look the same: a test that only asserted the shipped model would pass identically with
    /// <c>Presets_Loader</c> ripped out entirely. <c>classic</c> still carries non-model rows
    /// (<c>effort.supervisor</c>, <c>phone.replyKeyboard</c>, …) that the catalogue's own shipped
    /// default does not, and a config.json naming no preset resolves one of those through
    /// <see cref="Settings_Resolver"/> with <see cref="SettingOrigins.Preset"/> as its origin.
    /// </para>
    /// </summary>
    [Fact]
    public void WithNoPresetKeyAtAll_TheFourJudgingRoles_GetTheCataloguesOpus_AndThePresetStillApplies()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Supervisor));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Implementer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Solo));

        // Routing and narration are cheap on BOTH sides and neither preset ever touches them.
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.General));
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Communicator));

        // A non-model row from classic proves the preset layer is genuinely still being consulted.
        var effortDefinition = Catalog.Find_OrNull(Catalog.Get_EffortPath(SessionRoles.Supervisor))!;
        var presetTree = Presets_Loader.Resolve_ForConfig(
            JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject).Tree;

        var (value, origin) = Settings_Resolver.Resolve(effortDefinition, presetTree, configTree: null, session: null);

        Assert.Equal(SettingOrigins.Preset, origin);
        Assert.Equal("xhigh", value!.GetValue<string>());
    }

    /// <summary>
    /// The quiet preset names no model at all, so every role falls to the shipped default: opus.
    ///
    /// <para>
    /// STRENGTHENED 2026-09-12 (task-6 fix round 2): this used to assert only the four models, which
    /// would pass identically with <c>quiet.json</c> EMPTY — it distinguished nothing about quiet
    /// specifically, only "no preset states a model". The second half proves quiet itself is genuinely
    /// the preset consulted: <c>phone.push</c> is a row quiet carries and classic does not, and it
    /// must resolve with <see cref="SettingOrigins.Preset"/> as its origin.
    /// </para>
    /// </summary>
    [Fact]
    public void UnderTheQuietPreset_EveryRoleGetsTheShippedOpus_AndQuietItselfIsGenuinelyConsulted()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Supervisor));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Implementer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Solo));

        var definition = Catalog.Find_OrNull("phone.push")!;
        var presetTree = Presets_Loader.Resolve_ForConfig(
            JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject).Tree;

        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree, configTree: null, session: null);

        Assert.Equal(SettingOrigins.Preset, origin);
        Assert.Equal("everything", value!.GetValue<string>());
    }

    /// <summary>
    /// A KEY IN config.json BEATS THE PRESET, and the ladder-only keys are not written back. The
    /// owner who typed a model into the Settings window has said something; the preset is what
    /// applies when they have not — which is exactly the reviewerModel rule, one layer down.
    ///
    /// <para>
    /// STRENGTHENED 2026-09-12 (task-6 fix round 2): with all four model rows gone from <c>classic</c>
    /// (fix round 1), the old version of this test no longer exercised any preset-STATED model at
    /// all — Supervisor merely fell through an EMPTY preset to the catalogue's shipped default, which
    /// proves nothing about beating a preset specifically. This version points <c>preset</c> at a
    /// hand-edited file (<c>Presets_Loader.Load_FromDisk</c>) that genuinely states
    /// <c>models.supervisor</c>, so "config beats preset" has something real to beat.
    /// </para>
    /// </summary>
    [Fact]
    public void AModelInTheConfigFile_BeatsAPresetStatedModel_AndTheSaveDoesNotMaterialiseTheLadderKeys()
    {
        var presetFile = Path.Combine(_tempRoot, "hand-edited-preset-fix2-beats.json");
        File.WriteAllText(presetFile, """{"models.supervisor":"haiku"}""");
        var presetPath = presetFile.Replace('\\', '/');

        File.WriteAllText(
            _paths.ConfigFile,
            $$"""{"repos":[],"preset":"{{presetPath}}","implementerModel":"sonnet","supervisorModel":"opus"}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Implementer));

        // The config file's own supervisorModel beats the preset file's stated "haiku".
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Supervisor));

        OrchestratorConfig_Loader.Save(config, _paths);

        var written = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile)) as JsonObject;

        Assert.Null(written![OrchestratorConfig_Loader.REVIEWER_MODEL_KEY]);
        Assert.Null(written[OrchestratorConfig_Loader.SOLO_MODEL_KEY]);
    }

    /// <summary>
    /// CRITICAL, RULED 2026-09-12 (task-6 fix round 2): <c>Presets_Loader.Resolve_ForConfig</c> throws
    /// for an unknown preset word, and <c>Load_OrEmpty</c> used to call it with no try/catch — so one
    /// transposed letter in a hand-edited <c>"preset": "quite"</c> took the app's config loading down
    /// entirely, on the startup path and on every tick. A typo must cost exactly what an ABSENT
    /// <c>preset</c> key already costs — classic — never the load, and the failure must be named
    /// rather than swallowed.
    /// </summary>
    [Fact]
    public void AMistypedPresetWord_StillLoads_YieldsClassicsBehaviour_AndIsReportedNotSwallowed()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quite"}""");

        // The no-log overload must not throw either — this is the path the app's own startup and
        // every provider tick actually call.
        Assert.Null(Record.Exception(() => OrchestratorConfig_Loader.Load_OrEmpty(_paths)));

        var log = new RecordingLog();
        IOrchestratorConfig? config = null;

        var exception = Record.Exception(() => config = OrchestratorConfig_Loader.Load_OrEmpty(_paths, log));

        Assert.Null(exception);
        Assert.NotNull(config);

        // Classic's own behaviour — exactly what an ABSENT preset key already yields (classic states
        // no model any more), because a typo must cost nothing more than saying nothing would.
        Assert.Equal("opus", config!.Get_ModelForRole(SessionRoles.Supervisor));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Implementer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Reviewer));
        Assert.Equal("opus", config.Get_ModelForRole(SessionRoles.Solo));
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.General));
        Assert.Equal("sonnet", config.Get_ModelForRole(SessionRoles.Communicator));

        // Not swallowed: one warning, naming the bad word.
        var warning = Assert.Single(log.Warnings);
        Assert.Contains("quite", warning);
    }

    /// <summary>
    /// RULED 2026-09-12 (task-6 fix round 2): with all four model rows gone from <c>classic</c> and
    /// <c>quiet</c> never carrying one, nothing proved the LOADER actually threads its preset argument
    /// through <see cref="Settings_Resolver"/> — the whole rung could be replaced by a null and every
    /// other test in this file would stay green, including the retargeted
    /// <see cref="WithNoPresetKeyAtAll_TheFourJudgingRoles_GetTheCataloguesOpus_AndThePresetStillApplies"/>,
    /// since that one proves the LOADER's collaborators work in isolation, not that the loader
    /// consults them. A hand-edited preset FILE naming a model
    /// (<c>Presets_Loader.Load_FromDisk</c>) is the one shape that still exercises it. Verified by
    /// hand: passing <c>presetTree: null</c> instead of <c>preset</c> in
    /// <c>OrchestratorConfig_Loader.Read_Model_OrNull</c>'s call to <c>Settings_Resolver.Resolve</c>
    /// turns this test red (Implementer falls to the catalogue's opus instead of the preset file's
    /// haiku).
    /// </summary>
    [Fact]
    public void AHandEditedPresetFile_NamingAModel_IsGenuinelyConsultedByTheLoader()
    {
        var presetFile = Path.Combine(_tempRoot, "hand-edited-preset-fix2-consulted.json");
        File.WriteAllText(presetFile, """{"models.implementer":"haiku"}""");
        var presetPath = presetFile.Replace('\\', '/');

        File.WriteAllText(_paths.ConfigFile, $$"""{"repos":[],"preset":"{{presetPath}}"}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("haiku", config.Get_ModelForRole(SessionRoles.Implementer));
    }

    /// <summary>
    /// RULED 2026-09-12 (task-6 fix round 2): absent and empty config.json are the same statement —
    /// "the owner has said nothing" — so they must resolve identically. A missing config.json used to
    /// bypass the preset rung entirely (<c>OrchestratorConfig_Factory.Create_Empty</c>, before this
    /// round); it now goes through the exact same path an empty <c>{"repos":[]}</c> file does.
    ///
    /// <para>
    /// NOTHING OBSERVABLE THROUGH <see cref="IOrchestratorConfig"/> DISTINGUISHES THE TWO CASES
    /// TODAY, and this is stated rather than hidden: neither shipped preset states a model any more
    /// (fix round 1), and no other preset-stated setting is wired through this loader yet — that
    /// lands with a later task (effort). So this test cannot be forced red by reverting the code
    /// change the way the two tests above can; it pins the OBSERVABLE EQUIVALENCE the ruling asks
    /// for now, so the day a preset-stated, loader-wired setting exists, a regression back to the
    /// early return shows up here first.
    /// </para>
    /// </summary>
    [Fact]
    public void WithNoConfigFileAtAll_ResolvesIdenticallyToAnEmptyOne()
    {
        // _paths.ConfigFile is never written in this test — genuinely absent, not merely empty.
        var absent = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");
        var empty = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(empty.Get_ModelForRole(role), absent.Get_ModelForRole(role));

        Assert.Equal(empty.TelegramStatusScreenshots, absent.TelegramStatusScreenshots);
        Assert.Equal(empty.OrchestrationTokenBudget, absent.OrchestrationTokenBudget);
        Assert.Equal(empty.VoiceTranscribeCommand, absent.VoiceTranscribeCommand);
    }

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

    /// <summary>Captures what the loader reported, so "a mistyped preset is named, not swallowed" can be asserted.</summary>
    sealed class RecordingLog : IOrchestrationLog
    {
        public List<string> Warnings { get; } = [];

        public void Log_Info(string orchId, string message)
        {
        }

        public void Log_Warning(string orchId, string message)
        {
            Warnings.Add(message);
        }

        public void Log_Error(string orchId, string message, Exception? exception)
        {
        }

        public event Action<IOrchestrationLogEntry>? EntryLogged
        {
            add { }
            remove { }
        }
    }
}

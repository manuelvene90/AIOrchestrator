using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

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
}

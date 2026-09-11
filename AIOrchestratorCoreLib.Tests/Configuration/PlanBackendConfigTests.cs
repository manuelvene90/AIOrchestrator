using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

/// <summary>
/// THE planBackend KEY IS HAND-EDITED, WHICH MAKES SAVING THE DANGEROUS PART. Save() used to build a
/// fresh object and write it over config.json, so any key it did not know about was deleted the first
/// time the owner pressed a button in the Settings window — a key nobody in this app writes could
/// therefore never survive one. These tests pin both halves: it is read, and it is still there after a
/// save that knows nothing about it.
/// </summary>
public class PlanBackendConfigTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PlanBackendConfigTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-planbackend-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>Every config.json written before the seam existed: no key, PLAN.md alone.</summary>
    [Fact]
    public void AnAbsentKeyReadsAsNoBackend()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        Assert.Null(OrchestratorConfig_Loader.Load_OrEmpty(_paths).PlanBackend);
    }

    [Fact]
    public void TheExternalKindIsReadWithItsAssemblyAndType()
    {
        File.WriteAllText(_paths.ConfigFile, """
            {
              "repos": [],
              "planBackend": { "kind": "external", "assembly": "/opt/adapters/Adapter.dll", "type": "Adapter.PlanBackend" }
            }
            """);

        var settings = OrchestratorConfig_Loader.Load_OrEmpty(_paths).PlanBackend;

        Assert.NotNull(settings);
        Assert.True(settings!.Value.Is_External());
        Assert.Equal("/opt/adapters/Adapter.dll", settings.Value.AssemblyPath);
        Assert.Equal("Adapter.PlanBackend", settings.Value.TypeName);
    }

    /// <summary>A malformed block is not a crash: no kind, no backend.</summary>
    [Fact]
    public void AKeylessBlockReadsAsNoBackend()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"planBackend":{"assembly":"/opt/x.dll"}}""");

        Assert.Null(OrchestratorConfig_Loader.Load_OrEmpty(_paths).PlanBackend);
    }

    /// <summary>
    /// THE ONE THAT WOULD HAVE BITTEN. The owner hand-edits planBackend, then changes a model in the
    /// Settings window; the save must leave their key exactly where it was.
    /// </summary>
    [Fact]
    public void SavingFromAWindowDoesNotEraseTheHandEditedKey()
    {
        File.WriteAllText(_paths.ConfigFile, """
            {
              "repos": [],
              "supervisorModel": "opus",
              "planBackend": { "kind": "external", "assembly": "/opt/adapters/Adapter.dll", "type": "Adapter.PlanBackend" }
            }
            """);

        // Exactly what the Settings window does: build a config from its own fields — which have no
        // plan-backend box — and save it.
        OrchestratorConfig_Loader.Save(
            OrchestratorConfig_Factory.Create([], "sonnet", null, null, null, null, null, null, null, null, null, null, null),
            _paths);

        var reloaded = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal("sonnet", reloaded.SupervisorModel);
        Assert.NotNull(reloaded.PlanBackend);
        Assert.Equal("Adapter.PlanBackend", reloaded.PlanBackend!.Value.TypeName);
    }

    /// <summary>
    /// And the rule is general rather than a special case for this key — any key a future version, a
    /// hand-edit or another tool put there survives a save.
    /// </summary>
    [Fact]
    public void SavingKeepsEveryUnknownKey()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"somethingNobodyHereKnowsAbout":42}""");

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Factory.Create_Empty(), _paths);

        Assert.Contains("somethingNobodyHereKnowsAbout", File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>A config the loader itself produced round-trips with its backend intact.</summary>
    [Fact]
    public void TheFactoryCarriesTheSettingsThroughTheLiveToggle()
    {
        var settings = new PlanBackendSettings(PlanBackendSettings.KIND_EXTERNAL, "/opt/x.dll", "X.Backend");

        var config = OrchestratorConfig_Factory.Create(
            [], null, null, null, null, null, null, null, null, null, null, null, null, settings);

        Assert.Equal(settings, OrchestratorConfig_Factory.Create_WithStatusScreenshots(config, true).PlanBackend);
    }

    /// <summary>
    /// A KIND THE OWNER MISTYPED AS A NUMBER STAYS LOUD. The 2026-09-10 tolerance fix in
    /// <c>Get_String_OrNull</c> turned <c>{"kind": 42}</c> into null, and a null kind read as "no
    /// planBackend configured" — so the loader never saw a settings object and its own promise (an
    /// unrecognised kind is a FAILED load, never a silent downgrade to PLAN.md) could not be kept.
    /// Found by the re-review of that fix, on the day it was written.
    /// </summary>
    [Fact]
    public void AKindOfTheWrongType_ReachesTheLoaderAsAWordItRefuses_RatherThanReadingAsUnconfigured()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"planBackend":{"kind":42}}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.NotNull(config.PlanBackend);
        Assert.Equal("42", config.PlanBackend!.Value.Kind);

        var loaded = PlanBackend_Loader.Load(config.PlanBackend.Value);

        Assert.NotNull(loaded.Error);
        Assert.Contains("42", loaded.Error!);
    }
}

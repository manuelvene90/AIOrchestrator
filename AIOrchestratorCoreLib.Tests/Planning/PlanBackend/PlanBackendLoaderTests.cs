using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// WHICH BACKEND A MACHINE RUNS, and what happens when the configured one cannot be loaded: PLAN.md
/// alone, WITH A REASON. Falling back silently is the failure mode that matters — the owner would
/// believe their planning system is connected and it would not have been since a path changed.
/// </summary>
public class PlanBackendLoaderTests
{
    /// <summary>No key in config.json — every installation that never opted in.</summary>
    [Fact]
    public void NoSettingsMeansPlanMdAlone()
    {
        var load = PlanBackend_Loader.Load(null);

        Assert.IsType<PlanMdBackend>(load.Backend);
        Assert.Null(load.Error);
    }

    [Fact]
    public void TheDefaultKindMeansPlanMdAlone()
    {
        var load = PlanBackend_Loader.Load(PlanBackendSettings.Default());

        Assert.IsType<PlanMdBackend>(load.Backend);
        Assert.Null(load.Error);
    }

    /// <summary>
    /// A TYPO IS NOT A DEFAULT. "externa1" used to read as the default and produce no error at all —
    /// the owner edits config.json, restarts, and their planning system is simply not connected with
    /// nothing anywhere saying why. That is the silent downgrade this loader's whole docstring exists
    /// to forbid, reachable by one slipped character in a hand-edited file.
    /// </summary>
    [Fact]
    public void AnUnrecognisedKindIsAnErrorRatherThanASilentDefault()
    {
        var load = PlanBackend_Loader.Load(new PlanBackendSettings("externa1", null, null));

        Assert.IsType<PlanMdBackend>(load.Backend);
        Assert.Contains("not recognised", load.Error);
        Assert.Contains("running on PLAN.md alone", load.Error);
    }

    /// <summary>External with nothing to load: named, not guessed at.</summary>
    [Fact]
    public void ExternalWithNoAssemblySaysWhatIsMissing()
    {
        var load = PlanBackend_Loader.Load(new PlanBackendSettings(PlanBackendSettings.KIND_EXTERNAL, null, null));

        Assert.IsType<PlanMdBackend>(load.Backend);
        Assert.Contains("assembly and/or type are missing", load.Error);
    }

    /// <summary>The path that will actually happen in the field: an adapter that moved.</summary>
    [Fact]
    public void AMissingAssemblyFileFallsBackAndNamesThePath()
    {
        var load = PlanBackend_Loader.Load(new PlanBackendSettings(
            PlanBackendSettings.KIND_EXTERNAL,
            Path.Combine(Path.GetTempPath(), $"no-such-adapter-{Guid.NewGuid():N}.dll"),
            "Some.Adapter"));

        Assert.IsType<PlanMdBackend>(load.Backend);
        Assert.Contains("does not exist", load.Error);
        Assert.Contains("running on PLAN.md alone", load.Error);
    }

    /// <summary>
    /// An assembly that IS loadable and a type name that is not in it — the typo case. The test suite's
    /// own assembly stands in for an adapter's, which is the honest way to exercise the load path
    /// without shipping a fixture .dll.
    /// </summary>
    [Fact]
    public void AMissingTypeInARealAssemblyFallsBackAndNamesTheType()
    {
        var load = PlanBackend_Loader.Load(new PlanBackendSettings(
            PlanBackendSettings.KIND_EXTERNAL,
            typeof(PlanBackendLoaderTests).Assembly.Location,
            "Nothing.Called.This"));

        Assert.IsType<PlanMdBackend>(load.Backend);
        Assert.Contains("was not found", load.Error);
    }

    /// <summary>A type that exists and is not a backend at all.</summary>
    [Fact]
    public void ATypeThatDoesNotImplementTheInterfaceFallsBack()
    {
        var load = PlanBackend_Loader.Load(new PlanBackendSettings(
            PlanBackendSettings.KIND_EXTERNAL,
            typeof(PlanBackendLoaderTests).Assembly.Location,
            typeof(PlanBackendLoaderTests).FullName));

        Assert.IsType<PlanMdBackend>(load.Backend);
        Assert.Contains("does not implement IPlanBackend", load.Error);
    }

    /// <summary>
    /// AND THE SUCCESS PATH, which is the one the other six do not prove. Loaded by name, out of an
    /// assembly, through the interface — the same route an adapter living outside this repository
    /// takes.
    /// </summary>
    [Fact]
    public void ARealImplementationIsLoadedByName()
    {
        var load = PlanBackend_Loader.Load(new PlanBackendSettings(
            PlanBackendSettings.KIND_EXTERNAL,
            typeof(RecordingPlanBackend).Assembly.Location,
            typeof(RecordingPlanBackend).FullName));

        Assert.Null(load.Error);
        Assert.IsType<RecordingPlanBackend>(load.Backend);
    }
}

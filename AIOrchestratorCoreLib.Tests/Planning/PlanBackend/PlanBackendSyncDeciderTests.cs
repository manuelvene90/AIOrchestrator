using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// The two claims the engine used to decide in code no test could reach: an installation with no
/// backend spends nothing, and an external one is asked once a minute rather than once per 2-second
/// tick.
/// </summary>
public class PlanBackendSyncDeciderTests
{
    static readonly DateTime NOW = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>THE PROPERTY THE WHOLE SEAM IS JUDGED ON — no backend, no pass, ever.</summary>
    [Fact]
    public void TheDefaultBackendNeverSyncs()
    {
        Assert.False(PlanBackendSync_Decider.Should_Sync(new PlanMdBackend(), DateTime.MinValue, NOW));
    }

    [Fact]
    public void AnExternalBackendSyncsOnceTheIntervalHasPassed()
    {
        var backend = new RecordingPlanBackend();

        Assert.True(PlanBackendSync_Decider.Should_Sync(backend, DateTime.MinValue, NOW));
        Assert.True(PlanBackendSync_Decider.Should_Sync(backend, NOW.AddSeconds(-PlanBackendSync_Decider.SYNC_INTERVAL_SECONDS), NOW));
    }

    /// <summary>At tick rate this would be 1,800 calls an hour against somebody else's system.</summary>
    [Fact]
    public void AnExternalBackendIsNotAskedOnEveryTick()
    {
        Assert.False(PlanBackendSync_Decider.Should_Sync(new RecordingPlanBackend(), NOW.AddSeconds(-2), NOW));
    }

    [Fact]
    public void ABackendIsLoadedOnceAndReloadedOnlyWhenItsSettingsChange()
    {
        var settings = new PlanBackendSettings(PlanBackendSettings.KIND_EXTERNAL, "/opt/x.dll", "X.Backend");

        Assert.True(PlanBackendSync_Decider.Needs_Reload(alreadyLoaded: false, null, null));
        Assert.False(PlanBackendSync_Decider.Needs_Reload(alreadyLoaded: true, null, null));
        Assert.False(PlanBackendSync_Decider.Needs_Reload(alreadyLoaded: true, settings, settings));
        Assert.True(PlanBackendSync_Decider.Needs_Reload(alreadyLoaded: true, settings, null));
        Assert.True(PlanBackendSync_Decider.Needs_Reload(
            alreadyLoaded: true,
            settings,
            settings with { TypeName = "X.Other" }));
    }
}

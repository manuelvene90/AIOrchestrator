using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The .paused marker is the half of PAUSE the session itself obeys. The app can hold the traffic
/// and stop nudging on its own, but the turn-end hook is a bash script that cannot read session.json:
/// without this file a paused session with open ledger lines is refused its turn end and keeps
/// working — which is precisely what the owner paused it to stop.
/// </summary>
public class PausedFlagMarkerTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PausedFlagMarkerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-paused-flag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder("arb-fix"));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Paused_WritesTheFlagWhereABashHookCanTestForIt()
    {
        PausedFlag_Marker.Sync(_paths, "arb-fix", paused: true, out _);

        // run-to-the-end-check.sh tests `[ -f "$ORCH_FOLDER/.paused" ]`, so the NAME and the
        // LOCATION are the contract, not an implementation detail this test may paraphrase.
        Assert.True(File.Exists(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".paused")));
        Assert.True(PausedFlag_Marker.Is_Paused(_paths, "arb-fix"));
    }

    [Fact]
    public void Unpaused_RemovesIt_SoTheSessionIsHeldToTheLedgerAgain()
    {
        PausedFlag_Marker.Sync(_paths, "arb-fix", paused: true, out _);
        PausedFlag_Marker.Sync(_paths, "arb-fix", paused: false, out _);

        Assert.False(PausedFlag_Marker.Is_Paused(_paths, "arb-fix"));
    }

    /// <summary>
    /// DERIVED, NEVER AUTHORED. A file that lifts a guard must not outlive the state it stands for,
    /// or one crash leaves a session permanently exempt from the hook with nothing saying why.
    /// </summary>
    [Fact]
    public void AFlagLeftBehindByADeadApp_IsClearedByTheNextSyncThatDisagreesWithIt()
    {
        File.WriteAllText(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".paused"), "stale");

        var changed = PausedFlag_Marker.Sync(_paths, "arb-fix", paused: false, out var failure);

        Assert.True(changed);
        Assert.Null(failure);
        Assert.False(PausedFlag_Marker.Is_Paused(_paths, "arb-fix"));
    }

    [Fact]
    public void Sync_ReportsOnlyRealTransitions_SoATickDoesNotLogAThousandNoOps()
    {
        Assert.True(PausedFlag_Marker.Sync(_paths, "arb-fix", paused: true, out _));
        Assert.False(PausedFlag_Marker.Sync(_paths, "arb-fix", paused: true, out _));
        Assert.True(PausedFlag_Marker.Sync(_paths, "arb-fix", paused: false, out _));
        Assert.False(PausedFlag_Marker.Sync(_paths, "arb-fix", paused: false, out _));
    }

    /// <summary>
    /// An orchestration folder that does not exist yet must not throw: pause can be set on a session
    /// whose folder the app has only just created, and a throw here would take down the tick that
    /// reconciles every other flag.
    /// </summary>
    [Fact]
    public void AnUnknownOrchestration_IsNotPaused_AndSyncingItCreatesTheFolder()
    {
        Assert.False(PausedFlag_Marker.Is_Paused(_paths, "never-existed"));

        PausedFlag_Marker.Sync(_paths, "never-existed", paused: true, out var failure);

        Assert.Null(failure);
        Assert.True(PausedFlag_Marker.Is_Paused(_paths, "never-existed"));
    }

    /// <summary>
    /// The text is for a human who finds the file and wonders what stopped their session, so it says
    /// what the state means rather than naming the field it mirrors.
    /// </summary>
    [Fact]
    public void TheFlagFile_SaysWhatItMeans()
    {
        PausedFlag_Marker.Sync(_paths, "arb-fix", paused: true, out _);

        var text = File.ReadAllText(PausedFlag_Marker.Build_FilePath(_paths, "arb-fix"));

        Assert.Contains("paused", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("turn", text, StringComparison.OrdinalIgnoreCase);
    }
}

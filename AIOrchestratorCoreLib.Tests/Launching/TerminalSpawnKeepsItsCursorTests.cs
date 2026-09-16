using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Launching;

/// <summary>
/// TASK 5 OF THE ONE-WAKE-MODEL SERIES (2026-09-15). Until this task a terminal spawn DELETED the
/// session's state file (<c>OrchestrationLauncherModel.Start_Session</c>), because that file's only
/// meaning was "the dispatcher runs my turns". Task 3 gave the file a second meaning — its cursors —
/// and Task 4 made the first meaning explicit via <see cref="IPrintSessionState.DrivesTurns"/>, so the
/// delete becomes a write that PRESERVES what a bridge-driven session had already delivered.
///
/// <para>
/// Uses the same construction <c>OrchestrationLauncherTests</c> uses — a real
/// <see cref="IOrchestrationLauncher"/> over <c>RecordingSpawner_Fake</c> (internal to this assembly,
/// defined there) — rather than a `LauncherTestHarness`, which does not exist under that name in this
/// tree.
/// </para>
/// </summary>
public sealed class TerminalSpawnKeepsItsCursorTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;

    public TerminalSpawnKeepsItsCursorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-terminal-demote-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);

        // An untouched config.json spawns every role in a terminal — the default this whole series
        // must leave unchanged for an owner who states nothing.
        _launcher = OrchestrationLauncher_Factory.Create(
            _paths,
            OrchestratorConfigProvider_Factory.Create(_paths),
            _store,
            new RecordingSpawner_Fake(),
            OrchestrationLog_Factory.Create(_paths));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Spawning_a_terminal_session_leaves_a_state_file_that_does_not_drive_turns()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId; // imp-1, spawned in a terminal by the default config

        var state = PrintSessionState_Store.Read_OrNull(
            PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Implementer, session.OrchId, memberId));

        Assert.NotNull(state);
        Assert.False(state!.DrivesTurns);
    }

    [Fact]
    public void A_session_that_was_bridge_driven_keeps_its_cursors_when_it_moves_to_a_terminal()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var orchId = session.OrchId;
        var memberId = session.Members[0].MemberId;

        var stateFile = PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Implementer, orchId, memberId);

        // Overwrite what the terminal spawn just wrote, to model what the file looked like a MOMENT
        // AGO, while this member was still bridge-driven: DrivesTurns = true and a cursor that had
        // already delivered one entry.
        var channelFile = _paths.Get_ImplementerChannelFile(orchId, memberId);
        var cursor = TurnCursor_Factory.Create("own", channelFile, 1, new HashSet<string> { "abc123" });
        var bridgeDriven = PrintSessionState_Factory.Create(
            Guid.NewGuid().ToString(), true, SessionRoles.Implementer, orchId, memberId,
            _tempRepo, null, channelFile, [cursor], 1, 0, []);
        PrintSessionState_Store.Write(stateFile, bridgeDriven);

        // Respawning into a terminal (the only runner this config knows) must demote it, not delete it.
        _launcher.Respawn_Implementer(orchId, memberId);

        var state = PrintSessionState_Store.Read_OrNull(stateFile);

        Assert.NotNull(state);
        Assert.False(state!.DrivesTurns);
        Assert.Contains("abc123", state.Cursors[0].Delivered);
    }
}

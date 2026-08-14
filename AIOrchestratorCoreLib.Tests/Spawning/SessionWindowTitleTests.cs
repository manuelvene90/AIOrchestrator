using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Termination;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Spawning;

/// <summary>
/// THE TITLE IS A CONTRACT BETWEEN THREE READERS, and it silently broke because each wrote its own
/// copy of it: the spawner titles the window, the terminator closes that window by title at app
/// exit, and the app's "Show session" button focuses it by title. A solo's window was titled
/// "SOLO · &lt;orch&gt;" while both readers derived "SOLO-1 · &lt;orch&gt;" from its member id, so
/// the window was never found: it survived every shutdown showing "[process exited with code
/// 4294967295]" (the app's own tree-kill) and the owner read that corpse as a session failing to
/// start. "Show session" could not find it either.
///
/// These tests compare the SPAWNED title against what each reader derives, rather than asserting a
/// literal string in each place — a literal in three files is what drifted in the first place.
/// </summary>
public class SessionWindowTitleTests
{
    const string SUPERVISION_ROOT = @"C:\Users\x\.claude\supervision";

    static string Spawned_Title(AIOrchestratorCoreLib.Spawning.SpawnCommand.ISpawnCommand command)
    {
        // --title is followed by the title itself; reading it back beats duplicating the format here.
        for (var i = 0; i < command.Arguments.Count - 1; i++)
        {
            if (command.Arguments[i] == "--title")
                return command.Arguments[i + 1];
        }

        throw new Exception("The spawn command carries no --title argument");
    }

    [Fact]
    public void Solo_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "solo-1", ".pid");
        var command = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, pidFile);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
    }

    [Fact]
    public void Solo_TheWindowItSpawns_IsTheWindowShowSessionFocuses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "solo-1", ".pid");
        var command = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, pidFile);

        // The app's row builder cannot be referenced from here (it lives in the WPF project), so the
        // shared builder it now calls stands in for it. That call is the whole fix on that side.
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForMember("solo-1", "arb-fix"));
    }

    [Fact]
    public void Implementer_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "imp-2", ".pid");
        var command = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:\repos\arb", null, pidFile);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForMember("imp-2", "arb-fix"));
    }

    [Fact]
    public void Reviewer_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "rev-1", ".pid");
        var command = SpawnCommand_Builder.Build_ForReviewer("arb-fix", "rev-1", @"C:\repos\arb", null, pidFile);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForMember("rev-1", "arb-fix"));
    }

    [Fact]
    public void Supervisor_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", ".supervisor.pid");
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, pidFile);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForSupervisor("arb-fix"));
    }

    [Fact]
    public void Communicator_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", ".communicator.pid");
        var command = SpawnCommand_Builder.Build_ForCommunicator("arb-fix", @"C:\repos\arb", null, pidFile);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForCommunicator("arb-fix"));
    }

    [Fact]
    public void GeneralSupervisor_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "general", ".pid");
        var command = SpawnCommand_Builder.Build_ForGeneralSupervisor(Path.Combine(SUPERVISION_ROOT, "general"), null, pidFile);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
    }

    /// <summary>
    /// A title the terminator cannot derive is a window nobody closes, so it must not fail QUIETLY —
    /// the pid file it cannot place is named. Only a path with no orchestration folder above it can
    /// reach this, which is not a layout the app ever writes.
    /// </summary>
    [Fact]
    public void APidFileOutsideTheKnownLayout_YieldsNoTitle_RatherThanAWrongOne()
    {
        Assert.Null(SessionTerminator.Build_TitleFragment_OrNull(".pid"));
    }
}

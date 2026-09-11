using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Termination;
using AIOrchestratorCoreLib.WindowFocus;
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
        var command = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, pidFile, null);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
    }

    [Fact]
    public void Solo_TheWindowItSpawns_IsTheWindowShowSessionFocuses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "solo-1", ".pid");
        var command = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, pidFile, null);

        // The app's row builder cannot be referenced from here (it lives in the WPF project), so the
        // shared builder it now calls stands in for it. That call is the whole fix on that side.
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForMember("solo-1", "arb-fix"));
    }

    [Fact]
    public void Implementer_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "imp-2", ".pid");
        var command = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:\repos\arb", null, pidFile, null);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForMember("imp-2", "arb-fix"));
    }

    [Fact]
    public void Reviewer_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "rev-1", ".pid");
        var command = SpawnCommand_Builder.Build_ForReviewer("arb-fix", "rev-1", @"C:\repos\arb", null, pidFile, null);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForMember("rev-1", "arb-fix"));
    }

    [Fact]
    public void Supervisor_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", ".supervisor.pid");
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, pidFile, null);

        Assert.Equal(Spawned_Title(command), SessionTerminator.Build_TitleFragment_OrNull(pidFile));
        Assert.Equal(Spawned_Title(command), SessionWindowTitle_Builder.Build_ForSupervisor("arb-fix"));
    }

    [Fact]
    public void Communicator_TheWindowItSpawns_IsTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", ".communicator.pid");
        var command = SpawnCommand_Builder.Build_ForCommunicator("arb-fix", @"C:\repos\arb", null, pidFile, null);

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

    // ─── The goal name in the title (owner request, 2026-08-21: "it's a bit difficult to understand
    // which terminal I have to open when I'm at the pc") ───────────────────────────────────────────

    /// <summary>
    /// THE WHOLE POINT OF THE CHANGE. A rename already existed, but the name lived only in the live
    /// window text and the watchdog respawns sessions freely — so a named orchestration reverted to a
    /// bare "SUP · arb-fix" at the first respawn and never came back. Spawning it named is what makes
    /// it durable, and this is what pins it.
    /// </summary>
    [Fact]
    public void ASpawnedWindow_CarriesTheGoalName_SoARespawnDoesNotLoseIt()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", ".supervisor.pid");
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:
eposrb", null, pidFile, "away mode loop");

        Assert.Equal("SUP · arb-fix · away mode loop", Spawned_Title(command));
    }

    /// <summary>
    /// AND THE LOAD-BEARING HALF: the terminator has only a pid-file PATH, so it can never know the
    /// display name — it derives the bare fragment. If the name broke that match, app exit would stop
    /// closing the window and the terminal would survive every shutdown, which is the exact failure
    /// this whole test class exists because of.
    /// </summary>
    [Fact]
    public void ANamedWindow_IsStillTheWindowTheTerminatorCloses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "solo-1", ".pid");
        var command = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:
eposrb", null, pidFile, "away mode loop");

        var fragment = SessionTerminator.Build_TitleFragment_OrNull(pidFile);

        Assert.Equal("SOLO · arb-fix · away mode loop", Spawned_Title(command));
        Assert.True(SessionWindowTitle_Matcher.Matches(Spawned_Title(command), fragment!));
    }

    /// <summary>Show/Organize build the same bare fragment from ids alone, and must still find it.</summary>
    [Fact]
    public void ANamedWindow_IsStillTheWindowShowSessionFocuses()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", "imp-2", ".pid");
        var command = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:
eposrb", null, pidFile, "away mode loop");

        Assert.True(SessionWindowTitle_Matcher.Matches(
            Spawned_Title(command),
            SessionWindowTitle_Builder.Build_ForMember("imp-2", "arb-fix")));
    }

    /// <summary>
    /// AND IT MUST NOT WIDEN THE MATCH. The boundary rule is what stops `arb-fix-1` finding
    /// `arb-fix-10`'s window; a display name appended to the title must not reopen that hole —
    /// otherwise Show focuses the wrong terminal and the terminator can close a working session.
    /// </summary>
    [Fact]
    public void ANamedWindow_IsStillNotMatchedByAShorterOrchId()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix-10", ".supervisor.pid");
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix-10", @"C:
eposrb", null, pidFile, "away mode loop");

        Assert.False(SessionWindowTitle_Matcher.Matches(
            Spawned_Title(command),
            SessionWindowTitle_Builder.Build_ForSupervisor("arb-fix-1")));
    }

    /// <summary>
    /// An unnamed orchestration keeps the bare title. Null, empty and whitespace all mean "no name" —
    /// a title ending in a dangling " · " would be litter in the owner's taskbar, and `DisplayName`
    /// is free text from an agent, so blank is a real input rather than a theoretical one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoGoalName_TheTitleStaysTheBareFragment(string? displayName)
    {
        Assert.Equal("SUP · arb-fix", SessionWindowTitle_Builder.Build_Title("SUP · arb-fix", displayName));
    }

    /// <summary>
    /// The live rename and the spawn title must produce the SAME string, or a respawn would visibly
    /// change a window's name without anything having happened.
    /// </summary>
    [Fact]
    public void TheRenameFormat_AndTheSpawnFormat_AreOneFormat()
    {
        var pidFile = Path.Combine(SUPERVISION_ROOT, "arb-fix", ".supervisor.pid");
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:
eposrb", null, pidFile, "away mode loop");

        Assert.Equal(
            Spawned_Title(command),
            SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForSupervisor("arb-fix"), "away mode loop"));
    }
}

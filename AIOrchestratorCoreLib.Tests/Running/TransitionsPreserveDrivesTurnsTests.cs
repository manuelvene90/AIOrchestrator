using System.Linq;
using System.Reflection;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A TRANSITION MUST NOT PROMOTE A SESSION BACK TO DRIVING ITS OWN TURNS. Every
/// <c>CreateFrom_Existing_*</c> forwards to <c>Create</c>, whose <c>drivesTurns</c> defaults to true —
/// so omitting it is SILENT, and the omission puts a terminal session back under the dispatcher while
/// it still has a window. That is the "a window AND headless turns answering the same brief" failure
/// the launcher's delete existed to prevent (<c>OrchestrationLauncherModel</c>).
///
/// <para>
/// IT IS NOT HYPOTHETICAL: <see cref="PrintSessionState_Factory.CreateFrom_Existing_Cursors"/> is what
/// the ticket sweep calls to advance a terminal session's cursor, so the very first wake ticket would
/// have flipped that session back. Found reviewing Task 3 of the one-wake-model plan, 2026-09-15.
/// </para>
/// </summary>
public class TransitionsPreserveDrivesTurnsTests
{
    static IPrintSessionState Terminal_Session()
    {
        return PrintSessionState_Factory.Create(
            "33333333-3333-3333-3333-333333333333", true, SessionRoles.Implementer, "repo-1", "imp-1",
            "/tmp", null, "/tmp/channel.md", new List<ITurnCursor>(), 3, 0, [], null, drivesTurns: false);
    }

    [Fact]
    public void Every_transition_keeps_a_terminal_session_out_of_the_dispatcher()
    {
        var source = Terminal_Session();
        var executed = ExecutedTurn_Factory.Create(3, "req-3", 1, 2, DateTime.UtcNow, "ok", null);

        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(source, executed, source.SessionId, []).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_Cursors(source, []).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_SessionClaimed(source, source.SessionId).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_SessionUnclaimed(source, source.SessionId).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_Relaunched(source, "/tmp", null).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_AttemptFailed(source).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_LimitDeferred(source, DateTime.UtcNow.AddMinutes(5)).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_LimitDeferralCleared(source).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_AttemptsReset(source).DrivesTurns);
        Assert.False(PrintSessionState_Factory.CreateFrom_Existing_TurnSkipped(source).DrivesTurns);
    }

    /// <summary>
    /// A BRIDGE SESSION IS NOT DEMOTED EITHER — without this the case above would pass on an
    /// implementation that simply hardcoded false, which pins nothing (CLAUDE.md decision 20).
    /// </summary>
    [Fact]
    public void And_a_bridge_session_stays_driving()
    {
        var source = PrintSessionState_Factory.Create(
            "44444444-4444-4444-4444-444444444444", true, SessionRoles.Implementer, "repo-1", "imp-2",
            "/tmp", null, "/tmp/channel.md", new List<ITurnCursor>(), 1, 0, []);

        Assert.True(source.DrivesTurns);
        Assert.True(PrintSessionState_Factory.CreateFrom_Existing_Cursors(source, []).DrivesTurns);
        Assert.True(PrintSessionState_Factory.CreateFrom_Existing_AttemptFailed(source).DrivesTurns);
    }

    /// <summary>
    /// THE CENSUS. The case above names each transition by hand, so a NEW one added later would not be
    /// covered and its omission would be silent again. This fails the moment the count changes, and
    /// points at the file — it is the only way a hand-written list stays honest.
    /// </summary>
    [Fact]
    public void The_list_above_covers_every_transition_that_exists()
    {
        var transitions = typeof(PrintSessionState_Factory)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("CreateFrom_Existing_", StringComparison.Ordinal))
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(10, transitions.Count);
    }
}

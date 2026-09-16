using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE INTERLOCK OF THE ONE-WAKE-MODEL SERIES. From Task 5 a terminal session keeps a state file, and
/// the file used to mean "run my turns". If discovery did not screen on <c>DrivesTurns</c> the member
/// would have a window AND headless turns answering the same brief — the exact failure the launcher's
/// delete existed to prevent (<c>OrchestrationLauncherModel.cs:554</c>).
///
/// <para>
/// BOTH DIRECTIONS ARE PINNED (CLAUDE.md decision 20 — an assertion that could pass for two reasons
/// pins nothing). The negative case alone would also pass on a dispatcher that dispatches nothing at
/// all; the positive case proves the gate discriminates by <c>DrivesTurns</c> rather than blocking
/// every session outright.
/// </para>
/// </summary>
public class DispatcherIgnoresNonDrivingSessionsTests
{
    [Fact]
    public void A_registered_session_that_does_not_drive_turns_is_not_dispatched()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, drivesTurns: false);
        harness.Write_Scenario("""{"default":{"result":"imp-1 online\n\nack"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "TASK 1 — build the thing", "go");
        dispatcher.Tick(DateTime.Now);
        Thread.Sleep(300);
        dispatcher.Tick(DateTime.Now);

        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Empty(harness.Read_Invocations());
        Assert.Empty(harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns);
    }

    [Fact]
    public async Task A_registered_session_that_drives_turns_is_dispatched_as_before()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, drivesTurns: true);
        harness.Write_Scenario("""{"default":{"result":"imp-1 online\n\nack"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "TASK 1 — build the thing", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Single(harness.Read_Invocations());
    }

    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }
}

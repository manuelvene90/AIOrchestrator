using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Tests.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// "AN ANSWER IS COMING" ONLY WHEN THE TURN BEHIND IT CAN KEEP THE PROMISE. fincanva-6, 2026-09-11: the
/// phone read "✓✓ · 🔴 Sup: turn ended without a reply — nudged, an answer is coming" at 08:19:24 and
/// 08:22:31, each some twenty seconds after the same session's turn had stalled on
/// "Not logged in · Please run /login".
///
/// <para>
/// The bridge's own path to this line waits a hard-coded 150 s grace window on the wall clock, so it is
/// not driven end to end here; what is driven end to end is the fact it now asks — the failed attempts
/// the REAL dispatcher records for the owner-facing session — fed to the decision that uses it.
/// </para>
/// </summary>
public class OwnerNudgeReceiptDeciderTests
{
    const string ORCH = "repo-1";
    const string SPEAKER = "🔴 Sup";

    const string NOT_LOGGED_IN = """{"default":{"is_error":true,"exit_code":1,"result":"Not logged in · Please run /login"}}""";

    [Fact]
    public void ATurnThatHasNotFailed_StillGetsThePromise()
    {
        var text = OwnerNudgeReceipt_Decider.Build_Receipt_OrNull(SPEAKER, 0);

        Assert.Equal("✓✓  ·  🔴 Sup: turn ended without a reply — nudged, an answer is coming", text);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(PrintTurn_Words.MAX_ATTEMPTS)]
    public void ATurnThatHasFailed_GetsNothingNew(int failedAttempts)
    {
        Assert.Null(OwnerNudgeReceipt_Decider.Build_Receipt_OrNull(SPEAKER, failedAttempts));
    }

    /// <summary>
    /// The incident, through the real dispatcher: the supervisor's turn fails until it stalls, and the
    /// receipt the owner would be sent at the nudge is asked for from what the dispatcher wrote.
    /// </summary>
    [Fact]
    public async Task ASupervisorWhoseTurnStalled_IsNeverSaidToHaveAnAnswerComing()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario(NOT_LOGGED_IN);

        // Registered and not yet run: nothing has failed, so the promise stands. The control half —
        // without it "never says the promise" would pass for a decider that had simply lost the line.
        var beforeAnyTurn = OwnerFacingTurn_Reader.Read_CurrentTurnFailures(harness.Paths, ORCH, harness.Store.Get_Session_OrNull(ORCH));
        Assert.Null(beforeAnyTurn.ReadFailure);
        Assert.NotNull(OwnerNudgeReceipt_Decider.Build_Receipt_OrNull(SPEAKER, beforeAnyTurn.FailedAttempts));

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));
        Assert.True(ChannelAppender_OwnerSays(harness, "Dimmi che smoke devo fare per finire"));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).FailedAttempts >= PrintTurn_Words.MAX_ATTEMPTS, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var stalled = OwnerFacingTurn_Reader.Read_CurrentTurnFailures(harness.Paths, ORCH, harness.Store.Get_Session_OrNull(ORCH));

        Assert.Null(stalled.ReadFailure);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, stalled.FailedAttempts);
        Assert.Null(OwnerNudgeReceipt_Decider.Build_Receipt_OrNull(SPEAKER, stalled.FailedAttempts));
    }

    static bool ChannelAppender_OwnerSays(PrintRunnerTestHarness harness, string text)
    {
        return AIOrchestratorCoreLib.Channels.ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), text, DateTime.Now);
    }
}

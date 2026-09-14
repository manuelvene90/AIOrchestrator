using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ClosingTurn;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE STAGED HANG, END TO END — the spec's "a deliberately hung member turn is still killed", driven
/// through the dispatcher with the REAL readers: the fake CLI sleeps with no output, writes no
/// transcript and starts no child, so every sign of life is genuinely absent. It must die at the
/// silence limit, long before the deadline, and be closed down exactly as a deadline kill is — with
/// its records saying which brake fired, because the advice differs (split the brief vs. look for a
/// hang).
/// </summary>
public class SilenceKillClosingTurnTests
{
    [Fact]
    public async Task AHungMemberTurn_IsKilledAtTheSilenceLimit_AndClosedDownLikeADeadlineKill()
    {
        // A two-second brake under a two-minute deadline: a kill inside the test's window can only be the brake's.
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: 2, memberSilenceMinutes: 2.0 / 60);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);

        harness.Write_Scenario("""{"turns":[{"delay_ms":60000},{"result":"WHERE I AM\n\nStuck after the parser.","total_cost_usd":0.0042}],"default":{"result":"NOT EXPECTED\n\nthe pending set was re-run"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, "BRIEF — parser", "Write the parser.", DateTime.Now));

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, TimeSpan.FromSeconds(40)));
        await dispatcher.Stop_Async();

        var invocations = harness.Read_Invocations();
        Assert.Equal(2, invocations.Count);
        Assert.Contains("--resume", PrintRunnerTestHarness.Args(invocations[1]));
        Assert.Contains(ClosingTurn_Words.BUDGET_FLAG, PrintRunnerTestHarness.Args(invocations[1]));

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        var killedRecord = Assert.Single(entries, entry => entry.Subject.Contains($"{PrintTurn_Words.TURN_ENDED_SUBJECT} {memberId} turn 1 — {TurnOutcomes.TIMEOUT}"));
        Assert.Contains("killed by the silence brake while working", killedRecord.Body);
        Assert.DoesNotContain("at the deadline", killedRecord.Body);

        var report = Assert.Single(entries, entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Equal("WHERE I AM", report.Subject);

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Equal(0, state.FailedAttempts);
        Assert.Equal(TurnOutcomes.TIMEOUT, state.ExecutedTurns[0].Outcome);
    }
}

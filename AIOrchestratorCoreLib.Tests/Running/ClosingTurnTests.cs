using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ClosingTurn;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.TurnExecutor;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE DEADLINE KILL AND ITS ONE CLOSING TURN. Measured on the VPS 2026-09-07/09: 28 turns hit the
/// 30-minute timeout while WORKING (32–92 tool calls, 7–30 M input tokens) and had the same pending
/// entries re-run from scratch after the backoff — roughly 14 hours of work discarded. The owner's
/// decision was not a longer timeout (longer = more context) but a short closing turn that resumes
/// the killed transcript, asks the session to say where it is, and lets the next turn start fresh.
///
/// <para>
/// A SILENCE KILL IS NOT A DEADLINE KILL and keeps today's retry path: a mute process has said
/// nothing, so there is nothing to close down — the heartbeat killed it precisely because it was
/// not working.
/// </para>
/// </summary>
public class ClosingTurnTests
{
    /// <summary>
    /// Five seconds, so a killed turn and its closing turn each get a window a real
    /// <c>dotnet FakeClaude.dll</c> start fits inside. The production numbers are 30 minutes and
    /// <see cref="ClosingTurn_Words.TIMEOUT"/>.
    /// </summary>
    const double TIMEOUT_MINUTES = 5.0 / 60;

    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }

    [Fact]
    public async Task ADeadlineKill_RunsOneClosingTurnOnTheKilledTranscript_AndItsReportLandsInTheChannel()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);

        // Turn 1 outlives the deadline; turn 2 is the closing turn and answers at once.
        harness.Write_Scenario("""{"turns":[{"delay_ms":30000},{"result":"WHERE I AM\n\nParser done, tests half-written; next step is Parse_All.","total_cost_usd":0.0042}],"default":{"result":"NOT EXPECTED\n\nthe pending set was re-run"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF — parser", "Write the parser.");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, TimeSpan.FromSeconds(40)));

        // Nothing more starts: the pending set was consumed by the turn that was killed, so the
        // 60 s backoff and the attempt counter never come into it.
        Thread.Sleep(400);
        dispatcher.Tick(DateTime.Now);
        Thread.Sleep(400);
        dispatcher.Tick(DateTime.Now);
        await dispatcher.Stop_Async();

        var invocations = harness.Read_Invocations();
        Assert.Equal(2, invocations.Count);

        var killed = PrintRunnerTestHarness.Args(invocations[0]);
        var closing = PrintRunnerTestHarness.Args(invocations[1]);
        var killedSessionId = killed[killed.IndexOf("--session-id") + 1];

        Assert.Contains("--resume", closing);
        Assert.Equal(killedSessionId, closing[closing.IndexOf("--resume") + 1]);
        Assert.Contains(ClosingTurn_Words.BUDGET_FLAG, closing);
        Assert.Equal("2.00", closing[closing.IndexOf(ClosingTurn_Words.BUDGET_FLAG) + 1]);
        Assert.DoesNotContain("--session-id", closing);
        Assert.Equal("stdin", invocations[1]["prompt_source"]!.GetValue<string>());
        Assert.Contains("stopped at the time limit", invocations[1]["prompt"]!.GetValue<string>());

        // The report is the member's entry, filed the way every reply is — so the supervisor's
        // watcher wakes on it.
        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        var report = Assert.Single(entries, entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Equal("WHERE I AM", report.Subject);
        Assert.Contains("next step is Parse_All", report.Body);

        // Two records: the killed turn as a timeout, the closing turn tagged as one.
        Assert.Single(entries, entry => entry.Subject.Contains($"{PrintTurn_Words.TURN_ENDED_SUBJECT} {memberId} turn 1 — {TurnOutcomes.TIMEOUT}"));
        var closingEnded = Assert.Single(entries, entry => entry.Subject.Contains(ClosingTurn_Words.REQUEST_ID_SUFFIX));
        Assert.Contains($"{TurnOutcomes.SUCCESS}", closingEnded.Subject);
        Assert.Contains("cost_usd: 0.0042", closingEnded.Body);

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Equal(0, state.FailedAttempts);
        Assert.Equal(2, state.NextTurnNumber);
        Assert.Equal(TurnOutcomes.TIMEOUT, state.ExecutedTurns[0].Outcome);
        Assert.Equal(killedSessionId, state.ExecutedTurns[0].SessionId);
        Assert.Single(Assert.Single(state.Cursors).Delivered);
    }

    [Fact]
    public async Task AfterTheClosingTurn_TheNextBriefStartsAFreshSession_NotAReplayOfTheKilledOne()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES, resumeForMembers: "fresh");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"turns":[{"delay_ms":30000},{"result":"WHERE I AM\n\nhalf-way"}],"default":{"result":"REPORT\n\nsecond brief"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "first");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, TimeSpan.FromSeconds(40)));

        var killedSessionId = harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns[0].SessionId;

        Append_Supervisor(harness, orchId, memberId, "GO AHEAD", "second");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 2, TimeSpan.FromSeconds(40)));
        await dispatcher.Stop_Async();

        var invocations = harness.Read_Invocations();
        Assert.Equal(3, invocations.Count);

        var third = PrintRunnerTestHarness.Args(invocations[2]);
        Assert.Contains("--session-id", third);
        Assert.NotEqual(killedSessionId, third[third.IndexOf("--session-id") + 1]);

        // The second turn answers the SECOND brief only — the first was consumed by the turn that
        // was killed and reported on by the closing turn.
        var executed = harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns;
        Assert.Equal(1, executed[0].LastEntryIndex);
        Assert.Equal(TurnOutcomes.SUCCESS, executed[1].Outcome);
        Assert.Contains("second brief", harness.Read_Channel(orchId, memberId));
    }

    [Fact]
    public async Task ASilenceKill_KeepsTodaysRetryPath_AndRunsNoClosingTurn()
    {
        // Silence limit 1 s against a 5-minute turn timeout: the heartbeat kills the mute process
        // long before the deadline, and a process that has said nothing has nothing to close down.
        using var harness = new PrintRunnerTestHarness("implementer:stream", turnTimeoutMinutes: 5, streamSilenceSeconds: 1);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add(entry.Message);

        harness.Write_Scenario("""{"turns":[{"result":"online"},{"result":"too late","delay_ms":20000},{"result":"online"},{"result":"REPORT\n\nsecond attempt"}]}""");
        var dispatcher = harness.Create_Dispatcher(TimeSpan.FromMilliseconds(50));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, TimeSpan.FromSeconds(45)),
            $"the mute turn was not cut short. Log:\n{string.Join("\n", logged)}");
        await dispatcher.Stop_Async();

        Assert.Contains(logged, message => message.Contains("said nothing for", StringComparison.Ordinal));
        Assert.DoesNotContain(logged, message => message.Contains(ClosingTurn_Words.REQUEST_ID_SUFFIX, StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Read_Invocations(), invocation => PrintRunnerTestHarness.Args(invocation).Contains(ClosingTurn_Words.BUDGET_FLAG));

        // The retry answered the same brief, as it always has: one report, one executed turn.
        Assert.Single(ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)), entry => entry.Author == ChannelAuthors.Implementer);
    }

    [Fact]
    public async Task AClosingTurnThatAlsoTimesOut_SaysWhyOnce_AndLeavesTodaysRetryToRun()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"delay_ms":30000}}""");

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add(entry.Message);

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "slow", "a");

        // Attempt 1: the turn is killed, its closing turn is killed too — so the attempt counts and
        // the same pending set is retried, which is exactly today's behaviour.
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= 1, TimeSpan.FromSeconds(40)));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_Invocations().Count >= 3, TimeSpan.FromSeconds(40)));
        await dispatcher.Stop_Async();

        Assert.Contains(logged, message => message.Contains("closing turn", StringComparison.OrdinalIgnoreCase) && message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)), entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Empty(harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns);
    }

    [Fact]
    public async Task ATransportThatCannotRunAClosingTurn_SaysSo_AndFallsBackToTodaysBehaviour()
    {
        // The stream executor runs its closing turn on the print rung below it, because the living
        // process is dead by then. Wired with no rung below, it can do nothing but say so.
        //
        // TEN SECONDS, NOT FIVE, AND THE ROOM IS FOR THE BOOT. This test needs the WORK turn to be the
        // one killed at the deadline; a boot killed at the deadline now runs no closing turn at all
        // (nothing was asked, so there is nothing to close — ITurnResult.NothingToClose, added
        // 2026-09-09), and then the line this asserts on is never written. At a five-second turn
        // timeout the boot is a cold `dotnet FakeClaude.dll` start racing that deadline under a
        // parallel suite, and it lost once in nine runs on 2026-09-09.
        using var harness = new PrintRunnerTestHarness("implementer:stream", turnTimeoutMinutes: 10.0 / 60);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);
        harness.Write_Scenario("""{"turns":[{"result":"online"},{"result":"too late","delay_ms":30000}],"default":{"result":"online"}}""");

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add(entry.Message);

        var streamOnly = TurnExecutor_Factory.Create_Stream(harness.Paths, PrintRunnerTestHarness.Fake_Invocation(), null, harness.Log, harness.ConfigProvider);
        var dispatcher = PrintTurnDispatcher_Factory.Create(harness.Paths, harness.Store, harness.ConfigProvider, [streamOnly], harness.Log, TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= 1, TimeSpan.FromSeconds(40)),
            $"the deadline kill was never recorded. Log:\n{string.Join("\n", logged)}");
        await dispatcher.Stop_Async();

        Assert.Contains(logged, message => message.Contains("no closing turn", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns);
    }

    [Fact]
    public async Task TheClosingTurnIsTaggedInTheTurnLog_SoTailSaysWhichTurnItWas()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"turns":[{"delay_ms":30000},{"result":"WHERE I AM\n\nhalf-way"}]}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, TimeSpan.FromSeconds(40)));
        await dispatcher.Stop_Async();

        var records = TurnLog_Store.Read_LastRecords(TurnLog_Store.Get_File(harness.Paths, SessionRoles.Implementer, orchId, memberId), 50);
        var closing = Assert.Single(records, record => record[TurnLog_Store.KIND_KEY]?.GetValue<string>() == TurnLog_Store.KIND_CLOSING_TURN);

        Assert.Equal($"{orchId}/{memberId}/1-{ClosingTurn_Words.REQUEST_ID_SUFFIX}", TurnLog_Store.Read_RequestId_OrNull(closing));
        Assert.Equal("WHERE I AM\n\nhalf-way", closing["result"]!.GetValue<string>());
    }
}

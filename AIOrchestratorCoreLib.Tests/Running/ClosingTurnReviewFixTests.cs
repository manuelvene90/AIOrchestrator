using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ClosingTurn;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.TurnExecutor;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE DEFECTS AN ADVERSARIAL REVIEW OF THE CLOSING TURN FOUND (2026-09-09), each pinned by the
/// failure it actually caused. Every one of these passed the suite before its fix, which is the only
/// reason they are worth writing down: the feature's own tests all walk the happy branch, and five of
/// the six branches under it were saying something untrue.
///
/// <para>
/// The costliest was not a crash. A supervisor woken by the OWNER, killed at the deadline, filed its
/// closing report to a spoke — and the owner's question was marked delivered by a report that never
/// mentioned it. Both <c>turn_ended</c> records are agent-audience, so nothing reached the phone, and
/// there is nobody above a supervisor to re-brief it: the owner asked and would never have been
/// answered.
/// </para>
/// </summary>
public class ClosingTurnReviewFixTests
{
    /// <summary>Five seconds — a window a real <c>dotnet FakeClaude.dll</c> start fits inside; production is 30 minutes.</summary>
    const double TIMEOUT_MINUTES = 5.0 / 60;

    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }

    static IReadOnlyList<IChannelEntry> Read_OwnerChannel(PrintRunnerTestHarness harness, string orchId)
    {
        return ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_OwnerChannelFile(orchId)));
    }

    // ------------------------------------------------------------------ what the closing turn consumes

    /// <summary>
    /// A CLOSING REPORT CONSUMES ONLY WHAT IT ANSWERED. The killed turn's whole pending set used to be
    /// marked delivered whatever the report said, so a supervisor that was woken by the owner and wrote
    /// its "where I got to" to a spoke silently ate the owner's question: nothing in the owner channel,
    /// nothing on the phone (both records are agent-audience), and no counterpart above a supervisor to
    /// notice. Now the owner's entry stays pending and the very next turn answers it.
    /// </summary>
    [Fact]
    public async Task AClosingReportAddressedToASpoke_LeavesTheOwnersQuestionPending_AndTheNextTurnAnswersIt()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", turnTimeoutMinutes: TIMEOUT_MINUTES);
        harness.Register_Supervisor("repo-1", SessionRunners.Print);
        var session = harness.Store.Add_Member("repo-1", MemberKinds.Implementer);
        var memberId = session.Members[^1].MemberId;

        // 1: the boot turn. 2: the work turn the owner's question woke, killed at the deadline. 3: the
        // closing turn, which reports to the spoke and never mentions the owner. 4: the re-run.
        harness.Write_Scenario("""
        {"turns":[
          {"result":"ONLINE\n\nsupervisor up"},
          {"delay_ms":30000},
          {"result":"TO: MEMBER\nHEADS UP\n\nI was cut off at the deadline; I had read the owner's question and got as far as the plan."}
        ],"default":{"result":"WHERE WE ARE\n\nThe parser is half-written."}}
        """.Replace("MEMBER", memberId));

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS), "the boot turn never ran");

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile("repo-1"), "Where are we with the parser?", DateTime.Now));

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS), "the killed turn was never recorded as executed");

        // The report reached the spoke it addressed ...
        Assert.Contains(ChannelEntry_Parser.Parse_All(harness.Read_Channel("repo-1", memberId)), entry => entry.Subject == "HEADS UP");

        // ... and the owner's question was NOT consumed by it.
        var afterClosing = harness.Read_State(SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
        Assert.Empty(afterClosing.Cursors.First(cursor => cursor.SourceKey == TurnSource_Factory.OWNER_KEY).Delivered);

        // So the next turn is handed it, and the owner gets an answer where they are looking.
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Read_OwnerChannel(harness, "repo-1").Any(entry => entry.Author == ChannelAuthors.Supervisor && entry.Subject == "WHERE WE ARE"), PrintRunnerTestHarness.GENEROUS), "the owner's question was never re-run");
        await dispatcher.Stop_Async();

        Assert.NotEmpty(harness.Read_State(SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).Cursors.First(cursor => cursor.SourceKey == TurnSource_Factory.OWNER_KEY).Delivered);
    }

    /// <summary>
    /// AN EMPTY REPORT IS NOT A REPORT. <c>Write_Reply_Async</c> answers true for empty text — it files
    /// the "(no message)" entry every other path relies on — so a closing turn that said nothing at all
    /// still advanced the cursors and retired the brief. The brief has to survive that.
    /// </summary>
    [Fact]
    public async Task AClosingTurnThatSaysNothing_IsAFailedClosingTurn_AndTheBriefIsStillPending()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"turns":[{"delay_ms":30000},{"result":""}],"default":{"result":"WHERE I AM\n\nhalf-way"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= 1, PrintRunnerTestHarness.GENEROUS), "the empty closing report was accepted as a report");
        await dispatcher.Stop_Async();

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Empty(state.ExecutedTurns);
        Assert.Equal(0, state.Cursors.Sum(cursor => cursor.Delivered.Count));
        Assert.DoesNotContain(ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)), entry => entry.Subject == PrintTurnEntry_Splitter.EMPTY_SUBJECT);
    }

    /// <summary>
    /// A BOOT THAT EATS THE WHOLE TURN TIMEOUT NEVER SENT THE BRIEF. It came back timed out and unmarked,
    /// which is the shape of a deadline kill, so the session was given a closing turn on a transcript
    /// holding nothing but its role command — and the brief it had never seen was marked delivered.
    /// </summary>
    [Fact]
    public async Task AStreamBootKilledAtTheDeadline_RunsNoClosingTurn_AndTheUnseenBriefStaysPending()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        // The role command itself outlives the turn timeout, so the brief is never sent.
        harness.Write_Scenario("""{"turns":[{"result":"online","delay_ms":30000}],"default":{"result":"NOT EXPECTED\n\na closing turn ran on a transcript that holds only the boot"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromSeconds(30));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= 1, PrintRunnerTestHarness.GENEROUS), "the boot kill was never recorded");
        await dispatcher.Stop_Async();

        Assert.DoesNotContain(harness.Read_Invocations(), invocation => PrintRunnerTestHarness.Args(invocation).Contains(ClosingTurn_Words.BUDGET_FLAG));

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Empty(state.ExecutedTurns);
        Assert.Equal(0, state.Cursors.Sum(cursor => cursor.Delivered.Count));
    }

    // ------------------------------------------------------------------ what the records claim

    /// <summary>
    /// THE KILLED TURN'S RECORD SAYS WHAT HAPPENED, NOT WHAT WAS ABOUT TO BE TRIED. It was appended
    /// FIRST, before anything was attempted, claiming "a closing turn was run … and these entries are
    /// not re-run" — on the branch where the transport has no print rung, no closing turn is started at
    /// all and the entries are re-run within the minute.
    /// </summary>
    [Fact]
    public async Task WithNoPrintRungBeneathIt_TheKilledTurnsRecordSaysTheEntriesAreRetried()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);
        harness.Write_Scenario("""{"turns":[{"result":"online"},{"result":"too late","delay_ms":30000}],"default":{"result":"online"}}""");

        var streamOnly = TurnExecutor_Factory.Create_Stream(harness.Paths, PrintRunnerTestHarness.Fake_Invocation(), null, harness.Log, harness.ConfigProvider);
        var dispatcher = PrintTurnDispatcher_Factory.Create(harness.Paths, harness.Store, harness.ConfigProvider, [streamOnly], harness.Log, TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var record = Assert.Single(ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)), entry => entry.Subject.Contains(PrintTurn_Words.TURN_ENDED_SUBJECT));

        Assert.DoesNotContain("a closing turn wrote where it got to", record.Body);
        Assert.Contains("retried as before", record.Body);
        Assert.Contains("no print rung", record.Body);
    }

    /// <summary>Same lie on the branch where the closing turn WAS run and was itself killed: the entries are pending again, and the record has to say so.</summary>
    [Fact]
    public async Task WhenTheClosingTurnIsItselfKilled_TheKilledTurnsRecordSaysTheEntriesAreRetried()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"delay_ms":30000}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "slow", "a");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        var killedRecord = entries.First(entry => entry.Subject.Contains($"{PrintTurn_Words.TURN_ENDED_SUBJECT} {memberId} turn 1 — {TurnOutcomes.TIMEOUT}") && !entry.Subject.Contains(ClosingTurn_Words.REQUEST_ID_SUFFIX));

        Assert.Empty(harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns);
        Assert.DoesNotContain("are not re-run", killedRecord.Body);
        Assert.Contains("retried as before", killedRecord.Body);
    }

    /// <summary>
    /// THE MONEY IS NOT MADE UP. The recorded cost was <c>killed.TotalCostUsd ?? closing.TotalCostUsd</c>,
    /// and a killed turn never has a result document — so it was ALWAYS the closing turn's pennies,
    /// filed as the cost of a turn that in production is 30 minutes of work (probed at $0.0127).
    /// Decision 10 has one reader and one number: unknown is a number, a confidently wrong one is not.
    /// </summary>
    [Fact]
    public async Task TheKilledTurnsRecordedCost_IsUnknown_NotTheClosingTurnsPennies()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"turns":[{"delay_ms":30000},{"result":"WHERE I AM\n\nhalf-way","total_cost_usd":0.0127}]}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Null(Assert.Single(harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns).CostUsd);

        // The closing turn's own spend is still on the record where it belongs — on the closing turn.
        var closingEnded = Assert.Single(ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)), entry => entry.Subject.Contains(ClosingTurn_Words.REQUEST_ID_SUFFIX));
        Assert.Contains("cost_usd: 0.0127", closingEnded.Body);
    }

    // ------------------------------------------------------------------ the bounds around it

    /// <summary>
    /// THE CLI's OWN "that id names nothing" ARRIVES ON THE CLOSING TURN, because the closing turn is
    /// the one that resumes the killed transcript. Every failure branch handed <c>Record_Failure</c> the
    /// WORK turn's result, and only that was inspected — so the recovery written for exactly this signal
    /// never fired and the dead id stayed claimed, to be resumed and refused for ever.
    /// </summary>
    [Fact]
    public async Task WhenTheClosingTurnSaysTheTranscriptIsGone_TheIdIsGivenBack()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        // NOTE THE DEFAULT. Every unnamed field of a scenario turn is inherited from it, so a default
        // carrying `delay_ms` makes turn 2 time out before its exit code is ever read — which is how
        // the probe for this defect passed for the wrong reason (it exercised the timeout branch, not
        // the transcript-gone one) and would have certified a fix that did nothing.
        harness.Write_Scenario("""{"turns":[{"delay_ms":30000},{"exit_code":1,"stderr":"No conversation found with session ID: deadbeef"}],"default":{"result":"NOT EXPECTED\n\nthe dead transcript was resumed again"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromSeconds(30));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var first = PrintRunnerTestHarness.Args(harness.Read_Invocations()[0]);
        var killedId = first[first.IndexOf("--session-id") + 1];
        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);

        Assert.NotEqual(killedId, state.SessionId);
        Assert.False(state.SessionStarted);
    }

    /// <summary>
    /// A SESSION THAT DIES AT THE DEADLINE EVERY TURN MUST SAY SO. A deadline kill spends no attempt by
    /// design, so <c>MAX_ATTEMPTS</c> and its stall alert can never be reached by kills — probed at three
    /// briefs, three kills, three successful closing turns, <c>FailedAttempts</c> 0 and not one alert.
    /// The bound is now on the kills themselves, and it ALERTS without stalling: the closing turns are
    /// working, so the wasteful retry this feature removed must not come back with them.
    /// </summary>
    [Fact]
    public async Task ThreeDeadlineKillsInARow_RaiseAnAlert_WithoutBringingBackTheRetry()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: TIMEOUT_MINUTES);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""
        {"turns":[
          {"delay_ms":30000},{"result":"WHERE I AM\n\none"},
          {"delay_ms":30000},{"result":"WHERE I AM\n\ntwo"},
          {"delay_ms":30000},{"result":"WHERE I AM\n\nthree"}
        ],"default":{"result":"DONE\n\nfourth"}}
        """);
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        for (var brief = 1; brief <= ClosingTurn_Words.KILLS_BEFORE_ALERT; brief++)
        {
            Append_Supervisor(harness, orchId, memberId, $"BRIEF {brief}", "carry on");
            var target = brief;
            Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == target, PrintRunnerTestHarness.GENEROUS), $"brief {brief} never ended");
        }

        await dispatcher.Stop_Async();

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));

        // The retry is still gone: no attempt was spent and each brief was answered exactly once.
        Assert.Equal(0, state.FailedAttempts);
        Assert.Equal(ClosingTurn_Words.KILLS_BEFORE_ALERT, state.ExecutedTurns.Count);
        Assert.Equal(ClosingTurn_Words.KILLS_BEFORE_ALERT, entries.Count(entry => entry.Author == ChannelAuthors.Implementer));

        // ONE alert for the three, not one per kill (decision 14: an owner-facing repeat is a waterfall).
        var alert = Assert.Single(entries, entry => entry.Subject.Contains(PrintTurn_Words.DEADLINE_KILLS_SUBJECT));
        Assert.Contains($"{ClosingTurn_Words.KILLS_BEFORE_ALERT} turns in a row", alert.Subject);
    }

    /// <summary>
    /// THE DRAIN GRACE HAS TO COVER THE PATH IT IS DRAINING. A deadline kill now needs the turn timeout
    /// AND the closing turn behind it; the grace was still the turn timeout plus a minute, i.e. 31
    /// minutes of patience against 35 minutes of need in production.
    /// </summary>
    [Fact]
    public void TheDrainGrace_CoversTheKilledTurnAndItsClosingTurn()
    {
        var margin = TimeSpan.FromMinutes(1);

        // Production: 30 for the work turn, 5 for the closing turn (ClosingTurn_Words.TIMEOUT), 1 spare.
        Assert.Equal(TimeSpan.FromMinutes(36), ClosingTurn_Rule.Resolve_DrainGrace(TimeSpan.FromMinutes(30), margin));

        // A short turn timeout clamps the closing turn to itself, so the grace shrinks with it.
        Assert.Equal(TimeSpan.FromMinutes(3), ClosingTurn_Rule.Resolve_DrainGrace(TimeSpan.FromMinutes(1), margin));
    }

    /// <summary>
    /// AND THE OWNER IS TOLD WHAT THEY ARE WAITING FOR. The shutdown line said "the turn timeout plus a
    /// minute", which was both the wrong number and the wrong reason once a stop could be waiting on a
    /// closing turn.
    /// </summary>
    [Fact]
    public async Task TheShutdownLine_NamesTheClosingTurnItMayBeWaitingFor()
    {
        using var harness = new PrintRunnerTestHarness("implementer", turnTimeoutMinutes: 1.2);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"result":"REPORT\n\ndone","delay_ms":1500}}""");

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add(entry.Message);

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromSeconds(1));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_Invocations().Count >= 1, PrintRunnerTestHarness.GENEROUS), "the turn never started");

        await dispatcher.Stop_Async();

        // 1.2 min for the turn + 1.2 min for the closing turn (clamped to the turn timeout) + 1 min.
        var line = Assert.Single(logged, message => message.Contains("draining", StringComparison.Ordinal));
        Assert.Contains("up to 3.4 min", line);
        Assert.Contains("closing turn", line);
    }
}

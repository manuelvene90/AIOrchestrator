using System.Globalization;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE QUOTA REFUSAL, END TO END. Measured on the VPS 2026-09-08/09: a turn refused with
/// <c>api_error_status: 429</c> and "You've hit your weekly limit · resets 5am (Europe/Berlin)"
/// was retried three times a minute apart and then sat stalled — 167 to 509 minutes of dead
/// orchestration, twice for a whole night. These pin the two halves of the fix: a reset the
/// dispatcher can read is WAITED for, and one it cannot read changes nothing at all.
///
/// <para>
/// THE SENTENCE IS BUILT AGAINST THE CLOCK THE TEST RUNS AT, not frozen at "5am (Europe/Berlin)".
/// A reading is now bounded (<see cref="LimitReset_Parser.MAX_DEFERRAL"/>), so a hard-coded hour
/// would be honoured or refused depending on the time of day the suite happens to run — which is
/// the shape of test that goes red once a night and gets called intermittent. Every refusal here
/// names an hour a fixed distance from now, and the distance is the thing under test.
/// </para>
/// </summary>
public class PrintTurnLimitResetTests
{
    /// <summary>
    /// The measured refusal, with the reset clause aimed a chosen distance from now. UTC and not
    /// Europe/Berlin so the row reads the same on any host; the zone-reading itself is pinned by
    /// <see cref="LimitResetParserTests"/>, which does not need a process to say it.
    /// </summary>
    static string Refusal_Text(TimeSpan fromNow)
    {
        var target = DateTime.UtcNow + fromNow;
        return $"You've hit your weekly limit · resets {target.ToString("HH:mm", CultureInfo.InvariantCulture)} (UTC)";
    }

    static string Resets_Phrase(string refusalText)
    {
        return refusalText[refusalText.IndexOf("resets", StringComparison.Ordinal)..];
    }

    /// <summary>The measured refusal on the first turn, then a normal answer — the fake's own scenario shape.</summary>
    static string Refusal_Scenario(string refusalText)
    {
        return """{"turns":[{"is_error":true,"api_error_status":429,"result":"REFUSAL"}],"default":{"result":"REPORT\n\nback after the reset"}}"""
            .Replace("REFUSAL", refusalText);
    }

    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }

    static string Read_Log(PrintRunnerTestHarness harness, string orchId)
    {
        var file = harness.Paths.Get_OrchestrationLogFile(orchId);
        return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
    }

    /// <summary>
    /// WHY A MULTI-MEMBER WAIT RAN OUT, in the terms that settle it: for every member its executed
    /// turns, its spent attempts and its appointment, and then the orchestration log.
    ///
    /// <para>
    /// EXECUTED TURNS ARE IN HERE BECAUSE LEAVING THEM OUT COST A ROUND (2026-09-09). The first cut
    /// of this message reported the deferred member's appointment and attempts only — and "no
    /// appointment, no attempt spent" reads exactly the same whether the turn never ran at all or
    /// ran and SUCCEEDED. It had succeeded: the fake had crashed on the invocation log two other
    /// members were writing, and the dispatcher's third attempt drew the scenario's default turn.
    /// The log is appended for the same reason — it is the only place the fake's own stderr
    /// survives, and it is what finally named the crash.
    /// </para>
    /// </summary>
    static string Describe_Wait(PrintRunnerTestHarness harness, string orchId, params string[] memberIds)
    {
        var members = memberIds.Select(memberId => Describe_Member(harness, orchId, memberId));

        return $"{string.Join("; ", members)}\nlog:\n{Read_Log(harness, orchId)}";
    }

    static string Describe_Member(PrintRunnerTestHarness harness, string orchId, string memberId)
    {
        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);

        return $"{memberId}: executed {state.ExecutedTurns.Count}, attempts {state.FailedAttempts}, "
            + $"appointment {state.RetryNotBeforeUtc?.ToString("o") ?? "none"}";
    }

    /// <summary>
    /// <see cref="PrintRunnerTestHarness.Drive_Until"/> with the clock pushed forward — the only way
    /// to reach a reset instant hours away without waiting for it. The dispatcher takes the time as an
    /// argument precisely so a test can say when it is.
    /// </summary>
    static bool Drive_Until_At(IPrintTurnDispatcher dispatcher, TimeSpan skew, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            dispatcher.Tick(DateTime.Now + skew);

            if (condition())
                return true;

            Thread.Sleep(100);
        }

        dispatcher.Tick(DateTime.Now + skew);
        return condition();
    }

    /// <summary>Comfortably past any appointment these tests can buy, so the deferred turn runs.</summary>
    static readonly TimeSpan PAST_EVERY_APPOINTMENT = LimitReset_Parser.MAX_DEFERRAL + TimeSpan.FromHours(1);

    [Fact]
    public async Task A429ThatNamesItsReset_WaitsForThatInstant_SpendsNoAttemptAndIgnoresTheBackoff()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var refusal = Refusal_Text(TimeSpan.FromHours(2));
        harness.Write_Scenario(Refusal_Scenario(refusal));
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc != null, PrintRunnerTestHarness.GENEROUS));

        var deferred = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        var retryAt = deferred.RetryNotBeforeUtc!.Value;

        // NOT AN ATTEMPT. Three tries a minute apart cannot outlast a quota window, so spending them
        // on one is spending the session's whole allowance on nothing.
        Assert.Equal(0, deferred.FailedAttempts);
        Assert.Single(harness.Read_Invocations());
        Assert.Equal(DateTimeKind.Utc, retryAt.Kind);
        Assert.True(retryAt > DateTime.UtcNow, $"the retry was scheduled in the past ({retryAt:O})");

        // F1: THE HARD CAP. Whatever the sentence names, an appointment can never buy more than a
        // handful of hours of silence — beyond it the session keeps its attempts and its stall, which
        // new traffic clears.
        Assert.True(
            retryAt <= DateTime.UtcNow + LimitReset_Parser.MAX_DEFERRAL + PrintTurn_Words.LIMIT_RESET_MARGIN,
            $"the retry was scheduled further out than the {LimitReset_Parser.MAX_DEFERRAL.TotalHours:0} h cap ({retryAt:O})");

        // AND NOT THE BACKOFF EITHER: several backoff windows pass and nothing starts.
        for (var pass = 0; pass < 5; pass++)
        {
            Thread.Sleep(120);
            dispatcher.Tick(DateTime.Now);
        }

        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Single(harness.Read_Invocations());
        Assert.Equal(0, harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts);

        // The channel says WHEN it resumes, and from which words it read that.
        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        var alert = Assert.Single(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_LIMITED_SUBJECT));
        Assert.Contains(retryAt.ToString("HH:mm"), alert.Subject);
        Assert.Contains(Resets_Phrase(refusal), alert.Body);
        Assert.Contains($"{orchId}/{memberId}/1", alert.Body);

        // F4: THE DELAY, NOT A BARE CLOCK. "resumes 03:00 UTC" reads as this morning whether it is two
        // hours away or twenty-six, and the subject is what the owner sees on their phone.
        Assert.Contains("resumes in ", alert.Subject);
        Assert.Contains("UTC)", alert.Subject);

        // And so does the log.
        var log = Read_Log(harness, orchId);
        Assert.Contains("was refused for a usage limit", log);
        Assert.Contains($"retry scheduled in ", log);
        Assert.Contains(retryAt.ToString("HH:mm"), log);
        Assert.Contains(Resets_Phrase(refusal), log);

        // Past the reset the SAME request id runs — turn 1, and reported as attempt 1 because none was spent.
        Assert.True(Drive_Until_At(dispatcher, PAST_EVERY_APPOINTMENT, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var done = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Equal(2, harness.Read_Invocations().Count);
        Assert.Null(done.RetryNotBeforeUtc);
        Assert.Equal(0, done.FailedAttempts);
        Assert.Equal(1, Assert.Single(done.ExecutedTurns).TurnNumber);
        Assert.Contains("back after the reset", harness.Read_Channel(orchId, memberId));

        var replayed = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.DoesNotContain(replayed, entry => entry.Subject.Contains(PrintTurn_Words.TURN_STALLED_SUBJECT));
        Assert.Single(replayed, entry => entry.Author == ChannelAuthors.Implementer);
    }

    [Fact]
    public async Task TheScheduledRetry_IsInTheStateFile_SoABridgeRestartKeepsIt()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario(Refusal_Scenario(Refusal_Text(TimeSpan.FromHours(2))));
        var first = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(first, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc != null, PrintRunnerTestHarness.GENEROUS));
        await first.Stop_Async();

        var scheduled = harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc;

        // A SECOND DISPATCHER HAS NO TRACKERS — everything it knows about this session it read from
        // print-session.json. If the wait lived only in memory the restart would run the turn at once
        // and burn another refusal, which is exactly the loop the VPS was in.
        var restarted = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        for (var pass = 0; pass < 5; pass++)
        {
            Thread.Sleep(120);
            restarted.Tick(DateTime.Now);
        }

        Assert.Single(harness.Read_Invocations());
        Assert.Equal(scheduled, harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc);

        Assert.True(Drive_Until_At(restarted, PAST_EVERY_APPOINTMENT, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await restarted.Stop_Async();

        Assert.Equal(2, harness.Read_Invocations().Count);
        Assert.Null(harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc);
    }

    [Fact]
    public async Task A429WhoseResetCannotBeRead_KeepsTodaysThreeAttemptsAndStall_AndSaysWhatItCouldNotRead()
    {
        // DECISION 21: a guard that cannot evaluate its predicate says so and ALLOWS. The refusal is
        // real but names no clock, so nothing about the old path changes — and the log names which
        // predicate failed, because "hook error" is the silence this repo keeps paying for.
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"is_error":true,"api_error_status":429,"result":"You've hit your weekly limit","stderr":"429 rate_limit_error"}}""");
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= PrintTurn_Words.MAX_ATTEMPTS, TimeSpan.FromSeconds(40)));
        await dispatcher.Stop_Async();

        var stalled = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, stalled.FailedAttempts);
        Assert.Null(stalled.RetryNotBeforeUtc);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, harness.Read_Invocations().Count);

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.Single(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_STALLED_SUBJECT));
        Assert.DoesNotContain(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_LIMITED_SUBJECT));

        // ONE LOG LINE, NAMING WHAT IT COULD NOT READ — and nothing anywhere the owner gets texted.
        var log = Read_Log(harness, orchId);
        Assert.Contains("no reset time could be read", log);
        // The log is JSONL and its writer escapes an apostrophe, so the quoted text is matched from
        // the first plain word on — asserting on the apostrophe would pin the encoder, not the line.
        Assert.Contains("hit your weekly limit", log);
        Assert.DoesNotContain("retry scheduled", log);
    }

    /// <summary>
    /// F1, END TO END. A refusal naming an hour further out than the cap is NOT an appointment: it
    /// keeps its three attempts, stalls, and alerts — which new traffic can clear. That is strictly
    /// better than what the unbounded rule did with a boundary the account was standing on, where the
    /// same sentence bought a day of silence per refusal and nothing could wake the session.
    /// </summary>
    [Fact]
    public async Task ARefusalNamingAnHourBeyondTheCap_IsNeverParked_ItKeepsItsAttemptsAndStalls()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario(
            """{"default":{"is_error":true,"api_error_status":429,"result":"REFUSAL"}}"""
                .Replace("REFUSAL", Refusal_Text(LimitReset_Parser.MAX_DEFERRAL + TimeSpan.FromHours(2))));
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= PrintTurn_Words.MAX_ATTEMPTS, TimeSpan.FromSeconds(40)));
        await dispatcher.Stop_Async();

        var stalled = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Null(stalled.RetryNotBeforeUtc);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, stalled.FailedAttempts);

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.Single(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_STALLED_SUBJECT));
        Assert.DoesNotContain(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_LIMITED_SUBJECT));
    }

    /// <summary>
    /// F3, PROBE (b), END TO END: an ordinary repeatable error whose text merely mentions a rate-limit
    /// window. The gate was <c>Contains("limit")</c>, so this was parked WITHOUT an attempt being
    /// spent — and a turn that never spends an attempt can never reach the attempt limit, never
    /// stalls, and never alerts. It is a session quietly out of the world with nothing to end it.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryErrorMentioningARateLimitWindow_SpendsItsAttemptsAndStalls_AndIsNeverParked()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);

        harness.Write_Scenario(
            """{"default":{"is_error":true,"exit_code":1,"api_error_status":500,"result":"Error: upstream 500 while the provider's rate limit window CLAUSE","stderr":"500 internal"}}"""
                .Replace("CLAUSE", Resets_Phrase(Refusal_Text(TimeSpan.FromHours(2)))));

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts >= PrintTurn_Words.MAX_ATTEMPTS, TimeSpan.FromSeconds(40)));
        await dispatcher.Stop_Async();

        var stalled = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Null(stalled.RetryNotBeforeUtc);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, stalled.FailedAttempts);
        Assert.Equal(PrintTurn_Words.MAX_ATTEMPTS, harness.Read_Invocations().Count);

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.Single(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_STALLED_SUBJECT));
        Assert.DoesNotContain(entries, entry => entry.Subject.Contains(PrintTurn_Words.TURN_LIMITED_SUBJECT));
    }

    /// <summary>
    /// F5. The owner writes into a topic whose session is waiting out a limit: the bridge acks the
    /// message and <c>Consider_Session</c> then returns on the appointment before it looks at what is
    /// pending, so nothing else was ever said. This pins the one entry that says why — ONCE, naming
    /// when the session runs and how to override it — and pins that it reaches the OWNER for a solo,
    /// which is a role whose channel the owner reads.
    /// </summary>
    [Fact]
    public async Task NewTrafficDuringADeferral_IsAnsweredOnce_WithAnEntryTheOwnerCanSee()
    {
        using var harness = new PrintRunnerTestHarness("solo");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Solo);
        var ownerChannel = harness.Paths.Get_OwnerChannelFile(orchId);

        // Turn 1 is the boot greeting, turn 2 is the refusal, and nothing after it ever runs here.
        harness.Write_Scenario(
            """{"turns":[{"result":"solo-1 online — Repo\n\nReady."},{"is_error":true,"api_error_status":429,"result":"REFUSAL"}],"default":{"result":"REPORT\n\nlate"}}"""
                .Replace("REFUSAL", Refusal_Text(TimeSpan.FromHours(2))));

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Solo, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));

        Assert.True(ChannelAppender.Append_OwnerEntry(ownerChannel, "please look at the parser", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Solo, orchId, memberId).RetryNotBeforeUtc != null, PrintRunnerTestHarness.GENEROUS));

        // NOTHING IS SAID ABOUT THE TRAFFIC THE REFUSAL HAPPENED ON — it already has the deferral entry.
        for (var pass = 0; pass < 3; pass++)
        {
            Thread.Sleep(60);
            dispatcher.Tick(DateTime.Now);
        }

        Assert.DoesNotContain(
            ChannelEntry_Parser.Parse_All(File.ReadAllText(ownerChannel)),
            entry => entry.Subject.Contains(PrintTurn_Words.NEW_TRAFFIC_LIMITED_SUBJECT));

        // NOW something new lands.
        Assert.True(ChannelAppender.Append_OwnerEntry(ownerChannel, "and also the docs", DateTime.Now));

        Assert.True(PrintRunnerTestHarness.Drive_Until(
            dispatcher,
            () => ChannelEntry_Parser.Parse_All(File.ReadAllText(ownerChannel)).Any(entry => entry.Subject.Contains(PrintTurn_Words.NEW_TRAFFIC_LIMITED_SUBJECT)),
            PrintRunnerTestHarness.GENEROUS));

        // ONCE, not on every one of the forty ticks that follow.
        for (var pass = 0; pass < 10; pass++)
        {
            Thread.Sleep(30);
            dispatcher.Tick(DateTime.Now);
        }

        await dispatcher.Stop_Async();

        var notice = Assert.Single(
            ChannelEntry_Parser.Parse_All(File.ReadAllText(ownerChannel)),
            entry => entry.Subject.Contains(PrintTurn_Words.NEW_TRAFFIC_LIMITED_SUBJECT));

        // OWNER-FACING. An agent-tagged entry never reaches the phone, and this is the one line that
        // turns "my app is dead" into "it is waiting until 03:00 and /resume overrides it".
        Assert.False(AppEntryAudience_Tag.Is_AgentTagged(notice.Subject), $"the notice was agent-tagged and would never reach the owner: '{notice.Subject}'");
        Assert.Contains("runs in ", notice.Subject);
        Assert.Contains("/resume", notice.Body);

        // AND THE TURN NEVER RAN: the appointment still holds, no attempt was spent on the new traffic.
        Assert.Equal(2, harness.Read_Invocations().Count);
        Assert.NotNull(harness.Read_State(SessionRoles.Solo, orchId, memberId).RetryNotBeforeUtc);
        Assert.Equal(0, harness.Read_State(SessionRoles.Solo, orchId, memberId).FailedAttempts);
    }

    /// <summary>The measured refusal, scoped to one member's <c>--name</c> so a second member in the same scenario file can behave differently.</summary>
    static string Refusal_Scenario_ForNamedSession(string orchId, string memberId, string refusalText)
    {
        return ("""{"sessions":{"SESSION_NAME":{"turns":[{"is_error":true,"api_error_status":429,"result":"REFUSAL"}]}},"default":{"result":"REPORT\n\nall good"}}"""
            .Replace("SESSION_NAME", $"{orchId}-{memberId}"))
            .Replace("REFUSAL", refusalText);
    }

    /// <summary>
    /// THE GAP: /resume's own help text is "use when the usage limit resets", but a deferred session's
    /// appointment used to hold regardless of the fresh traffic /resume appends, because
    /// Consider_Session checks RetryNotBeforeUtc before it ever looks at what is pending. This pins the
    /// fix — IPrintTurnDispatcher.Clear_LimitDeferrals — end to end: still waiting on the real clock,
    /// then cleared, then the very next tick (no time skew needed — the whole point is not waiting for
    /// the real reset) runs the turn without spending an attempt.
    /// </summary>
    [Fact]
    public async Task ResumeClearsADeferredSession_SoTheVeryNextTickRunsTheTurn_WithNoWaitForTheRealReset()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario(Refusal_Scenario(Refusal_Text(TimeSpan.FromHours(2))));
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc != null, PrintRunnerTestHarness.GENEROUS));

        var retryAt = harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc!.Value;

        // STILL WAITING on the real clock — nothing but /resume changes that.
        for (var pass = 0; pass < 3; pass++)
        {
            Thread.Sleep(120);
            dispatcher.Tick(DateTime.Now);
        }

        Assert.Equal(0, dispatcher.InFlightCount);
        Assert.Single(harness.Read_Invocations());
        Assert.NotNull(harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc);

        // /resume's override — and it REPORTS what it broke (F7), which is the half the owner's reply
        // used to be missing entirely.
        Assert.Equal(1, dispatcher.Clear_LimitDeferrals());
        Assert.Null(harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc);

        // The very next tick runs it — real clock, no skew.
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var done = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        Assert.Equal(2, harness.Read_Invocations().Count);
        Assert.Equal(0, done.FailedAttempts);
        Assert.Null(done.RetryNotBeforeUtc);

        var log = Read_Log(harness, orchId);
        Assert.Contains(memberId, log);
        Assert.Contains("was waiting on a usage limit", log);
        Assert.Contains(retryAt.ToString("HH:mm"), log);
        Assert.Contains("resume cleared it", log);

        // A SECOND /resume with nothing left to clear reports nothing to clear.
        Assert.Equal(0, dispatcher.Clear_LimitDeferrals());
    }

    /// <summary>
    /// KEEP IT HONEST: a session /resume finds with no appointment is not rewritten and gets no line —
    /// inventing a clear that did not happen is the same lie /resume exists to end, in the other
    /// direction.
    /// </summary>
    [Fact]
    public async Task ResumeClear_LeavesANonDeferredSessions_StateFileUntouched_AndLogsNothingForIt()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, deferredMember) = harness.Register_Member(MemberKinds.Implementer);
        var (_, normalMember) = harness.Register_Member(MemberKinds.Implementer, orchId);
        harness.Write_Scenario(Refusal_Scenario_ForNamedSession(orchId, deferredMember, Refusal_Text(TimeSpan.FromHours(2))));
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, deferredMember, "BRIEF", "start on the parser");
        Append_Supervisor(harness, orchId, normalMember, "BRIEF", "start on the docs");

        Assert.True(PrintRunnerTestHarness.Drive_Until(
            dispatcher,
            () => harness.Read_State(SessionRoles.Implementer, orchId, deferredMember).RetryNotBeforeUtc != null
                && harness.Read_State(SessionRoles.Implementer, orchId, normalMember).ExecutedTurns.Count == 1,
            PrintRunnerTestHarness.GENEROUS),
            Describe_Wait(harness, orchId, deferredMember, normalMember));

        var normalStateFile = PrintSessionState_Store.Get_StateFile(harness.Paths, SessionRoles.Implementer, orchId, normalMember);
        var normalBefore = File.ReadAllText(normalStateFile);
        var retryAt = harness.Read_State(SessionRoles.Implementer, orchId, deferredMember).RetryNotBeforeUtc!.Value;

        Assert.Equal(1, dispatcher.Clear_LimitDeferrals());

        // UNTOUCHED, BYTE FOR BYTE — not just "same values", the file was never opened for writing.
        Assert.Equal(normalBefore, File.ReadAllText(normalStateFile));
        Assert.Null(harness.Read_State(SessionRoles.Implementer, orchId, deferredMember).RetryNotBeforeUtc);

        var log = Read_Log(harness, orchId);
        Assert.Contains(deferredMember, log);
        Assert.Contains(retryAt.ToString("HH:mm"), log);
        Assert.Equal(1, log.Split("was waiting on a usage limit").Length - 1);

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// F7. A session whose state file cannot be WRITTEN must cost one session, not the whole command.
    /// Only the READ used to be inside the try, so an IO failure in the write escaped into
    /// <c>Resume_AllSessions_Async</c> — which calls this BEFORE it appends anything — and /resume
    /// aborted before waking a single session, was logged as a Telegram backoff, and had the update
    /// redelivered and retried for ever. The folder is made read-only rather than the file, because
    /// the write is atomic: it fills a sibling temp file and renames it over the target.
    /// </summary>
    [RequiresUnwritableFolderFact]
    public async Task AStateFileThatCannotBeWritten_CostsOneSession_NotTheWholeResume()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, unwritableMember) = harness.Register_Member(MemberKinds.Implementer);
        var (_, healthyMember) = harness.Register_Member(MemberKinds.Implementer, orchId);

        // EVERY turn is refused, so both members end up with an appointment on file.
        harness.Write_Scenario(
            """{"default":{"is_error":true,"api_error_status":429,"result":"REFUSAL"}}"""
                .Replace("REFUSAL", Refusal_Text(TimeSpan.FromHours(2))));

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, unwritableMember, "BRIEF", "start on the parser");
        Append_Supervisor(harness, orchId, healthyMember, "BRIEF", "start on the docs");

        Assert.True(PrintRunnerTestHarness.Drive_Until(
            dispatcher,
            () => harness.Read_State(SessionRoles.Implementer, orchId, unwritableMember).RetryNotBeforeUtc != null
                && harness.Read_State(SessionRoles.Implementer, orchId, healthyMember).RetryNotBeforeUtc != null,
            PrintRunnerTestHarness.GENEROUS));

        await dispatcher.Stop_Async();

        using (UnwritableFolder.Take_WritePermission(harness.Paths.Get_ImplementerFolder(orchId, unwritableMember)))
        {
            // It must NOT throw, and the healthy session must still be freed.
            Assert.Equal(1, dispatcher.Clear_LimitDeferrals());
        }

        Assert.Null(harness.Read_State(SessionRoles.Implementer, orchId, healthyMember).RetryNotBeforeUtc);
        Assert.NotNull(harness.Read_State(SessionRoles.Implementer, orchId, unwritableMember).RetryNotBeforeUtc);

        // NAMED, not swallowed: the owner's /resume did not free this one and the log says which.
        Assert.Contains(unwritableMember, Read_Log(harness, orchId));
    }

    /// <summary>
    /// F6, THE ONE THAT ERASES WORK. <c>/resume</c> reads a state file and writes back a value derived
    /// from that SNAPSHOT. The appointment used to survive the whole turn it had scheduled — up to the
    /// full thirty-minute timeout — so a <c>/resume</c> landing in that window read a copy of the state
    /// from before the turn's own record and wrote it back over it: <c>executed_turns 1 → 0</c>,
    /// <c>next_turn 3 → 2</c>, and the same request id ran twice. A decision-8 violation caused by the
    /// owner's own recovery command.
    ///
    /// <para>
    /// THE ASSERTION IS THE CLOSED WINDOW, NOT THE RACE. Reproducing the clobber means winning a
    /// microsecond interleave, which is a test that fails in both directions; what is deterministic —
    /// and what actually closes it — is that a turn IN FLIGHT has no appointment left on file for a
    /// /resume to derive a stale copy from. Nothing here calls <c>Clear_LimitDeferrals</c> before that
    /// assertion, deliberately: a /resume of its own would clear the appointment too, and an assertion
    /// with two routes to the state it checks pins neither (CLAUDE.md decision 20). The /resume comes
    /// AFTER, to pin the second half — a session with a turn in flight is skipped outright.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ADeferredTurnDropsItsAppointmentWhenItStarts_SoAResumeMidTurnHasNothingToRollBack()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);

        harness.Write_Scenario(
            """{"turns":[{"is_error":true,"api_error_status":429,"result":"REFUSAL"}],"default":{"result":"REPORT\n\nback after the reset","delay_ms":2500}}"""
                .Replace("REFUSAL", Refusal_Text(TimeSpan.FromHours(2))));

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "start on the parser");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc != null, PrintRunnerTestHarness.GENEROUS));

        // The clock is pushed past the appointment so the deferred turn starts. WHILE IT RUNS the state
        // file must already be free of the appointment it was waiting on.
        Assert.True(
            Drive_Until_At(
                dispatcher,
                PAST_EVERY_APPOINTMENT,
                () => dispatcher.Is_TurnInFlight(orchId, memberId) && harness.Read_State(SessionRoles.Implementer, orchId, memberId).RetryNotBeforeUtc == null,
                PrintRunnerTestHarness.GENEROUS),
            "the appointment was still on file while the very turn it scheduled was running — a /resume in that window rewrites the turn's own record from a stale snapshot");

        // AND THE SECOND HALF: a /resume arriving right now finds nothing to clear and touches nothing,
        // because a session with a turn in flight is skipped whatever its file says.
        Assert.True(dispatcher.Is_TurnInFlight(orchId, memberId), "the turn ended before the /resume could be aimed at it");
        Assert.Equal(0, dispatcher.Clear_LimitDeferrals());

        Assert.True(Drive_Until_At(dispatcher, PAST_EVERY_APPOINTMENT, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS), "the deferred turn never completed");

        // Settle: several more ticks, so a rolled-back turn number would show up as a second run of the
        // same request id rather than as a state file that merely looks odd.
        Assert.False(Drive_Until_At(dispatcher, PAST_EVERY_APPOINTMENT, () => harness.Read_Invocations().Count > 2, TimeSpan.FromSeconds(3)));
        await dispatcher.Stop_Async();

        var done = harness.Read_State(SessionRoles.Implementer, orchId, memberId);

        Assert.Equal(2, harness.Read_Invocations().Count);
        Assert.Single(done.ExecutedTurns);
        Assert.Equal(2, done.NextTurnNumber);
        Assert.Equal($"{orchId}/{memberId}/1", done.ExecutedTurns[0].RequestId);
        Assert.Null(done.RetryNotBeforeUtc);
    }
}

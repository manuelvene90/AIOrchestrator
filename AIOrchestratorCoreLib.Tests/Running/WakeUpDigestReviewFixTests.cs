using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE FOUR THINGS THE 2026-09-09 REVIEW OF <c>stage/4h</c> PROVED BROKEN, each pinned where it broke
/// — at the dispatcher's call site, on an injected clock, through the real policy.
///
/// <para>
/// <see cref="WakeUpPolicyTests"/> is pure and <see cref="MemberTrafficRidesOneDigestedTurnTests"/>
/// covers one digest cycle. Neither could see any of these: the once-per-process bug needs a SECOND
/// cycle, the restart bug needs a second dispatcher over one state, and the first-entry bug needs a
/// member that appears after the supervisor has already been handed something. That is the shape of
/// the gap the review found — 20 of 22 cases still passed with the gate bypassed — so these are
/// written to redden when the WIRING is wrong and not only when the policy is.
/// </para>
/// <para>
/// NOTHING HERE SLEEPS. <c>Tick</c> takes the instant it is deciding at, so five minutes of digest is
/// a later argument (<c>PrintRunnerTestHarness.Drive_Until_At</c>); the only real waiting is for a
/// turn already admitted to appear on its background task.
/// </para>
/// </summary>
public class WakeUpDigestReviewFixTests
{
    const double DIGEST_MINUTES = 5;
    const string ORCH = "repo-1";

    static readonly DateTime T0 = new(2026, 9, 9, 10, 0, 0);

    /// <summary>
    /// THE DIGEST FIRES ON EVERY CYCLE, NOT ONCE PER APP LIFE — the review's first HIGH.
    ///
    /// <para>
    /// <c>DigestHeldSince</c> was cleared only on a tick whose pending set was non-empty and
    /// non-digestable, and the tick after a completed turn has an EMPTY set and returns above that
    /// line. So the <c>??=</c> kept the FIRST report's instant for the life of the process, every
    /// later report read as already past the window, and in the steady state the 247-wake-up saving
    /// was not delivered at all — silently, in the direction that looks like it is working.
    /// </para>
    /// <para>
    /// TWO CYCLES IS THE WHOLE TEST. The first one is what the stage's own tests already exercised;
    /// the second is the one nothing drove. Probed 2026-09-10 against <c>601c178</c>: the second
    /// report started a turn on the tick it landed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ASecondReportIsHeldToo_TheDigestIsNotSpentOncePerProcess()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        // CYCLE ONE — the one the stage already had. Held for the window, then delivered.
        var firstAt = T0.AddMinutes(2);

        Report(harness, imp, "REPORT — parser ported");

        Assert.False(Ran_AnotherTurn(harness, dispatcher, firstAt, 2), "the first report was not digested at all.");

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, firstAt.AddMinutes(DIGEST_MINUTES), () => Turns(harness) == 3, PrintRunnerTestHarness.GENEROUS),
            "the first digest window elapsed and the report was never delivered.");

        // CYCLE TWO — the case nothing drove. The hold stamp is spent, so this report must start its
        // OWN window rather than inherit a window that ran out three minutes ago.
        var secondAt = firstAt.AddMinutes(DIGEST_MINUTES + 1);

        Report(harness, imp, "REPORT — the splitter too");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, secondAt, 3),
            "the SECOND report woke the supervisor on the tick it landed: the digest fired once and never again.");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, secondAt.AddMinutes(DIGEST_MINUTES - 1), 3),
            "the second report was delivered a minute before its own window was out.");

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, secondAt.AddMinutes(DIGEST_MINUTES), () => Turns(harness) == 4, PrintRunnerTestHarness.GENEROUS),
            "the second digest window elapsed and the report was never delivered.");

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// A RESTART COSTS ONE EARLY DELIVERY, NOT A LATE ONE — the review's second HIGH, and the promise
    /// the docstring was already making.
    ///
    /// <para>
    /// The hold stamp lives in the dispatcher's tracker, which is per-process. The first tick after a
    /// restart used to stamp <c>nowLocal</c> on traffic that had ALREADY been waiting, so a restart
    /// restarted the window: probed by the review, a report filed at T+1 on a five-minute window went
    /// out at T+11, and 21 daemon restarts were measured in 44 hours of VPS uptime. Unbounded if
    /// restarts repeat, which is the direction that cannot be allowed.
    /// </para>
    /// <para>
    /// So a null stamp with digestable traffic pending DELIVERS. Probed 2026-09-10 against
    /// <c>601c178</c>: the second dispatcher held the report for a fresh five minutes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AfterARestart_TrafficThatWasAlreadyWaiting_IsDeliveredAtOnce()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var first = harness.Create_Dispatcher();

        Boot(harness, first);
        harness.Spend_FirstContact(first, ORCH, T0.AddMinutes(1), 1, imp);

        // THE CONTROL HALF: the same report, under the dispatcher that was watching when it arrived,
        // IS held. Without this the case would also pass with the digest simply switched off.
        Report(harness, imp, "REPORT — parser ported");

        Assert.False(Ran_AnotherTurn(harness, first, T0.AddMinutes(2), 2), "the report was not digested at all, so the restart half below proves nothing.");

        await first.Stop_Async();

        // THE RESTART. A new dispatcher over the same state file: it has no record of a hold, the
        // traffic has already waited an unknown time, and the honest answer is to deliver it.
        var second = harness.Create_Dispatcher();

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(second, T0.AddMinutes(3), () => Turns(harness) == 3, PrintRunnerTestHarness.GENEROUS),
            "after a restart the report already waiting was held for a FRESH window instead of being delivered.");

        await second.Stop_Async();
    }

    /// <summary>
    /// AND NOT ON THE SECOND TICK EITHER — the hole in the first attempt at this fix, kept as a case
    /// because it is invisible at a single injected instant.
    ///
    /// <para>
    /// An abandoned version of the restart rule keyed the exemption on "this dispatcher has CONSIDERED
    /// this session", set on the first tick that reached the stamping line. With a coalesce window
    /// configured — three seconds in production — that first tick does not start a turn, and the
    /// SECOND tick then found the flag already set and stamped a fresh window on the very traffic the
    /// rule existed to release: the restart bug wearing a three-second delay. The exemption is
    /// therefore keyed on a turn of this session's having COMPLETED under this dispatcher
    /// (<c>SessionTracker.HasDeliveredTraffic</c>), which no tick can set on its own — and which, for
    /// one commit, was keyed on a turn having merely STARTED, until a failed turn showed why that is
    /// the same bug wearing a failure (<see cref="WakeUpDigestSecondReviewTests"/>).
    /// </para>
    /// <para>
    /// THE TICKS ARE HAND-DRIVEN because a coalesce window needs two distinct instants to clear, and
    /// <c>Drive_Until_At</c> holds one. Probed 2026-09-10 against the abandoned flag: no turn ran.
    /// </para>
    /// <para>
    /// AND THAT IS ALSO WHY ITS CONTROL HALF HAD TO BE REWRITTEN ON 2026-09-10. It asserted "no turn
    /// ran at T+2" through <c>Ran_AnotherTurn</c>, which drives ONE frozen instant — and with a
    /// three-second coalesce window a frozen instant can never clear it, so no turn could ever run at
    /// that assertion whatever the digest did. Measured with the policy stubbed to release everything:
    /// the case passed. The control now ticks once to record the pending set and asserts at a second,
    /// later instant, where only the digest can be holding the report.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AfterARestart_TheSecondTickDoesNotStampAFreshWindowEither()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", coalesceSeconds: 3, memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var first = harness.Create_Dispatcher();

        // The boot turn, then the spoke's first-contact exemption: each needs one tick to record the
        // pending set and a later one to find it unchanged.
        first.Tick(T0);

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(first, T0.AddSeconds(10), () => Turns(harness) == 1, PrintRunnerTestHarness.GENEROUS),
            "the supervisor never took its boot turn.");

        Report(harness, imp, $"{imp} online");
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "get started", DateTime.Now));

        var opened = T0.AddMinutes(1);

        first.Tick(opened);

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(first, opened.AddSeconds(10), () => Turns(harness) == 2, PrintRunnerTestHarness.GENEROUS),
            "the owner's message did not start a turn, so the spoke's first entry was never handed over.");

        Report(harness, imp, "REPORT — parser ported");

        // TWO INSTANTS: the first records the pending set, the second is past the coalesce window, so
        // what holds the report at the assertion is the digest and nothing else.
        first.Tick(T0.AddMinutes(2));

        Assert.False(
            Ran_AnotherTurn(harness, first, T0.AddMinutes(2).AddSeconds(10), 2),
            "the report was not held at all, so the restart half below proves nothing.");

        await first.Stop_Async();

        // THE RESTART, ON TWO TICKS. The first clears nothing — the coalesce window has not run out —
        // and the second must still find no hold on record.
        var second = harness.Create_Dispatcher();
        var restartedAt = T0.AddMinutes(3);

        second.Tick(restartedAt);

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(second, restartedAt.AddSeconds(10), () => Turns(harness) == 3, PrintRunnerTestHarness.GENEROUS),
            "the tick after the restart's first one stamped a fresh window on traffic that had already waited.");

        await second.Stop_Async();
    }

    /// <summary>
    /// A NEW MEMBER'S GREETING IS NOT HELD — the review's MEDIUM, and it costs a whole window of dead
    /// time per <c>add-implementer</c>.
    ///
    /// <para>
    /// The greeting is the one entry <c>Read_Sources</c>' empty-cursor rule can ever see (its comment
    /// says so), and it is the boot announcement of a session that exists to be briefed. Holding it
    /// bought a wake-up the supervisor is about to spend anyway — it asked for this member — at the
    /// price of the member doing nothing for up to five minutes.
    /// </para>
    /// <para>
    /// ASKED OF THE CURSOR, never of the entry's <c>[n]</c> (CLAUDE.md decision 12). Probed 2026-09-10
    /// against <c>601c178</c>: the greeting was digested.
    /// </para>
    /// <para>
    /// AND IT CARRIES ITS OWN CONTROL SINCE 2026-09-10. Asserting only that a turn RAN is satisfied by
    /// a digest that is off, or stubbed, or never consulted — measured, with the policy stubbed to
    /// release everything this case passed — so the second half files an ordinary report from the same
    /// member and requires it to be held. <see cref="AndItsSecondEntry_IsHeldLikeAnyOtherReport"/>
    /// states the same thing as its own subject; here it is what makes this case able to fail.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AFreshMembersGreeting_WakesTheSupervisorAtOnce()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);

        // The member is added AFTER the supervisor is running, which is what `add-implementer` does.
        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;

        Report(harness, imp, $"{imp} online");

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, T0.AddMinutes(1), () => Turns(harness) == 2, PrintRunnerTestHarness.GENEROUS),
            "a new member's greeting was digested, so add-implementer cost a window of dead time before it could be briefed.");

        Report(harness, imp, "REPORT — the parser is ported");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(2), 2),
            "the digest was holding nothing for this session, so the greeting above was released by no rule in particular.");

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// AND ONLY THE FIRST ONE. The exemption is "this channel has never handed anything over", not
    /// "this channel is young": once the greeting has been delivered the member's next entry is an
    /// ordinary report and is held like any other, or every member would simply be exempt.
    /// </summary>
    [Fact]
    public async Task AndItsSecondEntry_IsHeldLikeAnyOtherReport()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        Report(harness, imp, "REPORT — parser ported");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(2), 2),
            "the member's second entry was exempt too, which makes the digest exempt everybody.");

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// THE REVIEW'S UNPROVEN SUSPICION, PROBED: a report held for the digest when its member is CLOSED
    /// inside the window.
    ///
    /// <para>
    /// A closed member is not a source (<c>TurnSources_Resolver</c> says so in as many words), so the
    /// spoke stops being read and the held entry never reaches the supervisor. This case records the
    /// behaviour as MEASURED rather than asserting the fix: the loss is not the digest's — the same
    /// entry is lost with the digest off if the close lands inside the coalesce window, and the cursor
    /// is deliberately kept so nothing is re-delivered either — but the digest widens the exposure
    /// from three seconds to five minutes, on the single most valuable entry a member ever writes
    /// ("done, pushed, here is the diff").
    /// </para>
    /// <para>
    /// WHAT IS OWED IS A LINE, NOT A DRAIN — reviewed 2026-09-10. Re-running the spoke's pending
    /// entries through a supervisor whose turn asked for the close is a second turn nobody ordered;
    /// what the close path owes is to SAY what it is dropping, in the log, naming the entries
    /// (decision 21, and decision 15 keeps it off the phone: the owner cannot act on it). That line
    /// belongs in <c>BridgeEngineModel.Execute_CloseImplementer</c>, which is outside this stage's
    /// file set, so it is reported with the exact change rather than taken. Until it lands, this case
    /// is the record that the loss is known and measured.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AHeldReport_IsNotDeliveredOnceItsMemberIsClosed()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        Report(harness, imp, "REPORT — the last thing I did");

        Assert.False(Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(2), 2), "the report was not digested at all.");

        harness.Store.Close_Member(ORCH, imp);

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(30), 2),
            "a closed member's spoke is being read again — the source screen changed, and this case now measures something else.");

        await dispatcher.Stop_Async();

        Assert.DoesNotContain("REPORT — the last thing I did", string.Join("\n", Traffic_Prompts(harness)));
    }

    // ----- helpers -----

    /// <summary>The supervisor's greeting: it runs with nothing pending and is never held, so it is out of the counts.</summary>
    static void Boot(PrintRunnerTestHarness harness, IPrintTurnDispatcher dispatcher)
    {
        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, T0, () => Turns(harness) == 1, PrintRunnerTestHarness.GENEROUS),
            "the supervisor never took its boot turn, so nothing below is measuring the digest.");
    }

    static void Report(PrintRunnerTestHarness harness, string memberId, string subject)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(
            harness.Paths.Get_ImplementerChannelFile(ORCH, memberId), ChannelAuthors.Implementer, subject, "Evidence in the body.", DateTime.Now));
    }

    static int Turns(PrintRunnerTestHarness harness)
    {
        return harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count;
    }

    /// <summary>
    /// Whether a turn beyond <paramref name="turnsSoFar"/> started at <paramref name="nowLocal"/>. A
    /// turn IN FLIGHT counts, not only a finished one: a negative assertion that read the executed list
    /// alone would pass while a turn was busy being wrong.
    /// </summary>
    static bool Ran_AnotherTurn(PrintRunnerTestHarness harness, IPrintTurnDispatcher dispatcher, DateTime nowLocal, int turnsSoFar)
    {
        return PrintRunnerTestHarness.Drive_Until_At(
            dispatcher,
            nowLocal,
            () => Turns(harness) > turnsSoFar || dispatcher.Is_TurnInFlight(ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID),
            TimeSpan.FromSeconds(1));
    }

    /// <summary>The prompts that carried pending traffic — stdin for print, a stream message for stream.</summary>
    static IReadOnlyList<string> Traffic_Prompts(PrintRunnerTestHarness harness)
    {
        return
        [
            .. harness.Read_Invocations()
                .Where(line => line["prompt_source"]?.GetValue<string>() == "stdin" || line["line_kind"]?.GetValue<string>() == "stream-message")
                .Select(line => line["prompt"]?.GetValue<string>() ?? string.Empty)
                .Where(prompt => prompt.StartsWith("[bridge turn ", StringComparison.Ordinal))
        ];
    }
}

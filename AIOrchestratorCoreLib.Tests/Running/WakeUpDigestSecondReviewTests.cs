using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// WHAT THE SECOND ADVERSARIAL REVIEW OF <c>stage/4h</c> FOUND, on 2026-09-10 — every case here was
/// RED against <c>732a433</c>, the commit that closed the first review.
///
/// <para>
/// The first round's cases (<see cref="WakeUpDigestReviewFixTests"/>) drive SUCCESSFUL turns only, and
/// that is the whole of the gap: the regression lived on the FAILURE paths, where a turn starts, does
/// not consume its pending set, and the hold was spent on the way in anyway.
/// </para>
/// <para>
/// THE CLOCK IS INJECTED, AND ANCHORED TO REAL TIME HERE rather than to a fixed date. The retry gate
/// (<c>Consider_Session</c>, <c>nowLocal - tracker.LastFailureAt &lt; _retryBackoff</c>) compares the
/// INJECTED instant against a failure stamped with <c>DateTime.Now</c>, so a fixed date in the past
/// makes that difference negative and no retry ever runs — a case about a failed turn would then be
/// measuring the anchor and not the digest.
/// </para>
/// </summary>
public class WakeUpDigestSecondReviewTests
{
    const double DIGEST_MINUTES = 5;
    const string ORCH = "repo-1";

    /// <summary>See the class docstring: anchored to now, because a failure stamp is real-clock.</summary>
    static readonly DateTime T0 = DateTime.Now;

    /// <summary>
    /// A FAILED TURN DOES NOT BUY THE HELD REPORT ANOTHER WINDOW — the regression <c>732a433</c>
    /// introduced, and the reason this file exists.
    ///
    /// <para>
    /// That commit cleared <c>DigestHeldSince</c> in <c>Start_Turn</c>, before the turn was admitted
    /// or executed. A turn that starts and does NOT consume its entries — an error exit, a channel
    /// still locked when the reply is appended, a failure outside the process, the request-id
    /// idempotency skip — leaves them pending, and the next tick then found no hold on record and
    /// stamped a FRESH window on traffic that had already waited its full one. At
    /// <c>MAX_ATTEMPTS</c> failures that is four windows instead of one, and the stall entry that is
    /// the only signal anything is wrong is late by the same amount.
    /// </para>
    /// <para>
    /// SO THE HOLD IS SPENT WHERE THE ENTRIES ARE, on the success path beside the cursor advance. A
    /// failed turn leaves the stamp alone, the retry inherits it, and the report — already past its
    /// window — goes at once. Probed 2026-09-10: RED against <c>732a433</c>, green against
    /// <c>601c178</c>, which is what makes it a regression and not a gap.
    /// </para>
    /// <para>
    /// ONE DOOR PROVES THE CLASS. The clear now lives only where <c>Advance_Cursors</c> is called, so
    /// there is no longer a path on which a turn spends the hold without consuming the entries; this
    /// case drives the cheapest of those doors (an error exit) rather than four copies of itself.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AFailedTurn_DoesNotBuyTheHeldReportAnotherWindow()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);

        // The boot turn and the first-contact turn succeed; the THIRD invocation — the one the digest
        // releases — errors; everything after it succeeds.
        harness.Write_Scenario("""{"turns":[{"result":"noted"},{"result":"noted"},{"is_error":true,"api_error_status":529,"stderr":"overloaded"}],"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        var filedAt = T0.AddMinutes(2);

        Report(harness, imp, "REPORT — done, pushed, diff in the body");

        Assert.False(Ran_AnotherTurn(harness, dispatcher, filedAt, 2), "the report was not held at all, so nothing below is measuring the digest.");

        // THE WINDOW RUNS OUT and the turn is released — and it fails, so the report is still pending.
        var releasedAt = filedAt.AddMinutes(DIGEST_MINUTES);

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, releasedAt, () => Failed_Attempts(harness) >= 1, PrintRunnerTestHarness.GENEROUS),
            "the digest window elapsed and no turn was released at all.");

        // THE FINDING. The retry must inherit the hold the failed turn did not spend.
        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, releasedAt.AddSeconds(30), () => Turns(harness) == 3, PrintRunnerTestHarness.GENEROUS),
            "the failed turn spent the report's hold: the retry started a FRESH digest window on traffic that had already waited one, so the report — and the stall entry behind it — is a whole window late per failure.");

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// FIVE CYCLES, WITH AN OWNER MESSAGE AND A RESTART INTERLEAVED — the re-review's own regression
    /// case, kept because every single-cycle case in this suite passed while the digest was firing
    /// once per process. What it pins is that each report waits ITS OWN window and no other: neither a
    /// spent hold nor a fresh one.
    /// </summary>
    [Fact]
    public async Task FiveCycles_WithAnOwnerMessageAndARestart_EachReportWaitsItsOwnWindow()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        // CYCLE 1 — held, then delivered on its own window.
        Report(harness, imp, "REPORT — one");
        Assert.False(Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(2), 2), "cycle 1 was not held.");
        Assert.True(Ran_Until(dispatcher, T0.AddMinutes(7), () => Turns(harness) == 3), "cycle 1 never arrived.");

        // CYCLE 2 — the owner writes, so the report rides that turn and never sees a window.
        Report(harness, imp, "REPORT — two");
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "how is it going", DateTime.Now));
        Assert.True(Ran_Until(dispatcher, T0.AddMinutes(9), () => Turns(harness) == 4), "the owner's message did not run at once.");

        // CYCLE 3 — a fresh window after a turn released by something else.
        Report(harness, imp, "REPORT — three");
        Assert.False(Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(10), 4), "cycle 3 was not held: the hold was inherited from the owner's turn.");
        Assert.True(Ran_Until(dispatcher, T0.AddMinutes(15), () => Turns(harness) == 5), "cycle 3 never arrived.");

        // CYCLE 4 — a report already waiting when the app restarts is delivered at once, not held again.
        Report(harness, imp, "REPORT — four");
        Assert.False(Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(16), 5), "cycle 4 was not held before the restart.");

        await dispatcher.Stop_Async();

        var restarted = harness.Create_Dispatcher();

        Assert.True(Ran_Until(restarted, T0.AddMinutes(17), () => Turns(harness) == 6), "the restart held traffic that had already waited.");

        // CYCLE 5 — and the restarted dispatcher, having now delivered, holds the next one properly.
        Report(harness, imp, "REPORT — five");
        Assert.False(Ran_AnotherTurn(harness, restarted, T0.AddMinutes(18), 6), "cycle 5 was not held by the restarted dispatcher.");
        Assert.True(Ran_Until(restarted, T0.AddMinutes(23), () => Turns(harness) == 7), "cycle 5 never arrived.");

        await restarted.Stop_Async();
    }

    /// <summary>
    /// A LONG-LIVED SPOKE DOES NOT BECOME "FIRST CONTACT" AGAIN AFTER COMPACTION — decision 13's
    /// shape, found by the re-review on 2026-09-10.
    ///
    /// <para>
    /// The first-contact exemption was read from <c>cursor.Delivered.Count == 0</c>, and that set is
    /// PRUNED at every advance to what is still in the live file
    /// (<c>TurnCursor_Factory.CreateFrom_Delivered</c>) — so on a turn where a compacted source had
    /// nothing pending it empties, and a member of many hours' standing reads as one that has just
    /// said hello. Its reports were then exempt from the digest for ever, which is the saving quietly
    /// switching itself off. <see cref="TurnCursorTests"/> holds the pure proof that the prune
    /// produces exactly this state; here it is written into the state file as the prune leaves it.
    /// </para>
    /// <para>
    /// <c>HighWaterIndex</c> is the half that cannot come back: it is only ever raised
    /// (<c>Math.Max</c>), it survives the prune, and zero is not an index any channel hands out.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AfterCompaction_ALongLivedSpokeIsNotOnFirstContactAgain()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        // The greeting at [1] was delivered and has since been archived; the live file now holds the
        // report at [2] and the delivered set is empty.
        Compacted_Spoke(harness, imp, highWaterIndex: 1, firstLiveIndex: 2, subject: "REPORT — the parser, hours later");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(2), 2),
            "a spoke whose delivered set was emptied by compaction was treated as first contact, so a long-lived member's every report is exempt from the digest.");

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// AND THE ARCHIVE-GAP WARNING IS NOT SILENCED BY THE SAME PRUNE. It returned early on
    /// <c>cursor.Delivered.Count == 0</c> too, so the one line that makes an archived-but-never-
    /// delivered hole audible went missing on exactly the channels compaction had touched — the case
    /// it exists for. Decision 21: a guard whose predicate is a live read must not report history.
    /// </summary>
    [Fact]
    public async Task AfterCompaction_TheArchiveGapWarningStillFires()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        List<string> logged = [];
        void Collect(IOrchestrationLogEntry entry) => logged.Add(entry.Message);
        harness.Log.EntryLogged += Collect;

        try
        {
            // Delivered up to [1], and the live file now starts at [9]: entries [2]–[8] left without
            // ever starting a turn.
            Compacted_Spoke(harness, imp, highWaterIndex: 1, firstLiveIndex: 9, subject: "REPORT — the one after the hole");

            Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(2), 2);

            Assert.Contains(logged, line => line.Contains("were archived without starting a turn"));
        }
        finally
        {
            harness.Log.EntryLogged -= Collect;
            await dispatcher.Stop_Async();
        }
    }

    /// <summary>
    /// EVERY REFUSAL THE CONFIG READER PRODUCED REACHES A READER — the re-review's third finding, and
    /// the one that was pure silence (decision 21).
    ///
    /// <para>
    /// <c>IRunnerConfigs.Rejections</c> was written by the parser and read by nothing outside the
    /// tests: <c>grep -rn Rejections</c> found the model, the factory, the interface and five test
    /// files, and the interface's own docstring claimed "one line each, logged once by the launcher"
    /// while no launcher did. So "refused with a line the operator reads" named a line nobody could
    /// read — a <c>bg</c> role silently demoted to a terminal, a memory ceiling silently replaced, a
    /// digest window silently halved, with no way to tell any of them from a setting that had been
    /// applied.
    /// </para>
    /// <para>
    /// SAID ONCE PER DISTINCT LINE, not once per tick: the dispatcher re-reads config.json on every
    /// tick, and a refusal repeated every two seconds would bury the log it is written into (decision
    /// 14). Per-line rather than once per process, so a config edited while the app runs still gets
    /// its new refusal reported.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AConfigRefusal_IsLoggedOnce_AndNotOncePerTick()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: 10);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        List<string> logged = [];
        void Collect(IOrchestrationLogEntry entry) => logged.Add(entry.Message);
        harness.Log.EntryLogged += Collect;

        var dispatcher = harness.Create_Dispatcher();

        try
        {
            for (var tick = 0; tick < 5; tick++)
                dispatcher.Tick(T0);

            var refusals = logged.Where(line => line.Contains("memberDigestMinutes")).ToList();

            Assert.Single(refusals);

            // BOTH NUMBERS: what the operator typed and what the app is actually using, because the
            // save rewrites the file with the second one (RunnerConfigs_Json.Write).
            Assert.Contains("is 10", refusals[0]);
            Assert.Contains("stays at 5", refusals[0]);
        }
        finally
        {
            harness.Log.EntryLogged -= Collect;
            await dispatcher.Stop_Async();
        }
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

    /// <summary>
    /// THE STATE A COMPACTION LEAVES BEHIND, written directly: a cursor that HAS delivered from this
    /// spoke — <paramref name="highWaterIndex"/> is the proof — with an EMPTY delivered set, because
    /// every identity it held has left the live file, and a live file that now starts at
    /// <paramref name="firstLiveIndex"/>.
    ///
    /// <para>
    /// IT IS WRITTEN RATHER THAN DRIVEN because reaching it through the real compactor needs the
    /// compaction to land between the tick's read and <c>Advance_Cursors</c>' re-read, which is a race
    /// and not a test. The state is not invented:
    /// <c>TurnCursorTests.CreateFrom_Delivered_WhenCompactionTookEveryDeliveredEntry_EmptiesTheSet</c>
    /// is the pure proof that the prune produces exactly this.
    /// </para>
    /// </summary>
    static void Compacted_Spoke(PrintRunnerTestHarness harness, string memberId, int highWaterIndex, int firstLiveIndex, string subject)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(harness.Paths, SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
        var state = PrintSessionState_Store.Read_OrNull(stateFile)!;

        List<ITurnCursor> rewritten = [];

        foreach (var cursor in state.Cursors)
        {
            rewritten.Add(string.Equals(cursor.SourceKey, memberId, StringComparison.OrdinalIgnoreCase)
                ? TurnCursor_Factory.Create(cursor.SourceKey, cursor.ChannelFilePath, highWaterIndex, new HashSet<string>())
                : cursor);
        }

        PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_Cursors(state, rewritten));

        File.WriteAllText(
            harness.Paths.Get_ImplementerChannelFile(ORCH, memberId),
            $"## [{firstLiveIndex}] FROM implementer — {DateTime.Now:yyyy-MM-dd HH:mm} — {subject}\n\nEvidence in the body.\n");
    }

    static int Turns(PrintRunnerTestHarness harness)
    {
        return harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count;
    }

    static int Failed_Attempts(PrintRunnerTestHarness harness)
    {
        return harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).FailedAttempts;
    }

    static bool Ran_Until(IPrintTurnDispatcher dispatcher, DateTime nowLocal, Func<bool> condition)
    {
        return PrintRunnerTestHarness.Drive_Until_At(dispatcher, nowLocal, condition, PrintRunnerTestHarness.GENEROUS);
    }

    /// <summary>
    /// Whether a turn beyond <paramref name="turnsSoFar"/> started at <paramref name="nowLocal"/> — a
    /// turn IN FLIGHT counts, not only a finished one, or a negative assertion would pass while a turn
    /// was busy being wrong. The count is a parameter because the baseline moves: every case here
    /// spends the boot turn and the first-contact turn before it measures anything.
    /// </summary>
    static bool Ran_AnotherTurn(PrintRunnerTestHarness harness, IPrintTurnDispatcher dispatcher, DateTime nowLocal, int turnsSoFar)
    {
        return PrintRunnerTestHarness.Drive_Until_At(
            dispatcher,
            nowLocal,
            () => Turns(harness) > turnsSoFar || dispatcher.Is_TurnInFlight(ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID),
            TimeSpan.FromSeconds(1));
    }
}

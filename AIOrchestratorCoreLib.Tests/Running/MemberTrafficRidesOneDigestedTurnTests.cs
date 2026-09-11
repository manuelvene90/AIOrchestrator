using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE WAKE-UP DIGEST AT ITS CALL SITE — driven through the real dispatcher against the FakeClaude
/// stub, and on an INJECTED clock throughout.
///
/// <para>
/// <see cref="WakeUpPolicyTests"/> is pure and says nothing about whether anybody consults the policy:
/// delete the call in <c>Consider_Session</c> and every case there stays green. These are the ones
/// that redden — two members' reports buy one turn, the owner is never behind a digest, a blocked
/// member is not held, and the app's own bookkeeping starts nothing at all.
/// </para>
/// <para>
/// NO TEST HERE SLEEPS THROUGH A WINDOW. <c>IPrintTurnDispatcher.Tick</c> takes the moment it is
/// deciding at, so five minutes of digest is a later argument rather than five minutes of a suite
/// (<c>PrintRunnerTestHarness.Drive_Until_At</c>). The only real waiting is for a turn that has
/// already been admitted to appear on its background task.
/// </para>
/// <para>
/// EVERY CASE BOOTS THE SUPERVISOR FIRST, deliberately: that is production's order — the supervisor is
/// registered and greets before any member exists — and it keeps the boot turn out of the counts,
/// which are then about the traffic under test alone.
/// </para>
/// </summary>
public class MemberTrafficRidesOneDigestedTurnTests
{
    /// <summary>Production's default (<c>RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW</c>), in the unit config.json takes.</summary>
    const double DIGEST_MINUTES = 5;

    const string ORCH = "repo-1";

    static readonly DateTime T0 = new(2026, 9, 9, 10, 0, 0);

    /// <summary>
    /// THE SAVING, THROUGH THE REAL DISPATCHER. Measured on the VPS 6–9 Sep 2026: 247 of a
    /// supervisor's ~400 wake-ups were member traffic at ~1 M input tokens each. Two members reporting
    /// four minutes apart now buy ONE turn between them, and the second report did not push the
    /// deadline out — the hold is stamped once, on the first held entry.
    /// </summary>
    [Fact]
    public async Task TwoMembersReportingInsideTheWindow_BuyOneSupervisorTurn()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var rev = harness.Store.Add_Member(ORCH, MemberKinds.Reviewer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp, rev);

        // THE WINDOW IS MEASURED FROM THE FIRST HELD ENTRY, so every instant below is relative to this
        // one rather than to the boot. That is the bound the change promises — a report is read at
        // most one window after it is filed — and writing the deadline as "T0 + 5" instead was the
        // first thing this test got wrong.
        var firstReportAt = T0.AddMinutes(2);

        Report(harness, imp, ChannelAuthors.Implementer, "REPORT — parser ported");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, firstReportAt, 2),
            "the first member's report woke the supervisor instead of being digested.");

        Report(harness, rev, ChannelAuthors.Reviewer, "VERDICT — accepted");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, firstReportAt.AddMinutes(3), 2),
            "the second report woke the supervisor three minutes into a five-minute window.");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, firstReportAt.AddMinutes(4), 2),
            "the pair was delivered a minute before the window was out.");

        // AND THE SECOND REPORT DID NOT RESTART THE CLOCK. It arrived three minutes in; if the hold
        // were stamped from the latest entry rather than the first, nothing would run here — that is
        // the failure mode SessionTracker.DigestHeldSince exists to prevent, and it is what a crew
        // reporting steadily would have suffered for ever.
        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, firstReportAt.AddMinutes(DIGEST_MINUTES), () => Turns(harness) == 3, PrintRunnerTestHarness.GENEROUS),
            "the digest window elapsed and the two reports were never delivered.");

        await dispatcher.Stop_Async();

        // AND THE ONE TURN READ BOTH OF THEM — without this, "one turn" would also be satisfied by a
        // turn that took one report and left the other pending, which is the same number wearing a
        // different bug. A cursor with deliveries is what says a channel was handed over.
        var state = harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

        Assert.Equal(3, state.ExecutedTurns.Count);
        Assert.Equal(2, state.Cursors.Count(cursor => cursor.SourceKey != TurnSource_Factory.OWNER_KEY && cursor.Delivered.Count > 0));
    }

    /// <summary>
    /// THE OWNER'S PATH DID NOT GET SLOWER, WHICH IS THE CONSTRAINT ON THE WHOLE CHANGE — and this is
    /// the strong form of it: the owner's message starts a turn at the SAME injected instant at which
    /// a member's report is sitting held, so not one tick of theirs is spent on a digest.
    ///
    /// <para>
    /// It also pins the half that makes holding cheap: a turn takes every pending entry there is, so
    /// the held report rides the owner's turn rather than waiting out its window and buying a second
    /// one. Nothing is lost by being held.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnOwnerMessage_RunsAtOnceAndCarriesTheHeldReportWithIt()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"on it"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        Report(harness, imp, ChannelAuthors.Implementer, "REPORT — parser ported");

        var held = T0.AddMinutes(2);

        Assert.False(Ran_AnotherTurn(harness, dispatcher, held, 2), "the member's report was not digested at all.");

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "how far are we?", DateTime.Now));

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, held, () => Turns(harness) == 3, PrintRunnerTestHarness.GENEROUS),
            "the owner's message waited for the digest window: no turn ran at the instant it landed.");

        await dispatcher.Stop_Async();

        var state = harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

        Assert.Equal(3, state.ExecutedTurns.Count);
        Assert.Equal(2, state.Cursors.Count(cursor => cursor.Delivered.Count > 0));
    }

    /// <summary>
    /// A MEMBER THAT SAYS IT IS BLOCKED IS NOT HELD, at the instant it says so. Two sessions and a
    /// person are waiting on each other at that point, so a digest would be five minutes of nobody
    /// working — the case the constraint on this change names by name.
    ///
    /// <para>
    /// TWO THINGS WERE WRONG WITH THIS CASE UNTIL 2026-09-10 and both made it green for free. The
    /// declaration was the member's FIRST entry, so what released it was the greeting exemption and
    /// not the marker at all; and the case then asserted only that a turn RAN, which a digest that had
    /// been switched off or bypassed guarantees. Measured with the policy stubbed to release
    /// everything: it passed. So the exemption is spent first, and the control half below files an
    /// ORDINARY report from the same member at the same window and requires it to be held.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ABlockedMember_WakesTheSupervisorAtOnce()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"asking the owner"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);
        harness.Spend_FirstContact(dispatcher, ORCH, T0.AddMinutes(1), 1, imp);

        Report(harness, imp, ChannelAuthors.Implementer, $"{MemberState_Resolver.BLOCKED_ON_OWNER_MARKER} — which branch do I target?");

        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, T0.AddMinutes(2), () => Turns(harness) == 3, PrintRunnerTestHarness.GENEROUS),
            "a member declaring BLOCKED ON OWNER was held for the digest.");

        // THE CONTROL: the same member, the same window, an ordinary report — and this one waits. It is
        // what says the digest was live for this session, so the assertion above is about the marker.
        Report(harness, imp, ChannelAuthors.Implementer, "REPORT — carried on with the other branch");

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, T0.AddMinutes(3), 3),
            "the digest was not holding anything for this session, so nothing above proves the marker did the releasing.");

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// THE APP'S OWN BOOKKEEPING STARTS NO TURN, EVER — not on the tick it lands and not when the
    /// digest window is long past. <c>turn_ended</c>, <c>STATUS</c>, the ledger advisory and the
    /// orphan/respawn notices are all written with <see cref="ChannelAuthors.App"/>, which
    /// <see cref="PrintTurn_Trigger.Is_Inbound"/> excludes for every role, so they are pending for
    /// nobody. Measured: zero turns in the 6–9 Sep window were triggered by app entries alone.
    ///
    /// <para>
    /// THE CLOCK IS PUSHED AN HOUR PAST THE WINDOW on purpose. "Never wakes it" and "wakes it five
    /// minutes later" are different rules and a tick at the moment of writing cannot tell them apart:
    /// this is the case that would redden if app entries were merely digested rather than ignored.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheAppsOwnBookkeeping_StartsNoTurnEvenLongAfterTheWindow()
    {
        using var harness = new PrintRunnerTestHarness("supervisor", memberDigestMinutes: DIGEST_MINUTES);
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted"}}""");

        var imp = harness.Store.Add_Member(ORCH, MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        Boot(harness, dispatcher);

        Assert.True(ChannelAppender.Append_AppEntry(
            harness.Paths.Get_ImplementerChannelFile(ORCH, imp),
            AppEntryAudiences.Agent,
            $"{PrintTurn_Words.TURN_ENDED_SUBJECT} {imp} turn 1 — success",
            "outcome: success",
            DateTime.Now));

        Assert.True(ChannelAppender.Append_AppEntry(
            harness.Paths.Get_OwnerChannelFile(ORCH),
            AppEntryAudiences.Agent,
            "STATUS — repo-1",
            "one member working",
            DateTime.Now));

        Assert.False(
            Ran_AnotherTurn(harness, dispatcher, T0.AddHours(1), 1),
            "the app's own bookkeeping bought the supervisor a turn.");

        await dispatcher.Stop_Async();
    }

    /// <summary>
    /// The supervisor's greeting, which production always has before a member can write: it runs with
    /// nothing pending (<c>Needs_BootTurn</c>) and is never held, so it is out of the way — and out of
    /// the counts — before the traffic under test arrives.
    /// </summary>
    static void Boot(PrintRunnerTestHarness harness, IPrintTurnDispatcher dispatcher)
    {
        Assert.True(
            PrintRunnerTestHarness.Drive_Until_At(dispatcher, T0, () => Turns(harness) == 1, PrintRunnerTestHarness.GENEROUS),
            "the supervisor never took its boot turn, so nothing below is measuring the digest.");
    }

    static void Report(PrintRunnerTestHarness harness, string memberId, ChannelAuthors author, string subject)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(
            harness.Paths.Get_ImplementerChannelFile(ORCH, memberId), author, subject, "Evidence in the body.", DateTime.Now));
    }

    static int Turns(PrintRunnerTestHarness harness)
    {
        return harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count;
    }

    /// <summary>
    /// Whether a turn beyond <paramref name="turnsSoFar"/> started at <paramref name="nowLocal"/>. A
    /// turn IN FLIGHT counts, not only a finished one: a negative assertion that looked at the executed
    /// list alone would pass while a turn was busy being wrong.
    ///
    /// <para>
    /// THE COUNT IS A PARAMETER AND NOT THE CONSTANT 1 IT WAS, and the reason is not the one this
    /// paragraph gave until 2026-09-10. Since these cases spend each spoke's first-contact exemption
    /// before they measure anything (<c>PrintRunnerTestHarness.Spend_FirstContact</c>) the baseline is
    /// no longer the boot turn alone — so a hard-coded 1 would compare 2 against 1, report a turn that
    /// ran minutes ago as a turn that ran now, and make every negative assertion below trivially RED,
    /// not trivially green. A stale baseline in this helper cannot manufacture a pass; it manufactures
    /// a failure, which is the safe direction and the wrong justification to leave written down.
    /// </para>
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

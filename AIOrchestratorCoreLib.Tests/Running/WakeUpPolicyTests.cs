using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// WHAT WAKES A SUPERVISOR, WHAT WAITS, AND WHAT NEVER WAKES IT (spec §C4).
///
/// <para>
/// Measured on the VPS 6–9 Sep 2026: ~400 supervisor wake-ups in the window, 247 of them member
/// traffic, 2.5 model calls and a mean 398 k context per call — one wake-up is on the order of 1 M
/// input tokens, and 15 % of the turns were acknowledgement-sized. So a member's ordinary report is
/// held for the digest window and several of them ride ONE turn.
/// </para>
/// <para>
/// THESE ARE THE PURE CASES — no clock, no files, no session. The dispatcher's own behaviour is pinned
/// separately in <see cref="MemberTrafficRidesOneDigestedTurnTests"/>, for the reason the coalesce
/// policy's tests give: delete the call in <c>Consider_Session</c> and every test in this file stays
/// green.
/// </para>
/// </summary>
public class WakeUpPolicyTests
{
    static readonly ITurnSource OWNER = TurnSource_Factory.Create_Owner("/o/owner-channel.md");
    static readonly ITurnSource IMP = TurnSource_Factory.Create_Spoke("imp-1", "/o/imp-1/channel.md");
    static readonly ITurnSource REV = TurnSource_Factory.Create_Spoke("rev-2", "/o/rev-2/channel.md");

    static readonly DateTime NOW = new(2026, 9, 9, 10, 5, 0);
    static readonly TimeSpan DIGEST = TimeSpan.FromMinutes(5);

    static IChannelEntry Entry(string author, string subject, string body = "body")
    {
        return ChannelEntry_Parser.Parse_All($"## [1] FROM {author} — 2026-09-09 10:05 — {subject}\n\n{body}\n")[0];
    }

    /// <summary>
    /// NO CHANNEL IS ON FIRST CONTACT unless a case says so — the state every source is in for all but
    /// the first entry of its life, and therefore the one these cases mean when they say "a member's
    /// report". <see cref="AFirstEntryFromANewChannel_IsNotHeld"/> is the other side.
    /// </summary>
    static string? Resolve(IReadOnlyList<PendingEntry> pending, DateTime? heldSince, DateTime now)
    {
        return WakeUp_Policy.Resolve_WakeReason_OrNull(pending, NO_FIRST_CONTACT, heldSince, now, DIGEST);
    }

    static readonly IReadOnlyCollection<string> NO_FIRST_CONTACT = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// THE OWNER'S LATENCY IS UNTOUCHED — the whole point of the change, and the thing it must not
    /// cost. Their message wakes the supervisor on the tick it is seen, digest or no digest.
    /// </summary>
    [Fact]
    public void AnOwnerMessage_WakesTheSupervisorAtOnce()
    {
        var reason = Resolve([new PendingEntry(OWNER, Entry("owner", "restart the crew"))], NOW, NOW);

        Assert.NotNull(reason);
        Assert.Contains("owner", reason);
    }

    /// <summary>
    /// AND THE OWNER TYPING STRAIGHT INTO A SPOKE IS STILL THE OWNER. It is a path the app supports
    /// (a supervisor's sources include every open spoke, and the owner can write into one from the
    /// terminal), and the digest must not decide by channel what it can decide by author.
    /// </summary>
    [Fact]
    public void AnOwnerMessageInASpoke_WakesTheSupervisorAtOnce()
    {
        Assert.NotNull(Resolve([new PendingEntry(IMP, Entry("owner", "do this one first"))], NOW, NOW));
    }

    /// <summary>A member's ordinary report is the 247: held until the window is out.</summary>
    [Fact]
    public void AMemberReport_IsHeldForTheDigestWindow()
    {
        Assert.Null(Resolve([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NOW, NOW.AddMinutes(4)));
    }

    /// <summary>And delivered when it is out — the bound on how late a report can be read.</summary>
    [Fact]
    public void AMemberReport_IsDeliveredWhenTheDigestWindowElapses()
    {
        var reason = Resolve([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NOW, NOW.AddMinutes(5));

        Assert.NotNull(reason);
        Assert.Contains("digest", reason);
    }

    /// <summary>
    /// THE SAVING, IN ONE ASSERTION. Two members reporting four minutes apart are ONE pending set
    /// when the window runs out, so they buy one turn between them instead of one each — and the
    /// second one's arrival did not push the deadline out, which is what the separate hold stamp
    /// (<c>SessionTracker.DigestHeldSince</c>) exists for.
    /// </summary>
    [Fact]
    public void TwoMembersReportingInsideOneWindow_RideOneTurn()
    {
        List<PendingEntry> pending =
        [
            new(IMP, Entry("implementer", "REPORT — parser done")),
            new(REV, Entry("reviewer", "VERDICT — accepted")),
        ];

        Assert.Null(Resolve(pending, NOW, NOW.AddMinutes(4)));
        Assert.NotNull(Resolve(pending, NOW, NOW.AddMinutes(5)));
    }

    /// <summary>
    /// A BLOCKED MEMBER IS NEVER HELD. <c>BLOCKED ON OWNER</c> means two sessions and a person are
    /// waiting on one another, so five minutes of digest would be five minutes of nobody working —
    /// the case the constraint on this change names explicitly.
    /// </summary>
    [Fact]
    public void AMemberDeclaringItIsBlocked_WakesTheSupervisorAtOnce()
    {
        var reason = Resolve(
            [new PendingEntry(IMP, Entry("implementer", $"{MemberState_Resolver.BLOCKED_ON_OWNER_MARKER} — which branch?"))],
            NOW,
            NOW);

        Assert.NotNull(reason);
        Assert.Contains(MemberState_Resolver.BLOCKED_ON_OWNER_MARKER, reason);
    }

    /// <summary>
    /// A QUESTION IS NOT HELD EITHER — and it is read from the BODY, at the start of a line, which is
    /// where the marker vocabulary says a declaration may live. The supervisor's own <c>QUESTION:</c>
    /// shape is what a member reaches for when it has one, and waking is both the cheap and the safe
    /// direction if it does.
    /// </summary>
    [Fact]
    public void AMemberAskingAQuestionInTheBody_WakesTheSupervisorAtOnce()
    {
        Assert.NotNull(Resolve(
            [new PendingEntry(IMP, Entry("implementer", "parser done", "QUESTION: do I merge or hold?"))],
            NOW,
            NOW));
    }

    /// <summary>
    /// AND A REPORT THAT MERELY MENTIONS A QUESTION IS NOT ASKING ONE — the review's MEDIUM of
    /// 2026-09-09, and the reason the marker carries its colon.
    ///
    /// <para>
    /// The marker was the bare word <c>QUESTION</c>, and the subject half of
    /// <see cref="MemberState_Resolver.Contains_Marker"/> matches a whole token ANYWHERE, case
    /// insensitively. So this exact subject defeated the hold — and members discuss this vocabulary
    /// constantly, which put the digest's cost back at one wake per report, silently and in the
    /// direction that looks like it is working.
    /// </para>
    /// </summary>
    [Fact]
    public void AReportMentioningTheWordQuestion_IsStillHeld()
    {
        Assert.Null(Resolve(
            [new PendingEntry(IMP, Entry("implementer", "REPORT — the open question about the parser is settled"))],
            NOW,
            NOW.AddMinutes(1)));

        Assert.Null(Resolve(
            [new PendingEntry(IMP, Entry("implementer", "REPORT — parser done", "- the question of the retry order is answered below"))],
            NOW,
            NOW.AddMinutes(1)));
    }

    /// <summary>
    /// AND THE MARKER IS THE REPO'S, NOT A FOURTH SPELLING OF IT (CLAUDE.md decision 12). The repo
    /// spelled this vocabulary three times before this stage and every one of those carried the colon;
    /// a fourth literal here is the drift that decision names, so the list holds the constant itself
    /// and this case is what notices if somebody re-types it.
    /// </summary>
    [Fact]
    public void TheQuestionMarker_IsTheOneTheRepoAlreadyOwns()
    {
        // BOTH names, on purpose: the word has ONE literal (MemberState_Resolver) and the mirror's
        // own name is now an alias of it, so this case reddens if either drifts from the other.
        Assert.Contains(MemberState_Resolver.QUESTION_MARKER, WakeUp_Policy.ESCALATION_MARKERS);
        Assert.Equal(MemberState_Resolver.QUESTION_MARKER, OwnerPush_Policy.QUESTION_MARKER);
        Assert.Contains(MemberState_Resolver.BLOCKED_ON_OWNER_MARKER, WakeUp_Policy.ESCALATION_MARKERS);
        Assert.Equal(2, WakeUp_Policy.ESCALATION_MARKERS.Count);
    }

    /// <summary>
    /// AND A BRIEF THAT QUOTES A MARKER DOES NOT DECLARE ONE. The match is
    /// <see cref="MemberState_Resolver.Contains_Marker"/>'s, which excludes quotation for exactly this
    /// reason — members discuss this vocabulary constantly, and a mention that woke the supervisor
    /// would put the digest's cost back at one wake per report.
    /// </summary>
    [Fact]
    public void AMemberMerelyQuotingAMarker_IsStillHeld()
    {
        Assert.Null(Resolve(
            [new PendingEntry(IMP, Entry("implementer", "REPORT — parser done", "> BLOCKED ON OWNER is what I would have written yesterday"))],
            NOW,
            NOW.AddMinutes(1)));
    }

    /// <summary>
    /// ONE URGENT ENTRY RELEASES THE WHOLE SET, because a turn takes every pending entry there is.
    /// That is what makes holding cheap: nothing waits for its own second turn.
    /// </summary>
    [Fact]
    public void AnUrgentEntryBesideHeldOnes_ReleasesThemAll()
    {
        Assert.NotNull(Resolve(
            [
                new PendingEntry(IMP, Entry("implementer", "REPORT — parser done")),
                new PendingEntry(REV, Entry("reviewer", $"{MemberState_Resolver.BLOCKED_ON_OWNER_MARKER} — no test data")),
            ],
            NOW,
            NOW));
    }

    /// <summary>
    /// EVERY OTHER ROLE'S TIMING IS UNTOUCHED, AND WITHOUT A ROLE TEST ANYWHERE. A member's inbound
    /// authors are its supervisor and the owner (<see cref="PrintTurn_Trigger.Is_Inbound"/>), so a
    /// brief is not digestable traffic and an implementer is woken by it exactly as it was before this
    /// stage existed.
    /// </summary>
    [Fact]
    public void ASupervisorBriefToAMember_IsNeverHeld()
    {
        Assert.NotNull(Resolve([new PendingEntry(IMP, Entry("supervisor", "BRIEF — port the parser"))], NOW, NOW));
    }

    /// <summary>
    /// THE APP'S OWN BOOKKEEPING CANNOT REACH THIS METHOD AT ALL, which is the strongest form of "it
    /// never wakes the supervisor": <see cref="PrintTurn_Trigger.Is_Inbound"/> is false for
    /// <see cref="ChannelAuthors.App"/> for every role, so a <c>turn_ended</c>, a <c>STATUS</c>, a
    /// ledger advisory or an orphan notice is never pending for anybody and never enters a cursor
    /// either (<c>TurnCursor_Factory</c> records inbound entries only). Measured: zero turns in the
    /// 6–9 Sep window were triggered by app entries alone. Pinned here so a later change to
    /// <c>Is_Inbound</c> cannot quietly make app traffic a reason to wake the most expensive role in
    /// the system.
    /// </summary>
    [Fact]
    public void TheAppsOwnEntries_AreInboundForNobody()
    {
        foreach (var role in SessionRole_Names.ALL)
            Assert.False(PrintTurn_Trigger.Is_Inbound(role, ChannelAuthors.App), $"an app entry is inbound for {role}");
    }

    /// <summary>
    /// A ZERO WINDOW IS THE DIGEST TURNED OFF, not a zero-length wait that still holds for a tick.
    /// It is the owner's way back to one-entry-one-turn without a deployment.
    /// </summary>
    [Fact]
    public void AZeroDigestWindow_DeliversAtOnce()
    {
        Assert.NotNull(WakeUp_Policy.Resolve_WakeReason_OrNull(
            [new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NO_FIRST_CONTACT, NOW, NOW, TimeSpan.Zero));
    }

    /// <summary>
    /// THE BOOT TURN RUNS WITH NOTHING PENDING and must not be held: the greeting it produces is what
    /// creates the orchestration's Telegram topic, so holding it would hold the owner's own way in.
    /// </summary>
    [Fact]
    public void AnEmptyPendingSet_IsTheBootTurnAndRuns()
    {
        Assert.NotNull(Resolve([], null, NOW));
    }

    /// <summary>
    /// A HOLD NOBODY STAMPED IS NOT A REASON TO WAIT. The dispatcher always sets the stamp before it
    /// asks, so this is the unreachable-by-construction case — and it answers with today's behaviour
    /// rather than holding traffic on a clock that was never started.
    /// </summary>
    [Fact]
    public void MemberTrafficWithNoHoldStamp_IsDelivered()
    {
        Assert.NotNull(Resolve([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], null, NOW));
    }

    /// <summary>
    /// AND A STAMP IN THE FUTURE IS A CLOCK THAT MOVED, NEVER A LONGER WAIT (CLAUDE.md decision 12: a
    /// duration that cannot be believed must not become a confident wrong number). The honest
    /// direction here is to deliver.
    /// </summary>
    [Fact]
    public void AHoldStampInTheFuture_DeliversRatherThanWaiting()
    {
        Assert.NotNull(Resolve([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NOW.AddMinutes(30), NOW));
    }

    /// <summary>
    /// THE CLOCK STARTS ON THE FIRST HELD ENTRY: this is the question the dispatcher asks to decide
    /// when to stamp it, and it must be true of a member's report and false of everything else.
    /// </summary>
    [Fact]
    public void OnlyAMembersOrdinaryEntry_StartsTheHold()
    {
        Assert.True(WakeUp_Policy.Contains_DigestableTraffic([new PendingEntry(IMP, Entry("implementer", "REPORT — parser done"))], NO_FIRST_CONTACT));
        Assert.True(WakeUp_Policy.Contains_DigestableTraffic([new PendingEntry(REV, Entry("reviewer", "VERDICT — accepted"))], NO_FIRST_CONTACT));

        Assert.False(WakeUp_Policy.Contains_DigestableTraffic([new PendingEntry(OWNER, Entry("owner", "restart the crew"))], NO_FIRST_CONTACT));
        Assert.False(WakeUp_Policy.Contains_DigestableTraffic([new PendingEntry(IMP, Entry("supervisor", "BRIEF — port the parser"))], NO_FIRST_CONTACT));
        Assert.False(WakeUp_Policy.Contains_DigestableTraffic([], NO_FIRST_CONTACT));
        Assert.False(WakeUp_Policy.Contains_DigestableTraffic(
            [new PendingEntry(IMP, Entry("implementer", $"{MemberState_Resolver.BLOCKED_ON_OWNER_MARKER} — which branch?"))], NO_FIRST_CONTACT));
    }

    /// <summary>
    /// A FIRST ENTRY FROM A CHANNEL NOTHING HAS EVER BEEN DELIVERED FROM IS NOT HELD — the greeting of
    /// a member created seconds ago, which the digest used to cost a whole window of dead time before
    /// it could be briefed (review finding, 2026-09-09).
    ///
    /// <para>
    /// BOTH QUESTIONS, because the dispatcher asks both and they have to agree: it must not START a
    /// hold clock for such an entry either, or the next report would inherit a window that began
    /// before it existed.
    /// </para>
    /// </summary>
    [Fact]
    public void AFirstEntryFromANewChannel_IsNotHeld()
    {
        IReadOnlyCollection<string> firstContact = new HashSet<string>([IMP.Key], StringComparer.OrdinalIgnoreCase);
        List<PendingEntry> greeting = [new(IMP, Entry("implementer", "imp-1 online"))];

        Assert.NotNull(WakeUp_Policy.Resolve_WakeReason_OrNull(greeting, firstContact, NOW, NOW, DIGEST));
        Assert.False(WakeUp_Policy.Contains_DigestableTraffic(greeting, firstContact));

        // AND ONLY THAT CHANNEL. A crew where one member is new must not have the OTHERS' reports
        // released with it — the exemption is per source, which is what the set is for.
        List<PendingEntry> otherMembersReport = [new(REV, Entry("reviewer", "VERDICT — accepted"))];

        Assert.Null(WakeUp_Policy.Resolve_WakeReason_OrNull(otherMembersReport, firstContact, NOW, NOW.AddMinutes(1), DIGEST));
        Assert.True(WakeUp_Policy.Contains_DigestableTraffic(otherMembersReport, firstContact));
    }

    /// <summary>
    /// BUT A TURN TAKES EVERYTHING PENDING, so a new member's greeting IN THE SAME SET releases the
    /// others' held reports with it — measured and disclosed on 2026-09-10 rather than fixed.
    ///
    /// <para>
    /// This is not the exemption leaking: it is the queue. A bridge-driven session has no queue but
    /// the channels, one turn takes every pending inbound entry of every source, and that is the
    /// property the whole digest rests on ("nothing is lost by being held"). There is no way to
    /// release the greeting without releasing what is pending beside it short of starting a turn on a
    /// SUBSET, which would mean two turns where the design promises one and would break the
    /// idempotency record that makes a turn re-runnable.
    /// </para>
    /// <para>
    /// SO WHAT IT COSTS IS SIZE, NOT CORRECTNESS: the other reports are read EARLY, which is the safe
    /// direction, and the saving is smaller by one wake-up every time <c>add-implementer</c> lands
    /// inside a window. <see cref="AFirstEntryFromANewChannel_IsNotHeld"/> is the other half — the
    /// exemption itself is per source, so a new member elsewhere does not by itself release a report.
    /// </para>
    /// </summary>
    [Fact]
    public void AFirstEntryReleasesEverythingPendingWithIt_BecauseATurnTakesTheWholeSet()
    {
        IReadOnlyCollection<string> firstContact = new HashSet<string>([IMP.Key], StringComparer.OrdinalIgnoreCase);

        List<PendingEntry> both =
        [
            new(REV, Entry("reviewer", "VERDICT — accepted")),
            new(IMP, Entry("implementer", "imp-1 online")),
        ];

        var reason = WakeUp_Policy.Resolve_WakeReason_OrNull(both, firstContact, NOW, NOW.AddMinutes(1), DIGEST);

        Assert.NotNull(reason);
        Assert.Contains(IMP.Key, reason);
    }

    /// <summary>
    /// AND THE MATCH ON A SOURCE KEY IS CASE-INSENSITIVE, the same as the cursor set's. A key is a word
    /// an agent typed, and the two records have to agree on what "the same channel" means or the
    /// exemption applies to a channel the cursor thinks is a different one.
    /// </summary>
    [Fact]
    public void TheFirstContactSet_MatchesASourceKeyTheWayTheCursorSetDoes()
    {
        IReadOnlyCollection<string> firstContact = new HashSet<string>(["IMP-1"], StringComparer.OrdinalIgnoreCase);

        Assert.NotNull(WakeUp_Policy.Resolve_WakeReason_OrNull(
            [new PendingEntry(IMP, Entry("implementer", "imp-1 online"))], firstContact, NOW, NOW, DIGEST));
    }
}

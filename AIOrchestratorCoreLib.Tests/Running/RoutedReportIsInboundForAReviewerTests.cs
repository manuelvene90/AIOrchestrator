using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE ONE APP ENTRY THAT WAKES ANYBODY, AND THE FOUR SCREENS THAT KEEP IT ALONE. The app relays a
/// fix report into a reviewer's channel so a fix round does not cost the supervisor a wake-up to act
/// as a postman (2026-09-15 one-wake-model spec, step 4). Everything else the app writes still wakes
/// nobody — <see cref="WakeUpPolicyTests.TheAppsOwnEntries_AreInboundForNobody"/> is the guard on
/// the author-only overload and it is deliberately NOT relaxed: this is a second, narrower question,
/// asked of the whole entry.
/// </summary>
public class RoutedReportIsInboundForAReviewerTests
{
    static IChannelEntry Entry(ChannelAuthors author, string subject)
    {
        return ChannelEntry_Parser.Parse_All(
            $"## [1] FROM {ChannelAuthor_Words.Get_Word(author)} — 2026-09-15 10:00 — {subject}\nbody\n")[0];
    }

    /// <summary>A subject as the relay writes it: the audience tag outermost, the routed tag inside it.</summary>
    static string Routed(string subject)
    {
        return AppEntryAudience_Tag.Apply(RoutedReport_Tag.Apply(subject), AppEntryAudiences.Agent);
    }

    const string SUBJECT = "re-review — imp-1's fix";

    [Fact]
    public void ARoutedReportIsInboundForAReviewer()
    {
        Assert.True(PrintTurn_Trigger.Is_Inbound(SessionRoles.Reviewer, Entry(ChannelAuthors.App, Routed(SUBJECT))));
    }

    /// <summary>
    /// AND FOR NOBODY ELSE. The supervisor is the role this series exists to wake less; an
    /// implementer would be answering a brief addressed to a reviewer.
    /// </summary>
    [Theory]
    [InlineData(SessionRoles.Supervisor)]
    [InlineData(SessionRoles.Implementer)]
    [InlineData(SessionRoles.Solo)]
    [InlineData(SessionRoles.General)]
    [InlineData(SessionRoles.Communicator)]
    public void ARoutedReportIsInboundForNoOtherRole(SessionRoles role)
    {
        Assert.False(PrintTurn_Trigger.Is_Inbound(role, Entry(ChannelAuthors.App, Routed(SUBJECT))));
    }

    /// <summary>
    /// THE APP'S ORDINARY BOOKKEEPING IS UNMOVED, asked through the NEW overload — the old one is
    /// pinned next door, and pinning only that would leave the widened door untested.
    /// </summary>
    [Theory]
    [InlineData("[agent] turn_ended imp-1 turn 4 — ok")]
    [InlineData("[agent] PLAN.md is behind your verdicts")]
    [InlineData("STATUS — 3 of 7 lines closed")]
    public void AnOrdinaryAppEntryIsStillInboundForNobody(string subject)
    {
        foreach (var role in SessionRole_Names.ALL)
            Assert.False(PrintTurn_Trigger.Is_Inbound(role, Entry(ChannelAuthors.App, subject)), $"'{subject}' woke {role}");
    }

    /// <summary>
    /// A MEMBER CANNOT TALK ITS WAY INTO ANOTHER MEMBER'S TURN. The tag means something only where
    /// the APP put it: the author screen is first, so a member whose entry quotes the tag — which is
    /// exactly what a report discussing this mechanism would do — relays nothing.
    ///
    /// <para>
    /// THE AUTHORS HERE ARE THE ONES THE ANSWER CAN BE READ FROM. The plan's sketch asserted this of
    /// a SUPERVISOR-authored entry too, and that assertion is false and always was: a supervisor's
    /// entry is inbound for a reviewer through the author test, tag or no tag — that is the ordinary
    /// brief. Asserting it would have failed for a reason that has nothing to do with this task, so
    /// the case screens the authors whose only possible route in is the tag.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(ChannelAuthors.Implementer)]
    [InlineData(ChannelAuthors.Reviewer)]
    public void TheTagOnANonAppEntryMeansNothing(ChannelAuthors author)
    {
        Assert.False(PrintTurn_Trigger.Is_Inbound(SessionRoles.Reviewer, Entry(author, Routed(SUBJECT))));
    }

    /// <summary>
    /// AND ONLY AT THE FRONT. A prefix test, like the audience tag's, so an entry that DISCUSSES the
    /// tag does not acquire its powers.
    /// </summary>
    [Fact]
    public void TheTagIsReadAtTheFrontOfTheSubjectAndNowhereElse()
    {
        Assert.False(PrintTurn_Trigger.Is_Inbound(
            SessionRoles.Reviewer,
            Entry(ChannelAuthors.App, $"[agent] a note about the {RoutedReport_Tag.ROUTED_TAG} tag")));
    }

    /// <summary>
    /// IT IS INBOUND, WHICH MEANS IT IS NOT A RIDING NOTE. Both were true of it before the exclusion
    /// in Is_AgentNote — two answers in one expression, which is how an entry gets delivered twice or
    /// not at all. The live control beside it is an ordinary note, which must still ride.
    /// </summary>
    [Fact]
    public void ARoutedReportIsNotAnAgentNote()
    {
        Assert.False(PrintTurn_Trigger.Is_AgentNote(Entry(ChannelAuthors.App, Routed(SUBJECT))));
        Assert.True(PrintTurn_Trigger.Is_AgentNote(Entry(ChannelAuthors.App, "[agent] PLAN.md is behind your verdicts")));
    }

    /// <summary>
    /// AND IT IS PENDING FOR A REVIEWER THAT HAS NOT SEEN IT — the property every screen above exists
    /// to serve. Without this the whole task is a predicate nobody calls.
    /// </summary>
    [Fact]
    public void ARoutedReportIsPendingForAReviewerThatHasNotSeenIt()
    {
        var entries = ChannelEntry_Parser.Parse_All(
            $"## [1] FROM app — 2026-09-15 10:00 — {Routed(SUBJECT)}\nthe delta and the findings\n");

        var source = TurnSource_Factory.Create_Spoke("rev-1", "/tmp/channel.md");

        var pending = PrintTurn_Trigger.Select_Pending(SessionRoles.Reviewer, entries, TurnCursor_Factory.Create_Empty(source));

        Assert.Equal(SUBJECT, Assert.Single(pending).Subject.Replace(AppEntryAudience_Tag.AGENT_TAG, "").Replace(RoutedReport_Tag.ROUTED_TAG, "").Trim());
    }

    /// <summary>
    /// AND IT IS ABSORBED AS HISTORY BY A BASELINE, like every other inbound entry. A reviewer
    /// registered after one was written must not be handed it again as new traffic — the reason the
    /// cursor's three call sites move onto this overload rather than only Select_Pending.
    /// </summary>
    [Fact]
    public void ARoutedReportAlreadyInTheFileIsAbsorbedByTheBaseline()
    {
        var entries = ChannelEntry_Parser.Parse_All(
            $"## [1] FROM app — 2026-09-15 10:00 — {Routed(SUBJECT)}\nthe delta and the findings\n");

        var source = TurnSource_Factory.Create_Spoke("rev-1", "/tmp/channel.md");
        var baseline = TurnCursor_Factory.Create_Baseline(source, SessionRoles.Reviewer, entries);

        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Reviewer, entries, baseline));

        // AND THE LIVE CONTROL: the same file read by a role the relay was not addressed to absorbs
        // nothing, so "empty" above is the absorption and not a baseline that swallows everything.
        Assert.Empty(TurnCursor_Factory.Create_Baseline(source, SessionRoles.Implementer, entries).Delivered);
    }

    /// <summary>
    /// AND A DELIVERED ROUTED REPORT SURVIVES THE NEXT TURN'S PRUNE. Dropped from the still-live set
    /// it would be handed to the reviewer again on the next turn, and the turn after — the failure
    /// the comment above that loop already records for app notes.
    ///
    /// <para>
    /// IT TAKES TWO ROUNDS AND A NEIGHBOUR, and the first sketch of this case had neither, so it
    /// stayed GREEN with the prune reverted. The prune only removes on a LATER cursor — the first
    /// one adds what was just delivered after it — and it is skipped entirely when the still-live set
    /// comes back empty, which is the factory's guard against an unreadable file wiping a cursor. So
    /// the routed report is delivered beside an ordinary supervisor brief, which keeps that set
    /// non-empty and lets the prune actually run.
    /// </para>
    /// </summary>
    [Fact]
    public void ADeliveredRoutedReportSurvivesTheNextTurnsPrune()
    {
        var entries = ChannelEntry_Parser.Parse_All(
            "## [1] FROM supervisor — 2026-09-15 09:59 — BRIEF — review the fix\nread it\n"
            + $"## [2] FROM app — 2026-09-15 10:00 — {Routed(SUBJECT)}\nthe delta and the findings\n");

        var source = TurnSource_Factory.Create_Spoke("rev-1", "/tmp/channel.md");

        // THE LIVE CONTROL: both entries are pending for a reviewer that has seen nothing, so the
        // emptiness below is delivery and not a fixture that was never inbound in the first place.
        Assert.Equal(2, PrintTurn_Trigger.Select_Pending(SessionRoles.Reviewer, entries, TurnCursor_Factory.Create_Empty(source)).Count);

        var afterTheTurn = TurnCursor_Factory.CreateFrom_Delivered(
            TurnCursor_Factory.Create_Empty(source), SessionRoles.Reviewer, entries, entries);

        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Reviewer, entries, afterTheTurn));

        // AND A LATER TURN THAT DELIVERED NOTHING NEW: this is where the prune runs.
        var afterAQuietTurn = TurnCursor_Factory.CreateFrom_Delivered(
            afterTheTurn, SessionRoles.Reviewer, entries, []);

        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Reviewer, entries, afterAQuietTurn));
    }

    /// <summary>
    /// AND IT MOVES THE HIGH-WATER MARK, because a routed report IS traffic: that mark is what the
    /// archive-gap warning reads, and an entry the reviewer was actually handed must count.
    /// </summary>
    [Fact]
    public void ARoutedReportMovesTheHighWaterMark()
    {
        var entries = ChannelEntry_Parser.Parse_All(
            $"## [7] FROM app — 2026-09-15 10:00 — {Routed(SUBJECT)}\nthe delta and the findings\n");

        var source = TurnSource_Factory.Create_Spoke("rev-1", "/tmp/channel.md");

        Assert.Equal(7, TurnCursor_Factory.CreateFrom_Delivered(
            TurnCursor_Factory.Create_Empty(source), SessionRoles.Reviewer, entries, entries).HighWaterIndex);

        // THE LIVE CONTROL: the same entry read by a role the relay was not addressed to is not
        // traffic, so its mark does not move — which is what makes the 7 above the routed rule and
        // not "any delivered entry moves it".
        Assert.Equal(0, TurnCursor_Factory.CreateFrom_Delivered(
            TurnCursor_Factory.Create_Empty(source), SessionRoles.Implementer, entries, entries).HighWaterIndex);
    }
}

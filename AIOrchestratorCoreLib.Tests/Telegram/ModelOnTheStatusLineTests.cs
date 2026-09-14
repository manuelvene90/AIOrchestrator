using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using AIOrchestratorCoreLib.Status.SessionModelReading;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// What model and effort each session is actually running, on the one message per topic. Requested
/// 2026-09-09: the dial decides a crew's cost and pace, and until now the only place it showed was
/// each session's own terminal — invisible from the phone.
///
/// Every reading that has one shows it, unlike the context figure, which members earn only near
/// full: a context percentage is an ALARM and is worth a glance only past a threshold, whereas the
/// model is a FACT about the row that is either known or not. The supervisor's rides on the LEAD
/// LINE for the reason ContextOnTheStatusLineTests gives — this line lists members and a crew's
/// supervisor is not one — and the fixtures here are that file's, so the two surfaces are pinned
/// against the same rows.
/// </summary>
public class ModelOnTheStatusLineTests
{
    static readonly DateTime NOW = new(2026, 8, 21, 20, 30, 0);
    static readonly DateTime PROBED = new(2026, 8, 21, 20, 29, 0, DateTimeKind.Utc);

    [Fact]
    public void AMembersModelAndEffortRideAfterItsDuration()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", Briefed(), model: Fable("xhigh"))], null, NOW,
            aMessageIsAlreadyPosted: false);

        Assert.Equal("• imp-1 · wiring the context field · 30 min · Fable 5.1 xhigh", line.Split('\n')[1]);
    }

    /// <summary>A quiet member is still running SOMETHING, and the row says what.</summary>
    [Fact]
    public void AMemberStandingByStillNamesItsModel()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", [], model: Fable("xhigh"))], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("• imp-1 · standing by · Fable 5.1 xhigh", line.Split('\n')[1]);
    }

    /// <summary>
    /// The field ORDER on a member row, with everything present: who, what, how long, on what, how
    /// full. The model sits between the duration and the context figure — the facts about the row
    /// first, the alarm last, where a glance lands.
    /// </summary>
    [Fact]
    public void TheModelSitsBetweenTheDurationAndTheContextField()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("solo-1", Briefed(), Reading(52), Fable("xhigh"))], null, NOW,
            aMessageIsAlreadyPosted: false);

        Assert.Equal("• solo-1 · wiring the context field · 30 min · Fable 5.1 xhigh · ctx 52%", line.Split('\n')[1]);
    }

    /// <summary>An older Claude Code reports no dial. Just the model, nothing trailing.</summary>
    [Fact]
    public void AnEffortlessReadingIsJustTheModel()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", Briefed(), model: SessionModelReading_Factory.Create("Opus 5", null))], null, NOW,
            aMessageIsAlreadyPosted: false);

        Assert.Equal("• imp-1 · wiring the context field · 30 min · Opus 5", line.Split('\n')[1]);
    }

    /// <summary>
    /// A member that has not reported renders EXACTLY as it did before this field existed — asserted
    /// as the whole row, because a Contains check cannot see a trailing separator or placeholder.
    /// TopicStatusLineBuilderTests.TheApprovedShape pins the same promise across a whole message.
    /// </summary>
    [Fact]
    public void AMemberWithNoReadingRendersExactlyAsBefore()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", Briefed())], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("• imp-1 · wiring the context field · 30 min", line.Split('\n')[1]);
    }

    [Fact]
    public void TheSupervisorsModelRidesOnTheLeadLine()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(72, 113), [], null, NOW, aMessageIsAlreadyPosted: false,
            supervisorModel: Fable("xhigh"));

        Assert.Equal("PULSE · 72/113 · 63% · sup Fable 5.1 xhigh", line);
    }

    /// <summary>
    /// The field ORDER on the lead line with all three optional fields present: the ledger reading
    /// stays together (count, percent, how long unchanged), then the supervisor's model, then its
    /// context — the same facts-then-alarm order as a member row, and `sup ctx` keeps its place at
    /// the very end so ContextOnTheStatusLineTests' expectations still hold.
    /// </summary>
    [Fact]
    public void TheLeadLineFieldOrderWithEverythingPresent()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), supervisorContext: Reading(41), supervisorModel: Fable("xhigh"));

        Assert.Equal("PULSE · 3/4 · 75% · unchanged 25 min · sup Fable 5.1 xhigh · sup ctx 41%", line);
    }

    /// <summary>
    /// NOTHING TO SAY STILL MEANS SAY NOTHING. A supervisor's model is not substance on its own —
    /// a topic with no ledger, no live member and no history stays silent rather than send a message
    /// whose whole content is the name of a model.
    /// </summary>
    [Fact]
    public void ASupervisorModelAloneDoesNotBreakTheSilenceRule()
    {
        Assert.Equal(
            "",
            TopicStatusLine_Builder.Build(
                null, [], null, NOW, aMessageIsAlreadyPosted: false, supervisorModel: Fable("xhigh")));
    }

    /// <summary>
    /// With no ledger the lead word comes back BARE and the supervisor's model goes with it, as the
    /// context figure always has: that return is the "nothing to say" shape, and hanging a field
    /// off it would be the say-nothing message the builder refuses. The member rows still carry
    /// theirs.
    /// </summary>
    [Fact]
    public void TheBareLeadWordStaysBareWithoutALedger()
    {
        var lines = TopicStatusLine_Builder.Build(
            null, [Member("imp-1", Briefed(), model: Fable("xhigh"))], null, NOW, aMessageIsAlreadyPosted: false,
            supervisorModel: Fable("xhigh")).Split('\n');

        Assert.Equal("PULSE", lines[0]);
        Assert.Equal("• imp-1 · wiring the context field · 30 min · Fable 5.1 xhigh", lines[1]);
    }

    /// <summary>The owner's first complaint about this line was "wide spaces"; a new field must not bring them back.</summary>
    [Fact]
    public void NoRowIsPaddedWithASpaceRun()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(3, 4),
            [
                Member("solo-1", Briefed(), Reading(52), Fable("xhigh")),
                Member("imp-1", [], model: SessionModelReading_Factory.Create("Opus 5", null)),
            ],
            "gate cleared on 34e5515",
            NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), supervisorContext: Reading(41), supervisorModel: Fable("xhigh"));

        Assert.DoesNotContain("  ", line);
    }

    /// <summary>
    /// The planner is the engine's only way in, so the reading must survive the trip through it —
    /// a builder parameter the planner does not thread is a field the owner never sees.
    /// </summary>
    [Fact]
    public void ThePlannerThreadsTheSupervisorsModelThrough()
    {
        var plan = TopicStatusLine_Planner.Plan(
            Progress(72, 113),
            [Member("imp-1", Briefed(), model: Fable("xhigh"))],
            NOW,
            existingMessageId: null,
            lastWrittenText: null,
            TelegramDeliveryModes.Normal,
            lastFailedAttemptAt: null,
            backoffSeconds: 30,
            newestTopicMessage: null,
            repostIsImpossible: false,
            figuresUnchangedFor: null,
            supervisorContext: null,
            supervisorModel: Fable("xhigh"));

        var lines = plan.Text.Split('\n');

        Assert.Equal(TopicStatusActions.Post, plan.Action);
        Assert.Equal("PULSE · 72/113 · 63% · sup Fable 5.1 xhigh", lines[0]);
        Assert.Equal("• imp-1 · wiring the context field · 30 min · Fable 5.1 xhigh", lines[1]);
    }

    /// <summary>
    /// A `FROM supervisor` brief — the real shape for an implementer, and a fixture convenience for
    /// the solo case, exactly as ContextOnTheStatusLineTests explains. Same stamp, so the same
    /// "30 min" lands on every row here.
    /// </summary>
    static IReadOnlyList<IChannelEntry> Briefed()
    {
        return ChannelEntry_Parser.Parse_All(
            "## [1] FROM supervisor — 2026-08-21 20:00 — wiring the context field\n\ngo\n");
    }

    static IPlanProgress Progress(int done, int total)
    {
        return PlanProgress_Factory.Create(done, 0, 0, 0, total, null, [], [], []);
    }

    static ITopicStatusMember Member(
        string memberId,
        IReadOnlyList<IChannelEntry> entries,
        ISessionContextUsage? context = null,
        ISessionModelReading? model = null)
    {
        return TopicStatusMember_Factory.Create(memberId, entries, isClosed: false, context, model);
    }

    static ISessionContextUsage Reading(double percent)
    {
        return SessionContextUsage_Factory.Create(percent, PROBED);
    }

    static ISessionModelReading Fable(string? effort)
    {
        return SessionModelReading_Factory.Create("Fable 5.1", effort);
    }
}

using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
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
/// model is a FACT about the row that is either known or not. The fixtures here are
/// ContextOnTheStatusLineTests', so the two surfaces are pinned against the same rows.
///
/// <para>
/// PORTED 2026-09-14 FROM MASTER'S ONE-LINE PULSE, under plan 03 ruling R10. Master wrote this file
/// (7f658e0) against a builder whose lead line carried the ledger figures and the supervisor's
/// readings — `PULSE · 72/113 · 63% · sup Fable 5.1 xhigh` — and whose member rows had no state word.
/// This tree's PULSE is the fork's six-field layout, which TopicStatusLineBuilderTests and
/// ContextOnTheStatusLineTests pin: a bare header, a `sup · …` row, member rows carrying their state
/// word, a `merged` row and the `updated` heartbeat. The builder was not bent back to master's text;
/// every case keeps its intent and its expected string is re-rendered in this layout.
/// </para>
/// <para>
/// THE SUPERVISOR'S READING RIDES ITS `sup` ROW, not the lead word, for the reason master put it on
/// the lead line: that was the supervisor's only line, because this message lists members and a
/// crew's supervisor is not one. Field 2 gave it a line of its own, and the context figure moved there
/// first (ContextOnTheStatusLineTests.TheSupervisorsOwnWindowRidesOnItsOwnSupRow).
/// </para>
/// <para>
/// EVERY CALL PASSES CLASSIC'S FIELD LIST: `modelEffort` is legal but not in the catalogue's shipped
/// list, and classic — master's phone — is the preset that asks for it. PulseSettingsJsonTests pins that
/// classic resolves to these five words.
/// </para>
/// <para>
/// CLASSIC LEADS WITH THE COMPACT COUNT SINCE 2026-09-23 (plan 03 Task 16): `progress` replaced `merged`
/// and moved above the header, so every whole-message expectation opens `N/M (P%)` and every member row
/// sits one line lower than it did — the row itself is unchanged, which is this file's subject.
/// </para>
/// </summary>
public class ModelOnTheStatusLineTests
{
    static readonly DateTime NOW = new(2026, 8, 21, 20, 30, 0);
    static readonly DateTime PROBED = new(2026, 8, 21, 20, 29, 0, DateTimeKind.Utc);

    /// <summary>kit/presets/classic.json's `pulse.fields`, spelled out for the reason PresetProbeTests gives.</summary>
    static readonly IReadOnlyList<string> CLASSIC =
    [
        PulseField_Names.PROGRESS, PulseField_Names.SUPERVISOR, PulseField_Names.MEMBERS, PulseField_Names.MODEL_EFFORT, PulseField_Names.UPDATED,
    ];

    /// <summary>The reading rides after the duration — the row's state word, which this layout adds, sits before it.</summary>
    [Fact]
    public void AMembersModelAndEffortRideAfterItsDuration()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", Briefed(), model: Fable("xhigh"))], null, NOW,
            aMessageIsAlreadyPosted: false, pulseFields: CLASSIC);

        Assert.Equal("• imp-1 · wiring the context field · working · 30 min · Fable 5.1 xhigh", line.Split('\n')[2]);
    }

    /// <summary>A quiet member is still running SOMETHING, and the row says what.</summary>
    [Fact]
    public void AMemberStandingByStillNamesItsModel()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", [], model: Fable("xhigh"))], null, NOW, aMessageIsAlreadyPosted: false, pulseFields: CLASSIC);

        Assert.Equal("• imp-1 · standing by · Fable 5.1 xhigh", line.Split('\n')[2]);
    }

    /// <summary>
    /// The field ORDER on a member row, with everything present: who, what, which state, how long, on
    /// what, how full. The model sits between the duration and the context figure — the facts about the
    /// row first, the alarm last, where a glance lands.
    /// </summary>
    [Fact]
    public void TheModelSitsBetweenTheDurationAndTheContextField()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("solo-1", Briefed(), Reading(52), Fable("xhigh"))], null, NOW,
            aMessageIsAlreadyPosted: false, pulseFields: CLASSIC);

        Assert.Equal("• solo-1 · wiring the context field · working · 30 min · Fable 5.1 xhigh · ctx 52%", line.Split('\n')[2]);
    }

    /// <summary>An older Claude Code reports no dial. Just the model, nothing trailing.</summary>
    [Fact]
    public void AnEffortlessReadingIsJustTheModel()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", Briefed(), model: SessionModelReading_Factory.Create("Opus 5", null))], null, NOW,
            aMessageIsAlreadyPosted: false, pulseFields: CLASSIC);

        Assert.Equal("• imp-1 · wiring the context field · working · 30 min · Opus 5", line.Split('\n')[2]);
    }

    /// <summary>
    /// A member that has not reported renders EXACTLY as it did before this field existed — asserted
    /// as the whole row, because a Contains check cannot see a trailing separator or placeholder.
    /// TopicStatusLineBuilderTests.TheApprovedShape pins the same row shape across a whole message.
    /// </summary>
    [Fact]
    public void AMemberWithNoReadingRendersExactlyAsBefore()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", Briefed())], null, NOW, aMessageIsAlreadyPosted: false, pulseFields: CLASSIC);

        Assert.Equal("• imp-1 · wiring the context field · working · 30 min", line.Split('\n')[2]);
    }

    /// <summary>
    /// WAS TheSupervisorsModelRidesOnTheLeadLine. The supervisor's reading rides the supervisor's own
    /// line — the `sup` row in this layout, as the class docstring records — and the ledger figures keep
    /// theirs.
    /// </summary>
    [Fact]
    public void TheSupervisorsModelRidesOnItsSupRow()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(72, 113), [], null, NOW, aMessageIsAlreadyPosted: false, pulseFields: CLASSIC,
            supervisorModel: Fable("xhigh"));

        Assert.Equal("72/113 (63%)\nPULSE\nsup · Fable 5.1 xhigh\nupdated 20:30", line);
    }

    /// <summary>
    /// WAS TheLeadLineFieldOrderWithEverythingPresent, then TheSupAndMergedRowFieldOrderWithEverythingPresent
    /// until classic swapped `merged` for `progress` (2026-09-23). The field ORDER with all three optional
    /// fields present: the ledger reading stays together (count, percent, and since plan 03 task 19 the
    /// "unchanged" clause the owner asked back for, entry [100]) on its own line — the compact one, with no
    /// "merged" label by the owner's "just 1/23 (4%)" — and the supervisor's row
    /// carries its model then its context: the same facts-then-alarm order as a member row, with `ctx`
    /// keeping its place at the very end so ContextOnTheStatusLineTests' expectations still hold.
    /// </summary>
    [Fact]
    public void TheSupRowAndProgressFieldOrderWithEverythingPresent()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false, pulseFields: CLASSIC,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), supervisorContext: Reading(41), supervisorModel: Fable("xhigh"));

        Assert.Equal("3/4 (75%) · unchanged 25 min\nPULSE\nsup · Fable 5.1 xhigh · ctx 41%\nupdated 20:30", line);
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
                null, [], null, NOW, aMessageIsAlreadyPosted: false, pulseFields: CLASSIC, supervisorModel: Fable("xhigh")));
    }

    /// <summary>
    /// With no ledger the lead word stays BARE: nothing hangs off the header in this layout, ledger or
    /// not, and no merged row is drawn. The member rows still carry their readings.
    ///
    /// <para>
    /// HALF OF MASTER'S CASE DOES NOT PORT, and the assertion says so rather than hiding it. Master
    /// dropped the supervisor's model along with its lead-line figures, because a field hung off the bare
    /// lead word was its "nothing to say" shape. Here the supervisor's reading is not on the lead word at
    /// all: the `sup` row is drawn whenever the message has substance, exactly as `sup · ctx` already
    /// was — so it is present, and pinned as present.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBareLeadWordStaysBareWithoutALedger()
    {
        var line = TopicStatusLine_Builder.Build(
            null, [Member("imp-1", Briefed(), model: Fable("xhigh"))], null, NOW, aMessageIsAlreadyPosted: false, pulseFields: CLASSIC,
            supervisorModel: Fable("xhigh"));

        Assert.Equal(
            "PULSE\nsup · Fable 5.1 xhigh\n• imp-1 · wiring the context field · working · 30 min · Fable 5.1 xhigh\nupdated 20:30",
            line);
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
            new TopicLastEvent("gate cleared on 34e5515", null),
            NOW, aMessageIsAlreadyPosted: false, pulseFields: CLASSIC,
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
            supervisorModel: Fable("xhigh"),
            pulseFields: CLASSIC);

        var lines = plan.Text.Split('\n');

        Assert.Equal(TopicStatusActions.Post, plan.Action);
        Assert.Equal("72/113 (63%)", lines[0]);
        Assert.Equal("PULSE", lines[1]);
        Assert.Equal("sup · Fable 5.1 xhigh", lines[2]);
        Assert.Equal("• imp-1 · wiring the context field · working · 30 min · Fable 5.1 xhigh", lines[3]);
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

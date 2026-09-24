using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.PulseSettings;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using AIOrchestratorCoreLib.Status.SessionModelReading;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// PULSE DRAWS THE FIELDS THE OWNER LISTS, IN THE ORDER THEY LIST THEM (plan 03, spec §7.3):
/// <c>pulse.fields</c> and <c>pulse.stepMinutes</c>, plus the <c>modelEffort</c> field master drew
/// and the fork's six-field builder never read.
///
/// <para>
/// THE EXPECTED LISTS ARE SPELLED OUT, not read back from the catalogue or a preset file, for the
/// reason <c>PresetProbeTests</c> gives: deriving them would assert that a file equals itself.
/// <c>PulseSettingsJsonTests</c> pins that classic and the catalogue resolve to these same words.
/// </para>
/// <para>
/// ONE FIXTURE CARRIES SOMETHING FOR EVERY FIELD — an ask, a declared supervisor, a working and a
/// standing-by member, a closed one, a last event, a ledger that has stood still, and a model reading
/// on every session — so a list that leaves a field out is visibly leaving something out, rather than
/// omitting a field that had nothing to say anyway.
/// </para>
/// </summary>
public class PulseFieldsAreConfigurableTests
{
    static readonly DateTime NOW = new(2026, 8, 12, 12, 30, 0);
    static readonly DateTime PROBED = new(2026, 8, 12, 12, 29, 0, DateTimeKind.Utc);

    /// <summary>The catalogue's shipped seven (spec §6.4) — the fork's PULSE, and quiet's.</summary>
    static readonly IReadOnlyList<string> SHIPPED =
    [
        PulseField_Names.WAITING_ON_YOU, PulseField_Names.SUPERVISOR, PulseField_Names.MEMBERS, PulseField_Names.CLOSED_COUNT,
        PulseField_Names.LAST_EVENT, PulseField_Names.MERGED, PulseField_Names.UPDATED,
    ];

    /// <summary>
    /// kit/presets/classic.json's list — master's pulse, rendered through the same field builders, with
    /// the compact <c>progress</c> reading in place of <c>merged</c> and at the top (owner, 2026-09-23).
    /// </summary>
    static readonly IReadOnlyList<string> CLASSIC =
    [
        PulseField_Names.PROGRESS, PulseField_Names.SUPERVISOR, PulseField_Names.MEMBERS, PulseField_Names.MODEL_EFFORT, PulseField_Names.UPDATED,
    ];

    /// <summary>
    /// THE LINE THE FIXTURE PRODUCED BEFORE THE LIST EXISTED — measured against the builder at
    /// <c>cfddd6b</c>, with the model readings already in the fixture and not drawn.
    /// </summary>
    const string TODAYS_LINE =
        "PULSE\n" +
        "⏳ waiting on you · ledger: your browser pass on FIN-D-277\n" +
        "sup · waiting for the review · declared 12:12 · ctx 41%\n" +
        "• imp-1 · committing the marker fix · working · 10 min\n" +
        "• rev-2 · standing by\n" +
        "1 closed\n" +
        "last · 12:20 · gate cleared on 34e5515\n" +
        "72/113 merged · 63 % · unchanged 25 min\n" +
        "updated 12:30";

    /// <summary>
    /// THE REGRESSION GUARD. Null is the shipped list, so the thirty-odd existing callers keep today's
    /// line without naming one; and the shipped list spelled out draws the same thing — which is what
    /// says the walk over the list, not a leftover fixed sequence, produced it. The model readings in
    /// the fixture stay OFF the line: <c>modelEffort</c> is legal and not shipped.
    /// </summary>
    [Fact]
    public void TheShippedList_ProducesExactlyTodaysLine()
    {
        Assert.Equal(TODAYS_LINE, Build_Rich(pulseFields: null));
        Assert.Equal(TODAYS_LINE, Build_Rich(pulseFields: SHIPPED));
    }

    /// <summary>
    /// CLASSIC'S PHONE: the task count FIRST, no waiting-on-you row, no closed count, no `last`, and
    /// every session's model and effort — the supervisor's on its own row, each member's after its
    /// duration. The model is a FACT about the row and the context figure is an ALARM, so the fact comes
    /// first and the alarm keeps the end of the row, where a glance lands (master's
    /// ModelOnTheStatusLineTests). The count is `72/113 (63%)` with no label — owner, 2026-09-23:
    /// *"I don't want to have useless words like 1/23 merged 4%. Just 1/23 (4%)."* — and, since task 19,
    /// the "unchanged" clause the owner asked back for (entry [100]): classic states nothing for
    /// <c>pulse.unchangedFor</c>, so the shipped ON applies.
    /// </summary>
    [Fact]
    public void ClassicsList_LeadsWithTheCount_DropsWaitingOnYouAndClosedCount_AndAddsModelEffort()
    {
        Assert.Equal(
            "72/113 (63%) · unchanged 25 min\n" +
            "PULSE\n" +
            "sup · waiting for the review · declared 12:12 · Fable 5.1 xhigh · ctx 41%\n" +
            "• imp-1 · committing the marker fix · working · 10 min · Opus 5 high\n" +
            "• rev-2 · standing by · Sonnet 5\n" +
            "updated 12:30",
            Build_Rich(pulseFields: CLASSIC));
    }

    /// <summary>
    /// THE COMPACT READING, IN ITS LISTED PLACE — `72/113 (63%)`, with no label. With
    /// <c>pulse.unchangedFor</c> off there is no "unchanged" clause either, even though the fixture's
    /// ledger has stood still for 25 minutes (with it on, <c>TheCountInItsListedPlace_CarriesTheClauseToo</c>).
    /// The percent is <c>PlanProgress_Formatter.Percent</c>'s, which truncates: 75 of 76 reads 98%, never a
    /// rounded 99%, so this line and `/progress` cannot quote the ledger differently.
    /// </summary>
    [Fact]
    public void TheProgressField_ReadsJustTheCountAndThePercent_InItsListedPlace()
    {
        Assert.Equal(
            "PULSE\n" +
            "sup · waiting for the review · declared 12:12 · ctx 41%\n" +
            "72/113 (63%)\n" +
            "updated 12:30",
            Build_Rich(pulseFields: [PulseField_Names.SUPERVISOR, PulseField_Names.PROGRESS, PulseField_Names.UPDATED], unchangedFor: false));

        Assert.Equal(
            "PULSE\n75/76 (98%)",
            TopicStatusLine_Builder.Build(
                Progress(75, 76), [], null, NOW, aMessageIsAlreadyPosted: false,
                pulseFields: [PulseField_Names.MEMBERS, PulseField_Names.PROGRESS]));
    }

    /// <summary>
    /// FIRST IN THE LIST MEANS FIRST ON THE MESSAGE — above the header, mode glyphs and all. The first
    /// line is what a Telegram notification preview shows, and the owner called the count "the most
    /// important information". It is a LINE OF ITS OWN rather than a prefix spliced into the header, so
    /// every line keeps one owner: the header still reads `🌙 PULSE` exactly, and the count reads
    /// exactly what it reads anywhere else in the list.
    /// </summary>
    [Fact]
    public void WhenProgressLeadsTheList_TheCountIsTheFirstLineOfTheMessage_AboveTheHeader()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 12), [], null, NOW, aMessageIsAlreadyPosted: false,
            fields: new TopicStatusFields(Mode: TelegramDeliveryModes.Deferred),
            pulseFields: [PulseField_Names.PROGRESS, PulseField_Names.UPDATED]);

        Assert.StartsWith("1/12 (8%)\n", line, StringComparison.Ordinal);
        Assert.Equal("1/12 (8%)\n🌙 PULSE\nupdated 12:30", line);
    }

    /// <summary>
    /// NO LEDGER, NO COUNT — `0/0 (0%)` would be the say-nothing message again, the same guard as
    /// <c>merged</c>. And with nothing to draw above it, the header leads the message as it always did:
    /// "first" is a place the count takes when it has something to say, never a blank line it leaves.
    /// </summary>
    [Fact]
    public void AnEmptyLedger_DrawsNoCount_AndTheHeaderLeadsAgain()
    {
        var working = TopicStatusMember_Factory.Create("imp-1", Briefed("committing the marker fix", "2026-08-12 12:18"), isClosed: false);

        Assert.Equal(
            "PULSE\n• imp-1 · committing the marker fix · working · 10 min",
            TopicStatusLine_Builder.Build(
                Progress(0, 0), [working], null, NOW, aMessageIsAlreadyPosted: false,
                pulseFields: [PulseField_Names.PROGRESS, PulseField_Names.MEMBERS]));

        Assert.Equal(
            "PULSE\n• imp-1 · committing the marker fix · working · 10 min",
            TopicStatusLine_Builder.Build(
                null, [working], null, NOW, aMessageIsAlreadyPosted: false,
                pulseFields: [PulseField_Names.PROGRESS, PulseField_Names.MEMBERS]));
    }

    /// <summary>
    /// <c>merged</c> IS UNTOUCHED BY THE NEW WORD: an owner who lists both reads the compact count on top
    /// and the labelled reading, clause and all, where they put it. Two renderings of one ledger and ONE
    /// arithmetic under both — the percents agree because they are the same call. Since task 19 the count
    /// carries the clause too, built once for both, so the two cannot disagree about it either.
    /// </summary>
    [Fact]
    public void TheMergedField_IsUnchanged_BesideTheProgressField()
    {
        Assert.Equal(
            "72/113 (63%) · unchanged 25 min\n" +
            "PULSE\n" +
            "72/113 merged · 63 % · unchanged 25 min",
            Build_Rich(pulseFields: [PulseField_Names.PROGRESS, PulseField_Names.MERGED]));
    }

    /// <summary>
    /// THE COUNT ON TOP DOES NOT MAKE A STILL ORCHESTRATION READ AS NEWS. The repost rule compares the
    /// line with its heartbeat stripped, and <c>Strip_Heartbeat</c> matches whole lines by their opening:
    /// the count opens with a digit, so it is never taken for the heartbeat and never stripped with it.
    /// With <c>pulse.unchangedFor</c> OFF the field carries no clause, so a ledger that stands still for
    /// another step leaves its text identical. With it ON (task 19, the shipped value and classic's) the
    /// clause moves at each step exactly as <c>merged</c>'s always has — that is the reading the owner
    /// asked back for, and a step is then news to the repost gate.
    /// </summary>
    [Fact]
    public void TheCountOnTop_IsNotTheHeartbeat_AndAStepLaterIsNotNews()
    {
        IReadOnlyList<string> countFirst = [PulseField_Names.PROGRESS, PulseField_Names.UPDATED];

        var before = TopicStatusLine_Builder.Build(
            Progress(1, 12), [], null, NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), pulseFields: countFirst, unchangedFor: false);
        var aStepLater = TopicStatusLine_Builder.Build(
            Progress(1, 12), [], null, NOW.AddMinutes(5), aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(30), pulseFields: countFirst, unchangedFor: false);

        Assert.NotEqual(before, aStepLater);
        Assert.Equal("1/12 (8%)\nPULSE", TopicStatusLine_Builder.Strip_Heartbeat(before));
        Assert.Equal(TopicStatusLine_Builder.Strip_Heartbeat(before), TopicStatusLine_Builder.Strip_Heartbeat(aStepLater));

        var onBefore = TopicStatusLine_Builder.Build(
            Progress(1, 12), [], null, NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), pulseFields: countFirst, unchangedFor: true);
        var onAStepLater = TopicStatusLine_Builder.Build(
            Progress(1, 12), [], null, NOW.AddMinutes(5), aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(30), pulseFields: countFirst, unchangedFor: true);

        Assert.Equal("1/12 (8%) · unchanged 25 min\nPULSE", TopicStatusLine_Builder.Strip_Heartbeat(onBefore));
        Assert.Equal("1/12 (8%) · unchanged 30 min\nPULSE", TopicStatusLine_Builder.Strip_Heartbeat(onAStepLater));
    }

    /// <summary>
    /// A MEMBER ROW WITH BOTH READINGS: who, what, which state, how long, on what, how full — the
    /// model between the duration and the context alarm. A solo carries its context figure always, so
    /// it is the member that can show the pair.
    /// </summary>
    [Fact]
    public void TheModelSitsBetweenTheDurationAndTheContextAlarm_OnEveryRowThatCarriesBoth()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5),
            [TopicStatusMember_Factory.Create("solo-1", Briefed("wiring the context field", "2026-08-12 12:00"), isClosed: false, Reading(52), Fable())],
            null, NOW, aMessageIsAlreadyPosted: false,
            supervisorContext: Reading(41),
            supervisorModel: Fable(),
            pulseFields: [PulseField_Names.SUPERVISOR, PulseField_Names.MEMBERS, PulseField_Names.MODEL_EFFORT]);

        Assert.Equal(
            "PULSE\nsup · Fable 5.1 xhigh · ctx 41%\n• solo-1 · wiring the context field · working · 30 min · Fable 5.1 xhigh · ctx 52%",
            line);
    }

    /// <summary>
    /// DECIDED: A REPEAT IS REFUSED, AND IN EXACTLY ONE PLACE. <c>SettingValidators.PULSE_FIELDS</c>
    /// refuses a list that names a field twice, so that layer is skipped whole and the resolver falls
    /// to the one below — here the catalogue's shipped list, because no preset is named. That is the
    /// validator's reach, and it is why the engine can never hand the builder a repeat.
    ///
    /// <para>
    /// THE BUILDER DOES NOT REFUSE IT A SECOND TIME. Handed a repeat directly, it draws what it was
    /// handed — twice. De-duplicating there would be a second copy of the validator's rule (CLAUDE.md
    /// decision 12), free to disagree with the first the day one of them changes; this half pins that
    /// the copy does not exist.
    /// </para>
    /// </summary>
    [Fact]
    public void AFieldNamedTwice_IsRefusedByTheValidator_AndTheBuilderHoldsNoSecondCopyOfTheRule()
    {
        var resolved = PulseSettings_Json.Parse(
            JsonNode.Parse("""{"pulse":{"fields":["merged","merged"]}}""")!.AsObject(), presetTree: null);

        Assert.Equal(SHIPPED, resolved.Fields);

        Assert.Equal(
            "PULSE\n72/113 merged · 63 %\n72/113 merged · 63 %",
            TopicStatusLine_Builder.Build(
                Progress(72, 113), [], null, NOW, aMessageIsAlreadyPosted: false,
                pulseFields: [PulseField_Names.MERGED, PulseField_Names.MERGED]));
    }

    /// <summary>
    /// A PULSE WITH NO FIELDS IS THE OWNER'S TO CHOOSE (IPulseSettings.Fields), and what it draws is
    /// the header: the lead word is the line's name and the mode glyphs' home, not a field.
    /// </summary>
    [Fact]
    public void AnEmptyFieldList_LeavesTheHeaderAndNothingElse()
    {
        Assert.Equal("PULSE", Build_Rich(pulseFields: []));
    }

    [Fact]
    public void TheFieldsAreDrawnInTheListedOrder_NotTheBuildersOrder()
    {
        Assert.Equal(
            "PULSE\n" +
            "updated 12:30\n" +
            "72/113 merged · 63 % · unchanged 25 min\n" +
            "• imp-1 · committing the marker fix · working · 10 min\n" +
            "• rev-2 · standing by\n" +
            "sup · waiting for the review · declared 12:12 · ctx 41%",
            Build_Rich(pulseFields: [PulseField_Names.UPDATED, PulseField_Names.MERGED, PulseField_Names.MEMBERS, PulseField_Names.SUPERVISOR]));
    }

    /// <summary>
    /// SUBSTANCE IS A FACT ABOUT THE ORCHESTRATION, NOT ABOUT THE LIST. Nothing to say stays silence
    /// (or the bare lead word over a posted message) with every field listed; and something to say in
    /// a field the list leaves out still writes the message — the owner who dropped `last` chose not
    /// to read it, not to be told nothing when it is the only thing that moved.
    /// </summary>
    [Fact]
    public void HasSubstance_StillGatesTheWholeMessage_WhateverTheList()
    {
        Assert.Equal(
            "",
            TopicStatusLine_Builder.Build(
                null, [], null, NOW, aMessageIsAlreadyPosted: false,
                supervisorContext: Reading(41), supervisorModel: Fable(), pulseFields: PulseField_Names.ALL));

        Assert.Equal(
            "PULSE",
            TopicStatusLine_Builder.Build(
                null, [], null, NOW, aMessageIsAlreadyPosted: true, pulseFields: PulseField_Names.ALL));

        Assert.Equal(
            "PULSE\nupdated 12:30",
            TopicStatusLine_Builder.Build(
                null, [], new TopicLastEvent("gate cleared on 34e5515", null), NOW, aMessageIsAlreadyPosted: false,
                pulseFields: [PulseField_Names.UPDATED]));
    }

    /// <summary>
    /// ONE STEP, TWO READERS. The member row's duration and the heartbeat both round to the same
    /// granularity, and a build where they disagree is CLAUDE.md decision 12's second copy arriving in
    /// the one file that has already paid for it (SessionRows_Builder's duplicate duration wording).
    ///
    /// <para>
    /// THE THIRD READER RIDES WITH THEM: the merged row's "unchanged" clause is the other ticking figure
    /// on the line, and a line whose three clocks step differently changes text at the finest of them.
    /// Forty-one minutes in, at 12:59, the three steps give three different answers — and inside each
    /// answer all three readers agree.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStep_MovesTheMemberDurationAndTheHeartbeatTogether()
    {
        var later = new DateTime(2026, 8, 12, 12, 59, 0);

        string At(int stepMinutes)
        {
            return TopicStatusLine_Builder.Build(
                Progress(72, 113),
                [TopicStatusMember_Factory.Create("imp-1", Briefed("committing the marker fix", "2026-08-12 12:18"), isClosed: false)],
                null, later, aMessageIsAlreadyPosted: false,
                figuresUnchangedFor: TimeSpan.FromMinutes(41),
                pulseFields: [PulseField_Names.MEMBERS, PulseField_Names.MERGED, PulseField_Names.UPDATED],
                stepMinutes: stepMinutes);
        }

        Assert.Equal(
            "PULSE\n• imp-1 · committing the marker fix · working · 40 min\n72/113 merged · 63 % · unchanged 40 min\nupdated 12:55",
            At(5));
        Assert.Equal(
            "PULSE\n• imp-1 · committing the marker fix · working · 30 min\n72/113 merged · 63 % · unchanged 30 min\nupdated 12:45",
            At(15));
        Assert.Equal(
            "PULSE\n• imp-1 · committing the marker fix · working · 41 min\n72/113 merged · 63 % · unchanged 41 min\nupdated 12:59",
            At(1));
    }

    /// <summary>
    /// THE REPOST RULE STAYS CONTENT-ONLY WHEREVER THE HEARTBEAT IS LISTED. <c>Strip_Heartbeat</c>
    /// used to strip only the LAST line, because the heartbeat was always drawn last; once the owner
    /// orders the fields it can be first, and a heartbeat that is not stripped makes every step boundary
    /// read as news — a buried line re-posted for the minute hand, the defect of 2026-09-10.
    /// </summary>
    [Fact]
    public void TheHeartbeatIsNotNews_WhereverTheListPutsIt()
    {
        IReadOnlyList<string> heartbeatFirst = [PulseField_Names.UPDATED, PulseField_Names.MERGED];

        var before = TopicStatusLine_Builder.Build(
            Progress(72, 113), [], null, NOW, aMessageIsAlreadyPosted: false, pulseFields: heartbeatFirst);
        var aStepLater = TopicStatusLine_Builder.Build(
            Progress(72, 113), [], null, NOW.AddMinutes(5), aMessageIsAlreadyPosted: false, pulseFields: heartbeatFirst);

        Assert.NotEqual(before, aStepLater);
        Assert.Equal("PULSE\n72/113 merged · 63 %", TopicStatusLine_Builder.Strip_Heartbeat(before));
        Assert.Equal(TopicStatusLine_Builder.Strip_Heartbeat(before), TopicStatusLine_Builder.Strip_Heartbeat(aStepLater));
    }

    /// <summary>
    /// THE PLANNER IS THE ENGINE'S ONLY WAY IN, so every value this task adds must survive the trip
    /// through it — a builder parameter the planner does not thread is a setting the owner never sees
    /// obeyed.
    /// </summary>
    [Fact]
    public void ThePlanner_ThreadsTheFieldListTheStepAndTheSupervisorsModelThrough()
    {
        var plan = TopicStatusLine_Planner.Plan(
            Progress(72, 113),
            [TopicStatusMember_Factory.Create("imp-1", Briefed("committing the marker fix", "2026-08-12 12:18"), isClosed: false, model: Fable())],
            new DateTime(2026, 8, 12, 12, 59, 0),
            existingMessageId: null,
            lastWrittenText: null,
            TelegramDeliveryModes.Normal,
            lastFailedAttemptAt: null,
            backoffSeconds: 30,
            newestTopicMessage: null,
            repostIsImpossible: false,
            supervisorModel: SessionModelReading_Factory.Create("Opus 5", "high"),
            pulseFields: [PulseField_Names.SUPERVISOR, PulseField_Names.MEMBERS, PulseField_Names.MODEL_EFFORT, PulseField_Names.UPDATED],
            stepMinutes: 15);

        Assert.Equal(TopicStatusActions.Post, plan.Action);
        Assert.Equal(
            "PULSE\nsup · Opus 5 high\n• imp-1 · committing the marker fix · working · 30 min · Fable 5.1 xhigh\nupdated 12:45",
            plan.Text);
    }

    // ── THE "UNCHANGED FOR" READING ON THE COMPACT COUNT (plan 03 task 19) ────────────────────────────
    //
    // Owner, 2026-09-24 (ai-orchestrator-29 entry [100]): "my brother removed the indication, in the
    // pulse message, of how long the progress and completion percentage have stayed identical in minutes.
    // It's useful to get an idea if the session is working or not." Task 16 gave classic the bare
    // `progress` count, and the clause went with the `merged` label the owner had called useless. It is
    // `pulse.unchangedFor` now: shipped on, and it rides whichever of the two fields is drawn.

    /// <summary>
    /// SILENT UNDER TEN MINUTES, THEN STEPPED — `UnchangedFor_Formatter`'s rule, the one `merged` has
    /// always followed, now on the count too, after `FIELD_SEPARATOR`. Listed first, the count is the
    /// line above the header, so the clause rides the first line of the message: "at the very top".
    /// </summary>
    [Theory]
    [InlineData(0, "1/12 (8%)")]
    [InlineData(9, "1/12 (8%)")]
    [InlineData(10, "1/12 (8%) · unchanged 10 min")]
    [InlineData(26, "1/12 (8%) · unchanged 25 min")]
    public void TheCountOnTop_SaysHowLongItHasStoodStill_AfterTenMinutes_Stepped(int minutes, string firstLine)
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 12), [], null, NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(minutes),
            pulseFields: [PulseField_Names.PROGRESS, PulseField_Names.UPDATED],
            unchangedFor: true);

        Assert.Equal($"{firstLine}\nPULSE\nupdated 12:30", line);
    }

    /// <summary>In its listed place the count carries the clause the same way — one switch, one reading.</summary>
    [Fact]
    public void TheCountInItsListedPlace_CarriesTheClauseToo()
    {
        Assert.Equal(
            "PULSE\n" +
            "sup · waiting for the review · declared 12:12 · ctx 41%\n" +
            "72/113 (63%) · unchanged 25 min",
            Build_Rich(pulseFields: [PulseField_Names.SUPERVISOR, PulseField_Names.PROGRESS], unchangedFor: true));
    }

    /// <summary>
    /// OFF DROPS IT FROM BOTH FIELDS — the fork's choice for quiet. `merged` keeps its label and its
    /// percent, and loses only the clause.
    /// </summary>
    [Fact]
    public void WithTheSettingOff_NeitherFieldSaysHowLongItHasStoodStill()
    {
        Assert.Equal(
            "72/113 (63%)\n" +
            "PULSE\n" +
            "72/113 merged · 63 %",
            Build_Rich(pulseFields: [PulseField_Names.PROGRESS, PulseField_Names.MERGED], unchangedFor: false));
    }

    /// <summary>
    /// NULL IS THE CATALOGUE'S SHIPPED VALUE, which is ON (ruling R14: today's behaviour for the shipped
    /// field list, where `merged` carries the clause) — so a caller that predates the setting keeps it.
    /// </summary>
    [Fact]
    public void NoSettingHandedIn_IsTheShippedValue_On()
    {
        Assert.Equal(
            "72/113 (63%) · unchanged 25 min\n" +
            "PULSE\n" +
            "72/113 merged · 63 % · unchanged 25 min",
            Build_Rich(pulseFields: [PulseField_Names.PROGRESS, PulseField_Names.MERGED]));
    }

    /// <summary>
    /// THROUGH THE REAL PRESETS: both resolve the setting on (neither states it — ruling R28, superseding
    /// R26). Classic's count on top says how long it has stood still, which is what Task 16 had lost; quiet's
    /// `merged` line says it as it always did — the fork never removed it. Resolved through the loader's own
    /// preset rung, so an edit to either file reaches this.
    /// </summary>
    [Fact]
    public void UnderEachPreset_TheProgressReadingSaysHowLongItHasStoodStill()
    {
        var classic = PulseSettings_Json.Parse(configRoot: null, Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC));
        var quiet = PulseSettings_Json.Parse(configRoot: null, Presets_Loader.Load_Embedded(Presets_Loader.QUIET));

        Assert.StartsWith(
            "72/113 (63%) · unchanged 25 min\n",
            Build_Rich(pulseFields: classic.Fields, unchangedFor: classic.UnchangedFor),
            StringComparison.Ordinal);

        var quietLine = Build_Rich(pulseFields: quiet.Fields, unchangedFor: quiet.UnchangedFor);

        Assert.Contains("72/113 merged · 63 % · unchanged 25 min", quietLine, StringComparison.Ordinal);
    }

    /// <summary>The planner is the engine's only way in, so the setting must survive the trip through it.</summary>
    [Fact]
    public void ThePlanner_ThreadsTheSettingThrough()
    {
        var on = TopicStatusLine_Planner.Plan(
            Progress(1, 12), [], NOW, existingMessageId: null, lastWrittenText: null, TelegramDeliveryModes.Normal,
            lastFailedAttemptAt: null, backoffSeconds: 30, newestTopicMessage: null, repostIsImpossible: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), pulseFields: [PulseField_Names.PROGRESS], unchangedFor: true);

        var off = TopicStatusLine_Planner.Plan(
            Progress(1, 12), [], NOW, existingMessageId: null, lastWrittenText: null, TelegramDeliveryModes.Normal,
            lastFailedAttemptAt: null, backoffSeconds: 30, newestTopicMessage: null, repostIsImpossible: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), pulseFields: [PulseField_Names.PROGRESS], unchangedFor: false);

        Assert.Equal("1/12 (8%) · unchanged 25 min\nPULSE", on.Text);
        Assert.Equal("1/12 (8%)\nPULSE", off.Text);
    }

    static string Build_Rich(IReadOnlyList<string>? pulseFields, bool? unchangedFor)
    {
        return TopicStatusLine_Builder.Build(
            EveryFieldHasSomethingToSay(), RichMembers(), new TopicLastEvent("gate cleared on 34e5515", new DateTime(2026, 8, 12, 12, 20, 0)), NOW,
            aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25),
            supervisorContext: Reading(41),
            fields: SupervisorDeclared(),
            supervisorModel: Fable(),
            pulseFields: pulseFields,
            unchangedFor: unchangedFor);
    }

    static string Build_Rich(IReadOnlyList<string>? pulseFields)
    {
        return TopicStatusLine_Builder.Build(
            EveryFieldHasSomethingToSay(), RichMembers(), new TopicLastEvent("gate cleared on 34e5515", new DateTime(2026, 8, 12, 12, 20, 0)), NOW,
            aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25),
            supervisorContext: Reading(41),
            fields: SupervisorDeclared(),
            supervisorModel: Fable(),
            pulseFields: pulseFields);
    }

    /// <summary>A ledger with a `[?]` line, so `waiting on you` has something to say.</summary>
    static IPlanProgress EveryFieldHasSomethingToSay()
    {
        return PlanProgress_Factory.Create(
            72, 0, 0, 0, 113, null, [], [], [], null, [new PlanLedgerLine("?", "your browser pass on FIN-D-277")]);
    }

    /// <summary>Working, standing by (with an effortless reading) and closed — every row kind the roster draws.</summary>
    static IReadOnlyList<ITopicStatusMember> RichMembers()
    {
        return
        [
            TopicStatusMember_Factory.Create(
                "imp-1", Briefed("committing the marker fix", "2026-08-12 12:18"), isClosed: false,
                model: SessionModelReading_Factory.Create("Opus 5", "high")),
            TopicStatusMember_Factory.Create(
                "rev-2", [Entry(1, ChannelAuthors.Reviewer, "STANDING BY", "2026-08-12 12:00")], isClosed: false,
                model: SessionModelReading_Factory.Create("Sonnet 5", null)),
            TopicStatusMember_Factory.Create("imp-3", [], isClosed: true),
        ];
    }

    static TopicStatusFields SupervisorDeclared()
    {
        return new TopicStatusFields(
            SupervisorDeclaredState: "waiting for the review",
            SupervisorDeclaredAt: new DateTime(2026, 8, 12, 12, 12, 0));
    }

    static IPlanProgress Progress(int done, int total)
    {
        return PlanProgress_Factory.Create(done, 0, 0, 0, total, null, [], [], []);
    }

    static IReadOnlyList<IChannelEntry> Briefed(string subject, string stamp)
    {
        return [Entry(1, ChannelAuthors.Supervisor, subject, stamp)];
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string stamp)
    {
        return ChannelEntry_Factory.Create(
            index, author, stamp, subject, "body",
            $"## [{index}] FROM {author} — {stamp} — {subject}\nbody");
    }

    static ISessionContextUsage Reading(double percent)
    {
        return SessionContextUsage_Factory.Create(percent, PROBED);
    }

    static ISessionModelReading Fable()
    {
        return SessionModelReading_Factory.Create("Fable 5.1", "xhigh");
    }
}

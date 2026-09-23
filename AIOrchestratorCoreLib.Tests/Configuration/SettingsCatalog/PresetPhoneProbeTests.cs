using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Bridge;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE PHASE-3 GATE (spec §10, plan 03 Task 12): the preset probes reach the PHONE. Plan 02's
/// <see cref="PresetProbeTests"/> measures the RESOLVED VALUE of every catalogue entry under each preset
/// and says plan 03 extends it; this is the extension. Each fact drives the REAL engine with a fake
/// Telegram client that keeps ONE ordered timeline — every send with its sound and its buttons, every
/// edit, every reaction, every topic creation with its colour, every rename, every tap answer — and
/// asserts what the owner's phone shows, in what order, and which of it rang.
///
/// <para>
/// SPELLED OUT, NEVER DERIVED FROM THE PRESET FILES. Spec §12 names <i>"`quiet` must reproduce the
/// fork's phone exactly"</i> as a top risk, and a risk is not mitigated by a tautology: an expectation
/// computed from <c>classic.json</c> would stay green through any edit of <c>classic.json</c>. Every
/// label, glyph, emoji and sound below is a literal, measured on this tree on 2026-09-23 and checked
/// against the ruling or owner request that asked for it.
/// </para>
/// <para>
/// THE CONVERSATION (one per preset) is the brief's: an owner message → a supervisor narration entry
/// → the supervisor's answer → a question with options → a tap. The narration is a <c>WAITING ON …</c>
/// status line, the real shape of "narration while the owner waits" (CLAUDE.md decision 25): under
/// <c>filtered</c> it is held and then forgotten when the answer goes (ruling R7), under
/// <c>everything</c> it is sent silently (D7 b). The question declares <c>RISK: high</c> with a
/// deadline and a default, so both halves of Task 15 are on the phone at once: classic decides it with
/// ONE tap and no code, quiet asks for the read-back code, and under BOTH it says DENIED on timeout —
/// never "option 1 is taken" (ruling R21).
/// </para>
/// <para>
/// THE TOPIC (one per preset) is what the owner sees in the topic list and on PULSE: the colour the
/// topic is created with (Task 14), PULSE's first line and its bar (Tasks 16 and 5 — the count on top
/// and no hold toggle under classic), General's dashboard bar (D4), and where the mode glyph goes when
/// the topic is silenced and then marked /done (Task 7, ruling R23: ✅ replaces the delivery glyph on
/// the name).
/// </para>
/// <para>
/// WHAT THIS FILE DOES NOT REACH, said plainly. The PERIODIC STATUS (Task 8, D1 a): the engine hands
/// its sweep <c>DateTime.Now</c>, not the injected clock, so a half-hour boundary cannot be crossed by
/// an engine test; the preset half is pinned by <c>PeriodicStatusSweepTests.UnderClassic_ThePeriodicStatusIsPostedOnItsSlot_WhenTheStatusChanged</c>
/// and <c>UnderQuiet_NoPeriodicStatusIsEverSent</c>, which drive the real sweep with the phone block
/// resolved from the real presets through the real loader. PULSE's CADENCE is kept out of the
/// conversation timeline and has facts of its own (Task 17, "PULSE's cadence" below): until 2026-09-23
/// the engine handed the planner a render key where the planner compared raw text, so PULSE was
/// re-edited on every tick and reposted after every burst even when its content did not change (on
/// master since the fork's 2143db8; plan 03 report §5.3).
/// </para>
/// <para>
/// THE RUNNERS ARE PINNED TO <c>terminal</c> in config.json, outranking quiet's print/stream rows: a
/// registered print session makes the engine's dispatcher reach for a LIVE <c>claude</c> (global
/// constraints). Nothing on the phone depends on the runner; the fixture refuses to run if the pin did
/// not take.
/// </para>
/// </summary>
public class PresetPhoneProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445599;
    const long OWNER_USER_ID = 555000199;
    const long TOPIC_ID = 7373;
    const long OWNER_MESSAGE_ID = 1501;

    /// <summary>
    /// A FINISHED message (it ends in '?'), so the window it waits is <c>phone.finishedMessageSeconds</c>:
    /// the fork's 2 s discount under quiet, the owner's 6 s with no discount under classic (Task 13).
    /// </summary>
    const string OWNER_TEXT = "is the rebuild done?";

    const string NARRATION_SUBJECT = "WAITING ON the integration suite";
    const string NARRATION_TEXT = "Running the integration suite against the rebuilt images now.";
    const string ANSWER_TEXT = "Answering you: the rebuild finished clean on the merged tree.";
    const string QUESTION_PROSE = "Two plans can carry the tail-risk methods, and Advanced is the cheaper one.";
    const string QUESTION_TEXT = "Which plan gets the tail-risk methods?";
    const string CHOSEN_OPTION = "Advanced";

    /// <summary>The five contract lines plus a deadline and a default, QUESTION last but for its companions.</summary>
    const string QUESTION_ENTRY =
        QUESTION_PROSE + "\nRECOMMEND: Advanced, the cheaper one.\nRISK: high\nROW: none\n"
        + "QUESTION: " + QUESTION_TEXT + "\nOPTION: " + CHOSEN_OPTION + "\nOPTION: Ultimate\nDEADLINE: 60m\nDEFAULT: 1";

    const string REPO_NAME = "alpha";
    const string DISPLAY_NAME = "crm bug";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingLog_Fake _log = new();
    readonly PhoneTimelineTelegram_Fake _telegram = new(TOPIC_ID);

    // NOT READONLY for one reason: each fact names its preset, and Use_Preset builds these once, before
    // the engine has ever run. Nothing else assigns them.
    IOrchestrationLauncher? _launcher;
    IBridgeEngine? _engine;

    public PresetPhoneProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-preset-phone-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------------------------------------
    // The conversation
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// CLASSIC — MANU'S PHONE, and a machine that names no preset. The receipt is a SILENT ✓ message
    /// carrying ⏸ Wait and ▶ Send now (Task 6, 6b, ruling R2 — the hold toggle is not on PULSE), which
    /// becomes ✓✓ no sooner than six seconds later (Task 13: 6 s, no discount for a finished message).
    /// The narration is never sent at all. The answer, the question's prose and the question ring. The
    /// question carries no 🔐, says DENIED on timeout, and one tap decides it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_TheConversationReachesThePhone_AsMastersPhone()
    {
        var (timeline, mark, channelText) = await Run_Conversation_Async(Presets_Loader.CLASSIC);
        var events = timeline.Skip(mark).ToList();

        Assert.Equal(
            [
                "send [Silent] ✓ {⏸ Wait | ▶ Send now}",
                "edit ✓ → ✓✓",
                "send [Rings] answer",
                "send [Rings] question prose",
                "send [Rings] question {Advanced | Ultimate | 💬 Let's talk}",
                "tap answered: ✓",
                "edit question → ✅ Advanced",
            ],
            Project_Conversation(timeline, mark));

        // THE OWNER'S SIX SECONDS (Task 13). A lower bound only, because a loaded machine can make a
        // delivery late and never early; the fork's 2-second discount would put this near 2.
        var tickSent = Single_Event(events, e => e.Kind == PhoneEventKinds.Sent && e.Text == "✓").AtUtc;
        var doubleTick = Single_Event(events, e => e.Kind == PhoneEventKinds.Edited && e.Text == "✓✓").AtUtc;

        Assert.True(
            doubleTick - tickSent >= TimeSpan.FromSeconds(5),
            $"the ✓ became ✓✓ after {(doubleTick - tickSent).TotalSeconds:0.0} s — classic's window is 6 s, with no discount for a finished message.");

        // NO CODE, STILL A TAP, NEVER A DEFAULT (Task 15, ruling R21).
        var question = Question_Text(events);

        Assert.DoesNotContain("🔐", question, StringComparison.Ordinal);
        Assert.Contains("this is DENIED (timeout)", question, StringComparison.Ordinal);
        Assert.DoesNotContain("is taken", question, StringComparison.Ordinal);
        Assert.True(_log.Has_Info_Containing("high-risk confirmation is off"), _log.Dump());

        Assert.DoesNotContain(events, e => e.Text.Contains(NARRATION_TEXT, StringComparison.Ordinal));
        Assert.Matches(OWNER_ANSWER_IN_CHANNEL, channelText);

        Assert_TheNameAskedAndThenStoppedAsking(events);
    }

    /// <summary>
    /// QUIET — NATHAN'S PHONE. The receipt is a 👀 reaction on the owner's own message and 👌 when it is
    /// delivered — no ✓, so no buttons. Everything the supervisor writes is sent; only the answer and the
    /// question ring (D7 b), and the third unanswered message puts the topic in 🤐 quiet, silently — the
    /// narration classic never sends is what reaches that count here. The high-risk question asks for the
    /// read-back code, and only the code delivers the choice.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderQuiet_TheConversationReachesThePhone_AsTheForksPhone()
    {
        var (timeline, mark, channelText) = await Run_Conversation_Async(Presets_Loader.QUIET);
        var events = timeline.Skip(mark).ToList();

        Assert.Equal(
            [
                "react owner message 👀",
                "react owner message 👌",
                "send [Silent] narration",
                "send [Rings] answer",
                "send [Rings] question prose",
                "send [Silent] 🤐 going quiet",
                "send [Rings] question {Advanced | Ultimate | 💬 Let's talk}",
                "tap answered: 🔐 type the code shown",
                "edit question → 🔐 read-back code",
                "edit question → ✅ Advanced",
            ],
            Project_Conversation(timeline, mark));

        // A WINDOW, AND A SHORT ONE: the fork's 2 s for a finished message. Lower bound only (see classic).
        var seen = Single_Event(events, e => e.Kind == PhoneEventKinds.Reacted && e.Text == "👀").AtUtc;
        var delivered = Single_Event(events, e => e.Kind == PhoneEventKinds.Reacted && e.Text == "👌").AtUtc;

        Assert.True(delivered - seen >= TimeSpan.FromSeconds(1.5), $"delivered {(delivered - seen).TotalSeconds:0.0} s after 👀 — quiet still waits its 2 s.");

        var question = Question_Text(events);

        Assert.Contains("🔐 High risk", question, StringComparison.Ordinal);
        Assert.Contains("this is DENIED (timeout)", question, StringComparison.Ordinal);

        // The code is shown in one Telegram message and never reaches the channel an agent reads.
        var code = Read_Back_Code_OrNull(events) ?? throw new Exception("unreachable — the projection above has the read-back");

        Assert.DoesNotContain(code, channelText, StringComparison.Ordinal);
        Assert.Matches(OWNER_ANSWER_IN_CHANNEL, channelText);

        Assert_TheNameAskedAndThenStoppedAsking(events);
    }

    // ---------------------------------------------------------------------------------------------
    // The topic
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// CLASSIC'S TOPIC: created in Telegram's default colour and none assigned (Task 14); PULSE opens with
    /// the bare count and carries the owner's eight verbs, with no ⏸ Wait (Tasks 16, 5); General's
    /// dashboard carries no buttons (D4); a silenced topic's NAME carries 🔕, and /done replaces it with
    /// ✅ rather than stacking <c>🔕 ✅</c> (Task 7, ruling R23). PULSE's header never draws a mode glyph.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_TheTopicLooksLikeMastersTopic()
    {
        var events = await Run_Topic_Async(Presets_Loader.CLASSIC);

        Assert.Equal($"{REPO_NAME}-1 colour=none", Single_Event(events, e => e.Kind == PhoneEventKinds.TopicCreated).Text);
        Assert.DoesNotContain("topicColor", File.ReadAllText(_paths.ConfigFile), StringComparison.Ordinal);

        var dashboard = First_Sent(events, "ALL ORCHESTRATIONS");
        Assert.Equal("", dashboard.Labels);

        var pulse = First_Sent(events, "PULSE").Text.Split('\n');

        Assert.Equal("0/1 (0%)", pulse[0]);
        Assert.Equal("PULSE", pulse[1]);
        Assert.StartsWith("updated ", pulse[^1], StringComparison.Ordinal);
        Assert.Equal("📸 /screen | 👁 /show | 🔀 /merge | 🧪 /test | 💻 /pc | 🏁 /close | 💤 /pause | 📊 /progress", First_Sent(events, "PULSE").Labels);

        Assert.Equal([DISPLAY_NAME, $"🔕 {DISPLAY_NAME}", $"✅ {DISPLAY_NAME}"], Renames(events));
        Assert.DoesNotContain(Pulse_Texts(events), text => text.Split('\n').Contains("🔕 PULSE"));
    }

    /// <summary>
    /// QUIET'S TOPIC: the first repository takes the rotation's first colour, blue, and keeps it on file;
    /// PULSE opens with its header and carries the shipped bar with ⏸ Wait on it; General's dashboard
    /// carries its five verbs; a silenced topic keeps its bare name and PULSE's header says 🔕 instead,
    /// and /done still puts ✅ on the name — the state glyph is drawn under either placement.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderQuiet_TheTopicLooksLikeTheForksTopic()
    {
        var events = await Run_Topic_Async(Presets_Loader.QUIET);

        // 0x6FB9F0, Telegram's blue — the first of its six permitted icon colours.
        Assert.Equal($"{REPO_NAME}-1 colour=7322096", Single_Event(events, e => e.Kind == PhoneEventKinds.TopicCreated).Text);
        Assert.Contains("\"topicColor\": 7322096", File.ReadAllText(_paths.ConfigFile), StringComparison.Ordinal);

        var dashboard = First_Sent(events, "ALL ORCHESTRATIONS");
        Assert.Equal("📊 /summary | ⏳ /pending | 📉 /limits | ▶ /resume | 🌙 /dnd_all", dashboard.Labels);

        var pulse = First_Sent(events, "PULSE").Text.Split('\n');

        Assert.Equal("PULSE", pulse[0]);
        Assert.Contains("0/1 merged · 0 %", pulse);
        Assert.DoesNotContain("0/1 (0%)", pulse);
        Assert.Equal("⏳ /pending | 📋 /left | 👀 /tail sup | 📉 /limits | 🔀 /merge | 🏁 /close | ⏸ Wait", First_Sent(events, "PULSE").Labels);

        Assert.Equal([DISPLAY_NAME, $"✅ {DISPLAY_NAME}"], Renames(events));
        Assert.Contains(Pulse_Texts(events), text => text.Split('\n')[0] == "🔕 PULSE");
    }

    // ---------------------------------------------------------------------------------------------
    // PULSE's cadence (Task 17)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Classic's field list without <c>updated</c>. The heartbeat steps every five minutes, so a fact that
    /// happened to straddle a step would see one honest edit and read it as noise. What these facts
    /// measure is the rest of the rendering, so the only clock on the line is taken off it.
    /// </summary>
    const string PULSE_WITHOUT_HEARTBEAT = ",\"pulse\":{\"fields\":[\"progress\",\"supervisor\",\"members\",\"modelEffort\"]}";

    /// <summary>The same, with a bar of two verbs where classic has eight — a change to the buttons alone.</summary>
    const string PULSE_WITH_A_SHORTER_BAR =
        ",\"pulse\":{\"fields\":[\"progress\",\"supervisor\",\"members\",\"modelEffort\"],\"buttons\":[\"screen\",\"show\"]}";

    const string DECLARED_STATE = "rebuilding the images";

    /// <summary>
    /// A PULSE THAT SAYS THE SAME THING IS NOT WRITTEN AGAIN. Found by the Task 12 gate (report §5.3): the
    /// engine remembered a render key and the planner compared it with raw text, so the two never matched
    /// and every tick edited the line with the text it already had — once every ~60 ms at this fixture's
    /// tick, once every 30 s per topic in production (the per-message edit gap), each one answered "not
    /// modified".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_AnUnchangedPulse_IsNeverEditedAgain()
    {
        var (timeline, mark) = await Run_Pulse_Async(async _ => await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(60)));

        Assert.True(Pulse_Activity(timeline, mark).Count == 0, Describe_PulseActivity(timeline, mark));
    }

    /// <summary>
    /// THE OWNER'S RULE OF 2026-09-09: PULSE is deleted and re-posted only when it is buried AND its content
    /// changed. The owner asks, the supervisor answers, the topic goes quiet for longer than the repost
    /// window — and PULSE, which says nothing new, stays where it is. The defect deleted and re-sent it
    /// about ten seconds after every such exchange.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_AnExchangeThatChangesNothingInPulse_DoesNotMoveIt()
    {
        var (timeline, mark) = await Run_Pulse_Async(Bury_Pulse_UnderAnExchange_Async);

        Assert.DoesNotContain(Pulse_Activity(timeline, mark), line => line.StartsWith("delete", StringComparison.Ordinal) || line.StartsWith("post", StringComparison.Ordinal));
        Assert.Contains(timeline.Skip(mark), e => e.Kind == PhoneEventKinds.Sent && e.Text.Contains(ANSWER_TEXT, StringComparison.Ordinal));
    }

    /// <summary>
    /// A CHANGE WHILE PULSE IS STILL THE LAST THING IN THE TOPIC IS ONE EDIT. The supervisor declares a
    /// state; under classic that entry is held rather than sent, so nothing buries the line and the new row
    /// is written in place — once.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_APulseThatChangedWhileLast_IsEditedOnce()
    {
        var (timeline, mark) = await Run_Pulse_Async(async channelFile =>
        {
            Append_SupervisorEntry(channelFile, 2, "status", $"Still on it.\nSTATE: {DECLARED_STATE}");

            await Require_Async(() => Pulse_Activity(_telegram.Events_Since(0), 0).Any(line => line.Contains(DECLARED_STATE, StringComparison.Ordinal)), 20_000, "PULSE never showed the declared state", 0);
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(60));
        });

        var activity = Pulse_Activity(timeline, mark);

        Assert.True(activity.Count == 1, Describe_PulseActivity(timeline, mark));
        Assert.StartsWith("edit", activity[0], StringComparison.Ordinal);
        Assert.Contains(DECLARED_STATE, activity[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A CHANGE WHILE PULSE IS BURIED AND THE TOPIC IS QUIET MOVES IT, ONCE: the old line is deleted and the
    /// new one is sent at the bottom, and nothing follows it. This is the half of the owner's rule the
    /// fix must not lose — "changed" still moves the line.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_APulseThatChangedWhileBuried_IsRepostedOnce()
    {
        var changedAt = 0;

        var (timeline, _) = await Run_Pulse_Async(async channelFile =>
        {
            await Bury_Pulse_UnderAnExchange_Async(channelFile);

            changedAt = _telegram.Mark();

            Append_SupervisorEntry(channelFile, 3, "status", $"Still on it.\nSTATE: {DECLARED_STATE}");

            await Require_Async(() => Pulse_Activity(_telegram.Events_Since(0), changedAt).Any(line => line.Contains(DECLARED_STATE, StringComparison.Ordinal)), 20_000, "PULSE never showed the declared state", changedAt);
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(60));
        });

        var activity = Pulse_Activity(timeline, changedAt);

        Assert.True(activity.Count == 2, Describe_PulseActivity(timeline, changedAt));
        Assert.Equal("delete", activity[0]);
        Assert.StartsWith("post", activity[1], StringComparison.Ordinal);
        Assert.Contains(DECLARED_STATE, activity[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// A CHANGE TO THE BAR ALONE STILL REACHES THE PHONE, as one edit. The render key exists for this
    /// (brief D): a quiet orchestration's text does not move for hours, so a bar compared by text would
    /// never be repainted. The fix compares text with text and must not lose it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_AChangeToThePulseBarAlone_IsEditedOnce()
    {
        var (timeline, mark) = await Run_Pulse_Async(async _ =>
        {
            Write_Config(Presets_Loader.CLASSIC, "[]", PULSE_WITH_A_SHORTER_BAR);

            await Require_Async(() => Pulse_Activity(_telegram.Events_Since(0), 0).Any(line => line.EndsWith("{📸 /screen | 👁 /show}", StringComparison.Ordinal)), 20_000, "the shorter bar never reached PULSE", 0);
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(60));
        });

        var activity = Pulse_Activity(timeline, mark);
        var textBefore = Pulse_Texts(timeline.Take(mark).ToList())[^1];

        Assert.True(activity.Count == 1, Describe_PulseActivity(timeline, mark));
        Assert.Equal($"edit {textBefore.Replace("\n", "⏎")} {{📸 /screen | 👁 /show}}", activity[0]);
    }

    /// <summary>
    /// Starts classic (without the heartbeat, see <see cref="PULSE_WITHOUT_HEARTBEAT"/>), waits for PULSE's
    /// first post and for the line to settle, then marks the timeline and runs <paramref name="body"/>.
    /// </summary>
    async Task<(List<PhoneEvent> Timeline, int Mark)> Run_Pulse_Async(Func<string, Task> body)
    {
        Use_Preset(Presets_Loader.CLASSIC, extraConfigJson: PULSE_WITHOUT_HEARTBEAT);

        var session = Launcher().Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = Ensure_OwnerChannel(session.OrchId);
        var mark = 0;

        await Run_WhileAsync(async () =>
        {
            await Require_Async(() => Pulse_Texts(_telegram.Events_Since(0)).Count > 0, 20_000, "PULSE was never posted", 0);
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(20));

            mark = _telegram.Mark();

            await body(channelFile);
        });

        return (_telegram.Events_Since(0), mark);
    }

    /// <summary>
    /// The owner asks, the supervisor answers — both land below PULSE — and then the topic stays quiet for
    /// longer than the repost window, so a line that had anything new to say would move.
    /// </summary>
    async Task Bury_Pulse_UnderAnExchange_Async(string channelFile)
    {
        // The id comes from the fake's own sequence, as a real one comes from the chat's: above PULSE, so it
        // buries the line, and below whatever the bot sends next, so a reposted line is not buried by it.
        _telegram.Queue_Updates(Message_Json(OWNER_TEXT, 8101, _telegram.Allocate_IncomingMessageId()));

        await Require_Async(() => Channel_Contains(channelFile, OWNER_TEXT), 30_000, "the owner's message was never delivered", 0);

        Append_SupervisorEntry(channelFile, 2, "the rebuild", ANSWER_TEXT);

        await Require_Async(() => _telegram.Has_Sent_Containing(ANSWER_TEXT), 20_000, "the answer never reached the phone", 0);
        await Wait_Until_Async(() => false, (TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS + 3) * 1000);
    }

    /// <summary>
    /// Every write to PULSE from <paramref name="mark"/> on, one line each: <c>post …</c>, <c>edit …</c>
    /// (text and bar), <c>delete</c>. PULSE's ids are taken from the WHOLE timeline, so an edit of a line
    /// posted before the mark is still recognised.
    /// </summary>
    static List<string> Pulse_Activity(List<PhoneEvent> timeline, int mark)
    {
        var pulseIds = timeline
            .Where(e => e.Kind == PhoneEventKinds.Sent && Is_Pulse(e.Text))
            .Select(e => e.MessageId)
            .ToHashSet();

        List<string> lines = [];

        foreach (var e in timeline.Skip(mark))
        {
            if (!pulseIds.Contains(e.MessageId))
                continue;

            var rendering = $"{e.Text.Replace("\n", "⏎")} {{{e.Labels}}}";

            switch (e.Kind)
            {
                case PhoneEventKinds.Sent:
                    lines.Add($"post {rendering}");
                    break;

                case PhoneEventKinds.Edited:
                    lines.Add($"edit {rendering}");
                    break;

                case PhoneEventKinds.Deleted:
                    lines.Add("delete");
                    break;

                default:
                    lines.Add(e.Describe());
                    break;
            }
        }

        return lines;
    }

    string Describe_PulseActivity(List<PhoneEvent> timeline, int mark)
    {
        return $"PULSE from the mark:{Environment.NewLine}{string.Join(Environment.NewLine, Pulse_Activity(timeline, mark).Take(20))}"
            + $"{Environment.NewLine}Phone:{Environment.NewLine}{_telegram.Dump(mark)}";
    }

    // ---------------------------------------------------------------------------------------------
    // Scenarios
    // ---------------------------------------------------------------------------------------------

    /// <summary>A channel entry the app wrote as the owner's tapped answer — "Advanced" alone as the body.</summary>
    static readonly Regex OWNER_ANSWER_IN_CHANNEL = new(@"FROM owner[^\n]*\n\s*" + CHOSEN_OPTION + @"\s*(\n|$)");

    /// <summary>
    /// The brief's five steps, each started only once the phone shows the previous one landed. Returns the
    /// whole timeline, the index the owner's message starts at, and the owner channel as the supervisor
    /// would read it.
    /// </summary>
    async Task<(List<PhoneEvent> Timeline, int Mark, string ChannelText)> Run_Conversation_Async(string preset)
    {
        Use_Preset(preset);

        var session = Launcher().Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = Ensure_OwnerChannel(session.OrchId);
        var mark = 0;

        await Run_WhileAsync(async () =>
        {
            // Past the tailer's first pass, which baselines a channel at its current length.
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

            mark = _telegram.Mark();

            // ── 1. the owner writes ─────────────────────────────────────────────────────────────
            _telegram.Queue_Updates(Message_Json(OWNER_TEXT, 8101, OWNER_MESSAGE_ID));

            await Require_Async(
                () => Channel_Contains(channelFile, OWNER_TEXT) && _telegram.Has_Event(Is_DeliveredReceipt),
                30_000, "the owner's message was never delivered and its receipt never said so", mark);

            // ── 2. the supervisor narrates while they wait ─────────────────────────────────────
            Append_SupervisorEntry(channelFile, 2, NARRATION_SUBJECT, NARRATION_TEXT);

            await Require_Async(() => _log.Has_Info_Containing("entry #2 FROM Supervisor"), 20_000, "the narration was never tailed", mark);

            // ── 3. the supervisor answers ──────────────────────────────────────────────────────
            Append_SupervisorEntry(channelFile, 3, "the rebuild", ANSWER_TEXT);

            await Require_Async(() => _telegram.Has_Sent_Containing(ANSWER_TEXT), 20_000, "the answer never reached the phone", mark);

            // ── 4. a question with options ─────────────────────────────────────────────────────
            Append_SupervisorEntry(channelFile, 4, "tail-risk plan", QUESTION_ENTRY);

            await Require_Async(() => _telegram.Find_ButtonData_OrNull(CHOSEN_OPTION) != null, 20_000, "the question's buttons never reached the phone", mark);

            // ── 5. the tap — and, if the phone asks for one, the code it shows ──────────────────
            var (questionId, data) = _telegram.Find_ButtonData_OrNull(CHOSEN_OPTION)!.Value;
            _telegram.Queue_Updates(Tap_Json(data, questionId, 8102));

            await Require_Async(() => _telegram.Has_Event(e => e.Kind == PhoneEventKinds.CallbackAnswered), 20_000, "the tap was never answered", mark);

            // WHAT THE OWNER DOES, NOT WHAT THE PRESET SAYS: they type the code only if the phone shows
            // one. The read-back takes a moment to be drawn after the tap is answered.
            await Wait_Until_Async(() => Read_Back_Code_OrNull(_telegram.Events_Since(mark)) != null, 2_000);

            var code = Read_Back_Code_OrNull(_telegram.Events_Since(mark));

            if (code != null)
                _telegram.Queue_Updates(Message_Json(code, 8103, OWNER_MESSAGE_ID + 1));

            await Require_Async(
                () => OWNER_ANSWER_IN_CHANNEL.IsMatch(File.ReadAllText(channelFile))
                      && _telegram.Has_Event(e => e.Kind == PhoneEventKinds.Edited && e.Text.EndsWith($"✅ {CHOSEN_OPTION}", StringComparison.Ordinal))
                      && Last_Rename_OrNull() == session.OrchId,
                30_000, "the choice was never delivered, stamped on the question, and taken off the name", mark);

            // Several more ticks, so a straggler — a late second receipt, a stray send — has every chance.
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(20));
        });

        return (_telegram.Events_Since(0), mark, File.ReadAllText(channelFile));
    }

    /// <summary>
    /// A new orchestration on a registered repository with no topic yet: its greeting makes the engine
    /// create the topic, then the topic is silenced and the owner types /done in it.
    /// </summary>
    async Task<List<PhoneEvent>> Run_Topic_Async(string preset)
    {
        var repoPath = _tempRepo.Replace("\\", "\\\\");

        Use_Preset(preset, reposJson: $"[{{\"name\":\"{REPO_NAME}\",\"path\":\"{repoPath}\"}}]");

        var session = Launcher().Start_Orchestration(REPO_NAME, _tempRepo);
        _store.Set_DisplayName(session.OrchId, DISPLAY_NAME);

        var channelFile = Ensure_OwnerChannel(session.OrchId);

        await Run_WhileAsync(async () =>
        {
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

            Append_SupervisorEntry(channelFile, 1, "supervisor online", "Ready — the crm bug is next.");

            await Require_Async(
                () => Pulse_Texts(_telegram.Events_Since(0)).Count > 0 && Last_Rename_OrNull() == DISPLAY_NAME,
                20_000, "the topic was never created, named and given its PULSE", 0);

            // ── silenced: the delivery glyph goes on the name OR on PULSE's header ───────────────
            _store.Set_TelegramMode(session.OrchId, TelegramDeliveryModes.Silenced);

            await Require_Async(
                () => Last_Rename_OrNull() == $"🔕 {DISPLAY_NAME}" || Pulse_Texts(_telegram.Events_Since(0)).Any(text => text.Split('\n')[0] == "🔕 PULSE"),
                20_000, "silencing the topic changed neither its name nor PULSE's header", 0);

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(20));

            // ── /done, typed in the topic ──────────────────────────────────────────────────────
            _telegram.Queue_Updates(Message_Json("/done", 8201, 1601));

            await Require_Async(() => Last_Rename_OrNull() == $"✅ {DISPLAY_NAME}", 20_000, "/done never put ✅ on the name", 0);

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(20));
        });

        return _telegram.Events_Since(0);
    }

    // ---------------------------------------------------------------------------------------------
    // Reading the timeline
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The conversation as the owner reads it: one line per thing on the phone, named by what it IS.
    /// PULSE, General's dashboard and renames are left out — PULSE and the name are the topic probes'
    /// subject, and PULSE's cadence has its own facts (Task 17). Anything the
    /// projection does not recognise is written out raw, so an unexpected send shows up in the diff
    /// instead of disappearing into a filter.
    /// </summary>
    static List<string> Project_Conversation(List<PhoneEvent> timeline, int mark)
    {
        // PULSE's ids from the WHOLE timeline: it is posted at start-up, before the owner writes.
        var pulseIds = timeline
            .Where(e => e.Kind == PhoneEventKinds.Sent && Is_Pulse(e.Text))
            .Select(e => e.MessageId)
            .ToHashSet();

        var namesById = new Dictionary<long, string>();
        var lines = new List<string>();

        foreach (var e in timeline.Skip(mark))
        {
            if (pulseIds.Contains(e.MessageId) || e.Kind == PhoneEventKinds.TopicRenamed || e.Text.Contains("ALL ORCHESTRATIONS", StringComparison.Ordinal))
                continue;

            switch (e.Kind)
            {
                case PhoneEventKinds.Sent:
                    var name = Name_Of(e.Text);
                    namesById[e.MessageId] = name;
                    lines.Add($"send [{e.Sound}] {name}{(e.Buttons.Count == 0 ? "" : $" {{{e.Labels}}}")}");
                    break;

                case PhoneEventKinds.Edited:
                    var target = namesById.TryGetValue(e.MessageId, out var known) ? known : $"#{e.MessageId}";
                    lines.Add($"edit {target} → {Name_Of_Edit(e.Text)}");
                    break;

                case PhoneEventKinds.Reacted:
                    lines.Add(e.MessageId == OWNER_MESSAGE_ID ? $"react owner message {e.Text}" : $"react #{e.MessageId} {e.Text}");
                    break;

                case PhoneEventKinds.CallbackAnswered:
                    lines.Add($"tap answered: {e.Text}");
                    break;

                default:
                    lines.Add(e.Describe());
                    break;
            }
        }

        return lines;
    }

    static string Name_Of(string text)
    {
        if (text == "✓")
            return "✓";

        if (text.Contains(NARRATION_TEXT, StringComparison.Ordinal))
            return "narration";

        if (text.Contains(ANSWER_TEXT, StringComparison.Ordinal))
            return "answer";

        if (text.Contains(QUESTION_PROSE, StringComparison.Ordinal))
            return "question prose";

        if (text.StartsWith($"❓ {QUESTION_TEXT}", StringComparison.Ordinal))
            return "question";

        if (text.StartsWith("🤐 going quiet here", StringComparison.Ordinal))
            return "🤐 going quiet";

        return text.Replace("\n", "⏎");
    }

    static string Name_Of_Edit(string text)
    {
        if (text.EndsWith($"✅ {CHOSEN_OPTION}", StringComparison.Ordinal))
            return $"✅ {CHOSEN_OPTION}";

        if (Read_Back_Code_OrNull(text) != null)
            return "🔐 read-back code";

        return text.Replace("\n", "⏎");
    }

    static string? Read_Back_Code_OrNull(List<PhoneEvent> events)
    {
        return events
            .Where(e => e.Kind == PhoneEventKinds.Edited)
            .Select(e => Read_Back_Code_OrNull(e.Text))
            .FirstOrDefault(code => code != null);
    }

    static string? Read_Back_Code_OrNull(string text)
    {
        if (!text.Contains($"You are about to: {CHOSEN_OPTION}", StringComparison.Ordinal))
            return null;

        var match = Regex.Match(text, @"Reply with the code (\d{4})\b");

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The question message as it was SENT — its terms, before any tap edited it.</summary>
    static string Question_Text(List<PhoneEvent> events)
    {
        return Single_Event(events, e => e.Kind == PhoneEventKinds.Sent && e.Text.StartsWith($"❓ {QUESTION_TEXT}", StringComparison.Ordinal)).Text;
    }

    /// <summary>A delivered receipt in either preset's shape — which one is the projection's to assert.</summary>
    static bool Is_DeliveredReceipt(PhoneEvent e)
    {
        return (e.Kind == PhoneEventKinds.Edited && e.Text == "✓✓")
            || (e.Kind == PhoneEventKinds.Reacted && e.MessageId == OWNER_MESSAGE_ID && e.Text == "👌");
    }

    /// <summary>
    /// While a question is open the name carries ❓; once it is answered the name goes back to the bare
    /// orchestration id. Asserted apart from the projection because the rename runs on its own schedule.
    /// </summary>
    void Assert_TheNameAskedAndThenStoppedAsking(List<PhoneEvent> events)
    {
        var renames = Renames(events);

        Assert.Contains("❓ repo-1", renames);
        Assert.Equal("repo-1", renames[^1]);
    }

    static List<string> Renames(List<PhoneEvent> events)
    {
        return [.. events.Where(e => e.Kind == PhoneEventKinds.TopicRenamed).Select(e => e.Text)];
    }

    string? Last_Rename_OrNull()
    {
        return Renames(_telegram.Events_Since(0)).LastOrDefault();
    }

    static List<string> Pulse_Texts(List<PhoneEvent> events)
    {
        return [.. events.Where(e => (e.Kind is PhoneEventKinds.Sent or PhoneEventKinds.Edited) && Is_Pulse(e.Text)).Select(e => e.Text)];
    }

    /// <summary>By its HEADER LINE — since Task 16 classic's count is drawn above it, so PULSE does not open the message.</summary>
    static bool Is_Pulse(string text)
    {
        return text.Split('\n').Any(line => line.EndsWith("PULSE", StringComparison.Ordinal));
    }

    static PhoneEvent First_Sent(List<PhoneEvent> events, string fragment)
    {
        return events.FirstOrDefault(e => e.Kind == PhoneEventKinds.Sent && e.Text.Contains(fragment, StringComparison.Ordinal))
            ?? throw new Exception($"nothing containing '{fragment}' was ever sent");
    }

    static PhoneEvent Single_Event(List<PhoneEvent> events, Func<PhoneEvent, bool> predicate)
    {
        var matches = events.Where(predicate).ToList();

        Assert.True(matches.Count == 1, $"expected exactly one such event, found {matches.Count}:{Environment.NewLine}{string.Join(Environment.NewLine, events.Select(e => e.Describe()))}");

        return matches[0];
    }

    // ---------------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the preset into config.json and builds the engine on it — the window from the SETTINGS
    /// (<c>Create_Custom_WindowFromSettings</c>), because a window named by the timing outranks the
    /// setting, and the owner's six seconds are part of what is probed.
    /// </summary>
    void Use_Preset(string preset, string reposJson = "[]", string extraConfigJson = "")
    {
        Write_Config(preset, reposJson, extraConfigJson);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var config = configProvider.Get_Current();

        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(SessionRunners.Terminal, config.Runners.Get_ForRole(role).Runner);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, _store, _launcher, _log, _telegram,
            BridgeEngineTiming_Factory.Create_Custom_WindowFromSettings(
                BridgeTestTiming.TICK_MILLISECONDS,
                BridgeTestTiming.RETRY_BACKOFF_SECONDS,
                BridgeTestTiming.TICK_LOCK_ALLOWANCE_MILLISECONDS,
                BridgeTestTiming.TRAILING_ENTRY_QUIET_MILLISECONDS));
    }

    /// <summary>
    /// config.json as every fact writes it; <paramref name="extraConfigJson"/> is spliced in as further
    /// top-level members (a leading comma included), so a fact can state a key over its preset — and
    /// rewrite the file mid-run, which the provider picks up on its write stamp.
    /// </summary>
    void Write_Config(string preset, string reposJson, string extraConfigJson)
    {
        var terminalRunners = string.Join(
            ",",
            SessionRole_Names.ALL.Select(role => $"\"{SessionRole_Names.Get_ConfigKey(role)}\":{{\"runner\":\"terminal\"}}"));

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":{reposJson},\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"preset\":\"{preset}\",\"runners\":{{{terminalRunners}}}{extraConfigJson}}}");
    }

    IOrchestrationLauncher Launcher() => _launcher ?? throw new Exception("Use_Preset was not called");

    string Ensure_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        return channelFile;
    }

    static void Append_SupervisorEntry(string channelFile, int index, string subject, string body)
    {
        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — {subject}\n{body}\n");
    }

    static bool Channel_Contains(string channelFile, string fragment)
    {
        return File.Exists(channelFile) && File.ReadAllText(channelFile).Contains(fragment, StringComparison.Ordinal);
    }

    /// <summary>A step that never lands stops the probe with the whole phone and log — a silent timeout proves nothing.</summary>
    async Task Require_Async(Func<bool> condition, int maxMilliseconds, string failure, int mark)
    {
        Assert.True(
            await Wait_Until_Async(condition, maxMilliseconds),
            $"{failure}.{Environment.NewLine}Phone:{Environment.NewLine}{_telegram.Dump(mark)}{Environment.NewLine}Log:{Environment.NewLine}{_log.Dump()}");
    }

    async Task Run_WhileAsync(Func<Task> body)
    {
        var engine = _engine ?? throw new Exception("Use_Preset was not called");

        using var cancellation = new CancellationTokenSource();
        var loop = engine.Run_Async(cancellation.Token);

        try
        {
            await body();
        }
        finally
        {
            await cancellation.CancelAsync();

            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // The only way these loops end.
            }
        }
    }

    static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }

    static string Message_Json(string text, long updateId, long messageId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":" + updateId + ",\"message\":{\"message_id\":" + messageId + ","
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    static string Tap_Json(string callbackData, long tappedMessageId, long updateId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":" + updateId + ",\"callback_query\":{\"id\":\"cbq-" + updateId + "\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{tappedMessageId},\"message_thread_id\":{TOPIC_ID},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}";
    }
}

/// <summary>
/// ONE ORDERED TIMELINE OF EVERYTHING THE OWNER'S PHONE WOULD SHOW — sends with their sound and
/// buttons, edits, reactions, topic creations with their colour, renames, callback answers — because
/// the preset probes assert the ORDER, and a fake that keeps one list per call kind cannot say which
/// came first.
/// </summary>
internal sealed class PhoneTimelineTelegram_Fake(long createdTopicId) : ITelegramApiClient
{
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    readonly Queue<string> _queuedUpdates = new();
    readonly List<PhoneEvent> _events = [];
    long _nextMessageId = 7000;

    public int Mark()
    {
        lock (_lock)
            return _events.Count;
    }

    public List<PhoneEvent> Events_Since(int mark)
    {
        lock (_lock)
            return [.. _events.Skip(mark)];
    }

    /// <summary>
    /// An id for a message the OWNER sends, taken from the same counter as the bot's sends. Telegram ids
    /// rise chat-wide, so an owner message sits between the bot message before it and the one after it —
    /// the ordering PULSE's burial check reads. An id picked from outside the sequence would bury every
    /// later bot message too.
    /// </summary>
    public long Allocate_IncomingMessageId()
    {
        lock (_lock)
            return _nextMessageId++;
    }

    public void Queue_Updates(string updatesJson)
    {
        lock (_lock)
            _queuedUpdates.Enqueue(updatesJson);
    }

    public bool Has_Event(Func<PhoneEvent, bool> predicate)
    {
        lock (_lock)
            return _events.Any(predicate);
    }

    public bool Has_Sent_Containing(string fragment)
    {
        lock (_lock)
            return _events.Any(e => e.Kind == PhoneEventKinds.Sent && e.Text.Contains(fragment, StringComparison.Ordinal));
    }

    public (long MessageId, string Data)? Find_ButtonData_OrNull(string labelFragment)
    {
        lock (_lock)
        {
            foreach (var e in _events)
            {
                if (e.Kind != PhoneEventKinds.Sent)
                    continue;

                foreach (var button in e.Buttons)
                {
                    if (button.Label.Contains(labelFragment, StringComparison.Ordinal))
                        return (e.MessageId, button.Data);
                }
            }

            return null;
        }
    }

    public string Dump(int mark = 0)
    {
        lock (_lock)
        {
            var lines = new List<string>();
            string? previous = null;
            var repeats = 0;

            foreach (var e in _events.Skip(mark))
            {
                var key = $"{e.Kind} #{e.MessageId} {e.Text} {e.Labels}";

                if (key == previous)
                {
                    repeats++;
                    continue;
                }

                if (repeats > 0)
                    lines.Add($"   (the line above repeated {repeats} more time(s))");

                repeats = 0;
                previous = key;
                lines.Add(e.Describe());
            }

            if (repeats > 0)
                lines.Add($"   (the line above repeated {repeats} more time(s))");

            return string.Join(Environment.NewLine, lines);
        }
    }

    long Record(PhoneEventKinds kind, long messageId, string text, TelegramSendSounds? sound, IReadOnlyList<(string Data, string Label)>? buttons)
    {
        lock (_lock)
        {
            _events.Add(new PhoneEvent(kind, messageId, text, sound, buttons ?? [], DateTime.UtcNow));
            return messageId;
        }
    }

    long? Record_Send(string text, TelegramSendSounds sound, IReadOnlyList<(string Data, string Label)>? buttons)
    {
        long id;

        lock (_lock)
            id = _nextMessageId++;

        return Record(PhoneEventKinds.Sent, id, text, sound, buttons);
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_queuedUpdates.Count > 0)
                return _queuedUpdates.Dequeue();
        }

        await Task.Delay(200, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record_Send(text, sound, null));

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record_Send(html, sound, null));

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record_Send(html, sound, buttons));

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record_Send(text, sound, buttons));

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record_Send(text, sound, [.. buttonRows.SelectMany(row => row)]));

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        Record_Send($"[document {fileName}] {captionHtml}", sound, null);
        return Task.CompletedTask;
    }

    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        Record_Send($"[photo] {filePath}", sound, null);
        return Task.CompletedTask;
    }

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.Edited, messageId, text, null, null);
        return Task.CompletedTask;
    }

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.Edited, messageId, html, null, null);
        return Task.CompletedTask;
    }

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.Edited, messageId, text, null, buttons);
        return Task.CompletedTask;
    }

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.Edited, messageId, text, null, [.. buttonRows.SelectMany(row => row)]);
        return Task.CompletedTask;
    }

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.Reacted, messageId, emoji ?? "(cleared)", null, null);
        return Task.CompletedTask;
    }

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.TopicCreated, createdTopicId, $"{topicName} colour={(iconColor?.ToString() ?? "none")}", null, null);
        return Task.FromResult(createdTopicId);
    }

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.TopicRenamed, messageThreadId, newName, null, null);
        return Task.CompletedTask;
    }

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.CallbackAnswered, 0, text, null, null);
        return Task.CompletedTask;
    }

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.ButtonsRemoved, messageId, "", null, null);
        return Task.CompletedTask;
    }

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken)
    {
        Record(PhoneEventKinds.Deleted, messageId, "", null, null);
        return Task.CompletedTask;
    }

    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");
    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Close_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal enum PhoneEventKinds
{
    Sent,
    Edited,
    Reacted,
    TopicCreated,
    TopicRenamed,
    CallbackAnswered,
    ButtonsRemoved,
    Deleted,
}

internal sealed class PhoneEvent(PhoneEventKinds kind, long messageId, string text, TelegramSendSounds? sound, IReadOnlyList<(string Data, string Label)> buttons, DateTime atUtc)
{
    public PhoneEventKinds Kind { get; } = kind;
    public long MessageId { get; } = messageId;
    public string Text { get; } = text;
    public TelegramSendSounds? Sound { get; } = sound;
    public IReadOnlyList<(string Data, string Label)> Buttons { get; } = buttons;
    public DateTime AtUtc { get; } = atUtc;

    public string Labels => string.Join(" | ", Buttons.Select(button => button.Label));

    public string Describe()
    {
        var sound = Sound == null ? "" : $"[{Sound}] ";
        var buttons = Buttons.Count == 0 ? "" : $" {{{Labels}}}";

        return $"{AtUtc:HH:mm:ss.fff} {Kind} #{MessageId} {sound}{Text.Replace("\n", "⏎")}{buttons}";
    }
}

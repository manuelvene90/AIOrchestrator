using System.Text;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.PulseSettings;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Bridge;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE BARS OBEY <c>pulse.buttons</c> AND <c>general.buttons</c>, AND THE HOLD TOGGLE LIVES IN EXACTLY
/// ONE PLACE (plan 03 Task 5). Until this task both bars were two private arrays and the ⏸/▶ toggle was
/// always on PULSE; classic — the preset a machine that names none resolves to — asks for a different
/// bar, no General bar at all, and the toggle on the receipt.
///
/// <para>
/// THE PARSER MUST ACCEPT WHAT THE BUILDER DREW. <c>KNOWN_COMMANDS</c> was a static set of the verbs
/// the two private arrays named; the moment <c>pulse.buttons</c> is configurable, a tap on a perfectly
/// legal configured button parses to null, falls through every handler, and NOTHING HAPPENS WITH
/// NOTHING LOGGED — which is the exact failure <c>Parse_OrNull</c>'s own docstring says it refuses to
/// cause.
/// </para>
/// <para>
/// AND EVERY COMMAND CAN BE A BUTTON (plan 03 Task 5b, ruling R16). Task 5's widened wiring guard found
/// twenty menu verbs no tap could run, so an owner could order their bar but not choose what was on
/// it. A tap on a verb with no dedicated case is now dispatched exactly as the typed <c>/verb</c> in
/// the same topic; what is still left off a bar, and said once in the log, is an element whose TARGET
/// no payload can carry (<c>tail 1</c>).
/// </para>
/// </summary>
public class ConfigurableCommandButtonsTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>Telegram's hard cap on callback_data — a payload over it is rejected at SEND time, on the phone.</summary>
    const int TELEGRAM_CALLBACK_DATA_BYTE_LIMIT = 64;

    /// <summary>The ✓ receipt's exact text — the one message the receipt-side toggle can ride.</summary>
    const string TICK_TEXT = "✓";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly SurfaceRecordingTelegram_Fake _telegram = new();
    readonly RecordingLog_Fake _log = new();

    public ConfigurableCommandButtonsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-configurable-bars-{Guid.NewGuid():N}");
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

    // ---------------------------------------------------------------------------------------
    // The parser
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// "cmd:pause:5" was refused by name before this task (it was a malformed-payload case). /pause is
    /// on classic's bar, so its payload is one the app now draws, and it has to come back as the
    /// command, never as "not ours".
    /// </summary>
    [Fact]
    public void ATapOnAConfiguredVerb_ParsesBack()
    {
        Assert.Equal(("pause", 5L), TopicCommandButtons.Parse_OrNull("cmd:pause:5"));
        Assert.Equal(("progress", 5L), TopicCommandButtons.Parse_OrNull("cmd:progress:5"));

        // And every button classic's bar actually draws round-trips through the one parser.
        var classic = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Pulse.Buttons;

        foreach (var (data, _) in TopicCommandButtons.Build_ForTopic(classic, TOPIC_ID, isHolding: false, heldCount: 0, holdToggleOnTheBar: false))
            Assert.Equal(TOPIC_ID, TopicCommandButtons.Parse_OrNull(data)!.Value.MessageThreadId);
    }

    /// <summary>
    /// THE MEMBERSHIP TEST IS "IS THIS A COMMAND AT ALL", and it still refuses what is not one. "tail 1"
    /// is a legal ELEMENT of a bar (its first word is a menu verb) but not a verb the tap handler can
    /// dispatch, so it is never drawn and a payload claiming it is not ours.
    /// </summary>
    [Theory]
    [InlineData("cmd:restart:5")]
    [InlineData("cmd:PAUSE:5")]
    [InlineData("cmd:tail 1:5")]
    [InlineData("cmd:hold:5")]
    public void ATapOnAVerbThatIsNotACommandAtAll_StillParsesToNull(string callbackData)
    {
        Assert.Null(TopicCommandButtons.Parse_OrNull(callbackData));
    }

    // ---------------------------------------------------------------------------------------
    // The bars
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// CLASSIC NAMES EIGHT AND EIGHT ARE DRAWN, in classic's own order — the bar master drew on
    /// 2026-09-09 (a2c9a3d), WITH MASTER'S LABELS (plan 03 Task 5b): 📸 /screen, 👁 /show, 🧪 /test,
    /// 💻 /pc, 💤 /pause and 📊 /progress are master's own glyphs, read back from that commit. Task 5 had
    /// drawn those six bare under D3, which is right only for a verb that never had a label. The expected
    /// bar is SPELLED OUT, not read back from the preset — deriving it would assert that a file equals
    /// itself.
    /// </summary>
    [Fact]
    public void ClassicsBar_RendersEightButtons_InTheStatedOrder()
    {
        var classic = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Pulse.Buttons;

        var bar = TopicCommandButtons.Build_ForTopic(classic, TOPIC_ID, isHolding: false, heldCount: 0, holdToggleOnTheBar: false);

        Assert.Equal(
            ["📸 /screen", "👁 /show", "🔀 /merge", "🧪 /test", "💻 /pc", "🏁 /close", "💤 /pause", "📊 /progress"],
            bar.Select(button => button.Label));
        Assert.Equal(
            [
                "cmd:screen:4242", "cmd:show:4242", "cmd:merge:4242", "cmd:test:4242",
                "cmd:pc:4242", "cmd:close:4242", "cmd:pause:4242", "cmd:progress:4242",
            ],
            bar.Select(button => button.Data));

        // Nothing classic names is refused, so the engine has nothing to say about it.
        Assert.Null(TopicCommandButtons.Describe_VerbsWithoutTapRoute_OrNull(PulseSettings_Json.BUTTONS_PATH, classic));
    }

    /// <summary>
    /// A VERB TASK 5 REFUSED IS DRAWN, IN THE CONFIGURED ORDER (plan 03 Task 5b). /cost, /model and /done
    /// had no case in the tap handler, so Task 5 left all three off every bar; the owner's ask was to
    /// choose WHICH commands are there and in which order, and a bar that silently drops three of them
    /// answers neither half. Nothing is said in the log, because nothing was refused.
    /// </summary>
    [Fact]
    public void APreviouslyRefusedVerb_IsDrawn_InTheConfiguredOrder()
    {
        var bar = TopicCommandButtons.Build_ForTopic(["cost", "model", "done"], TOPIC_ID, isHolding: false, heldCount: 0, holdToggleOnTheBar: false);

        Assert.Equal(["cmd:cost:4242", "cmd:model:4242", "cmd:done:4242"], bar.Select(button => button.Data));
        Assert.Equal(["/cost", "/model", "/done"], bar.Select(button => button.Label));
        Assert.Null(TopicCommandButtons.Describe_VerbsWithoutTapRoute_OrNull(PulseSettings_Json.BUTTONS_PATH, ["cost", "model", "done"]));

        // Every verb of the "/" menu is drawable, on either bar — the set plan 04's settings picker reads.
        foreach (var (verb, _) in BotCommandMenu.ALL)
            Assert.True(TopicCommandButtons.Has_TapRoute(verb), $"'/{verb}' is a menu command and is still refused from the bar");
    }

    /// <summary>
    /// A TARGET NO PAYLOAD CARRIES IS STILL LEFT OFF, AND SAID. "tail 1" is a legal bar ELEMENT (its first
    /// word is a menu verb), but a tap payload has two fields — verb and topic — and only "tail sup" rides
    /// inside its verb. Drawn, its tap would not parse back and would do nothing with nothing logged. The
    /// line names the setting and every refused element, because the owner reads it to find out why a
    /// button they configured is missing.
    /// </summary>
    [Fact]
    public void ATargetNoTapCarries_IsLeftOffTheBar_AndDescribedByItsSetting()
    {
        var bar = TopicCommandButtons.Build_ForTopic(["pending", "tasks", "left", "tail 1"], TOPIC_ID, isHolding: false, heldCount: 0, holdToggleOnTheBar: false);

        Assert.Equal(["cmd:pending:4242", "cmd:tasks:4242", "cmd:left:4242"], bar.Select(button => button.Data));
        Assert.Equal(
            "pulse.buttons names 'tail 1', 'log sup' — no tap can run them, so they are left off the bar (type the command instead)",
            TopicCommandButtons.Describe_VerbsWithoutTapRoute_OrNull(PulseSettings_Json.BUTTONS_PATH, ["pending", "tail 1", "left", "log sup"]));
        Assert.Equal(
            "general.buttons names 'tail 1' — no tap can run it, so it is left off the bar (type the command instead)",
            TopicCommandButtons.Describe_VerbsWithoutTapRoute_OrNull(PulseSettings_Json.GENERAL_BUTTONS_PATH, ["tail 1"]));
    }

    /// <summary>D3 (b): a verb the label table does not know renders as its bare slash command — usable, never unrenderable.</summary>
    [Fact]
    public void AVerbWithNoEmoji_RendersAsItsSlashCommand()
    {
        Assert.Equal("/cost", CommandButton_Labels.For("cost"));
        Assert.Equal("/tasks", CommandButton_Labels.For("tasks"));
        Assert.Equal("/organize_mains", CommandButton_Labels.For("organize_mains"));
    }

    /// <summary>
    /// D3 (a): the labels that existed before the bars were configurable are carried VERBATIM — eleven
    /// rows across the two old arrays, nine verbs, since /pending and /limits sat on both bars with the
    /// same label — and so are MASTER'S SIX (a2c9a3d, plan 03 Task 5b), the labels classic's own bar
    /// wore on the owner's phone before the fork merge.
    /// </summary>
    [Theory]
    [InlineData("pending", "⏳ /pending")]
    [InlineData("left", "📋 /left")]
    [InlineData("tail sup", "👀 /tail sup")]
    [InlineData("limits", "📉 /limits")]
    [InlineData("merge", "🔀 /merge")]
    [InlineData("close", "🏁 /close")]
    [InlineData("summary", "📊 /summary")]
    [InlineData("resume", "▶ /resume")]
    [InlineData("dnd_all", "🌙 /dnd_all")]
    [InlineData("screen", "📸 /screen")]
    [InlineData("show", "👁 /show")]
    [InlineData("test", "🧪 /test")]
    [InlineData("pc", "💻 /pc")]
    [InlineData("pause", "💤 /pause")]
    [InlineData("progress", "📊 /progress")]
    public void TheLabelsThatAlreadyExisted_AreCarriedVerbatim(string verb, string label)
    {
        Assert.Equal(label, CommandButton_Labels.For(verb));
    }

    /// <summary>
    /// D4: an empty list is NO BAR, not an empty keyboard — Telegram will not take an empty
    /// <c>inline_keyboard</c>, and classic's <c>general.buttons</c> is exactly that list. The wire half
    /// (no <c>reply_markup</c> at all) is pinned in TelegramApiClientWireTests; the engine half in
    /// DndKeepsTheSilentSurfacesCurrentTests.
    /// </summary>
    [Fact]
    public void AnEmptyGeneralList_ProducesNoButtonsAtAll()
    {
        Assert.Empty(TopicCommandButtons.Build_ForGeneral([], 0));
        Assert.Empty(OrchestratorConfig_Loader.Load_OrEmpty(_paths).Pulse.GeneralButtons);

        // The same holds for a topic bar emptied by its owner with the toggle on the receipt.
        Assert.Empty(TopicCommandButtons.Build_ForTopic([], TOPIC_ID, isHolding: false, heldCount: 0, holdToggleOnTheBar: false));
    }

    /// <summary>
    /// A GENERAL LIST IS DRAWN AS CONFIGURED, through the real engine and onto the dashboard — /tasks
    /// included since plan 03 Task 5b, and the element whose target no tap carries left off and said once. This is the half DndKeepsTheSilentSurfacesCurrentTests
    /// used to pin with the shipped five; under classic that list is empty now.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AConfiguredGeneralList_IsTheBarOnTheDashboard()
    {
        var engine = Build_Engine("\"general\":{\"buttons\":[\"summary\",\"tasks\",\"tail 1\"]}");

        await Run_WhileAsync(engine, async () =>
        {
            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_ButtonDataFor(GeneralDashboard_Composer.HEADING) != null, 25_000),
                $"the General dashboard never reached the client.{Environment.NewLine}{_telegram.Dump()}{Environment.NewLine}{_log.Dump()}");

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(5));
        });

        Assert.Equal(["cmd:summary:0", "cmd:tasks:0"], _telegram.Find_ButtonDataFor(GeneralDashboard_Composer.HEADING)!);
        Assert.Equal(1, Count_WarningsContaining("general.buttons", "'tail 1'"));
    }

    // ---------------------------------------------------------------------------------------
    // The hold toggle — one place
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>pulse.holdToggle = true</c>: the toggle is the bar's last button, and the ✓ receipt carries no
    /// hold button — only ▶ Send now (plan 03 Task 6b), which is delivery and not the toggle (ruling R2).
    /// Driven through the engine, because the one value is read at two call sites there and a pure test
    /// of either builder stays green with the argument dropped at one of them.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheHoldToggle_IsOnTheBar_WhenHoldToggleIsTrue()
    {
        var engine = Build_Engine("\"pulse\":{\"holdToggle\":true}");

        var (bar, tick) = await Run_UntilTheBarAndTheTickArrive_Async(engine);

        Assert.Equal(HoldButton_Data.Build(HoldButtonActions.Hold, TOPIC_ID), bar[^1]);
        Assert.Equal([HoldButton_Data.Build(HoldButtonActions.Go, TOPIC_ID)], tick);
    }

    /// <summary>
    /// <c>pulse.holdToggle = false</c> — classic, and the machine that names nothing. The tick carries
    /// ⏸ Wait then ▶ Send now (plan 03 Task 6b), and the bar carries no hold payload at all.
    ///
    /// Classic's receipt IS the ✓ since plan 03 Task 6 (<c>phone.receipts = ticks</c>, no reaction
    /// attempted); the harness still refuses reactions because the D10 case below runs under
    /// <c>reactions</c>, where the ✓ is only the fallback.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheHoldToggle_IsOnTheReceipt_WhenHoldToggleIsFalse()
    {
        var engine = Build_Engine(extraConfigJson: null);

        var (bar, tick) = await Run_UntilTheBarAndTheTickArrive_Async(engine);

        Assert.Equal(
            [
                "cmd:screen:4242", "cmd:show:4242", "cmd:merge:4242", "cmd:test:4242",
                "cmd:pc:4242", "cmd:close:4242", "cmd:pause:4242", "cmd:progress:4242",
            ],
            bar);
        Assert.Equal([HoldButton_Data.Build(HoldButtonActions.Hold, TOPIC_ID), HoldButton_Data.Build(HoldButtonActions.Go, TOPIC_ID)], tick);

        // Classic's bar is all tap-routed and its pair is coherent, so the log has nothing to say.
        Assert.Equal(0, Count_WarningsContaining("no tap can run"));
        Assert.Equal(0, Count_WarningsContaining(PulseSettings_Json.HOLD_TOGGLE_PATH));
    }

    /// <summary>
    /// A TYPED WAIT PUTS ITS ▶ GO WHERE THE TOGGLE LIVES, and only there. The acknowledgement a WAIT gets
    /// carried ▶ GO whatever the placement, so with the toggle on PULSE the owner saw two of them — the
    /// acknowledgement's, and PULSE's own. Asked through the engine for both placements, because the
    /// acknowledgement is one of four receipt-side call sites and a pure test of the builder stays green
    /// with the placement dropped at any one of them.
    /// </summary>
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATypedWait_PutsItsReleaseOnlyWhereTheToggleLives(bool holdToggle)
    {
        const string ACKNOWLEDGEMENT_FRAGMENT = "send GO when you";

        var engine = Build_Engine($"\"pulse\":{{\"holdToggle\":{(holdToggle ? "true" : "false")}}}");
        var go = HoldButton_Data.Build(HoldButtonActions.Go, TOPIC_ID);

        await Run_WhileAsync(engine, async () =>
        {
            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_ButtonDataFor("PULSE") != null, 25_000),
                $"PULSE was never posted.{Environment.NewLine}{_telegram.Dump()}{Environment.NewLine}{_log.Dump()}");

            _telegram.Queue_Updates(OwnerMessage_Json("wait"));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_ButtonDataFor(ACKNOWLEDGEMENT_FRAGMENT) != null, 20_000),
                $"the WAIT was never acknowledged.{Environment.NewLine}{_telegram.Dump()}{Environment.NewLine}{_log.Dump()}");

            // PULSE repaints with the hold state because its render key includes the bar; with the toggle
            // on the receipt there is nothing on PULSE to wait for, so a few ticks pass instead.
            if (holdToggle)
                Assert.True(
                    await Wait_Until_Async(() => _telegram.Find_ButtonDataFor("PULSE")!.Contains(go), 20_000),
                    $"PULSE never offered ▶ GO while holding.{Environment.NewLine}{_telegram.Dump()}");
            else
                await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));
        });

        var acknowledgement = _telegram.Find_ButtonDataFor(ACKNOWLEDGEMENT_FRAGMENT)!;
        var bar = _telegram.Find_ButtonDataFor("PULSE")!;

        if (holdToggle)
        {
            Assert.Empty(acknowledgement);
            Assert.Contains(go, bar);
        }
        else
        {
            Assert.Equal([go], acknowledgement);
            Assert.DoesNotContain(bar, data => HoldButton_Data.Parse_OrNull(data) != null);
        }
    }

    /// <summary>
    /// NEVER BOTH, UNDER EITHER PRESET AND EITHER CROSS COMBINATION. Every place a hold-family button can
    /// be drawn is asked: the bar, the ✓ tick (⏸ Wait) and the hold receipt (▶ GO). Exactly one of the bar
    /// and the receipts carries it — the receipts together are ONE place, the message under what the
    /// owner sent.
    ///
    /// <para>
    /// ▶ SEND NOW IS NOT COUNTED AS THE TOGGLE (plan 03 Task 6b, ruling R2). It carries the GO payload —
    /// the engine's release, which skips the window — so a payload-only count reads it as a second copy
    /// of the toggle under every placement. It is recognised by its LABEL, the one thing that tells it
    /// from the ▶ GO a hold receipt carries, and it is asserted separately: it rides every ✓.
    /// </para>
    /// </summary>
    [Fact]
    public void TheHoldToggle_IsNeverInBothPlaces_UnderEitherPreset()
    {
        foreach (var preset in new[] { Presets_Loader.CLASSIC, Presets_Loader.QUIET })
        {
            File.WriteAllText(_paths.ConfigFile, $"{{\"repos\":[],\"preset\":\"{preset}\"}}");

            var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

            Assert_TheToggleIsInExactlyOnePlace(preset, config.Pulse.Buttons, config.Pulse.HoldToggle, config.Phone.Receipts);

            foreach (var holdToggle in new[] { true, false })
            {
                foreach (var receipts in new[] { ReceiptStyles.Ticks, ReceiptStyles.Reactions })
                    Assert_TheToggleIsInExactlyOnePlace($"{preset} with holdToggle={holdToggle}, receipts={receipts}", config.Pulse.Buttons, holdToggle, receipts);
            }
        }
    }

    /// <summary>
    /// D10: <c>holdToggle = false</c> with <c>receipts = reactions</c> has no receipt message to carry the
    /// toggle, so it falls back to the PULSE bar and the log says so ONCE for the process — naming both
    /// keys, never on Telegram (decision 15). The pure half first, then the engine, where the latch lives.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WithHoldToggleFalseAndReactionReceipts_ItFallsBackToTheBar_AndLogsOnce()
    {
        Assert.Equal((true, true), HoldTogglePlacement_Resolver.Resolve(holdToggle: false, ReceiptStyles.Reactions));
        Assert.Equal((false, false), HoldTogglePlacement_Resolver.Resolve(holdToggle: false, ReceiptStyles.Ticks));
        Assert.Equal((true, false), HoldTogglePlacement_Resolver.Resolve(holdToggle: true, ReceiptStyles.Reactions));
        Assert.Equal((true, false), HoldTogglePlacement_Resolver.Resolve(holdToggle: true, ReceiptStyles.Ticks));

        var engine = Build_Engine("\"pulse\":{\"holdToggle\":false},\"phone\":{\"receipts\":\"reactions\"}");

        var (bar, tick) = await Run_UntilTheBarAndTheTickArrive_Async(engine);

        Assert.Equal(HoldButton_Data.Build(HoldButtonActions.Hold, TOPIC_ID), bar[^1]);

        // The fallback ✓ carries ▶ Send now alone: the toggle fell back to the bar, and Send now is not it.
        Assert.Equal([HoldButton_Data.Build(HoldButtonActions.Go, TOPIC_ID)], tick);

        Assert.Equal(1, Count_WarningsContaining("pulse.holdToggle", "phone.receipts"));
        Assert.False(
            _telegram.Has_Sent_Containing("pulse.holdToggle"),
            $"the fallback warning reached the phone — an alert the owner cannot act on from there (decision 15).{Environment.NewLine}{_telegram.Dump()}");
    }

    /// <summary>
    /// "cmd:" plus a 20-character long plus the verb. The longest verb the parser accepts is the
    /// ceiling, because it is the longest an owner may configure and a future tap route may draw; the
    /// overhead is MEASURED from a payload the builder really wrote rather than restated here.
    /// </summary>
    [Fact]
    public void TheCallbackPayload_StaysUnder64Bytes_ForTheLongestConfigurableVerb()
    {
        var everyConfigurableVerb = BotCommandMenu.ALL.Select(command => command.Command).Append("tail sup").ToArray();
        var longestVerb = everyConfigurableVerb.MaxBy(verb => Encoding.UTF8.GetByteCount(verb))!;

        var drawn = TopicCommandButtons.Build_ForTopic(everyConfigurableVerb, long.MinValue, isHolding: true, heldCount: 999, holdToggleOnTheBar: true);

        foreach (var (data, _) in drawn)
            Assert.True(Encoding.UTF8.GetByteCount(data) <= TELEGRAM_CALLBACK_DATA_BYTE_LIMIT, $"callback data too long: '{data}'");

        var (sampleData, _) = drawn[0];
        var sampleVerb = TopicCommandButtons.Parse_OrNull(sampleData)!.Value.Command;
        var overhead = Encoding.UTF8.GetByteCount(sampleData) - Encoding.UTF8.GetByteCount(sampleVerb);

        Assert.True(
            overhead + Encoding.UTF8.GetByteCount(longestVerb) <= TELEGRAM_CALLBACK_DATA_BYTE_LIMIT,
            $"'{longestVerb}' would make a {overhead + Encoding.UTF8.GetByteCount(longestVerb)}-byte payload on the widest topic id");
    }

    // ---------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------

    static void Assert_TheToggleIsInExactlyOnePlace(string scenario, IReadOnlyList<string> buttons, bool holdToggle, ReceiptStyles receipts)
    {
        var (onTheBar, _) = HoldTogglePlacement_Resolver.Resolve(holdToggle, receipts);

        var barCarriesIt = TopicCommandButtons.Build_ForTopic(buttons, TOPIC_ID, isHolding: false, heldCount: 0, onTheBar)
            .Any(button => HoldButton_Data.Parse_OrNull(button.Data) != null);

        var tick = ReceiptButtons_Builder.Build_ForTick(TOPIC_ID, onTheBar);

        var receiptsCarryIt = tick
            .Concat(ReceiptButtons_Builder.Build_ForHoldReceipt(HoldButtonActions.Hold, TOPIC_ID, onTheBar))
            .Concat(ReceiptButtons_Builder.Build_ForHoldReceipt(HoldButtonActions.Go, TOPIC_ID, onTheBar))
            .Where(button => button.Label != HoldButton_Data.SEND_NOW_LABEL)
            .Any(button => HoldButton_Data.Parse_OrNull(button.Data) != null);

        Assert.True(barCarriesIt != receiptsCarryIt, $"{scenario}: bar carries the toggle = {barCarriesIt}, a receipt carries it = {receiptsCarryIt}");

        Assert.True(
            tick.Count(button => button.Label == HoldButton_Data.SEND_NOW_LABEL) == 1,
            $"{scenario}: the ✓ does not carry exactly one ▶ Send now — it rides every tick, wherever the toggle lives");
    }

    /// <summary>A new provider per fact, with the keys written before it exists — see ThePulseObeysItsSettingsTests.</summary>
    IBridgeEngine Build_Engine(string? extraConfigJson)
    {
        var extra = extraConfigJson == null ? "" : $",{extraConfigJson}";

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}{extra}}}");

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        var session = launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        return BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    /// <summary>
    /// PULSE is posted, then the owner writes with reactions refused, so the ✓ tick is sent under either
    /// receipt style (under ticks no reaction is attempted at all; under reactions the ✓ is the fallback);
    /// a few more ticks follow so any once-per-process latch has been asked more than once. Returns the
    /// payloads of the last PULSE write and of the tick.
    /// </summary>
    async Task<(IReadOnlyList<string> Bar, IReadOnlyList<string> Tick)> Run_UntilTheBarAndTheTickArrive_Async(IBridgeEngine engine)
    {
        _telegram.Refuse_Reactions("Bad Request: REACTION_INVALID");

        await Run_WhileAsync(engine, async () =>
        {
            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_ButtonDataFor("PULSE") != null, 25_000),
                $"PULSE was never posted.{Environment.NewLine}{_telegram.Dump()}{Environment.NewLine}{_log.Dump()}");

            _telegram.Queue_Updates(OwnerMessage_Json("look at this"));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_ButtonDataForText(TICK_TEXT) != null, 20_000),
                $"the ✓ tick was never sent.{Environment.NewLine}{_telegram.Dump()}{Environment.NewLine}{_log.Dump()}");

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(5));
        });

        return (_telegram.Find_ButtonDataFor("PULSE")!, _telegram.Find_ButtonDataForText(TICK_TEXT)!);
    }

    int Count_WarningsContaining(params string[] fragments)
    {
        return _log.Dump()
            .Split(Environment.NewLine)
            .Count(line => line.StartsWith("WARN", StringComparison.Ordinal)
                && fragments.All(fragment => line.Contains(fragment, StringComparison.Ordinal)));
    }

    static string OwnerMessage_Json(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":3001,\"message\":{\"message_id\":91,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    static async Task Run_WhileAsync(IBridgeEngine engine, Func<Task> body)
    {
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
}

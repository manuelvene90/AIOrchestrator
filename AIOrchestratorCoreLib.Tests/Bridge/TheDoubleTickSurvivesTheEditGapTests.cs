using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;
using static AIOrchestratorCoreLib.Tests.Bridge.SendNowSkipsTheWindowTests;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE ✓ ALWAYS REACHES ✓✓ (plan 03 Task 6c). The owner, 2026-09-23: *"I like the double tick message to
/// confirm that the message has arrived and then that the message has been handed to the sup/solo … the
/// double tick message should carry the wait button as it used to."* Tasks 6/6b made that classic's
/// ordinary path — and made a receipt that could say "⏸ holding" for ever, on a message already delivered,
/// the owner's ordinary path too.
///
/// <para>
/// THE MECHANISM, confirmed on the branch source by the Task 6/6b review. The real client allows ONE edit
/// of a message per <see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>, checked BEFORE
/// the call and stamped at reservation. A ⏸ Wait tap is the ✓'s first edit, so a ▶ GO inside the gap made
/// the ✓✓ its second: the client threw <c>TelegramHeldException</c>, the delivery receipt caught it, logged
/// it and never tried again — the ✓'s id was already out of the registry — and nothing later corrected
/// the message.
/// </para>
/// <para>
/// THE FAKES COULD NOT SEE IT, which is why no probe did. Every engine fake let a message be edited any
/// number of times a second. This harness puts the REAL per-message gate in front of every edit
/// (<see cref="ScriptedInbound_Fake.Gate_EditsThrough"/>, over the real budget) with a gap the test sets
/// through <see cref="TelegramSendBudget_Factory.Create_WithEditGap"/> — never the production thirty
/// seconds — so a held edit can be watched landing, and one that is dropped fails in seconds.
/// </para>
/// <para>
/// A 60-SECOND AGGREGATION WINDOW, as in <see cref="SendNowSkipsTheWindowTests"/>: anything delivered inside
/// a probe's lifetime was delivered by the tap or word the probe sent.
/// </para>
/// </summary>
public class TheDoubleTickSurvivesTheEditGapTests : IDisposable
{
    /// <summary>
    /// The gap every message is held to here. Long enough that a tap sent straight after the edit it follows
    /// lands inside it on a loaded machine; short enough that the held edit lands within a probe.
    /// </summary>
    static readonly TimeSpan EDIT_GAP = TimeSpan.FromSeconds(3);

    /// <summary>The gap, the drain's next tick, and a loaded machine — then a dropped edit is a red, not a wait.</summary>
    const int AFTER_THE_GAP_MILLISECONDS = 15_000;

    const string DOUBLE_TICK = "✓✓";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    IOrchestrationLauncher _launcher;
    IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly ScriptedInbound_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    CancellationTokenSource? _runCancellation;
    Task? _runLoop;
    string? _orchId;

    public TheDoubleTickSurvivesTheEditGapTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-edit-gap-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        // NO PRESET, which is classic: the receipt is the ✓ MESSAGE (phone.receipts = ticks) and it carries
        // ⏸ Wait then ▶ Send now (pulse.holdToggle = false) — the owner's own setup.
        (_configProvider, _launcher) = Configure(preset: null);

        _telegram.Gate_EditsThrough(TelegramSendBudget_Factory.Create_WithEditGap(EDIT_GAP));
    }

    /// <summary>
    /// Writes config.json for <paramref name="preset"/> (null: none, which is classic) and builds the provider
    /// and launcher on it — called again by a probe BEFORE it builds its engine. QUIET NAMES BRIDGE-DRIVEN
    /// RUNNERS, and a registered print session would make the engine reach for a LIVE <c>claude</c>, so every
    /// role is pinned to the terminal runner, config.json outranking the preset — and the fixture refuses to
    /// run if that did not take (decision 20), exactly as <c>WhoRingsUnderEachPresetTests.Use_Preset</c> does.
    /// </summary>
    (IOrchestratorConfigProvider, IOrchestrationLauncher) Configure(string? preset)
    {
        var terminalRunners = string.Join(
            ",",
            SessionRole_Names.ALL.Select(role => $"\"{SessionRole_Names.Get_ConfigKey(role)}\":{{\"runner\":\"terminal\"}}"));

        var presetKey = preset == null ? "" : $",\"preset\":\"{preset}\"";

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}{presetKey},\"runners\":{{{terminalRunners}}}}}");

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var config = configProvider.Get_Current();

        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(SessionRunners.Terminal, config.Runners.Get_ForRole(role).Runner);

        Assert.Equal(preset == "quiet" ? ReceiptStyles.Reactions : ReceiptStyles.Ticks, config.Phone.Receipts);

        return (configProvider, OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log));
    }

    void Use_Quiet()
    {
        (_configProvider, _launcher) = Configure("quiet");
    }

    public void Dispose()
    {
        if (_runCancellation != null && _runLoop != null)
        {
            Stop_Async(_runCancellation, _runLoop).GetAwaiter().GetResult();
            _runCancellation.Dispose();
        }

        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// ⏸ WAIT THEN ▶ GO INSIDE THE GAP — the exact flow the owner uses, and the one that left "⏸ holding" on
    /// a delivered message. The WAIT's rewrite is the ✓'s first edit, so the ✓✓ is its second and the gate
    /// holds it; it must land once the gap has passed, and the message must end as ✓✓ with no keyboard.
    /// This closes the blind spot <see cref="SendNowSkipsTheWindowTests"/>'s <c>WaitThenGo…</c> probe wrote
    /// down about itself.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WaitThenGo_WithinTheGap_TheTickStillBecomesDoubleTick()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9801, 801)));

            Assert.True(await Wait_Until_Async(() => _telegram.Find_ButtonFor("Send now") != null, 20_000), _telegram.Dump_Sent());

            var tickId = Find_TickIds().Last();
            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Hold, tickId, 9802)));

            Assert.True(
                await Wait_Until_Async(() => Shows(tickId, "holding"), 10_000),
                $"the WAIT never turned the ✓ into the hold receipt.{Environment.NewLine}{Describe(tickId)}");

            // INSIDE THE GAP: the WAIT's rewrite was this message's first edit a moment ago.
            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Go, tickId, 9803)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT), 10_000),
                $"the GO did not deliver the held message.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(tickId) == (DOUBLE_TICK, ""), AFTER_THE_GAP_MILLISECONDS),
                $"the message was delivered and its receipt never became ✓✓ — the held edit was dropped.{Environment.NewLine}{Describe(tickId)}{Environment.NewLine}{_log.Dump()}");

            // THE CONTROL: "✓✓ at the end" has two routes — a held edit that was retried, or a GO that came
            // after the gap and was never held. Only the first is this probe's subject.
            Assert.True(
                _telegram.Count_HeldEdits_Of(tickId) >= 1,
                $"no edit of the receipt was ever held, so the GO came after the gap and this probe proved nothing about it.{Environment.NewLine}{Describe(tickId)}");

            // And it stays: nothing staged earlier may land on top of it.
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

            Assert.Equal((DOUBLE_TICK, ""), _telegram.Current_Of_OrNull(tickId));
        });
    }

    /// <summary>
    /// LAST WRITER WINS. Two messages typed during a hold stage two count-ups of the same receipt inside the
    /// gap; only the newer may land. A retry that held a copy of the older text would draw "2 messages" over
    /// a receipt that should say 3 — which is why the slot is per message, not per edit.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ANewerWantedText_WinsOverAnOlderHeldOne()
    {
        var engine = Build_Engine();
        Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9811, 811)));

            Assert.True(await Wait_Until_Async(() => _telegram.Find_ButtonFor("Send now") != null, 20_000), _telegram.Dump_Sent());

            var tickId = Find_TickIds().Last();
            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Hold, tickId, 9812)));

            Assert.True(
                await Wait_Until_Async(() => Shows(tickId, "holding · 1 message"), 10_000),
                $"the WAIT never turned the ✓ into the hold receipt.{Environment.NewLine}{Describe(tickId)}");

            // Two held messages in one batch: two count-ups of the receipt, both inside the gap.
            _telegram.Queue_Updates(
                "{\"ok\":true,\"result\":["
                + Message_Json("and the second part of", 9813, 813) + ","
                + Message_Json("and the third part of", 9814, 814) + "]}");

            Assert.True(
                await Wait_Until_Async(() => Shows(tickId, "holding · 3 messages"), AFTER_THE_GAP_MILLISECONDS),
                $"the newest count-up never reached the receipt — a held edit was dropped.{Environment.NewLine}{Describe(tickId)}{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                _telegram.Count_HeldEdits_Of(tickId) >= 1,
                $"no count-up was ever held, so this probe never met the gap.{Environment.NewLine}{Describe(tickId)}");

            // Several more ticks, so a late copy of the older text has every chance to land.
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

            Assert.DoesNotContain(_telegram.ButtonEdits, edit => edit.MessageId == tickId && edit.Text.Contains("2 messages", StringComparison.Ordinal));
            Assert.Equal(("✓ ⏸ holding · 3 messages — send GO when you're done", HoldButton_Data.GO_LABEL), _telegram.Current_Of_OrNull(tickId));
        });
    }

    /// <summary>
    /// ▶ SEND NOW ON AN OLDER ✓ WHILE A TYPED WAIT HOLDS — the hold's own receipt is the one to finish (the
    /// Task 6/6b review's Minor 1). The typed WAIT turned the NEWEST ✓ into "⏸ holding"; the owner then taps
    /// Send now on an earlier ✓ still showing its buttons. That tap used to rewrite the tapped ✓ with the
    /// content it already had — a no-op edit that spends its slot — and leave the real hold receipt saying
    /// "holding" after the delivery.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SendNowOnAnOlderTick_DuringATypedHold_FinishesTheHoldReceipt()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("first part of the", 9821, 821)));
            Assert.True(await Wait_Until_Async(() => Find_TickIds().Count == 1, 20_000), _telegram.Dump_Sent());

            _telegram.Queue_Updates(Updates_Json(Message_Json("second part of the", 9822, 822)));
            Assert.True(await Wait_Until_Async(() => Find_TickIds().Count == 2, 20_000), _telegram.Dump_Sent());

            var olderTickId = Find_TickIds()[0];
            var holdReceiptId = Find_TickIds()[1];

            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9823, 823)));

            Assert.True(
                await Wait_Until_Async(() => Shows(holdReceiptId, "holding · 2 messages"), 10_000),
                $"the typed WAIT never turned the newest ✓ into the hold receipt.{Environment.NewLine}{Describe(holdReceiptId)}");

            _telegram.Queue_Updates(Updates_Json(SendNowTap_Json(olderTickId, 9824)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, "first part of the") && Channel_Contains(channelFile, "second part of the"), 10_000),
                $"Send now did not deliver the held messages.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(holdReceiptId) == (DOUBLE_TICK, ""), AFTER_THE_GAP_MILLISECONDS),
                $"the hold receipt still says what it said before the delivery.{Environment.NewLine}{Describe(holdReceiptId)}{Environment.NewLine}{_log.Dump()}");

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(5));

            // The tapped ✓ is left as it was: rewriting it with the content it already has is a 400 that
            // spends its slot, and it is not this batch's receipt.
            Assert.DoesNotContain(_telegram.ButtonEdits, edit => edit.MessageId == olderTickId);
            Assert.DoesNotContain(_telegram.TextEdits, edit => edit.MessageId == olderTickId);
        });
    }

    /// <summary>
    /// A TYPED WAIT THEN A TAPPED ▶ GO REACHES ✓✓ (the review's Minor 8). The typed WAIT took the ✓ out of
    /// the registry to make it the hold receipt, so the delivery had no tick to edit and the GO's rewrite
    /// put "✓ + both buttons" on a batch that had just been delivered — while the tapped Wait then GO
    /// reached ✓✓. A GO that releases held messages now makes the hold receipt THE ✓, so both paths end in
    /// the double tick, with one edit.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TypedWaitThenGo_ReachesDoubleTick()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9831, 831)));

            Assert.True(await Wait_Until_Async(() => _telegram.Find_ButtonFor("Send now") != null, 20_000), _telegram.Dump_Sent());

            var tickId = Find_TickIds().Last();
            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9832, 832)));

            Assert.True(
                await Wait_Until_Async(() => Shows(tickId, "holding · 1 message"), 10_000),
                $"the typed WAIT never turned the ✓ into the hold receipt.{Environment.NewLine}{Describe(tickId)}");

            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Go, tickId, 9833)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT), 10_000),
                $"the GO did not deliver the held message.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(tickId) == (DOUBLE_TICK, ""), AFTER_THE_GAP_MILLISECONDS),
                $"a typed WAIT released by ▶ GO never reached ✓✓.{Environment.NewLine}{Describe(tickId)}{Environment.NewLine}{_log.Dump()}");

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(5));

            Assert.Equal((DOUBLE_TICK, ""), _telegram.Current_Of_OrNull(tickId));
        });
    }

    // ---------------------------------------------------------------------------------------
    // ONE HOLD, ONE RECEIPT — every route that ends a hold finishes it (Task 6c fix round 1)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// TYPED WAIT THEN TYPED GO, UNDER CLASSIC (path A, ruling R19). The typed GO dropped the hold entry and
    /// gave the GO word a ✓ of its own, which then became ✓✓ — while the hold receipt went on saying "⏸ holding
    /// … send GO" over a batch that had been delivered. The GO word is a control word, not content: its effect
    /// is shown on the hold receipt, which becomes THE ✓ and then ✓✓ — one hold, one receipt, one ✓✓.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TypedWaitThenTypedGo_UnderClassic_TheHoldReceiptBecomesTheDoubleTick_AndTheGoWordGetsNoTick()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9841, 841)));
            Assert.True(await Wait_Until_Async(() => Find_TickIds().Count == 1, 20_000), _telegram.Dump_Sent());

            var holdReceiptId = Find_TickIds()[0];
            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9842, 842)));

            Assert.True(
                await Wait_Until_Async(() => Shows(holdReceiptId, "holding · 1 message"), 10_000),
                $"the typed WAIT never turned the ✓ into the hold receipt.{Environment.NewLine}{Describe(holdReceiptId)}");

            _telegram.Queue_Updates(Updates_Json(Message_Json("go", 9843, 843)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT), 10_000),
                $"the typed GO did not deliver the held message.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(holdReceiptId) == (DOUBLE_TICK, ""), AFTER_THE_GAP_MILLISECONDS),
                $"the hold receipt still says what it said before the typed GO delivered the batch.{Environment.NewLine}{Describe(holdReceiptId)}{Environment.NewLine}{_telegram.Dump_Sent()}");

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(5));

            Assert.Equal([holdReceiptId], Find_TickIds());
            Assert.Equal((DOUBLE_TICK, ""), _telegram.Current_Of_OrNull(holdReceiptId));
        });
    }

    /// <summary>
    /// TYPED WAIT THEN TYPED GO, UNDER QUIET (path A, ruling R19). The message before the WAIT carries 👀, so the
    /// delivery turns THAT 👌 and the hold receipt is not adopted — it is rewritten to its after-hold text (a ✓,
    /// with the tick's own button) instead of saying "⏸ holding" for ever. The GO word gets no reaction of its
    /// own: it used to take the 👀 slot, so the 👌 landed on "go" and not on the message it picked up.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TypedWaitThenTypedGo_UnderQuiet_TheHoldReceiptIsRewritten_AndTheGoWordGetsNoReaction()
    {
        Use_Quiet();

        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9851, 851)));
            Assert.True(await Wait_Until_Async(() => _telegram.Reactions.Contains((851, OwnerReaction_Emoji.RECEIVED)), 20_000), _log.Dump());

            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9852, 852)));
            Assert.True(await Wait_Until_Async(() => _telegram.Find_SentContaining("holding") != null, 10_000), _telegram.Dump_Sent());

            var holdReceiptId = _telegram.LastSentMessageId_Containing("holding");
            _telegram.Queue_Updates(Updates_Json(Message_Json("go", 9853, 853)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT), 10_000),
                $"the typed GO did not deliver the held message.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(holdReceiptId) == ("✓", HoldButton_Data.SEND_NOW_LABEL), AFTER_THE_GAP_MILLISECONDS),
                $"the hold receipt was never finished after the typed GO.{Environment.NewLine}{Describe(holdReceiptId)}{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Reactions.Contains((851, OwnerReaction_Emoji.PICKED_UP)), 10_000),
                $"the message the delivery picked up never got its 👌.{Environment.NewLine}{string.Join(" | ", _telegram.Reactions)}");

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(5));

            Assert.DoesNotContain(_telegram.Reactions, reaction => reaction.MessageId == 853);
        });
    }

    /// <summary>
    /// ▶ GO ON THE PULSE BAR ENDS A TYPED WAIT'S HOLD (path B, quiet — the toggle lives on the bar). The bar's
    /// branch only forgot the hold entry, so the typed WAIT's "⏸ holding" stayed on a delivered batch. The bar
    /// is not a receipt and is never rewritten; the hold's own receipt is finished, as from every other route.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AGoOnThePulseBar_UnderQuiet_FinishesATypedHoldsReceipt()
    {
        Use_Quiet();

        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            Assert.True(
                await Wait_Until_Async(() => _store.Get_Session(_orchId!).StatusLineMessageId != null, 30_000),
                $"the status line was never posted, so there is no bar to tap.{Environment.NewLine}{_log.Dump()}");

            var statusLineId = _store.Get_Session(_orchId!).StatusLineMessageId!.Value;

            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9861, 861)));
            Assert.True(await Wait_Until_Async(() => _telegram.Reactions.Contains((861, OwnerReaction_Emoji.RECEIVED)), 20_000), _log.Dump());

            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9862, 862)));
            Assert.True(await Wait_Until_Async(() => _telegram.Find_SentContaining("holding") != null, 10_000), _telegram.Dump_Sent());

            var holdReceiptId = _telegram.LastSentMessageId_Containing("holding");
            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Go, statusLineId, 9863)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT), 10_000),
                $"the bar's ▶ GO did not deliver the held message.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(holdReceiptId) == ("✓", HoldButton_Data.SEND_NOW_LABEL), AFTER_THE_GAP_MILLISECONDS),
                $"the bar's ▶ GO left the typed WAIT's receipt saying what it said.{Environment.NewLine}{Describe(holdReceiptId)}{Environment.NewLine}{_log.Dump()}");
        });
    }

    /// <summary>
    /// A SECOND TYPED WAIT DURING A HOLD (path E). It sent a new "⏸ holding" and moved the hold onto it, so the
    /// first one was stranded saying "holding" for ever. One hold has one receipt: the second WAIT targets it
    /// (and redraws it only if its count changed — an identical edit is a 400 that still spends the slot).
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASecondTypedWait_DuringAHold_KeepsOneHoldMessage_AndItReachesDoubleTick()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9871, 871)));
            Assert.True(await Wait_Until_Async(() => _telegram.Find_SentContaining("holding") != null, 20_000), _telegram.Dump_Sent());

            var holdReceiptId = _telegram.LastSentMessageId_Containing("holding");
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9872, 872)));

            Assert.True(
                await Wait_Until_Async(() => Shows(holdReceiptId, "holding · 1 message"), 10_000),
                $"the held message never counted up on the hold receipt.{Environment.NewLine}{Describe(holdReceiptId)}");

            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9873, 873)));
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

            Assert.Equal(1, _telegram.Count_Sent_Containing("holding"));

            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Go, holdReceiptId, 9874)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT), 10_000),
                $"the GO did not deliver the held message.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(holdReceiptId) == (DOUBLE_TICK, ""), AFTER_THE_GAP_MILLISECONDS),
                $"the one hold receipt never reached ✓✓.{Environment.NewLine}{Describe(holdReceiptId)}{Environment.NewLine}{_telegram.Dump_Sent()}");

            Assert.Equal(1, _telegram.Count_Sent_Containing("holding"));
        });
    }

    /// <summary>
    /// ⏸ WAIT TAPPED ON AN OLDER ✓ WHILE A TYPED WAIT HOLDS (path D). The tap moved the hold onto the older ✓
    /// and rewrote it into a second "⏸ holding", stranding the typed WAIT's receipt. One hold, one receipt: the
    /// tap is about the hold's own receipt, the older ✓ is left as it is, and the receipt reaches ✓✓ at GO.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AWaitTappedOnAnOlderTick_DuringATypedHold_KeepsTheHoldReceipt()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("first part of the", 9881, 881)));
            Assert.True(await Wait_Until_Async(() => Find_TickIds().Count == 1, 20_000), _telegram.Dump_Sent());

            _telegram.Queue_Updates(Updates_Json(Message_Json("second part of the", 9882, 882)));
            Assert.True(await Wait_Until_Async(() => Find_TickIds().Count == 2, 20_000), _telegram.Dump_Sent());

            var olderTickId = Find_TickIds()[0];
            var holdReceiptId = Find_TickIds()[1];

            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9883, 883)));

            Assert.True(
                await Wait_Until_Async(() => Shows(holdReceiptId, "holding · 2 messages"), 10_000),
                $"the typed WAIT never turned the newest ✓ into the hold receipt.{Environment.NewLine}{Describe(holdReceiptId)}");

            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Hold, olderTickId, 9884)));

            Assert.True(await Wait_Until_Async(() => _telegram.Answered_Callbacks >= 1, 10_000), "the ⏸ tap was never answered");
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

            Assert.DoesNotContain(_telegram.ButtonEdits, edit => edit.MessageId == olderTickId);

            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Go, holdReceiptId, 9885)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, "second part of the"), 10_000),
                $"the GO did not deliver the held messages.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(holdReceiptId) == (DOUBLE_TICK, ""), AFTER_THE_GAP_MILLISECONDS),
                $"the typed WAIT's receipt was stranded by the ⏸ tap on an older ✓.{Environment.NewLine}{Describe(holdReceiptId)}{Environment.NewLine}{Describe(olderTickId)}");

            Assert.DoesNotContain(_telegram.ButtonEdits, edit => edit.MessageId == olderTickId);
            Assert.DoesNotContain(_telegram.TextEdits, edit => edit.MessageId == olderTickId);
        });
    }

    /// <summary>
    /// ⏸ WAIT ON AN OLDER ✓ WHILE A NEWER ✓ IS LIVE, THEN ▶ GO (the review's m1). Adopting the older ✓ as the
    /// tick replaced the newer one, so the newest ✓ — the one under the owner's last message — kept its buttons
    /// on a delivered batch. A hold receipt is adopted only when nothing else is registered: here the newest ✓
    /// becomes ✓✓ and the older one goes back to being a ✓.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AWaitOnAnOlderTick_ThenGo_TheNewestTickBecomesTheDoubleTick()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("first part of the", 9891, 891)));
            Assert.True(await Wait_Until_Async(() => Find_TickIds().Count == 1, 20_000), _telegram.Dump_Sent());

            _telegram.Queue_Updates(Updates_Json(Message_Json("second part of the", 9892, 892)));
            Assert.True(await Wait_Until_Async(() => Find_TickIds().Count == 2, 20_000), _telegram.Dump_Sent());

            var olderTickId = Find_TickIds()[0];
            var newestTickId = Find_TickIds()[1];

            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Hold, olderTickId, 9893)));

            Assert.True(
                await Wait_Until_Async(() => Shows(olderTickId, "holding · 2 messages"), 10_000),
                $"the ⏸ tap never turned the older ✓ into the hold receipt.{Environment.NewLine}{Describe(olderTickId)}");

            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Go, olderTickId, 9894)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, "second part of the"), 10_000),
                $"the GO did not deliver the held messages.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(newestTickId) == (DOUBLE_TICK, ""), AFTER_THE_GAP_MILLISECONDS),
                $"the newest ✓ kept its buttons on a delivered batch.{Environment.NewLine}{Describe(newestTickId)}{Environment.NewLine}{Describe(olderTickId)}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Current_Of_OrNull(olderTickId) == ("✓", $"{HoldButton_Data.HOLD_LABEL} | {HoldButton_Data.SEND_NOW_LABEL}"), AFTER_THE_GAP_MILLISECONDS),
                $"the older ✓ still says it is holding after the hold ended.{Environment.NewLine}{Describe(olderTickId)}");
        });
    }

    // ---------------------------------------------------------------------------------------
    // Harness — the static helpers are SendNowSkipsTheWindowTests' own; these need this fixture
    // ---------------------------------------------------------------------------------------

    IBridgeEngine Build_Engine()
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            LongWindow());
    }

    string Start_Orchestration()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _orchId = session.OrchId;

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        return channelFile;
    }

    /// <summary>Every ✓ receipt sent so far, oldest first — the sends whose text IS the tick.</summary>
    List<long> Find_TickIds()
    {
        return [.. _telegram.Sent_WithIds.Where(sent => sent.Text == "✓").Select(sent => sent.Id)];
    }

    bool Shows(long messageId, string fragment)
    {
        return _telegram.Current_Of_OrNull(messageId)?.Text.Contains(fragment, StringComparison.Ordinal) == true;
    }

    string Describe(long messageId)
    {
        var current = _telegram.Current_Of_OrNull(messageId);

        return $"message #{messageId} now shows: {(current == null ? "(never sent)" : $"'{current.Value.Text}' [{current.Value.Labels}]")}; "
            + $"held edits: {_telegram.Count_HeldEdits_Of(messageId)}{Environment.NewLine}"
            + "text edits: " + string.Join(" | ", _telegram.TextEdits.Where(edit => edit.MessageId == messageId).Select(edit => edit.Text))
            + $"{Environment.NewLine}button edits: " + string.Join(" | ", _telegram.ButtonEdits.Where(edit => edit.MessageId == messageId).Select(edit => $"{edit.Text} [{edit.Labels}]"));
    }

    async Task Run_WhileAsync(IBridgeEngine engine, Func<Task> body)
    {
        _runCancellation = new CancellationTokenSource();
        _runLoop = engine.Run_Async(_runCancellation.Token);

        try
        {
            await body();
        }
        finally
        {
            await Stop_Async(_runCancellation, _runLoop);
            _runCancellation.Dispose();
            _runCancellation = null;
            _runLoop = null;
        }
    }
}

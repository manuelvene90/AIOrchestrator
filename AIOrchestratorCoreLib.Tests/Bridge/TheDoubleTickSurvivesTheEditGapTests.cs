using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
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
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly ScriptedInbound_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    CancellationTokenSource? _runCancellation;
    Task? _runLoop;

    public TheDoubleTickSurvivesTheEditGapTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-edit-gap-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // NO PRESET, which is classic: the receipt is the ✓ MESSAGE (phone.receipts = ticks) and it carries
        // ⏸ Wait then ▶ Send now (pulse.holdToggle = false) — the owner's own setup.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);

        _telegram.Gate_EditsThrough(TelegramSendBudget_Factory.Create_WithEditGap(EDIT_GAP));
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

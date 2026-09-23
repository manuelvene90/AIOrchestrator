using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// ▶ SEND NOW BESIDE ⏸ WAIT ON THE ✓ RECEIPT (plan 03 Task 6b). The owner, 2026-09-14: *"Sometimes I am
/// absolutely sure that what I have written is correct and full, and waiting 6 seconds is annoying, I'd
/// like to have a button that basically says 'don't worry, send it to the solo/sup right away'."* — and
/// again on 2026-09-23, for the double-tick receipt they use.
///
/// <para>
/// THE ENGINE ALREADY HAD THE ACTION. A GO releases the buffer (<c>ReleaseRequested</c>, which
/// <c>Is_Ready</c> answers true for with no window) and the tap handler flushes at once — so Send now is
/// the same <c>go:&lt;thread&gt;</c> payload under another label, and these probes are about what the tap
/// does to the phone, not about a new delivery path (there is none).
/// </para>
/// <para>
/// A 60-SECOND WINDOW, so a delivery inside a test's lifetime can only have come from the tap. The
/// owner's text is a FRAGMENT with no closing punctuation, because a finished single message is given
/// the shorter quiet (<c>OwnerMessageComplete_Decider</c>) and would blur which route delivered it.
/// </para>
/// </summary>
public class SendNowSkipsTheWindowTests : IDisposable
{
    internal const long SUPERGROUP_CHAT_ID = -1002233445566;
    internal const long OWNER_USER_ID = 555000111;
    internal const long TOPIC_ID = 9797;

    internal const string FRAGMENT = "and also check the";

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

    public SendNowSkipsTheWindowTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-send-now-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // NO PRESET, which is classic: phone.receipts = ticks (plan 03 Task 6), so the receipt is the ✓
        // MESSAGE — a reaction carries no buttons, and there would be nothing to tap — and
        // pulse.holdToggle = false, so that ✓ carries ⏸ Wait then ▶ Send now.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
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

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SendNow_DeliversAtOnce_WhileTheWindowWouldHaveHeldIt()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9701, 701)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_ButtonFor("Send now") != null, 20_000),
                $"the receipt carries no Send now button.{Environment.NewLine}{_telegram.Dump_Sent()}");

            Assert.Equal(HoldButton_Data.Build(HoldButtonActions.Go, TOPIC_ID), _telegram.Find_ButtonFor("Send now"));

            // THE CONTROL, because "delivered" has two routes: the tap, or a window that was not actually
            // long. Several ticks go by and the message must still be in the buffer.
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

            Assert.False(
                Channel_Contains(channelFile, FRAGMENT),
                "the message was delivered before any tap — the window in this fixture is not long, so the test below proves nothing.");

            var receiptId = Find_TickId();
            _telegram.Queue_Updates(Updates_Json(SendNowTap_Json(receiptId, 9702)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT), 10_000),
                $"Send now was tapped and the message did not reach the channel inside 10 s of a 60 s window.{Environment.NewLine}{_log.Dump()}");

            // "✓" — something changed on this tap. Not "already sent": the message was still waiting.
            Assert.True(await Wait_Until_Async(() => _telegram.Answered_Callbacks >= 1, 5_000), "the tap was never answered");
            Assert.Equal(HoldTap_Decider.DONE_ANSWER, _telegram.Answered_CallbackTexts[0]);
        });
    }

    /// <summary>
    /// AFTER SEND NOW THE ✓ BECOMES ✓✓ — IN ITS ONLY EDIT. The owner's double tick is the thing they asked
    /// to keep, and it must survive the tap that makes the delivery happen. A rewrite of the tapped ✓ first
    /// (to the buttons it already has) would spend the one edit Telegram allows that message per 30 s
    /// (<see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>): the ✓✓ is then HELD by the
    /// real client, and the owner is left with "✓ ⏸ Wait ▶ Send now" under a message that was delivered.
    /// The fake has no such gate, so the probe asserts the one thing that keeps the real one open: the ✓
    /// is edited exactly once, to ✓✓, and never re-drawn with buttons.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AfterSendNow_TheTickBecomesTheDoubleTick_InItsOnlyEdit()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9711, 711)));

            Assert.True(await Wait_Until_Async(() => _telegram.Find_ButtonFor("Send now") != null, 20_000), _telegram.Dump_Sent());

            var receiptId = Find_TickId();
            _telegram.Queue_Updates(Updates_Json(SendNowTap_Json(receiptId, 9712)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.TextEdits.Contains((receiptId, "✓✓")), 10_000),
                $"Send now delivered the message and its ✓ never became ✓✓.{Environment.NewLine}{Describe_Edits()}{Environment.NewLine}{_log.Dump()}");

            // Several more ticks, so a late second edit has every chance to appear.
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

            Assert.True(Channel_Contains(channelFile, FRAGMENT), "the ✓✓ was written but the message is not in the channel");
            Assert.Equal([(receiptId, "✓✓")], _telegram.TextEdits.Where(edit => edit.MessageId == receiptId).ToList());
            Assert.DoesNotContain(_telegram.ButtonEdits, edit => edit.MessageId == receiptId);
        });
    }

    /// <summary>
    /// ⏸ WAIT THEN ▶ GO ON THE SAME ✓ — classic's hold, entirely by taps. The WAIT turns the ✓ into the hold
    /// receipt (one rewrite, with the release); the GO releases it, and the delivery that follows edits that
    /// same message to ✓✓. The GO adds NO rewrite of its own in between: that would be a second edit of one
    /// message inside Telegram's 30-second gap, in front of the ✓✓ the owner is waiting for.
    ///
    /// <para>
    /// WHAT THIS DOES NOT PROVE, said plainly: against the REAL client the ✓✓ is still the second edit of
    /// that message, so a GO tapped within 30 s of the WAIT has its ✓✓ held by the per-message edit gap
    /// (2026-09-10). The fake in this file has no gap, so no probe here can see that. Until plan 03 Task 6c
    /// the held ✓✓ was never retried and the receipt kept saying "⏸ holding" after the delivery; that is now
    /// fixed, and proved over the REAL gate by
    /// <see cref="TheDoubleTickSurvivesTheEditGapTests.WaitThenGo_WithinTheGap_TheTickStillBecomesDoubleTick"/>.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WaitThenGoOnTheTick_LeavesTheReleaseToTheDoubleTick()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9741, 741)));

            Assert.True(await Wait_Until_Async(() => _telegram.Find_ButtonFor("Send now") != null, 20_000), _telegram.Dump_Sent());

            var receiptId = Find_TickId();
            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Hold, receiptId, 9742)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.ButtonEdits.Any(edit => edit.MessageId == receiptId), 10_000),
                $"the WAIT never turned the ✓ into the hold receipt.{Environment.NewLine}{Describe_Edits()}");

            _telegram.Queue_Updates(Updates_Json(Tap_Json(HoldButtonActions.Go, receiptId, 9743)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.TextEdits.Contains((receiptId, "✓✓")), 10_000),
                $"the GO released the hold and the receipt never became ✓✓.{Environment.NewLine}{Describe_Edits()}{Environment.NewLine}{_log.Dump()}");

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(5));

            Assert.True(Channel_Contains(channelFile, FRAGMENT), "the ✓✓ was written but the held message is not in the channel");

            var rewrite = Assert.Single(_telegram.ButtonEdits, edit => edit.MessageId == receiptId);
            Assert.Contains("holding", rewrite.Text, StringComparison.Ordinal);
            Assert.Equal(HoldButton_Data.GO_LABEL, rewrite.Labels);
        });
    }

    /// <summary>
    /// A GO THAT ENDS A HOLD NO DELIVERY WILL EDIT puts the receipt back to a ✓ that offers BOTH buttons
    /// again — the next message may be one to hold or one to send at once, never a lone ⏸ Wait. A typed
    /// WAIT with nothing typed before it is that case: its "⏸ holding" message is not a ✓ any delivery is
    /// waiting to edit, and without the rewrite it would say "holding" for ever after the hold ended.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AGoThatEndsAnEmptyHold_PutsTheReceiptBackWithWaitAndSendNow()
    {
        var engine = Build_Engine();
        Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("wait", 9721, 721)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_SentContaining("holding") != null, 20_000),
                $"the typed WAIT was never acknowledged.{Environment.NewLine}{_telegram.Dump_Sent()}");

            var holdReceiptId = _telegram.LastSentMessageId_Containing("holding");
            _telegram.Queue_Updates(Updates_Json(SendNowTap_Json(holdReceiptId, 9722)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.ButtonEdits.Any(edit => edit.MessageId == holdReceiptId), 10_000),
                $"the hold receipt was never rewritten after the GO ended the hold.{Environment.NewLine}{_log.Dump()}");

            var rewrite = _telegram.ButtonEdits.First(edit => edit.MessageId == holdReceiptId);

            Assert.Equal("✓", rewrite.Text);
            Assert.Equal(2, rewrite.ButtonCount);
            Assert.Equal($"{HoldButton_Data.HOLD_LABEL} | {HoldButton_Data.SEND_NOW_LABEL}", rewrite.Labels);
        });
    }

    /// <summary>
    /// SEND NOW ON A MESSAGE THAT ALREADY LEFT says "already sent" — a bare ✓ over the button would read
    /// as "sent now" — and rewrites nothing, because nothing changed.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SendNow_OnAMessageAlreadyDelivered_AnswersAlreadySent_AndRewritesNothing()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9731, 731)));

            Assert.True(await Wait_Until_Async(() => _telegram.Find_ButtonFor("Send now") != null, 20_000), _telegram.Dump_Sent());

            var receiptId = Find_TickId();
            _telegram.Queue_Updates(Updates_Json(SendNowTap_Json(receiptId, 9732)));

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT) && _telegram.Answered_Callbacks >= 1, 10_000),
                $"the first Send now did not deliver.{Environment.NewLine}{_log.Dump()}");

            // The same button again — a phone can show a stale ✓ for as long as the owner likes.
            _telegram.Queue_Updates(Updates_Json(SendNowTap_Json(receiptId, 9733)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Answered_Callbacks >= 2, 10_000),
                $"the second tap was never answered.{Environment.NewLine}{_log.Dump()}");

            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(5));

            Assert.Equal(HoldTap_Decider.ALREADY_SENT_ANSWER, _telegram.Answered_CallbackTexts[1]);
            Assert.DoesNotContain(_telegram.ButtonEdits, edit => edit.MessageId == receiptId);
        });
    }

    // ---------------------------------------------------------------------------------------
    // Harness — copied from TheBridgeNeverLiesAboutDeliveryTests, where these helpers are private.
    // The static ones are INTERNAL so TheDoubleTickSurvivesTheEditGapTests shares them rather than
    // adding another copy (code-conventions: share the wait/fake-Telegram helpers).
    // ---------------------------------------------------------------------------------------

    /// <summary>Fast()'s tick, lock allowance and trailing quiet, with an aggregation window no test outlives.</summary>
    internal static IBridgeEngineTiming LongWindow() =>
        BridgeEngineTiming_Factory.Create_Custom(
            BridgeTestTiming.TICK_MILLISECONDS,
            ownerAggregationSeconds: 60,
            BridgeTestTiming.RETRY_BACKOFF_SECONDS,
            BridgeTestTiming.TICK_LOCK_ALLOWANCE_MILLISECONDS,
            BridgeTestTiming.TRAILING_ENTRY_QUIET_MILLISECONDS);

    IBridgeEngine Build_Engine()
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            LongWindow());
    }

    /// <summary>Starts the orchestration on the topic and returns its owner channel file, created if absent.</summary>
    string Start_Orchestration()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        return channelFile;
    }

    /// <summary>
    /// The ✓ receipt's id — the newest send whose text IS the tick, not merely one that contains it (a
    /// status line or a dashboard may carry a ✓ of its own).
    /// </summary>
    long Find_TickId()
    {
        return _telegram.Sent_WithIds.Last(sent => sent.Text == "✓").Id;
    }

    internal static bool Channel_Contains(string channelFile, string fragment)
    {
        return File.Exists(channelFile) && File.ReadAllText(channelFile).Contains(fragment, StringComparison.Ordinal);
    }

    string Describe_Edits()
    {
        return "text edits: " + string.Join(" | ", _telegram.TextEdits.Select(edit => $"#{edit.MessageId} {edit.Text}"))
            + $"{Environment.NewLine}button edits: " + string.Join(" | ", _telegram.ButtonEdits.Select(edit => $"#{edit.MessageId} {edit.Text} [{edit.Labels}]"))
            + $"{Environment.NewLine}sent: {_telegram.Dump_Sent()}";
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

    internal static string Updates_Json(string update) => "{\"ok\":true,\"result\":[" + update + "]}";

    internal static string Message_Json(string text, long updateId, long messageId)
    {
        return $"{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}";
    }

    /// <summary>A tap on the ✓'s ▶ Send now — which is GO's payload, so a tap on a hold receipt's ▶ GO reads the same.</summary>
    internal static string SendNowTap_Json(long tappedMessageId, long updateId)
    {
        return Tap_Json(HoldButtonActions.Go, tappedMessageId, updateId);
    }

    internal static string Tap_Json(HoldButtonActions action, long tappedMessageId, long updateId)
    {
        var data = HoldButton_Data.Build(action, TOPIC_ID);

        return $"{{\"update_id\":{updateId},\"callback_query\":{{\"id\":\"cbq-{updateId}\","
            + $"\"data\":\"{data}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{tappedMessageId},\"message_thread_id\":{TOPIC_ID},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}";
    }

    internal static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }

    internal static async Task Stop_Async(CancellationTokenSource cancellation, Task loop)
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

using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE INBOUND LOOP LOSES NOTHING AND REPEATS NOTHING, whatever one update does to it.
///
/// <para>
/// THE INCIDENT, from the VPS log of 2026-09-08 01:24-01:26Z: `answerCallbackQuery failed: query is
/// too old` four times at 20, 40 and 60 seconds, then `getUpdates failed —
/// DllNotFoundException: user32.dll`. A Windows-only window call threw on the Linux daemon, escaped
/// the command dispatch, escaped the whole batch — and because `_lastUpdateId` advanced only after
/// the ENTIRE batch, Telegram re-served every update in it. Four times. Four copies of the owner's
/// message, four flips of whatever toggle was in that batch.
/// </para>
/// <para>
/// Driven through the real engine with a scripted client, because every claim here is about what
/// the loop does with a batch — a unit test of the parser cannot see an offset advance or a tap
/// firing twice.
/// </para>
/// </summary>
public class TheInboundLoopSurvivesItsOwnBatchTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 7373;

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
    IBridgeEngine _engine;

    public TheInboundLoopSurvivesItsOwnBatchTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-inbound-batch-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = Build_Engine();
    }

    IBridgeEngine Build_Engine(AIOrchestratorCoreLib.Hosting.HostWindowing.IHostWindowing? hostWindowing = null)
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            BridgeTestTiming.Fast(),
            hostWindowing);
    }

    public void Dispose()
    {
        // The tests that start the loop through Start_Async leave it running for their duration;
        // it is stopped here so a cancelled engine cannot outlive the temp folder it writes into.
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
    /// THE 409 IS A SITUATION, NOT A FAILURE. Two pollers on one bot token, or a webhook registered
    /// against it: Telegram refuses `getUpdates` for as long as it lasts, and the loop used to log
    /// one Error per retry and tell the owner nothing at all — while their taps went to whichever
    /// host won the race.
    ///
    /// <para>
    /// ONE MESSAGE AND ONE LOG LINE PER STATE CHANGE is the whole assertion: the backoff keeps
    /// retrying (so the conflict is not fatal), and the recovery says so once.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task A409Conflict_TellsTheOwnerOnce_KeepsRetrying_AndSaysWhenItIsOver()
    {
        _telegram.Fail_Updates_With(new TelegramApiException(409, "Conflict: terminated by other getUpdates request"));

        using var cancellation = new CancellationTokenSource();
        var loop = _engine.Run_Async(cancellation.Token);

        try
        {
            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("another bridge is polling") >= 1, 20_000),
                $"the owner was never told about the conflict.{Environment.NewLine}{_log.Dump()}");

            // It KEEPS TRYING — the conflict is somebody else's process, and it may end at any time.
            Assert.True(
                await Wait_Until_Async(() => _telegram.UpdateCalls >= 3, 20_000),
                $"the loop stopped polling after a 409.{Environment.NewLine}{_log.Dump()}");

            // ...and says it once, however many times it retried.
            Assert.Equal(1, _telegram.Count_Sent_Containing("another bridge is polling"));
            Assert.Equal(1, Count_Log_Lines_Containing("409 CONFLICT"));

            // The message NAMES THIS MACHINE: without it the owner cannot tell which of the two
            // hosts is complaining, and the fix is to stop one of them.
            var conflictMessage = _telegram.Find_SentContaining("another bridge is polling")!;
            Assert.Contains(Environment.MachineName, conflictMessage, StringComparison.Ordinal);
            Assert.Contains("telegramInbound", conflictMessage, StringComparison.Ordinal);

            _telegram.Stop_Failing_Updates();

            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("reading your messages again") == 1, 20_000),
                $"the recovery was never announced.{Environment.NewLine}{_telegram.Dump_Sent()}");

            Assert.Equal(1, Count_Log_Lines_Containing("the 409 conflict is over"));
        }
        finally
        {
            await Stop_Async(cancellation, loop);
        }
    }

    /// <summary>
    /// A BATCH OF THREE WHOSE SECOND HANDLER THROWS: the first is not re-routed, the third is
    /// handled, and the offset ends past all three.
    ///
    /// <para>
    /// THE THROW IS THE INCIDENT'S OWN. `/show` reached three static classes of unguarded
    /// user32/dwmapi/gdi32 P/Invoke, and on the Linux daemon it raised
    /// <see cref="DllNotFoundException"/> — so that is what the injected capability raises here,
    /// from the same call the engine makes first. Before this stage it escaped the command
    /// dispatch, escaped the batch, and Telegram re-served every update in it four times
    /// (2026-09-08 01:24-01:26Z).
    /// </para>
    /// <para>
    /// A CAPABILITY THAT THROWS IS NOT WHAT SHIPS — the Linux implementation returns "no" without
    /// throwing, and another probe pins that. This one exists for the case nobody can rule out: a
    /// host call that fails for a reason its author did not foresee. The loop must survive it.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task OneUpdateThatThrows_CostsOnlyItself_AndTheOffsetMovesPastTheWholeBatch()
    {
        _engine = Build_Engine(new ThrowingHostWindowing_Fake());

        var orchId = await Start_Async();

        _telegram.Queue_Updates(
            "{\"ok\":true,\"result\":["
            + Message_Json("first message", 8001, 101)
            + "," + Message_Json("/show", 8002, 102)
            + "," + Message_Json("third message", 8003, 103)
            + "]}");

        Assert.True(
            await Wait_Until_Async(() => _log.Has_Line_Containing("Handling update 8002 failed"), 20_000),
            $"the middle update never failed, so this test proves nothing.{Environment.NewLine}{_log.Dump()}");

        // THE THIRD WAS STILL HANDLED — the batch did not end at the throw.
        Assert.True(
            await Wait_Until_Async(() => Channel(orchId).Contains("third message", StringComparison.Ordinal), 25_000),
            $"the third update was never handled.{Environment.NewLine}{Channel(orchId)}{Environment.NewLine}{_log.Dump()}");

        // AND THE OFFSET IS PAST ALL THREE, so Telegram will not re-serve them.
        Assert.True(
            await Wait_Until_Async(() => _telegram.LastRequestedOffset > 8003, 20_000),
            $"the offset never moved past the batch (last requested {_telegram.LastRequestedOffset}).{Environment.NewLine}{_log.Dump()}");

        // THE FIRST IS NOT RE-ROUTED. Its words appear once, however many polls have happened.
        Assert.Equal(1, Count_Occurrences(Channel(orchId), "first message"));

        // The failure was reported as ONE update's, naming it — not as "getUpdates failed".
        Assert.DoesNotContain("Telegram getUpdates failed", _log.Dump(), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE SAME TAP DELIVERED TWICE FIRES ONCE. A replay re-serves the whole update, and a tap acted
    /// on twice is a DECISION TAKEN TWICE — the one duplicate this system cannot afford, since the
    /// decisions that reach it include pushes and deploys.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSameCallbackDeliveredTwice_IsActedOnOnce()
    {
        var orchId = await Start_Async();

        Append_Supervisor(orchId, 3, COMPLETE_QUESTION);
        Append_Supervisor(orchId, 4, "Nothing else — this second entry only terminates the one above it.");

        Assert.True(
            await Wait_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 25_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var startIt = _telegram.Find_ButtonFor("Start it")!;
        var questionMessageId = _telegram.LastButtonMessageId!.Value;

        // The SAME callback_query.id twice, in two separate batches — Telegram's own identity for
        // one gesture.
        _telegram.Queue_Updates(Tap_Json(startIt, questionMessageId, updateId: 8101, callbackId: "cbq-same"));

        Assert.True(
            await Wait_Until_Async(() => _engineState.Load_OrEmpty().OpenQuestions.Count == 0, 20_000),
            $"the tap never closed the question.{Environment.NewLine}{_log.Dump()}");

        var answersAfterFirstTap = Count_Occurrences(Channel(orchId), "Start it");

        _telegram.Queue_Updates(Tap_Json(startIt, questionMessageId, updateId: 8102, callbackId: "cbq-same"));

        Assert.True(
            await Wait_Until_Async(() => _log.Has_Line_Containing("cbq-same was already handled"), 20_000),
            $"the replayed tap was not recognised as a duplicate.{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(answersAfterFirstTap, Count_Occurrences(Channel(orchId), "Start it"));
    }

    const string COMPLETE_QUESTION =
        "Two plan levers changed and the build can start.\n"
        + "QUESTION: Start the FIN-D-277a build now?\n"
        + "OPTION: Start it\n"
        + "OPTION: Wait\n"
        + "RECOMMEND: Start it — the matrix values are numbers, not layout.\n"
        + "RISK: low\n"
        + "ROW: FIN-D-277a";

    string Channel(string orchId) => File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));

    int Count_Log_Lines_Containing(string fragment)
    {
        return Count_Occurrences(_log.Dump(), fragment);
    }

    static int Count_Occurrences(string text, string fragment)
    {
        var count = 0;
        var at = text.IndexOf(fragment, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal);
        }

        return count;
    }

    CancellationTokenSource? _runCancellation;
    Task? _runLoop;

    async Task<string> Start_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        _runCancellation = new CancellationTokenSource();
        _runLoop = _engine.Run_Async(_runCancellation.Token);

        // The tailer baselines a channel it has never seen at its CURRENT length, so the engine has
        // to make one pass before anything is appended.
        _telegram.Queue_Updates("{\"ok\":true,\"result\":[" + Message_Json("what is happening", 8000, 100) + "]}");

        Assert.True(
            await Wait_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 15_000),
            $"the engine never made its first pass.{Environment.NewLine}{_log.Dump()}");

        return session.OrchId;
    }

    void Append_Supervisor(string orchId, int entryNumber, string body)
    {
        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{entryNumber}] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a question\n{body}\n");
    }

    static string Message_Json(string text, long updateId, long messageId)
    {
        return $"{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}";
    }

    static string Tap_Json(string callbackData, long questionMessageId, long updateId, string callbackId)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"callback_query\":{{\"id\":\"{callbackId}\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{questionMessageId},\"message_thread_id\":{TOPIC_ID},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}";
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

    static async Task Stop_Async(CancellationTokenSource cancellation, Task loop)
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

/// <summary>
/// A CLIENT WHOSE INBOUND SIDE IS SCRIPTABLE: it can refuse `getUpdates` with a chosen
/// <see cref="TelegramApiException"/>, count how often it was asked, and remember which offset was
/// asked for — the three facts the loop's contract is written in. Sends are recorded like every
/// other fake here, and can be made to throw by fragment so a failure can be produced from inside
/// ONE update's own handling.
/// </summary>
internal sealed class ScriptedInbound_Fake : ITelegramApiClient
{
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    readonly List<string> _sentTexts = [];
    readonly List<string> _editedTexts = [];
    readonly List<(string Data, string Label)> _buttons = [];
    string? _queuedUpdatesJson;
    Exception? _updatesFailure;
    string? _failSendsContaining;
    long _nextMessageId = 4000;

    public int UpdateCalls { get; private set; }
    public long LastRequestedOffset { get; private set; }
    public long? LastButtonMessageId { get; private set; }
    public List<(string FileName, byte[] Content, string Caption)> Documents { get; } = [];

    public void Queue_Updates(string updatesJson)
    {
        lock (_lock)
            _queuedUpdatesJson = updatesJson;
    }

    public void Fail_Updates_With(Exception failure)
    {
        lock (_lock)
            _updatesFailure = failure;
    }

    public void Stop_Failing_Updates()
    {
        lock (_lock)
            _updatesFailure = null;
    }

    public void Fail_Sends_Containing(string fragment)
    {
        lock (_lock)
            _failSendsContaining = fragment;
    }

    readonly Dictionary<string, RateLimitScript_Fake> _sendRateLimitsByFragment = [];
    readonly Dictionary<long, RateLimitScript_Fake> _editRateLimitsByMessageId = [];

    /// <summary>
    /// A REAL 429, SCRIPTABLE BY TEXT FRAGMENT — the gap <see cref="Fail_Sends_Containing"/> cannot
    /// close.
    ///
    /// <para>
    /// WHY A PLAIN <see cref="Exception"/> WAS NOT ENOUGH. Every failure classifier in the engine —
    /// <c>TelegramAttempt_Gate.Classify_Failure</c>, <c>TopicDelete_Decider.Classify</c>,
    /// <c>RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull</c> — pattern-matches on
    /// <see cref="TelegramApiException"/> and reads its status code; a bare <see cref="Exception"/>
    /// carries no status, so every one of those classifiers reads it as a hard, permanent refusal.
    /// <see cref="Fail_Sends_Containing"/> can therefore only ever produce "Telegram said no", never
    /// "Telegram said slow down" — and before this method, no engine test could exercise a 429 path
    /// at all. <c>TheBridgeNeverLiesAboutDeliveryTests</c> says so outright.
    /// </para>
    /// <para>
    /// OPT-IN AND SEPARATE from <see cref="Fail_Sends_Containing"/>, which keeps its exact behaviour
    /// — existing tests depend on the plain-Exception "hard failure" outcome, and nothing here
    /// changes what fragment maps to what. This is the OTHER outcome: Telegram is still there,
    /// answering, and asking to wait.
    /// </para>
    /// <para>
    /// <paramref name="failFirstAttempts"/> null means "fails on every matching attempt, forever" —
    /// for a test proving a ceiling is honoured (the failure must be handed back, never slept
    /// through, however many times it is asked). A number N means "the first N attempts are
    /// rate-limited, the (N+1)th succeeds" — for a test proving a retry actually lands.
    /// <see cref="Count_SendAttempts_Containing"/> reports how many matching attempts were made,
    /// which is the number every back-off test is actually about.
    /// </para>
    /// </summary>
    public void Rate_Limit_Sends_Containing(string fragment, int retryAfterSeconds, int? failFirstAttempts = null)
    {
        lock (_lock)
            _sendRateLimitsByFragment[fragment] = new RateLimitScript_Fake(retryAfterSeconds, failFirstAttempts);
    }

    /// <summary>
    /// The edit twin of <see cref="Rate_Limit_Sends_Containing"/> — keyed by message id rather than
    /// by text fragment, because an edit is identified by WHICH message it rewrites, not by what it
    /// rewrites it to (two edits of the same status line carry the same text on purpose).
    /// </summary>
    public void Rate_Limit_Edits_Of(long messageId, int retryAfterSeconds, int? failFirstAttempts = null)
    {
        lock (_lock)
            _editRateLimitsByMessageId[messageId] = new RateLimitScript_Fake(retryAfterSeconds, failFirstAttempts);
    }

    /// <summary>How many times a send matching <paramref name="fragment"/> was attempted — the count every back-off test asserts on.</summary>
    public int Count_SendAttempts_Containing(string fragment)
    {
        lock (_lock)
            return _sendRateLimitsByFragment.TryGetValue(fragment, out var script) ? script.Attempts : 0;
    }

    /// <summary>How many times an edit of <paramref name="messageId"/> was attempted.</summary>
    public int Count_EditAttempts_Of(long messageId)
    {
        lock (_lock)
            return _editRateLimitsByMessageId.TryGetValue(messageId, out var script) ? script.Attempts : 0;
    }

    public int Count_Sent_Containing(string fragment)
    {
        lock (_lock)
            return _sentTexts.Count(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public string? Find_SentContaining(string fragment)
    {
        lock (_lock)
            return _sentTexts.LastOrDefault(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public bool Has_Edited_Containing(string fragment)
    {
        lock (_lock)
            return _editedTexts.Any(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public string Dump_Sent()
    {
        lock (_lock)
            return string.Join($"{Environment.NewLine}--- sent ---{Environment.NewLine}", _sentTexts);
    }

    public string? Find_ButtonFor(string labelFragment)
    {
        lock (_lock)
            return _buttons.FirstOrDefault(button => button.Label.Contains(labelFragment, StringComparison.Ordinal)).Data;
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        string? queued;
        Exception? failure;

        lock (_lock)
        {
            UpdateCalls++;
            LastRequestedOffset = offset;
            failure = _updatesFailure;
            queued = _queuedUpdatesJson;
            _queuedUpdatesJson = null;
        }

        // A FAILURE IS INSTANT, like a refused HTTP call — but it is still paced, because the 409
        // path retries on its own backoff and a fake that throws instantly in a tight loop starves
        // the machine rather than testing the backoff.
        if (failure != null)
        {
            await Task.Delay(20, cancellationToken);
            throw failure;
        }

        if (queued != null)
            return queued;

        // The real call is a LONG POLL. Returning instantly spins this loop at full speed and
        // starves the mirror loop it shares a machine with — which is exactly what happened when
        // this fake was first written: three probes produced no output for minutes.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    string? _timeoutSendsContaining;

    /// <summary>
    /// Makes a matching send behave like an HttpClient TIMEOUT: a TaskCanceledException raised while
    /// the caller's token is NOT cancelled. That distinction is the whole point — code that rethrows
    /// OperationCanceledException unfiltered mistakes it for a shutdown.
    /// </summary>
    public void Timeout_Sends_Containing(string fragment)
    {
        lock (_lock)
            _timeoutSendsContaining = fragment;
    }

    long? Record(string text)
    {
        lock (_lock)
        {
            if (_failSendsContaining != null && text.Contains(_failSendsContaining, StringComparison.Ordinal))
                throw new Exception($"scripted send failure for text containing '{_failSendsContaining}'");

            if (_timeoutSendsContaining != null && text.Contains(_timeoutSendsContaining, StringComparison.Ordinal))
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 90 seconds elapsing.");

            foreach (var (fragment, script) in _sendRateLimitsByFragment)
            {
                if (!text.Contains(fragment, StringComparison.Ordinal))
                    continue;

                script.Attempts++;

                if (script.FailFirstAttempts == null || script.Attempts <= script.FailFirstAttempts)
                {
                    throw new TelegramApiException(
                        429,
                        $"Telegram 'sendMessage' failed with HTTP 429: {{\"description\":\"Too Many Requests: retry after {script.RetryAfterSeconds}\"}}",
                        retryAfterSeconds: script.RetryAfterSeconds);
                }

                break;
            }

            _sentTexts.Add(text);
            return _nextMessageId++;
        }
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.FromResult(Record(text));

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.FromResult(Record(html));

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Send_MessageWithButtons_Async(messageThreadId, html, buttons, sound, cancellationToken);

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        var messageId = Record(text);

        lock (_lock)
        {
            _buttons.AddRange(buttons);

            if (buttons.Any(button => button.Data.StartsWith(CallbackToken.PREFIX, StringComparison.Ordinal)))
                LastButtonMessageId = messageId;
        }

        return Task.FromResult(messageId);
    }

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        var flattened = buttonRows.SelectMany(row => row).ToList();
        return Send_MessageWithButtons_Async(messageThreadId, text, flattened, sound, cancellationToken);
    }

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_editRateLimitsByMessageId.TryGetValue(messageId, out var script))
            {
                script.Attempts++;

                if (script.FailFirstAttempts == null || script.Attempts <= script.FailFirstAttempts)
                {
                    return Task.FromException(new TelegramApiException(
                        429,
                        $"Telegram 'editMessageText' failed with HTTP 429: {{\"description\":\"Too Many Requests: retry after {script.RetryAfterSeconds}\"}}",
                        retryAfterSeconds: script.RetryAfterSeconds));
                }
            }

            _editedTexts.Add(text);
        }

        return Task.CompletedTask;
    }

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken) => Edit_MessageText_Async(messageId, html, cancellationToken);

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        lock (_lock)
            Documents.Add((fileName, content, captionHtml));

        return Task.CompletedTask;
    }

    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;

    byte[] _downloadBytes = [];

    /// <summary>What a download returns — set when a test asserts on the FILE that lands on disk.</summary>
    public void Set_DownloadBytes(byte[] bytes)
    {
        lock (_lock)
            _downloadBytes = bytes;
    }

    Exception? _downloadFailure;

    /// <summary>Makes every download fail — for the probes about what the owner is told when it does.</summary>
    public void Fail_Downloads_With(Exception failure)
    {
        lock (_lock)
            _downloadFailure = failure;
    }

    readonly List<(long MessageId, string? Emoji)> _reactions = [];
    string? _refuseReactionsReason;

    /// <summary>Every reaction set, in order — the receipt brief D replaced the ✓ message with.</summary>
    public IReadOnlyList<(long MessageId, string? Emoji)> Reactions
    {
        get { lock (_lock) return [.. _reactions]; }
    }

    /// <summary>Makes every reaction refused, the way Telegram refuses one it will not allow.</summary>
    public void Refuse_Reactions(string reason)
    {
        lock (_lock)
            _refuseReactionsReason = reason;
    }

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_refuseReactionsReason != null)
                return Task.FromException(new Exception(_refuseReactionsReason));

            _reactions.Add((messageId, emoji));
            return Task.CompletedTask;
        }
    }


    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken)
    {
        lock (_lock)
            return _downloadFailure == null ? Task.FromResult(_downloadBytes) : Task.FromException<byte[]>(_downloadFailure);
    }

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => Task.FromResult(1L);
    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    readonly List<(long MessageId, string Text, int ButtonCount, string Labels)> _buttonEdits = [];

    /// <summary>
    /// Every edit that rewrote a message AND its keyboard — which is how a probe sees whether the
    /// status line's four-row bar survived a tap or was replaced by a single button.
    /// </summary>
    public IReadOnlyList<(long MessageId, string Text, int ButtonCount, string Labels)> ButtonEdits
    {
        get { lock (_lock) return [.. _buttonEdits]; }
    }

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        lock (_lock)
            _buttonEdits.Add((messageId, text, buttons.Count, string.Join(" | ", buttons.Select(button => button.Label))));

        return Task.CompletedTask;
    }

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        lock (_lock)
            _buttonEdits.Add((messageId, text, buttonRows.Sum(row => row.Count), string.Join(" | ", buttonRows.SelectMany(row => row).Select(button => button.Label))));

        return Task.CompletedTask;
    }
    int _answeredCallbacks;

    /// <summary>How many taps were answered — a tap left unanswered spins on the owner's phone.</summary>
    public int Answered_Callbacks
    {
        get { lock (_lock) return _answeredCallbacks; }
    }

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        lock (_lock)
            _answeredCallbacks++;

        return Task.CompletedTask;
    }
    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// ONE SCRIPTED 429: Telegram's own <c>retry_after</c>, whether it ever stops, and how many times it
/// has been asked for so far. Mutable by design — <see cref="ScriptedInbound_Fake.Record"/> and
/// <see cref="ScriptedInbound_Fake.Edit_MessageText_Async"/> increment <see cref="Attempts"/> under
/// the fake's own lock, which is the count every back-off test is actually about.
/// </summary>
internal sealed class RateLimitScript_Fake(int retryAfterSeconds, int? failFirstAttempts)
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
    public int? FailFirstAttempts { get; } = failFirstAttempts;
    public int Attempts;
}

/// <summary>
/// A WINDOWING CAPABILITY THAT THROWS THE INCIDENT'S OWN EXCEPTION. `DllNotFoundException:
/// user32.dll` is what the Linux daemon raised on 2026-09-08 when `/show` reached the Windows
/// P/Invoke; nothing that ships behaves this way (the unsupported implementation answers "no"), and
/// the point of the fake is the case nobody can rule out — a host call that fails for a reason its
/// author did not foresee.
/// </summary>
internal sealed class ThrowingHostWindowing_Fake : AIOrchestratorCoreLib.Hosting.HostWindowing.IHostWindowing
{
    public bool Is_Supported => true;

    public string? Find_OwnerFacingWindow_OrNull(AIOrchestratorCoreLib.Sessions.OrchestrationSession.IOrchestrationSession session)
        => throw new DllNotFoundException("Unable to load shared library 'user32.dll'");

    public bool Try_Focus(string titleFragment)
        => throw new DllNotFoundException("Unable to load shared library 'user32.dll'");

    public Task<string?> Try_Capture_Async(string titleFragment, string imagePath, CancellationToken cancellationToken)
        => throw new DllNotFoundException("Unable to load shared library 'gdi32.dll'");

    public int Organize(AIOrchestratorCoreLib.Sessions.OrchestrationSession.IOrchestrationSession session)
        => throw new DllNotFoundException("Unable to load shared library 'user32.dll'");

    public int Organize_MainWindows(IReadOnlyList<AIOrchestratorCoreLib.Sessions.OrchestrationSession.IOrchestrationSession> openSessions)
        => throw new DllNotFoundException("Unable to load shared library 'user32.dll'");
}

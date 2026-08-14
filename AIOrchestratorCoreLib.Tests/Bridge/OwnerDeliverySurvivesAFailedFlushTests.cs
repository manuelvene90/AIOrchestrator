using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Channels;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Translation.MessageTranslator;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// AN OWNER MESSAGE MUST SURVIVE EVERY WAY OUT OF THE FLUSH, NOT JUST THE APPEND'S OWN FAILURE.
/// <para>
/// <c>Take_ReadyDeliveries</c> empties the buffer for the WHOLE BATCH before the loop body runs, so
/// from that moment the local variables are the only copy of the owner's words. The append's failure
/// was covered by R1's put-back; the other routes out were not — a translator that throws destroyed
/// the text outright, and any escape from the loop destroyed every delivery still to come in the
/// batch along with it.
/// </para>
/// <para>
/// THE TRANSLATOR IS THE ROUTE USED HERE because it is the one that runs BEFORE the append and after
/// the drain — the exact window where the message exists nowhere else. It reaches the loop through a
/// factory seam rather than a race, so nothing here depends on timing.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class OwnerDeliverySurvivesAFailedFlushTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    const string FIRST_TEXT = "did the overnight rebuild finish";
    const string SECOND_TEXT = "and is the ledger clean yet";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;
    readonly ThrowingTranslator_Fake _translator;

    public OwnerDeliverySurvivesAFailedFlushTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-flush-escape-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The Italian layer is ON here, unlike every other engine test: it is what puts the
        // translator on the delivery path at all, and the translator is the escape being pinned.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":true}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new FailableTelegram_Fake();
        _translator = new ThrowingTranslator_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithTelegramClientAndTranslator(
            _paths, configProvider, _store, _launcher, _log, _telegram, _translator);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// The two halves fail for disjoint reasons: while the translator throws the text is absent from
    /// the channel and the engine says it kept it; once the translator recovers the SAME text
    /// arrives, which can only happen if it was put back.
    /// <para>
    /// It also pins that the ORIGINAL is put back, not a half-translated working copy — the assertion
    /// is on the owner's exact words.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATranslatorThatThrowsDoesNotDestroyTheOwnersMessage()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(FIRST_TEXT, 4101));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Line_Containing("failed mid-delivery"), 40_000),
            "the flush never reached a throwing translator, so nothing below means anything."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.DoesNotContain(FIRST_TEXT, File.ReadAllText(ownerChannel));

        // The only copy is now whatever the engine kept. Before this fix the throw escaped the loop
        // and the owner's words existed nowhere.
        _translator.Stop_Throwing();

        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(ownerChannel).Contains(FIRST_TEXT), 40_000),
            "THE DEFECT: the owner's message was destroyed by a failure BETWEEN the drain and the append. "
            + "Take_ReadyDeliveries had already emptied the buffer, so an escape from the loop was the end of it."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// A SECOND message must not die because a FIRST one failed. The whole batch leaves the buffer
    /// together, so an escape from the loop used to take every delivery still to come with it — the
    /// route that survives imp-7's catch filter through the cancellation path.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task OneFailedDeliveryDoesNotTakeTheRestOfTheBatchDownWithIt()
    {
        var first = _launcher.Start_Orchestration("RepoOne", _tempRepo);
        var second = _launcher.Start_Orchestration("RepoTwo", _tempRepo);

        _store.Set_TelegramTopicId(first.OrchId, TOPIC_ID);
        _store.Set_TelegramTopicId(second.OrchId, TOPIC_ID + 1);

        Seed_OwnerChannel(first.OrchId);
        Seed_OwnerChannel(second.OrchId);

        // Only the FIRST orchestration's delivery throws; the second must be unaffected.
        _translator.Throw_OnlyFor(FIRST_TEXT);

        // BOTH in ONE update batch, which is both how Telegram really delivers them and what puts
        // them in the SAME drained batch — the exact state where an escape destroyed the one behind.
        // (Queue_OwnerMessage REPLACES rather than appends, so two calls would only queue the second.)
        _telegram.Queue_OwnerMessage(Build_TwoOwnerMessagesJson());

        var secondChannel = _paths.Get_OwnerChannelFile(second.OrchId);

        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(secondChannel).Contains(SECOND_TEXT), 40_000),
            "THE DEFECT: a delivery that failed took the rest of the batch with it. Every delivery had already "
            + "been removed from the buffer, so the ones behind the failure were never re-delivered."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // And the failed one is still safe rather than traded away for the second.
        Assert.True(
            _log.Has_Line_Containing("failed mid-delivery"),
            $"the first delivery did not fail, so this proves nothing.{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// THE CANCELLATION ROUTE SPECIFICALLY, which the case above does NOT cover and which I claimed
    /// before I had pinned it.
    /// <para>
    /// The receipt block's <c>catch (OperationCanceledException) { throw; }</c> sits INSIDE the
    /// <c>foreach</c>, so a cancellation-shaped failure escaped the loop and destroyed every delivery
    /// behind it — the route that survives imp-7's catch filter. The case above throws
    /// <c>InvalidOperationException</c> and therefore leaves that route untested: restoring the
    /// rethrow left it green.
    /// </para>
    /// <para>
    /// <c>TaskCanceledException</c> is the honest shape rather than a contrivance — it is what an
    /// <c>HttpClient</c> TIMEOUT throws, and the translator is an HTTP call.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ACancellationShapedFailureDoesNotTakeTheRestOfTheBatchDownWithIt()
    {
        var first = _launcher.Start_Orchestration("RepoOne", _tempRepo);
        var second = _launcher.Start_Orchestration("RepoTwo", _tempRepo);

        _store.Set_TelegramTopicId(first.OrchId, TOPIC_ID);
        _store.Set_TelegramTopicId(second.OrchId, TOPIC_ID + 1);

        Seed_OwnerChannel(first.OrchId);
        Seed_OwnerChannel(second.OrchId);

        _translator.Throw_OnlyFor(FIRST_TEXT);
        _translator.Throw_AsCancellation();

        _telegram.Queue_OwnerMessage(Build_TwoOwnerMessagesJson());

        var secondChannel = _paths.Get_OwnerChannelFile(second.OrchId);

        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(secondChannel).Contains(SECOND_TEXT), 40_000),
            "THE DEFECT: a cancellation-shaped failure escaped the loop and took the rest of the batch with it. "
            + "An HttpClient timeout in the translator throws exactly this, so it is the ordinary case rather than "
            + "the exotic one."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.True(
            _log.Has_Line_Containing("failed mid-delivery"),
            $"the first delivery did not fail, so this proves nothing.{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// THE PUT-BACK MUST RESTORE THE OWNER'S WORDS, NOT THE TRANSLATION OF THEM. rev-9's F1.
    /// <para>
    /// The locked-channel route put back <c>deliveryText</c>, which with the Italian layer on is the
    /// translator's OUTPUT. The buffer then held a machine translation instead of the owner's message,
    /// and the retry ran that through the translator again — the owner's words replaced by a
    /// paraphrase of themselves, and re-paraphrased on every subsequent lock.
    /// </para>
    /// <para>
    /// The translator here MARKS its output, so a second pass is visible as a second mark. Counting
    /// marks is what makes this an assertion about fidelity rather than about delivery: the message
    /// arrives either way, which is exactly why the defect survived a route classified as covered.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ALockedChannelPutsBackTheOWNERSWords_NotTheTranslationOfThem()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(ownerChannel);

        Directory.CreateDirectory(lockDirectory);
        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session", "another-writer"));

        // Translates rather than throws: this route is about fidelity, not about escaping.
        _translator.Stop_Throwing();
        _translator.Mark_Translations();

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(FIRST_TEXT, 4301));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Line_Containing("stayed locked by another writer"), 40_000),
            $"the delivery never hit a locked channel, so nothing below means anything.{Environment.NewLine}{_log.Dump()}");

        Directory.Delete(lockDirectory, recursive: true);

        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(ownerChannel).Contains(FIRST_TEXT), 40_000),
            $"the message never arrived at all.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var marks = File.ReadAllText(ownerChannel).Split(ThrowingTranslator_Fake.TRANSLATION_MARK).Length - 1;

        Assert.Equal(1, marks);
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>Two owner messages in one poll, bound for two different topics.</summary>
    static string Build_TwoOwnerMessagesJson()
    {
        return "{\"ok\":true,\"result\":["
            + Build_MessageObject(FIRST_TEXT, 4201, TOPIC_ID) + ","
            + Build_MessageObject(SECOND_TEXT, 4202, TOPIC_ID + 1)
            + "]}";
    }

    static string Build_MessageObject(string text, int updateId, long topicId)
    {
        return $"{{\"update_id\":{updateId},\"message\":{{\"message_id\":{updateId},"
            + $"\"message_thread_id\":{topicId},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}";
    }

    static string Build_OwnerMessageJson(string text, int updateId, long topicId = TOPIC_ID)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{updateId},"
            + $"\"message_thread_id\":{topicId},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(100);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}

/// <summary>
/// Fails the way a translator really can — a network call that throws — so the escape between the
/// drain and the append is reachable without racing anything.
/// </summary>
internal sealed class ThrowingTranslator_Fake : IMessageTranslator
{
    /// <summary>Appended to every translation, so a SECOND pass over the same text is countable.</summary>
    public const string TRANSLATION_MARK = " [EN]";

    readonly object _lock = new();
    bool _throwing = true;
    bool _asCancellation;
    bool _marking;
    string? _onlyForText;

    public void Mark_Translations()
    {
        lock (_lock)
            _marking = true;
    }

    public void Stop_Throwing()
    {
        lock (_lock)
            _throwing = false;
    }

    /// <summary>What an <c>HttpClient</c> timeout actually throws, and the shape that used to escape.</summary>
    public void Throw_AsCancellation()
    {
        lock (_lock)
            _asCancellation = true;
    }

    public void Throw_OnlyFor(string text)
    {
        lock (_lock)
            _onlyForText = text;
    }

    public Task<string> Translate_ToEnglish_Async(string text, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_throwing && (_onlyForText == null || text.Contains(_onlyForText, StringComparison.Ordinal)))
            {
                if (_asCancellation)
                    throw new TaskCanceledException("translation timed out");

                throw new InvalidOperationException("translation service unreachable");
            }
        }

        lock (_lock)
            return Task.FromResult(_marking ? text + TRANSLATION_MARK : text);
    }

    public Task<string> Translate_ToItalian_Async(string text, CancellationToken cancellationToken)
    {
        return Task.FromResult(text);
    }
}

using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Channels;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// AN OWNER MESSAGE MUST SURVIVE EVERY WAY OUT OF THE FLUSH, NOT JUST THE APPEND'S OWN "STAYED
/// LOCKED" ANSWER — restored 2026-09-09 after the Telegram translation layer that used to prove it
/// was abolished (owner directive) and its test file, <c>OwnerDeliverySurvivesAFailedFlushTests</c>,
/// went with it.
/// <para>
/// THE ENGINE CARRIES TWO SEPARATE SAFETY NETS FOR THIS, and the deleted file proved the second one,
/// which nothing else in the suite reaches:
/// <list type="bullet">
/// <item>
/// <c>Deliver_OwnerMessage_Async</c>'s own branch — <c>ChannelAppender.Append_OwnerEntry</c> RETURNS
/// false when the channel stayed cooperatively locked for its whole budget. This is what
/// <c>OwnerMessageSurvivesALockedChannelTests</c> already covers, untouched by the deletion, using
/// <c>ChannelFile_Lock</c>'s lock DIRECTORY the way another writer would hold it.
/// </item>
/// <item>
/// <c>Flush_OwnerDeliveries_Async</c>'s outer <c>catch (Exception exception)</c> — reached only when
/// <c>Deliver_OwnerMessage_Async</c> THROWS instead of returning false. The translator was the only
/// thing that ran, awaited, BEFORE the append in that window, so it was the only reachable way to
/// make that happen. With it gone, nothing between the buffer drain and the append is async or
/// throws — with one exception: the append itself.
/// </item>
/// </list>
/// </para>
/// <para>
/// THE REPLACEMENT INJECTOR: an OS-level exclusive handle held on the channel file itself
/// (<c>FileShare.None</c>), so <c>ChannelAppender</c>'s <c>File.ReadAllText</c> /
/// <c>File.AppendAllText</c> throw a genuine <see cref="IOException"/> from inside the write lambda —
/// which <c>ChannelWrite_Lock.Try_Run_Serialised</c> and <c>ChannelFile_Lock.Try_Run_WithLock</c> do
/// NOT catch, so it propagates out of <c>Append_OwnerEntry</c> and out of
/// <c>Deliver_OwnerMessage_Async</c>, landing in exactly the outer catch the translator used to reach.
/// This is not a new technique for this suite: <c>MeetingDefersAlertsProbeTests
/// .AnAppendThatTHROWS_DoesNotTakeTheRestOfTheTickWithIt</c> and
/// <c>.ANudgeWhoseAppendFAILED_IsDeliveredOnTheNextTick</c> already provoke the identical throw on the
/// same owner channel file for a different append site, and <c>ChannelTailerTests</c> and
/// <c>OrchestrationLogTests</c> use the same handle un-gated by <c>RequiresFileShareEnforcementFact</c>
/// — that gate is for POSIX's unenforced RENAME/DELETE semantics, not for this: opening a file for
/// read or append against an exclusive handle IS enforced by .NET on this machine, confirmed by
/// running it before trusting it.
/// </para>
/// <para>
/// ONE CONSEQUENCE OF USING THE REAL FILE, worth stating so nobody "fixes" it later: with our own
/// handle holding <c>FileShare.None</c>, THIS TEST cannot read the channel file either while the
/// handle is open — a second <c>File.ReadAllText</c> from the same process hits the identical
/// sharing violation. So unlike the cooperative-lock tests, the "the text is not there yet" half of
/// the assertion is dropped in favour of the log line that says the append was attempted and failed;
/// the file is only read again after the handle is disposed. <c>MeetingDefersAlertsProbeTests</c>
/// follows the same discipline for the same reason.
/// </para>
/// <para>
/// PROPERTY 3, "A CANCELLATION-SHAPED FAILURE BEHAVES LIKE ANY OTHER FAILURE, NOT A SHUTDOWN", IS
/// NOT RESTORED HERE, and it is not faked. The deleted
/// <c>ACancellationShapedFailureDoesNotTakeTheRestOfTheBatchDownWithIt</c> relied on the translator
/// throwing a <c>TaskCanceledException</c> in the same pre-append window this file's injector also
/// reaches — but an exclusive file handle can only produce an <see cref="IOException"/>, never an
/// <c>OperationCanceledException</c>, and nothing else between the buffer drain and the append is
/// async, takes a <c>CancellationToken</c>, or can throw one. Reading the current code is enough to
/// see that the property still HOLDS: <c>Flush_OwnerDeliveries_Async</c>'s catch is
/// <c>catch (Exception exception)</c> with no token filter and an explicit comment — "CONTINUE,
/// deliberately, including on cancellation" — so it cannot discriminate by exception type. What it
/// takes to PROVE that with a test, rather than read it, is a seam that runs something awaitable and
/// interruptible in that exact window again — which is production surface this task was not asked to
/// add, and adding one purely to manufacture a cancellation for a test would be exactly the kind of
/// seam CLAUDE.md decision 22 calls scope creep. Flagged here rather than silently dropped.
/// </para>
/// <para>
/// PROPERTY 4, "THE ORDER THE OWNER SENT THINGS IN SURVIVES A FAILURE AND A RETRY", never depended
/// on the translator and was never deleted: <c>OwnerDeliveryBufferTests
/// .APutBackLandsAHEADOfAMessageThatArrivedWhileItWasOut</c> and
/// <c>.TwoPutBacksComeOutChronological_WHICHEVEROfThemLandsFirst</c> already pin the ordinal
/// mechanism directly against the buffer, including the harder case (a second message landing WHILE
/// the first is out) that a translator-driven engine test never touched either. This file adds one
/// end-to-end version of the same property, driven through the real engine and this file's own
/// injector, so the property is proven at both levels rather than only at the unit level.
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

    public OwnerDeliverySurvivesAFailedFlushTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-flush-escape-{Guid.NewGuid():N}");
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
        _log = new RecordingLog_Fake();
        _telegram = new FailableTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// PROPERTY 1 — a delivery that cannot be written puts the owner's words back, unchanged, and
    /// they are delivered on a later pass. This exercises <c>Flush_OwnerDeliveries_Async</c>'s OUTER
    /// catch specifically (the append THROWS), which is the half <c>OwnerMessageSurvivesALockedChannelTests</c>
    /// does not reach — that file's cooperative lock makes the append return false instead, one level
    /// further in.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ALockedFileThatThrowsDoesNotDestroyTheOwnersMessage()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);

        using (File.Open(ownerChannel, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(FIRST_TEXT, 4101));

            Assert.True(
                await Run_Until_Async(() => _log.Has_Line_Containing("failed mid-delivery"), 40_000),
                "the flush never reached a throwing append, so nothing below means anything."
                + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

            // Cannot read ownerChannel here: our own exclusive handle refuses even our own reads.
            // The log line above is the only witness available while the handle is open — see the
            // class doc comment.
        }

        // The only copy is now whatever the engine kept. Before this fix an escape from the loop
        // (or a fall-through on the append's own failure) was the end of it.
        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(ownerChannel).Contains(FIRST_TEXT), 40_000),
            "THE DEFECT: the owner's message was destroyed by a failure BETWEEN the drain and the append. "
            + "Take_ReadyDeliveries had already emptied the buffer, so an escape from the loop was the end of it."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// PROPERTY 2 — one failed delivery does not take the rest of the batch down with it. Both
    /// orchestrations' owner messages arrive in ONE Telegram update batch — both how Telegram really
    /// delivers them and what puts them in the SAME drained batch, the exact state where an escape
    /// used to destroy the one behind. Only the FIRST orchestration's channel file is held exclusively.
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

        var firstChannel = _paths.Get_OwnerChannelFile(first.OrchId);
        var secondChannel = _paths.Get_OwnerChannelFile(second.OrchId);

        using (File.Open(firstChannel, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            // Queue_OwnerMessage REPLACES rather than appends, so two calls would only queue the
            // second — both messages have to travel in the one batch below.
            _telegram.Queue_OwnerMessage(Build_TwoOwnerMessagesJson());

            Assert.True(
                await Run_Until_Async(() => File.ReadAllText(secondChannel).Contains(SECOND_TEXT), 40_000),
                "THE DEFECT: a delivery that failed took the rest of the batch with it. Every delivery had already "
                + "been removed from the buffer, so the ones behind the failure were never re-delivered."
                + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                _log.Has_Line_Containing("failed mid-delivery"),
                $"the first delivery did not fail, so this proves nothing.{Environment.NewLine}{_log.Dump()}");
        }

        // Not traded away for the second: once the obstruction clears, the first is still owed and
        // still arrives.
        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(firstChannel).Contains(FIRST_TEXT), 40_000),
            $"the first message was lost rather than retried.{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// PROPERTY 4, end to end — the SECOND message arrives WHILE the first is stuck behind the same
    /// locked file, which is the harder interleaving <c>OwnerDeliveryBufferTests
    /// .APutBackLandsAHEADOfAMessageThatArrivedWhileItWasOut</c> already pins at the buffer level.
    /// Here the same shape runs through the real engine and this file's own failure injector: if
    /// <c>Restore_Segment</c>'s ordinal argument were ever dropped, or the put-back started
    /// prepending instead of relying on the ordinal sort, the combined entry would read
    /// "SECOND_TEXT" before "FIRST_TEXT" — the owner's later "actually, X" landing above the
    /// message it was meant to follow.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheOrderTheOwnerSentThingsInSurvivesAFailedFlushAndARetry()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);

        using (File.Open(ownerChannel, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(FIRST_TEXT, 4201));

            // Run #1: buffer FIRST_TEXT, let the aggregation window close, watch its flush fail and
            // get put back — the engine is stopped again the moment this returns.
            Assert.True(
                await Run_Until_Async(() => _log.Has_Line_Containing("failed mid-delivery"), 40_000),
                "the first message never failed on its own, so queuing the second below proves nothing about ordering."
                + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

            // The owner speaks again while the first message is still stuck behind the locked file.
            // Queuing it now, with the engine stopped, is fine — Get_UpdatesJson_Async just hands it
            // back on the next poll, whenever that is.
            _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(SECOND_TEXT, 4202));

            // Run #2: the inbound loop picks SECOND_TEXT up and adds it to the SAME pending entry —
            // Release() from run #1's put-back already marked it ReleaseRequested, so the very next
            // tick tries to flush BOTH segments together, and fails again for the same locked reason.
            // A second "failed mid-delivery" line is the only externally visible proof that the
            // second segment actually joined the batch before the handle below is released.
            Assert.True(
                await Run_Until_Async(() => Count_Occurrences(_log.Dump(), "failed mid-delivery") >= 2, 40_000),
                "the second message never joined the stuck delivery, so the retry below proves nothing about ordering."
                + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
        }

        // Run #3: the handle is gone, so this retry's append finally lands.
        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(ownerChannel).Contains(SECOND_TEXT), 40_000),
            $"the second message never arrived at all.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var channelText = File.ReadAllText(ownerChannel);

        Assert.Contains(FIRST_TEXT, channelText);
        Assert.True(
            channelText.IndexOf(FIRST_TEXT, StringComparison.Ordinal) < channelText.IndexOf(SECOND_TEXT, StringComparison.Ordinal),
            $"THE DEFECT: the retry delivered the owner's words out of order.{Environment.NewLine}Channel:{Environment.NewLine}{channelText}");
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
            + Build_MessageObject(FIRST_TEXT, 4301, TOPIC_ID) + ","
            + Build_MessageObject(SECOND_TEXT, 4302, TOPIC_ID + 1)
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

    static int Count_Occurrences(string haystack, string needle)
    {
        return haystack.Split(needle, StringSplitOptions.None).Length - 1;
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

using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE TURN-END DIGEST IS WHAT THE SESSION SAID SINCE THE OWNER SPOKE — AND SINCE THE LAST THING IT
/// SENT THEM — nothing older (plan 03 Task 2, <c>phone.push = filtered</c>).
///
/// <para>
/// TWO FORGETS DRAW THAT LINE, and each fact here has exactly one of them as its only route (decision
/// 20 — a fact with both routes would stay green with either deleted):
/// </para>
/// <list type="bullet">
/// <item><c>Raise_OwnerWait</c> forgets what was held before the owner spoke. Narration from a
/// conversation that has already moved on, replayed under their NEW question, would answer something
/// they did not just ask — master's words, which it drew with a timestamp against the reply's delivery.
/// Its fact has NO entry sent after the owner speaks, so the second forget never runs.</item>
/// <item>A SEND forgets what was held before it (ruling R7, task-2 fix round 1 — master's clear-on-send
/// at 58ff547). A "WAITING ON …" status line held seconds before the answer is stale the moment the
/// answer goes out; replayed under it at turn end it is the 2026-09-10 failure decision 25 describes.
/// It also keeps a retried append from filing the same held entries twice. Its fact has no entry
/// before the owner speaks, so the first forget has nothing to do.</item>
/// </list>
/// <para>
/// The harness is <c>AStatusLineDoesNotSpendTheOwnersWaitTests</c>': the session is held MID-TURN by a
/// transcript until the last entry has been tailed, because the turn-ended announcement is the polled
/// transition out of that state. The fixture writes no <c>preset</c>, so it runs under classic —
/// <c>filtered</c>.
/// </para>
/// </summary>
public class TheTurnEndDigestStartsAtTheOwnersMessageTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445522;
    const long OWNER_USER_ID = 555000133;
    const long TOPIC_ID = 4848;

    /// <summary>Plain narration written BEFORE the owner's message — held, then forgotten when they speak.</summary>
    const string BEFORE_THE_OWNER_SPOKE = "Rebased the staging branch onto master; nothing is waiting on you.";

    /// <summary>
    /// A reply that is only a turn-end declaration: held under filtered even with the credit open, so
    /// nothing is SENT after the owner speaks and the send-forget never runs.
    /// </summary>
    const string HELD_REPLY = "Read you: the re-review is still running, the roll rule comes after it.";

    /// <summary>A status line written after the owner spoke but BEFORE the answer — held, then stale.</summary>
    const string STATUS_BEFORE_THE_ANSWER = "Task 6 fix round landed: 116 runtime tests, review running.";

    /// <summary>Carries no question and no marker: the owner's wait is its only route to the phone.</summary>
    const string ANSWER_TEXT = "Agreed, the roll rule defaults to the front month for every root.";

    /// <summary>A status line written after the answer — held, and owed to the owner in the completion.</summary>
    const string AFTER_THE_ANSWER = "Your message is answered above; the re-review is still running.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public TheTurnEndDigestStartsAtTheOwnersMessageTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-digest-start-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The inbound loop reads the chat and owner ids and throws without them.
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
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>The <c>Raise_OwnerWait</c> forget, alone: nothing is sent between the owner's message and the turn end.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderFiltered_TheCompletionCarriesWhatFollowedTheOwnersMessage_AndNothingHeldBeforeIt()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();
        Mark_SessionMidTurn(orchId);

        // 1 — narration while nobody is waiting: held, and never sent on its own.
        Append_SupervisorEntry(orchId, 1, "progress", BEFORE_THE_OWNER_SPOKE);

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("entry #1 FROM Supervisor"), 20_000),
            $"the earlier narration was never tailed, so it was never held and this test reaches nothing.{Environment.NewLine}{_log.Dump()}");

        // 2 — the owner speaks. Their credit is raised as the message lands, and what was held before it
        // is forgotten there.
        Deliver_OwnerMessage("which month does the roll rule use");

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message delivered"), BridgeTestTiming.Window_ForAggregation(60)),
            $"the owner's message was never delivered, so the credit was never raised.{Environment.NewLine}{_log.Dump()}");

        // 3 — a reply that is only a turn-end declaration. Held, not sent: that is what keeps the
        // send-forget out of this fact.
        Append_SupervisorEntry(orchId, 3, "WAITING ON the re-review - roll rule after it", HELD_REPLY);

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("entry #3 FROM Supervisor"), 20_000),
            $"the held reply was never tailed.{Environment.NewLine}{_log.Dump()}");

        Assert.False(
            _telegram.Has_Sent_Containing(HELD_REPLY),
            "the turn-end declaration was SENT, so a send-forget ran in this fact and it no longer isolates Raise_OwnerWait's.");

        // 4 — the turn ends.
        Mark_SessionIdle(orchId);

        var completion = await Wait_ForCompletion_Async(HELD_REPLY);

        Assert.DoesNotContain(BEFORE_THE_OWNER_SPOKE, completion, StringComparison.Ordinal);

        Assert.False(
            _telegram.Has_Sent_Containing(BEFORE_THE_OWNER_SPOKE),
            "narration held before the owner spoke reached the phone — the digest replayed a conversation that had moved on."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}");
    }

    /// <summary>The send-forget, alone (ruling R7): nothing is held before the owner speaks.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderFiltered_TheCompletionCarriesNothingHeldBeforeTheAnswerWasSent()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();
        Mark_SessionMidTurn(orchId);

        Deliver_OwnerMessage("which month does the roll rule use");

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message delivered"), BridgeTestTiming.Window_ForAggregation(60)),
            $"the owner's message was never delivered, so the credit was never raised.{Environment.NewLine}{_log.Dump()}");

        // The session's boundary in the order sessions write it: status, answer, status. Indices follow
        // the owner's entry, which the bridge numbered [1].
        Append_SupervisorEntry(orchId, 2, "WAITING ON the task 6 re-review - fix landed 7af0aafe", STATUS_BEFORE_THE_ANSWER);
        Append_SupervisorEntry(orchId, 3, "roll rule defaults", ANSWER_TEXT);
        Append_SupervisorEntry(orchId, 4, "WAITING ON the task 6 re-review - answered in 3", AFTER_THE_ANSWER);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(ANSWER_TEXT) && _log.Has_Info_Containing("entry #4 FROM Supervisor"), 20_000),
            $"the answer never reached the phone, or the status line after it was never tailed.{Environment.NewLine}{_log.Dump()}");

        Mark_SessionIdle(orchId);

        var completion = await Wait_ForCompletion_Async(AFTER_THE_ANSWER);

        Assert.DoesNotContain(STATUS_BEFORE_THE_ANSWER, completion, StringComparison.Ordinal);

        Assert.False(
            _telegram.Has_Sent_Containing(STATUS_BEFORE_THE_ANSWER),
            "the status line held before the answer reached the phone — stale words replayed after the answer they preceded."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}");
    }

    async Task<string> Wait_ForCompletion_Async(string heldFragment)
    {
        Assert.True(
            await Run_Until_Async(() => Find_TurnEndedCompletion_OrNull(heldFragment) != null, 30_000),
            "the turn ended and the words held since the owner spoke never reached them — no digest was built."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}"
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        return Find_TurnEndedCompletion_OrNull(heldFragment)!;
    }

    string? Find_TurnEndedCompletion_OrNull(string fragment)
    {
        return _telegram.Sent_Texts()
            .FirstOrDefault(text => text.Contains("turn ended", StringComparison.Ordinal) && text.Contains(fragment, StringComparison.Ordinal));
    }

    void Deliver_OwnerMessage(string text)
    {
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(text));
    }

    /// <summary>A transcript whose last activity is NOW — the status line's probe reads that as mid-turn.</summary>
    void Mark_SessionMidTurn(string orchId)
    {
        Write_SessionActivity(orchId, DateTime.UtcNow);
    }

    /// <summary>The same transcript, ten minutes stale — well past the probe's 120 s of quiet.</summary>
    void Mark_SessionIdle(string orchId)
    {
        Write_SessionActivity(orchId, DateTime.UtcNow.AddMinutes(-10));
    }

    void Write_SessionActivity(string orchId, DateTime lastActivityUtc)
    {
        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, orchId, _store.Get_Session_OrNull(orchId));
        var sessionFolder = Path.GetDirectoryName(usageFile) ?? throw new Exception($"usage file '{usageFile}' has no folder");

        Directory.CreateDirectory(sessionFolder);

        var transcriptPath = Path.Combine(sessionFolder, "transcript.jsonl");
        var stamp = lastActivityUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        File.WriteAllText(transcriptPath, $$"""{"type":"assistant","timestamp":"{{stamp}}","uuid":"u"}""");
        File.WriteAllText(usageFile, $$"""{"transcript_path":"{{transcriptPath.Replace("\\", "\\\\")}}","version":"2.1.229"}""");
    }

    /// <summary>An unseen file is registered at its current end and emits nothing, so the channel is baselined first.</summary>
    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":6001,\"message\":{\"message_id\":131,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    async Task Run_For_Async(int milliseconds)
    {
        await Run_Until_Async(() => false, milliseconds);
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

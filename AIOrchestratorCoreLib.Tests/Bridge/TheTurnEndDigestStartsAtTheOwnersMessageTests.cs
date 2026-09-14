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
/// THE TURN-END DIGEST IS WHAT THE SESSION SAID SINCE THE OWNER SPOKE — nothing older (plan 03 Task 2,
/// <c>phone.push = filtered</c>).
///
/// <para>
/// Narration held while nobody was waiting belongs to a conversation that has already moved on.
/// Replayed under the completion of the owner's NEW question, it would answer something they did not
/// just ask — master's own words for the rule, which it drew with a timestamp against the reply's
/// delivery. This tree draws it at the same moment without one: <c>Raise_OwnerWait</c>, the one place
/// the owner's credit is raised, forgets what was held before it.
/// </para>
/// <para>
/// BOTH HALVES ARE ASSERTED ON THE SAME COMPLETION, so neither passes alone: the entry written after the
/// owner's message must be in it (the digest was built at all), and the one written before must not.
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
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("which month does the roll rule use"));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message delivered"), BridgeTestTiming.Window_ForAggregation(60)),
            $"the owner's message was never delivered, so the credit was never raised.{Environment.NewLine}{_log.Dump()}");

        // 3 — the answer (credited, sent) and a status line after it (held for the completion).
        Append_SupervisorEntry(orchId, 3, "roll rule defaults", ANSWER_TEXT);
        Append_SupervisorEntry(orchId, 4, "WAITING ON the re-review - answered in 3", AFTER_THE_ANSWER);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(ANSWER_TEXT) && _log.Has_Info_Containing("entry #4 FROM Supervisor"), 20_000),
            $"the answer never reached the phone, or the status line after it was never tailed.{Environment.NewLine}{_log.Dump()}");

        // 4 — the turn ends.
        Mark_SessionIdle(orchId);

        Assert.True(
            await Run_Until_Async(() => Find_TurnEndedCompletion_OrNull(AFTER_THE_ANSWER) != null, 30_000),
            "the turn ended and the status line held after the answer never reached the owner — no digest was built."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}"
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var completion = Find_TurnEndedCompletion_OrNull(AFTER_THE_ANSWER)!;

        Assert.DoesNotContain(BEFORE_THE_OWNER_SPOKE, completion, StringComparison.Ordinal);

        Assert.False(
            _telegram.Has_Sent_Containing(BEFORE_THE_OWNER_SPOKE),
            "narration held before the owner spoke reached the phone — the digest replayed a conversation that had moved on."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}");
    }

    string? Find_TurnEndedCompletion_OrNull(string fragment)
    {
        return _telegram.Sent_Texts()
            .FirstOrDefault(text => text.Contains("turn ended", StringComparison.Ordinal) && text.Contains(fragment, StringComparison.Ordinal));
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

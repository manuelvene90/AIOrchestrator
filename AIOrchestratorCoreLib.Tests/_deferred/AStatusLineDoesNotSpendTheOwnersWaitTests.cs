using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE OWNER'S ANSWER MUST SURVIVE A STATUS LINE WRITTEN JUST BEFORE IT — the 2026-09-10 incident.
///
/// The owner asks; the session, at its next boundary, first writes its turn-end declaration
/// ("WAITING ON the re-review — fix landed …") and then the answer. The wait is one credit, and the
/// status line took it: the answer that followed had no question in it, was filed as narration into
/// a single-slot memo, and the "WAITING ON …" line written a minute later overwrote it there. The
/// owner re-typed their question three times in one morning and was told nothing three times.
///
/// Two things are pinned, and the second needs the first to be exact: the answer is pushed the
/// moment it lands, and what the session filed AFTER it inside the same turn reaches the owner at
/// turn end — as one message, the turn-ended completion.
///
/// THE ANSWER CARRIES NO QUESTION MARK AND NO MARKER, on purpose: under OwnerPush_Policy that leaves
/// exactly one route to it being pushed — the owner waiting. The status lines carry none either, so
/// the only route to the phone for them is the turn-ended completion.
///
/// The session is made MID-TURN with a transcript, as the status line's probe reads it, because the
/// turn-ended announcement is the polled transition out of that state — without it the resolver
/// announces on the first tick, before the mirror has filed anything, which is a race this test
/// would otherwise be measuring instead of the defect.
/// </summary>
public class AStatusLineDoesNotSpendTheOwnersWaitTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445577;
    const long OWNER_USER_ID = 555000112;
    const long TOPIC_ID = 4343;

    const string STATUS_BEFORE_THE_ANSWER = "Task 6 fix round landed: 7af0aafe, 116 runtime tests, review running.";
    const string ANSWER_TEXT = "Agreed, three rules: every futures root carries a default roll rule.";
    const string STATUS_AFTER_THE_ANSWER = "Your 11:01 message is answered above; the re-review is still running.";
    const string NARRATION_AFTER_THE_TURN = "The re-review came back clean and the branch is still green.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public AStatusLineDoesNotSpendTheOwnersWaitTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-status-line-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new FailableTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheAnswerIsPushed_AndWhatFollowedItInTheSameTurn_ReachesThemAtTurnEnd()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();
        Mark_SessionMidTurn(orchId);

        // 1 — the owner asks. The wait is raised when the message lands in the channel.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("how should futures roll by default"));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message delivered"), 40_000),
            $"the owner's message was never delivered, so the wait was never raised.{Environment.NewLine}{_log.Dump()}");

        // 2 — the session's boundary, in the order sessions actually write it. Indices follow the
        // owner's entry, which the bridge numbered [1].
        Append_SupervisorEntry(orchId, 2, "WAITING ON the task 6 re-review - fix landed 7af0aafe", STATUS_BEFORE_THE_ANSWER);
        Append_SupervisorEntry(orchId, 3, "defaults per root, C is the continuous automatically", ANSWER_TEXT);
        Append_SupervisorEntry(orchId, 4, "WAITING ON the task 6 re-review - answered in 3", STATUS_AFTER_THE_ANSWER);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(ANSWER_TEXT), 20_000),
            "THE DEFECT: the status line written just before the answer spent the owner's wait, so the "
            + "answer re-evaluated as narration and never reached the phone."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}"
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.False(
            _telegram.Has_Sent_Containing(STATUS_BEFORE_THE_ANSWER),
            "the status line was pushed as if it were the answer — that is the credit being spent on it");

        Assert.False(
            _telegram.Has_Sent_Containing(STATUS_AFTER_THE_ANSWER),
            "narration after the answer was pushed immediately — the waterfall the push policy exists to stop");

        // 3 — the turn ends. Everything filed after the answer is delivered, once, as the completion.
        //
        // The trailing entry is released by the tailer only after its quiet polls, and the resolver
        // runs BEFORE the poll in a tick — so the session stays mid-turn until the last status line
        // has been tailed, exactly as a real session's transcript keeps it mid-turn for 120 s after
        // its last write. Flipping earlier would measure that race, not the defect.
        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("entry #4 FROM Supervisor"), 20_000),
            $"the last status line was never tailed, so the turn-end digest could not carry it.{Environment.NewLine}{_log.Dump()}");

        Mark_SessionIdle(orchId);

        Assert.True(
            await Run_Until_Async(() => Was_SentAsTurnEndedCompletion(STATUS_AFTER_THE_ANSWER), 30_000),
            "the turn ended and the status line filed after the answer never reached the owner — the "
            + "turn-ended completion did not carry what the session said inside the reply turn."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}"
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // 4 — AND THE WAIT IS SPENT. Narration after the turn is narration again.
        Append_SupervisorEntry(orchId, 5, "progress", NARRATION_AFTER_THE_TURN);

        Assert.False(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(NARRATION_AFTER_THE_TURN), 12_000),
            "the answer was delivered but the owner's wait was never consumed, so ordinary narration "
            + "is still being pushed to their phone");
    }

    bool Was_SentAsTurnEndedCompletion(string fragment)
    {
        foreach (var text in _telegram.Sent_Texts())
        {
            if (text.Contains("turn ended", StringComparison.Ordinal) && text.Contains(fragment, StringComparison.Ordinal))
                return true;
        }

        return false;
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

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>An unseen file is registered at its current end and emits nothing, so the channel is baselined first.</summary>
    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        await Run_For_Async(4_000);

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
        return "{\"ok\":true,\"result\":[{\"update_id\":3001,\"message\":{\"message_id\":97,"
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

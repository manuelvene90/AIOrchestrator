using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A TURN-END COMPLETION THAT CARRIES HELD WORDS RINGS (ruling R8, task-2 fix round 1, <c>phone.push =
/// filtered</c>).
///
/// <para>
/// THE CASE IS THE ORDINARY ONE. The owner asks for a job; the session says "on it" — that spends their
/// credit, and it rings, correctly — and then does the work. Its closing report ("merged, 214 green") is
/// narration by shape, so the filter holds it, and the ONLY way it reaches the owner is the completion
/// at turn end. Sent silent, a finished job reached them as silence: the exact complaint the completion
/// was created for (2026-08-25, *"the terminal completes the operation and stops, and I haven't received
/// anything telling me 'done'"*), and the rule the send site states itself — a completion is SENT, never
/// edited, precisely because an edit raises no notification.
/// </para>
/// <para>
/// THE SOUND RIDES THE HELD WORDS, NOT THE TURN END. What rings is the supervisor's own words reaching
/// the owner for the first time; the app's tick beneath them adds nothing that would ring on its own.
/// The second fact pins the other half as far as this tree lets an engine test see it: with nothing
/// held after the answer, the turn end sends nothing at all, so nothing rings. The one SILENT send left
/// on this path — an unanswered turn's "turn ended" line with no message to edit — needs a busy
/// narration to have been attempted and failed, which first fires after 180 s; it is not reachable here,
/// and its sound is unchanged by this ruling.
/// </para>
/// <para>
/// The fixture writes no <c>preset</c>, so it runs under classic — <c>filtered</c>. The harness is
/// <c>TheTurnEndDigestStartsAtTheOwnersMessageTests</c>' with the sound-recording client.
/// </para>
/// </summary>
public class ACompletionCarryingHeldWordsRingsTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445533;
    const long OWNER_USER_ID = 555000134;
    const long TOPIC_ID = 4949;

    /// <summary>The early reply that spends the credit. Not a question, not a marker — sent only because they wait.</summary>
    const string ON_IT_TEXT = "On it: merging as soon as the suite is green on the merged tree.";

    /// <summary>The closing report, held as narration once the credit is spent.</summary>
    const string CLOSING_REPORT = "Merged to master, 214 tests green on the merged tree, pushed.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly SoundRecordingTelegram_Fake _telegram = new();
    readonly RecordingLog_Fake _log = new();

    public ACompletionCarryingHeldWordsRingsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-completion-rings-tests-{Guid.NewGuid():N}");
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
    public async Task UnderFiltered_ACompletionCarryingAHeldClosingReport_Rings()
    {
        var orchId = await Start_Answered_Async("merge the branch when the suite is green");

        // The work, then its report — held, because the "on it" above already spent the credit.
        Append_SupervisorEntry(orchId, 3, "merged and pushed", CLOSING_REPORT);

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("entry #3 FROM Supervisor"), 20_000),
            $"the closing report was never tailed.{Environment.NewLine}{_log.Dump()}");

        Assert.False(
            _telegram.Has_Sent_Containing(CLOSING_REPORT),
            "the closing report was sent on its own, so it was never held and the completion is not its only route."
            + $"{Environment.NewLine}{_telegram.Dump_Sent()}");

        Mark_SessionIdle(orchId);

        Assert.True(
            await Run_Until_Async(() => Find_Completion_OrNull(CLOSING_REPORT) != null, 30_000),
            "the turn ended and the held closing report never reached the owner."
            + $"{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(TelegramSendSounds.Rings, Find_Completion_OrNull(CLOSING_REPORT)!.Value.Sound);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderFiltered_ATurnThatEndsWithNothingHeld_SendsNothingThatRings()
    {
        var orchId = await Start_Answered_Async("is the rebuild done");

        var ringsBeforeTheTurnEnd = Count_Rings();

        Mark_SessionIdle(orchId);

        // The engine's own account of the branch taken, so the absence below is not a missed tick.
        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("nothing further to say, nothing sent"), 30_000),
            $"the turn end was never announced, so its silence proves nothing.{Environment.NewLine}{_log.Dump()}");

        Assert.False(_telegram.Has_Sent_Containing(TURN_ENDED), _telegram.Dump_Sent());
        Assert.Equal(ringsBeforeTheTurnEnd, Count_Rings());
    }

    /// <summary>
    /// Channel seen, session mid-turn, the owner's message delivered, and the "on it" answer SENT —
    /// which both spends the credit and marks the exchange answered, so the turn end is a completion.
    /// </summary>
    async Task<string> Start_Answered_Async(string ownerText)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

        Mark_SessionMidTurn(session.OrchId);

        _telegram.Queue_Updates(Build_OwnerMessageJson(ownerText));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message delivered"), BridgeTestTiming.Window_ForAggregation(60)),
            $"the owner's message was never delivered, so the credit was never raised.{Environment.NewLine}{_log.Dump()}");

        Append_SupervisorEntry(session.OrchId, 2, "on it", ON_IT_TEXT);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(ON_IT_TEXT), 20_000),
            $"the early reply never reached the phone, so the credit was never spent.{Environment.NewLine}{_log.Dump()}");

        return session.OrchId;
    }

    /// <summary>
    /// The first send carrying <paramref name="heldFragment"/>, provided it is the turn-ended completion.
    /// The held text is asserted never sent on its own first, so its first send IS the completion.
    /// </summary>
    (string Text, TelegramSendSounds Sound)? Find_Completion_OrNull(string heldFragment)
    {
        var sent = _telegram.Find_Sent_Containing(heldFragment);

        return sent != null && sent.Value.Text.Contains(TURN_ENDED, StringComparison.Ordinal) ? sent : null;
    }

    int Count_Rings()
    {
        return _telegram.Count_Sent_WithSound(TelegramSendSounds.Rings);
    }

    const string TURN_ENDED = "turn ended";

    void Mark_SessionMidTurn(string orchId)
    {
        Write_SessionActivity(orchId, DateTime.UtcNow);
    }

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

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(_paths.Get_OwnerChannelFile(orchId), $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":7001,\"message\":{\"message_id\":141,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
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

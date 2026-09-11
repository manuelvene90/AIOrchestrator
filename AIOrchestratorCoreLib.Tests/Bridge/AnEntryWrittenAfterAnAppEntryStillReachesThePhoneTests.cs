using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A SUPERVISOR ENTRY WRITTEN AFTER THE APP WROTE ONE OF ITS OWN STILL REACHES THE PHONE.
///
/// <para>
/// WHERE IT COMES FROM. While building stage 8a a two-question probe could not get its SECOND
/// question onto the fake phone: entry `[9]` sat in owner-channel.md and no `entry #9` line ever
/// appeared in the log. Between the two appends the app had written an entry of its own (the
/// message-contract coaching), so it was parked as a suspected mirror-cursor or self-write-baseline
/// defect — a supervisor entry never mirrored is a LOST entry.
/// </para>
/// <para>
/// IT WAS NEITHER. The cause is the tailer's TRAILING-ENTRY rule: the last entry in a file is held
/// back until the file has stopped growing for <c>TrailingEntryQuietMilliseconds</c>, and that
/// quiet is measured against the INJECTED CLOCK. These probes freeze the clock, so "quiet" never
/// elapses and the trailing entry is held for ever. Entry #3 was released only because #4 and #5
/// arrived behind it and made it no longer trailing. In production the clock moves and the entry
/// flushes; nothing was ever lost.
/// </para>
/// <para>
/// SO THE TEST STAYS, WITH THE CLOCK STEPPED where the file going quiet would do it. What it pins
/// is the claim the parked finding doubted — an entry appended after the app's own entry is
/// mirrored — and, by stepping the clock explicitly, it also documents the harness rule that cost
/// two debugging sessions: a frozen clock stops the LAST entry of a channel from ever arriving.
/// </para>
/// </summary>
public class AnEntryWrittenAfterAnAppEntryStillReachesThePhoneTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 5150;

    // Both are COMPLETE questions: a question is the one shape OwnerPush_Policy always pushes, so
    // the probe is about the mirror and not about the narration filter. The first one deliberately
    // carries prose under its QUESTION: line, which is what makes the app write its coaching entry
    // in between the two — the exact interleaving under test.
    const string FIRST_QUESTION =
        "Two plan levers changed and the build can start.\n"
        + "QUESTION: Start the FIN-D-277a build now?\n"
        + "OPTION: Start it\n"
        + "OPTION: Wait\n"
        + "RECOMMEND: Start it — the matrix values are numbers, not layout.\n"
        + "RISK: low\n"
        + "ROW: FIN-D-277a\n"
        + "One more line of prose, below the question, so the contract has something to say.";

    const string SECOND_QUESTION =
        "The perf branch is green.\n"
        + "QUESTION: Merge wf-perf into master now?\n"
        + "OPTION: Merge it\n"
        + "OPTION: Hold\n"
        + "RECOMMEND: Hold — you asked to read every merge to master first.\n"
        + "RISK: low\n"
        + "ROW: FIN-D-277b";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly CapturingTelegram_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);
    readonly IBridgeEngine _engine;

    public AnEntryWrittenAfterAnAppEntryStillReachesThePhoneTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-entry-after-app-{Guid.NewGuid():N}");
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

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
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
    public async Task AnEntryAppendedAfterTheAppsOwnEntry_IsStillMirrored()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        using var cancellation = new CancellationTokenSource();
        var loop = _engine.Run_Async(cancellation.Token);

        try
        {
            // The tailer baselines a file it has never seen at its CURRENT length, so nothing may be
            // appended until it has made one pass over it — and an owner message arriving is the
            // proof that the pass happened, exactly as the stage-8a harness uses it.
            _telegram.Queue_Updates(Build_OwnerMessageJson("what is happening", updateId: 5001, messageId: 71));

            Assert.True(
                await Wait_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 15_000),
                $"the engine never made its first pass.{Environment.NewLine}{_log.Dump()}");

            Append_Supervisor(session.OrchId, 3, FIRST_QUESTION);

            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
                $"the first question never reached the phone.{Environment.NewLine}{_log.Dump()}");

            // The app's OWN entry, in between: the contract coaching the first question earns for
            // carrying prose under its QUESTION: line.
            Assert.True(
                await Wait_Until_Async(() => File.ReadAllText(channelFile).Contains("FROM app", StringComparison.Ordinal), 20_000),
                $"the app never wrote an entry of its own, so this probe would test nothing.{Environment.NewLine}{File.ReadAllText(channelFile)}");

            Append_Supervisor(session.OrchId, 9, SECOND_QUESTION);

            // THE LAST ENTRY IN A FILE IS HELD UNTIL THE FILE IS QUIET — the tailer's trailing-entry
            // rule, read off the injected clock. A frozen clock means "quiet" never elapses, so the
            // clock is stepped here exactly as the file going quiet would be in production.
            await Task.Delay(300);
            _clock.Advance(TimeSpan.FromSeconds(30));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_ButtonFor("Merge it") != null, 25_000),
                "the SECOND question never reached the phone — an entry written after the app's own "
                + $"entry was lost.{Environment.NewLine}=== CHANNEL ==={Environment.NewLine}{File.ReadAllText(channelFile)}"
                + $"{Environment.NewLine}=== LOG ==={Environment.NewLine}{_log.Dump()}"
                + $"{Environment.NewLine}=== SENT ==={Environment.NewLine}{_telegram.Dump_Sent()}");
        }
        finally
        {
            await cancellation.CancelAsync();

            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // The only way this loop ends.
            }
        }
    }

    static string Build_OwnerMessageJson(string text, long updateId, long messageId)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    void Append_Supervisor(string orchId, int entryNumber, string body)
    {
        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{entryNumber}] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a question\n{body}\n");
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
}

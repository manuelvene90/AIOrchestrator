using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// D8 (coordinator, 2026-09-14): <c>phone.push</c> GOVERNS ORCHESTRATION OWNER CHANNELS ONLY — the
/// general supervisor's channel is exempt, and this is the engine-level pin the answer asked for.
///
/// <para>
/// WHY GENERAL IS EXEMPT. It is an owner channel too, so the push block reaches it; filtering it would
/// stop the concierge's narration reaching the phone, which is most of what General is for — "work on
/// skeleton client" typed from the phone is answered in narration, not in a question.
/// </para>
/// <para>
/// BOTH HALVES IN ONE RUN, on the same engine and the same fixture, because either half alone has a
/// second route to green (decision 20): General narration arriving proves nothing if the fixture was
/// never under <c>filtered</c> at all, and orchestration narration being held proves nothing about
/// General. The fixture writes no <c>preset</c>, so it runs under classic — <c>filtered</c>.
/// </para>
/// </summary>
public class TheGeneralChannelIsExemptFromPhonePushTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445599;
    const long OWNER_USER_ID = 555000131;
    const long TOPIC_ID = 4545;

    /// <summary>Narration by every rule the filter has: no question mark, no marker, no BLOCKED, no file.</summary>
    const string GENERAL_NARRATION = "Started strategy-lab-7 on the Suite repo; its supervisor is booting.";

    const string ORCHESTRATION_NARRATION = "imp-1 is pricing the matrix; rev-1 has the diff.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public TheGeneralChannelIsExemptFromPhonePushTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-general-push-tests-{Guid.NewGuid():N}");
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
    public async Task UnderFiltered_GeneralNarration_ReachesThePhone_WhileTheSameKindOfWordsInAnOrchestrationAreHeld()
    {
        var orchId = await Start_WithBothChannelsAlreadySeen_Async();

        // Distinct indices, so the two "was it tailed" log lines cannot answer for each other.
        Append_SupervisorEntry(_paths.GeneralChannelFile, 7, "progress", GENERAL_NARRATION);
        Append_SupervisorEntry(_paths.Get_OwnerChannelFile(orchId), 9, "progress", ORCHESTRATION_NARRATION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(GENERAL_NARRATION) && _log.Has_Info_Containing("entry #9 FROM Supervisor"), 20_000),
            "D8: the general supervisor's narration never reached the phone under classic — General was "
            + "filtered like an orchestration, so the concierge went quiet."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}"
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // A few more ticks, so a send of the orchestration entry that was merely still in flight when
        // the condition above held has had every chance to land before its absence is believed.
        await Run_For_Async(BridgeTestTiming.Window_ForTicks(5));

        Assert.False(
            _telegram.Has_Sent_Containing(ORCHESTRATION_NARRATION),
            "the orchestration's narration was pushed too, so this fixture was never under phone.push = "
            + "filtered and General arriving above proves nothing about an exemption."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}");
    }

    /// <summary>
    /// Both channels exist and have been polled once before anything is appended: the tailer registers a
    /// file it has never seen at its current end and emits nothing for it.
    /// </summary>
    async Task<string> Start_WithBothChannelsAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        Seed_Channel(_paths.Get_OwnerChannelFile(session.OrchId));
        Seed_Channel(_paths.GeneralChannelFile);

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    static void Seed_Channel(string channelFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(channelFile) ?? throw new Exception($"channel file '{channelFile}' has no folder"));

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# CHANNEL\n\n---\n");
    }

    static void Append_SupervisorEntry(string channelFile, int index, string subject, string body)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
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

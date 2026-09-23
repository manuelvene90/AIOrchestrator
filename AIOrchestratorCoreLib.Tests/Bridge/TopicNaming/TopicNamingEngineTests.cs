using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.TopicNaming;

/// <summary>
/// A TOPIC IS BORN WITH ITS NAME, AND EVERY RENAME SAYS WHAT TELEGRAM ANSWERED.
///
/// <para>
/// Observed 2026-09-23 on `da-vinci-fintech-suite-33`: the solo filed `set-orchestration-name`
/// ("TKT · ticket view sync") at 15:07:39 UTC, four seconds BEFORE its topic existed. The topic was
/// created with the bare orch id and relied entirely on the later rename sync — and the owner saw the
/// bare id while the session and the terminal said the name had been set. Nothing in the logs could
/// say when (or whether) the name landed, because a successful `editForumTopic` was never logged and
/// a 200 was not told apart from 400 TOPIC_NOT_MODIFIED.
/// </para>
/// </summary>
public class TopicNamingEngineTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long EXISTING_TOPIC_ID = 4343;

    const string DISPLAY_NAME = "TKT · ticket view sync";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public TopicNamingEngineTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-topic-naming-{Guid.NewGuid():N}");
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
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    /// <summary>
    /// THE dvfs-33 SHAPE: named first, topic second. The topic is created WITH the name, and because
    /// creation is itself Telegram confirming the name, no rename follows it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASessionNamedBeforeItsTopicExists_GetsItsTopicCreatedWithTheName_AndNoRenameFollows()
    {
        var orchId = await Start_Untopiced_WithChannelAlreadySeen_Async(DISPLAY_NAME);

        Append_SupervisorEntry(orchId, 1, "progress", "The build is green.");

        Assert.True(
            await Run_Until_Async(() => _telegram.Created_TopicNames().Count > 0, 20_000),
            $"no topic was ever created, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        // Room for the follow-up sync that used to rename the bare id into the name.
        await Run_For_Async(BridgeTestTiming.Window_ForTicks(6));

        Assert.Equal([DISPLAY_NAME], _telegram.Created_TopicNames());

        Assert.True(
            _telegram.Renamed_TopicNames().Count == 0,
            $"a topic born with its name was renamed anyway: {string.Join(" | ", _telegram.Renamed_TopicNames())}");
    }

    /// <summary>
    /// TODAY'S FALLBACK KEPT: a session that has no name yet gets its orch id, exactly as before —
    /// and still no redundant rename to the same thing.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASessionWithNoNameYet_StillGetsItsOrchId()
    {
        var orchId = await Start_Untopiced_WithChannelAlreadySeen_Async(displayName: null);

        Append_SupervisorEntry(orchId, 1, "progress", "The build is green.");

        Assert.True(
            await Run_Until_Async(() => _telegram.Created_TopicNames().Count > 0, 20_000),
            $"no topic was ever created, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(6));

        Assert.Equal([orchId], _telegram.Created_TopicNames());
        Assert.Empty(_telegram.Renamed_TopicNames());
    }

    /// <summary>
    /// TELEGRAM REFUSES THE NAME, SO THE TOPIC IS STILL BORN — as the orch id, in the same call. A
    /// refused creation would otherwise mirror to General on every tick. The refusal is logged, and the
    /// name sync still pushes the real name afterwards: the fallback is a birth name, not a verdict.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ARefusedNamedCreate_FallsBackToTheOrchId_AndTheSyncStillTriesTheName()
    {
        var orchId = await Start_Untopiced_WithChannelAlreadySeen_Async(DISPLAY_NAME);

        _telegram.Fail_NextCreate_With(new TelegramApiException(
            400, "Telegram 'createForumTopic' failed with HTTP 400: {\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: TOPIC_NAME_INVALID\"}", null));

        Append_SupervisorEntry(orchId, 1, "progress", "The build is green.");

        Assert.True(
            await Run_Until_Async(() => _telegram.Renamed_TopicNames().Contains(DISPLAY_NAME), 20_000),
            $"the sync never tried the real name after the fallback.{Environment.NewLine}"
            + $"creates: {string.Join(" | ", _telegram.Created_TopicNames())}{Environment.NewLine}{_log.Dump()}");

        Assert.Equal([DISPLAY_NAME, orchId], _telegram.Created_TopicNames());
        Assert.True(
            _log.Has_Line_Containing($"WARN  [{orchId}] Telegram refused to create the topic as '{DISPLAY_NAME}'"),
            $"the refusal was not logged as a warning.{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// A FAILURE THAT IS NOT TELEGRAM'S REFUSAL NEVER BUYS A SECOND CREATE. The client throws a plain
    /// Exception after an HTTP 200 whose body it cannot read — the topic may already exist, so creating
    /// again in the same call would make two. Today's behaviour stands: no orch-id retry.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ANonTelegramFailureAfterACreate_DoesNotTriggerASecondCreate()
    {
        var orchId = await Start_Untopiced_WithChannelAlreadySeen_Async(DISPLAY_NAME);

        _telegram.Fail_NextCreate_With(new Exception("createForumTopic response has no result.message_thread_id: {\"ok\":true}"));

        Append_SupervisorEntry(orchId, 1, "progress", "The build is green.");

        Assert.True(
            await Run_Until_Async(() => _telegram.Created_TopicNames().Count > 0, 20_000),
            $"no create was ever attempted, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(6));

        Assert.DoesNotContain(orchId, _telegram.Created_TopicNames());
    }

    /// <summary>
    /// ONE LINE PER DISTINCT ANSWER, never one per tick. A rename that lands says so once; the same
    /// name re-sent and answered the same way says nothing; TOPIC_NOT_MODIFIED — which the gate counts
    /// as applied — gets its OWN line, once, so a reader can tell "Telegram took the edit" from
    /// "Telegram already had it".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task EveryRenameOutcome_IsLoggedOnce_AndAnUnchangedResendIsSilent()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, EXISTING_TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, DISPLAY_NAME);

        var renamedLine = $"renamed topic {EXISTING_TOPIC_ID} to '{DISPLAY_NAME}'";
        var alreadyNamedLine = $"topic {EXISTING_TOPIC_ID} already named '{DISPLAY_NAME}'";

        // 1 — the first sync applies the name: one "renamed" line, and it stays one across ticks.
        Assert.True(
            await Run_Until_Async(() => _telegram.Renamed_TopicNames().Count > 0, 20_000),
            $"the name was never pushed, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(6));

        Assert.True(
            _log.Count_Infos_Containing(renamedLine) == 1,
            $"expected exactly one applied-rename line.{Environment.NewLine}{_log.Dump()}");

        // 2 — /refresh re-sends the SAME name and Telegram answers 200 again: nothing new to say.
        await Refresh_AndWait_Async(3001, expectedRenames: 2);

        Assert.Equal(1, _log.Count_Infos_Containing(renamedLine));
        Assert.Equal(0, _log.Count_Infos_Containing(alreadyNamedLine));

        // 3 — Telegram now answers TOPIC_NOT_MODIFIED: a different answer, so its own line, once…
        _telegram.Answer_Renames_AsNotModified();
        await Refresh_AndWait_Async(3002, expectedRenames: 3);

        Assert.True(
            _log.Count_Infos_Containing(alreadyNamedLine) == 1,
            $"TOPIC_NOT_MODIFIED was not logged as its own line exactly once.{Environment.NewLine}{_log.Dump()}");

        // …and the same answer again says nothing.
        await Refresh_AndWait_Async(3003, expectedRenames: 4);

        Assert.Equal(1, _log.Count_Infos_Containing(alreadyNamedLine));
        Assert.Equal(1, _log.Count_Infos_Containing(renamedLine));
    }

    async Task Refresh_AndWait_Async(long updateId, int expectedRenames)
    {
        var refreshLinesBefore = _log.Count_Infos_Containing("/refresh —");

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(updateId, "/refresh"));

        Assert.True(
            await Run_Until_Async(
                () => _telegram.Renamed_TopicNames().Count >= expectedRenames
                    && _log.Count_Infos_Containing("/refresh —") > refreshLinesBefore,
                20_000),
            $"/refresh (update {updateId}) never re-sent the name.{Environment.NewLine}{_log.Dump()}");
    }

    async Task<string> Start_Untopiced_WithChannelAlreadySeen_Async(string? displayName)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        if (displayName != null)
            _store.Set_DisplayName(session.OrchId, displayName);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        // The tailer registers an unseen file at its current end; one short run baselines it so the
        // entry appended next is new bytes and is mirrored — which is what creates the topic.
        await Run_For_Async(BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static string Build_OwnerMessageJson(long updateId, string text)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{updateId},"
            + $"\"message_thread_id\":{EXISTING_TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
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

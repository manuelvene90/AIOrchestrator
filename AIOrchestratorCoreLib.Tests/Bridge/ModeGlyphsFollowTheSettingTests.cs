using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE ENGINE OBEYS <c>topic.modeGlyphs</c> ON THE TOPIC NAME — plan 03 Task 7, through the front door.
///
/// <para>
/// The pure half (which glyph goes where, the precedence, never both) is TelegramDeliveryModeGlyphsTests'.
/// This file proves the two things only the engine can get wrong: that <c>Build_WantedTopicName</c>
/// actually HANDS the mode inputs in (a composer that can draw 🔕 is worth nothing if the caller always
/// passes Normal), and that the rename path does not now fire on every tick. The mode inputs are
/// reconciled every tick, so a name composed from them must be compared against the name last applied
/// before any <c>editForumTopic</c> — each rename writes a service message into the owner's thread.
/// That comparison (<c>_appliedTopicNames</c>) predates this task; the probe below is what keeps it.
/// </para>
/// </summary>
public class ModeGlyphsFollowTheSettingTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 6161;
    const string DISPLAY_NAME = "crm bug";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingTelegram_Fake _telegram = new();

    public ModeGlyphsFollowTheSettingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-modeglyphs-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    /// <summary>
    /// CLASSIC — a config that names no preset — STATES <c>name</c>, master's topic list: a silenced
    /// topic's NAME carries 🔕. And it is renamed ONCE: the count after twelve more ticks is the count
    /// after the first six, because nothing the name should say has changed in between.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_ASilencedTopicsNameCarriesTheBell_AndIsNotRenamedAgainOnLaterTicks()
    {
        var engine = Build_Engine(extraConfig: "");
        var orchId = Start_SilencedOrchestration();

        var renamed = await Run_Until_Async(engine, () => _telegram.Last_TopicName_OrNull() != null, 20_000);

        Assert.True(renamed, $"the topic was never renamed at all.{Environment.NewLine}{_telegram.Dump_TopicNames()}");
        Assert.Equal($"{TelegramDeliveryMode_Glyphs.SILENCED} {DISPLAY_NAME}", _telegram.Last_TopicName_OrNull());

        var afterFirstRun = Count_Renames();

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(12));

        Assert.True(
            Count_Renames() == afterFirstRun,
            $"the name was re-sent while nothing it says changed — each rename is a service message in the owner's thread. "
            + $"Renames: {_telegram.Dump_TopicNames()}");

        Assert.NotNull(_store.Get_Session_OrNull(orchId));
    }

    /// <summary>
    /// THE SHIPPED <c>pulseHeader</c>: the same silenced topic, and its name carries NO bell — the bell is
    /// on PULSE's header instead. Stated in config.json, the third rung, so the probe proves the engine
    /// reads the key rather than a preset's opinion of it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderPulseHeader_ASilencedTopicsNameCarriesNoModeGlyph()
    {
        var engine = Build_Engine(extraConfig: ",\"topic\":{\"modeGlyphs\":\"pulseHeader\"}");
        Start_SilencedOrchestration();

        var renamed = await Run_Until_Async(engine, () => _telegram.Last_TopicName_OrNull() != null, 20_000);

        Assert.True(renamed, $"the topic was never renamed at all, so its absence of a bell proves nothing.{Environment.NewLine}{_telegram.Dump_TopicNames()}");
        Assert.Equal(DISPLAY_NAME, _telegram.Last_TopicName_OrNull());
    }

    /// <summary>
    /// AN APP-WIDE CHANGE MID-RUN RENAMES EACH OPEN TOPIC EXACTLY ONCE under <c>name</c> (fix round 1,
    /// finding 4) — the cost the catalogue's description now states. /dnd_all is typed in General, as
    /// the owner would; it drops every applied-name memo on purpose, so this is also the proof that the
    /// re-push after the drop lands once per topic and then stops.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_DndAll_RenamesEachOpenTopicExactlyOnce()
    {
        var engine = Build_Engine(extraConfig: "");
        Start_Orchestration(ALPHA_TOPIC_ID, "alpha");
        Start_Orchestration(BETA_TOPIC_ID, "beta");

        var named = await Run_Until_Async(engine, () => Count_Named("alpha") == 1 && Count_Named("beta") == 1, 20_000);
        Assert.True(named, $"the two topics were never named.{Environment.NewLine}{_telegram.Dump_TopicNames()}");

        _telegram.Queue_OwnerMessage(Build_GeneralMessageJson("/dnd_all"));

        var deferred = await Run_Until_Async(engine, () => Count_Named("🌙 alpha") >= 1 && Count_Named("🌙 beta") >= 1, 20_000);
        Assert.True(deferred, $"/dnd_all did not reach the names.{Environment.NewLine}{_telegram.Dump_TopicNames()}");

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(12));

        Assert.Equal(1, Count_Named("🌙 alpha"));
        Assert.Equal(1, Count_Named("🌙 beta"));
        Assert.True(Count_Renames() == 4, $"expected exactly two names per topic, one before and one after /dnd_all: {_telegram.Dump_TopicNames()}");
    }

    /// <summary>
    /// A CLOSED TOPIC THAT IS KEPT (<c>topic.onClose = close</c>) IS RENAMED TO ITS 🏁 NAME ONCE AND THEN
    /// NEVER AGAIN (fix round 1, finding 2) — not by /dnd_all, which drops every applied-name memo, and
    /// not by the next start, which begins with none. Before the fix every kept topic stayed in the name
    /// sync for good: re-sent at each revalidation, and under <c>name</c> renamed by every app-wide
    /// toggle with a service message into a finished thread. The closed name carries no mode glyph, and
    /// a persisted marker (<c>TelegramTopicFinalNameUtc</c>) ends the sync.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClose_AKeptTopicIsRenamedToItsFlagOnce_AndNeverAgain_EvenAcrossDndAllAndARestart()
    {
        const string KEPT_CONFIG = ",\"topic\":{\"onClose\":\"close\"}";

        var engine = Build_Engine(KEPT_CONFIG);
        Start_Orchestration(ALPHA_TOPIC_ID, "alpha");
        var betaId = Start_Orchestration(BETA_TOPIC_ID, "beta");

        Assert.True(await Run_Until_Async(engine, () => Count_Named("alpha") == 1 && Count_Named("beta") == 1, 20_000),
            $"the two topics were never named.{Environment.NewLine}{_telegram.Dump_TopicNames()}");

        var closed = await Run_Until_Async(
            engine,
            () => Count_Named($"{TelegramDeliveryMode_Glyphs.CLOSED} beta") == 1,
            20_000,
            beforeWait: running => running.Close_Orchestration_ByOwner(betaId, "the owner closed it from the app"));

        Assert.True(closed, $"the kept topic was never renamed to its 🏁 name.{Environment.NewLine}{_telegram.Dump_TopicNames()}");
        Assert.NotNull(_store.Get_Session(betaId).TelegramTopicFinalNameUtc);

        _telegram.Queue_OwnerMessage(Build_GeneralMessageJson("/dnd_all"));

        Assert.True(await Run_Until_Async(engine, () => Count_Named("🌙 alpha") == 1, 20_000),
            $"/dnd_all did not reach the open topic, so its absence on the closed one proves nothing.{Environment.NewLine}{_telegram.Dump_TopicNames()}");

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(12));

        // THE RESTART: a new engine starts with no applied-name memo at all.
        await Run_Until_Async(Build_Engine(KEPT_CONFIG), () => false, BridgeTestTiming.Window_ForTicks(12));

        Assert.True(
            Count_Renames_Of("beta") == 2,
            $"the kept topic was renamed after its 🏁 name was applied: {_telegram.Dump_TopicNames()}");
    }

    const long ALPHA_TOPIC_ID = 6171;
    const long BETA_TOPIC_ID = 6172;

    string Start_Orchestration(long topicId, string displayName)
    {
        var launcher = OrchestrationLauncher_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, new RecordingSpawner_Fake(), OrchestrationLog_Factory.Create(_paths));

        var session = launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, topicId);
        _store.Set_DisplayName(session.OrchId, displayName);

        return session.OrchId;
    }

    /// <summary>How many times exactly this name was sent.</summary>
    int Count_Named(string name)
    {
        return All_Renames().Count(sent => sent == name);
    }

    /// <summary>Every rename of the topic whose base name is this one, whatever glyphs it carried.</summary>
    int Count_Renames_Of(string baseName)
    {
        return All_Renames().Count(sent => TelegramDeliveryMode_Glyphs.Strip_Glyph(sent) == baseName);
    }

    string[] All_Renames()
    {
        var dump = _telegram.Dump_TopicNames();

        return dump.Length == 0 ? [] : dump.Split(" | ");
    }

    /// <summary>An owner message in GENERAL — no message_thread_id — which is where an app-wide command is typed.</summary>
    static string Build_GeneralMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":2001,\"message\":{\"message_id\":88,"
            + $"\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    string Start_SilencedOrchestration()
    {
        var launcher = OrchestrationLauncher_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, new RecordingSpawner_Fake(), OrchestrationLog_Factory.Create(_paths));

        var session = launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, DISPLAY_NAME);
        _store.Set_TelegramMode(session.OrchId, TelegramDeliveryModes.Silenced);

        return session.OrchId;
    }

    IBridgeEngine Build_Engine(string extraConfig)
    {
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID}{extraConfig}}}");

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);

        return BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, launcher, log, _telegram, BridgeTestTiming.Fast());
    }

    int Count_Renames()
    {
        var dump = _telegram.Dump_TopicNames();

        return dump.Length == 0 ? 0 : dump.Split(" | ").Length;
    }

    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds, Action<IBridgeEngine>? beforeWait = null)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
        beforeWait?.Invoke(engine);

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

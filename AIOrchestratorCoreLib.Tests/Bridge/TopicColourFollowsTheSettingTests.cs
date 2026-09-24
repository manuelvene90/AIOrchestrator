using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE REAL ENGINE OBEYS <c>topic.repoColours</c> WHEN IT CREATES A TOPIC (plan 03 task 14). Owner,
/// 2026-09-23 08:10: <i>"since we merged his forks the topic icon gets colored without any context of
/// why, red, blue, green, seemingly random."</i> The colour is the fork's brief F1 — each REPOSITORY
/// takes the next colour of <see cref="TopicColor_Rotation.PALETTE"/> and keeps it in config.json —
/// deterministic, but nothing tells the owner which repo is which colour. Classic turns it off.
///
/// <para>
/// TWO FACTS PER TOPIC, because "no colour" has two routes and only the pair pins it (decision 20): the
/// colour the engine hands <c>createForumTopic</c> (null is Telegram's default; that a null puts no
/// <c>icon_color</c> on the wire is <c>TelegramApiClientWireTests</c>' to pin), AND config.json — an
/// assignment that was persisted but not passed would colour the NEXT topic after a setting flip without
/// the owner having asked for anything.
/// </para>
/// <para>
/// QUIET'S VALUE IS STATED, NOT QUIET ITSELF: quiet also names print runners, and an engine test that
/// registers a print session can spawn the real <c>claude</c> (global constraints). <c>true</c> is the
/// shipped default and what quiet resolves to — <c>PresetProbeTests</c> pins both presets' answers.
/// </para>
/// </summary>
public class TopicColourFollowsTheSettingTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const string REPO_NAME = "alpha";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram = new();
    readonly RecordingLog_Fake _log = new();
    int _configWrites;

    public TopicColourFollowsTheSettingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-topic-colour-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // NO PRESET, which is classic — the owner's way, colours off.
        Write_Config(repoColoursOrNull: null);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
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

    /// <summary>
    /// UNDER CLASSIC A NEW TOPIC IS CREATED IN TELEGRAM'S DEFAULT, AND NOTHING IS ASSIGNED. No colour on
    /// the call, and no <c>topicColor</c> written onto the repository — a colour picked "for later" would
    /// be a decision the owner turned off, taken anyway.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_ANewTopic_IsCreatedWithNoColour_AndNoColourIsWrittenToConfigJson()
    {
        var created = await Create_TopicFor_NewOrchestration_Async(expectedTopics: 1);

        Assert.Null(created.IconColor);
        Assert.DoesNotContain("topicColor", File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>
    /// WITH THE SETTING ON (the shipped default, quiet's value) TODAY'S BEHAVIOUR HOLDS UNCHANGED: the
    /// first repository takes the rotation's first colour, and it is written down on the repo entry.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WithRepoColoursOn_ANewTopic_TakesTheRotationsColour_AndItIsPersistedOnTheRepo()
    {
        Write_Config(repoColoursOrNull: true);

        var created = await Create_TopicFor_NewOrchestration_Async(expectedTopics: 1);

        Assert.Equal(TopicColor_Rotation.PALETTE[0], created.IconColor);
        Assert.Equal(TopicColor_Rotation.PALETTE[0], Persisted_Colour_OrNull());
    }

    /// <summary>
    /// A CONFIG.JSON EDIT IS OBEYED AT THE NEXT CREATION, with the same engine: read at the point of
    /// effect, never cached. Off → the first topic is default; on → the second takes a colour.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FlippingTheSettingInConfigJson_IsObeyedAtTheNextTopicCreation_WithoutARestart()
    {
        var first = await Create_TopicFor_NewOrchestration_Async(expectedTopics: 1);

        Assert.Null(first.IconColor);

        Write_Config(repoColoursOrNull: true);

        var second = await Create_TopicFor_NewOrchestration_Async(expectedTopics: 2);

        Assert.Equal(TopicColor_Rotation.PALETTE[0], second.IconColor);
    }

    /// <summary>
    /// TURNING IT OFF DOES NOT UN-ASSIGN, AND DOES NOT PAINT EITHER. A repository that already has a
    /// colour in config.json (it was on once) gets a DEFAULT topic while the setting is off, and the
    /// persisted colour stays — so turning it back on restores the same colour, not the next in line.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_ARepoThatAlreadyHasAColour_GetsADefaultTopic_AndKeepsItsColourOnFile()
    {
        Write_Config(repoColoursOrNull: null, persistedColourOrNull: TopicColor_Rotation.PALETTE[3]);

        var created = await Create_TopicFor_NewOrchestration_Async(expectedTopics: 1);

        Assert.Null(created.IconColor);
        Assert.Equal(TopicColor_Rotation.PALETTE[3], Persisted_Colour_OrNull());
    }

    /// <summary>
    /// Starts an orchestration on <see cref="REPO_NAME"/> with NO topic id, lets the tailer baseline its
    /// channel, then appends an entry — the mirror resolves the thread id, which is where the topic is
    /// created. Returns the newest recorded creation.
    /// </summary>
    async Task<(string Name, int? IconColor)> Create_TopicFor_NewOrchestration_Async(int expectedTopics)
    {
        var session = _launcher.Start_Orchestration(REPO_NAME, _tempRepo);
        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

        File.AppendAllText(
            channelFile,
            $"\n## [1] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a question\nShall I carry on with the next step?\n");

        Assert.True(
            await Run_Until_Async(() => _telegram.Created_Topics().Count >= expectedTopics, 20_000),
            $"the engine never created the orchestration's topic, so nothing below measures the colour.{Environment.NewLine}{_log.Dump()}");

        return _telegram.Created_Topics()[expectedTopics - 1];
    }

    int? Persisted_Colour_OrNull()
    {
        return OrchestratorConfig_Loader.Load_OrEmpty(_paths).Repos.Single(repo => repo.Name == REPO_NAME).TopicColor;
    }

    /// <summary>
    /// The fixture's config: one repository, an optional <c>topic.repoColours</c> and an optional colour
    /// already on file. The stamp is pushed forward explicitly because the provider reloads on the file's
    /// write stamp, and two writes inside one tick would read as no change at all.
    /// </summary>
    void Write_Config(bool? repoColoursOrNull, int? persistedColourOrNull = null)
    {
        var colour = persistedColourOrNull == null ? "" : $",\"topicColor\":{persistedColourOrNull.Value}";
        var topic = repoColoursOrNull == null ? "" : $",\"topic\":{{\"repoColours\":{(repoColoursOrNull.Value ? "true" : "false")}}}";
        var repoPath = _tempRepo.Replace("\\", "\\\\");

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[{{\"name\":\"{REPO_NAME}\",\"path\":\"{repoPath}\"{colour}}}],"
            + $"\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID}{topic}}}");

        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref _configWrites)));
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

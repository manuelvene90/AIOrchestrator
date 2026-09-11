using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Time.Clock;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE TEST A3.2 COULD NOT BE, and the reason it could not: <see cref="DecisionStateSurvivesARestartTests"/>
/// drives two engines over ONE IN-MEMORY store, deliberately — the property it pins is "the second
/// engine reads what the first wrote", and a real file would add a second thing that can fail without
/// adding anything to that assertion. It is a good decision and it is still a good decision. But it
/// means the suite's one restart acceptance test never touched <see cref="FileEngineStateStoreModel"/>,
/// never wrote a byte, and could be green on a build where nothing ever reached the disk at all.
///
/// <para>
/// WHAT MADE THAT GAP WORTH CLOSING. On the VPS on 2026-09-06 the daemon was killed with a decision
/// pending and <c>.engine-state.json</c> was found holding <c>"pendingButtons": []</c>, its mtime nine
/// minutes older than the buttons on the owner's phone. That decision turns out to have been a CLOSE
/// CONFIRMATION, which is a separate registry that is deliberately not persisted at all (its durable
/// record is the parked request file) — so the file was right and the alarm was about the wrong
/// family. But nothing in the suite could have told anyone that, because no test had ever asserted
/// that a question with options reaches the disk under the store the daemon actually runs.
/// </para>
/// <para>
/// So this is the same journey as A3.2 — question, buttons, kill, restart, tap — with two changes that
/// are the whole point: the FILE store, and a SECOND store instance built fresh over the same path, so
/// the second engine can only know what is genuinely on disk.
/// </para>
/// </summary>
public class DecisionStateReachesTheDiskTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    const string QUESTION_TEXT = "Which way do you want the cache invalidated?";
    const string FIRST_OPTION = "Invalidate on write";
    const string SECOND_OPTION = "Invalidate on a timer";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly RecordingLog_Fake _log = new();

    public DecisionStateReachesTheDiskTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-engine-state-disk-{Guid.NewGuid():N}");
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
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AQuestionWithOptions_IsONTHEDISK_AndASecondProcessReadingOnlyThatFileHonoursTheTap()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        // ── The first process, on a FILE store ───────────────────────────────────────────────
        var firstTelegram = new CapturingTelegram_Fake();
        var firstEngine = Build_Engine(firstTelegram, EngineStateStore_Factory.Create_File(_paths, _log));

        // The tailer baselines a channel it has never seen at its current length, so the question is
        // appended only after the engine is running — anything written before its first pass is
        // absorbed as history and never mirrored at all.
        firstTelegram.Queue_Updates(Build_OwnerMessageJson("what is the state of the cache work"));

        Assert.True(
            await Run_Until_Async(firstEngine, () => _log.Has_Info_Containing("Owner message buffered"), 15_000),
            $"the owner's message never reached the router.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Append_SupervisorQuestion(session.OrchId, 3, QUESTION_TEXT, FIRST_OPTION, SECOND_OPTION);

        Assert.True(
            await Run_Until_Async(firstEngine, () => firstTelegram.Find_ButtonFor(FIRST_OPTION) != null, 25_000),
            $"the question never reached the phone.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var capturedButton = firstTelegram.Find_ButtonFor(FIRST_OPTION)!;
        var questionMessageId = firstTelegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id, so no tap could ever refer to it");

        // ── THE FILE ITSELF ─────────────────────────────────────────────────────────────────
        // Read as JSON rather than through the store, because the store is one of the things under
        // test: asking it what it saved would answer with whatever it holds in memory if it saved
        // nothing at all. This is the assertion the VPS could not make, and it is why the file was
        // read there by hand.
        Assert.True(File.Exists(_paths.EngineStateFile), $"the daemon's own state file was never created at '{_paths.EngineStateFile}'");

        var pendingButtons = (JsonNode.Parse(File.ReadAllText(_paths.EngineStateFile)) as JsonObject)?["pendingButtons"]?.AsArray()
            ?? throw new Exception($"'{_paths.EngineStateFile}' holds no pendingButtons array:{Environment.NewLine}{File.ReadAllText(_paths.EngineStateFile)}");

        var onDisk = Assert.Single(pendingButtons.Where(button => button!["data"]?.GetValue<string>() == capturedButton))!;

        // A NONCE AND A DEADLINE, both, in the file. Either alone is a button that is unsafe in a
        // different way: without the nonce a payload minted by a later process answers an earlier
        // question, and without the expiry a keyboard on a scrolled-past phone answers for ever.
        var parsed = CallbackToken.Parse_OrNull(capturedButton);
        Assert.NotNull(parsed);
        Assert.Equal(CallbackToken.NONCE_HEX_LENGTH, parsed.Value.Nonce.Length);
        Assert.True(parsed.Value.Nonce.All(Uri.IsHexDigit), $"the payload's name is not a nonce: {capturedButton}");
        // Unix seconds on the wire, the units the serializer writes — read as the file holds them
        // rather than through the reader that produced them, or a wrong unit on both sides agrees.
        var expiresUtc = DateTimeOffset.FromUnixTimeSeconds(onDisk["expiresUtc"]!.GetValue<long>()).UtcDateTime;
        Assert.True(expiresUtc > DateTime.UtcNow, $"the button was persisted already expired (expiresUtc {expiresUtc:O})");

        // ── kill -9 ─────────────────────────────────────────────────────────────────────────
        // Nothing is disposed and nothing is flushed. A save that only happens on a clean shutdown is
        // exactly the save that is not there when it is needed.

        // ── The second process, over the same PATH and a store it builds itself ─────────────
        var secondTelegram = new CapturingTelegram_Fake();
        var secondEngine = Build_Engine(secondTelegram, EngineStateStore_Factory.Create_File(_paths, _log));

        secondTelegram.Queue_Updates(Build_CallbackTapJson(capturedButton, questionMessageId));

        Assert.True(
            await Run_Until_Async(secondEngine, () => Read_OwnerChannel(session.OrchId).Contains(FIRST_OPTION, StringComparison.Ordinal), 20_000),
            "THE DEFECT THIS EXISTS FOR: a button minted before the restart answered nothing — the "
            + "owner's choice was refused as expired, and the only thing that had ever proved otherwise "
            + "was a test sharing one in-memory store between the two processes."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // AND THE TAP CONSUMED IT, on disk. Otherwise the restored keyboard answers twice.
        var afterTheTap = (JsonNode.Parse(File.ReadAllText(_paths.EngineStateFile)) as JsonObject)!["pendingButtons"]!.AsArray();
        Assert.DoesNotContain(afterTheTap, button => button!["data"]?.GetValue<string>() == capturedButton);
    }

    IBridgeEngine Build_Engine(ITelegramApiClient telegram, IEngineStateStore engineState)
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, telegram,
            engineState, Clock_Factory.Create_System(),
            BridgeTestTiming.Fast());
    }

    string Read_OwnerChannel(string orchId) => File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    void Append_SupervisorQuestion(string orchId, int index, string question, string firstOption, string secondOption)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{index}] FROM supervisor — {stamp} — a question\n"
            // RECOMMEND/RISK/ROW are what OwnerQuestion_Contract requires of every question the app
            // will forward; `RISK: low` keeps the high-risk classification coming from the patterns,
            // which is what these tests are about.
            + $"QUESTION: {question}\nOPTION: {firstOption}\nOPTION: {secondOption}\n"
            + "RECOMMEND: whichever you prefer — this fixture takes no view.\nRISK: low\nROW: none\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":3001,\"message\":{\"message_id\":77,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    static string Build_CallbackTapJson(string callbackData, long questionMessageId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":3002,\"callback_query\":{\"id\":\"cbq-1\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{questionMessageId},\"message_thread_id\":{TOPIC_ID},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}";
    }

    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
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

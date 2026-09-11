using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Time.Clock;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE SNAPSHOT STOPS LYING BY OMISSION, AND THE TAP KEEPS BEHAVING EXACTLY AS IT DID.
///
/// <para>
/// On the VPS on 2026-09-06 the daemon was killed with a decision on the owner's phone and
/// <c>.engine-state.json</c> read <c>"pendingButtons": []</c>, its mtime nine minutes stale. Both were
/// correct: the decision was a CLOSE CONFIRMATION, whose durable record is the parked request file,
/// and whose live prompts were a sixth dictionary that <c>Persist_EngineState</c> did not know about.
/// The file was right and the alarm was about the wrong family — and an evening went into finding
/// that out, because nothing on disk said the family existed.
/// </para>
/// <para>
/// So the record is written now: readable, per parked request, carrying what it is about and when it
/// expires. WHAT IT DELIBERATELY DOES NOT CARRY IS THE CALLBACK PAYLOAD, and that omission is the
/// design. A payload in the file is a keyboard somebody eventually restores, and restoring it changes
/// which of two mechanisms is the truth after a restart. Today a restart RE-ASKS, on purpose: a prompt
/// nobody can see any more is indistinguishable from an owner who has not answered. This test pins
/// both halves at once — the file now says what is pending, and the pre-restart keyboard still does
/// nothing.
/// </para>
/// </summary>
public class CloseConfirmationsAreInTheSnapshotTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>The label the orchestration-close prompt puts on its confirming button.</summary>
    const string CONFIRM_LABEL = "Close it";

    const string CLOSE_REASON = "the work is done and the branch is merged";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly RecordingLog_Fake _log = new();

    public CloseConfirmationsAreInTheSnapshotTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-close-snapshot-{Guid.NewGuid():N}");
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
    public async Task APendingCloseIsWrittenToTheStateFile_AndThePreRestartKeyboardStillDoesNothing()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        var parkedPath = Park_CloseRequest(session.OrchId);
        var parkedUtc = File.GetLastWriteTimeUtc(parkedPath);

        // ── The first process, on a FILE store ───────────────────────────────────────────────
        var firstTelegram = new CapturingTelegram_Fake();
        var firstEngine = Build_Engine(firstTelegram, EngineStateStore_Factory.Create_File(_paths, _log));

        Assert.True(
            await Run_Until_Async(firstEngine, () => firstTelegram.Find_ButtonFor(CONFIRM_LABEL) != null, 25_000),
            $"the close confirmation never reached the phone.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var preRestartButton = firstTelegram.Find_ButtonFor(CONFIRM_LABEL)!;

        // ── THE FILE ITSELF ─────────────────────────────────────────────────────────────────
        // Read as JSON rather than through the store, for the same reason the sibling disk test
        // gives: asking the store what it holds answers from memory even when it wrote nothing.
        Assert.True(File.Exists(_paths.EngineStateFile), $"the daemon's own state file was never created at '{_paths.EngineStateFile}'");

        var root = JsonNode.Parse(File.ReadAllText(_paths.EngineStateFile)) as JsonObject
            ?? throw new Exception($"'{_paths.EngineStateFile}' is not a JSON object:{Environment.NewLine}{File.ReadAllText(_paths.EngineStateFile)}");

        var records = root["closeConfirmations"]?.AsArray()
            ?? throw new Exception(
                "THE DEFECT THIS EXISTS FOR: the state file holds no closeConfirmations array at all, so a "
                + "decision on the owner's phone leaves nothing on disk that says so."
                + $"{Environment.NewLine}{File.ReadAllText(_paths.EngineStateFile)}");

        var record = Assert.Single(records)!;

        Assert.Equal(session.OrchId, record["orchId"]?.GetValue<string>());
        Assert.Equal(parkedPath, record["parkedPath"]?.GetValue<string>());
        Assert.Equal("Orchestration", record["kind"]?.GetValue<string>());
        Assert.Contains("supervisor", record["requester"]?.GetValue<string>() ?? "");

        // WHEN, AND UNTIL WHEN. Unix seconds, the units the serializer writes, read as the file holds
        // them rather than through the reader that produced them — or a wrong unit on both sides agrees.
        var askedUtc = DateTimeOffset.FromUnixTimeSeconds(record["askedUtc"]!.GetValue<long>()).UtcDateTime;
        var expiresUtc = DateTimeOffset.FromUnixTimeSeconds(record["expiresUtc"]!.GetValue<long>()).UtcDateTime;

        Assert.True(askedUtc > DateTime.UtcNow.AddMinutes(-5), $"the prompt was recorded as asked at {askedUtc:O}, which is not now");
        Assert.Equal(
            parkedUtc.AddHours(CloseConfirmation_Parking.EXPIRY_HOURS),
            expiresUtc,
            TimeSpan.FromSeconds(1));

        // THE PAYLOAD IS NOT IN THE FILE, and that is deliberate — see the class remarks. A record
        // that carries no ticket cannot be turned into a live keyboard by a later refactor without
        // somebody deciding to put the ticket there first.
        Assert.DoesNotContain(preRestartButton, File.ReadAllText(_paths.EngineStateFile), StringComparison.Ordinal);

        // AND THE OTHER FAMILY IS STILL EMPTY. This is the exact reading that cost the evening: the
        // two live side by side, and the file now shows both rather than one of them.
        Assert.Empty(root["pendingButtons"]!.AsArray());

        // ── kill -9 ─────────────────────────────────────────────────────────────────────────
        // ── The second process, over the same PATH and a store it builds itself ─────────────
        var secondTelegram = new CapturingTelegram_Fake();
        var secondEngine = Build_Engine(secondTelegram, EngineStateStore_Factory.Create_File(_paths, _log));

        secondTelegram.Queue_Updates(Build_CallbackTapJson(preRestartButton));

        // The sweep re-asks within a tick or two, which is the recovery this design relies on.
        Assert.True(
            await Run_Until_Async(secondEngine, () => secondTelegram.Find_ButtonFor(CONFIRM_LABEL) != null, 25_000),
            $"the restarted host never re-asked the owner.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // THE GUARANTEE, UNCHANGED: the keyboard minted before the restart answers nothing. Option 3
        // — honouring it — changes what a restart MEANS and is the owner's decision, not a refactor's.
        var reread = _store.Get_Session_OrNull(session.OrchId);
        Assert.NotNull(reread);
        Assert.Null(reread.ClosedUtc);
        Assert.True(File.Exists(parkedPath), "the pre-restart tap resolved the parked request — the restart guarantee is gone");

        // The fresh prompt is a DIFFERENT keyboard, so the owner's next tap answers the question that
        // is actually live.
        Assert.NotEqual(preRestartButton, secondTelegram.Find_ButtonFor(CONFIRM_LABEL));

        // A1e.2 — THE JOURNAL CAN TELL THE TWO APART. On the VPS it could not: the line for a first
        // ask and the line for a re-ask after a restart were the same sentence.
        Assert.True(
            _log.Has_Info_Containing("AGAIN"),
            $"the re-ask reads exactly like a first ask, so a journal cannot tell them apart.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
    }

    IBridgeEngine Build_Engine(ITelegramApiClient telegram, IEngineStateStore engineState)
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, telegram,
            engineState, Clock_Factory.Create_System(),
            BridgeTestTiming.Fast());
    }

    string Park_CloseRequest(string orchId)
    {
        var requestFile = Path.Combine(_paths.RequestsFolder, $"close-{Guid.NewGuid():N}.json");

        File.WriteAllText(
            requestFile,
            $$"""{"action":"close-orchestration","orchId":"{{orchId}}","reason":"{{CLOSE_REASON}}","requester":"supervisor of {{orchId}}"}""");

        return CloseConfirmation_Parking.Park(_paths, requestFile);
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>
    /// A close-confirmation tap is matched on its DATA alone, so the message it claims to come from
    /// is arbitrary — which is itself the point: nothing about the message rescues a dead payload.
    /// </summary>
    static string Build_CallbackTapJson(string callbackData)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":4002,\"callback_query\":{\"id\":\"cbq-close-1\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":9001,\"message_thread_id\":{TOPIC_ID},"
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

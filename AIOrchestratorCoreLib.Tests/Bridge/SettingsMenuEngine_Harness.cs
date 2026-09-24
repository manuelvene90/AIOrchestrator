using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.SettingsMenu;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE REAL ENGINE, DRIVEN FROM THE PHONE, for the /settings tests (plan 04 Task 5) — the harness of
/// <see cref="ATapIsTheTypedCommandTests"/> (temp root, config and secrets files, <see cref="ScriptedInbound_Fake"/>,
/// <see cref="RecordingLog_Fake"/>) with three things the menu needs on top:
/// <list type="bullet">
/// <item>the REAL per-message edit gate in front of every edit (<see cref="ScriptedInbound_Fake.Gate_EditsThrough"/>)
/// on a budget built with production's own thirty-second gap and handed to the engine as its budget — so D7's
/// exemption is exercised through the call the real client makes, not assumed (ruling P14);</item>
/// <item>an in-memory engine-state store two engines can share, for the restart (ruling P23);</item>
/// <item>a clock the test moves, for D9's five-minute window.</item>
/// </list>
/// A TERMINAL-RUNNER orchestration only: an engine test that registered a print-runner session could spawn a
/// live `claude` through the dispatcher the factory hard-wires.
/// </summary>
internal sealed class SettingsMenuEngine_Harness : IDisposable
{
    public const long SUPERGROUP_CHAT_ID = -1002233445566;
    public const long OWNER_USER_ID = 555000111;
    public const long TOPIC_ID = 4242;

    public const string MENU_HEADER = "Settings — preset";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IOrchestrationLauncher _launcher;

    long _nextUpdateId = 9000;

    public ISupervisionPaths Paths { get; }
    public IOrchestrationSessionStore Store { get; }
    public ScriptedInbound_Fake Telegram { get; } = new();
    public RecordingLog_Fake Log { get; } = new();
    public IEngineStateStore EngineState { get; } = EngineStateStore_Factory.Create_InMemory();
    public FixedClock_Fake Clock { get; } = new(DateTime.UtcNow);
    public ITelegramSendBudget Budget { get; } = TelegramSendBudget_Factory.Create_WithEditGap(TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE);
    public string OrchId { get; }

    public SettingsMenuEngine_Harness(string name, string? extraConfigJson = null)
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-{name}-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        Paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(Paths.RequestsFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.GeneralChannelFile)!);

        File.WriteAllText(Paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");
        File.WriteAllText(
            Paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID}{extraConfigJson ?? ""}}}");

        Store = OrchestrationSessionStore_Factory.Create(Paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(Paths);
        _launcher = OrchestrationLauncher_Factory.Create(Paths, _configProvider, Store, new RecordingSpawner_Fake(), Log);

        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        Store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        OrchId = session.OrchId;

        var channelFile = Paths.Get_OwnerChannelFile(OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        Telegram.Gate_EditsThrough(Budget);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    public IBridgeEngine Build_Engine()
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            Paths, _configProvider, Store, _launcher, Log, Telegram, EngineState, Clock, BridgeTestTiming.Fast(),
            sendBudget: Budget);
    }

    /// <summary>Runs <paramref name="body"/> while the engine's loops run, and stops them after.</summary>
    public async Task Run_WhileAsync(IBridgeEngine engine, Func<Task> body)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);

        try
        {
            Assert.True(
                await Wait_Until_Async(() => Telegram.UpdateCalls >= 2, 20_000),
                $"the inbound loop never polled.{Environment.NewLine}{Log.Dump()}");

            await Task.Delay(BridgeTestTiming.Window_ForTicks(10));
            await body();
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
                // The only way these loops end.
            }
        }
    }

    public Task Owner_Types_Async(string text, long? threadId)
    {
        var updateId = _nextUpdateId++;
        var thread = threadId == null ? "" : $"\"message_thread_id\":{threadId},";

        return Deliver_Async(
            $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{updateId},"
            + $"{thread}\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":{JsonValue.Create(text).ToJsonString()}}}}}]}}");
    }

    public Task Owner_Taps_Async(string callbackData, long messageId, long? threadId = null)
    {
        var updateId = _nextUpdateId++;
        var thread = threadId == null ? "" : $"\"message_thread_id\":{threadId},";

        return Deliver_Async(
            $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"callback_query\":{{\"id\":\"cbq-{updateId}\","
            + $"\"data\":{JsonValue.Create(callbackData).ToJsonString()},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{messageId},{thread}"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}");
    }

    /// <summary>
    /// Queues one update and returns once its batch has been HANDLED: the poll that took it increments the call
    /// count, and the one after it can only start once that batch is done — so two increments past the count at
    /// queue time is "handled", never a guess at a delay.
    /// </summary>
    async Task Deliver_Async(string updatesJson)
    {
        var before = Telegram.UpdateCalls;

        Telegram.Queue_Updates(updatesJson);

        Assert.True(
            await Wait_Until_Async(() => Telegram.UpdateCalls >= before + 2, 20_000),
            $"the update was never handled.{Environment.NewLine}{Log.Dump()}");
    }

    /// <summary>The id of the newest message that is a machine settings menu.</summary>
    public long Live_MenuMessageId()
    {
        return Telegram.LastSentMessageId_Containing(MENU_HEADER);
    }

    /// <summary>What the phone was SENT since <paramref name="sentIndex"/>, leaving out the two surfaces the app repaints on its own.</summary>
    public IReadOnlyList<(long Id, string Text, TelegramSendSounds Sound)> Sent_Since(int sentIndex)
    {
        return [.. Telegram.Sent_WithIds
            .Skip(sentIndex)
            .Where(sent => !sent.Text.Contains("PULSE", StringComparison.Ordinal)
                && !sent.Text.Contains(GeneralDashboard_Composer.HEADING, StringComparison.Ordinal))];
    }

    public int Count_RoutedMessages()
    {
        return Log.Dump().Split(Environment.NewLine).Count(line => line.Contains("Owner message buffered", StringComparison.Ordinal));
    }

    public static int Index_Of(string path)
    {
        for (var index = 0; index < Catalog.ALL.Count; index++)
        {
            if (Catalog.ALL[index].Path == path)
                return index;
        }

        throw new ArgumentException($"'{path}' is not in the catalogue");
    }

    public static string Payload(SettingsMenuViews view, string path, SettingsMenuEdits? edit, string? word, int page = 0)
    {
        return SettingsButton_Data.Build(view, null, Index_Of(path), page, edit, word);
    }

    public JsonNode? Read_ConfigValue_OrNull(string dottedPath)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(Paths.ConfigFile));

        foreach (var part in dottedPath.Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node))
                return null;
        }

        return node;
    }

    public static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 50)
        {
            if (condition())
                return true;

            await Task.Delay(50);
        }

        return condition();
    }
}

using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;

namespace AIOrchestratorCoreLib.Bridge.BridgeEngine;

public static class BridgeEngine_Factory
{
    /// <summary>
    /// Builds the engine with persisted bridge state (mirror offsets + last Telegram update id).
    /// The Telegram client is created only when the config carries complete Telegram settings.
    /// </summary>
    public static IBridgeEngine Create(
        ISupervisionPaths paths,
        IOrchestratorConfig config,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log)
    {
        var (fileOffsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(paths);
        var tailer = ChannelTailer_Factory.Create(fileOffsets);

        ITelegramApiClient? telegramClient = null;

        if (config.Is_TelegramConfigured())
        {
            var botToken = config.TelegramBotToken
                ?? throw new Exception("Is_TelegramConfigured returned true but the bot token is null");
            var supergroupChatId = config.TelegramSupergroupChatId
                ?? throw new Exception("Is_TelegramConfigured returned true but the supergroup chat id is null");

            telegramClient = TelegramApiClient_Factory.Create(botToken, supergroupChatId);
        }

        var watchdog = SessionWatchdog_Factory.Create(paths, store, launcher, log);

        return new BridgeEngineModel(paths, config, store, launcher, log, tailer, telegramClient, watchdog, lastUpdateId);
    }
}

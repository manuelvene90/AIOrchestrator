using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
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
    /// The Telegram client is created from the config AT STARTUP — changing the bot token or the
    /// chat/user ids needs an app restart. Everything else (repos, models) is read live via the
    /// provider, because agents edit config.json at runtime.
    /// </summary>
    public static IBridgeEngine Create(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log)
    {
        // Passing the log so a quarantined (corrupt) cursor file is visible rather than a silent reset.
        var (fileOffsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(paths, log);
        var tailer = ChannelTailer_Factory.Create(fileOffsets);

        var startupConfig = configProvider.Get_Current();
        ITelegramApiClient? telegramClient = null;

        if (startupConfig.Is_TelegramConfigured())
        {
            var botToken = startupConfig.TelegramBotToken
                ?? throw new Exception("Is_TelegramConfigured returned true but the bot token is null");
            var supergroupChatId = startupConfig.TelegramSupergroupChatId
                ?? throw new Exception("Is_TelegramConfigured returned true but the supergroup chat id is null");

            telegramClient = TelegramApiClient_Factory.Create(botToken, supergroupChatId);
        }

        var watchdog = SessionWatchdog_Factory.Create(paths, store, launcher, log);
        var translator = Translation.MessageTranslator.MessageTranslator_Factory.Create(log);
        var transcriber = Transcription.VoiceTranscriber.VoiceTranscriber_Factory.Create(log);

        return new BridgeEngineModel(paths, configProvider, store, launcher, log, tailer, telegramClient, watchdog, translator, transcriber, lastUpdateId);
    }
}

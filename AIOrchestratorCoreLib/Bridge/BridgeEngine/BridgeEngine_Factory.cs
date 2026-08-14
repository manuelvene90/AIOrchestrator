using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Translation.MessageTranslator;
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

        return Create_WithTelegramClient(paths, configProvider, store, launcher, log, telegramClient);
    }

    /// <summary>
    /// THE ENGINE-LEVEL TELEGRAM TEST SEAM — not a production mode. Every production caller uses the
    /// overload above, which builds the real client from config; this one exists so a test can hand
    /// in a fake and drive the mirror path with a send that FAILS.
    ///
    /// WHY IT HAD TO EXIST: `Mirror_Append_Async` returns early when the client is null, ABOVE the
    /// owner-push logic, so every test running in file-only mode passes straight over that code. The
    /// defect where a failed send dropped the owner's answer (R1) was unreachable from a test until
    /// this seam existed. `BridgeEngineModel` is internal and this repo has twice refused
    /// `InternalsVisibleTo`, so an additive public overload is the in-idiom alternative.
    ///
    /// A null client here means the same thing it means in production: file-only mode, no phone.
    /// </summary>
    public static IBridgeEngine Create_WithTelegramClient(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log,
        ITelegramApiClient? telegramClient)
    {
        return Create_WithTelegramClientAndTranslator(
            paths, configProvider, store, launcher, log, telegramClient,
            Translation.MessageTranslator.MessageTranslator_Factory.Create(log));
    }

    /// <summary>
    /// THE TRANSLATOR TEST SEAM, added for the same reason as the one above and by the same idiom.
    ///
    /// WHY IT HAD TO EXIST: <c>Take_ReadyDeliveries</c> empties the buffer for the whole batch before
    /// the loop body runs, so from that point the local variables are the only copy of the owner's
    /// words. The append's own failure has a put-back (R1); a translator that THROWS did not, and it
    /// destroyed the owner's text outright — a route unreachable from a test while the real
    /// translator was the only one obtainable. Hand in one that fails and the route becomes testable.
    ///
    /// Every production caller uses the overload above.
    /// </summary>
    public static IBridgeEngine Create_WithTelegramClientAndTranslator(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log,
        ITelegramApiClient? telegramClient,
        IMessageTranslator translator)
    {
        // Passing the log so a quarantined (corrupt) cursor file is visible rather than a silent reset.
        var (fileOffsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(paths, log);
        var tailer = ChannelTailer_Factory.Create(fileOffsets);

        var watchdog = SessionWatchdog_Factory.Create(paths, store, launcher, log);
        var transcriber = Transcription.VoiceTranscriber.VoiceTranscriber_Factory.Create(log);

        return new BridgeEngineModel(paths, configProvider, store, launcher, log, tailer, telegramClient, watchdog, translator, transcriber, lastUpdateId);
    }
}

using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;
using AIOrchestratorCoreLib.Time.Clock;
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
        return Create_WithTiming(paths, configProvider, store, launcher, log, BridgeEngineTiming_Factory.Create_Production());
    }

    /// <summary>
    /// THE TIMING TEST SEAM ON THE PRODUCTION PATH, added by the same idiom as the two below and
    /// for a measured reason: a dozen test files drive the real engine through <see cref="Create"/>
    /// and therefore sat in front of the real 2-second tick, which is wall-clock sleep and nothing
    /// else. This overload is <see cref="Create"/> with the periods named instead of assumed —
    /// same client, same store, same clock — and <see cref="Create"/> is now the one-line caller
    /// that names the shipped ones. See <see cref="IBridgeEngineTiming"/>.
    /// </summary>
    public static IBridgeEngine Create_WithTiming(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log,
        IBridgeEngineTiming timing)
    {
        var startupConfig = configProvider.Get_Current();
        ITelegramApiClient? telegramClient = null;
        ITelegramSendBudget? sendBudget = null;

        if (startupConfig.Is_TelegramConfigured())
        {
            var botToken = startupConfig.TelegramBotToken
                ?? throw new Exception("Is_TelegramConfigured returned true but the bot token is null");
            var supergroupChatId = startupConfig.TelegramSupergroupChatId
                ?? throw new Exception("Is_TelegramConfigured returned true but the supergroup chat id is null");

            // THE SEND ALLOWANCE IS RESTORED, NOT RESET (brief F5). The same object goes to the
            // client, which spends from it, and to the engine, which writes it back into
            // .bridge-state.json on every tick — so a restart resumes where the last process
            // stopped instead of granting a free burst of twenty messages.
            var persisted = BridgeState_Store.Load_SendBucket_OrNull(paths);

            sendBudget = persisted == null
                ? TelegramSendBudget_Factory.Create_Fresh()
                : TelegramSendBudget_Factory.Create_FromPersisted(persisted.Value.Tokens, persisted.Value.RefilledUtc, DateTime.UtcNow);

            telegramClient = TelegramApiClient_Factory.Create(botToken, supergroupChatId, sendBudget);
        }

        return Create_WithTelegramClient(paths, configProvider, store, launcher, log, telegramClient, timing, sendBudget);
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
    ///
    /// It used to delegate to a fourth seam that also took an <c>IMessageTranslator</c>; the Telegram
    /// translation layer was abolished on 2026-09-09, so that overload went with it and this one now
    /// calls <see cref="Create_WithDecisionState"/> directly.
    /// </summary>
    public static IBridgeEngine Create_WithTelegramClient(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log,
        ITelegramApiClient? telegramClient,
        IBridgeEngineTiming timing,
        ITelegramSendBudget? sendBudget = null)
    {
        // NAMED, not positional. The rebase onto brief B put an `IHostWindowing?` in the slot this
        // argument used to occupy, and the only thing that stopped it being handed to the wrong
        // parameter was that the two types differ. The next optional parameter added here might not
        // be so lucky, so the trailing ones are named from now on.
        return Create_WithDecisionState(
            paths, configProvider, store, launcher, log, telegramClient,
            EngineStateStore_Factory.Create_File(paths, log),
            Clock_Factory.Create_System(),
            timing,
            sendBudget: sendBudget);
    }

    /// <summary>
    /// THE RESTART TEST SEAM, added by the same idiom as the two above and for the same class of
    /// reason.
    ///
    /// <para>
    /// WHY IT HAD TO EXIST: the claim this stage makes is "kill the bridge with decisions pending
    /// and start it again — nothing is lost". Asserting that needs TWO engines sharing ONE store,
    /// and with the store built inside the factory the only way to share it is a filesystem and a
    /// real clock, which turns a deadline test into a test that waits for the deadline. Handing in
    /// an in-memory store and a clock the test moves makes both properties assertions instead of
    /// arguments.
    /// </para>
    /// <para>
    /// Every production caller uses the overload above. The watchdog's crash-loop counters are
    /// restored HERE rather than inside the engine, because the engine is handed the watchdog
    /// already built and the counters belong to it — the engine only persists them.
    /// </para>
    /// </summary>
    public static IBridgeEngine Create_WithDecisionState(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log,
        ITelegramApiClient? telegramClient,
        IEngineStateStore engineStateStore,
        IClock clock,
        IBridgeEngineTiming timing,

        // OPTIONAL, AND RESOLVED PER HOST WHEN ABSENT: every production caller wants "whatever this
        // machine can do", which is what the null means. A test passes
        // HostWindowing_Factory.Create_Unsupported() to assert the refusal REGARDLESS of the OS the
        // suite happens to run on — a probe that depends on its own host being Linux is a probe that
        // silently stops testing anything on Windows.
        Hosting.HostWindowing.IHostWindowing? hostWindowing = null,

        // Null in file-only mode and on the test seams that hand in their own client: with no
        // budget the engine simply writes no sendBucket key, and the next start begins full.
        ITelegramSendBudget? sendBudget = null)
    {
        // Passing the log so a quarantined (corrupt) cursor file is visible rather than a silent reset.
        var (fileOffsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(paths, log);
        // The quiet period comes from the TIMING and not from the tailer's own default, for the reason
        // every other period on IBridgeEngineTiming is there: an engine-driving test pays it in wall
        // clock once per mirrored entry. Production resolves it back to the tailer's constant.
        var tailer = ChannelTailer_Factory.Create(
            fileOffsets,
            TimeSpan.FromMilliseconds(timing.TrailingEntryQuietMilliseconds),
            clock);

        var watchdog = SessionWatchdog_Factory.Create(paths, configProvider, store, launcher, log);
        var transcriber = Transcription.VoiceTranscriber.VoiceTranscriber_Factory.Create(log);

        // The print dispatcher idles unless a role is configured `runner: print` — with a stock
        // config.json it discovers no registered session and its tick costs one Load_All.
        var printTurns = PrintTurnDispatcher_Factory.Create(paths, store, configProvider, ClaudeInvocation_Resolver.Resolve_ForThisOs(), log);

        // ONE LOAD, here, for the reason the cursor above is also loaded here: a primary
        // constructor's field initialisers cannot share a value between them, so loading inside the
        // engine would mean reading the file once per restored field.
        var restoredState = engineStateStore.Load_OrEmpty();

        watchdog.Restore_ConsecutiveRespawns(restoredState.ConsecutiveRespawns);

        return new BridgeEngineModel(
            paths, configProvider, store, launcher, log, tailer, telegramClient, watchdog, transcriber,
            printTurns, lastUpdateId, engineStateStore, restoredState, clock, timing,
            hostWindowing ?? Hosting.HostWindowing.HostWindowing_Factory.Create_ForThisHost(),
            sendBudget);
    }
}

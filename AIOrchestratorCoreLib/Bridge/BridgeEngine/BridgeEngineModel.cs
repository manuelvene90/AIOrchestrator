using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.WindowFocus;
using AIOrchestratorCoreLib.GeneralSupervision.PendingRequests;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Git;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Usage;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Termination;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;
using AIOrchestratorCoreLib.Transcription.VoiceTranscriber;
using AIOrchestratorCoreLib.Translation.MessageTranslator;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;

namespace AIOrchestratorCoreLib.Bridge.BridgeEngine;

internal sealed class BridgeEngineModel(
    ISupervisionPaths paths,
    IOrchestratorConfigProvider configProvider,
    IOrchestrationSessionStore store,
    IOrchestrationLauncher launcher,
    IOrchestrationLog log,
    IChannelTailer tailer,
    ITelegramApiClient? telegramClient,
    ISessionWatchdog watchdog,
    IMessageTranslator translator,
    IVoiceTranscriber transcriber,
    long initialLastUpdateId) : IBridgeEngine
{
    /// <summary>In-memory inline-button registry cap — taps on evicted buttons get an "expired" toast.</summary>
    const int BUTTON_REGISTRY_CAP = 300;

    /// <summary>Channel silence that counts as a stall once nobody is mid-turn.</summary>
    const int STALL_ALERT_MINUTES = 25;

    /// <summary>How long an implementer may leave a brief unanswered before the app nudges it.</summary>
    const int IMPLEMENTER_NUDGE_MINUTES = 8;

    /// <summary>How long the owner may wait for their supervisor's acknowledgement before the app steps in.</summary>
    const int OWNER_REPLY_GRACE_SECONDS = 150;

    /// <summary>
    /// The communicator waited ~45 s before narrating, so an IDLE supervisor picks the message up
    /// itself and the owner gets the real answer instead of a status line. Same number, same reason.
    /// </summary>
    const int NARRATION_FIRST_DELAY_SECONDS = 45;

    /// <summary>The communicator's "still at it" cadence while the supervisor stays busy.</summary>
    const int NARRATION_REPEAT_SECONDS = 180;

    /// <summary>The supervisor's old ~30-minute STATUS cadence, now emitted by the app.</summary>
    const int PERIODIC_STATUS_SECONDS = 1800;

    /// <summary>
    /// How long a nudged, idle session may stay frozen before it is declared ORPHANED. The nudge
    /// changed its channel, so a live watcher fires within seconds — this window is generous
    /// enough that only a genuinely absent listener runs it out.
    /// </summary>
    const int ORPHAN_CONFIRM_MINUTES = 6;

    const int MIRROR_TICK_MILLISECONDS = 2000;
    const int INBOUND_LONG_POLL_SECONDS = 20;
    const int INBOUND_ERROR_BACKOFF_START_MILLISECONDS = 5000;
    const int INBOUND_ERROR_BACKOFF_MAX_MILLISECONDS = 60000;
    const int LIMIT_CHECK_INTERVAL_SECONDS = 60;

    /// <summary>Pause before relaunching a bridge loop that ended, so a broken loop cannot spin.</summary>
    const int LOOP_RELAUNCH_DELAY_MILLISECONDS = 5000;

    /// <summary>Consecutive quick deaths after which a loop is abandoned instead of relaunched forever.</summary>
    const int LOOP_RELAUNCH_CAP = 10;

    /// <summary>
    /// A loop that ran this long before dying was retrying, not spinning — its relaunch allowance
    /// starts over. It MUST stay well below the Telegram HttpClient timeout (90 s): a wedged
    /// endpoint kills each incarnation at ~90 s, and a threshold above that would classify every
    /// one of those deaths as unhealthy, never reset the counter, and make the guard give up after
    /// ~17 minutes — abandoning the bridge during precisely the outage it was written to survive.
    /// Six times the relaunch pause is comfortably clear of a spin and comfortably under any
    /// network timeout in this app.
    /// </summary>
    const int LOOP_HEALTHY_RUN_MILLISECONDS = 30000;

    /// <summary>Below this age /cost prints no burn rate — dividing by minutes invents a number.</summary>
    const double MINIMUM_BURN_RATE_HOURS = 0.25;

    /// <summary>The owner often texts several messages in a row — quiet time before delivery as ONE entry.</summary>
    /// <summary>
    /// Short ON PURPOSE: most messages arrive alone, and a long window makes every one of them feel
    /// slow. The multi-message case is covered explicitly by WAIT … GO instead of by making
    /// everyone wait (owner directive).
    /// </summary>
    const int OWNER_AGGREGATION_SECONDS = 4;

    /// <summary>A forgotten WAIT must not swallow the owner's messages forever.</summary>
    const int OWNER_HOLD_CAP_SECONDS = 60;

    const string GLOBAL_ORCH_ID = "";

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestratorConfigProvider _configProvider = configProvider;
    readonly IOrchestrationSessionStore _store = store;
    readonly IOrchestrationLauncher _launcher = launcher;
    readonly IOrchestrationLog _log = log;
    readonly IChannelTailer _tailer = tailer;
    readonly ITelegramApiClient? _telegramClient = telegramClient;
    readonly ISessionWatchdog _watchdog = watchdog;
    readonly IMessageTranslator _translator = translator;
    readonly IVoiceTranscriber _transcriber = transcriber;
    readonly Dictionary<string, (long? ThreadId, string OptionText, long GroupId, string QuestionText)> _buttonOptions = [];
    readonly Queue<string> _buttonOrder = new();

    /// <summary>"&lt;file&gt;|&lt;line&gt;" of every malformed header already reported — say it once, not every tick.</summary>
    readonly HashSet<string> _reportedMalformedHeaders = [];

    /// <summary>Channels whose pre-existing malformed headers have been absorbed as history.</summary>
    readonly HashSet<string> _channelsShapeBaselined = [];
    readonly Lock _buttonLock = new();
    long _buttonSequence;
    long _buttonGroupSequence;

    /// <summary>One alert per stall/budget EPISODE — cleared when traffic resumes (stalls only).</summary>
    readonly HashSet<string> _stallAlertedOrchIds = [];
    readonly HashSet<string> _budgetAlertedOrchIds = [];
    /// <summary>When each member was nudged — the nudge doubles as the PROBE that proves a watcher exists.</summary>
    readonly Dictionary<string, DateTime> _nudgedMemberUtc = [];

    /// <summary>When the supervisor last posted a verdict into a spoke — the ledger's due-by signal.</summary>
    readonly Dictionary<string, DateTime> _lastSupervisorVerdictUtc = [];
    readonly HashSet<string> _ledgerBehindReportedOrchIds = [];
    readonly Dictionary<string, string> _reportedLedgerShapeByOrchId = [];
    readonly Dictionary<string, (string Line, DateTime SentUtc)> _lastHandoffLineByOrchId = [];
    readonly Lock _stateLock = new();
    readonly IOwnerDeliveryBuffer _ownerDeliveryBuffer = OwnerDeliveryBuffer_Factory.Create(OWNER_AGGREGATION_SECONDS, OWNER_HOLD_CAP_SECONDS);
    readonly Dictionary<string, (string OrchId, long? ThreadId)> _deliveryTargets = [];
    readonly Lock _deliveryLock = new();

    /// <summary>Topic name last pushed to Telegram, so the glyph sync only calls the API on a real change.</summary>
    readonly Dictionary<string, string> _appliedTopicNames = [];

    /// <summary>
    /// The receipt message being EVOLVED per thread (✓ → ✓✓ → ✓✓ · handoff), so three states cost
    /// one message instead of three. Key 0 = the General topic (no thread id).
    /// </summary>
    readonly Dictionary<long, long> _receiptMessageIdByThread = [];
    readonly Lock _receiptLock = new();

    /// <summary>
    /// Message ids KNOWN to belong to each topic (ours + the owner's), for /clear. Telegram message
    /// ids are chat-wide, not per topic, so deleting a computed RANGE would wipe other topics —
    /// only ids observed in this topic may ever be deleted. Key 0 = the General topic.
    /// </summary>
    readonly Dictionary<long, List<long>> _knownMessageIdsByThread = [];
    readonly Lock _knownMessageIdsLock = new();

    /// <summary>
    /// Owner messages handed over and NOT yet answered by their supervisor. Tracked so a receipt
    /// can never stay frozen on "thinking…" — the owner always learns what became of what they
    /// sent, even if the supervisor goes idle without replying.
    /// </summary>
    readonly Dictionary<string, PendingOwnerReply> _pendingOwnerReplies = [];

    /// <summary>Per-orchestration clock for the app-emitted periodic STATUS.</summary>
    readonly Dictionary<string, DateTime> _lastPeriodicStatusUtc = [];

    /// <summary>Per-orchestration cooldown so the brevity feedback never becomes noise itself.</summary>
    readonly Dictionary<string, DateTime> _lastVerbosityNudgeUtc = [];

    sealed class HoldReceipt
    {
        public long? MessageId;
        public int HeldCount;
    }

    /// <summary>Target channel → the WAIT acknowledgement being kept up to date while held.</summary>
    readonly Dictionary<string, HoldReceipt> _holdReceipts = [];

    sealed class OpenQuestion
    {
        public string OrchId = "";
        public string Text = "";
        public DateTime AskedUtc;
    }

    /// <summary>
    /// How long an unanswered question freezes the conversation. Long enough to make "a question
    /// stops the turn" real; short enough that an owner who never answers is not starved of
    /// everything else the orchestration has to say.
    /// </summary>
    const int QUESTION_HOLD_CAP_MINUTES = 10;

    /// <summary>Presence of this file in an orchestration folder stops its supervisor dead.</summary>
    public const string AWAITING_ANSWER_FLAG_FILE = ".awaiting-answer";

    /// <summary>How long EVERYTHING must be idle before a suppressed last word is released.</summary>
    const int SILENT_DEADLOCK_MINUTES = 5;

    sealed class SuppressedEntry
    {
        public string Text = "";
        public DateTime SuppressedUtc;
    }

    /// <summary>Per orchestration: the last supervisor entry we chose not to push.</summary>
    readonly Dictionary<string, SuppressedEntry> _lastSuppressedEntry = [];

    /// <summary>
    /// Orchestrations where the owner has spoken and the supervisor's reply has NOT yet been pushed.
    /// Its whole purpose is to guarantee an answer always reaches them, so it is owned by the mirror
    /// path alone — sharing _pendingOwnerReplies for this dropped every answer.
    /// </summary>
    readonly HashSet<string> _ownerAwaitingAnswer = [];

    /// <summary>Telegram message id → a question the owner has NOT answered yet.</summary>
    readonly Dictionary<long, OpenQuestion> _openQuestions = [];

    sealed class AwayTracker
    {
        public int UnansweredCount;
        public bool IsQuiet;
    }

    /// <summary>Per-orchestration "have I been talking into the void?" counter. See AwayMode_Policy.</summary>
    readonly Dictionary<string, AwayTracker> _awayTrackers = [];

    /// <summary>
    /// APP-WIDE away state. The owner is at their phone or not — never present for one orchestration
    /// and absent for another, which is why this is one flag and the app drives every orchestration
    /// from it instead of supervisors relaying to each other.
    /// </summary>
    bool _awayActive;

    /// <summary>The owner's last message in ANY topic — presence anywhere counts everywhere.</summary>
    DateTime _lastOwnerMessageUtc = DateTime.UtcNow;

    /// <summary>
    /// Guards _holdReceipts and _pendingOwnerReplies. GO flushes from the INBOUND loop (waiting for
    /// the 2 s mirror tick would be exactly the lag GO exists to remove), so both dictionaries are
    /// now touched from two threads. Short, non-async critical sections only — never hold this
    /// across an await.
    /// </summary>
    readonly Lock _ownerStateLock = new();

    sealed class PendingOwnerReply
    {
        public long? ThreadId;
        public long? ReceiptMessageId;
        public int SupervisorEntryCountAtDelivery;
        public DateTime DeliveredUtc;
        public bool Nudged;

        /// <summary>Last time the owner was told what the busy supervisor is doing (the old communicator's job).</summary>
        public DateTime LastNarratedUtc;

        /// <summary>
        /// The ONE narration message, edited in place on every repeat. Without it a supervisor that
        /// thinks for ten minutes left the owner with a column of near-identical "still at it" texts
        /// — each one a phone notification — which is exactly the waterfall this system exists to
        /// prevent (owner, 2026-08-10).
        /// </summary>
        public long? NarrationMessageId;

        /// <summary>Said once, when the turn the owner was told about actually ends.</summary>
        public bool TurnEndAnnounced;
    }

    long _lastUpdateId = initialLastUpdateId;
    DateTime _lastLimitCheckUtc = DateTime.MinValue;

    /// <summary>App-wide Do-Not-Disturb: everything is kept and replayed when it goes off.</summary>
    volatile bool _telegramMuted;

    /// <summary>App-wide silence: everything is DROPPED while it lasts (the owner works at the PC).</summary>
    volatile bool _silenceAllTopics;

    public event Action<string>? OrchestrationActivity;
    public event Action<bool>? MutedChanged;
    public event Action<bool>? SilenceAllChanged;
    public event Action<bool>? ItalianLayerChanged;

    /// <summary>
    /// Flips the 🇮🇹 translation layer and PERSISTS it: the config provider reloads on the file's
    /// write stamp, so the next outbound message already honours the new setting — there is no
    /// in-memory copy of this flag to keep in step.
    /// </summary>
    public void Set_ItalianLayer(bool enabled)
    {
        var current = _configProvider.Get_Current();

        if (current.TelegramItalianLayer == enabled)
            return;

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Factory.Create_WithItalianLayer(current, enabled), _paths);

        _log.Log_Info(GLOBAL_ORCH_ID, enabled
            ? "Italian layer ON — outbound Telegram traffic is translated on the way out"
            : "Italian layer OFF — outbound Telegram traffic goes out as the agents wrote it");

        try
        {
            ItalianLayerChanged?.Invoke(enabled);
        }
        catch
        {
            // A faulty subscriber must not take the bridge down.
        }
    }

    public void Set_SilenceAllTopics(bool silenced)
    {
        if (_silenceAllTopics == silenced)
            return;

        _silenceAllTopics = silenced;

        _log.Log_Info(GLOBAL_ORCH_ID, silenced
            ? "App-wide silence ON — every topic's messages are DROPPED (not queued)"
            : "App-wide silence OFF");

        try
        {
            SilenceAllChanged?.Invoke(silenced);
        }
        catch
        {
            // A faulty subscriber must not take the bridge down.
        }
    }

    public void Set_TelegramMuted(bool muted)
    {
        if (_telegramMuted == muted)
            return;

        _telegramMuted = muted;
        _log.Log_Info(GLOBAL_ORCH_ID, muted
            ? "Telegram DND ON — outbound paused; pending traffic accumulates and is delivered in one burst on unmute"
            : "Telegram DND OFF — catching up: all pending channel traffic mirrors now");

        try
        {
            MutedChanged?.Invoke(muted);
        }
        catch
        {
            // A faulty subscriber must not take the bridge down.
        }
    }

    public async Task Run_Async(CancellationToken cancellationToken)
    {
        GeneralChannel_Initializer.Ensure_Exists(_paths);

        List<Task> loops = [Run_Supervised_Async("mirror", Run_MirrorLoop_Async, cancellationToken)];

        if (_telegramClient != null)
            loops.Add(Run_Supervised_Async("inbound", Run_InboundLoop_Async, cancellationToken));

        _log.Log_Info(GLOBAL_ORCH_ID, _telegramClient == null
            ? "Bridge started (file-only mode — Telegram not configured)"
            : "Bridge started (Telegram mirror + inbound routing active)");

        await Task.WhenAll(loops);
    }

    /// <summary>
    /// A bridge loop that ENDS while the app is still running is by definition a bug — and it was
    /// the one failure this app could not see. A loop that returns completes its Task
    /// SUCCESSFULLY, so the Task.WhenAll above sees nothing wrong and TaskScheduler's
    /// UnobservedTaskException never fires either. On 2026-08-11 both loops returned on a 90 s HTTP
    /// timeout and the bridge became a dead shell for hours — no mirroring, no owner messages, no
    /// request-file actions, no session respawns — while the app itself stayed alive and
    /// responding, so nothing anywhere reported a fault. Only an app restart brought it back.
    ///
    /// So a loop that ends is relaunched, loudly. The delay and the cap are what stop a genuinely
    /// broken loop from becoming a hot loop; the cap counts CONSECUTIVE quick deaths, so an app
    /// that runs for days and relaunches once is never starved of its allowance.
    /// </summary>
    async Task Run_Supervised_Async(string loopName, Func<CancellationToken, Task> loop, CancellationToken cancellationToken)
    {
        var consecutiveRelaunches = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var startedUtc = DateTime.UtcNow;

            try
            {
                await loop(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return;

                _log.Log_Error(GLOBAL_ORCH_ID, $"Bridge '{loopName}' loop ENDED on its own while the app is running — that is a bug; relaunching it", null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Log_Error(GLOBAL_ORCH_ID, $"Bridge '{loopName}' loop FAULTED while the app is running — relaunching it", ex);
            }

            // Long-lived before it died, so this is not a crash loop and the allowance starts over.
            if ((DateTime.UtcNow - startedUtc).TotalMilliseconds >= LOOP_HEALTHY_RUN_MILLISECONDS)
                consecutiveRelaunches = 0;

            consecutiveRelaunches++;

            if (consecutiveRelaunches > LOOP_RELAUNCH_CAP)
            {
                _log.Log_Error(GLOBAL_ORCH_ID, $"Bridge '{loopName}' loop died {consecutiveRelaunches} times in a row — giving up on it; the app must be restarted to get it back", null);
                await Alert_LoopAbandoned_BestEffort_Async(loopName, consecutiveRelaunches, cancellationToken);
                return;
            }

            try
            {
                await Task.Delay(LOOP_RELAUNCH_DELAY_MILLISECONDS, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The owner's complaint on 2026-08-11 was not that something broke — it was that they were cut
    /// off and had no way to KNOW. So the give-up path may not be silent: a log line nobody is
    /// reading is the same silence. Best-effort by construction, and it swallows everything
    /// including cancellation: this runs while the guard is already abandoning a loop, and an alert
    /// that threw out of the guard would take down the OTHER loop with it.
    ///
    /// If the bridge is abandoned because Telegram itself is unreachable, this send fails too — and
    /// that is fine. It costs one attempt, the log keeps the record, and the case it does cover (a
    /// loop failing for a reason that is not Telegram) is exactly the one the owner cannot
    /// otherwise see.
    /// </summary>
    async Task Alert_LoopAbandoned_BestEffort_Async(string loopName, int deaths, CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        try
        {
            await _telegramClient.Send_Message_Async(
                null,
                $"🛑 The bridge's '{loopName}' loop failed {deaths} times in a row and has been abandoned. "
                    + "Mirroring and/or your messages are DOWN until the app is restarted — nothing else will bring it back.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Loop-abandoned alert send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The TOKEN decides whether this loop may end — never the exception type. An
    /// HttpClient.Timeout expiry throws TaskCanceledException, which IS an
    /// OperationCanceledException, so the bare catch this filter replaced read a wedged Telegram
    /// endpoint as "shutting down, stop cleanly" and returned. On 2026-08-11 that killed the mirror
    /// loop silently — a `return` logs nothing — and with it the request-file protocol, the session
    /// watchdog and every alert, for hours. With the filter a timeout falls through to the generic
    /// catch below, is logged, and the loop keeps ticking.
    /// </summary>
    async Task Run_MirrorLoop_Async(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Execute_MirrorTick_Async(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Log_Error(GLOBAL_ORCH_ID, "Mirror tick failed", ex);
            }

            try
            {
                await Task.Delay(MIRROR_TICK_MILLISECONDS, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    async Task Execute_MirrorTick_Async(CancellationToken cancellationToken)
    {
        Process_PendingRequests();

        // After closes are processed, so a freshly-closed session is not immediately revived.
        _watchdog.Check_AndRestart_DeadSessions();

        // Owner texts flow to the agents regardless of DND — mute only pauses OUTBOUND.
        await Flush_OwnerDeliveries_Async(cancellationToken);

        // DND: skip tailing entirely — offsets freeze, so unmute delivers everything pending
        // in one catch-up burst (including supervisors' questions that waited for the owner).
        // Crash-loop alerts stay queued in the watchdog until unmute for the same reason.
        if (_telegramMuted && _telegramClient != null)
            return;

        await Send_CrashLoopAlerts_Async(cancellationToken);
        await Send_StallAlerts_Async(cancellationToken);
        await Send_BudgetAlerts_Async(cancellationToken);
        await Nudge_IdleImplementers_Async(cancellationToken);
        await Resolve_PendingOwnerReplies_Async(cancellationToken);

        var channels = Find_ActiveChannels();
        var pollResult = _tailer.Poll(channels);

        foreach (var truncatedFile in pollResult.TruncatedFiles)
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Channel file shrank (append-only protocol anomaly), offset reset: {truncatedFile}");

        foreach (var append in pollResult.CompletedAppends)
        {
            await Mirror_Append_Async(append, cancellationToken);
            Raise_OrchestrationActivity(append.Channel.OrchId);
        }

        await Check_UsageLimits_Async(cancellationToken);

        // Every tick, so a mode toggled from the APP's card or checkboxes reaches the Telegram
        // topic name too — not just the ones toggled by a Telegram command. It only calls the API
        // when a name actually changed.
        await Sync_TopicNames_BestEffort_Async(cancellationToken);

        await Check_LedgerHealth_Async(cancellationToken);
        await Check_ChannelShapes_Async(cancellationToken);
        Expire_StaleAwaitingAnswerFlags();
        await Break_SilentDeadlock_Async(cancellationToken);
        await Check_AwayMode_Async(cancellationToken);
        await Push_PeriodicStatus_Async(cancellationToken);

        Compact_LongChannels();
        Persist_BridgeState();
    }

    /// <summary>
    /// Archives the old tail of long channels so respawned sessions boot cheaply. Runs AFTER the
    /// mirror poll and re-anchors the tailer's offset to the rewritten file — otherwise the
    /// shrink reads as a protocol anomaly and the whole file is re-mirrored to Telegram.
    /// </summary>
    void Compact_LongChannels()
    {
        foreach (var channel in ChannelDiscovery.Find_ChannelFiles(_paths))
        {
            var newLength = Channel_Compactor.Compact_IfNeeded(channel.FilePath);

            if (newLength == null)
                continue;

            _tailer.Set_Offset(channel.FilePath, newLength.Value);
            _log.Log_Info(channel.OrchId, $"Channel compacted — older entries archived beside it ({Path.GetFileName(channel.FilePath)})");
        }
    }

    /// <summary>A session respawning repeatedly without coming alive is INVISIBLE from the phone — escalate it.</summary>
    async Task Send_CrashLoopAlerts_Async(CancellationToken cancellationToken)
    {
        foreach (var alert in _watchdog.Take_PendingCrashLoopAlerts())
        {
            if (_telegramClient == null || Is_TopicSilenced(alert.OrchId))
                continue;

            try
            {
                var session = _store.Get_Session_OrNull(alert.OrchId);
                await _telegramClient.Send_Message_Async(session?.TelegramTopicId, alert.AlertText, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(alert.OrchId, $"Crash-loop alert send failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The failure the watchdog CANNOT see: every session is alive, but the orchestration has gone
    /// silent — typically a turn that ended without re-arming its watcher, which freezes the whole
    /// duplex loop. Detected as "no channel traffic for a long while AND nobody is mid-turn".
    /// </summary>
    async Task Send_StallAlerts_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            var quietFor = DateTime.UtcNow - Get_LastChannelActivityUtc(session);

            if (quietFor.TotalMinutes < STALL_ALERT_MINUTES)
            {
                // Traffic resumed — the next stall gets its own alert.
                _stallAlertedOrchIds.Remove(session.OrchId);
                continue;
            }

            // Someone is actually working (transcript growing): a long thinking turn, not a stall.
            if (Is_AnySessionMidTurn(session))
                continue;

            if (!_stallAlertedOrchIds.Add(session.OrchId))
                continue;

            if (Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
                continue;

            var alertText = $"⚠️ {session.DisplayName ?? session.OrchId}: quiet for {SessionDuration_Formatter.Describe(quietFor)} and no session is working — the supervisor may have ended its turn without re-arming its watcher. Text it to wake it up.";

            try
            {
                await _telegramClient.Send_Message_Async(session.TelegramTopicId, alertText, cancellationToken);
                _log.Log_Warning(session.OrchId, alertText);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(session.OrchId, $"Stall alert send failed: {ex.Message}");
            }
        }
    }

    DateTime Get_LastChannelActivityUtc(IOrchestrationSession session)
    {
        var latest = session.CreatedUtc;

        List<string> channelFiles = [_paths.Get_OwnerChannelFile(session.OrchId)];

        foreach (var member in session.Members)
            channelFiles.Add(_paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId));

        foreach (var channelFile in channelFiles)
        {
            if (!File.Exists(channelFile))
                continue;

            var lastWrite = File.GetLastWriteTimeUtc(channelFile);

            if (lastWrite > latest)
                latest = lastWrite;
        }

        return latest;
    }

    /// <summary>
    /// A session mid-turn is writing its transcript RIGHT NOW (the status line hands us the exact
    /// path). Falls back to the probe file's own mtime when a transcript path is unavailable.
    /// </summary>
    bool Is_AnySessionMidTurn(IOrchestrationSession session)
    {
        var orchFolder = _paths.Get_OrchestrationFolder(session.OrchId);

        List<string> usageFiles =
        [
            Path.Combine(orchFolder, UsageTotals_Reader.SESSION_USAGE_FILE),
            Path.Combine(orchFolder, UsageTotals_Reader.COMMUNICATOR_USAGE_FILE),
        ];

        foreach (var member in session.Members)
            usageFiles.Add(Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE));

        foreach (var usageFile in usageFiles)
        {
            if (Is_SessionMidTurn(usageFile))
                return true;
        }

        return false;
    }

    /// <summary>Shared with the UI's chips, so "working right now" means one thing everywhere.</summary>
    static bool Is_SessionMidTurn(string usageFilePath)
    {
        return SessionActivity_Probe.Is_MidTurn(usageFilePath);
    }

    /// <summary>
    /// The backstop for a missed hand-off: an implementer whose channel ends with SOMEONE ELSE'S
    /// entry (a brief it never answered), quiet for minutes, and not mid-turn. That is exactly the
    /// state a watcher armed AFTER the brief landed produces — it can never fire on its own.
    /// The app appends a FROM app entry, which changes the channel and therefore trips the
    /// (content-fingerprint) watcher: the orchestration heals itself instead of stalling.
    /// </summary>
    async Task Nudge_IdleImplementers_Async(CancellationToken cancellationToken)
    {
        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            Nudge_IdleSupervisor(session);

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId);

                if (!File.Exists(channelFile))
                    continue;

                var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));
                var memberKey = $"{session.OrchId}/{member.MemberId}";

                if (entries.Count == 0)
                {
                    _nudgedMemberUtc.Remove(memberKey);
                    continue;
                }

                var spokeLast = ChannelAuthor_Kinds.Is_Member(entries[^1].Author);

                // A session CANNOT give itself the next turn — its monitor fires only when someone
                // ELSE writes. So a member that spoke last and then went quiet is waiting for a
                // message that is never coming, and the nudge IS that message.
                //
                // The two states that are LEGITIMATE dormancy are excluded: a filed report is
                // waiting for the supervisor's verdict, and BLOCKED ON OWNER is waiting for the
                // owner. Both have someone who owes them a reply; nudging them would be noise.
                // Everything else the member said last — an open writing window, a "proceeding
                // with X" — means it stopped mid-task with nobody about to speak to it.
                var memberState = MemberState_Resolver.Resolve(entries);

                var waitingOnSomeoneElse =
                    memberState == MemberStates.AwaitingSupervisorReview || memberState == MemberStates.BlockedOnOwner;

                // A member that has NEVER been briefed is not dormant mid-work — it is waiting for
                // work, which is the correct state for a freshly spawned imp-1 or rev-1. Without
                // this, every orchestration's pre-spawned members were nudged for saying "online"
                // and then respawned, losing their context on a loop.
                var everBriefed = false;

                foreach (var channelEntry in entries)
                {
                    if (channelEntry.Author == ChannelAuthors.Supervisor)
                    {
                        everBriefed = true;
                        break;
                    }
                }

                var dormantMidWork = spokeLast && everBriefed && !waitingOnSomeoneElse;

                if (spokeLast && !dormantMidWork)
                {
                    _nudgedMemberUtc.Remove(memberKey);
                    continue;
                }

                var quietFor = DateTime.UtcNow - File.GetLastWriteTimeUtc(channelFile);
                var alreadyNudged = _nudgedMemberUtc.TryGetValue(memberKey, out var nudgedUtc);

                if (!alreadyNudged && quietFor.TotalMinutes < IMPLEMENTER_NUDGE_MINUTES)
                    continue;

                // Transcript growing = genuinely working (a long build, a big read). NOT orphaned:
                // this is the false positive the whole detector has to avoid.
                if (Is_SessionMidTurn(Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE)))
                    continue;

                if (!alreadyNudged)
                {
                    await Nudge_Implementer_Async(session, member.MemberId, channelFile, entries[^1], quietFor, dormantMidWork, cancellationToken);
                    _nudgedMemberUtc[memberKey] = DateTime.UtcNow;
                    continue;
                }

                if ((DateTime.UtcNow - nudgedUtc).TotalMinutes < ORPHAN_CONFIRM_MINUTES)
                    continue;

                // ESCALATION, and the probe is the TRANSCRIPT, not the channel. The nudge changed
                // the channel, so a live monitor fired and the session took a turn — but the
                // protocol forbids acknowledgment-only entries, so a live, obedient session with
                // nothing to say answers with SILENCE. Treating that silence as death respawned
                // healthy sessions and threw away their context, repeatedly.
                var memberUsageFile = Path.Combine(
                    _paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE);

                var lastActivityUtc = SessionActivity_Probe.Get_LastActivityUtc_OrNull(memberUsageFile);

                if (lastActivityUtc != null && lastActivityUtc > nudgedUtc)
                {
                    // It woke after the nudge: alive, and its monitor works. Nothing is wrong.
                    _nudgedMemberUtc.Remove(memberKey);
                    continue;
                }

                _nudgedMemberUtc.Remove(memberKey);
                await Recover_OrphanedImplementer_Async(session, member.MemberId, cancellationToken);
            }
        }
    }

    /// <summary>
    /// A third way a session goes silent while everything is healthy: a header written in a shape
    /// the parser does not recognise. The entry then exists on disk but NOT to the app — never
    /// mirrored to the owner, never counted, its index still free. The writer has no way to notice;
    /// only the app can see the discrepancy, so the app says so, in the channel, once per offence.
    /// </summary>
    async Task Check_ChannelShapes_Async(CancellationToken cancellationToken)
    {
        foreach (var channel in ChannelDiscovery.Find_ChannelFiles(_paths))
        {
            var malformed = ChannelShape_Validator.Find_MalformedHeaders(UsageTotals_Reader.Read_Text_Safe(channel.FilePath));

            if (malformed.Count == 0)
                continue;

            // FIRST sight of this file — every malformed header in it is HISTORY. Record it
            // silently. This warning means "the entry you just wrote was invisible", and it is
            // actionable only then: an entry from days ago cannot be re-appended usefully, and
            // re-announcing it at every startup trains the owner to ignore the one that matters.
            var isFirstSight = _channelsShapeBaselined.Add(channel.FilePath);

            List<(int LineNumber, string Line)> unreported = [];

            foreach (var entry in malformed)
            {
                var isNew = _reportedMalformedHeaders.Add($"{channel.FilePath}|{entry.Line}");

                if (isNew && !isFirstSight)
                    unreported.Add(entry);
            }

            if (unreported.Count == 0)
                continue;

            ChannelAppender.Append_AppEntry(
                channel.FilePath,
                $"{unreported.Count} entr{(unreported.Count == 1 ? "y is" : "ies are")} INVISIBLE — malformed header",
                ChannelShape_Validator.Build_ReportBody(unreported),
                DateTime.Now);

            _log.Log_Warning(channel.OrchId, $"{Path.GetFileName(channel.FilePath)}: {unreported.Count} malformed entry header(s) — those entries were never mirrored");
            Raise_OrchestrationActivity(channel.OrchId);

            // On the OWNER channel the loss is the owner's: the content never reached their phone.
            if (channel.IsOwnerChannel)
                await Alert_MalformedOwnerEntries_Async(channel.OrchId, unreported.Count, cancellationToken);
        }
    }

    async Task Alert_MalformedOwnerEntries_Async(string orchId, int count, CancellationToken cancellationToken)
    {
        var session = _store.Get_Session_OrNull(orchId);

        if (_telegramClient == null || session == null || Resolve_EffectiveMode(orchId) != TelegramDeliveryModes.Normal)
            return;

        try
        {
            await _telegramClient.Send_Message_Async(
                session.TelegramTopicId,
                $"⚠️ {count} message{(count == 1 ? "" : "s")} in this orchestration never reached you — the session wrote a malformed channel header, so the app could not see {(count == 1 ? "it" : "them")}. It has been told to re-post.",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Malformed-header alert send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The same backstop for the SUPERVISOR, which nothing else covered — and it is the session
    /// whose dormancy costs most, because every member's report waits behind it. It is nudged when
    /// a member's channel ends with that member's entry (a report nobody has answered), the wait is
    /// past the threshold, and the supervisor is not mid-turn.
    ///
    /// The nudge goes on owner-channel.md — the supervisor's own channel — so it reaches the
    /// supervisor without landing in a member's channel, where it would read as traffic addressed
    /// to that member.
    /// </summary>
    void Nudge_IdleSupervisor(IOrchestrationSession session)
    {
        var supervisorUsageFile = Path.Combine(_paths.Get_OrchestrationFolder(session.OrchId), UsageTotals_Reader.SESSION_USAGE_FILE);

        if (Is_SessionMidTurn(supervisorUsageFile))
            return;

        List<string> waitingMembers = [];

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId);

            if (!File.Exists(channelFile))
                continue;

            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));

            if (entries.Count == 0 || !ChannelAuthor_Kinds.Is_Member(entries[^1].Author))
                continue;

            if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(channelFile)).TotalMinutes < IMPLEMENTER_NUDGE_MINUTES)
                continue;

            waitingMembers.Add(member.MemberId);
        }

        if (waitingMembers.Count == 0)
        {
            _nudgedMemberUtc.Remove(session.OrchId);
            return;
        }

        // Once per quiet spell, not once per tick.
        if (_nudgedMemberUtc.ContainsKey(session.OrchId))
            return;

        _nudgedMemberUtc[session.OrchId] = DateTime.UtcNow;

        ChannelAppender.Append_AppEntry(
            _paths.Get_OwnerChannelFile(session.OrchId),
            $"unread reports waiting on you — {string.Join(", ", waitingMembers)}",
            $"{string.Join(", ", waitingMembers)} filed entries you have not answered, and nothing has moved since. Read each of those channels from your last entry down and give a verdict. If your monitor is no longer running, arm a fresh one.",
            DateTime.Now);

        _log.Log_Warning(session.OrchId, $"Supervisor had unanswered reports from {string.Join(", ", waitingMembers)} — nudged");
        Raise_OrchestrationActivity(session.OrchId);
    }

    async Task Nudge_Implementer_Async(
        IOrchestrationSession session,
        string memberId,
        string channelFile,
        Channels.ChannelEntry.IChannelEntry lastEntry,
        TimeSpan quietFor,
        bool dormantMidWork,
        CancellationToken cancellationToken)
    {
        var subject = dormantMidWork
            ? "you stopped mid-task — nothing was going to wake you"
            : "unread traffic — you have not answered";

        var body = dormantMidWork
            ? $"Your own entry [{lastEntry.Index}] is the last thing on this channel and nothing has moved for {SessionDuration_Formatter.Describe(quietFor)}. Your monitor only fires when someone ELSE writes, so a turn ended mid-task can never continue on its own — this entry is the app waking you. Resume the work you announced. If you are in fact waiting on somebody, say so explicitly (file your report, or write BLOCKED ON OWNER with the question) instead of going quiet — silence is indistinguishable from a dead session."
            : $"Entry [{lastEntry.Index}] FROM {lastEntry.Author.ToString().ToLowerInvariant()} has been waiting {SessionDuration_Formatter.Describe(quietFor)} with no reply from you. Read this channel from your last entry down and act on it. If your monitor is no longer running, arm a fresh one.";

        ChannelAppender.Append_AppEntry(channelFile, subject, body, DateTime.Now);

        var reason = dormantMidWork ? "went dormant mid-task" : "had unread traffic";
        _log.Log_Warning(session.OrchId, $"{memberId} {reason} for {SessionDuration_Formatter.Describe(quietFor)} — nudged");
        Raise_OrchestrationActivity(session.OrchId);

        // The owner is NOT told. This is routine self-healing that already worked — the nudge is
        // written, the member wakes, the work continues — and a "⚠️" for it reads like a fault
        // report the owner must act on. Owner: "I don't want to receive, in telegram, stuff that is
        // not an actual problem; those messages look like problems."
        //
        // It stays in the log, where diagnosing this belongs. If a nudge does NOT work, the
        // orphan-recovery path speaks up — and that one IS a real problem, because context is lost.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Last resort for a session that is ALIVE but has no way back: it ignored a channel change
    /// while idle, so nothing is listening for it. Respawning is the only recovery — its files and
    /// its channel survive, and the role command's boot re-reads the channel. In-conversation
    /// context is lost, which is why this only runs after the nudge probe has failed.
    /// </summary>
    async Task Recover_OrphanedImplementer_Async(IOrchestrationSession session, string memberId, CancellationToken cancellationToken)
    {
        _log.Log_Error(session.OrchId, $"{memberId} is ORPHANED (idle, ignored a channel change) — respawning it", null);

        try
        {
            SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_ImplementerPidFile(session.OrchId, memberId));
            _launcher.Respawn_Implementer(session.OrchId, memberId);

            ChannelAppender.Append_AppEntry(
                _paths.Get_ImplementerChannelFile(session.OrchId, memberId),
                "session was orphaned and has been respawned",
                "Your previous session went idle with nothing listening for new traffic, so the app restarted you. Your files and this channel are intact — read it from the top of the unanswered traffic and continue. Arm your watcher with the baseline captured BEFORE you read.",
                DateTime.Now);

            Raise_OrchestrationActivity(session.OrchId);
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, $"Orphan recovery for '{memberId}' failed", ex);
            return;
        }

        if (_telegramClient == null || Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
            return;

        try
        {
            await _telegramClient.Send_Message_Async(
                session.TelegramTopicId,
                $"⚠️ {memberId} was ORPHANED (alive but nothing listening — it ignored the nudge). Respawned it; its work on disk is untouched, but its in-session context is gone.",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(session.OrchId, $"Orphan-recovery alert send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The ledger's missing feedback loop. A supervisor verdict with no PLAN.md update is now
    /// FLAGGED — visible to the owner, on the card, and to the turn-end hook that blocks the
    /// supervisor. The ledger was the only artifact in the protocol whose omission produced no
    /// signal whatsoever, which is precisely why it was the one that kept being skipped.
    /// Shape is checked at the same time: a line covering "tasks 3-9" can never show progress,
    /// however faithfully it is maintained.
    /// </summary>
    async Task Check_LedgerHealth_Async(CancellationToken cancellationToken)
    {
        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            _lastSupervisorVerdictUtc.TryGetValue(session.OrchId, out var lastVerdictUtc);

            var isBehind = LedgerHealth_Tracker.Is_LedgerBehind(_paths, session.OrchId, lastVerdictUtc == default ? null : lastVerdictUtc);
            LedgerHealth_Tracker.Sync_Flag(_paths, session.OrchId, isBehind);

            if (!isBehind)
            {
                _ledgerBehindReportedOrchIds.Remove(session.OrchId);
            }
            else if (_ledgerBehindReportedOrchIds.Add(session.OrchId))
            {
                ChannelAppender.Append_AppEntry(
                    _paths.Get_OwnerChannelFile(session.OrchId),
                    "PLAN.md is behind your verdicts",
                    "You accepted implementer work without updating the task ledger, so the owner's progress bar is now wrong. Update PLAN.md before your next turn ends — the turn-end hook will block until you do.",
                    DateTime.Now);

                _log.Log_Warning(session.OrchId, "Ledger is behind the supervisor's verdicts — flagged for the turn-end hook");
                Raise_OrchestrationActivity(session.OrchId);
            }

            Report_LedgerShape(session);
        }
    }

    /// <summary>
    /// A ledger-shape complaint goes to the SUPERVISOR's channel and the log, never to Telegram:
    /// splitting a lumped task line is the supervisor's job and the owner can do nothing with the
    /// warning, so texting it was pure noise on the phone (owner directive).
    /// </summary>
    void Report_LedgerShape(IOrchestrationSession session)
    {
        var planFile = _paths.Get_PlanFile(session.OrchId);

        if (!File.Exists(planFile))
            return;

        var complaints = PlanShape_Validator.Find_UnrepresentableLines(UsageTotals_Reader.Read_Text_Safe(planFile));
        var fingerprint = string.Join("\n", complaints);

        // Re-report only when the offending set CHANGES, so a warning cannot become background noise.
        if (_reportedLedgerShapeByOrchId.TryGetValue(session.OrchId, out var reported) && reported == fingerprint)
            return;

        _reportedLedgerShapeByOrchId[session.OrchId] = fingerprint;

        if (complaints.Count == 0)
            return;

        ChannelAppender.Append_AppEntry(
            _paths.Get_OwnerChannelFile(session.OrchId),
            "PLAN.md has lines that cannot show progress",
            $"{string.Join("\n", complaints)}\n\nUntil these are split, work on them renders as zero movement on the owner's bar no matter how often you update the ledger.",
            DateTime.Now);

        _log.Log_Warning(session.OrchId, $"PLAN.md shape problems: {complaints.Count}");
        Raise_OrchestrationActivity(session.OrchId);
    }

    /// <summary>Runaway guard: a per-orchestration token ceiling the owner sets in config.json.</summary>
    async Task Send_BudgetAlerts_Async(CancellationToken cancellationToken)
    {
        var budgetTokens = _configProvider.Get_Current().OrchestrationTokenBudget;

        if (_telegramClient == null || budgetTokens == null || budgetTokens.Value <= 0)
            return;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            var (_, tokens) = UsageTotals_Reader.Build_OrchestrationTotals(_paths, session);

            if (tokens < budgetTokens.Value)
                continue;

            if (!_budgetAlertedOrchIds.Add(session.OrchId))
                continue;

            if (Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
                continue;

            var alertText = $"⚠️ {session.DisplayName ?? session.OrchId}: {UsageTotals_Reader.Format_Tokens(tokens)} used — past the {UsageTotals_Reader.Format_Tokens(budgetTokens.Value)} budget you set.";

            try
            {
                await _telegramClient.Send_Message_Async(session.TelegramTopicId, alertText, cancellationToken);
                _log.Log_Warning(session.OrchId, alertText);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(session.OrchId, $"Budget alert send failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Scans the .usage.json files the status line probe drops beside every session and texts the
    /// General topic when a usage limit crosses 90/95/97/98/99/100% (deduplicated per limit window).
    /// If this Claude Code version's statusline payload carries no limit data, this idles silently.
    /// </summary>
    async Task Check_UsageLimits_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null || _telegramMuted)
            return;

        if ((DateTime.UtcNow - _lastLimitCheckUtc).TotalSeconds < LIMIT_CHECK_INTERVAL_SECONDS)
            return;

        _lastLimitCheckUtc = DateTime.UtcNow;

        try
        {
            Dictionary<string, double> maxPercents = [];

            foreach (var usageFile in Directory.EnumerateFiles(_paths.Root, "*.usage.json", SearchOption.AllDirectories))
            {
                var percents = Limits.LimitData_Parser.Extract_LimitPercents(File.ReadAllText(usageFile));

                foreach (var pair in percents)
                {
                    if (!maxPercents.TryGetValue(pair.Key, out var existing) || pair.Value > existing)
                        maxPercents[pair.Key] = pair.Value;
                }
            }

            if (maxPercents.Count == 0)
                return;

            var state = Load_LimitAlertState();

            foreach (var pair in maxPercents)
            {
                state.TryGetValue(pair.Key, out var lastAlerted);

                if (Limits.LimitAlert_Tracker.Should_ResetWindow(pair.Value, lastAlerted))
                {
                    state[pair.Key] = 0;
                    lastAlerted = 0;
                }

                var newlyCrossed = Limits.LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull(pair.Value, lastAlerted);

                if (newlyCrossed == null)
                    continue;

                state[pair.Key] = newlyCrossed.Value;

                var alertText = $"⚠️ LIMIT: {Limits.LimitData_Parser.Build_ShortLabel(pair.Key)} {pair.Value:F0}%";
                _log.Log_Warning(GLOBAL_ORCH_ID, $"{alertText} (key '{pair.Key}')");
                await _telegramClient.Send_Message_Async(null, alertText, cancellationToken);
            }

            Save_LimitAlertState(state);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(GLOBAL_ORCH_ID, "Usage limit check failed", ex);
        }
    }

    Dictionary<string, double> Load_LimitAlertState()
    {
        Dictionary<string, double> state = [];

        if (!File.Exists(_paths.LimitAlertStateFile))
            return state;

        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(_paths.LimitAlertStateFile)) is System.Text.Json.Nodes.JsonObject root)
            {
                foreach (var pair in root)
                {
                    if (pair.Value != null)
                        state[pair.Key] = pair.Value.GetValue<double>();
                }
            }
        }
        catch
        {
            // Corrupt state file → re-alert once; harmless.
        }

        return state;
    }

    void Save_LimitAlertState(Dictionary<string, double> state)
    {
        var root = new System.Text.Json.Nodes.JsonObject();

        foreach (var pair in state)
            root[pair.Key] = pair.Value;

        File.WriteAllText(_paths.LimitAlertStateFile, root.ToJsonString());
    }

    IReadOnlyList<IDiscoveredChannel> Find_ActiveChannels()
    {
        List<IDiscoveredChannel> activeChannels = [];

        foreach (var channel in ChannelDiscovery.Find_ChannelFiles(_paths))
        {
            if (channel.OrchId == ChannelDiscovery.GENERAL_ORCH_ID)
            {
                activeChannels.Add(channel);
                continue;
            }

            var session = _store.Get_Session_OrNull(channel.OrchId);
            if (session == null || session.ClosedUtc != null)
                continue;

            if (!channel.IsOwnerChannel && Is_MemberClosed(session, channel.SpokeName))
                continue;

            // DEFERRED topics are not polled at all, so their offsets FREEZE and everything they
            // produced replays the moment the mode goes back to Normal. (Silenced topics ARE
            // polled — their traffic is dropped, deliberately never replayed.)
            if (Resolve_EffectiveMode(channel.OrchId) == TelegramDeliveryModes.Deferred)
                continue;

            // WAIT holds BOTH directions. It means "hold on, I am still writing" — so the
            // supervisor must stop adding to the screen too, not just stop receiving. Freezing the
            // offset here is the same mechanism DND uses, so everything it produced replays in
            // order on GO; nothing is lost, it just stops landing while the owner composes.
            if (channel.IsOwnerChannel && _ownerDeliveryBuffer.Is_Holding(channel.FilePath))
                continue;

            // NOTE: a pending question does NOT freeze this channel. Queueing the supervisor's
            // output would hide the real problem rather than fix it — the supervisor would still be
            // working, briefing implementers and moving the state while the owner's answer was
            // pending, so the answer would land against a world that had already changed. The
            // supervisor is STOPPED instead, by the awaiting-answer hook (see Raise_AwaitingAnswerFlag).

            activeChannels.Add(channel);
        }

        return activeChannels;
    }

    static bool Is_MemberClosed(IOrchestrationSession session, string memberId)
    {
        foreach (var member in session.Members)
        {
            if (member.MemberId == memberId)
                return member.ClosedUtc != null;
        }

        return false;
    }

    async Task Mirror_Append_Async(ICompletedChannelAppend append, CancellationToken cancellationToken)
    {
        foreach (var entry in append.Entries)
        {
            _log.Log_Info(append.Channel.OrchId, $"[{append.Channel.SpokeName}] entry #{entry.Index} FROM {entry.Author}: {entry.Subject}");

            // A supervisor entry in a SPOKE is a brief or a verdict — either way the ledger owes
            // an update from this moment, and the flag below is what makes skipping it visible.
            if (!append.Channel.IsOwnerChannel && entry.Author == ChannelAuthors.Supervisor)
                _lastSupervisorVerdictUtc[append.Channel.OrchId] = DateTime.UtcNow;
        }

        if (_telegramClient == null)
            return;

        var mirrorableEntries = Select_MirrorableEntries(append);

        if (mirrorableEntries.Count == 0)
            return;

        // TOPIC SILENCE ("I'm at the PC, talking to this supervisor in its terminal"): drop this
        // orchestration's outbound traffic entirely. Unlike DND, nothing is queued for later —
        // the owner is already reading it live in the terminal, and offsets keep advancing.
        if (Is_TopicSilenced(append.Channel.OrchId))
            return;

        var threadId = await Resolve_ThreadId_OrNull_Async(append.Channel, cancellationToken);

        foreach (var entry in mirrorableEntries)
        {
            // WHAT REACHES THE PHONE, owner's rule: "I answer the sup a question, and then the sup
            // doesn't disturb me anymore unless it has another question. A brief every 30 minutes
            // is fine, but not the waterfall." So a supervisor entry is pushed only when it asks
            // something, answers something they asked, or reports being blocked. Progress narration
            // stays in the channel and in the app — it is not lost, it is just not a notification.
            if (append.Channel.IsOwnerChannel && ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author))
            {
                // NOT _pendingOwnerReplies: that dictionary is cleared by the reply-resolver EARLIER
                // in the same tick, the moment it counts the supervisor's new entry. By the time the
                // answer reached this line the flag was already false, so every answer to the owner
                // was silently suppressed — they asked, the supervisor replied, and they never saw
                // it. This flag is owned solely by this path and cannot race.
                var ownerIsWaiting = false;

                lock (_ownerStateLock)
                {
                    ownerIsWaiting = _ownerAwaitingAnswer.Contains(append.Channel.OrchId);
                }

                if (!OwnerPush_Policy.Should_Push(entry.RawText, ownerIsWaiting))
                {
                    // Remembered, not discarded. If the whole orchestration then falls silent, this
                    // was the last thing said and it gets released — see Break_SilentDeadlock_Async.
                    lock (_ownerStateLock)
                    {
                        _lastSuppressedEntry[append.Channel.OrchId] = new SuppressedEntry
                        {
                            Text = MirrorText_Formatter.Format(append.Channel, entry),
                            SuppressedUtc = DateTime.UtcNow,
                        };
                    }

                    continue;
                }

                lock (_ownerStateLock)
                {
                    _lastSuppressedEntry.Remove(append.Channel.OrchId);

                    // The owner has now had their answer; later entries are narration again.
                    _ownerAwaitingAnswer.Remove(append.Channel.OrchId);
                }
            }

            var text = MirrorText_Formatter.Format(append.Channel, entry);

            // Special lines in the entry become REAL Telegram artifacts, never raw text:
            // IMAGE: <path> lines upload as photos; OPTION: <label> lines render as inline
            // decision buttons the owner can tap instead of typing.
            var photoPaths = Extract_MarkerLines(ref text, "IMAGE");
            var optionLabels = Extract_MarkerLines(ref text, "OPTION");
            var questionLines = Extract_MarkerLines(ref text, "QUESTION");

            // Built from the ENGLISH text, before the Italian layer rewrites it: an explicit
            // QUESTION: line and a derived one then get translated the same way, together.
            var questionPrompt = optionLabels.Count > 0 ? QuestionPrompt_Builder.Build(questionLines, text) : null;

            // Italian layer (live config): the owner reads Italian on the phone; sessions and
            // channels stay English. The speaker prefix ("🟢 Com: ") is split off DETERMINISTICALLY
            // and reattached — a live translation once mangled it into garbage. Presence lines
            // (implementer spokes' "online") are canned app strings and stay English entirely.
            if (_configProvider.Get_Current().TelegramItalianLayer && append.Channel.IsOwnerChannel)
            {
                // Fenced blocks (ASCII mockups, snippets) are lifted out first: translating a
                // drawing corrupts the very thing being shown.
                var (withoutBlocks, blocks) = MonospaceBlocks_Formatter.Extract_Blocks(text);
                var (speakerPrefix, content) = Split_SpeakerPrefix(withoutBlocks);

                text = MonospaceBlocks_Formatter.Restore_Blocks(
                    speakerPrefix + await _translator.Translate_ToItalian_Async(content, cancellationToken), blocks);
            }

            var chunks = TelegramMessage_Chunker.Chunk(text);

            try
            {
                foreach (var chunk in chunks)
                    Remember_TopicMessage(threadId, await Send_MirrorChunk_Async(threadId, chunk, cancellationToken));

                // Counts toward away detection: a supervisor message that reached the phone and is
                // so far unanswered. Only the supervisor's own voice counts — app notices and
                // presence lines are not something the owner is expected to reply to.
                if (append.Channel.IsOwnerChannel && ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author) && chunks.Count > 0)
                {
                    Nudge_IfTooVerbose(append.Channel.OrchId, text);

                    if (Note_SupervisorSpokeToOwner_AndJustWentQuiet(append.Channel.OrchId))
                        await Enter_QuietMode_Async(append.Channel.OrchId, cancellationToken);
                }

                // The buttons NEVER ride on the body. Agents write long, thorough messages, and
                // options hanging off the bottom of one arrive on a phone as a wall of text with
                // taps underneath and no visible question. They get their own short message.
                if (questionPrompt != null)
                    await Send_QuestionWithButtons_Async(threadId, questionPrompt, optionLabels, append.Channel, cancellationToken);

                foreach (var photoPath in photoPaths)
                    await Send_EntryPhoto_BestEffort_Async(threadId, photoPath, append.Channel.OrchId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Error(append.Channel.OrchId, $"Telegram mirror send failed for entry #{entry.Index}", ex);
                return;
            }
        }
    }

    /// <summary>
    /// Sends one mirrored chunk, as HTML when it carries a fenced block so an ASCII mockup keeps a
    /// MONOSPACED font and its alignment. Telegram rejects malformed HTML (a chunk boundary can
    /// split a fence), so a rejection falls back to plain text — a mangled mockup beats a lost
    /// message.
    /// </summary>
    async Task<long?> Send_MirrorChunk_Async(long? threadId, string chunk, CancellationToken cancellationToken)
    {
        var client = _telegramClient
            ?? throw new Exception("Send_MirrorChunk_Async called without a Telegram client");

        if (MonospaceBlocks_Formatter.Has_Blocks(chunk))
        {
            try
            {
                return await client.Send_HtmlMessage_Async(threadId, MonospaceBlocks_Formatter.Build_Html(chunk), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(GLOBAL_ORCH_ID, $"HTML mockup send rejected, falling back to plain text: {ex.Message}");
            }
        }

        return await client.Send_Message_Async(threadId, chunk, cancellationToken);
    }

    /// <summary>
    /// "🔴 Sup: body" → ("🔴 Sup: ", "body") — the prefix must NEVER pass through the translator.
    /// The bound covers the longest prefix ("🟡 Gen-Sup: " is already 12 UTF-16 units, its emoji
    /// being a surrogate pair) with room to spare; the LAZY quantifier still stops at the first
    /// ": ", which is always the formatter's own prefix.
    /// </summary>
    static (string Prefix, string Content) Split_SpeakerPrefix(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"^(.{1,18}?: )(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);

        if (!match.Success)
            return (string.Empty, text);

        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    /// <summary>
    /// A topic's OWN mode wins over the app-wide setting — "silence just this one while I work in
    /// its terminal" must survive someone flipping the global DND, and vice versa. Only when the
    /// topic is Normal does the app-wide setting apply.
    /// </summary>
    TelegramDeliveryModes Resolve_EffectiveMode(string orchId)
    {
        if (orchId != ChannelDiscovery.GENERAL_ORCH_ID)
        {
            var topicMode = _store.Get_Session_OrNull(orchId)?.TelegramMode ?? TelegramDeliveryModes.Normal;

            if (topicMode != TelegramDeliveryModes.Normal)
                return topicMode;
        }

        if (_telegramMuted)
            return TelegramDeliveryModes.Deferred;

        if (_silenceAllTopics)
            return TelegramDeliveryModes.Silenced;

        return TelegramDeliveryModes.Normal;
    }

    /// <summary>Silence is TOTAL for a topic: its mirrored entries AND its alerts.</summary>
    bool Is_TopicSilenced(string orchId)
    {
        return Resolve_EffectiveMode(orchId) == TelegramDeliveryModes.Silenced;
    }

    /// <summary>Pulls '<marker>: value' lines out of the text (which shrinks accordingly) and returns the values.</summary>
    static IReadOnlyList<string> Extract_MarkerLines(ref string text, string marker)
    {
        List<string> values = [];
        List<string> keptLines = [];

        foreach (var line in text.Split('\n'))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line.TrimEnd('\r'), $@"^{marker}:\s*(.+)$");

            if (match.Success)
                values.Add(match.Groups[1].Value.Trim());
            else
                keptLines.Add(line);
        }

        if (values.Count > 0)
            text = string.Join('\n', keptLines).Trim('\n');

        return values;
    }

    /// <summary>
    /// The decision message: a SHORT question with the options under it, sent on its own rather
    /// than bolted to the end of the body. The owner sees what is being asked without re-reading
    /// the message above it, and after tapping this same message records their answer.
    /// </summary>
    async Task Send_QuestionWithButtons_Async(
        long? threadId,
        string questionPrompt,
        IReadOnlyList<string> optionLabels,
        Channels.DiscoveredChannel.IDiscoveredChannel channel,
        CancellationToken cancellationToken)
    {
        var client = _telegramClient
            ?? throw new Exception("Send_QuestionWithButtons_Async called without a Telegram client");

        var prompt = questionPrompt;

        if (_configProvider.Get_Current().TelegramItalianLayer && channel.IsOwnerChannel)
            prompt = await _translator.Translate_ToItalian_Async(prompt, cancellationToken);

        var messageId = await client.Send_MessageWithButtons_Async(
            threadId, prompt, Register_Buttons(threadId, optionLabels, prompt), cancellationToken);

        Remember_TopicMessage(threadId, messageId);

        // Remembered UNANSWERED, so away mode can mark it parked. This is the exact thing that made
        // the owner's plane landing unusable: a screen of questions with no way to tell which were
        // still live.
        if (messageId != null)
        {
            lock (_ownerStateLock)
            {
                _openQuestions[messageId.Value] = new OpenQuestion
                {
                    OrchId = channel.OrchId,
                    Text = prompt,
                    AskedUtc = DateTime.UtcNow,
                };
            }

            // It asked; now it stops. The hook refuses every tool until the owner answers.
            if (channel.IsOwnerChannel)
            {
                Raise_AwaitingAnswerFlag(channel.OrchId);
            }
        }
    }

    IReadOnlyList<(string Data, string Label)> Register_Buttons(long? threadId, IReadOnlyList<string> optionLabels, string questionText)
    {
        List<(string Data, string Label)> buttons = [];

        lock (_buttonLock)
        {
            // One GROUP per button message: the first tap invalidates all its siblings.
            _buttonGroupSequence++;

            foreach (var label in optionLabels)
            {
                _buttonSequence++;
                var data = $"opt-{_buttonSequence}";

                _buttonOptions[data] = (threadId, label, _buttonGroupSequence, questionText);
                _buttonOrder.Enqueue(data);
                buttons.Add((data, label));
            }

            // Every question also offers a way to ASK BACK. The button's label is short; the text
            // the supervisor receives is the full instruction, which is why the two differ here.
            // Tapping it consumes the group like any other choice, so the supervisor answers and
            // then re-asks with fresh buttons.
            _buttonSequence++;
            var detailData = $"opt-{_buttonSequence}";

            _buttonOptions[detailData] = (threadId, OwnerPush_Policy.MORE_DETAIL_REQUEST, _buttonGroupSequence, questionText);
            _buttonOrder.Enqueue(detailData);
            buttons.Add((detailData, OwnerPush_Policy.MORE_DETAIL_LABEL));

            while (_buttonOrder.Count > BUTTON_REGISTRY_CAP)
                _buttonOptions.Remove(_buttonOrder.Dequeue());
        }

        return buttons;
    }

    async Task Send_EntryPhoto_BestEffort_Async(long? threadId, string photoPath, string orchId, CancellationToken cancellationToken)
    {
        try
        {
            if (_telegramClient == null)
                return;

            if (!File.Exists(photoPath))
            {
                _log.Log_Warning(orchId, $"Entry photo not sent — file missing: {photoPath}");
                return;
            }

            await _telegramClient.Send_Photo_Async(threadId, photoPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Entry photo send failed for '{photoPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Owner-channel entries only, and of any QUEUED periodic STATUS entries (a DND catch-up
    /// batch) only the NEWEST survives — hours of muted half-hour reports must not flood the
    /// owner on unmute.
    /// </summary>
    static IReadOnlyList<Channels.ChannelEntry.IChannelEntry> Select_MirrorableEntries(ICompletedChannelAppend append)
    {
        List<Channels.ChannelEntry.IChannelEntry> mirrorable = [];

        foreach (var entry in append.Entries)
        {
            if (MirrorText_Formatter.Should_Mirror(append.Channel, entry))
                mirrorable.Add(entry);
        }

        var lastStatusIndex = -1;

        for (var i = mirrorable.Count - 1; i >= 0; i--)
        {
            if (MirrorText_Formatter.Is_StatusEntry(mirrorable[i]))
            {
                lastStatusIndex = i;
                break;
            }
        }

        if (lastStatusIndex < 0)
            return mirrorable;

        List<Channels.ChannelEntry.IChannelEntry> deduplicated = [];

        for (var i = 0; i < mirrorable.Count; i++)
        {
            var isSupersededStatus = i != lastStatusIndex && MirrorText_Formatter.Is_StatusEntry(mirrorable[i]);

            if (!isSupersededStatus)
                deduplicated.Add(mirrorable[i]);
        }

        return deduplicated;
    }

    /// <summary>General channel → the General topic (null thread id). Orchestrations get a topic on first mirror.</summary>
    async Task<long?> Resolve_ThreadId_OrNull_Async(IDiscoveredChannel channel, CancellationToken cancellationToken)
    {
        if (channel.OrchId == ChannelDiscovery.GENERAL_ORCH_ID)
            return null;

        if (_telegramClient == null)
            return null;

        var session = _store.Get_Session_OrNull(channel.OrchId);
        if (session == null)
            return null;

        if (session.TelegramTopicId != null)
            return session.TelegramTopicId;

        try
        {
            var topicId = await _telegramClient.Create_ForumTopic_Async(channel.OrchId, cancellationToken);
            _store.Set_TelegramTopicId(channel.OrchId, topicId);
            _log.Log_Info(channel.OrchId, $"Telegram topic created (thread id {topicId})");
            Remove_TopicCreationPin_FireAndForget(channel.OrchId, topicId);
            return topicId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(channel.OrchId, "Telegram topic creation failed — mirroring to the General topic for now", ex);
            return null;
        }
    }

    void Process_PendingRequests()
    {
        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        foreach (var malformedRequest in pending.MalformedRequests)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Malformed request file deleted — {malformedRequest.Reason}: {malformedRequest.FilePath}");

            // Tell the AGENT why, in its own channel — a silently deleted request file used to
            // look to the supervisor like an action that simply never happened.
            if (malformedRequest.OrchId != null && _store.Get_Session_OrNull(malformedRequest.OrchId) != null)
                Append_OrchestrationAppEntry(malformedRequest.OrchId, "request REJECTED", $"Your request file was rejected: {malformedRequest.Reason}. Fix it and drop a new file (same action string).");

            Delete_RequestFile(malformedRequest.FilePath);
        }

        Process_StartRequests(pending);
        Process_AddImplementerRequests(pending);
        Process_CloseImplementerRequests(pending);
        Process_CloseOrchestrationRequests(pending);
        Process_SetTelegramMutedRequests(pending);
        Process_SetOrchestrationNameRequests(pending);
        Process_SetModelRequests(pending);
    }

    /// <summary>
    /// Per-orchestration model override (owner: "use fable for this") — stored on session.json,
    /// then the affected sessions are killed and respawned on the new model; they resume from
    /// their channels. Never touches the global defaults.
    /// </summary>
    void Process_SetModelRequests(IPendingRequests pending)
    {
        foreach (var request in pending.SetModelRequests)
        {
            try
            {
                if (request.Role == GeneralSupervision.SetModelRequest.SetModelRequest_Factory.SUPERVISOR_ROLE)
                {
                    _store.Set_SupervisorModelOverride(request.OrchId, request.Model);
                    SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_SupervisorPidFile(request.OrchId));
                    _launcher.Respawn_Supervisor(request.OrchId);
                }
                else
                {
                    _store.Set_ImplementerModelOverride(request.OrchId, request.Model);
                    var session = _store.Get_Session(request.OrchId);

                    foreach (var member in session.Members)
                    {
                        if (member.ClosedUtc != null)
                            continue;

                        SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_ImplementerPidFile(request.OrchId, member.MemberId));
                        _launcher.Respawn_Implementer(request.OrchId, member.MemberId);
                    }
                }

                Append_OrchestrationAppEntry(
                    request.OrchId,
                    $"model set: {request.Role} → {request.Model} — {request.Reason}",
                    "Affected sessions respawned on the new model; they resume from their channels.");
            }
            catch (Exception ex)
            {
                _log.Log_Error(request.OrchId, $"set-model {request.Role} → '{request.Model}' failed", ex);
                Append_OrchestrationAppEntry(request.OrchId, $"set-model FAILED: {request.Role} → {request.Model}", $"Error: {ex.Message}");
            }
            finally
            {
                Delete_RequestFile(request.SourceFilePath);
            }
        }
    }

    void Process_SetOrchestrationNameRequests(IPendingRequests pending)
    {
        foreach (var request in pending.SetOrchestrationNameRequests)
        {
            try
            {
                var session = _store.Get_Session(request.OrchId);
                _store.Set_DisplayName(request.OrchId, request.Name);

                if (_telegramClient != null && session.TelegramTopicId != null)
                    Rename_TelegramTopic_FireAndForget(request.OrchId, session.TelegramTopicId.Value, request.Name);

                Rename_SessionWindows_BestEffort(session, request.Name);

                _log.Log_Info(request.OrchId, $"Named '{request.Name}'");
                Raise_OrchestrationActivity(request.OrchId);
            }
            catch (Exception ex)
            {
                _log.Log_Error(request.OrchId, $"set-orchestration-name '{request.Name}' failed", ex);
            }
            finally
            {
                Delete_RequestFile(request.SourceFilePath);
            }
        }
    }

    void Remove_TopicCreationPin_FireAndForget(string orchId, long topicId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var client = _telegramClient
                    ?? throw new Exception($"Telegram client vanished while unpinning topic {topicId} of '{orchId}'");

                await client.Remove_TopicCreationPin_Async(topicId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.Log_Warning(orchId, $"Topic pin removal failed for topic {topicId}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Renames the session terminal windows to carry the goal name. The original fragment stays
    /// as a prefix ("SUP · crm-2 · CRM invoice crash") so focusing/closing keep matching.
    /// </summary>
    void Rename_SessionWindows_BestEffort(Sessions.OrchestrationSession.IOrchestrationSession session, string name)
    {
        try
        {
            var supervisorFragment = $"SUP · {session.OrchId}";
            TerminalWindow_Focuser.Try_Rename_ByTitleFragment(supervisorFragment, $"{supervisorFragment} · {name}");

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var memberFragment = $"{member.MemberId.ToUpperInvariant()} · {session.OrchId}";
                TerminalWindow_Focuser.Try_Rename_ByTitleFragment(memberFragment, $"{memberFragment} · {name}");
            }
        }
        catch (Exception ex)
        {
            _log.Log_Warning(session.OrchId, $"Terminal window rename failed: {ex.Message}");
        }
    }

    void Rename_TelegramTopic_FireAndForget(string orchId, long topicId, string newName)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var client = _telegramClient
                    ?? throw new Exception($"Telegram client vanished while renaming topic {topicId} of '{orchId}'");

                await client.Edit_ForumTopic_Async(topicId, newName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.Log_Error(orchId, $"Telegram editForumTopic({topicId} → '{newName}') failed", ex);
            }
        });
    }

    void Process_SetTelegramMutedRequests(IPendingRequests pending)
    {
        foreach (var request in pending.SetTelegramMutedRequests)
        {
            Set_TelegramMuted(request.Muted);
            Delete_RequestFile(request.SourceFilePath);
        }
    }

    void Process_StartRequests(IPendingRequests pending)
    {
        foreach (var request in pending.StartRequests)
        {
            try
            {
                // Config is read LIVE: the general supervisor seeds/extends config.json at
                // runtime, and a startup snapshot here already caused "Known repos: ." failures.
                var repos = _configProvider.Get_Current().Repos;
                var repo = RepoQuery_Resolver.Resolve_OrNull(request.RepoQuery, repos);

                if (repo == null)
                {
                    var known = string.Join(", ", repos.Select(r => r.Name));
                    Append_GeneralAppEntry(
                        $"start-orchestration FAILED: '{request.RepoQuery}'",
                        $"Could not resolve repo '{request.RepoQuery}' to exactly one configured repo. Known repos: {known}. Ask the owner which one is meant, then drop a new request.");
                    continue;
                }

                var session = _launcher.Start_Orchestration(repo.Name, repo.Path);

                Append_GeneralAppEntry(
                    $"orchestration '{session.OrchId}' started",
                    $"Orchestration '{session.OrchId}' started on repo '{repo.Name}' ({repo.Path}). Supervisor and implementer imp-1 spawned; its Telegram topic appears on its first channel entry.");
            }
            catch (Exception ex)
            {
                _log.Log_Error(GLOBAL_ORCH_ID, $"start-orchestration for '{request.RepoQuery}' failed", ex);
                Append_GeneralAppEntry(
                    $"start-orchestration FAILED for repo '{request.RepoQuery}'",
                    $"Error: {ex.Message}");
            }
            finally
            {
                Delete_RequestFile(request.SourceFilePath);
            }
        }
    }

    void Process_AddImplementerRequests(IPendingRequests pending)
    {
        foreach (var request in pending.AddImplementerRequests)
        {
            try
            {
                var session = _launcher.Add_Member(request.OrchId, request.Kind);
                var newMember = session.Members[session.Members.Count - 1];
                var kindWord = request.Kind.ToString().ToLowerInvariant();

                var briefingHint = request.Kind == MemberKinds.Reviewer
                    ? $"New reviewer '{newMember.MemberId}' spawned for orchestration '{request.OrchId}' — READ-ONLY (it cannot edit or commit). Its channel is {newMember.MemberId}/channel.md — brief it there, and the brief MUST name a review DEPTH (quick | standard | deep | max) and exactly what to review."
                    : $"New implementer '{newMember.MemberId}' spawned for orchestration '{request.OrchId}'. Its channel is {newMember.MemberId}/channel.md — brief it there.";

                // The REASON rides in the subject because App entries mirror subject-only — the
                // owner must never see a session appear (and burn tokens) without knowing why.
                Append_OrchestrationAppEntry(
                    request.OrchId,
                    $"{kindWord} '{newMember.MemberId}' added — {request.Reason}",
                    briefingHint);
            }
            catch (Exception ex)
            {
                _log.Log_Error(request.OrchId, $"add-{request.Kind.ToString().ToLowerInvariant()} failed", ex);
                Append_OrchestrationAppEntry(request.OrchId, $"add-{request.Kind.ToString().ToLowerInvariant()} FAILED", $"Error: {ex.Message}");
            }
            finally
            {
                Delete_RequestFile(request.SourceFilePath);
            }
        }
    }

    void Process_CloseImplementerRequests(IPendingRequests pending)
    {
        foreach (var request in pending.CloseImplementerRequests)
        {
            try
            {
                _store.Close_Member(request.OrchId, request.MemberId);
                SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_ImplementerPidFile(request.OrchId, request.MemberId));

                Append_OrchestrationAppEntry(
                    request.OrchId,
                    $"implementer '{request.MemberId}' closed — {request.Reason}",
                    $"Implementer '{request.MemberId}' is retired: its terminal was closed and its channel stays on disk as audit trail.");
            }
            catch (Exception ex)
            {
                _log.Log_Error(request.OrchId, $"close-implementer '{request.MemberId}' failed", ex);
                Append_OrchestrationAppEntry(request.OrchId, $"close-implementer FAILED: '{request.MemberId}'", $"Error: {ex.Message}");
            }
            finally
            {
                Delete_RequestFile(request.SourceFilePath);
            }
        }
    }

    void Process_CloseOrchestrationRequests(IPendingRequests pending)
    {
        foreach (var request in pending.CloseOrchestrationRequests)
        {
            try
            {
                var session = _store.Get_Session(request.OrchId);
                _store.Close_Orchestration(request.OrchId);
                SessionTerminator.Kill_OrchestrationSessions(_paths, request.OrchId);

                if (_telegramClient != null && session.TelegramTopicId != null)
                    Delete_TelegramTopic_FireAndForget(request.OrchId, session.TelegramTopicId.Value);

                Append_GeneralAppEntry(
                    $"orchestration '{request.OrchId}' closed — {request.Reason}",
                    "Sessions ended; folder kept as audit trail; Telegram topic deleted.");
            }
            catch (Exception ex)
            {
                _log.Log_Error(request.OrchId, "close-orchestration failed", ex);
                Append_GeneralAppEntry($"close-orchestration FAILED: '{request.OrchId}'", $"Error: {ex.Message}");
            }
            finally
            {
                Delete_RequestFile(request.SourceFilePath);
            }
        }
    }

    static void Delete_RequestFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // A locked request file will be retried (and re-executed) next tick; acceptable for
            // idempotent-ish actions, and deletion failures on a local disk are extremely rare.
        }
    }

    void Delete_TelegramTopic_FireAndForget(string orchId, long topicId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var client = _telegramClient
                    ?? throw new Exception($"Telegram client vanished while deleting topic {topicId} of '{orchId}'");

                await client.Delete_ForumTopic_Async(topicId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.Log_Error(orchId, $"Telegram deleteForumTopic({topicId}) failed", ex);
            }
        });
    }

    void Append_GeneralAppEntry(string subject, string body)
    {
        ChannelAppender.Append_AppEntry(_paths.GeneralChannelFile, subject, body, DateTime.Now);
        Raise_OrchestrationActivity(ChannelDiscovery.GENERAL_ORCH_ID);
    }

    void Append_OrchestrationAppEntry(string orchId, string subject, string body)
    {
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(ownerChannel))
        {
            _log.Log_Warning(orchId, $"No owner-channel.md for '{orchId}' — app entry '{subject}' logged only");
            return;
        }

        ChannelAppender.Append_AppEntry(ownerChannel, subject, body, DateTime.Now);
        Raise_OrchestrationActivity(orchId);
    }

    /// <summary>
    /// Same rule as the mirror loop: only a cancelled TOKEN may end this loop. This is the one
    /// whose silent death took the owner's phone offline on 2026-08-11 — the long poll hung, the
    /// 90 s HttpClient timeout raised a TaskCanceledException, and the bare catch returned without
    /// logging a thing, so the log stayed quiet instead of filling with backoff lines.
    /// </summary>
    async Task Run_InboundLoop_Async(CancellationToken cancellationToken)
    {
        var client = _telegramClient
            ?? throw new Exception("Inbound loop started without a Telegram client");

        var startupConfig = _configProvider.Get_Current();

        var supergroupChatId = startupConfig.TelegramSupergroupChatId
            ?? throw new Exception("Inbound loop started without a supergroup chat id");

        var ownerUserId = startupConfig.TelegramOwnerUserId
            ?? throw new Exception("Inbound loop started without an owner user id");

        var backoffMilliseconds = INBOUND_ERROR_BACKOFF_START_MILLISECONDS;

        await Register_BotCommands_BestEffort_Async(client, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var json = await client.Get_UpdatesJson_Async(_lastUpdateId + 1, INBOUND_LONG_POLL_SECONDS, cancellationToken);
                var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, supergroupChatId, ownerUserId);

                // Bot commands: /dnd acts directly (and must NOT auto-unmute); /summary and
                // /pending become canned English requests routed to the general supervisor.
                List<ITelegramOwnerMessage> routableMessages = [];
                List<(string Command, long? ThreadId)> modeCommands = [];

                foreach (var message in batch.OwnerMessages)
                {
                    // Tracked so /clear can remove the owner's own messages too.
                    Remember_TopicMessage(message.MessageThreadId, message.MessageId);

                    // ANY message means the owner is here — including a bot command, which never
                    // reaches Route_OwnerMessage_Async and so would otherwise leave away mode on
                    // while they are visibly typing /resume at it.
                    if (Note_OwnerSpoke_AndWasAway())
                        await Exit_AwayMode_Async(cancellationToken);

                    var command = Get_BotCommand_OrNull(message.Text);

                    // Telegram's own command menu only allows [a-z0-9_], so the menu entries are
                    // mute_all/dnd_all while a hand-typed mute-all works just as well.
                    if (command == "dnd" || command == "mute" || command == "unmute"
                        || command == "dnd-all" || command == "mute-all" || command == "dnd_all" || command == "mute_all")
                    {
                        // Deferred until after the loop: toggling must not race the ✓ acks, and a
                        // /dnd must not be auto-unmuted by the very message that requested it.
                        modeCommands.Add((command, message.MessageThreadId));
                    }
                    else if (command == "summary")
                    {
                        routableMessages.Add(Build_GeneralCommandMessage(message, "Make a summary of what is going on across all orchestrations."));
                    }
                    else if (command == "pending")
                    {
                        routableMessages.Add(Build_GeneralCommandMessage(message, "List every pending question that awaits me, and which topic to answer each in."));
                    }
                    else if (command == "progress")
                    {
                        // Answered by the APP straight from PLAN.md — instant, and it works even
                        // while the supervisor is mid-turn (which is exactly when it gets asked).
                        await Send_ProgressReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "tokens")
                    {
                        await Send_TokensReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "cost")
                    {
                        await Send_CostReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "italian")
                    {
                        await Toggle_ItalianLayer_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "limits")
                    {
                        await Send_LimitsReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "diff")
                    {
                        await Send_GitReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "clear")
                    {
                        await Clear_Topic_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "status")
                    {
                        await Send_MemberStatusReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "resume")
                    {
                        await Resume_AllSessions_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command != null && command.StartsWith("imp", StringComparison.Ordinal))
                    {
                        await Send_ImplementerPeek_Async(client, message.MessageThreadId, command, message.Text, cancellationToken);
                    }
                    else
                    {
                        routableMessages.Add(message);
                    }
                }

                // The owner texting or tapping ANYTHING (except a mode command) lifts app-wide DND
                // — before routing, so the ✓ acks go out.
                if ((routableMessages.Count > 0 || batch.CallbackTaps.Count > 0) && _telegramMuted)
                    Set_TelegramMuted(false);

                foreach (var message in routableMessages)
                {
                    if (await Apply_HoldControlWord_Async(client, message, cancellationToken))
                        continue;

                    await Route_OwnerMessage_Async(message, cancellationToken);

                    // While HELD the phone stays quiet: no per-message tick. The single WAIT
                    // acknowledgement already said "I have you" and is updated with the count
                    // instead; the ✓/✓✓ pair comes after GO.
                    if (Is_TargetHeld(message))
                        await Update_HoldReceipt_Async(client, message, cancellationToken);
                    else
                        await Send_ReceivedAck_Async(client, message.MessageThreadId, cancellationToken);
                }

                foreach (var tap in batch.CallbackTaps)
                    await Handle_CallbackTap_Async(client, tap, cancellationToken);

                foreach (var modeCommand in modeCommands)
                    await Apply_ModeCommand_Async(client, modeCommand.Command, modeCommand.ThreadId, cancellationToken);

                // Our own topic renames make Telegram post "changed the topic name" notices —
                // delete them so a mode toggle leaves the conversation clean.
                foreach (var serviceMessageId in batch.TopicServiceMessageIds)
                    await Delete_ServiceMessage_BestEffort_Async(client, serviceMessageId, cancellationToken);

                if (batch.MaxUpdateId != null)
                {
                    _lastUpdateId = batch.MaxUpdateId.Value;
                    Persist_BridgeState();
                }

                backoffMilliseconds = INBOUND_ERROR_BACKOFF_START_MILLISECONDS;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Log_Error(GLOBAL_ORCH_ID, "Telegram getUpdates failed — backing off", ex);

                try
                {
                    await Task.Delay(backoffMilliseconds, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                backoffMilliseconds = Math.Min(backoffMilliseconds * 2, INBOUND_ERROR_BACKOFF_MAX_MILLISECONDS);
            }
        }
    }

    /// <summary>
    /// Registers the chat's ☰ command menu — two taps beat typing the check-in ritual.
    ///
    /// The OCE filter below is load-bearing, unlike the ~30 other rethrow sites: this is the only
    /// helper awaited OUTSIDE the inbound loop's while, so its exception does not land in a guarded
    /// catch — it escapes Run_InboundLoop_Async before the loop ever starts. An unfiltered rethrow
    /// therefore let a wedged endpoint stop the poller from EXISTING (90 s HttpClient timeout →
    /// TaskCanceledException → rethrown → loop never entered), which is the same outage this
    /// change exists to prevent, arriving by a different door. A menu that failed to register is
    /// worth a warning, never the owner's phone line.
    /// </summary>
    async Task Register_BotCommands_BestEffort_Async(ITelegramApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.Set_MyCommands_Async(
                [
                    ("status", "What every session of this orchestration is doing"),
                    ("progress", "Task ledger of this orchestration (all of them in General)"),
                    ("cost", "What this has cost, per session, and the burn rate"),
                    ("tokens", "Token and usage totals"),
                    ("limits", "5-hour and weekly usage limits"),
                    ("diff", "What the repo and worktrees ACTUALLY contain"),
                    ("imp", "Latest traffic of an implementer (/imp 2)"),
                    ("summary", "What is going on across all orchestrations"),
                    ("pending", "Open questions awaiting me"),
                    ("resume", "Wake EVERY session — use when the usage limit resets"),
                    ("clear", "Wipe THIS topic's messages (the sessions keep running)"),
                    ("mute", "Toggle 🔕 THIS topic — drop its messages (I'm in its terminal)"),
                    ("dnd", "Toggle 🌙 THIS topic — hold its messages for later"),
                    ("mute_all", "Toggle 🔕 everywhere"),
                    ("dnd_all", "Toggle 🌙 everywhere"),
                    ("italian", "Toggle 🇮🇹 — translate what I send you"),
                ],
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"setMyCommands failed: {ex.Message}");
        }
    }

    /// <summary>"/summary", "/summary@BotName" → "summary"; non-commands → null.</summary>
    static string? Get_BotCommand_OrNull(string text)
    {
        var trimmed = text.Trim();

        if (!trimmed.StartsWith('/'))
            return null;

        var command = trimmed[1..];
        var atIndex = command.IndexOf('@');

        if (atIndex >= 0)
            command = command[..atIndex];

        return command.ToLowerInvariant();
    }

    /// <summary>A command becomes a canned English request for the GENERAL supervisor (thread null = general channel).</summary>
    static ITelegramOwnerMessage Build_GeneralCommandMessage(ITelegramOwnerMessage original, string cannedText)
    {
        return TelegramOwnerMessage_Factory.Create(
            original.UpdateId, original.MessageId, original.ChatId, original.FromUserId, null, cannedText, null, null);
    }

    /// <summary>
    /// /progress — the PLAN.md task ledger, straight from disk. In a topic: that orchestration's
    /// full ledger; in General: one line per open orchestration. Deliberately NOT routed to the
    /// supervisor: this is asked precisely when the supervisor is mid-turn and cannot answer.
    /// </summary>
    async Task Send_ProgressReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_ProgressReportText(messageThreadId);

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_ProgressReportText(long? messageThreadId)
    {
        if (messageThreadId != null)
        {
            var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

            if (session == null)
                return "no orchestration is bound to this topic";

            return Build_OrchestrationLedgerText(session.OrchId, session.DisplayName ?? session.OrchId);
        }

        List<string> blocks = [];

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            blocks.Add(Build_OrchestrationCountsLine(session.OrchId, session.DisplayName ?? session.OrchId));
        }

        if (blocks.Count == 0)
            return "no open orchestrations";

        return string.Join('\n', blocks);
    }

    /// <summary>Full ledger for one orchestration — the raw '- [x]' lines are the point of the command.</summary>
    string Build_OrchestrationLedgerText(string orchId, string displayName)
    {
        const int MAX_LEDGER_LINES = 40;

        var planText = Read_FileText_Safe(_paths.Get_PlanFile(orchId));
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(planText);

        if (progress == null)
            return $"{displayName}: no task ledger yet — the supervisor writes PLAN.md once you approve a direction";

        List<string> taskLines = [];

        foreach (var rawLine in planText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();

            if (line.StartsWith("- [", StringComparison.Ordinal))
                taskLines.Add(line);
        }

        var shown = taskLines.Count <= MAX_LEDGER_LINES ? taskLines : [.. taskLines.Take(MAX_LEDGER_LINES)];
        var truncationNote = taskLines.Count > MAX_LEDGER_LINES ? $"\n… and {taskLines.Count - MAX_LEDGER_LINES} more" : "";

        return $"{Build_OrchestrationCountsLine(orchId, displayName)}\n\n{string.Join('\n', shown)}{truncationNote}";
    }

    string Build_OrchestrationCountsLine(string orchId, string displayName)
    {
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(orchId)));

        if (progress == null)
            return $"{displayName}: no task ledger yet";

        return $"{displayName}: {Planning.PlanProgress_Formatter.Describe_Counts(progress)}";
    }

    static string Read_FileText_Safe(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return string.Empty;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// /tokens — LIFETIME token and usage figures (respawns folded in). In a topic: that
    /// orchestration, broken down per session; in General: every orchestration plus a grand total.
    /// The figures are API-EQUIVALENT: subscription plans are not billed per token.
    /// </summary>
    async Task Send_TokensReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_TokensReportText(messageThreadId);

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_TokensReportText(long? messageThreadId)
    {
        if (messageThreadId != null)
        {
            var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

            if (session == null)
                return "no orchestration is bound to this topic";

            var (cost, tokens) = UsageTotals_Reader.Build_OrchestrationTotals(_paths, session);

            if (tokens <= 0 && cost <= 0)
                return $"{session.DisplayName ?? session.OrchId}: no usage recorded yet";

            List<string> lines = [$"{session.DisplayName ?? session.OrchId}: {UsageTotals_Reader.Format_Tokens(tokens)} · ≈${cost:F2} equiv (not billed)"];

            foreach (var line in Build_PerSessionUsageLines(session))
                lines.Add(line);

            return string.Join('\n', lines);
        }

        List<string> blocks = [];
        var grandCost = 0.0;
        long grandTokens = 0;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            var (cost, tokens) = UsageTotals_Reader.Build_OrchestrationTotals(_paths, session);
            grandCost += cost;
            grandTokens += tokens;

            blocks.Add($"{session.DisplayName ?? session.OrchId}: {UsageTotals_Reader.Format_Tokens(tokens)} · ≈${cost:F2}");
        }

        if (blocks.Count == 0)
            return "no open orchestrations";

        blocks.Add($"TOTAL: {UsageTotals_Reader.Format_Tokens(grandTokens)} · ≈${grandCost:F2} equiv (not billed)");
        return string.Join('\n', blocks);
    }

    IReadOnlyList<string> Build_PerSessionUsageLines(IOrchestrationSession session)
    {
        var orchFolder = _paths.Get_OrchestrationFolder(session.OrchId);

        List<(string Label, string File)> sources =
        [
            ("supervisor", Path.Combine(orchFolder, UsageTotals_Reader.SESSION_USAGE_FILE)),
            ("communicator", Path.Combine(orchFolder, UsageTotals_Reader.COMMUNICATOR_USAGE_FILE)),
        ];

        foreach (var member in session.Members)
            sources.Add((member.MemberId, Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE)));

        List<string> lines = [];

        foreach (var source in sources)
        {
            var tokens = UsageTotals_Reader.Read_Tokens_OrNull(source.File);

            if (tokens == null)
                continue;

            lines.Add($"- {source.Label}: {UsageTotals_Reader.Format_Tokens(tokens.Value)} (current session)");
        }

        return lines;
    }

    /// <summary>
    /// /cost — the MONEY view of the same lifetime figures /tokens reports: what an orchestration
    /// has cost, WHICH SESSION spent it, and how fast it is burning. Costs are API-EQUIVALENT —
    /// a subscription is not billed per token — so this answers "was this worth it", not "what do
    /// I owe".
    /// </summary>
    async Task Send_CostReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_CostReportText(messageThreadId);

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_CostReportText(long? messageThreadId)
    {
        if (messageThreadId != null)
        {
            var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

            if (session == null)
                return "no orchestration is bound to this topic";

            return Build_OneOrchestrationCostText(session);
        }

        List<string> lines = [];
        var grandCost = 0.0;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            var (cost, _) = UsageTotals_Reader.Build_OrchestrationTotals(_paths, session);
            grandCost += cost;

            var burnRate = Describe_BurnRate_OrEmpty(session, cost);
            var ratePart = burnRate.Length > 0 ? $" · {burnRate}" : "";

            lines.Add($"{session.DisplayName ?? session.OrchId}: ≈${cost:F2}{ratePart}");
        }

        if (lines.Count == 0)
            return "no open orchestrations";

        // No combined burn rate: each rate is an average over that orchestration's own lifetime,
        // and summing them would only be true if they had all run at the same time.
        lines.Add($"TOTAL: ≈${grandCost:F2} equiv (not billed)");
        return string.Join('\n', lines);
    }

    string Build_OneOrchestrationCostText(IOrchestrationSession session)
    {
        var perSource = UsageTotals_Reader.Build_PerSourceTotals(_paths, session);
        var costTotal = 0.0;

        foreach (var source in perSource)
            costTotal += source.Cost;

        if (costTotal <= 0)
            return $"{session.DisplayName ?? session.OrchId}: no cost recorded yet";

        List<string> lines =
        [
            $"{session.DisplayName ?? session.OrchId}: ≈${costTotal:F2} lifetime (equiv, not billed)",
        ];

        foreach (var source in perSource)
        {
            if (source.Cost <= 0)
                continue;

            var share = costTotal > 0 ? source.Cost / costTotal * 100.0 : 0.0;
            lines.Add($"- {source.Label}: ≈${source.Cost:F2} ({share:F0}%) · {UsageTotals_Reader.Format_Tokens(source.Tokens)}");
        }

        var burnRate = Describe_BurnRate_OrEmpty(session, costTotal);

        if (burnRate.Length > 0)
            lines.Add($"{burnRate} over {SessionDuration_Formatter.Describe(DateTime.UtcNow - session.CreatedUtc)}");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// "≈$0.82/h", or nothing at all for an orchestration too young to have a meaningful average —
    /// a rate extrapolated from the first minutes is noise, not information.
    /// </summary>
    static string Describe_BurnRate_OrEmpty(IOrchestrationSession session, double cost)
    {
        var elapsedHours = (DateTime.UtcNow - session.CreatedUtc).TotalHours;

        if (elapsedHours < MINIMUM_BURN_RATE_HOURS || cost <= 0)
            return "";

        return $"≈${cost / elapsedHours:F2}/h";
    }

    /// <summary>
    /// /italian — flips the translation layer from the phone. The confirmation is written in the
    /// language the layer is being switched TO, so the toggle demonstrates itself.
    /// </summary>
    async Task Toggle_ItalianLayer_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var enabled = !_configProvider.Get_Current().TelegramItalianLayer;
        Set_ItalianLayer(enabled);

        var text = enabled
            ? "🇮🇹 Italian layer ON — everything I send you is translated from here on."
            : "🇬🇧 Italian layer OFF — messages now reach you exactly as the sessions wrote them.";

        if (enabled)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        await Send_DirectReply_BestEffort_Async(client, messageThreadId, text, cancellationToken);
    }

    /// <summary>
    /// /limits — the 5-hour and weekly usage windows, per model where the status line reports
    /// them. Data comes from the status-line probe files; every session writes what its Claude
    /// Code version exposes, and the WORST (highest) percent per window is what matters.
    /// </summary>
    async Task Send_LimitsReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_LimitsReportText();

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_LimitsReportText()
    {
        var windows = RateLimits_Reader.Read_WorstAcrossSessions(UsageTotals_Reader.Find_AllUsageFiles(_paths));

        if (windows.Count == 0)
            return "no limit data in the status line of this Claude Code version — nothing to report (the automatic limit alerts idle for the same reason)";

        List<string> lines = [];

        foreach (var window in windows)
        {
            var resetPart = window.ResetsAtLocal == null
                ? ""
                : $" · resets in {SessionDuration_Formatter.Describe(window.ResetsAtLocal.Value - DateTime.Now)} ({window.ResetsAtLocal.Value:HH:mm})";

            lines.Add($"{window.Window}: {window.Percent:F0}%{resetPart}");
        }

        // The account's limits are reported per WINDOW, never per model — say so rather than
        // letting a per-model reading be inferred from the models that happened to report.
        lines.Add($"(account-wide, all models — seen from: {string.Join(" | ", windows.Select(w => w.Models).Distinct())})");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// /diff — GROUND TRUTH from git, not agent prose: branch, ahead/behind, dirty files and the
    /// latest commits for the repo and every worktree the orchestration uses.
    /// </summary>
    async Task Send_GitReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_GitReportText(messageThreadId);

        // NOT translated: this is verbatim git output (branch names, commit subjects, paths).
        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_GitReportText(long? messageThreadId)
    {
        if (messageThreadId == null)
            return "send /diff inside an orchestration's topic — it reports that orchestration's repo and worktrees";

        var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null)
            return "no orchestration is bound to this topic";

        List<string> blocks = [];

        foreach (var snapshot in GitSnapshot_Reader.Read_RepoAndWorktrees(session.RepoPath))
        {
            if (!snapshot.IsRepository)
                continue;

            var aheadPart = snapshot.AheadOfUpstream > 0 ? $" · {snapshot.AheadOfUpstream} ahead" : "";
            var behindPart = snapshot.BehindUpstream > 0 ? $" · {snapshot.BehindUpstream} behind" : "";
            var dirtyPart = snapshot.DirtyFileCount > 0 ? $" · {snapshot.DirtyFileCount} uncommitted" : " · clean";

            List<string> lines = [$"{snapshot.ShortPath} [{snapshot.Branch}]{aheadPart}{behindPart}{dirtyPart}"];

            foreach (var commit in snapshot.RecentCommits.Take(5))
                lines.Add($"  {commit}");

            blocks.Add(string.Join('\n', lines));
        }

        if (blocks.Count == 0)
            return $"{session.RepoPath} is not a git repository";

        return string.Join("\n\n", blocks);
    }

    /// <summary>
    /// The four delivery toggles. All of them TOGGLE (one command to remember per scope), and the
    /// -all pair is the app-wide setting while the bare pair is this topic's own override:
    ///   /mute      this topic → Silenced (dropped)      /mute-all  app-wide Silenced
    ///   /dnd       this topic → Deferred (kept, replayed) /dnd-all app-wide Deferred
    /// In the General topic (no orchestration behind it) the bare commands act app-wide too.
    /// </summary>
    async Task Apply_ModeCommand_Async(ITelegramApiClient client, string command, long? messageThreadId, CancellationToken cancellationToken)
    {
        var wantedMode = command.StartsWith("mute", StringComparison.Ordinal)
            ? TelegramDeliveryModes.Silenced
            : TelegramDeliveryModes.Deferred;

        // "/unmute" stays as an explicit way back to Normal for anyone who does not trust a toggle.
        var forceNormal = command == "unmute";
        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);
        var isAppWide = command.EndsWith("-all", StringComparison.Ordinal) || command.EndsWith("_all", StringComparison.Ordinal);

        if (isAppWide || session == null)
        {
            await Apply_AppWideMode_Async(client, messageThreadId, wantedMode, forceNormal, cancellationToken);
            return;
        }

        try
        {
            var newMode = forceNormal || session.TelegramMode == wantedMode
                ? TelegramDeliveryModes.Normal
                : wantedMode;

            _store.Set_TelegramMode(session.OrchId, newMode);
            _log.Log_Info(session.OrchId, $"Topic delivery mode → {newMode}");
            Raise_OrchestrationActivity(session.OrchId);

            Tell_Supervisor_AboutMode(session.OrchId, session.TelegramMode, newMode);

            // Sent BEFORE the new mode takes hold on the next tick, so the confirmation gets through.
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, Describe_Mode(newMode, appWide: false), cancellationToken);
            await Sync_TopicNames_BestEffort_Async(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, $"'{command}' failed", ex);
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, $"could not change the mode: {ex.Message}", cancellationToken);
        }
    }

    async Task Apply_AppWideMode_Async(ITelegramApiClient client, long? messageThreadId, TelegramDeliveryModes wantedMode, bool forceNormal, CancellationToken cancellationToken)
    {
        var alreadyOn = wantedMode == TelegramDeliveryModes.Deferred ? _telegramMuted : _silenceAllTopics;
        var turningOn = !forceNormal && !alreadyOn;

        if (wantedMode == TelegramDeliveryModes.Deferred)
            Set_TelegramMuted(turningOn);
        else
            Set_SilenceAllTopics(turningOn);

        var effective = turningOn ? wantedMode : TelegramDeliveryModes.Normal;

        await Send_DirectReply_BestEffort_Async(client, messageThreadId, Describe_Mode(effective, appWide: true), cancellationToken);
        await Sync_TopicNames_BestEffort_Async(cancellationToken);
    }

    static string Describe_Mode(TelegramDeliveryModes mode, bool appWide)
    {
        var scope = appWide ? "everywhere" : "this topic";

        return mode switch
        {
            TelegramDeliveryModes.Normal => $"🔔 {scope}: messages ON",
            TelegramDeliveryModes.Deferred => $"{TelegramDeliveryMode_Glyphs.DEFERRED} {scope}: Do-Not-Disturb — nothing is lost, it all arrives when you switch back",
            TelegramDeliveryModes.Silenced => $"{TelegramDeliveryMode_Glyphs.SILENCED} {scope}: silenced — messages are DROPPED while this lasts (you're reading them in the terminal)",
            _ => throw new Exception($"Unhandled TelegramDeliveryModes: {mode}"),
        };
    }

    /// <summary>
    /// Keeps each Telegram topic's NAME carrying its mode glyph (🔕 / 🌙), so the owner sees the
    /// state in the topic list without opening anything. Only calls the API when the name actually
    /// changes — the desired name is compared against the last one pushed.
    /// </summary>
    /// <summary>
    /// Telegram rejects an edit that would leave the topic unchanged. That is not an error for us —
    /// the desired state already holds — so it must be treated as a success or the sync retries it
    /// on every tick.
    /// </summary>
    static bool Is_TopicAlreadyNamed(Exception exception)
    {
        return exception.Message.Contains("TOPIC_NOT_MODIFIED", StringComparison.OrdinalIgnoreCase);
    }

    async Task Sync_TopicNames_BestEffort_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null || session.TelegramTopicId == null)
                continue;

            var baseName = TelegramDeliveryMode_Glyphs.Strip_Glyph(session.DisplayName ?? session.OrchId);
            var wantedName = TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                baseName, Resolve_EffectiveMode(session.OrchId), Is_AwayMode(), Is_Quiet(session.OrchId));

            if (_appliedTopicNames.TryGetValue(session.OrchId, out var applied) && applied == wantedName)
                continue;

            try
            {
                await _telegramClient.Edit_ForumTopic_Async(session.TelegramTopicId.Value, wantedName, cancellationToken);
                _appliedTopicNames[session.OrchId] = wantedName;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (Is_TopicAlreadyNamed(ex))
            {
                // TOPIC_NOT_MODIFIED means the name is ALREADY what we want — success, not failure.
                // The cache is what stops this running every tick, and it was only being written on
                // the success path, so this case retried every 2 seconds forever: one orchestration
                // logged 28 identical errors in minutes and would have done so for as long as the
                // app ran. It happens on every restart, because the cache starts empty while
                // Telegram already holds the correct names.
                _appliedTopicNames[session.OrchId] = wantedName;
            }
            catch (Exception ex)
            {
                // A REAL failure still must not spin: remember the attempt so it is retried on the
                // next name change rather than on the next tick.
                _appliedTopicNames[session.OrchId] = wantedName;
                _log.Log_Warning(session.OrchId, $"Topic name sync failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// /status — the per-session state, the same reading the app's chips show. LIVE activity
    /// (transcript growing) outranks the declared channel markers, because a marker only records
    /// what an agent announced, not what it is doing now.
    /// </summary>
    /// <summary>
    /// /resume — wakes EVERY session in every open orchestration. Built for the usage-limit reset:
    /// a session that hit the limit ends its turn without doing the work, and nothing will speak to
    /// it again on its own, so the whole fleet sits idle until someone says go.
    ///
    /// It works by APPENDING to each channel rather than touching the terminals: a channel change
    /// is what every monitor is already watching for, so the wake goes through the same path as
    /// ordinary traffic and needs no window handling, no pids, no respawn.
    /// </summary>
    async Task Resume_AllSessions_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        const string SUBJECT = "GO AHEAD — resume";

        var body = "The owner sent /resume (usage limits reset, or they want you moving again).\n\n"
            + "Pick up exactly where you left off: re-read this channel from your last entry down, and if your last "
            + "turn was cut short by a usage limit, redo that step now. If you were genuinely finished and waiting, "
            + "say so in one line and go back to waiting — do NOT invent new work to look busy.";

        var wokenSessions = 0;
        var wokenOrchestrations = 0;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            wokenOrchestrations++;

            ChannelAppender.Append_AppEntry(_paths.Get_OwnerChannelFile(session.OrchId), SUBJECT, body, DateTime.Now);
            wokenSessions++;

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                ChannelAppender.Append_AppEntry(
                    _paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId), SUBJECT, body, DateTime.Now);

                wokenSessions++;
            }

            Raise_OrchestrationActivity(session.OrchId);
        }

        // The general supervisor too — it has the same problem and its own channel.
        ChannelAppender.Append_AppEntry(_paths.GeneralChannelFile, SUBJECT, body, DateTime.Now);
        wokenSessions++;

        _log.Log_Info(GLOBAL_ORCH_ID, $"/resume — woke {wokenSessions} session(s) across {wokenOrchestrations} orchestration(s)");

        await Send_DirectReply_BestEffort_Async(
            client,
            messageThreadId,
            $"▶ go ahead sent to {wokenSessions} session{(wokenSessions == 1 ? "" : "s")} across {wokenOrchestrations} orchestration{(wokenOrchestrations == 1 ? "" : "s")} (+ general)",
            cancellationToken);
    }

    async Task Send_MemberStatusReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_MemberStatusText(messageThreadId);

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_MemberStatusText(long? messageThreadId)
    {
        if (messageThreadId == null)
            return "send /status inside an orchestration's topic — it reports that orchestration's sessions";

        var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null)
            return "no orchestration is bound to this topic";

        return Build_MemberStatusText_ForSession(session);
    }

    /// <summary>
    /// ONE builder for both /status and the periodic push, so the answer the owner pulls and the
    /// one the app sends can never disagree.
    /// </summary>
    string Build_MemberStatusText_ForSession(IOrchestrationSession session)
    {
        var orchFolder = _paths.Get_OrchestrationFolder(session.OrchId);
        var supervisorUsage = Path.Combine(orchFolder, UsageTotals_Reader.SESSION_USAGE_FILE);

        var supervisorLine = SessionActivity_Probe.Is_MidTurn(supervisorUsage)
            ? $"working now{Describe_Activity_Suffix(supervisorUsage)}"
            : "idle — waiting";

        // The header carries the ledger counts, so "who is doing what" and "how far along are we"
        // arrive in one answer — the owner asked for both without having to send /progress too.
        // Same builder /progress uses, so the two can never quote different figures.
        List<string> lines =
        [
            Build_OrchestrationCountsLine(session.OrchId, session.DisplayName ?? session.OrchId),
            $"- supervisor: {supervisorLine}",
        ];

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
            {
                lines.Add($"- {member.MemberId}: closed");
                continue;
            }

            var memberFolder = _paths.Get_ImplementerFolder(session.OrchId, member.MemberId);
            var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId);
            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));
            var declared = MemberState_Resolver.Resolve(entries);
            var workingNow = SessionActivity_Probe.Is_MidTurn(Path.Combine(memberFolder, UsageTotals_Reader.SESSION_USAGE_FILE));

            var lastWrite = File.Exists(channelFile)
                ? $" · last wrote {SessionDuration_Formatter.Describe(DateTime.UtcNow - File.GetLastWriteTimeUtc(channelFile))} ago"
                : "";

            lines.Add($"- {member.MemberId}: {Describe_DeclaredState(declared, workingNow)}{lastWrite}");
        }

        return string.Join('\n', lines);
    }

    /// <summary>" — editing Foo.cs" when the transcript says so, empty when it cannot be read.</summary>
    static string Describe_Activity_Suffix(string usageFilePath)
    {
        var activity = SupervisorActivity_Describer.Describe_OrNull(usageFilePath);

        return activity == null ? "" : $" — {activity}";
    }

    string Describe_SessionActivity(string usageFilePath, string idleText)
    {
        return SessionActivity_Probe.Is_MidTurn(usageFilePath) ? "working now" : idleText;
    }

    static string Describe_DeclaredState(MemberStates declared, bool workingNow)
    {
        var declaredText = declared switch
        {
            MemberStates.NewNoTraffic => "new — no traffic",
            MemberStates.ImplementerWorking => "briefed — not started yet",
            MemberStates.AwaitingSupervisorReview => "awaiting review",
            MemberStates.WritingWindowOpen => "idle — writing window left open",
            MemberStates.BlockedOnOwner => "BLOCKED ON OWNER",
            _ => throw new Exception($"Unhandled MemberStates: {declared}"),
        };

        return workingNow ? $"working now (channel says: {declaredText})" : declaredText;
    }

    /// <summary>
    /// /clear — empties the TELEGRAM view, never the sessions: no terminal is touched, no channel
    /// file is altered, the work continues untouched. An orchestration topic is deleted and
    /// recreated with the same name, which wipes it completely; the General topic cannot be
    /// deleted, so there the app removes the messages it KNOWS belong to it.
    ///
    /// Telegram message ids are chat-wide, not per topic, so a computed range would delete other
    /// topics' messages — only observed ids are ever touched.
    /// </summary>
    async Task Clear_Topic_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null)
        {
            var deleted = await Delete_KnownMessages_Async(client, messageThreadId, cancellationToken);

            await Send_DirectReply_BestEffort_Async(
                client,
                messageThreadId,
                $"🧹 removed {deleted} message(s) I could account for. Telegram does not let a bot wipe the General topic wholesale — older messages need Telegram's own \"clear history\".",
                cancellationToken);

            return;
        }

        try
        {
            var baseName = TelegramDeliveryMode_Glyphs.Strip_Glyph(session.DisplayName ?? session.OrchId);
            var topicName = TelegramDeliveryMode_Glyphs.Decorate_TopicName(baseName, Resolve_EffectiveMode(session.OrchId));

            // Recreate rather than delete-by-id: it is the only way to leave the topic genuinely
            // empty, and it cannot touch a neighbouring topic by accident.
            await client.Delete_ForumTopic_Async(messageThreadId ?? throw new Exception($"orchestration '{session.OrchId}' has no topic id to clear"), cancellationToken);

            var newTopicId = await client.Create_ForumTopic_Async(topicName, cancellationToken);
            _store.Set_TelegramTopicId(session.OrchId, newTopicId);

            _appliedTopicNames[session.OrchId] = topicName;
            Take_KnownTopicMessageIds(messageThreadId);
            Take_ReceiptMessageId_OrNull(messageThreadId);

            await client.Remove_TopicCreationPin_Async(newTopicId, cancellationToken);

            _log.Log_Info(session.OrchId, $"Telegram topic cleared (recreated as {newTopicId}) — sessions untouched");
            Raise_OrchestrationActivity(session.OrchId);

            await Send_DirectReply_BestEffort_Async(
                client,
                newTopicId,
                "🧹 topic cleared. The sessions kept running — nothing was interrupted and the channel files still hold the full history.",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, "clear-topic failed", ex);
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, $"could not clear the topic: {ex.Message}", cancellationToken);
        }
    }

    async Task<int> Delete_KnownMessages_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var deleted = 0;

        foreach (var messageId in Take_KnownTopicMessageIds(messageThreadId))
        {
            try
            {
                await client.Delete_Message_Async(messageId, cancellationToken);
                deleted++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Already gone, or older than Telegram's deletion window — expected, keep going.
            }
        }

        return deleted;
    }

    /// <summary>/imp 2 — the latest entries of one implementer's spoke, which never reaches Telegram otherwise.</summary>
    async Task Send_ImplementerPeek_Async(ITelegramApiClient client, long? messageThreadId, string command, string rawText, CancellationToken cancellationToken)
    {
        var text = Build_ImplementerPeekText(messageThreadId, command, rawText);

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_ImplementerPeekText(long? messageThreadId, string command, string rawText)
    {
        const int PEEK_ENTRIES = 6;

        if (messageThreadId == null)
            return "send /imp inside an orchestration's topic (e.g. /imp 2)";

        var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null)
            return "no orchestration is bound to this topic";

        // Accepts "/imp 2", "/imp2" and "/imp imp-2".
        var digits = new string([.. $"{command} {rawText}".Where(char.IsAsciiDigit)]);

        if (digits.Length == 0)
            return $"which implementer? e.g. /imp 1 (open: {string.Join(", ", session.Members.Where(m => m.ClosedUtc == null).Select(m => m.MemberId))})";

        var memberId = $"imp-{digits[0]}";
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, memberId);
        var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));

        if (entries.Count == 0)
            return $"{memberId}: no traffic yet";

        List<string> lines = [$"{memberId} — last {Math.Min(PEEK_ENTRIES, entries.Count)} entries"];

        foreach (var entry in entries.TakeLast(PEEK_ENTRIES))
        {
            var body = entry.Body.Replace('\n', ' ').Trim();
            var preview = body.Length <= 180 ? body : $"{body[..180]}…";

            lines.Add($"[{entry.Author.ToString().ToLowerInvariant()}] {entry.Subject}");

            if (preview.Length > 0)
                lines.Add($"   {preview}");
        }

        return string.Join('\n', lines);
    }

    async Task Remove_Buttons_BestEffort_Async(ITelegramApiClient client, long messageId, CancellationToken cancellationToken)
    {
        try
        {
            await client.Remove_MessageButtons_Async(messageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Button keyboard removal failed for message {messageId}: {ex.Message}");
        }
    }

    async Task Handle_CallbackTap_Async(ITelegramApiClient client, ITelegramCallbackTap tap, CancellationToken cancellationToken)
    {
        (long? ThreadId, string OptionText, long GroupId, string QuestionText) registered;
        bool found;

        lock (_buttonLock)
        {
            found = _buttonOptions.TryGetValue(tap.Data, out registered);

            // SINGLE-USE: the first tap consumes the WHOLE option group — a second tap (or a
            // sibling button) resolves to "expired" instead of double-firing a decision.
            if (found)
            {
                List<string> groupKeys = [.. _buttonOptions.Where(pair => pair.Value.GroupId == registered.GroupId).Select(pair => pair.Key)];

                foreach (var key in groupKeys)
                    _buttonOptions.Remove(key);
            }
        }

        try
        {
            // Must always be answered or the button spinner hangs on the phone.
            await client.Answer_CallbackQuery_Async(tap.CallbackQueryId, found ? "✓" : "expired — please type your choice", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"answerCallbackQuery failed: {ex.Message}");
        }

        if (!found)
            return;

        // Rewrite the question message to RECORD the choice ("❓ … / ✅ deep"). Telegram's tap
        // acknowledgement is a transient toast and the keyboard vanishes, so without this the chat
        // keeps no trace of what was picked — the owner scrolls back and cannot tell what they
        // answered. Editing the text also drops the keyboard, so it replaces the strip step.
        if (tap.MessageId != null)
        {
            // Answered — it must never be marked "parked" by a later away-mode sweep.
            lock (_ownerStateLock)
            {
                _openQuestions.Remove(tap.MessageId.Value);
            }

            try
            {
                await client.Edit_MessageText_Async(
                    tap.MessageId.Value,
                    QuestionPrompt_Builder.Build_AnsweredText(registered.QuestionText, registered.OptionText),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(GLOBAL_ORCH_ID, $"Answered-question edit failed: {ex.Message}");

                // The record is nice; a live keyboard on an already-answered question is a BUG,
                // so fall back to at least removing it.
                await Remove_Buttons_BestEffort_Async(client, tap.MessageId.Value, cancellationToken);
            }
        }

        // A tap IS an owner message: the chosen option text goes through the normal pipeline
        // (aggregation, translation, delivery receipts) into the topic the buttons live in.
        var syntheticMessage = TelegramOwnerMessage_Factory.Create(
            tap.UpdateId, tap.MessageId, 0, 0, registered.ThreadId ?? tap.MessageThreadId, registered.OptionText, null, null);

        await Route_OwnerMessage_Async(syntheticMessage, cancellationToken);
    }

    async Task Delete_ServiceMessage_BestEffort_Async(ITelegramApiClient client, long messageId, CancellationToken cancellationToken)
    {
        try
        {
            await client.Delete_Message_Async(messageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Needs can_delete_messages; without it the notice simply stays.
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Could not delete a topic service message: {ex.Message}");
        }
    }

    async Task Send_DirectReply_BestEffort_Async(ITelegramApiClient client, long? messageThreadId, string text, CancellationToken cancellationToken)
    {
        try
        {
            Remember_TopicMessage(messageThreadId, await client.Send_Message_Async(messageThreadId, text, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Direct reply send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Owner texts are BUFFERED, not delivered immediately: several messages sent in a row
    /// aggregate into one entry after a quiet window (Flush_OwnerDeliveries_Async does the
    /// delivery and sends the '✓ → Sup' receipt).
    /// </summary>
    /// <summary>
    /// WAIT / GO — the owner's own hold on delivery. Returns true when the message WAS the control
    /// word, in which case it is consumed: it never reaches the session, because "wait" alone is
    /// not something the supervisor should read as an instruction.
    ///
    /// Only a message that is EXACTLY the word counts (see OwnerControlWords) — "wait for imp-2" is
    /// a real instruction and must pass through untouched.
    /// </summary>
    async Task<bool> Apply_HoldControlWord_Async(
        ITelegramApiClient client, Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message, CancellationToken cancellationToken)
    {
        // Only typed text can be a control word; a voice note or photo is content.
        if (message.VoiceFileId != null || message.PhotoFileId != null)
            return false;

        var targetKey = Resolve_TargetChannelFile_OrNull(message);

        if (targetKey == null)
            return false;

        if (OwnerControlWords.Is_Wait(message.Text))
        {
            _ownerDeliveryBuffer.Hold(targetKey, DateTime.UtcNow);
            _log.Log_Info(Describe_MessageOrch(message), "Owner sent WAIT — delivery held until GO");

            // WAIT also holds what is ALREADY buffered — the common case is realising mid-countdown
            // that you have more to say. Those messages keep their tick (they WERE received) and
            // the tick itself becomes the hold receipt, so the owner sees "✓ ⏸ holding · 1 message"
            // where the bare "✓" was, instead of a stale tick plus a second message.
            var heldAlready = _ownerDeliveryBuffer.Count_Pending(targetKey);
            var existingTickId = Take_ReceiptMessageId_OrNull(message.MessageThreadId);

            try
            {
                long? receiptId;

                if (existingTickId != null)
                {
                    await client.Edit_MessageText_Async(existingTickId.Value, Build_HoldReceiptText(heldAlready), cancellationToken);
                    receiptId = existingTickId;
                }
                else
                {
                    receiptId = await client.Send_Message_Async(message.MessageThreadId, Build_HoldReceiptText(heldAlready), cancellationToken);
                }

                lock (_ownerStateLock)
                {
                    _holdReceipts[targetKey] = new HoldReceipt { MessageId = receiptId, HeldCount = heldAlready };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(Describe_MessageOrch(message), $"WAIT acknowledgement failed: {ex.Message}");
            }

            return true;
        }

        if (OwnerControlWords.Is_Go(message.Text))
        {
            _ownerDeliveryBuffer.Release(targetKey);

            lock (_ownerStateLock)
            {
                _holdReceipts.Remove(targetKey);
            }

            _log.Log_Info(Describe_MessageOrch(message), "Owner sent GO — releasing held messages");

            // The tick the owner did not get per message, now that the thought is complete.
            await Send_ReceivedAck_Async(client, message.MessageThreadId, cancellationToken);

            // Deliver HERE rather than waiting for the next mirror tick. GO means "I am done
            // typing", so every millisecond after it is dead time — and the tick is up to 2 s away.
            await Flush_OwnerDeliveries_Async(cancellationToken);

            return true;
        }

        return false;
    }

    bool Is_TargetHeld(Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message)
    {
        var targetKey = Resolve_TargetChannelFile_OrNull(message);

        return targetKey != null && _ownerDeliveryBuffer.Is_Holding(targetKey);
    }

    /// <summary>
    /// The tick is KEPT when messages are already waiting: they were received, and WAIT does not
    /// un-receive them — it stops them being delivered. "✓ ⏸ holding · 1 message" is the honest
    /// state of a message that was mid-countdown when the owner realised they had more to say.
    /// </summary>
    /// <summary>
    /// True while a question of ours is unanswered — the owner channel stays frozen and everything
    /// the supervisor writes queues behind it.
    ///
    /// Capped in time on purpose: an owner who simply never answers must not starve themselves of
    /// everything else forever. Past the cap the queue flows again, and the quiet/away machinery is
    /// what handles a genuinely absent owner.
    /// </summary>
    /// <summary>
    /// Makes the deadlock structurally impossible rather than heuristically unlikely.
    ///
    /// The push filter can only ever suppress a REAL question by mistake if that question carried
    /// neither a marker nor a question mark. The consequence would be silent and symmetric: the
    /// supervisor waits for an answer, the owner never saw anything to answer, and neither can
    /// observe the other waiting.
    ///
    /// The escape is that a stalled orchestration looks unmistakable from here — the supervisor is
    /// idle AND every member is idle AND nothing has been said for minutes. Work in progress never
    /// looks like that, which is why this can be safe and still almost never fire. When it does, the
    /// last thing the supervisor said is released, whatever it was: if it was a question the
    /// deadlock breaks, and if it was not, the owner has lost nothing but one message about an
    /// orchestration that had gone quiet anyway.
    /// </summary>
    async Task Break_SilentDeadlock_Async(CancellationToken cancellationToken)
    {
        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null || session.TelegramTopicId == null)
                continue;

            SuppressedEntry? suppressed;

            lock (_ownerStateLock)
            {
                if (!_lastSuppressedEntry.TryGetValue(session.OrchId, out suppressed))
                    continue;

                if ((DateTime.UtcNow - suppressed.SuppressedUtc).TotalMinutes < SILENT_DEADLOCK_MINUTES)
                    continue;
            }

            // Anything still running means this is ordinary progress, not a stall.
            if (Is_AnySessionWorking(session))
                continue;

            lock (_ownerStateLock)
            {
                _lastSuppressedEntry.Remove(session.OrchId);
            }

            _log.Log_Warning(session.OrchId, "Everything went idle with an unsent supervisor entry — releasing it in case it was a question");

            await Send_AwayNotice_Async(
                session,
                $"{suppressed.Text}\n\n(nothing has moved for {SILENT_DEADLOCK_MINUTES} min — sending you the last thing it said, in case it needed you)",
                cancellationToken);
        }
    }

    /// <summary>The supervisor or ANY open member mid-turn — i.e. the orchestration is alive.</summary>
    bool Is_AnySessionWorking(IOrchestrationSession session)
    {
        var orchFolder = _paths.Get_OrchestrationFolder(session.OrchId);

        if (Is_SessionMidTurn(Path.Combine(orchFolder, UsageTotals_Reader.SESSION_USAGE_FILE)))
            return true;

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            var memberUsage = Path.Combine(
                _paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE);

            if (Is_SessionMidTurn(memberUsage))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Raised the moment the supervisor asks, so its PreToolUse hook refuses to let it do anything
    /// else. This is the terminal's behaviour: a question ends the turn and NOTHING happens until
    /// the answer arrives. Queueing its output instead would have left it working in the
    /// background, which is precisely what makes an answer arrive against a changed world.
    /// </summary>
    void Raise_AwaitingAnswerFlag(string orchId)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(_paths.Get_OrchestrationFolder(orchId), AWAITING_ANSWER_FLAG_FILE),
                DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Could not raise the awaiting-answer flag: {ex.Message}");
        }
    }

    void Clear_AwaitingAnswerFlag(string orchId)
    {
        try
        {
            var flagFile = Path.Combine(_paths.Get_OrchestrationFolder(orchId), AWAITING_ANSWER_FLAG_FILE);

            if (File.Exists(flagFile))
                File.Delete(flagFile);
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Could not clear the awaiting-answer flag: {ex.Message}");
        }
    }

    /// <summary>
    /// A supervisor left waiting on an owner who never answers must not stay frozen forever — past
    /// the cap it may work again, and the quiet/away machinery covers a genuinely absent owner.
    /// </summary>
    void Expire_StaleAwaitingAnswerFlags()
    {
        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            var flagFile = Path.Combine(_paths.Get_OrchestrationFolder(session.OrchId), AWAITING_ANSWER_FLAG_FILE);

            try
            {
                if (!File.Exists(flagFile))
                    continue;

                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(flagFile)).TotalMinutes >= QUESTION_HOLD_CAP_MINUTES)
                {
                    File.Delete(flagFile);
                    _log.Log_Info(session.OrchId, $"Awaiting-answer flag expired after {QUESTION_HOLD_CAP_MINUTES} min — the supervisor may proceed");
                }
            }
            catch (Exception ex)
            {
                _log.Log_Warning(session.OrchId, $"Awaiting-answer flag check failed: {ex.Message}");
            }
        }
    }

    /// <summary>The owner engaged — whatever they said, the conversation moves again.</summary>
    void Clear_OpenQuestions(string orchId)
    {
        lock (_ownerStateLock)
        {
            List<long> answered = [];

            foreach (var pair in _openQuestions)
            {
                if (pair.Value.OrchId == orchId)
                    answered.Add(pair.Key);
            }

            foreach (var messageId in answered)
                _openQuestions.Remove(messageId);
        }
    }

    static string Build_HoldReceiptText(int heldCount)
    {
        if (heldCount == 0)
            return "⏸ holding — send GO when you're done";

        return $"✓ ⏸ holding · {heldCount} message{(heldCount == 1 ? "" : "s")} — send GO when you're done";
    }

    /// <summary>
    /// Counts a message that landed during a hold, and rewrites the WAIT acknowledgement in place.
    /// Held messages get no tick of their own, so without this the owner is typing into silence.
    /// </summary>
    async Task Update_HoldReceipt_Async(
        ITelegramApiClient client, Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message, CancellationToken cancellationToken)
    {
        var targetKey = Resolve_TargetChannelFile_OrNull(message);

        if (targetKey == null)
            return;

        HoldReceipt? receipt;

        lock (_ownerStateLock)
        {
            if (!_holdReceipts.TryGetValue(targetKey, out receipt))
                return;

            receipt.HeldCount++;
        }

        if (receipt.MessageId == null)
            return;

        try
        {
            await client.Edit_MessageText_Async(receipt.MessageId.Value, Build_HoldReceiptText(receipt.HeldCount), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(Describe_MessageOrch(message), $"Hold receipt update failed: {ex.Message}");
        }
    }

    /// <summary>The buffer keys on the channel file — one resolver, so hold and delivery cannot disagree.</summary>
    string? Resolve_TargetChannelFile_OrNull(Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message)
    {
        if (message.MessageThreadId == null)
            return _paths.GeneralChannelFile;

        var session = _store.Find_ByTelegramTopicId_OrNull(message.MessageThreadId.Value);

        return session == null ? null : _paths.Get_OwnerChannelFile(session.OrchId);
    }

    string Describe_MessageOrch(Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message)
    {
        if (message.MessageThreadId == null)
            return ChannelDiscovery.GENERAL_ORCH_ID;

        return _store.Find_ByTelegramTopicId_OrNull(message.MessageThreadId.Value)?.OrchId ?? GLOBAL_ORCH_ID;
    }

    async Task Route_OwnerMessage_Async(Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message, CancellationToken cancellationToken)
    {
        string orchId;
        string channelFile;

        if (message.MessageThreadId == null)
        {
            orchId = ChannelDiscovery.GENERAL_ORCH_ID;
            channelFile = _paths.GeneralChannelFile;
        }
        else
        {
            var session = _store.Find_ByTelegramTopicId_OrNull(message.MessageThreadId.Value);

            if (session == null)
            {
                _log.Log_Warning(GLOBAL_ORCH_ID, $"Owner message in unknown topic {message.MessageThreadId} ignored: {message.Text}");
                return;
            }

            orchId = session.OrchId;
            channelFile = _paths.Get_OwnerChannelFile(orchId);
        }

        string segmentText;

        if (message.VoiceFileId != null)
        {
            var voiceText = await Build_VoiceEntryText_OrNull_Async(message, channelFile, orchId, cancellationToken);

            // Not configured or failed — the owner already got a direct reply; nothing to route.
            if (voiceText == null)
                return;

            segmentText = voiceText;
        }
        else if (message.PhotoFileId != null)
        {
            segmentText = await Build_PhotoEntryText_Async(message, channelFile, orchId, cancellationToken);
        }
        else
        {
            segmentText = message.Text;
        }

        lock (_deliveryLock)
        {
            _deliveryTargets[channelFile] = (orchId, message.MessageThreadId);
        }

        // Any word from the owner — including a button tap — means they are back.
        if (Note_OwnerSpoke_AndWasAway())
            await Exit_AwayMode_Async(cancellationToken);

        // ...and it answers whatever was asked, whether or not it answers it. The conversation
        // unfreezes and everything the supervisor queued behind the question flows now.
        Clear_OpenQuestions(orchId);
        Clear_AwaitingAnswerFlag(orchId);

        // The owner is engaged, so nothing is deadlocked — a suppressed entry from before must not
        // surface later, out of context, as if it were still waiting for them.
        lock (_ownerStateLock)
        {
            _lastSuppressedEntry.Remove(orchId);

            // Whatever the supervisor says next is the answer to this, and it MUST reach them.
            _ownerAwaitingAnswer.Add(orchId);
        }

        _ownerDeliveryBuffer.Add_Segment(channelFile, segmentText, DateTime.UtcNow);
        _log.Log_Info(orchId, "Owner message buffered (aggregation window running)");
    }

    async Task Flush_OwnerDeliveries_Async(CancellationToken cancellationToken)
    {
        if (!_ownerDeliveryBuffer.Has_PendingDeliveries())
            return;

        foreach (var delivery in _ownerDeliveryBuffer.Take_ReadyDeliveries(DateTime.UtcNow))
        {
            // Delivered — including via the idle cap on a forgotten WAIT, which never sees a GO.
            lock (_ownerStateLock)
            {
                _holdReceipts.Remove(delivery.Key);
            }

            (string OrchId, long? ThreadId) target;

            lock (_deliveryLock)
            {
                if (!_deliveryTargets.TryGetValue(delivery.Key, out target))
                {
                    _log.Log_Warning(GLOBAL_ORCH_ID, $"Owner delivery for '{delivery.Key}' has no recorded target — dropped");
                    continue;
                }
            }

            var deliveryText = delivery.Value;

            // Italian layer: the SESSION must only ever see English — translate the aggregated
            // owner text before it touches the channel. Already-English text passes unchanged.
            if (_configProvider.Get_Current().TelegramItalianLayer)
                deliveryText = await _translator.Translate_ToEnglish_Async(deliveryText, cancellationToken);

            // Counted BEFORE the owner entry lands, so a later increase can only mean the
            // supervisor answered THIS message.
            var supervisorEntryCountBefore = Count_SupervisorEntries(delivery.Key);

            ChannelAppender.Append_OwnerEntry(delivery.Key, deliveryText, DateTime.Now);
            _log.Log_Info(target.OrchId, "Owner message delivered to the supervisor");
            Raise_OrchestrationActivity(target.OrchId);

            if (_telegramClient == null || _telegramMuted)
                continue;

            try
            {
                // The batch's ✓ becomes "✓✓" — plus a TRUTHFUL handoff line (can the recipient
                // answer now, or is it mid-turn with the communicator covering the wait?). One
                // message that evolves, never a pile of ✓ / ✓✓ / thinking lines.
                var handoffLine = Build_HandoffLine(target.OrchId);

                var receiptText = Should_SendHandoffLine(target.OrchId, handoffLine)
                    ? $"✓✓  ·  {handoffLine}"
                    : "✓✓";

                var receiptMessageId = await Publish_DeliveryReceipt_Async(_telegramClient, target.ThreadId, receiptText, cancellationToken);

                // Tracked until the supervisor actually answers — the owner must never be left
                // staring at a receipt frozen on "thinking…".
                lock (_ownerStateLock)
                {
                    _pendingOwnerReplies[target.OrchId] = new PendingOwnerReply
                    {
                        ThreadId = target.ThreadId,
                        ReceiptMessageId = receiptMessageId,
                        SupervisorEntryCountAtDelivery = supervisorEntryCountBefore,
                        DeliveredUtc = DateTime.UtcNow,
                        Nudged = false,
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(target.OrchId, $"Delivery receipt send failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Several messages sent minutes apart close separate aggregation windows, and repeating the
    /// SAME handoff line after each ✓✓ is pure noise (the owner saw three identical "thinking…"
    /// lines in a row). Repeat it only when the state actually changed, or after a long gap when
    /// it has become informative again.
    /// </summary>
    bool Should_SendHandoffLine(string orchId, string handoffLine)
    {
        const int REPEAT_AFTER_MINUTES = 5;

        if (_lastHandoffLineByOrchId.TryGetValue(orchId, out var last)
            && last.Line == handoffLine
            && (DateTime.UtcNow - last.SentUtc).TotalMinutes < REPEAT_AFTER_MINUTES)
        {
            return false;
        }

        _lastHandoffLineByOrchId[orchId] = (handoffLine, DateTime.UtcNow);
        return true;
    }

    /// <summary>
    /// What happens to the message the owner just sent. "thinking…" is only honest when the
    /// recipient is free to pick it up; a session already mid-turn cannot, and saying so (with
    /// who will cover the wait) is the whole point of having a communicator.
    /// </summary>
    string Build_HandoffLine(string orchId)
    {
        if (orchId == ChannelDiscovery.GENERAL_ORCH_ID)
        {
            return Is_SessionMidTurn(Path.Combine(_paths.GeneralFolder, UsageTotals_Reader.SESSION_USAGE_FILE))
                ? "🟡 Gen-Sup: busy — will read this the moment the current turn ends"
                : "🟡 Gen-Sup: thinking…";
        }

        var supervisorUsageFile = Path.Combine(_paths.Get_OrchestrationFolder(orchId), UsageTotals_Reader.SESSION_USAGE_FILE);

        if (!Is_SessionMidTurn(supervisorUsageFile))
            return "🔴 Sup: thinking…";

        // Say WHAT it is doing, not just that it is busy — read straight off its transcript, which
        // is where the communicator used to read it, minus the session and the turn it cost.
        var activity = SupervisorActivity_Describer.Describe_OrNull(supervisorUsageFile);

        return activity == null
            ? "🔴 Sup: busy mid-task — he'll pick this up when the current turn ends"
            : $"🔴 Sup: busy — {activity} — he'll pick this up when the current turn ends";
    }

    /// <summary>
    /// Single tick = "received", sent immediately per message. Its id is remembered so the
    /// delivery (✓✓) and the handoff line can REWRITE this very message instead of adding more.
    /// </summary>
    async Task Send_ReceivedAck_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        try
        {
            var messageId = await client.Send_Message_Async(messageThreadId, "✓", cancellationToken);

            if (messageId != null)
            {
                Remember_ReceiptMessage(messageThreadId, messageId.Value);
                Remember_TopicMessage(messageThreadId, messageId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Received-ack send failed: {ex.Message}");
        }
    }

    void Remember_ReceiptMessage(long? messageThreadId, long messageId)
    {
        lock (_receiptLock)
        {
            _receiptMessageIdByThread[messageThreadId ?? 0] = messageId;
        }
    }

    /// <summary>Records a message id as belonging to a topic — the ONLY source /clear may delete from.</summary>
    void Remember_TopicMessage(long? messageThreadId, long? messageId)
    {
        const int KNOWN_IDS_PER_TOPIC_CAP = 4000;

        if (messageId == null)
            return;

        lock (_knownMessageIdsLock)
        {
            var key = messageThreadId ?? 0;

            if (!_knownMessageIdsByThread.TryGetValue(key, out var ids))
            {
                ids = [];
                _knownMessageIdsByThread[key] = ids;
            }

            ids.Add(messageId.Value);

            if (ids.Count > KNOWN_IDS_PER_TOPIC_CAP)
                ids.RemoveRange(0, ids.Count - KNOWN_IDS_PER_TOPIC_CAP);
        }
    }

    IReadOnlyList<long> Take_KnownTopicMessageIds(long? messageThreadId)
    {
        lock (_knownMessageIdsLock)
        {
            var key = messageThreadId ?? 0;

            if (!_knownMessageIdsByThread.TryGetValue(key, out var ids))
                return [];

            List<long> taken = [.. ids];
            ids.Clear();
            return taken;
        }
    }

    long? Take_ReceiptMessageId_OrNull(long? messageThreadId)
    {
        lock (_receiptLock)
        {
            var key = messageThreadId ?? 0;

            if (!_receiptMessageIdByThread.TryGetValue(key, out var messageId))
                return null;

            // Consumed: the next batch starts its own receipt rather than rewriting this one.
            _receiptMessageIdByThread.Remove(key);
            return messageId;
        }
    }

    /// <summary>
    /// The owner must always learn what became of their message. If the supervisor's turn ends
    /// without a reply here (it went idle, typically waiting on an implementer), the app says so
    /// on the receipt AND nudges the supervisor in its channel — which trips its watcher, so a
    /// real answer follows instead of a receipt frozen on "thinking…".
    /// </summary>
    /// <summary>
    /// The periodic STATUS the SUPERVISOR used to write every ~30 min — about 26 paid turns a day
    /// (~$44) spent restating what this process can compute for free from PLAN.md, the member
    /// states and the activity probes. Same cadence, same content, same "only while work is in
    /// flight" condition, and it runs on the bridge tick, so it adds no session and no idle wake.
    /// </summary>
    async Task Push_PeriodicStatus_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null || session.TelegramTopicId == null)
                continue;

            // No mode gate here: the status now rides the channel, so Normal mirrors it, Deferred
            // queues it (newest only) and Silenced drops it — all handled by the mirror already.
            _lastPeriodicStatusUtc.TryGetValue(session.OrchId, out var lastUtc);

            if ((DateTime.UtcNow - lastUtc).TotalSeconds < PERIODIC_STATUS_SECONDS)
                continue;

            // Away mode: the owner cannot reply, so this update is their ONLY window into the
            // orchestration — it goes out whether or not the ledger says work is in flight,
            // because "imp-1 is blocked waiting for you" is exactly what they need to know.
            if (Is_AwayMode())
            {
                _lastPeriodicStatusUtc[session.OrchId] = DateTime.UtcNow;
                Post_StatusEntry(session.OrchId, Build_AwayUpdateText(session));
                continue;
            }

            if (!Has_WorkInFlight(session))
            {
                // Nothing running: the supervisor's rule was to stop the cadence, not to report
                // "no change" forever. Stamp anyway so work starting does not fire instantly.
                _lastPeriodicStatusUtc[session.OrchId] = DateTime.UtcNow;
                continue;
            }

            _lastPeriodicStatusUtc[session.OrchId] = DateTime.UtcNow;
            Post_StatusEntry(session.OrchId, Build_PeriodicStatusText(session));
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// A supervisor message reached the owner's phone and is so far unanswered. The 3rd one makes
    /// this orchestration go QUIET immediately — waiting out the 15-minute clock before reacting is
    /// exactly how the owner ended up with a hundred questions from a single flight.
    /// </summary>
    bool Note_SupervisorSpokeToOwner_AndJustWentQuiet(string orchId)
    {
        // SILENCED means the owner is reading this orchestration LIVE in its terminal and asked
        // not to be texted it twice. They are present, and nothing was delivered to ignore — so no
        // reply is expected and none of this counts. Counting it would manufacture an absence out
        // of a setting the owner deliberately chose.
        if (Resolve_EffectiveMode(orchId) == TelegramDeliveryModes.Silenced)
            return false;

        lock (_ownerStateLock)
        {
            var tracker = Get_AwayTracker(orchId);
            tracker.UnansweredCount++;

            if (tracker.IsQuiet || !AwayMode_Policy.Should_GoQuiet(tracker.UnansweredCount))
                return false;

            tracker.IsQuiet = true;
            return true;
        }
    }

    /// <summary>
    /// The owner said ANYTHING, in ANY topic (including tapping a button) — they are here, for every
    /// orchestration at once. Returns true when this ends an away spell.
    /// </summary>
    bool Note_OwnerSpoke_AndWasAway()
    {
        lock (_ownerStateLock)
        {
            var wasAway = _awayActive;

            _lastOwnerMessageUtc = DateTime.UtcNow;
            _awayActive = false;

            foreach (var tracker in _awayTrackers.Values)
            {
                tracker.UnansweredCount = 0;
                tracker.IsQuiet = false;
            }

            return wasAway;
        }
    }

    AwayTracker Get_AwayTracker(string orchId)
    {
        if (_awayTrackers.TryGetValue(orchId, out var existing))
            return existing;

        var created = new AwayTracker();
        _awayTrackers[orchId] = created;
        return created;
    }

    public bool Is_AwayMode()
    {
        lock (_ownerStateLock)
        {
            return _awayActive;
        }
    }

    /// <summary>
    /// Tells the supervisor, with real numbers, when the message it just sent the owner was too
    /// long. The rule has been in its role command from day one and the owner still reports it as
    /// verbose — every rule in this system that actually held got a feedback loop, not firmer
    /// wording. Rate-limited, because nagging after every message would itself become the noise.
    /// </summary>
    void Nudge_IfTooVerbose(string orchId, string mirroredText)
    {
        if (!Brevity_Policy.Is_TooLong(mirroredText))
            return;

        lock (_ownerStateLock)
        {
            _lastVerbosityNudgeUtc.TryGetValue(orchId, out var lastUtc);

            if ((DateTime.UtcNow - lastUtc).TotalMinutes < Brevity_Policy.NUDGE_COOLDOWN_MINUTES)
                return;

            _lastVerbosityNudgeUtc[orchId] = DateTime.UtcNow;
        }

        ChannelAppender.Append_AppEntry(
            _paths.Get_OwnerChannelFile(orchId),
            "that message was too long for a phone",
            Brevity_Policy.Build_NudgeBody(mirroredText),
            DateTime.Now);

        _log.Log_Info(orchId, $"Supervisor message exceeded the brevity cap ({Brevity_Policy.Count_Lines(mirroredText)} lines) — nudged");
    }

    /// <summary>
    /// Do-Not-Disturb is the owner SAYING they are away, so it needs no detection and no 15-minute
    /// wait: the supervisor is told at once to behave exactly as in away mode. (Silenced is the
    /// opposite — they are reading the terminal live, so nothing changes for the supervisor.)
    /// </summary>
    void Tell_Supervisor_AboutMode(string orchId, TelegramDeliveryModes previousMode, TelegramDeliveryModes newMode)
    {
        if (newMode == previousMode)
            return;

        if (newMode == TelegramDeliveryModes.Deferred)
        {
            ChannelAppender.Append_AppEntry(
                _paths.Get_OwnerChannelFile(orchId),
                "the owner switched this topic to Do-Not-Disturb — treat it as AWAY",
                "They set DND deliberately, so this is not a guess: they are away and nothing you write reaches them "
                + "until they switch back.\n\n"
                + "Behave exactly as in AWAY MODE: ask NOTHING, park what you need from them, decide and delegate "
                + "everything you safely can, and leave the owner-approval and merge gates standing. The app queues a "
                + "short status for them and keeps only the newest, so they return to the CURRENT state instead of a "
                + "backlog. You get an entry here when they switch back.",
                DateTime.Now);

            Raise_OrchestrationActivity(orchId);
            return;
        }

        if (previousMode == TelegramDeliveryModes.Deferred && newMode == TelegramDeliveryModes.Normal)
        {
            ChannelAppender.Append_AppEntry(
                _paths.Get_OwnerChannelFile(orchId),
                "Do-Not-Disturb is off — the owner is back",
                "Normal mode. Re-ask ONLY what still matters, rewritten against the CURRENT state, and drop what "
                + "events have overtaken. One line on what you decided while they were away.",
                DateTime.Now);

            Raise_OrchestrationActivity(orchId);
        }
    }

    /// <summary>
    /// Per-orchestration: has THIS one stopped asking? Quiet stays local on purpose — the owner may
    /// be silent here simply because they are working in another topic, which is not absence.
    /// </summary>
    public bool Is_Quiet(string orchId)
    {
        lock (_ownerStateLock)
        {
            return _awayTrackers.TryGetValue(orchId, out var tracker) && tracker.IsQuiet;
        }
    }

    /// <summary>
    /// Told to ONE orchestration the moment it hits three unanswered messages. Nothing is announced
    /// to the owner and nothing is parked yet — they may be seconds from replying. This only stops
    /// the flood while we find out.
    /// </summary>
    async Task Enter_QuietMode_Async(string orchId, CancellationToken cancellationToken)
    {
        _log.Log_Info(orchId, $"QUIET — {AwayMode_Policy.QUIET_THRESHOLD} unanswered messages; supervisor told to hold further questions");

        ChannelAppender.Append_AppEntry(
            _paths.Get_OwnerChannelFile(orchId),
            "HOLD — the owner has not answered your last messages",
            $"{AwayMode_Policy.QUIET_THRESHOLD} of your messages are unanswered. They may simply be mid-task, so nothing is being "
            + "assumed yet — but STOP sending them anything more for now: no questions, no options, no updates.\n\n"
            + "Park what you would have asked (keep the list; you will re-ask from it) and carry on with what you can "
            + $"decide and delegate yourself. If they stay silent for {AwayMode_Policy.AWAY_AFTER_MINUTES} minutes you will get an "
            + "AWAY MODE ON entry; if they reply, everything returns to normal on its own.",
            DateTime.Now);

        Raise_OrchestrationActivity(orchId);

        // The owner gets ONE line marking the boundary in the conversation: everything above it was
        // asked, nothing below will be until they reply. The topic glyph says WHAT, this says WHERE.
        var session = _store.Get_Session_OrNull(orchId);

        if (session != null)
            await Send_AwayNotice_Async(session, AwayMode_Policy.QUIET_ON_NOTICE, cancellationToken);
    }

    /// <summary>
    /// Flips away mode on once the owner has visibly stopped reading, and keeps the short updates
    /// coming while it is on. The supervisor is told through its channel (that is the only thing it
    /// reads); the owner is told on Telegram.
    /// </summary>
    async Task Check_AwayMode_Async(CancellationToken cancellationToken)
    {
        bool shouldEnter;

        lock (_ownerStateLock)
        {
            var anyQuiet = _awayTrackers.Values.Any(tracker => tracker.IsQuiet);

            shouldEnter = !_awayActive && AwayMode_Policy.Should_EnterAway(anyQuiet, _lastOwnerMessageUtc, DateTime.UtcNow);

            if (shouldEnter)
                _awayActive = true;
        }

        if (shouldEnter)
            await Enter_AwayMode_Async(cancellationToken);
    }

    /// <summary>
    /// APP-WIDE. Every open orchestration is told, the general supervisor is told, every topic gets
    /// the ✈ glyph and one notice. The app coordinates all of it directly — supervisors relaying
    /// this to each other would be slower, lossier, and would cost tokens to do worse.
    /// </summary>
    async Task Enter_AwayMode_Async(CancellationToken cancellationToken)
    {
        _log.Log_Info(GLOBAL_ORCH_ID, "AWAY MODE ON (app-wide) — owner unresponsive; every supervisor told to proceed without questions");

        ChannelAppender.Append_AppEntry(
            _paths.GeneralChannelFile,
            "AWAY MODE ON — the owner is not reading",
            "Every orchestration has been told directly; you do not need to relay it. Ask them nothing until the "
            + "AWAY MODE OFF entry arrives.",
            DateTime.Now);

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            ChannelAppender.Append_AppEntry(
                _paths.Get_OwnerChannelFile(session.OrchId),
                "AWAY MODE ON — the owner is not reading",
                "They have not answered. Assume they are unavailable, NOT ignoring you.\n\n"
                + "Until further notice: ask NOTHING. Park every question you would have asked (keep a list — you will "
                + "re-ask the ones that still matter). Decide everything you can safely decide yourself and keep the "
                + "implementers working; the owner-approval gate and the merge gate still stand, so work that genuinely "
                + "needs their decision waits rather than proceeding without it.\n\n"
                + "The app posts a short update to them every 30 min — you do not need to. When they return you get an "
                + "AWAY MODE OFF entry; then re-ask ONLY what is still relevant, updated to the current state, and drop "
                + "what events have overtaken.",
                DateTime.Now);

            Raise_OrchestrationActivity(session.OrchId);

            await Park_OpenQuestions_Async(session.OrchId, cancellationToken);
            await Send_AwayNotice_Async(session, AwayMode_Policy.AWAY_ON_NOTICE, cancellationToken);
        }
    }

    async Task Exit_AwayMode_Async(CancellationToken cancellationToken)
    {
        _log.Log_Info(GLOBAL_ORCH_ID, "AWAY MODE OFF (app-wide) — owner is back");

        ChannelAppender.Append_AppEntry(
            _paths.GeneralChannelFile,
            "AWAY MODE OFF — the owner is back",
            "Every orchestration has been told directly.",
            DateTime.Now);

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            ChannelAppender.Append_AppEntry(
                _paths.Get_OwnerChannelFile(session.OrchId),
                "AWAY MODE OFF — the owner is back",
                "Normal mode: they are reading and can answer within a short time.\n\n"
                + "Go through the questions you parked. Re-ask ONLY the ones that still matter, rewritten against the "
                + "CURRENT state (facts may have moved while they were away), and say in one line what you decided "
                + "yourself in the meantime. Drop the rest without ceremony — a re-asked obsolete question is exactly "
                + "the mess this mode exists to prevent.",
                DateTime.Now);

            Raise_OrchestrationActivity(session.OrchId);

            await Send_AwayNotice_Async(session, AwayMode_Policy.AWAY_OFF_NOTICE, cancellationToken);
        }
    }

    async Task Send_AwayNotice_Async(IOrchestrationSession session, string text, CancellationToken cancellationToken)
    {
        if (_telegramClient == null || Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
            return;

        try
        {
            await _telegramClient.Send_Message_Async(session.TelegramTopicId, text, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(session.OrchId, $"Away-mode notice send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks every unanswered question as parked and strips its buttons, so a returning owner can
    /// see at a glance which ones are dead instead of having to work it out.
    /// </summary>
    async Task Park_OpenQuestions_Async(string orchId, CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        List<(long MessageId, string Text)> parked = [];

        lock (_ownerStateLock)
        {
            foreach (var pair in _openQuestions)
            {
                if (pair.Value.OrchId == orchId)
                    parked.Add((pair.Key, pair.Value.Text));
            }

            foreach (var entry in parked)
                _openQuestions.Remove(entry.MessageId);
        }

        foreach (var entry in parked)
        {
            try
            {
                await _telegramClient.Edit_MessageText_Async(
                    entry.MessageId, $"{entry.Text}{AwayMode_Policy.PARKED_SUFFIX}", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(orchId, $"Parking question message {entry.MessageId} failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Written to the CHANNEL, not straight to Telegram, so the existing delivery modes handle it:
    ///   Normal   — mirrored now.
    ///   Deferred — queued while DND lasts and collapsed to the NEWEST, so returning shows the
    ///              current state rather than a hundred stale reports.
    ///   Silenced — dropped, because the owner is reading the terminal live.
    /// Doing this by hand at each send site would have meant reimplementing all three.
    /// </summary>
    void Post_StatusEntry(string orchId, string text)
    {
        ChannelAppender.Append_AppEntry(
            _paths.Get_OwnerChannelFile(orchId),
            MirrorText_Formatter.STATUS_SUBJECT_PREFIX,
            text,
            DateTime.Now);

        Raise_OrchestrationActivity(orchId);
    }

    /// <summary>
    /// THREE LINES MAX, by owner mandate: enough to stay oriented while unable to reply, short
    /// enough to read on a lock screen. Anything past three members collapses into the last line.
    /// </summary>
    public const int AWAY_UPDATE_MAX_LINES = 3;

    string Build_AwayUpdateText(IOrchestrationSession session)
    {
        List<string> memberLines = [];

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId);
            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));
            var usageFile = Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE);

            memberLines.Add($"{member.MemberId}: {Describe_AwayMemberState(entries, usageFile)}");
        }

        if (memberLines.Count == 0)
            return "🌙 away · no open members";

        List<string> lines = [.. memberLines.Take(AWAY_UPDATE_MAX_LINES - 1)];

        var remaining = memberLines.Skip(AWAY_UPDATE_MAX_LINES - 1).ToList();

        if (remaining.Count == 1)
            lines.Add(remaining[0]);
        else if (remaining.Count > 1)
            lines.Add(string.Join(" · ", remaining));

        return $"🌙 {string.Join('\n', lines)}";
    }

    static string Describe_AwayMemberState(IReadOnlyList<Channels.ChannelEntry.IChannelEntry> entries, string usageFilePath)
    {
        var state = MemberState_Resolver.Resolve(entries);

        if (state == MemberStates.BlockedOnOwner)
            return "BLOCKED — needs you";

        var working = SessionActivity_Probe.Is_MidTurn(usageFilePath);
        var lastBrief = entries.LastOrDefault(e => e.Author == ChannelAuthors.Supervisor);

        var task = lastBrief == null
            ? ""
            : $" — {TextSummary_Formatter.Summarize_Task(lastBrief.Subject, TextSummary_Formatter.CARD_TASK_WORDS)}";

        if (working)
            return $"working{task}";

        if (state == MemberStates.AwaitingSupervisorReview)
            return "report filed, awaiting review";

        return $"idle{task}";
    }

    string Build_PeriodicStatusText(IOrchestrationSession session)
    {
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(
            UsageTotals_Reader.Read_Text_Safe(_paths.Get_PlanFile(session.OrchId)));

        // Just the word: the counts now lead the body (the same line /status shows), and printing
        // them here as well put the same figures twice in one message.
        const string header = "STATUS";

        var current = progress?.CurrentTaskText;

        var body = current == null
            ? Build_MemberStatusText_ForSession(session)
            : $"{Build_MemberStatusText_ForSession(session)}\n- now: {TextSummary_Formatter.Summarize_Task(current, TextSummary_Formatter.CARD_TASK_WORDS)}";

        return $"{header}\n{body}";
    }

    /// <summary>
    /// "Work in flight" without asking anyone: a member is mid-turn, or the ledger says a task is
    /// in progress. Both are facts on disk; neither costs a turn to establish.
    /// </summary>
    bool Has_WorkInFlight(IOrchestrationSession session)
    {
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(
            UsageTotals_Reader.Read_Text_Safe(_paths.Get_PlanFile(session.OrchId)));

        if (progress != null && progress.InProgress > 0)
            return true;

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            var usageFile = Path.Combine(
                _paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE);

            if (SessionActivity_Probe.Is_MidTurn(usageFile))
                return true;
        }

        return false;
    }

    /// <summary>
    /// What the COMMUNICATOR session used to do, for free. It cost $74/day per orchestration and
    /// 196 turns to emit 37 identical STATUS entries; every input it used (the supervisor's
    /// transcript, the owner channel) is readable from here, and the rules it followed are the ones
    /// encoded below — wait ~45 s so an idle supervisor answers for itself, never speak once the
    /// supervisor has the floor, repeat every ~3 minutes while it stays busy, stay short.
    ///
    /// The line goes STRAIGHT to Telegram and never into owner-channel.md: the supervisor was told
    /// to ignore communicator entries anyway, so writing them only made its context bigger.
    /// </summary>
    async Task Narrate_BusySupervisor_Async(string orchId, PendingOwnerReply pending, string supervisorUsageFile, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var isFirst = pending.LastNarratedUtc == default;

        var dueSeconds = isFirst ? NARRATION_FIRST_DELAY_SECONDS : NARRATION_REPEAT_SECONDS;
        var since = isFirst ? pending.DeliveredUtc : pending.LastNarratedUtc;

        if ((now - since).TotalSeconds < dueSeconds)
            return;

        if (_telegramClient == null || Resolve_EffectiveMode(orchId) != TelegramDeliveryModes.Normal)
            return;

        var activity = SupervisorActivity_Describer.Describe_OrNull(supervisorUsageFile);
        var waitedFor = SessionDuration_Formatter.Describe(now - pending.DeliveredUtc);

        var text = isFirst
            ? Build_FirstNarration(activity)
            : $"🔴 Sup: still at it{(activity == null ? "" : $" — {activity}")} · your message has been waiting {waitedFor}";

        try
        {
            // Repeats EDIT the first narration instead of sending another message — one line that
            // keeps counting up, not a column of notifications. Same reasoning as the turn-ended
            // receipt below, which has always worked this way.
            if (pending.NarrationMessageId != null)
            {
                await _telegramClient.Edit_MessageText_Async(pending.NarrationMessageId.Value, text, cancellationToken);
            }
            else
            {
                pending.NarrationMessageId = await _telegramClient.Send_Message_Async(pending.ThreadId, text, cancellationToken);
            }

            pending.LastNarratedUtc = now;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed EDIT must not freeze the narration forever on a dead message id: drop it so
            // the next repeat sends a fresh line and starts editing that one instead.
            pending.NarrationMessageId = null;
            _log.Log_Warning(orchId, $"Busy-supervisor narration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The owner's complaint, verbatim: "I should ALWAYS be advised when a sup finishes reasoning
    /// ... otherwise I have no way of knowing that I can text him." They had been shown "Sup: busy",
    /// the turn then ended silently, and nothing ever corrected that line.
    ///
    /// It EDITS the existing receipt rather than sending a new message — the whole point is that the
    /// stale line stops lying, and one more notification would work against the quiet this system
    /// has been fighting for.
    /// </summary>
    async Task Announce_SupervisorFree_Async(string orchId, PendingOwnerReply pending, CancellationToken cancellationToken)
    {
        if (_telegramClient == null || pending.ReceiptMessageId == null)
            return;

        if (Resolve_EffectiveMode(orchId) != TelegramDeliveryModes.Normal)
            return;

        try
        {
            await _telegramClient.Edit_MessageText_Async(
                pending.ReceiptMessageId.Value,
                "✓✓  ·  🔴 Sup: turn ended — free now, he is reading this",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Turn-ended announcement failed: {ex.Message}");
        }
    }

    static string Build_FirstNarration(string? activity)
    {
        var doing = activity == null ? "mid-task" : $"mid-task — {activity}";

        return $"🔴 Sup: {doing}. Your message is delivered; he picks it up when this turn ends.";
    }

    async Task Resolve_PendingOwnerReplies_Async(CancellationToken cancellationToken)
    {
        List<string> trackedOrchIds;

        lock (_ownerStateLock)
        {
            trackedOrchIds = [.. _pendingOwnerReplies.Keys];
        }

        foreach (var orchId in trackedOrchIds)
        {
            PendingOwnerReply? pending;

            lock (_ownerStateLock)
            {
                // GO can flush (and replace an entry) from the inbound loop between iterations.
                if (!_pendingOwnerReplies.TryGetValue(orchId, out pending))
                    continue;
            }

            var ownerChannel = orchId == ChannelDiscovery.GENERAL_ORCH_ID
                ? _paths.GeneralChannelFile
                : _paths.Get_OwnerChannelFile(orchId);

            var supervisorEntryCount = Count_SupervisorEntries(ownerChannel);

            // Answered: the supervisor wrote to the owner. The mirrored entry IS the feedback.
            if (supervisorEntryCount > pending.SupervisorEntryCountAtDelivery)
            {
                lock (_ownerStateLock)
                {
                    _pendingOwnerReplies.Remove(orchId);
                }

                continue;
            }

            var supervisorUsageFile = orchId == ChannelDiscovery.GENERAL_ORCH_ID
                ? Path.Combine(_paths.GeneralFolder, UsageTotals_Reader.SESSION_USAGE_FILE)
                : Path.Combine(_paths.Get_OrchestrationFolder(orchId), UsageTotals_Reader.SESSION_USAGE_FILE);

            var supervisorBusy = Is_SessionMidTurn(supervisorUsageFile);

            // The communicator's whole job, done from this loop: while the supervisor is mid-turn
            // and the owner is waiting, say concretely what it is doing. First after ~45 s (an idle
            // supervisor answers for itself well inside that, which is the better outcome), then
            // every ~3 minutes for as long as it stays busy.
            if (supervisorBusy)
            {
                await Narrate_BusySupervisor_Async(orchId, pending, supervisorUsageFile, cancellationToken);
                continue;
            }

            // It was busy, the owner was told so, and now the turn has ENDED. Say so, once: without
            // it the owner is left watching a "busy" line that never changes, with no way to know
            // the supervisor is free and a message would be picked up immediately.
            if (pending.LastNarratedUtc != default && !pending.TurnEndAnnounced)
            {
                pending.TurnEndAnnounced = true;
                await Announce_SupervisorFree_Async(orchId, pending, cancellationToken);
            }

            if (pending.Nudged || (DateTime.UtcNow - pending.DeliveredUtc).TotalSeconds < OWNER_REPLY_GRACE_SECONDS)
                continue;

            pending.Nudged = true;

            ChannelAppender.Append_AppEntry(
                ownerChannel,
                "the owner is still waiting for your reply",
                "Your turn ended without answering the owner's message above. Reply now, even one line (what you are doing / what you are waiting on). The owner is looking at an unanswered receipt.",
                DateTime.Now);

            _log.Log_Warning(orchId, "Owner message went unanswered past the grace window — supervisor nudged");
            Raise_OrchestrationActivity(orchId);

            if (_telegramClient == null || Resolve_EffectiveMode(orchId) != TelegramDeliveryModes.Normal)
                continue;

            var text = "✓✓  ·  🔴 Sup: turn ended without a reply — nudged, an answer is coming";

            try
            {
                if (pending.ReceiptMessageId != null)
                    await _telegramClient.Edit_MessageText_Async(pending.ReceiptMessageId.Value, text, cancellationToken);
                else
                    await Send_DirectReply_BestEffort_Async(_telegramClient, pending.ThreadId, text, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(orchId, $"Could not update the stale receipt: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Counts across the archive too. The pending-reply logic compares a count taken at delivery
    /// against a count taken later, and that only works if the number cannot go DOWN — but
    /// compaction moves older entries out of the live file. See
    /// <see cref="ChannelHistory_Counter"/> for the 2026-08-10 incident this caused.
    /// </summary>
    static int Count_SupervisorEntries(string channelFile)
    {
        return ChannelHistory_Counter.Count_Entries_ByAuthor(channelFile, ChannelAuthors.Supervisor);
    }

    /// <summary>
    /// Turns the last ✓ of the batch into the final receipt, in place. Falls back to sending a new
    /// message when there is nothing to edit or the edit fails (Telegram refuses very old edits).
    /// </summary>
    async Task<long?> Publish_DeliveryReceipt_Async(ITelegramApiClient client, long? messageThreadId, string text, CancellationToken cancellationToken)
    {
        var messageId = Take_ReceiptMessageId_OrNull(messageThreadId);

        if (messageId != null)
        {
            try
            {
                await client.Edit_MessageText_Async(messageId.Value, text, cancellationToken);
                return messageId;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(GLOBAL_ORCH_ID, $"Receipt edit failed, sending a new message: {ex.Message}");
            }
        }

        try
        {
            return await client.Send_Message_Async(messageThreadId, text, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Receipt send failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads an owner-sent image beside the channel (media/) and references it with an
    /// 'IMAGE: &lt;path&gt;' line — the supervisor Reads the file to inspect the screenshot.
    /// </summary>
    /// <summary>
    /// Downloads the voice note and runs the CONFIGURED transcription command; the transcript
    /// becomes the message text (then translated by the Italian layer like any owner text).
    /// Null = unconfigured/failed, with a direct explanatory reply already sent to the owner.
    /// </summary>
    async Task<string?> Build_VoiceEntryText_OrNull_Async(
        Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message,
        string channelFile,
        string orchId,
        CancellationToken cancellationToken)
    {
        var client = _telegramClient
            ?? throw new Exception("Voice message arrived without a Telegram client");

        var commandTemplate = _configProvider.Get_Current().VoiceTranscribeCommand;

        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            await Send_DirectReply_BestEffort_Async(
                client,
                message.MessageThreadId,
                "🎙 voice received, but transcription is not configured — set voiceTranscribeCommand in config.json (a CLI printing the transcript to stdout, {input} = audio path), or type instead",
                cancellationToken);

            return null;
        }

        try
        {
            var voiceFileId = message.VoiceFileId
                ?? throw new Exception("Build_VoiceEntryText_OrNull_Async called without a voice file id");

            var mediaFolder = Path.Combine(Path.GetDirectoryName(channelFile)
                ?? throw new Exception($"Channel file '{channelFile}' has no parent folder"), "media");
            Directory.CreateDirectory(mediaFolder);

            var audioPath = Path.Combine(mediaFolder, $"tg-voice-{message.UpdateId}.oga");
            var audioBytes = await client.Download_File_Async(voiceFileId, cancellationToken);
            await File.WriteAllBytesAsync(audioPath, audioBytes, cancellationToken);

            var transcript = await _transcriber.Transcribe_OrNull_Async(audioPath, commandTemplate, cancellationToken);

            if (transcript == null)
            {
                await Send_DirectReply_BestEffort_Async(client, message.MessageThreadId, "🎙 couldn't transcribe the voice message — please type it", cancellationToken);
                return null;
            }

            _log.Log_Info(orchId, $"Voice note transcribed ({transcript.Length} chars)");
            return message.Text.Length == 0 ? transcript : $"{message.Text}\n{transcript}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(orchId, "Voice note handling failed", ex);
            await Send_DirectReply_BestEffort_Async(client, message.MessageThreadId, "🎙 voice message failed — please type it", cancellationToken);
            return null;
        }
    }

    async Task<string> Build_PhotoEntryText_Async(
        Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message,
        string channelFile,
        string orchId,
        CancellationToken cancellationToken)
    {
        var caption = message.Text.Length == 0 ? "(image, no caption)" : message.Text;

        try
        {
            var client = _telegramClient
                ?? throw new Exception("Photo message arrived without a Telegram client");

            var photoFileId = message.PhotoFileId
                ?? throw new Exception("Build_PhotoEntryText_Async called without a photo file id");

            var mediaFolder = Path.Combine(Path.GetDirectoryName(channelFile)
                ?? throw new Exception($"Channel file '{channelFile}' has no parent folder"), "media");
            Directory.CreateDirectory(mediaFolder);

            var imagePath = Path.Combine(mediaFolder, $"tg-{message.UpdateId}.jpg");
            var imageBytes = await client.Download_File_Async(photoFileId, cancellationToken);
            await File.WriteAllBytesAsync(imagePath, imageBytes, cancellationToken);

            _log.Log_Info(orchId, $"Owner image downloaded to {imagePath}");

            return $"{caption}\n\nIMAGE: {imagePath}\n(The owner sent this image — Read the file to inspect it.)";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(orchId, "Owner image download failed", ex);
            return $"{caption}\n\n(The owner sent an image but downloading it FAILED: {ex.Message})";
        }
    }

    void Persist_BridgeState()
    {
        lock (_stateLock)
        {
            BridgeState_Store.Save(_paths, _tailer.Get_OffsetsSnapshot(), _lastUpdateId);
        }
    }

    void Raise_OrchestrationActivity(string orchId)
    {
        try
        {
            OrchestrationActivity?.Invoke(orchId);
        }
        catch
        {
            // A faulty subscriber must not take the bridge down.
        }
    }
}


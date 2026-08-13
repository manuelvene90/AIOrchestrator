using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.WindowFocus;
using AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;
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

    /// <summary>
    /// How long a nudged, idle session may stay frozen before it is declared ORPHANED. The nudge
    /// changed its channel, so a live watcher fires within seconds — this window is generous
    /// enough that only a genuinely absent listener runs it out.
    /// </summary>
    const int ORPHAN_CONFIRM_MINUTES = 6;

    const int MIRROR_TICK_MILLISECONDS = 2000;

    /// <summary>
    /// Pause before re-sending a channel whose mirror send failed. The tailer re-emits an
    /// unconfirmed append on EVERY poll — that is what makes the retry possible — so without this
    /// the retry would be a 2-second hammer against an endpoint that is already failing, which is
    /// precisely the shape that earns a bot a server-side throttle.
    /// </summary>
    const int MIRROR_RETRY_BACKOFF_SECONDS = 30;

    /// <summary>
    /// How long a failing channel keeps being retried before its entries are declared undeliverable
    /// and dropped, loudly. Generous ON PURPOSE: a Telegram outage must not cost the owner their
    /// supervisors' messages, and this one lasted ~2.5 hours on 2026-08-11. It is bounded only
    /// because the alternative — retrying forever — lets one permanently-rejected entry block a
    /// channel's mirror for the rest of the app's life.
    /// </summary>
    const int MIRROR_RETRY_WINDOW_MINUTES = 30;
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

    /// <summary>When a channel's mirror first failed, and when it was last attempted — the retry window.</summary>
    readonly Dictionary<string, DateTime> _mirrorRetryFirstFailureUtc = [];
    readonly Dictionary<string, DateTime> _mirrorRetryLastAttemptUtc = [];

    /// <summary>Channels whose pre-existing malformed headers have been absorbed as history.</summary>
    readonly HashSet<string> _channelsShapeBaselined = [];
    readonly Lock _buttonLock = new();
    long _buttonSequence;
    long _buttonGroupSequence;

    /// <summary>
    /// Live close-confirmation prompts, keyed by their callback data. Deliberately NOT the shared
    /// button registry above, for three reasons that only matter because this action is
    /// irreversible: that registry evicts FIFO past BUTTON_REGISTRY_CAP, so a pending confirmation
    /// could be silently dropped while its keyboard is still on screen; its tickets are
    /// "opt-{sequence}" from a counter that restarts at zero every launch, so a stale on-screen
    /// button can collide with a freshly minted one; and every tap through it ends up routed to an
    /// AGENT as a synthetic owner message, whereas this one is the app's own decision to act on.
    /// The GUID data below cannot collide across restarts.
    ///
    /// Losing this dictionary on restart is safe by design: the parked FILE is the durable state,
    /// and a parked request with no live prompt is simply asked again.
    /// </summary>
    readonly Dictionary<string, CloseConfirmation> _closeConfirmations = [];

    /// <summary>
    /// Parked paths whose tap is mid-flight. A decision takes two awaited Telegram calls before its
    /// file is archived, and for that whole span the request has no registrations and is still on
    /// disk — which the sweep read as "never asked" and answered with a second prompt.
    /// </summary>
    readonly HashSet<string> _closeConfirmationsResolving = [];
    readonly Lock _closeConfirmationLock = new();

    sealed class CloseConfirmation
    {
        public required string OrchId { get; init; }
        public required string ParkedPath { get; init; }

        /// <summary>True for the "close it" button, false for "keep it open".</summary>
        public required bool Confirms { get; init; }

        public long? PromptMessageId { get; init; }
    }

    /// <summary>One alert per stall/budget EPISODE — cleared when traffic resumes (stalls only).</summary>
    readonly HashSet<string> _stallAlertedOrchIds = [];
    readonly HashSet<string> _budgetAlertedOrchIds = [];
    /// <summary>When each member was nudged — the nudge doubles as the PROBE that proves a watcher exists.</summary>
    readonly Dictionary<string, DateTime> _nudgedMemberUtc = [];

    /// <summary>
    /// WHICH unanswered thing each member was last nudged about — the raw text of the conversation
    /// entry, never its index or stamp (see Nudge_Decider.Identify_LastConversationEntry_OrNull for
    /// why those are silent failures).
    ///
    /// A SECOND DICTIONARY, DELIBERATELY, AND IT IS THE POINT OF THE FIX. `_nudgedMemberUtc` is
    /// ESCALATION state: it dates the nudge so the orphan probe can run six minutes later, and the
    /// probe clears it the moment the member proves alive. Two earlier fixes tried to answer "should
    /// we nudge again?" by changing when that map is cleared or refreshed — one re-arms a nudge, the
    /// other re-arms a RESPAWN — because the nudge gate was borrowing a map that already carried two
    /// meanings. It never needed to: this one has exactly one meaning and drives nothing else.
    ///
    /// Lost on restart, which costs ONE extra nudge per member. Visible, cheap, self-correcting —
    /// and the alternative is a third meaning in the map that can respawn a session.
    /// </summary>
    readonly Dictionary<string, string> _nudgedAboutEntry = [];

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
    /// The status-line text last WRITTEN to each topic, so an unchanged line costs no API call.
    /// In memory on purpose, unlike the message id: after a restart the first tick edits once with
    /// whatever is current, which is correct, and the id — the thing that must not be lost — lives
    /// in session.json.
    /// </summary>
    readonly Dictionary<string, string> _statusLineTextByOrchId = [];

    /// <summary>
    /// Which members have already been flagged as idle, per orchestration, so the reminder is written
    /// ONCE per quiet spell rather than every tick. Cleared when the set changes, which is what makes
    /// a member becoming idle — or stopping — say something exactly once.
    /// </summary>
    readonly Dictionary<string, string> _flaggedIdleMembersByOrchId = [];

    /// <summary>
    /// When the status line last FAILED per orchestration, so a real error backs off. Stored in
    /// LOCAL time because the planner compares it against the same clock the durations use — it
    /// held UtcNow while being compared against DateTime.Now, which cleared a 30-second backoff
    /// instantly on any machine not on UTC.
    /// </summary>
    readonly Dictionary<string, DateTime> _statusLineFailedAtByOrchId = [];

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

    /// <summary>
    /// The last half-hour SLOT each orchestration has spent, LOCAL — not a clock reading, and named
    /// so nobody compares it against a UTC one. `PeriodicStatusSlot_Planner` owns the rule.
    /// </summary>
    readonly Dictionary<string, DateTime> _lastPeriodicStatusSlot = [];

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

    /// <summary>Members waiting on a verdict, one id per line — read by the awaiting-answer hook.</summary>
    public const string AWAITING_VERDICT_FILE = ".awaiting-verdict";

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

        // Lapsing a stale close sends the owner nothing and closes nothing, so it runs even while
        // muted. Behind the gate, DND froze the only thing that disarms a live confirmation button.
        Expire_StaleCloseConfirmations();

        // DND: skip tailing entirely — offsets freeze, so unmute delivers everything pending
        // in one catch-up burst (including supervisors' questions that waited for the owner).
        // Crash-loop alerts stay queued in the watchdog until unmute for the same reason.
        if (_telegramMuted && _telegramClient != null)
            return;

        // PROMPTING stays after the DND return — nothing is asked, and so nothing closes, while the
        // owner is not being disturbed. Expiry ran above, before the gate, because lapsing is not a
        // disturbance and leaving it here let a mute keep a stale button alive indefinitely.
        await Resolve_CloseConfirmations_Async(cancellationToken);

        await Send_CrashLoopAlerts_Async(cancellationToken);
        await Send_StallAlerts_Async(cancellationToken);
        await Send_BudgetAlerts_Async(cancellationToken);
        await Nudge_IdleImplementers_Async(cancellationToken);
        await Resolve_PendingOwnerReplies_Async(cancellationToken);
        await Refresh_TopicStatusLines_Async(cancellationToken);
        Flag_IdleMembers();
        Report_GuardsNotInForce();

        var channels = Find_ActiveChannels();
        var pollResult = _tailer.Poll(channels);

        foreach (var truncatedFile in pollResult.TruncatedFiles)
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Channel file shrank (append-only protocol anomaly), offset reset: {truncatedFile}");

        // The tailer has no logger of its own. A channel it cannot read is a session the owner
        // silently stops hearing from, so the failure is surfaced here and the next poll retries it.
        foreach (var unreadableFile in pollResult.UnreadableFiles)
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Channel file could not be read this tick (the other channels are unaffected, this one retries): {unreadableFile}");

        foreach (var append in pollResult.CompletedAppends)
        {
            if (!Is_MirrorAttemptDue(append.Channel.FilePath))
                continue;

            var delivered = await Mirror_Append_Async(append, cancellationToken);
            Raise_OrchestrationActivity(append.Channel.OrchId);
            Settle_MirrorAttempt(append, delivered);
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
    /// Whether this channel may be sent NOW. Only channels whose last send failed are ever held
    /// back: they are re-emitted by the tailer on every poll, and re-sending every 2 s to an
    /// endpoint that is already failing is how a bot earns a server-side throttle.
    /// </summary>
    bool Is_MirrorAttemptDue(string channelFilePath)
    {
        if (!_mirrorRetryLastAttemptUtc.TryGetValue(channelFilePath, out var lastAttemptUtc))
            return true;

        return DateTime.UtcNow - lastAttemptUtc >= TimeSpan.FromSeconds(MIRROR_RETRY_BACKOFF_SECONDS);
    }

    /// <summary>
    /// Decides what happens to the entries just attempted. Confirming is what lets the persisted
    /// cursor move past them, so NOT confirming is the retry: the tailer re-emits them next poll.
    /// Before this, the cursor advanced during the read and a failed send dropped the owner's
    /// messages permanently — the outage of 2026-08-11 lost every entry that met a 502.
    /// </summary>
    void Settle_MirrorAttempt(ICompletedChannelAppend append, bool delivered)
    {
        var channelFilePath = append.Channel.FilePath;

        if (delivered)
        {
            _mirrorRetryFirstFailureUtc.Remove(channelFilePath);
            _mirrorRetryLastAttemptUtc.Remove(channelFilePath);
            _tailer.Confirm_Append(channelFilePath);
            return;
        }

        _mirrorRetryLastAttemptUtc[channelFilePath] = DateTime.UtcNow;

        if (!_mirrorRetryFirstFailureUtc.TryGetValue(channelFilePath, out var firstFailureUtc))
        {
            firstFailureUtc = DateTime.UtcNow;
            _mirrorRetryFirstFailureUtc[channelFilePath] = firstFailureUtc;
        }

        if (DateTime.UtcNow - firstFailureUtc < TimeSpan.FromMinutes(MIRROR_RETRY_WINDOW_MINUTES))
            return;

        // The window is spent, so this confirm DROPS the entries. Said at Error and naming the
        // channel, because the alternative — a channel that quietly never mirrors again — is the
        // exact failure the owner reported: cut off, with no way to know.
        _log.Log_Error(
            append.Channel.OrchId,
            $"Telegram mirror gave up after {MIRROR_RETRY_WINDOW_MINUTES} minutes of retries — entries from '{Path.GetFileName(channelFilePath)}' never reached the phone",
            null);

        _mirrorRetryFirstFailureUtc.Remove(channelFilePath);
        _mirrorRetryLastAttemptUtc.Remove(channelFilePath);
        _tailer.Confirm_Append(channelFilePath);
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
            // A channel that still owes Telegram a delivery must not be rewritten underneath the
            // tailer: compaction re-anchors the offset to the new file, and the entries waiting to
            // be retried would go with it. It compacts on a later tick, once the send lands.
            if (_tailer.Has_UnconfirmedEntries(channel.FilePath))
                continue;

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

            var quietFor = Get_OrchestrationQuietFor(session);

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

    /// <summary>
    /// How long this orchestration has been quiet — the SHORTEST quiet across its channels, since any
    /// one of them speaking means the orchestration is alive.
    ///
    /// THE THIRD CLOCK, and the last of them to stop reading file stamps (rev-7's F1). It used to take
    /// the max <c>File.GetLastWriteTimeUtc</c> over the owner channel and every member channel, so any
    /// write that SAID NOTHING marked the whole orchestration alive: a compaction's rename-over, a
    /// request confirmation, a `/resume` broadcast — and, self-referentially, **the supervisor nudge
    /// this engine fires itself.** The app wrote to say a report was waiting, and that write told the
    /// stall detector everything was fine. A liveness signal its own alarm resets is not a liveness
    /// signal.
    ///
    /// It goes through the SAME reader as both nudge clocks rather than measuring its own way — that
    /// is the whole point of the branch this arrived on, and a fourth private notion of "quiet" is how
    /// there came to be three.
    ///
    /// LOCAL throughout, deliberately. <see cref="Nudge_Decider.Measure_QuietFor"/> reads agent stamps
    /// and file stamps, both local wall time, so mixing a UTC `now` in here would make every span two
    /// hours short on this machine and suppress the alert rather than fire it — the silent direction.
    /// The comparison is done in TimeSpans for the same reason: nothing has to be converted, so
    /// nothing can be converted wrongly.
    /// </summary>
    TimeSpan Get_OrchestrationQuietFor(IOrchestrationSession session)
    {
        var now = DateTime.Now;

        // An orchestration cannot have been quiet for longer than it has existed — and with entry
        // stamps in play that is no longer automatic, because a stamp inside a channel can predate
        // the session that owns it.
        var quietFor = now - session.CreatedUtc.ToLocalTime();

        List<string> channelFiles = [_paths.Get_OwnerChannelFile(session.OrchId)];

        foreach (var member in session.Members)
            channelFiles.Add(_paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId));

        foreach (var channelFile in channelFiles)
        {
            if (!File.Exists(channelFile))
                continue;

            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));
            var channelQuietFor = Nudge_Decider.Measure_QuietFor(entries, now);

            // A CHANNEL THAT CANNOT BE DATED CONTRIBUTES NOTHING TO THE MINIMUM, and skipping is the
            // noisy direction here rather than the quiet one. This span is the SHORTEST across
            // channels, so any value at all pulls it down and can mask a stall; treating an
            // unmeasurable channel as recent activity would let one unreadable stamp vouch for a
            // whole orchestration being alive.
            if (channelQuietFor != null && channelQuietFor < quietFor)
                quietFor = channelQuietFor.Value;
        }

        return quietFor;
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

            List<string> awaitingVerdict = [];

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

                // Both nudge rules live in Nudge_Decider, together — see the class comment for why
                // splitting them across two files is what made them exhaustive.
                var dormantMidWork = Nudge_Decider.Is_DormantMidWork(entries, Nudge_Decider.Has_BeenBriefed(channelFile));

                // KEPT FROM MASTER, and it is the hunk that would have silently deleted a guard.
                // The awaiting-answer hook must let a supervisor answer someone already waiting while
                // refusing to let it brief someone new, and this list is how it knows. The question is
                // answered HERE, by the resolver, and merely read by the hook — a bash re-derivation
                // of the same rule drifted from this one within the hour it was written.
                //
                // A STANDING BY member is deliberately NOT published, and that is a real consequence
                // worth stating rather than discovering: it resolves to StandingBy, not
                // AwaitingSupervisorReview, so the hook will refuse a write to it while a question is
                // with the owner. That is the hook's own rule working — writing to an idle member is
                // briefing new work, which is exactly what it exists to prevent, and answering a
                // member who filed something is what it exists to allow.
                if (MemberState_Resolver.Resolve(entries) == MemberStates.AwaitingSupervisorReview)
                    awaitingVerdict.Add(member.MemberId);

                if (!dormantMidWork && !Nudge_Decider.Has_UnansweredInboundTraffic(entries))
                {
                    _nudgedMemberUtc.Remove(memberKey);
                    continue;
                }

                // The app's own writes do not count as the channel moving — see Measure_QuietFor.
                // LOCAL, and it must stay local: both sources that function reads are local wall
                // time. Handing it UtcNow here on THIS machine (UTC+2) makes quietFor NEGATIVE, so
                // nothing reaches the threshold and every nudge in the system stops — silently, with
                // a green suite. NudgeClockProbeTests pins this call for that reason; the decision is
                // pure and pinned four ways, and all four passed while this line was wrong.
                var quietFor = Nudge_Decider.Measure_QuietFor(entries, DateTime.Now);
                var alreadyNudged = _nudgedMemberUtc.TryGetValue(memberKey, out var nudgedUtc);

                // NULL IS PAST THE THRESHOLD, never under it. An unreadable clock means nobody can say
                // this member is working, and the expensive mistake is the one that stays quiet: the
                // gate below still holds it to one nudge per unanswered thing, so the cost of being
                // wrong here is a single wake.
                if (!alreadyNudged && quietFor != null && quietFor.Value.TotalMinutes < IMPLEMENTER_NUDGE_MINUTES)
                    continue;

                // Transcript growing = genuinely working (a long build, a big read). NOT orphaned:
                // this is the false positive the whole detector has to avoid.
                if (Is_SessionMidTurn(Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE)))
                    continue;

                if (!alreadyNudged)
                {
                    // ONE NUDGE PER UNANSWERED THING. The app's own entry cannot change the last
                    // CONVERSATION entry, so nothing the app writes can qualify a member for another
                    // nudge — which is what made the old repetition self-feeding: it woke the member,
                    // the waking proved it alive, that proof cleared the escalation map, and the
                    // clock its own write had restarted elapsed. Every 8 minutes, needing nobody.
                    //
                    // MEMBER PATH ONLY. The supervisor nudge is keyed and written elsewhere and does
                    // not self-feed the same way; it is not covered here, and saying so is the point
                    // of this sentence.
                    //
                    // A LIVENESS CHECK STOPPED RUNNING HERE AND IT WAS NOT LOST BY ACCIDENT. Under
                    // the old loop a healthy idle member was re-probed every eight minutes — but
                    // nobody designed that polling, it was the defect's exhaust: the repeat existed
                    // only because the app kept re-qualifying the member with its own writes. A
                    // member is now probed once per unanswered thing.
                    //
                    // The case that leaves open is narrow and deliberate: a member that is nudged,
                    // proves alive, and dies LATER with nothing new said to it. PROCESS death is not
                    // this path's job — pid files and the watchdog cover that. This path catches a
                    // live process whose MONITOR is dead, and such a member goes unnoticed only for
                    // as long as nobody needs it: the moment anyone writes, the conversation moves,
                    // the nudge fires and the probe runs six minutes later. Detected when it matters
                    // rather than polled forever.
                    var conversationIdentity = Nudge_Decider.Identify_LastConversationEntry_OrNull(entries);

                    if (conversationIdentity != null
                        && _nudgedAboutEntry.TryGetValue(memberKey, out var alreadyNudgedAbout)
                        && alreadyNudgedAbout == conversationIdentity)
                        continue;

                    await Nudge_Implementer_Async(session, member.MemberId, channelFile, entries[^1], quietFor, dormantMidWork, cancellationToken);
                    _nudgedMemberUtc[memberKey] = DateTime.UtcNow;

                    if (conversationIdentity != null)
                        _nudgedAboutEntry[memberKey] = conversationIdentity;

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

            Publish_AwaitingVerdict(session.OrchId, awaitingVerdict);
        }
    }

    /// <summary>
    /// Writes the members currently waiting on a verdict, one id per line, for the awaiting-answer
    /// hook to read. The hook is bash and cannot call the resolver, so the app answers the question
    /// and the hook only looks it up — the alternative, re-deriving "who spoke last" in shell, was
    /// written and drifted from the C# within the hour (it counted an app nudge as the last speaker
    /// and denied exactly the reply it was meant to allow).
    ///
    /// Best effort throughout: a file we cannot write costs the supervisor one allowed reply, never
    /// a wedged session.
    /// </summary>
    void Publish_AwaitingVerdict(string orchId, IReadOnlyList<string> memberIds)
    {
        try
        {
            var file = Path.Combine(_paths.Get_OrchestrationFolder(orchId), AWAITING_VERDICT_FILE);

            if (memberIds.Count == 0)
            {
                if (File.Exists(file))
                    File.Delete(file);

                return;
            }

            Storage.Atomic_FileWriter.Write_AllText(file, string.Join('\n', memberIds));
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Could not publish the awaiting-verdict list: {ex.Message}");
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

            if (!Nudge_Decider.Owes_MemberAVerdict(entries))
                continue;

            // ONE READER FOR BOTH CLOCKS. This path used to measure the member's channel by its FILE
            // stamp while the member-nudge path above measured the conversation — so the same channel
            // was "quiet for 20 minutes" to one and "quiet for 0" to the other, and the quiet-clock
            // fix looked complete while the half that reports to the supervisor still reset on every
            // app write. A compaction is the sharpest case: its rename-over advances the stamp with
            // NOBODY having spoken, so a report owed for twenty minutes went unreported. `/resume`
            // did it too, unboundedly, having no dedupe.
            //
            // LOCAL `now`, and it must stay local — Measure_QuietFor reads agent stamps and the file
            // stamp, both local wall time. The UtcNow this line used to pass was correct only because
            // it was paired with GetLastWriteTimeUtc; handing UtcNow to the shared reader on this
            // machine (UTC+2) would make the span NEGATIVE and silence the path completely.
            // Null — nothing here can be dated — is PAST the threshold, so the supervisor is told
            // rather than left to assume silence means nothing is waiting.
            var memberQuietFor = Nudge_Decider.Measure_QuietFor(entries, DateTime.Now);

            if (memberQuietFor != null && memberQuietFor.Value.TotalMinutes < IMPLEMENTER_NUDGE_MINUTES)
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
        TimeSpan? quietFor,
        bool dormantMidWork,
        CancellationToken cancellationToken)
    {
        // TWO RULES THIS MESSAGE HAS ALREADY BROKEN, both by asserting things nobody checked.
        //
        // It claimed "nothing was going to wake you". The app cannot see a session's monitor, and it
        // said this to a reviewer whose monitor was alive and had fired on every write for the
        // previous half hour.
        //
        // Then it offered three remedies, none of which could work. This branch is reached if and
        // only if the member spoke last, has been briefed, and has an OPEN WINDOW — the window test
        // in MemberState_Resolver precedes the blocked and standing-by tests, so declaring either of
        // those cannot change the state while a window is open. The only escape is closing the
        // window, which the message never mentioned. It told a stuck session to do three things that
        // would leave it exactly as stuck, which is worse than saying nothing.
        //
        // An alert that asserts an unchecked fact teaches the reader to discount the ones that are
        // checked, and this nudge is load-bearing for a genuinely stalled session.
        var subject = Nudge_Wording.Subject_For(dormantMidWork);

        var body = dormantMidWork
            ? Nudge_Wording.Body_ForOpenWindow(lastEntry.Index, Nudge_Wording.Describe_QuietFor(quietFor))
            : Nudge_Wording.Body_ForUnansweredTraffic(
                lastEntry.Index,
                lastEntry.Author.ToString().ToLowerInvariant(),
                Nudge_Wording.Describe_QuietFor(quietFor));

        ChannelAppender.Append_AppEntry(channelFile, subject, body, DateTime.Now);

        var reason = dormantMidWork ? "went dormant mid-task" : "had unread traffic";
        _log.Log_Warning(session.OrchId, $"{memberId} {reason} for {Nudge_Wording.Describe_QuietFor(quietFor)} — nudged");
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

            // The obligation is DURABLE, and the flag file is what carries it. This dictionary is
            // in-memory and BridgeState_Store persists only offsets and the last update id, so an
            // app restart used to empty it — after which Is_LedgerBehind returned false and
            // Sync_Flag DELETED the flag. In a system whose own lifecycle tree-kills and respawns
            // everything, that made the ledger debt droppable by restarting, and the comment on the
            // Stop hook claiming the enforcement was "delayed, never skipped" was simply false.
            //
            // The flag's own write time is when the debt was incurred, so re-seeding from it costs
            // no new persistence and lets the ordinary comparison clear it once PLAN.md is newer.
            if (lastVerdictUtc == default)
                lastVerdictUtc = Read_LedgerDebtStamp_OrDefault(session.OrchId);

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
    /// When the ledger debt was incurred, recovered from the flag file the previous run left behind.
    /// Default when there is no flag, which is the honest answer: no debt is recorded.
    /// </summary>
    DateTime Read_LedgerDebtStamp_OrDefault(string orchId)
    {
        try
        {
            var flagFile = LedgerHealth_Tracker.Build_FlagFilePath(_paths, orchId);

            return File.Exists(flagFile) ? File.GetLastWriteTimeUtc(flagFile) : default;
        }
        catch
        {
            // A flag we cannot stat must not invent a debt, and must not clear one either.
            return default;
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
            Dictionary<string, (double Percent, DateTime? WindowResetsAtUtc)> maxPercents = [];
            var nowUtc = DateTime.UtcNow;

            // Only probe files with a window that has not already reset. Probe files are never
            // deleted, so without this the alert scan folded five-day-old closed orchestrations into
            // "the account right now" — which is how .limit-alerts.json latched at 100% and stopped
            // alerting entirely.
            // Read_Text_Safe rather than File.ReadAllText: a live session rewriting its probe file
            // used to throw a sharing violation out of this loop and abort the whole check.
            foreach (var usageFile in RateLimits_Reader.Find_UsageFiles_WithLiveWindow(_paths, DateTime.Now))
            {
                var windows = Limits.LimitData_Parser.Extract_LimitWindows(UsageTotals_Reader.Read_Text_Safe(usageFile));

                foreach (var pair in windows)
                {
                    // PER WINDOW, not per file. The file-level gate above keeps a file when ANY of
                    // its windows is live, so a spent five_hour was riding in on a live weekly's
                    // stamp and could still fire an alert about an allowance already handed back.
                    // Same predicate /limits uses, not a second copy of it.
                    if (RateLimits_Reader.Is_ExpiredWindow(pair.Value.WindowResetsAtUtc, nowUtc))
                        continue;

                    if (!maxPercents.TryGetValue(pair.Key, out var known))
                    {
                        maxPercents[pair.Key] = pair.Value;
                        continue;
                    }

                    // The same rule /limits uses, through the same comparison: a newer window
                    // replaces an older one outright, and only readings of the SAME window compete
                    // on percentage.
                    var instance = Limits.WindowInstance_Order.Compare_Instance(pair.Value.WindowResetsAtUtc, known.WindowResetsAtUtc);

                    if (instance > 0 || (instance == 0 && pair.Value.Percent > known.Percent))
                        maxPercents[pair.Key] = pair.Value;
                }
            }

            if (maxPercents.Count == 0)
                return;

            var state = Load_LimitAlertState();

            foreach (var pair in maxPercents)
            {
                state.TryGetValue(pair.Key, out var stored);

                var lastAlerted = Limits.LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(
                    stored.Threshold,
                    stored.WindowResetsAtUtc,
                    pair.Value.WindowResetsAtUtc,
                    pair.Value.Percent);

                var newlyCrossed = Limits.LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull(pair.Value.Percent, lastAlerted);

                // Record the identity even with nothing to say: a re-armed latch that is never
                // written back would be re-derived from an unknown window on every single check,
                // leaving the file permanently mid-migration.
                if (newlyCrossed == null)
                {
                    state[pair.Key] = (lastAlerted, pair.Value.WindowResetsAtUtc);
                    continue;
                }

                state[pair.Key] = (newlyCrossed.Value, pair.Value.WindowResetsAtUtc);

                var alertText = $"⚠️ LIMIT: {Limits.LimitData_Parser.Build_ShortLabel(pair.Key)} {pair.Value.Percent:F0}%";
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

    /// <summary>Shape and migration live in <see cref="Limits.LimitAlertState_Store"/>, where they are testable.</summary>
    Dictionary<string, (double Threshold, DateTime? WindowResetsAtUtc)> Load_LimitAlertState()
    {
        if (!File.Exists(_paths.LimitAlertStateFile))
            return [];

        try
        {
            return new Dictionary<string, (double Threshold, DateTime? WindowResetsAtUtc)>(
                Limits.LimitAlertState_Store.Parse(File.ReadAllText(_paths.LimitAlertStateFile)));
        }
        catch
        {
            // Unreadable state file → re-alert once; harmless.
            return [];
        }
    }

    void Save_LimitAlertState(Dictionary<string, (double Threshold, DateTime? WindowResetsAtUtc)> state)
    {
        File.WriteAllText(_paths.LimitAlertStateFile, Limits.LimitAlertState_Store.To_Json(state));
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

    /// <summary>
    /// Mirrors one append to Telegram. Returns whether the caller may confirm it — TRUE when every
    /// entry reached the phone AND when there was deliberately nothing to send (no client, a
    /// silenced topic, nothing mirrorable); FALSE only when a send actually failed, which leaves
    /// the append unconfirmed so the tailer re-emits it and the retry can happen.
    /// </summary>
    async Task<bool> Mirror_Append_Async(ICompletedChannelAppend append, CancellationToken cancellationToken)
    {
        List<int> supervisorEntryIndexes = [];

        foreach (var entry in append.Entries)
        {
            _log.Log_Info(append.Channel.OrchId, $"[{append.Channel.SpokeName}] entry #{entry.Index} FROM {entry.Author}: {entry.Subject}");

            if (!append.Channel.IsOwnerChannel && entry.Author == ChannelAuthors.Supervisor)
                supervisorEntryIndexes.Add(entry.Index);
        }

        // Only a VERDICT puts the ledger in debt — an answer to work a member filed. This used to
        // arm on ANY supervisor entry in any spoke, so briefing someone started a 90-second
        // countdown to being nudged for not having recorded work that had not happened yet.
        //
        // Judged at each appended entry's own INDEX rather than at the file's tail: the mirror pass
        // runs after the write, so a catch-up burst or an app entry arriving in between left the
        // supervisor's entry no longer last and the verdict was missed entirely.
        if (supervisorEntryIndexes.Count > 0)
        {
            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(append.Channel.FilePath));

            foreach (var index in supervisorEntryIndexes)
            {
                if (!Planning.LedgerHealth_Tracker.Is_VerdictAt(entries, index))
                    continue;

                _lastSupervisorVerdictUtc[append.Channel.OrchId] = DateTime.UtcNow;
                break;
            }
        }

        // File-only mode: there is no phone to reach, so the entries are as delivered as they will
        // ever be. Returning false here would freeze the cursor forever on a machine with no bot.
        if (_telegramClient == null)
            return true;

        var mirrorableEntries = Select_MirrorableEntries(append);

        if (mirrorableEntries.Count == 0)
            return true;

        // TOPIC SILENCE ("I'm at the PC, talking to this supervisor in its terminal"): drop this
        // orchestration's outbound traffic entirely. Unlike DND, nothing is queued for later —
        // the owner is already reading it live in the terminal, and offsets keep advancing.
        if (Is_TopicSilenced(append.Channel.OrchId))
            return true;

        var threadId = await Resolve_ThreadId_OrNull_Async(append.Channel, cancellationToken);

        foreach (var entry in mirrorableEntries)
        {
            // Set when this entry is the answer the owner is waiting for, and ACTED ON only once the
            // send below has succeeded. The wait is not consumed by an attempt.
            var answersTheOwnersWait = false;

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
                }

                // The flag is deliberately NOT cleared here — it is cleared after the send below.
                // Clearing it at this point consumed the owner's wait on an ATTEMPT: when the send
                // then failed, the append was left unconfirmed (by design, so it retries), but the
                // re-emitted entry now read the flag as false, re-evaluated as ordinary narration
                // and was SUPPRESSED. The answer to a question the owner actually asked was dropped
                // silently — they asked, the supervisor replied, and nothing ever reached them.
                answersTheOwnersWait = true;
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

                // ONLY NOW is the owner's wait consumed: everything this entry had to say is on the
                // phone, so what follows is narration again. Anything that threw above skipped this
                // line with the flag still raised, which is what makes the retry deliver the answer
                // instead of re-classifying it.
                if (answersTheOwnersWait)
                {
                    lock (_ownerStateLock)
                    {
                        _ownerAwaitingAnswer.Remove(append.Channel.OrchId);
                    }
                }
            }
            // THE TOKEN DECIDES WHETHER THIS IS A SHUTDOWN, never the exception type — the same rule
            // Run_MirrorLoop_Async already states. HttpClient.Timeout expiry throws
            // TaskCanceledException, which IS an OperationCanceledException, so the bare rethrow this
            // filter replaced let an ordinary network timeout escape Mirror_Append_Async entirely.
            // Settle_MirrorAttempt then never ran, which cost BOTH stamps: no backoff, so the channel
            // re-attempted on the next 2 s tick, and no first-failure time, so the 30-minute give-up
            // never armed. One wedged endpoint re-notified the owner every ~90 s forever.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Error(append.Channel.OrchId, $"Telegram mirror send failed for entry #{entry.Index}", ex);

                // FALSE, not "consumed": the caller leaves this append unconfirmed and the tailer
                // re-emits it, so the entry is retried instead of vanishing. Stopping at the first
                // failure keeps the channel in ORDER, at the price of re-sending any entry of this
                // same append that already landed. A duplicate on the phone is a nuisance; a
                // supervisor's message that never arrives is what the owner reported today.
                return false;
            }
        }

        return true;
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

                var session = request.IsBasic
                    ? _launcher.Start_BasicOrchestration(repo.Name, repo.Path)
                    : _launcher.Start_Orchestration(repo.Name, repo.Path);

                var crew = request.IsBasic
                    ? "One solo session spawned — no supervisor, no implementers; you talk to it directly."
                    : "Supervisor and implementer imp-1 spawned;";

                Append_GeneralAppEntry(
                    $"orchestration '{session.OrchId}' started",
                    $"Orchestration '{session.OrchId}' started on repo '{repo.Name}' ({repo.Path}). {crew} its Telegram topic appears on its first channel entry.");
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

    /// <summary>
    /// A member close is NOT executed on arrival either — it parks for the owner's tap, exactly as an
    /// orchestration close does (owner decision 2026-08-12: this one action, not set-model and not
    /// mute, because those are cheap to undo and this throws away a session's work).
    ///
    /// It used to kill the session tree about two seconds after the file landed, on the say-so of a
    /// request nothing had verified — and <c>Close_Member</c> has no other caller, so there was no
    /// owner-facing route at all. The guard does not take anything away from them; it gives them the
    /// only say they had.
    /// </summary>
    void Process_CloseImplementerRequests(IPendingRequests pending)
    {
        foreach (var request in pending.CloseImplementerRequests)
        {
            try
            {
                var parkedPath = CloseConfirmation_Parking.Park(_paths, request.SourceFilePath);

                _log.Log_Info(request.OrchId, $"close-implementer '{request.MemberId}' held for the owner's confirmation ({parkedPath})");

                Append_OrchestrationAppEntry(
                    request.OrchId,
                    $"close of '{request.MemberId}' HELD — the owner confirms every close with a tap now",
                    $"Nothing has been closed and '{request.MemberId}' is still running. The owner has been asked to confirm.\n\n"
                    + $"Reason relayed: {request.Reason}\n\n"
                    + $"You will get an entry here either way. If they do not answer within {CloseConfirmation_Parking.EXPIRY_HOURS} hours the request lapses and you are told — do NOT re-drop it in the meantime.");
            }
            catch (Exception ex)
            {
                // The guard could not be honoured, so the member is NOT closed. This is the app, not
                // a hook: a hook that cannot evaluate its predicate says so and allows, because it
                // only ever advises — here the effect is destructive and irreversible, so the same
                // failure has to stop rather than wave through. Fail closed, and say why.
                _log.Log_Error(request.OrchId, $"close-implementer '{request.MemberId}' could not be held for confirmation — NOT closed", ex);

                Append_OrchestrationAppEntry(
                    request.OrchId,
                    $"close of '{request.MemberId}' NOT held — nothing was closed",
                    $"Your close request could not be held for the owner's confirmation ({ex.Message}), so it was not acted on and '{request.MemberId}' is still running. Ask again if the close is still wanted.");

                Archive_ResolvedRequest_BestEffort(request.SourceFilePath, "unheld");
            }
        }
    }

    /// <summary>
    /// The ONE member-close execution, reached only from the owner's confirmed tap — the same shape
    /// as <see cref="Execute_Close"/> for an orchestration, so a close cannot come to mean two
    /// different things depending on which door it walked through.
    /// </summary>
    /// <remarks>
    /// It REPORTS and then rethrows, which is the contract <see cref="Execute_Close"/> already has
    /// and the caller already relies on: the confirmed-tap path swallows, because it runs on the
    /// inbound loop with nobody watching. Reporting anywhere but here would mean a failed close is
    /// silent to the one session waiting on it.
    /// </remarks>
    void Execute_CloseImplementer(string orchId, string memberId, string reason)
    {
        try
        {
            _store.Close_Member(orchId, memberId);
            SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_ImplementerPidFile(orchId, memberId));

            Append_OrchestrationAppEntry(
                orchId,
                $"member '{memberId}' closed — {reason}",
                $"'{memberId}' is retired: the owner confirmed it with a tap, its terminal was closed, and its channel stays on disk as audit trail.");
        }
        catch (Exception ex)
        {
            _log.Log_Error(orchId, $"close-implementer '{memberId}' failed after the owner confirmed it", ex);

            Append_OrchestrationAppEntry(
                orchId,
                $"close of '{memberId}' FAILED — it may still be running",
                $"The owner confirmed the close, but it did not complete ({ex.Message}). Check whether '{memberId}' is still alive before asking again.");

            throw;
        }
    }

    /// <summary>
    /// A close request is NOT executed on arrival — it is parked until the owner taps to confirm
    /// (owner directive 2026-08-11, verbatim: "Always confirm with a tap").
    ///
    /// The reason it is a directive: on 2026-08-11 'ai-orchestrator-1' was closed because its
    /// supervisor read "mi serve che chiudi questo" as an instruction to end the whole
    /// orchestration. The request executed within ~2 s of being written, killed every session and
    /// deleted the topic, and nothing on disk could afterwards say who had asked.
    /// </summary>
    void Process_CloseOrchestrationRequests(IPendingRequests pending)
    {
        foreach (var request in pending.CloseOrchestrationRequests)
        {
            // EVERY request parks. There is deliberately no field that can wave one through: the
            // owner's own closes do not arrive here at all, they call Close_Orchestration_ByOwner
            // directly, so nothing in this JSON can claim a confirmation that did not happen.
            try
            {
                var parkedPath = CloseConfirmation_Parking.Park(_paths, request.SourceFilePath);

                _log.Log_Info(request.OrchId, $"close-orchestration held for the owner's confirmation — asked by {request.Requester} ({parkedPath})");

                // The requester is told immediately, because the old contract had it expect to be
                // killed seconds later. A supervisor that posts a farewell and then keeps running
                // with no explanation is a worse failure than the one being fixed.
                Append_OrchestrationAppEntry(
                    request.OrchId,
                    "close request HELD — the owner confirms every close with a tap now",
                    $"Nothing has been closed and your sessions are still running. The owner has been asked to confirm.\n\n"
                    + $"Asked by: {request.Requester}\nReason relayed: {request.Reason}\n\n"
                    + $"You will get an entry here either way. If they do not answer within {CloseConfirmation_Parking.EXPIRY_HOURS} hours the request lapses and you are told — do NOT re-drop it in the meantime, and carry on working.");
            }
            catch (Exception ex)
            {
                // Parking failed, so the guard cannot be honoured — the one thing not to do here is
                // fall through and close it anyway.
                _log.Log_Error(request.OrchId, "close-orchestration could not be held for confirmation — NOT closed", ex);

                // The REQUESTER is told, in its own channel, not the general one: it is the session
                // waiting on this and it would otherwise sit there believing the owner had been
                // asked. And the file is archived rather than deleted, because "nothing on disk says
                // who asked" is the hole this whole unit exists to close — a failure path is exactly
                // where it would have reopened.
                Append_OrchestrationAppEntry(
                    request.OrchId,
                    "close request NOT held — nothing was closed",
                    $"Your close request could not be held for the owner's confirmation ({ex.Message}), so it was not acted on and nothing was closed. Everything is still running. Ask again if the close is still wanted.");

                Archive_ResolvedRequest_BestEffort(request.SourceFilePath, "unheld");
            }
        }
    }

    /// <summary>The owner's own close, straight from the app — see <see cref="IBridgeEngine"/>.</summary>
    public void Close_Orchestration_ByOwner(string orchId, string reason)
    {
        Execute_Close(orchId, reason, "the owner, from the app", "Closed by the owner from the app.");
    }

    /// <summary>
    /// The ONE close execution. Both authorised routes end here — the owner's click in the app and
    /// the owner's tap on a held agent request — so a close cannot come to mean two different things
    /// depending on which door it walked through.
    ///
    /// It reports a failure and then RETHROWS, because the two callers need opposite things. The tap
    /// arrives on a background loop with nobody watching, so it swallows; the owner's click has a
    /// person in front of it who has just answered a modal, and swallowing there told them the
    /// orchestration was closed while its card sat open in front of them. Catching everything here
    /// made the UI's own error handling unreachable — dead code that could never run.
    /// </summary>
    void Execute_Close(string orchId, string reason, string requester, string authorisation)
    {
        try
        {
            // Snapshot BEFORE closing: the topic id is needed after, to delete the topic.
            var session = _store.Get_Session(orchId);
            _store.Close_Orchestration(orchId);
            SessionTerminator.Kill_OrchestrationSessions(_paths, orchId);

            if (_telegramClient != null && session.TelegramTopicId != null)
                Delete_TelegramTopic_FireAndForget(orchId, session.TelegramTopicId.Value);

            Append_GeneralAppEntry(
                $"orchestration '{orchId}' closed — {reason}",
                $"{authorisation} Asked by: {requester}. Sessions ended; folder kept as audit trail; Telegram topic deleted.");
        }
        catch (Exception ex)
        {
            _log.Log_Error(orchId, "close-orchestration failed", ex);
            Append_GeneralAppEntry($"close-orchestration FAILED: '{orchId}'", $"Error: {ex.Message}");

            // Reported, but NOT absorbed — whoever asked has to be able to find out.
            throw;
        }
    }

    /// <summary>
    /// Tells the REQUESTER that its close was not honoured. A request that vanishes without a word
    /// leaves the session that asked believing the owner is still deciding — which is the same
    /// silence the whole guard is meant to remove, arriving through the failure path instead.
    ///
    /// The orch id is recovered with <see cref="OrchestrationRequests_Reader.Peek_OrchId_OrNull"/>,
    /// which reads it best-effort from a file the strict parse has already rejected.
    /// </summary>
    void Report_UnhonouredCloseRequest(string parkedPath, string what, string advice)
    {
        var orchId = OrchestrationRequests_Reader.Peek_OrchId_OrNull(parkedPath);

        if (orchId == null || _store.Get_Session_OrNull(orchId) == null)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"A close request {what} and no orchestration could be named to tell: {parkedPath}");
            return;
        }

        Append_OrchestrationAppEntry(orchId, $"close request {what} — nothing was closed", advice);
    }

    void Archive_ResolvedRequest_BestEffort(string requestFilePath, string outcome)
    {
        try
        {
            CloseConfirmation_Parking.Archive(_paths, requestFilePath, outcome);
        }
        catch (Exception ex)
        {
            // The audit copy is worth having, but never at the price of leaving an executable
            // request file behind to run a second time.
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Could not archive a resolved request ({outcome}): {ex.Message}");
            Delete_RequestFile(requestFilePath);
        }
    }

    /// <summary>
    /// Walks the parked close requests every tick: expires the stale ones, and asks about any that
    /// has no prompt currently live. The second half is what makes a restart safe — the in-memory
    /// prompts are gone but the parked files are not, so the owner is simply asked again instead of
    /// the request either vanishing or executing unconfirmed.
    /// </summary>
    /// <summary>
    /// EXPIRY ONLY, and it runs regardless of Do-Not-Disturb.
    ///
    /// Lapsing a request closes nothing and sends the owner nothing, so it does not belong behind the
    /// mute gate that prompting does. Leaving it there meant DND froze the only thing that disarms a
    /// live button: a request could be asked at 21:00, muted, and still be tappable — and closing —
    /// thirteen hours later, while its requester waited on an answer that had expired silently.
    /// </summary>
    void Expire_StaleCloseConfirmations()
    {
        foreach (var parkedPath in CloseConfirmation_Parking.Find_Parked(_paths))
        {
            if (Is_BeingResolved(parkedPath))
                continue;

            if (CloseConfirmation_Parking.Is_Expired(parkedPath, DateTime.UtcNow))
                Expire_CloseConfirmation(parkedPath);
        }
    }

    async Task Resolve_CloseConfirmations_Async(CancellationToken cancellationToken)
    {
        foreach (var parkedPath in CloseConfirmation_Parking.Find_Parked(_paths))
        {
            // A request the tap handler is part-way through resolving is NOT unasked. Its
            // registrations are already gone (dropped under the lock the moment the owner tapped)
            // but its file is not archived until two awaited Telegram calls later, and "already
            // asked" keys on registrations alone — so this tick used to post a SECOND prompt with
            // fresh buttons for a decision the owner had just made. Those duplicate registrations
            // then pointed at an archived path that only the file scan can clear, so nothing ever
            // cleared them, and a tap on that immortal button closed an orchestration the owner had
            // explicitly refused to close.
            if (Is_BeingResolved(parkedPath))
                continue;

            // An expired request is never re-asked, even if lapsing it failed. Splitting expiry into
            // its own pass dropped the `continue` that used to guarantee this: if the lapse cleared
            // the registrations and then BOTH the archive and its fallback failed, the file stayed
            // parked with nothing registered, and this loop posted a fresh prompt with live buttons
            // to the owner's phone every two seconds. The old single loop produced repeated channel
            // entries; this produced a waterfall (CLAUDE.md item 14).
            if (CloseConfirmation_Parking.Is_Expired(parkedPath, DateTime.UtcNow))
                continue;

            bool alreadyAsked;

            lock (_closeConfirmationLock)
                alreadyAsked = _closeConfirmations.Values.Any(confirmation => confirmation.ParkedPath == parkedPath);

            if (alreadyAsked)
                continue;

            await Ask_OwnerToConfirmClose_Async(parkedPath, cancellationToken);
        }
    }

    /// <summary>
    /// Drops the live confirmation prompts for one orchestration, WITHOUT touching their parked
    /// files. The request stays exactly where it was; only the app's belief that a prompt is
    /// currently out is discarded, so the next sweep asks again. Used when the message carrying the
    /// prompt is destroyed — a `/clear` recreates the whole topic — because a registration pointing
    /// at a message nobody can see is indistinguishable from an unanswered owner.
    /// </summary>
    void Forget_CloseConfirmations_For(string orchId)
    {
        lock (_closeConfirmationLock)
        {
            foreach (var key in _closeConfirmations.Where(pair => pair.Value.OrchId == orchId).Select(pair => pair.Key).ToList())
                _closeConfirmations.Remove(key);
        }
    }

    bool Is_BeingResolved(string parkedPath)
    {
        lock (_closeConfirmationLock)
            return _closeConfirmationsResolving.Contains(parkedPath);
    }

    async Task Ask_OwnerToConfirmClose_Async(string parkedPath, CancellationToken cancellationToken)
    {
        var request = ParkedCloseRequest_Reader.Read_OrNull(parkedPath);

        if (request == null)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Parked close request is unreadable — archived unexecuted: {parkedPath}");
            Archive_ResolvedRequest_BestEffort(parkedPath, "unreadable");
            Report_UnhonouredCloseRequest(parkedPath, "could not be read", "It was archived unexecuted and nothing was closed. Drop a fresh, valid request if the close is still wanted.");
            return;
        }

        var session = _store.Get_Session_OrNull(request.OrchId);

        // Already gone, or closed by the owner from the UI while this waited: there is nothing left
        // to ask about, and asking would offer to close something twice.
        if (session == null || session.ClosedUtc != null)
        {
            _log.Log_Info(request.OrchId, "Parked close request is moot — the orchestration is already closed");
            Archive_ResolvedRequest_BestEffort(parkedPath, "moot");
            return;
        }

        // Same reasoning one level down: a member that is already retired cannot be retired again,
        // and a prompt offering to would invite a tap that kills nothing while reading as if it had.
        if (request.Kind == ParkedCloseKinds.Implementer
            && session.Members.FirstOrDefault(member => member.MemberId == request.MemberId)?.ClosedUtc != null)
        {
            _log.Log_Info(request.OrchId, $"Parked close request is moot — '{request.MemberId}' is already closed");
            Archive_ResolvedRequest_BestEffort(parkedPath, "moot");
            return;
        }

        // No way to ask means no way to confirm, and this guard fails CLOSED: it keeps waiting and
        // eventually lapses. Nothing is closed on a machine that cannot reach the owner.
        if (_telegramClient == null || session.TelegramTopicId == null)
            return;

        // What is being ended mid-flight, named at the moment they decide. It does NOT block the
        // close: a ledger that can refuse to let an orchestration end is the tail wagging the dog,
        // and it is the same shape as every deadlock removed tonight — an enforcement demanding an
        // action some other state forbids. The owner is already tapping; give them the fact.
        //
        // Read only for an ORCHESTRATION close. The ledger belongs to the orchestration, so it says
        // nothing about one member being safe to retire, and the builder deliberately leaves it out
        // of that prompt — reading it here anyway would be work whose only possible use is to
        // mislead.
        var unresolved = request.Kind != ParkedCloseKinds.Orchestration
            ? null
            : Planning.PlanProgress_Formatter.Describe_UnresolvedAtClose_OrNull(
                Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(request.OrchId))));

        var text = CloseConfirmationPrompt_Builder.Build(request, unresolved);

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        var confirmData = $"close-yes-{Guid.NewGuid():N}";
        var declineData = $"close-no-{Guid.NewGuid():N}";

        try
        {
            var messageId = await _telegramClient.Send_MessageWithButtons_Async(
                session.TelegramTopicId,
                text,
                [(confirmData, "✅ Close it"), (declineData, "✋ Keep it open")],
                cancellationToken);

            Remember_TopicMessage(session.TelegramTopicId, messageId);

            lock (_closeConfirmationLock)
            {
                _closeConfirmations[confirmData] = new CloseConfirmation { OrchId = request.OrchId, ParkedPath = parkedPath, Confirms = true, PromptMessageId = messageId };
                _closeConfirmations[declineData] = new CloseConfirmation { OrchId = request.OrchId, ParkedPath = parkedPath, Confirms = false, PromptMessageId = messageId };
            }

            _log.Log_Info(request.OrchId, $"Asked the owner to confirm closing '{request.OrchId}' (asked by {request.Requester})");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Nothing registered, so the next tick asks again. The request stays parked meanwhile.
            _log.Log_Warning(request.OrchId, $"Could not ask the owner to confirm a close: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true when the tap was a close confirmation and has been dealt with, so the generic
    /// button path does not also route it to an agent as a synthetic owner message.
    /// </summary>
    async Task<bool> Try_HandleCloseConfirmationTap_Async(ITelegramApiClient client, ITelegramCallbackTap tap, CancellationToken cancellationToken)
    {
        CloseConfirmation? confirmation;

        lock (_closeConfirmationLock)
        {
            if (!_closeConfirmations.TryGetValue(tap.Data, out confirmation))
                return false;

            // SINGLE-USE, both ways: the first tap retires this prompt's other button too, so a
            // second tap can neither close what was just declined nor decline what is closing.
            foreach (var key in _closeConfirmations.Where(pair => pair.Value.ParkedPath == confirmation.ParkedPath).Select(pair => pair.Key).ToList())
                _closeConfirmations.Remove(key);

            // Held across the awaits below so the 2 s sweep cannot see this request as unasked and
            // post a duplicate prompt while the owner's decision is still being carried out.
            _closeConfirmationsResolving.Add(confirmation.ParkedPath);
        }

        try
        {
            return await Resolve_CloseConfirmationTap_Async(client, tap, confirmation, cancellationToken);
        }
        finally
        {
            lock (_closeConfirmationLock)
                _closeConfirmationsResolving.Remove(confirmation.ParkedPath);
        }
    }

    async Task<bool> Resolve_CloseConfirmationTap_Async(
        ITelegramApiClient client,
        ITelegramCallbackTap tap,
        CloseConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        // A tap is only as good as the request it refers to. Both checks are refusals to close, and
        // both matter because the button outlives everything around it: Telegram keeps it on the
        // owner's phone forever, while the file it points at can be archived, lapsed or expired
        // meanwhile — and under Do-Not-Disturb the sweep that would have lapsed it never ran.
        var stillParked = File.Exists(confirmation.ParkedPath);
        var expired = CloseConfirmation_Parking.Is_Expired(confirmation.ParkedPath, DateTime.UtcNow);

        if (!stillParked || expired)
        {
            try
            {
                await client.Answer_CallbackQuery_Async(
                    tap.CallbackQueryId,
                    stillParked ? "expired — nothing closed" : "already resolved — nothing closed",
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(confirmation.OrchId, $"answerCallbackQuery failed on a stale close button: {ex.Message}");
            }

            _log.Log_Warning(
                confirmation.OrchId,
                $"A close confirmation was tapped after it {(stillParked ? "expired" : "was already resolved")} — NOTHING was closed ({confirmation.ParkedPath})");

            if (expired && stillParked)
                Expire_CloseConfirmation(confirmation.ParkedPath);

            return true;
        }

        try
        {
            await client.Answer_CallbackQuery_Async(tap.CallbackQueryId, confirmation.Confirms ? "closing…" : "kept open", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(confirmation.OrchId, $"answerCallbackQuery failed on a close confirmation: {ex.Message}");
        }

        // Record the decision on the prompt itself BEFORE acting: confirming deletes the topic, and
        // an edit sent afterwards would have nowhere to land.
        if (tap.MessageId != null)
        {
            var decided = confirmation.Confirms
                ? $"⚠️ Close '{confirmation.OrchId}'?\n\n✅ Closed — you confirmed."
                : $"⚠️ Close '{confirmation.OrchId}'?\n\n✋ Kept open — you declined. Its sessions keep running.";

            try
            {
                await client.Edit_MessageText_Async(tap.MessageId.Value, decided, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(confirmation.OrchId, $"Could not record the close decision on the prompt: {ex.Message}");
            }
        }

        // A TAP IS THE OWNER SPEAKING, and this path returns before the generic routing that would
        // otherwise say so. Without it, declining a close woke the supervisor into a session where
        // the awaiting-answer hook denied every tool call until the flag expired — the owner would
        // have said "keep it open" and got a deadlocked supervisor for the answer.
        if (Note_OwnerSpoke_AndWasAway())
            await Exit_AwayMode_Async(cancellationToken);

        Clear_OpenQuestions(confirmation.OrchId);
        Clear_AwaitingAnswerFlag(confirmation.OrchId);

        if (confirmation.Confirms)
            Execute_ConfirmedClose(confirmation);
        else
            Decline_CloseConfirmation(confirmation);

        return true;
    }

    void Execute_ConfirmedClose(CloseConfirmation confirmation)
    {
        var request = ParkedCloseRequest_Reader.Read_OrNull(confirmation.ParkedPath);

        // NON-NEGOTIABLE: a request we cannot read is not authority to end an orchestration. This
        // used to close anyway and record it as "Asked by: unrecorded" — killing every session of an
        // orchestration whose close nobody could produce, which is the precise failure this entire
        // guard exists to prevent, reached through the guard itself.
        if (request == null)
        {
            _log.Log_Warning(
                confirmation.OrchId,
                $"A confirmed close had no readable request — NOTHING was closed, left parked to be re-asked ({confirmation.ParkedPath})");

            // NOT archived. A sharing violation at tap time is transient, and archiving would throw
            // away a close the owner had already approved with no way back. Left parked, this heals
            // itself: the registrations are already gone, so the next sweep asks again — and if the
            // file is genuinely corrupt, Ask_OwnerToConfirmClose_Async has the same null check and
            // files it as unreadable there.
            //
            // Told to the REQUESTER, in its own channel, because that is where this guard promised
            // an answer either way — the general channel cannot be read by the session waiting.
            Append_OrchestrationAppEntry(
                confirmation.OrchId,
                "close NOT executed — the request could not be read just now",
                "The owner's tap arrived, but your request file could not be read at that moment, so nothing was closed. It has been left in place and they will be asked again shortly. Do not re-drop it.");

            return;
        }

        try
        {
            // The kind decides what the tap ends, and it comes from the FILE rather than from
            // anything remembered alongside the button. A prompt that said "member" must never be
            // able to execute an orchestration close because some other state disagreed.
            if (request.Kind == ParkedCloseKinds.Implementer)
            {
                Execute_CloseImplementer(confirmation.OrchId, request.MemberId!, request.Reason);
            }
            else
            {
                Execute_Close(
                    confirmation.OrchId,
                    request.Reason,
                    request.Requester,
                    "The owner confirmed it with a tap.");
            }
        }
        catch (Exception)
        {
            // Already logged and reported to the general channel by Execute_Close. Swallowed HERE
            // because this runs on the inbound loop with nobody watching, and a throw would take the
            // loop down; the owner's own close does the opposite and surfaces it.
        }
        finally
        {
            Archive_ResolvedRequest_BestEffort(confirmation.ParkedPath, "closed");
        }
    }

    void Decline_CloseConfirmation(CloseConfirmation confirmation)
    {
        var request = ParkedCloseRequest_Reader.Read_OrNull(confirmation.ParkedPath);

        // "a close request" while the file is unreadable, and the exact subject when it is not. The
        // requester is told which of its asks was refused; it may have more than one thing running.
        var subject = request == null ? "what you asked to close" : CloseConfirmationPrompt_Builder.Describe_Subject(request);

        _log.Log_Info(confirmation.OrchId, "The owner declined a close request");

        Append_OrchestrationAppEntry(
            confirmation.OrchId,
            "close DECLINED by the owner — keep working",
            $"You asked to close {subject} ({request?.Reason ?? "no reason recorded"}) and the owner said no. Nothing was closed and every session is still running.\n\n"
            + "Do NOT drop the request again. If you believe the work really is finished, say so in one line and let them answer.");

        Report_CloseOutcome_ToGeneral(confirmation.OrchId, "declined by the owner", request);
        Archive_ResolvedRequest_BestEffort(confirmation.ParkedPath, "declined");
    }

    /// <summary>
    /// Every close OUTCOME reaches the general channel, not just the successful ones.
    ///
    /// A close can be asked for by the general supervisor, and the held/declined/lapsed notices go to
    /// the ORCHESTRATION's channel — so when it was the general supervisor that asked, it heard
    /// nothing back and sat waiting on a request that had been refused, or had lapsed twelve hours
    /// earlier. Reporting the outcome here fixes that without having to work out who asked from a
    /// free-text field: a close that the owner refused is orchestration-level news the general
    /// supervisor already tracks, exactly as a completed close is.
    /// </summary>
    void Report_CloseOutcome_ToGeneral(string orchId, string outcome, IParkedCloseRequest? request)
    {
        // A MEMBER close names the member, because "close of 'orch' declined" for a one-member ask
        // reads as the whole orchestration having been up for closure — the general supervisor
        // tracks orchestrations, and it would file the wrong fact.
        var what = request == null || request.Kind == ParkedCloseKinds.Orchestration
            ? $"'{orchId}'"
            : $"'{request.MemberId}' in '{orchId}'";

        Append_GeneralAppEntry(
            $"close of {what} {outcome} — nothing was closed",
            $"Asked by: {request?.Requester ?? "unrecorded"}. Reason given: {request?.Reason ?? "none recorded"}. Its sessions are all still running.");
    }

    void Expire_CloseConfirmation(string parkedPath)
    {
        var request = ParkedCloseRequest_Reader.Read_OrNull(parkedPath);

        lock (_closeConfirmationLock)
        {
            foreach (var key in _closeConfirmations.Where(pair => pair.Value.ParkedPath == parkedPath).Select(pair => pair.Key).ToList())
                _closeConfirmations.Remove(key);
        }

        if (request != null)
        {
            _log.Log_Info(request.OrchId, $"A close request lapsed unanswered after {CloseConfirmation_Parking.EXPIRY_HOURS} h");

            Append_OrchestrationAppEntry(
                request.OrchId,
                $"close of {CloseConfirmationPrompt_Builder.Describe_Subject(request)} LAPSED — the owner never answered",
                $"Your close request sat unanswered for {CloseConfirmation_Parking.EXPIRY_HOURS} hours, so it has expired and nothing was closed. "
                + "It is not carried over: a close must reflect the situation at the moment it is confirmed, not a stale one. Ask again if it still applies.");

            Report_CloseOutcome_ToGeneral(request.OrchId, $"lapsed unanswered after {CloseConfirmation_Parking.EXPIRY_HOURS} h", request);
        }

        Archive_ResolvedRequest_BestEffort(parkedPath, "expired");
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
                    // "left" is an ALIAS, not a second implementation: it is the word the owner used
                    // ("a slash command that lets me know what's left"), and two commands reading one
                    // ledger would drift apart — the second-copy hazard applied to features.
                    else if (command == "progress" || command == "left")
                    {
                        // Answered by the APP straight from PLAN.md — instant, and it works even
                        // while the supervisor is mid-turn (which is exactly when it gets asked).
                        await Send_ProgressReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    // NOT an alias of /progress: the owner asked to KEEP the full detail when the
                    // short form was built, so this is the second RENDERING of the same parse.
                    else if (command == "tasks")
                    {
                        await Send_TaskListReport_Async(client, message.MessageThreadId, cancellationToken);
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
                    ("progress", "What's LEFT to do here (all orchestrations in General)"),
                    ("left", "What's left to do — same as /progress"),
                    ("tasks", "The FULL ledger of this orchestration, done lines included"),
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

    /// <summary>
    /// /tasks — the FULL ledger, done lines included. The owner asked to keep this level of detail
    /// when /progress was shortened: "keep the current level of detail in a NEW command." Shortening
    /// the one command they had would have removed the view rather than moved it.
    /// </summary>
    async Task Send_TaskListReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_TaskListText(messageThreadId);

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_TaskListText(long? messageThreadId)
    {
        if (messageThreadId == null)
            return "ask for /tasks inside an orchestration's topic — the full ledger is per-orchestration";

        var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null)
            return "no orchestration is bound to this topic";

        var progress = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(session.OrchId)));

        if (progress == null)
            return $"{session.DisplayName ?? session.OrchId}: no task ledger yet";

        // The SAME parse the short form reads — two renderings, one reading. Two commands parsing the
        // ledger their own way is how two answers to one question start disagreeing.
        var counts = Build_OrchestrationCountsLine(session.OrchId, session.DisplayName ?? session.OrchId);

        return $"{counts}\n\n{Planning.PlanProgress_Formatter.Describe_EveryLine(progress)}";
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
    /// <summary>
    /// WHAT IS LEFT first, then the counts — the owner asked for "a slash command that lets me know
    /// what's left to do", and this used to answer with up to forty raw ledger lines including
    /// everything already finished. On a 207-line ledger that is a message nobody reads, and their
    /// rule all evening has been that a long message is a useless one.
    /// </summary>
    string Build_OrchestrationLedgerText(string orchId, string displayName)
    {
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(orchId)));

        if (progress == null)
            return $"{displayName}: no task ledger yet — the supervisor writes PLAN.md once you approve a direction";

        return $"{Build_OrchestrationCountsLine(orchId, displayName)}\n\nLEFT:\n{Planning.PlanProgress_Formatter.Describe_Remaining(progress)}";
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
        // The live-window filter is what keeps the "seen from" model list honest too: a five-day-old
        // closed orchestration was still contributing its model name to a report about right now.
        var now = DateTime.Now;
        var windows = RateLimits_Reader.Read_WorstAcrossSessions(RateLimits_Reader.Find_UsageFiles_WithLiveWindow(_paths, now), now);

        if (windows.Count == 0)
            return "no CURRENT limit windows to report — either every window on disk has already reset, or this Claude Code version's status line carries no limit data at all (the automatic alerts read the same probe files, so they are idle for whichever reason applies)";

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

    /// <summary>
    /// ONE status message per topic: posted the first time there is anything to say, then EDITED
    /// forever. It never notifies and never scrolls away, which is why it can be kept current at all.
    ///
    /// Three properties matter and each has a test:
    ///   - a change edits;
    ///   - an IDENTICAL line does nothing, because an edit that writes the same text is a wasted API
    ///     call and, against the 429 limit we already have open on the ledger, a real cost;
    ///   - a RESTART edits the existing message rather than posting a second one — the id is read
    ///     from session.json, not from memory.
    /// </summary>
    async Task Refresh_TopicStatusLines_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null || session.TelegramTopicId == null)
                continue;

            // PER-TOPIC DND AND SILENCE, the same gate nine other outbound sites in this file use.
            // Only the app-wide mute stopped this one. The POST branch is an ordinary sendMessage
            // with no disable_notification, so a topic the owner had explicitly silenced could put
            // a push on their phone the first time it needed a status line. Silenced means
            // DISCARDED, not quiet, and this code drew no distinction.
            _statusLineTextByOrchId.TryGetValue(session.OrchId, out var lastText);
            _statusLineFailedAtByOrchId.TryGetValue(session.OrchId, out var lastFailedAttemptAt);

            // EVERY decision is made in Telegram.TopicStatusLine_Planner, which the suite can reach.
            // This method is left with execution only. Three gates lived here and a reviewer deleted
            // all three at once without reddening a single test — the engine is internal sealed with
            // no InternalsVisibleTo, so nothing decided inside it can be checked.
            var plan = Telegram.TopicStatusLine_Planner.Plan(
                session.DisplayName ?? session.OrchId,
                Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(session.OrchId))),
                Build_TopicStatusMembers(session),
                DateTime.Now,
                session.StatusLineMessageId,
                lastText,
                Resolve_EffectiveMode(session.OrchId),
                _statusLineFailedAtByOrchId.ContainsKey(session.OrchId) ? lastFailedAttemptAt : null,
                MIRROR_RETRY_BACKOFF_SECONDS);

            var action = plan.Action;
            var text = plan.Text;

            if (action == Telegram.TopicStatusActions.None)
                continue;

            try
            {
                // The id re-checked rather than asserted through .Value: the decider guarantees it,
                // but a guarantee that lives in another file is not one the compiler can see.
                if (action == Telegram.TopicStatusActions.Edit && session.StatusLineMessageId != null)
                {
                    await _telegramClient.Edit_MessageText_Async(session.StatusLineMessageId.Value, text, cancellationToken);
                }
                else
                {
                    var messageId = await _telegramClient.Send_Message_Async(session.TelegramTopicId, text, cancellationToken);

                    if (messageId == null)
                        continue;

                    _store.Set_StatusLineMessageId(session.OrchId, messageId.Value);
                }

                _statusLineTextByOrchId[session.OrchId] = text;
                _statusLineFailedAtByOrchId.Remove(session.OrchId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown, not a failure — and THE TOKEN DECIDES, which the unguarded version got
                // wrong. HttpClient.Timeout surfaces as a TaskCanceledException with the token NOT
                // cancelled, so a wedged endpoint was being rethrown as if it were a shutdown: the
                // whole mirror tick aborted, skipping the tailer poll, all agent-to-Telegram
                // mirroring, usage checks, name sync, compaction and the state persist — and it
                // bypassed the backoff stamp below, so the next tick retried the same wedged
                // endpoint immediately, defeating the backoff added in the same commit.
                //
                // Before that change the generic catch below handled a timeout and the tick carried
                // on, which is the behaviour this restores for everything except a real shutdown.
                throw;
            }
            catch (Exception exception) when (Telegram.TopicStatusLine_Decider.Is_MessageAlreadyCurrent(exception.Message))
            {
                // "message is not modified" means the desired state ALREADY HOLDS — success, not
                // failure. Advancing the cache is the whole point: static text is the NORMAL state of
                // an idle orchestration, so without this the tick rejected a call a second, forever.
                // Sync_TopicNames_BestEffort_Async fixed this exact case 150 lines above and its
                // comment says a real failure still must not spin. I took the shape of that catch and
                // inverted its conclusion.
                _statusLineTextByOrchId[session.OrchId] = text;
            }
            catch (Exception exception) when (Telegram.TopicStatusLine_Decider.Is_MessageGone(exception.Message))
            {
                // TERMINAL for this message id: the message it names no longer exists, which is what
                // /clear leaves behind — the topic is torn down and recreated while the id survives in
                // session.json. Retrying could never succeed, so the id is FORGOTTEN and the next tick
                // posts a fresh line. Without this the orchestration never gets a status line again
                // for the life of the machine.
                _store.Clear_StatusLineMessageId(session.OrchId);
                _statusLineTextByOrchId.Remove(session.OrchId);
                _log.Log_Warning(session.OrchId, $"Topic status message is gone — posting a new one next tick ({exception.Message})");
            }
            catch (Exception exception)
            {
                // Never fatal: a status line that cannot be drawn must not stop the mirror. The
                // remembered text is deliberately NOT updated, so the next tick retries — but BACKED
                // OFF, because a 429 answered at the tick rate inverts the cadence from once a minute
                // to thirty times a minute per topic and sustains the throttling that caused it.
                _statusLineFailedAtByOrchId[session.OrchId] = DateTime.Now;
                _log.Log_Warning(session.OrchId, $"Topic status line could not be updated — {exception.Message}");
            }
        }
    }


    /// <summary>
    /// Tells the SUPERVISOR which members have declared themselves idle and stayed that way — the
    /// owner's directive, 2026-08-12: an implementer nobody wants any more "stays open forever
    /// monitoring the channel and wasting tokens".
    ///
    /// It goes to the supervisor's channel because the supervisor is who closes members, and it
    /// stays OFF Telegram because the owner cannot close one. Pushing it to their phone would be
    /// this app forwarding somebody else's job to their lock screen (rule 15); the mirror suppresses
    /// it by subject, which is the same mechanism every other agent-facing app entry uses.
    ///
    /// ONCE PER QUIET SPELL. The set of flagged members is remembered and only a CHANGE speaks, so a
    /// crew that stays idle is mentioned once rather than every two seconds — a reminder that repeats
    /// is a reminder that gets ignored, which would leave the accumulation exactly where it started.
    ///
    /// It never closes anything. Retiring a live member on an inference is the failure this protocol
    /// warns about twice; the decision stays the supervisor's.
    /// </summary>
    /// <summary>
    /// Picks up the marker a hook drops when it cannot evaluate its predicate, records it through the
    /// app's OWN writer, and deletes it.
    ///
    /// The app writes rather than the hook because the log panel is fed by an in-process event a
    /// separate process can never raise — so a hook-written line is invisible until somebody goes
    /// looking, which preserves the very property this exists to remove. Writing it here also means
    /// one rotation threshold and one low-disk rule rather than a copy in the shell that could not
    /// honour the disk half at all.
    ///
    /// DELETED once recorded, so the next inability is a new fact rather than a stale one. If the
    /// record cannot be written the marker STAYS, and the next tick tries again.
    /// </summary>
    void Report_GuardsNotInForce()
    {
        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            var markerFile = Path.Combine(_paths.Get_OrchestrationFolder(session.OrchId), Status.GuardNotInForce_Marker.FILE_NAME);

            if (!File.Exists(markerFile))
                continue;

            var description = Status.GuardNotInForce_Marker.Describe_OrNull(Read_FileText_Safe(markerFile));

            if (description == null)
            {
                File.Delete(markerFile);
                continue;
            }

            try
            {
                // Warning rather than Info: this is a guard the session believes is protecting it and
                // is not. It goes through _log, so rotation and the low-disk drop apply and the UI
                // panel shows it live — which is the whole reason the app writes this and not the hook.
                _log.Log_Warning(session.OrchId, description);

                ChannelAppender.Append_AppEntry(
                    _paths.Get_OwnerChannelFile(session.OrchId),
                    Status.GuardNotInForce_Marker.ENTRY_SUBJECT,
                    $"{description} This is almost always the machine rather than the code — hooks shell out, and a machine that cannot fork cannot run them. Nothing is wrong with your work; the restraint you think you are under is simply not applied right now.",
                    DateTime.Now);

                File.Delete(markerFile);
            }
            catch (Exception exception)
            {
                // The marker deliberately survives: a report that could not be made has not been made.
                _log.Log_Warning(session.OrchId, $"Guard-not-in-force marker could not be reported — {exception.Message}");
            }
        }
    }

    void Flag_IdleMembers()
    {
        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            List<string> idle = [];

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId);

                if (!File.Exists(channelFile))
                    continue;

                var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));

                if (!Status.Retirement_Advisor.Should_SuggestClosing(entries, Nudge_Decider.Has_BeenBriefed(channelFile), DateTime.Now))
                    continue;

                idle.Add($"{member.MemberId} (idle {Status.Retirement_Advisor.Describe_IdleFor_OrNull(entries, DateTime.Now)})");
            }

            var signature = string.Join(", ", idle);

            _flaggedIdleMembersByOrchId.TryGetValue(session.OrchId, out var lastSignature);

            if (signature == (lastSignature ?? ""))
                continue;

            _flaggedIdleMembersByOrchId[session.OrchId] = signature;

            if (idle.Count == 0)
                continue;

            ChannelAppender.Append_AppEntry(
                _paths.Get_OwnerChannelFile(session.OrchId),
                Status.Retirement_Advisor.FLAG_SUBJECT,
                $"{signature} — each declared STANDING BY and has nothing owed. Close what you are finished with: an idle member holds a window, a watcher and a context, and bills for all three. This is a REMINDER, not an instruction — if you still want one of them, keep it and ignore this.",
                DateTime.Now);

            _log.Log_Info(session.OrchId, $"Idle members flagged to the supervisor — {signature}");
        }
    }


    /// <summary>
    /// GATHERS, decides nothing. A closed member's channel is never read — 64% of 3.65 MB per tick —
    /// but it IS handed over marked closed, so the builder's guard is a state the app can produce.
    /// </summary>
    IReadOnlyList<Telegram.TopicStatusMember.ITopicStatusMember> Build_TopicStatusMembers(IOrchestrationSession session)
    {
        List<Telegram.TopicStatusMember.ITopicStatusMember> members = [];

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
            {
                members.Add(Telegram.TopicStatusMember.TopicStatusMember_Factory.Create(member.MemberId, [], isClosed: true));
                continue;
            }

            var entries = ChannelEntry_Parser.Parse_All(
                UsageTotals_Reader.Read_Text_Safe(_paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId)));

            members.Add(Telegram.TopicStatusMember.TopicStatusMember_Factory.Create(member.MemberId, entries, isClosed: false));
        }

        return members;
    }


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
        // ONE copy, in a project the test suite compiles. This switch used to be a duplicate of the
        // card builder's — item 12 — and the pair is why adding a state left three consumers
        // throwing on the happy path with 484 tests green.
        var declaredText = MemberState_Descriptor.Describe(declared);

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

        // A live close confirmation dies with the topic it was posted in. Its registrations would
        // otherwise survive, keeping "already asked" true for a prompt that no longer exists
        // anywhere, so the request sat parked until it lapsed twelve hours later — while the
        // requester had been told the owner was asked. Dropping them here makes the sweep re-ask in
        // the new topic on the next tick, which is the same recovery a restart already relies on.
        Forget_CloseConfirmations_For(session.OrchId);

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

            // THE STATUS LINE IS FORGOTTEN HERE, DETERMINISTICALLY, beside the four resets that were
            // already doing this for everything else the old topic owned.
            //
            // The error-string recovery on the edit path is REACTIVE: it needs a failed edit to fire.
            // An all-idle orchestration builds byte-identical text every tick, so the decider returns
            // None forever, no edit is ever attempted, the predicate never fires — and the recreated
            // topic gets no status line until somebody is next briefed, which in an idle
            // orchestration may be never.
            //
            // Resetting here makes matching on exception.Message a BACKSTOP rather than the
            // mechanism, which is where it belongs: substring-against-a-sentence is the class this
            // repo has now hit four times.
            _store.Clear_StatusLineMessageId(session.OrchId);
            _statusLineTextByOrchId.Remove(session.OrchId);
            _statusLineFailedAtByOrchId.Remove(session.OrchId);

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
        // Close confirmations are the app's OWN decision to act on, so they are resolved here and
        // never fall through to the generic path below, which forwards a tapped label to an agent.
        if (await Try_HandleCloseConfirmationTap_Async(client, tap, cancellationToken))
            return;

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
    ///
    /// THE CADENCE IS THE WALL CLOCK'S, not each orchestration's own. This used to gate on elapsed
    /// time since THIS orchestration's last push, so every topic carried the phase of whenever it
    /// first pushed and the owner got a trickle: "when I have many orchestration sessions open I get
    /// continuously spammed because they are all out of sync". Every topic now fires on the same
    /// :00/:30 tick. `PeriodicStatusSlot_Planner` owns WHEN — out of this class because it is
    /// `internal sealed` with no `InternalsVisibleTo`, so a rule decided in here is unreachable from
    /// the suite; this method keeps only the sending.
    /// </summary>
    async Task Push_PeriodicStatus_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        // ONE reading of the clock for the whole sweep. Taking it per session would let a sweep that
        // straddles a boundary split the batch across two slots — the trickle, in miniature.
        var now = DateTime.Now;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null || session.TelegramTopicId == null)
                continue;

            // No mode gate here: the status now rides the channel, so Normal mirrors it, Deferred
            // queues it (newest only) and Silenced drops it — all handled by the mirror already.
            var plan = PeriodicStatusSlot_Planner.Decide(now, Last_PeriodicStatusSlot_OrNull(session.OrchId));

            if (plan.Action == PeriodicStatusSlotActions.Skip)
                continue;

            // Spent whatever happens below: an Adopt sends nothing, and of the three sending paths
            // one deliberately stays silent. Recording once here is why none of them can fire twice.
            _lastPeriodicStatusSlot[session.OrchId] = plan.SlotStart;

            // First sight — including EVERY orchestration after an app restart, since this store is
            // in-memory. It stays silent so a restart cannot push every topic at once, off-boundary.
            if (plan.Action == PeriodicStatusSlotActions.Adopt)
                continue;

            // Away mode: the owner cannot reply, so this update is their ONLY window into the
            // orchestration — it goes out whether or not the ledger says work is in flight,
            // because "imp-1 is blocked waiting for you" is exactly what they need to know.
            if (Is_AwayMode())
            {
                Post_StatusEntry(session.OrchId, Build_AwayUpdateText(session));
                continue;
            }

            // Nothing running: the supervisor's rule was to stop the cadence, not to report
            // "no change" forever. The slot is spent above, so work starting mid-slot waits for
            // the next boundary like everyone else rather than firing on its own schedule.
            if (!Has_WorkInFlight(session))
                continue;

            Post_StatusEntry(session.OrchId, Build_PeriodicStatusText(session));
        }

        await Task.CompletedTask;
    }

    /// <summary>The slot this orchestration last spent, or null when it has never been seen.</summary>
    DateTime? Last_PeriodicStatusSlot_OrNull(string orchId)
    {
        return _lastPeriodicStatusSlot.TryGetValue(orchId, out var slot) ? slot : null;
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

        // ONE canvas per delivery. The receipt is ALREADY the owner-facing message for this exchange
        // (✓ → ✓✓ → ✓✓ · handoff), and the handoff line has usually just written "Sup: busy" onto
        // it — so sending here stacked a SECOND notification saying what the receipt already said
        // (owner, 2026-08-11). Adopting the receipt makes every later repeat edit that same line,
        // which is the contract; sending survives only as the fallback for a delivery whose receipt
        // never published.
        var canvasMessageId = pending.NarrationMessageId ?? pending.ReceiptMessageId;
        var isReceiptCanvas = canvasMessageId != null && canvasMessageId == pending.ReceiptMessageId;

        try
        {
            // Repeats EDIT the first narration instead of sending another message — one line that
            // keeps counting up, not a column of notifications. Same reasoning as the turn-ended
            // receipt below, which has always worked this way.
            if (canvasMessageId != null)
            {
                // The ✓✓ has to survive the edit: the owner still needs to see their message landed.
                var canvasText = isReceiptCanvas ? $"✓✓  ·  {text}" : text;

                await _telegramClient.Edit_MessageText_Async(canvasMessageId.Value, canvasText, cancellationToken);
                pending.NarrationMessageId = canvasMessageId;
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
            // the next repeat sends a fresh line and starts editing that one instead. A receipt we
            // cannot edit is dead for the turn-ended announcement too, so it goes with it —
            // otherwise the same dead id would be re-adopted as the canvas on every later repeat.
            if (isReceiptCanvas)
                pending.ReceiptMessageId = null;

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
        if (_telegramClient == null)
            return;

        if (Resolve_EffectiveMode(orchId) != TelegramDeliveryModes.Normal)
            return;

        const string TURN_ENDED_TEXT = "✓✓  ·  🔴 Sup: turn ended — free now, he is reading this";

        // No receipt to edit — one failed narration edit is enough to drop the id — so SEND it.
        // The owner's complaint that created this announcement was being left watching a "busy"
        // line that never changed, and a transient Telegram error silently reproducing that exact
        // silence is the same defect wearing a different hat.
        if (pending.ReceiptMessageId == null)
        {
            await Send_DirectReply_BestEffort_Async(_telegramClient, pending.ThreadId, TURN_ENDED_TEXT, cancellationToken);
            return;
        }

        try
        {
            await _telegramClient.Edit_MessageText_Async(pending.ReceiptMessageId.Value, TURN_ENDED_TEXT, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Turn-ended announcement edit failed, sending it instead: {ex.Message}");

            // Same reasoning as above: the signal matters more than which message carries it.
            await Send_DirectReply_BestEffort_Async(_telegramClient, pending.ThreadId, TURN_ENDED_TEXT, cancellationToken);
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


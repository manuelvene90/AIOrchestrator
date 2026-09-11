using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;
using AIOrchestratorCoreLib.Bridge.ChannelChangeWaker;
using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using AIOrchestratorCoreLib.Bridge.PendingAnnouncements;
using AIOrchestratorCoreLib.Bridge.TopicDeletion;
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
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using AIOrchestratorCoreLib.Usage;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using AIOrchestratorCoreLib.Tailing;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Termination;
using AIOrchestratorCoreLib.Time.Clock;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;
using AIOrchestratorCoreLib.Transcription.VoiceTranscriber;
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
    IVoiceTranscriber transcriber,
    IPrintTurnDispatcher printTurns,
    long initialLastUpdateId,
    IEngineStateStore engineStateStore,
    EngineStateSnapshot restoredState,
    IClock clock,
    IBridgeEngineTiming timing,
    Hosting.HostWindowing.IHostWindowing hostWindowing,

    // The OUTBOUND ALLOWANCE the Telegram client spends from, held here only so it can be written
    // into .bridge-state.json beside the cursor (brief F5) — the engine never asks it for a token.
    // Null in file-only mode and on the test seams that hand in their own client.
    Telegram.TelegramSendBudget.ITelegramSendBudget? sendBudget = null) : IBridgeEngine
{
    /// <summary>
    /// WHAT THIS HOST CAN DO WITH WINDOWS, asked rather than assumed. The engine used to call
    /// `WindowFocus.*` — three static classes of unguarded user32/dwmapi/gdi32 P/Invoke — by name,
    /// so `/show` on the Linux daemon threw DllNotFoundException out of the command dispatch and out
    /// of the inbound batch with it, and Telegram re-served every update in that batch four times
    /// (2026-09-08 01:24-01:26Z). See <see cref="Hosting.HostWindowing.IHostWindowing"/>.
    /// </summary>
    readonly Hosting.HostWindowing.IHostWindowing _hostWindowing = hostWindowing;

    /// <summary>
    /// The one line the owner gets for a command this host cannot carry out, and the one log line
    /// that records it.
    ///
    /// <para>
    /// SAID EVERY TIME TO THE OWNER, LOGGED ONCE PER COMMAND. They typed it, so they are owed an
    /// answer each time — silence would read as an app that ignored them. The log is the opposite
    /// case: it is read to find out what this host cannot do, and learns nothing from the tenth copy.
    /// </para>
    /// </summary>
    async Task<bool> Refuse_IfNoWindowing_Async(ITelegramApiClient client, string command, long? messageThreadId, CancellationToken cancellationToken)
    {
        if (_hostWindowing.Is_Supported)
            return false;

        if (_windowingRefusalsLogged.Add(command))
        {
            _log.Log_Warning(
                GLOBAL_ORCH_ID,
                $"/{command} needs a desktop this host does not have ({Environment.OSVersion.Platform} on {Environment.MachineName}) — refused with one line to the owner. Not logged again for /{command}.");
        }

        await Send_DirectReply_BestEffort_Async(
            client, messageThreadId,
            $"🖥 /{command} is not available on this host yet — it needs the machine whose screen the terminals are on.",
            cancellationToken);

        return true;
    }

    readonly HashSet<string> _windowingRefusalsLogged = [];

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
    /// <summary>
    /// THREE MINUTES BEFORE THE APP SAYS "BUSY" — the owner's number, 2026-09-09. It was 45
    /// seconds, which is inside the time a normal turn takes: the sentence therefore appeared under
    /// almost every message they sent, saying what the typing bubble already said. Past three
    /// minutes it is information ("this is taking a while"), and it arrives as an EDIT of the tick.
    /// </summary>
    const int NARRATION_FIRST_DELAY_SECONDS = 180;

    /// <summary>The communicator's "still at it" cadence while the supervisor stays busy.</summary>
    const int NARRATION_REPEAT_SECONDS = 180;

    /// <summary>
    /// How often the typing bubble is refreshed while the owner waits. Telegram clears a chat action
    /// after about five seconds, so anything slower flickers; the tick is 2 s, so this lands on every
    /// other tick.
    /// </summary>
    const int TYPING_REFRESH_SECONDS = 4;

    /// <summary>
    /// How long a nudged, idle session may stay frozen before it is declared ORPHANED. The nudge
    /// changed its channel, so a live watcher fires within seconds — this window is generous
    /// enough that only a genuinely absent listener runs it out.
    /// </summary>
    const int ORPHAN_CONFIRM_MINUTES = 6;

    /// <summary>How far back /log reads to find the whole of the last turn. A turn is tens of events; this is generous.</summary>
    const int TURN_LOG_SCAN_RECORDS = 400;

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

    /// <summary>
    /// HTTP 409 from `getUpdates`: another poller holds this bot token, or a webhook is registered
    /// against it. One situation with one action, which is why it is not left in the generic
    /// failure catch — see <c>Note_InboundConflicted_IfNew_Async</c>.
    /// </summary>
    const int TELEGRAM_CONFLICT_STATUS = 409;
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

    /// <summary>
    /// A whole mirror tick running longer than this IS the finding, not just the trigger for one.
    /// Execute_MirrorTick_Inside_Snapshot_Async's body is a sequential list of ~20 steps, several of
    /// which call Telegram inline, and stage/15's per-message brake
    /// (TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE, 30 s) is AWAITED inside that
    /// sequence — so one message edited twice within 30 s can park the whole tick (the mirror, the
    /// owner's deliveries, the deadline sweep) for up to half a minute. Nothing in the journal could
    /// show that until now: there was no line recording how long a tick took, so the stall was
    /// invisible and would reach the owner only as "the app went quiet", with nothing to attribute it
    /// to. Five seconds, not two: two is the tick's own period, so a tick merely running its own
    /// length proves nothing, while five already means something held it for more than one cycle.
    /// Production evidence for the register this is logged at: build 481efc9, the hour to 22:38 on
    /// 2026-09-10, 388 HTTP 429s, of which 376 were topic status lines re-attempted at the TICK's own
    /// rate against a retry_after of 20–34 s — the downstream shape a parked tick produces.
    /// </summary>
    const int SLOW_TICK_THRESHOLD_MILLISECONDS = 5000;

    /// <summary>Below this age /cost prints no burn rate — dividing by minutes invents a number.</summary>
    const double MINIMUM_BURN_RATE_HOURS = 0.25;

    /// <summary>
    const string GLOBAL_ORCH_ID = "";

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestratorConfigProvider _configProvider = configProvider;
    readonly IOrchestrationSessionStore _store = store;

    /// <summary>
    /// THE TICK'S OWN ROSTER, loaded once at the top of <see cref="Execute_MirrorTick_Async"/> and
    /// dropped when it ends. Null outside a tick, which is what <see cref="Sessions_ThisTick"/> reads
    /// to fall back to the store.
    ///
    /// <para>
    /// WHY: thirteen sweeps inside one tick each asked the store for every orchestration, so a
    /// three-orchestration root enumerated the supervision folder and read three <c>session.json</c>
    /// files thirteen times every two seconds — for a roster that no code between them can change.
    /// </para>
    /// <para>
    /// IT CHANGES NO BEHAVIOUR, and that is not an assumption. A session created or closed WHILE a
    /// tick runs is already only seen by the NEXT tick for every sweep that ran before the change:
    /// the tick is one sequential await chain and the roster it reads is whatever the disk held at
    /// the moment each sweep asked. Fixing the moment to the tick's start moves that boundary by
    /// less than one tick and makes the sweeps agree with each other, which they previously did only
    /// by luck.
    /// </para>
    /// <para>
    /// ONLY THE TICK'S OWN SWEEPS READ IT. Every method that takes this snapshot has exactly one
    /// caller — the tick — so nothing reached from the poll loop (a Telegram command, a request file)
    /// can be handed it. Those keep calling the store, which is the point: a command that has just
    /// created an orchestration must see it, and a snapshot taken by a tick already in flight would
    /// not contain it.
    /// </para>
    /// </summary>
    IReadOnlyList<IOrchestrationSession>? _sessionsThisTick;
    readonly IOrchestrationLauncher _launcher = launcher;
    readonly IOrchestrationLog _log = log;
    readonly IChannelTailer _tailer = tailer;
    readonly ITelegramApiClient? _telegramClient = telegramClient;
    readonly ISessionWatchdog _watchdog = watchdog;
    readonly IVoiceTranscriber _transcriber = transcriber;
    readonly IPrintTurnDispatcher _printTurns = printTurns;

    /// <summary>
    /// Live option buttons, keyed by their callback payload — RESTORED from disk, which is the
    /// change. A keyboard on the owner's phone outlives this process; before the restore, every
    /// question asked before a restart answered "expired" to a tap the owner had every reason to
    /// believe in, and the only way to answer was to notice that and type instead.
    /// </summary>
    readonly Dictionary<string, PendingButtonRecord> _buttonOptions =
        restoredState.PendingButtons.ToDictionary(button => button.Data);

    readonly Queue<string> _buttonOrder = new(restoredState.PendingButtons.Select(button => button.Data));

    /// <summary>"&lt;file&gt;|&lt;line&gt;" of every malformed header already reported — say it once, not every tick.</summary>
    readonly HashSet<string> _reportedMalformedHeaders = [];

    /// <summary>When a channel's mirror first failed, and when it was last attempted — the retry window.</summary>
    readonly Dictionary<string, DateTime> _mirrorRetryFirstFailureUtc = [];
    readonly Dictionary<string, DateTime> _mirrorRetryLastAttemptUtc = [];

    /// <summary>
    /// ENTRIES THE MIRROR GAVE UP ON, held until a send to that channel's topic works again.
    ///
    /// <para>
    /// The give-up used to CONFIRM the append it could not deliver — which moves the persisted
    /// cursor past those entries for ever — and write one Error line into a log on a machine the
    /// owner never reads. From the phone that is indistinguishable from nothing having happened.
    /// </para>
    /// <para>
    /// IN MEMORY, AND BOUNDED (<see cref="Mirroring.UndeliveredDigest_Builder.MAX_PARKED_ENTRIES"/>).
    /// The channel FILE remains the record of record — this is the copy that gets carried to the
    /// phone late, not a second source of truth, so losing it in a restart costs the digest and
    /// nothing else.
    /// </para>
    /// </summary>
    readonly Dictionary<string, List<(DateTime WhenUtc, string Author, string Subject, string Body)>> _parkedUndelivered = [];

    /// <summary>
    /// Every channel whose CONTENTS have been read — by the baseline pass or by either sweep, whichever
    /// reached it first. ONE set on purpose: it was three, and every pair of them left a window where
    /// one consumer had taken first sight and another had not, in which an arriving offence was
    /// absorbed as history by whichever got there second and could never be reported.
    /// </summary>
    readonly HashSet<string> _channelsFirstSighted = [];

    /// <summary>Say a crossing once, and absorb the ones already there when the file was first read.</summary>
    readonly HashSet<string> _screenedIndexCrossings = [];

    readonly Lock _buttonLock = new();

    /// <summary>
    /// Restored, so a group id cannot be reissued to a new question while an old keyboard bearing
    /// it is still on screen — which would let one tap consume another question's siblings. The
    /// per-button counter that used to sit beside this is gone: the payload is a nonce now
    /// (<see cref="CallbackToken"/>), and a counter restarting at zero every launch was half of
    /// what made a stale button dangerous.
    /// </summary>
    long _buttonGroupSequence = restoredState.ButtonGroupSequence;

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
    ///
    /// <para>
    /// IT IS IN <c>.engine-state.json</c> AS A RECORD AND NOT AS A TICKET. Until 2026-09-07 it was not
    /// in that file at all, which is the half nobody had written down: the file held the FIVE maps
    /// <c>Persist_EngineState</c> named and this was a sixth, so while a close, a member-close or a
    /// promotion waited on the owner's tap the state file read <c>"pendingButtons": []</c> and its
    /// mtime did not move. On the VPS on 2026-09-06 that was read as a lost save and cost an evening:
    /// the buttons were on the phone, the file said nothing was pending, and both were correct.
    /// <see cref="CloseConfirmationRecord"/> now says so on disk — WITHOUT the payloads, so nothing
    /// here can be turned back into a live keyboard by accident. The tap on the pre-restart keyboard
    /// still does nothing — <c>Try_HandleCloseConfirmationTap_Async</c> answers only payloads in this
    /// dictionary, which starts empty — and the sweep posts a fresh prompt, which is the one that works.
    /// </para>
    /// </summary>
    readonly Dictionary<string, CloseConfirmation> _closeConfirmations = [];

    /// <summary>
    /// Prompts that were live when the PREVIOUS process stopped, by parked path — read from the
    /// restored snapshot and used for exactly one thing: telling a first ask apart from a re-ask
    /// after a restart in the journal (<c>Describe_AskForTheJournal</c>). NEVER consulted by a tap.
    ///
    /// It is emptied entry by entry as each request is re-asked, so the second prompt of one run does
    /// not keep blaming a restart that happened an hour ago.
    /// </summary>
    /// <remarks>
    /// GROUPED, not <c>ToDictionary</c> straight: this is file content, and a hand-edited or
    /// double-written file with two rows for one path would throw HERE — in a field initialiser, at
    /// construction, taking the whole host down over a journal nicety.
    /// </remarks>
    readonly Dictionary<string, DateTime> _closeConfirmationsFromABygoneProcess =
        restoredState.CloseConfirmations
            .GroupBy(record => record.ParkedPath)
            .ToDictionary(group => group.Key, group => group.First().AskedUtc);

    /// <summary>Parked paths this run has already prompted for at least once. Journal only, as above.</summary>
    readonly HashSet<string> _closeConfirmationsAskedInThisRun = [];

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

        /// <summary>
        /// WHAT THE PROMPT IS ABOUT, copied off the request at ask time rather than re-read at save
        /// time. <see cref="Persist_EngineState"/> runs under two locks on a hot path; opening N
        /// parked files there to describe them would put filesystem latency inside them.
        /// </summary>
        public required string Kind { get; init; }

        public string? MemberId { get; init; }
        public string? Requester { get; init; }

        public DateTime AskedUtc { get; init; }

        /// <summary>Null when the parked file could not be stat'ed — never a guessed deadline.</summary>
        public DateTime? ExpiresUtc { get; init; }
    }

    /// <summary>
    /// Crash-loop alerts taken from the watchdog that could not be delivered yet, and when each was
    /// last attempted. The watchdog's queue is DRAINED by the take, so without this they were simply
    /// gone — see <see cref="Send_CrashLoopAlerts_Async"/>.
    /// <para>
    /// KEYED ON THE ALERT, NOT THE ORCHESTRATION. It was keyed on the orchestration for one release,
    /// which collapsed siblings: one watchdog pass checks the supervisor AND every member, each
    /// registering its own respawn, and what tells them apart — "supervisor of X" versus "imp-2 of
    /// X" — lives in the TEXT that "newest wins" overwrote. A machine-wide cause (a binary off PATH,
    /// a machine that cannot fork) crash-loops every session in lockstep and reaches the threshold on
    /// the same pass, so that is the common case rather than the exotic one, and it is unrecoverable:
    /// the watchdog emits at <c>count != CRASH_LOOP_THRESHOLD</c> — exactly once — and the counter
    /// resets only when that slot comes alive (rev-6 F1, 2026-08-13).
    /// </para>
    /// </summary>
    readonly Dictionary<(string OrchId, string AlertText), CrashLoopAlertHold> _heldCrashLoopAlerts = [];

    /// <summary>
    /// A held crash-loop alert's delivery state: when it was last attempted, and how many attempts it
    /// has cost. ATTEMPTS, not elapsed time, because a meeting or a DND spell holds the alert without
    /// trying — counting wall-clock would let a long meeting spend the budget and drop an alert that
    /// was never once offered to Telegram.
    /// </summary>
    readonly record struct CrashLoopAlertHold(DateTime LastAttemptUtc, int Attempts);

    /// <summary>
    /// How many failed sends a crash-loop alert costs before it is given up. It must TERMINATE: the
    /// hold added for rev-6 F3 turned "one failed send" into a retry with no ceiling, and an alert
    /// that can never be delivered — a closed topic, a revoked token — would otherwise log every
    /// backoff for the life of the app (rev-5, 2026-08-13).
    /// </summary>
    const int CRASH_LOOP_ALERT_MAX_ATTEMPTS = 10;

    /// <summary>
    /// Channels already logged as holding an unterminated trailing entry. Cleared as soon as the
    /// tailer stops reporting one, so the next occurrence speaks again — content-addressed by the
    /// file path, with no token to strand.
    /// </summary>
    readonly HashSet<string> _heldTrailingEntryFiles = [];

    /// <summary>
    /// ONCE PER SPELL, the same shape as _heldTrailingEntryFiles just above: a stall that parks
    /// several ticks in a row (see SLOW_TICK_THRESHOLD_MILLISECONDS) is ONE event on the owner's
    /// screen, not N warnings — a line every 2 s would just replace the 429 storm this exists to
    /// explain with a log storm of its own. There is only one "thing" that can be slow — the tick
    /// itself — so unlike the HashSet above a bool is the whole state: set (and logged) the first
    /// time a tick crosses the threshold, cleared the moment a tick lands back under it, so the NEXT
    /// crossing speaks again.
    /// </summary>
    bool _tickWasSlowLastTime;

    /// <summary>
    /// One alert per stall/budget EPISODE — cleared when traffic resumes (stalls only). Both are
    /// written ONLY after a confirmed send, so an alert nobody received never marks itself delivered.
    /// </summary>
    /// <summary>
    /// WHETHER THE SESSION THAT TALKS TO THE OWNER IS WAITING OUT A USAGE LIMIT rather than waiting
    /// on them.
    ///
    /// <para>
    /// The owner got *"⚠️ … has been waiting on your reply for 25 min"* at 18:38 on 2026-09-09 while
    /// the supervisor was refused for a usage limit and had an APPOINTMENT to resume. Nothing on the
    /// stall path could see it: the dispatcher records the instant in the member's state file
    /// (<c>RetryNotBeforeUtc</c>, written only for a limit that NAMED its reset) and announces it as
    /// an APP entry, which the quiet clock deliberately ignores — so the pause was invisible exactly
    /// where it mattered.
    /// </para>
    /// <para>
    /// SUPERVISOR OR SOLO, whichever this orchestration has: the alert speaks for whoever talks to
    /// the owner, so that is whose pause silences it.
    /// </para>
    /// </summary>
    bool Is_SupervisorPausedForUsageLimit(IOrchestrationSession session)
    {
        if (Is_PausedForUsageLimit(Running.SessionRoles.Supervisor, session.OrchId, Running.SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID))
            return true;

        // A SOLO IS A MEMBER, so its id comes from the roster rather than from a constant — a basic
        // orchestration has no supervisor at all, and the solo is the one that talks to the owner.
        foreach (var member in session.Members)
        {
            if (member.ClosedUtc == null
                && Sessions.MemberKind_Ids.Resolve_Kind(member.MemberId) == Sessions.MemberKinds.Solo
                && Is_PausedForUsageLimit(Running.SessionRoles.Solo, session.OrchId, member.MemberId))
            {
                return true;
            }
        }

        return false;
    }

    bool Is_PausedForUsageLimit(Running.SessionRoles role, string orchId, string memberId)
    {
        try
        {
            var stateFile = Running.PrintSessionState.PrintSessionState_Store.Get_StateFile(_paths, role, orchId, memberId);
            var state = Running.PrintSessionState.PrintSessionState_Store.Read_OrNull(stateFile);

            return state?.RetryNotBeforeUtc != null
                && Limits.DispatchPause_Gate.Is_Paused(state.RetryNotBeforeUtc, _clock.UtcNow);
        }
        catch (Exception ex)
        {
            // A STATE FILE THAT CANNOT BE READ IS NOT A PAUSE. Answering "paused" on an unreadable
            // file would silence the alert for a session that genuinely is stuck, which is the one
            // case it exists for — so the failure direction is to alert, and to say why here.
            _log.Log_Warning(orchId, $"Could not read '{memberId}' state to check for a usage-limit pause ({ex.Message}) — treated as NOT paused");
            return false;
        }
    }

    /// <summary>
    /// WHICH QUESTION each orchestration has already been alerted about — a hash of what the question
    /// SAYS and who asked it, not a bool and not the `[n]`.
    ///
    /// <para>
    /// The owner's ruling, 2026-09-09: the ⚠️ fires *"once per question"*. A bool could only be
    /// re-armed by traffic resuming, which is how the old alert fired three times in one evening
    /// about the same nothing: their own words, of an alert at 17:30 after the supervisor had said
    /// "Nothing more needed from you", and again at 18:38 and 20:25. Keyed on the question, a second
    /// alert needs a second QUESTION rather than merely more silence.
    /// </para>
    /// <para>
    /// IT WAS THE ENTRY INDEX UNTIL 2026-09-10 — the owner's own first wording, corrected by them on
    /// the grounds of decision 12: the `[n]` is agent-written and is a guess unless the writer
    /// re-read the file, and `option-lab-2` really did carry two `[80]` and two `[81]` in one
    /// evening. On a duplicate index the SECOND question was silent (one key, and the alert fires
    /// once per key); across a compaction that renumbered an entry, the SAME question earned a second
    /// alert. Both failures are silent, and both produce exactly the noise this brief removes.
    /// </para>
    /// </summary>
    /// <remarks>
    /// CLEARED WHEN THE OWNER SPEAKS into the orchestration (`Route_OwnerMessage_Async`), which is
    /// what stops "once per question" from meaning "once per WORDING, forever". Agents reuse subjects
    /// and bodies, so without it a byte-identical question asked again hours later hashed to the
    /// remembered key and was never alerted about. Keeping the memo across traffic that does NOT
    /// answer is still right, and that is the case the stall loop itself handles.
    ///
    /// Guarded by `_ownerStateLock` at all three sites: the alert reads and writes it on the tick
    /// thread, the clear runs on the inbound loop.
    /// </remarks>
    readonly Dictionary<string, string> _stallAlertedQuestionKeyByOrchId = [];

    /// <summary>
    /// WHEN THE OWNER LAST REPLIED IN WORDS, per topic — the one fact that lets a newer question
    /// close older ones honestly. With several questions open a typed reply binds to none (the app
    /// will not guess), so they all stayed open, for hours, as "waiting on you". A typed reply
    /// FOLLOWED BY a newer question from the same asker is the evidence that the older ones were
    /// dealt with in prose (owner decision, 2026-09-10: this variant, not "a new question always
    /// closes the old ones" — two genuinely parallel questions with no reply between them stay open).
    /// In memory only: after a restart nothing is superseded until the owner types again, which errs
    /// on the side of leaving a question open.
    /// </summary>
    readonly Dictionary<string, DateTime> _ownerRepliedInWordsUtcByOrchId = [];
    readonly HashSet<string> _budgetAlertedOrchIds = [];
    /// <summary>When each member was nudged — the nudge doubles as the PROBE that proves a watcher exists.</summary>
    readonly Dictionary<string, DateTime> _nudgedMemberUtc = [];

    /// <summary>
    /// The last set of owner-message contract faults coached to each orchestration, so the same
    /// advice is not repeated on every entry of the same shape. Deliberately NOT persisted: a fresh
    /// run deserves to be told again, and this is coaching rather than state anything depends on.
    /// </summary>
    readonly Dictionary<string, string> _lastContractFaults = [];

    /// <summary>
    /// WHICH unanswered thing each member was last nudged about — whatever
    /// `Nudge_Decider.Identify_NudgeSubject` returns for the channel, and NEVER an index or a stamp
    /// (see `Identify_LastConversationEntry_OrNull` for why those two are silent failures: both are
    /// agent-written and neither is unique, so a genuinely new entry can compare equal to a remembered
    /// one and lose the nudge it earned).
    ///
    /// IT IS NOT ALWAYS A CONVERSATION ENTRY'S RAW TEXT, which is what this said until `5f3dc1f` and
    /// was then false for two commits. `Identify_NudgeSubject` answers in three shapes: the last
    /// conversation entry's raw text (the ordinary case), the last entry the app did not write while
    /// WAKING this member, or the `NO_CONVERSATION_YET` sentinel when there is nothing of either kind.
    /// The last two exist because a null identity skipped the gate AND the record together, which was
    /// the loop. Read the value as an OPAQUE KEY: the only property this map needs is that it stops
    /// matching when the thing owed changes, and the sentinel is deliberately constant-per-channel
    /// rather than per-entry for exactly that reason.
    ///
    /// A SECOND DICTIONARY, DELIBERATELY, AND IT IS THE POINT OF THE FIX. `_nudgedMemberUtc` is
    /// ESCALATION state: it dates the nudge so the orphan probe can run six minutes later, and the
    /// probe clears it the moment the member proves alive. Two earlier fixes tried to answer "should
    /// we nudge again?" by changing when that map is cleared or refreshed — one re-arms a nudge, the
    /// other re-arms a RESPAWN — because the nudge gate was borrowing a map that already carried two
    /// meanings. It never needed to: this one has exactly one meaning and drives nothing else.
    ///
    /// NO LONGER LOST ON RESTART. It used to be, and the note here read "costs ONE extra nudge per
    /// member. Visible, cheap, self-correcting" — which was true of ONE restart and false of the
    /// situation that actually produces restarts. The app is closed and reopened to rebuild it, and
    /// every reopen re-nudged every member about a thing it had already nudged them about, on
    /// exactly the channels running longest. It is persisted now; the alternative considered and
    /// still rejected is a third meaning in the map that can respawn a session.
    /// </summary>
    readonly Dictionary<string, string> _nudgedAboutEntry = new(restoredState.NudgedAboutEntry);

    /// <summary>
    /// When this orchestration last incurred a LEDGER DEBT — the due-by signal for PLAN.md.
    ///
    /// TWO EVENTS ARM IT, and it took the second one to make the rule hold for every session:
    ///   - the supervisor posts a VERDICT into a spoke (work happened, the ledger must say so);
    ///   - the OWNER sends a message (they asked for something, the ledger or the OWNER REQUESTS
    ///     table must carry it).
    ///
    /// Only the first existed, and it cannot fire in a BASIC orchestration at all — a solo has no
    /// spokes and posts no verdicts — so a solo was structurally exempt from ledger enforcement. It
    /// then did exactly what an unenforced protocol step gets done: the owner asked for six things
    /// over two hours and the bar read 3/3 throughout (2026-08-14).
    /// </summary>
    /// <summary>
    /// The plan backend in force, loaded from config.json and reloaded only when those settings change.
    /// <see cref="PlanBackendSettings"/> is a value, so the comparison is value equality.
    /// </summary>
    IPlanBackend? _planBackend;
    PlanBackendSettings? _planBackendSettings;
    bool _planBackendLoaded;
    DateTime _planBackendLastSyncUtc = DateTime.MinValue;

    /// <summary>
    /// One plan-backend pass at a time. The pass runs OFF the tick thread (a backend is third-party
    /// code that leaves the machine), and a second one starting while the first is still waiting on a
    /// socket would put two writers on the same PLAN.md.
    /// </summary>
    int _planBackendPassRunning;

    readonly Dictionary<string, DateTime> _ledgerDebtSinceUtc = [];
    readonly HashSet<string> _ledgerBehindReportedOrchIds = [];
    readonly Dictionary<string, string> _reportedLedgerShapeByOrchId = [];

    /// <summary>
    /// Since when NOTHING in this orchestration has been mid-turn — the clock behind the stale
    /// `- [>]` check. Removed the moment anything speaks, so it measures QUIET rather than age.
    /// </summary>
    readonly Dictionary<string, DateTime> _quietSinceUtc = [];

    readonly Lock _stateLock = new();
    readonly IBridgeEngineTiming _timing = timing;
    readonly IOwnerDeliveryBuffer _ownerDeliveryBuffer = OwnerDeliveryBuffer_Factory.Create(timing.OwnerAggregationSeconds);

    /// <summary>
    /// THE CURSOR AS IT WAS LAST WRITTEN TO DISK — the thing a new one has to differ from before the
    /// file is rewritten. Null until the first write of this process, which is why that first write
    /// always happens whatever the cursor holds.
    ///
    /// <para>
    /// WHY A COMPARISON AND NOT A DIRTY FLAG. The persisted offsets are not a field anyone assigns:
    /// <see cref="IChannelTailer.Get_OffsetsSnapshot"/> DERIVES each one, per file, as the cursor
    /// minus the bytes that are pending and the bytes that are unconfirmed. Three moving parts, in a
    /// dozen mutation sites inside the tailer's poll, and a flag missing from any one of them is a
    /// cursor that silently stops being saved — the one failure this file's own class remark calls a
    /// silent one-way hole in the mirror. A comparison cannot be incomplete: it asks the same
    /// question the file answers.
    /// </para>
    /// <para>
    /// IT COSTS A DICTIONARY WALK OVER THE OPEN CHANNELS and saves an atomic file write — a temp file,
    /// a flush and a rename — on every tick that mirrored nothing, which on a quiet orchestration is
    /// most of them. The tick was rewriting this file thirty times a minute to store bytes identical
    /// to the ones already there.
    /// </para>
    /// </summary>
    IReadOnlyDictionary<string, long>? _persistedOffsets;
    long _persistedUpdateId;

    /// <summary>
    /// Announcements whose channel was locked. These are the one class of write a return check
    /// cannot save — they fire on the EDGE, with the transition already recorded in the mode state,
    /// so there is no memo to withhold. See <see cref="IPendingAnnouncements"/>.
    /// </summary>
    readonly IPendingAnnouncements _pendingAnnouncements = PendingAnnouncements_Factory.Create();
    readonly Dictionary<string, (string OrchId, long? ThreadId)> _deliveryTargets = [];
    readonly Lock _deliveryLock = new();

    /// <summary>Topic name last pushed to Telegram, so the glyph sync only calls the API on a real change.</summary>
    readonly Dictionary<string, string> _appliedTopicNames = [];

    /// <summary>
    /// When the belief above was last thrown away on purpose. It is a BELIEF and never an
    /// observation — the sync skips any topic whose wanted name matches it, and nothing reads the
    /// real name back from Telegram — so once it is wrong it is wrong for the life of the process.
    /// There are at least three routes to that: a refused push that the sync's own catch records as
    /// applied, a name the owner edits by hand in the client, and a topic recreated underneath us.
    ///
    /// The owner has been hitting the consequence often enough to ask for a manual escape
    /// (2026-08-25, of a ❓ that survived their answer: *"It happens so often that the question mark
    /// gets stuck"*). /refresh is that escape; this is the half that means they need it less. A
    /// periodic forget costs one editForumTopic per topic per interval — Telegram answers
    /// TOPIC_NOT_MODIFIED when the name is already right, which the gate already classifies as
    /// Applied — and buys a divergence that repairs itself instead of lasting until a restart.
    /// </summary>
    readonly Dictionary<string, DateTime> _topicNameRevalidatedUtc = [];

    /// <summary>
    /// Long enough that the steady state is one no-op call per topic per five minutes, short enough
    /// that a stuck glyph corrects itself well before the owner has finished being annoyed by it.
    /// </summary>
    const int TOPIC_NAME_REVALIDATE_MINUTES = 5;

    /// <summary>
    /// ONE TOPIC-NAME SYNC AT A TIME. The sync is reached from BOTH long-running loops — the mirror
    /// tick and the inbound command loop, started side by side under Task.WhenAll — and it is a
    /// read-compare-push-record sequence over a Dictionary nothing guarded. Interleave two of them
    /// and the record stops describing the push: one loop's edit reaches Telegram last while the
    /// other's name is what the map ends up holding. From then on the guard reads "already applied"
    /// about a name Telegram never received, and because the guard's whole job is to skip, the topic
    /// can never be corrected — every later tick agrees there is nothing to do.
    ///
    /// The owner, 2026-08-24, on a topic switched back to Normal six minutes earlier with no sync
    /// error logged: *"the topic still has the moon even though I removed the DND"*.
    ///
    /// A gate rather than a lock on the dictionary, because locking the map alone would leave the
    /// ORDER free — and the order is the defect. Held across the Telegram calls on purpose: this is
    /// a best-effort path that already runs sequentially, and a command's sync waiting for a tick's
    /// costs a moment and then re-reads the store, so it pushes fresher state rather than staler.
    /// </summary>
    readonly SemaphoreSlim _topicNameSyncGate = new(1, 1);

    /// <summary>
    /// WHEN A TOPIC NAME MAY BE ATTEMPTED AGAIN after an attempt whose outcome we could not learn.
    ///
    /// A SECOND DICTIONARY, DELIBERATELY, AND IT IS THE POINT OF THE FIX — the same move
    /// <see cref="Status.Nudge_Decider"/> records for `_nudgedAboutEntry`, whose docstring notes that two
    /// earlier attempts failed because "the nudge gate was borrowing a map that already carried two
    /// meanings". `_appliedTopicNames` carried two: *this name is applied* and *do not retry this now*.
    /// The entry guard read both from one value, so every failure had to be forced into one of them and a
    /// transport failure — which tells us NOTHING — was recorded as if it had told us the name applied.
    /// Choosing which of the two meanings to get wrong is not a predicate problem and no predicate fixes
    /// it. This map has exactly one meaning and drives nothing else.
    ///
    /// Lost on restart, which costs one extra attempt per orchestration. Visible, cheap, self-correcting.
    /// </summary>
    readonly Dictionary<string, DateTime> _topicNameRetryAfterUtc = [];

    /// <summary>
    /// The status-line text last WRITTEN to each topic, so an unchanged line costs no API call.
    /// In memory on purpose, unlike the message id: after a restart the first tick edits once with
    /// whatever is current, which is correct, and the id — the thing that must not be lost — lives
    /// in session.json.
    /// </summary>
    readonly Dictionary<string, string> _statusLineTextByOrchId = [];

    /// <summary>
    /// The idle member SET last flagged, per orchestration, so the reminder is written ONCE per quiet
    /// spell rather than every tick.
    ///
    /// It is REPLACED on every change, never cleared — and the difference is the whole mechanism. The
    /// EMPTY set is stored like any other, which is what resets this when everyone goes back to work
    /// and makes the next idle spell news again. A reader who took "cleared when the set changes"
    /// literally — as the earlier wording here said — would move or drop that store and silently
    /// suppress every second idle spell for the life of the process.
    ///
    /// The value is the member set, NEVER the rendered line: a duration inside the key changes every
    /// minute and never matches itself, which produced 151 flags in six hours on 2026-08-13.
    /// </summary>
    readonly Dictionary<string, string> _flaggedIdleMembersByOrchId = [];

    /// <summary>
    /// The progress artefact last written per orchestration, so an unchanged ledger costs no disk
    /// write. Same shape and same reasoning as <see cref="_statusLineTextByOrchId"/> above: in memory,
    /// so the first tick after a restart rewrites once with whatever is current.
    /// </summary>
    readonly Dictionary<string, string> _progressArtefactByOrchId = [];

    /// <summary>
    /// The last guard-not-in-force marker reported per orchestration, and when. Keyed on the marker's
    /// CONTENT rather than on a rendered line, for the reason the field above learned the hard way:
    /// a key with a moving value in it never matches itself.
    /// </summary>
    sealed class GuardReportRecord
    {
        public string MarkerText = "";
        public DateTime ReportedAt;
    }

    readonly Dictionary<string, GuardReportRecord> _reportedGuardsByOrchId = [];

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

    /// <summary>
    /// The OWNER'S OWN message currently wearing 👀, per thread — brief D's receipt (2026-09-09:
    /// "acknowledge my messages with a reaction on my bubble instead of a ✓ message").
    ///
    /// <para>
    /// Beside <see cref="_receiptMessageIdByThread"/> rather than replacing it, because the two
    /// hold DIFFERENT message ids and only one of them exists at a time: this one is the owner's
    /// message, and that one is a bot message sent only when the reaction was refused. Reusing a
    /// single dictionary would mean an id whose meaning depends on which path wrote it, and the
    /// pickup would then edit the owner's own message instead of reacting to it.
    /// </para>
    /// </summary>
    readonly Dictionary<long, long> _reactedOwnerMessageIdByThread = [];

    readonly Lock _receiptLock = new();

    /// <summary>
    /// Message ids KNOWN to belong to each topic (ours + the owner's), for /clear. Telegram message
    /// ids are chat-wide, not per topic, so deleting a computed RANGE would wipe other topics —
    /// only ids observed in this topic may ever be deleted. Key 0 = the General topic.
    /// </summary>
    readonly Dictionary<long, List<long>> _knownMessageIdsByThread = [];
    readonly Lock _knownMessageIdsLock = new();

    /// <summary>
    /// The NEWEST message id seen in each topic and when it was seen — the two facts the status-line
    /// planner needs to know whether its message has been buried, and whether the topic has since
    /// gone quiet. Written by the same one method that records every id, so nothing can be recorded
    /// as known without also being recorded as newest. Key 0 = the General topic.
    ///
    /// IN MEMORY ON PURPOSE, not in session.json. It is a fact about a conversation that is still
    /// happening; after a restart the planner is told nothing rather than something stale, and it
    /// answers that by editing in place until the first message repopulates this.
    ///
    /// The STATUS LINE'S OWN message is deliberately absent — it is posted through
    /// Refresh_TopicStatusLines_Async, which does not record it. It must not count as traffic that
    /// buries itself.
    /// </summary>
    readonly Dictionary<long, Telegram.TopicStatusLine_Planner.TopicNewestMessage> _newestTopicMessageByThread = [];

    /// <summary>
    /// Orchestrations whose status line can never be MOVED, because Telegram refused to delete it —
    /// past the 48-hour deletion window, or without `can_delete_messages`. A refusal is permanent for
    /// that message, and it is not a gone message, so nothing else clears it: without this latch the
    /// delete throws ahead of the send on every tick and starves the edit with it, leaving the line
    /// buried AND stale where before the repost existed it was merely buried.
    ///
    /// Latched, the topic keeps editing its line in place — master's behaviour, which is the right
    /// floor to degrade to.
    ///
    /// It is CLEARED whenever the message it applies to stops existing: `/clear` recreates the topic,
    /// and a message reported gone is replaced by a fresh post. The 48-hour reason dies with the old
    /// message, so a new one deserves one attempt. In-memory for the same reason — a restart retries
    /// once, and a permission granted meanwhile takes effect without anybody remembering to say so.
    /// </summary>
    readonly HashSet<string> _repostImpossibleOrchIds = [];

    /// <summary>
    /// Owner messages handed over and NOT yet answered by their supervisor. Tracked so the owner
    /// always learns what became of what they sent — the typing bubble while it is being worked on,
    /// a nudge if the supervisor goes idle without replying.
    /// </summary>
    readonly Dictionary<string, PendingOwnerReply> _pendingOwnerReplies = [];

    /// <summary>
    /// Last typing refresh per topic, so the bubble is re-sent on <see cref="TYPING_REFRESH_SECONDS"/>
    /// rather than on every tick. Guarded by <see cref="_ownerStateLock"/>: delivery can run from the
    /// inbound loop (GO) and the refresh runs on the mirror tick.
    /// </summary>
    readonly Dictionary<long, DateTime> _lastTypingSentUtcByThread = [];

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

    /// <summary>
    /// How long an unanswered question freezes the conversation. Long enough to make "a question
    /// stops the turn" real; short enough that an owner who never answers is not starved of
    /// everything else the orchestration has to say.
    /// </summary>
    const int QUESTION_HOLD_CAP_MINUTES = 10;

    /// <summary>
    /// Presence of this file in an orchestration folder stops its supervisor dead. The name is
    /// FORWARDED from the marker rather than restated: two copies of a filename that a bash hook
    /// also hard-codes is one drift away from a block nothing can clear.
    /// </summary>
    public const string AWAITING_ANSWER_FLAG_FILE = Status.AwaitingAnswerFlag_Marker.FILE_NAME;

    /// <summary>Members waiting on a verdict, one id per line — read by the awaiting-answer hook.</summary>
    public const string AWAITING_VERDICT_FILE = ".awaiting-verdict";

    /// <summary>How long EVERYTHING must be idle before a suppressed last word is released.</summary>


    /// <summary>
    /// Orchestrations where the owner has spoken and the supervisor's reply has NOT yet been pushed.
    /// Its whole purpose is to guarantee an answer always reaches them, so it is owned by the mirror
    /// path alone — sharing _pendingOwnerReplies for this dropped every answer.
    /// </summary>
    readonly HashSet<string> _ownerAwaitingAnswer = [.. restoredState.OwnerAwaitingAnswer];

    /// <summary>
    /// Telegram message id → a question the owner has NOT answered yet, restored across a restart.
    ///
    /// <para>
    /// The record carries the button group so an answer that did NOT come through that keyboard can
    /// still take it down: a tap knows its group from the ticket it arrived on, a typed answer knows
    /// only the orchestration, and without this it had no route back to the buttons it had just
    /// answered. It also carries the deadline and default — see <see cref="OpenQuestionRecord"/>.
    /// </para>
    /// </summary>
    readonly Dictionary<long, OpenQuestionRecord> _openQuestions =
        restoredState.OpenQuestions.ToDictionary(question => question.MessageId);

    /// <summary>How many closures are remembered for the diagnostic below before the oldest is dropped.</summary>
    const int CLOSED_QUESTION_MEMORY = 64;

    /// <summary>
    /// WHY EACH RECENTLY CLOSED QUESTION IS NO LONGER OPEN — a diagnostic, and nothing reads it to
    /// decide anything. Written under <c>_ownerStateLock</c> beside every removal from
    /// <see cref="_openQuestions"/>, so the answer can never be inferred from a registry that has
    /// already forgotten the question.
    ///
    /// <para>
    /// IT EXISTS BECAUSE A LOG LINE ASSERTED THE OPPOSITE OF THE TRUTH. A lapsed high-risk read-back
    /// said "the question is still open" without ever reading the registry; on 2026-09-09 at ~16:03Z
    /// it said that about a question that had been stamped closed minutes before. See
    /// <see cref="QuestionClosure_Wording"/>.
    /// </para>
    /// <para>
    /// IN MEMORY ONLY, AND BOUNDED. It is not in the snapshot because nothing depends on it: a
    /// closure from before a restart is reported as unrecorded, which is true and is still better
    /// than the confident wrong sentence it replaces.
    /// </para>
    /// </summary>
    readonly Dictionary<long, string> _closedQuestionReasons = [];

    readonly Queue<long> _closedQuestionOrder = new();

    /// <summary>
    /// How many handled updates and taps are remembered. One long-poll batch is at most 100 updates,
    /// so this holds several batches — far more than a replay can ever span, and small enough that
    /// remembering it costs nothing.
    /// </summary>
    const int HANDLED_UPDATE_MEMORY = 512;

    /// <summary>
    /// THE UPDATES THIS PROCESS HAS ALREADY ACTED ON. `_lastUpdateId` advances once per BATCH, so
    /// anything that ends a batch early — a crash, or, before this stage, any escaped exception —
    /// makes Telegram re-serve every update in it. On 2026-09-08 01:24-01:26Z a Windows-only window
    /// call threw DllNotFoundException on the Linux daemon and one batch was replayed four times.
    ///
    /// <para>
    /// AT MOST ONCE IS THE CHOICE, and it is the owner's side that decides it: a replayed owner
    /// message is a second copy of their words in the channel and a second answer from the
    /// supervisor, while an update dropped after a failed handler is one loud Error line naming the
    /// update. The second is recoverable by asking again; the first corrupts the conversation.
    /// </para>
    /// <para>
    /// IN MEMORY, DELIBERATELY. Persisting it would make the offset and this set two sources of
    /// truth for the same question across a restart; the offset is already durable, and this set
    /// exists for the window in which it is not yet.
    /// </para>
    /// </summary>
    readonly HashSet<long> _handledUpdateIds = [];

    readonly Queue<long> _handledUpdateOrder = new();

    /// <summary>
    /// The same, for TAPS, keyed by <c>callback_query.id</c> rather than the update id — Telegram's
    /// own identity for the gesture. A tap acted on twice is a decision taken twice, and the
    /// decisions that reach here include pushes and deploys.
    /// </summary>
    readonly HashSet<string> _handledTapIds = [];

    readonly Queue<string> _handledTapOrder = new();

    readonly object _handledLock = new();

    bool Was_UpdateHandled(long updateId)
    {
        lock (_handledLock)
            return _handledUpdateIds.Contains(updateId);
    }

    void Note_UpdateHandled(long updateId)
    {
        lock (_handledLock)
        {
            if (!_handledUpdateIds.Add(updateId))
                return;

            _handledUpdateOrder.Enqueue(updateId);

            while (_handledUpdateOrder.Count > HANDLED_UPDATE_MEMORY)
                _handledUpdateIds.Remove(_handledUpdateOrder.Dequeue());
        }
    }

    bool Was_TapHandled(string callbackQueryId)
    {
        lock (_handledLock)
            return _handledTapIds.Contains(callbackQueryId);
    }

    void Note_TapHandled(string callbackQueryId)
    {
        lock (_handledLock)
        {
            if (!_handledTapIds.Add(callbackQueryId))
                return;

            _handledTapOrder.Enqueue(callbackQueryId);

            while (_handledTapOrder.Count > HANDLED_UPDATE_MEMORY)
                _handledTapIds.Remove(_handledTapOrder.Dequeue());
        }
    }

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

    /// <summary>
    /// Per orchestration: the away digest last SENT, so an identical one is never sent again.
    /// Guarded by _ownerStateLock — written from the mirror loop and cleared from the inbound one.
    /// See <see cref="AwayDigest_Decider"/> for the 30-minute loop this ends.
    /// </summary>
    readonly Dictionary<string, string> _lastAwayDigestByOrchId = [];

    /// <summary>Which stale-in-progress SET was last reported, so a fix to one line still leaves the rest heard.</summary>
    readonly Dictionary<string, string> _reportedStaleInProgress = [];

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

        /// <summary>
        /// The receipt for this exchange was a REACTION on the owner's own message, so there is no
        /// bot message to edit and there never was one (brief D).
        ///
        /// <para>
        /// It exists to keep two different "no canvas" states apart, which the narration paths
        /// otherwise cannot: an edit that FAILED (the id is cleared, and a message here would be a
        /// SECOND one about the same state — the owner's 2026-09-09 "never a second message"), and
        /// a receipt that was never a message at all (a message here is the FIRST one). Reading
        /// them the same way is what silently switched off the ≥3-minute busy narration for every
        /// orchestration the moment the tick became a reaction.
        /// </para>
        /// </summary>
        public bool ReceiptWasReaction;
        public int OwnerAnswerCountAtDelivery;
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

        /// <summary>
        /// Whether the SESSION has been told the owner is waiting while it stayed mid-turn. One per
        /// pending reply, and deliberately not <see cref="Nudged"/>: that one arms orphan recovery,
        /// which would kill a healthy busy session.
        /// </summary>
        public bool BusyNoticeWritten;

        /// <summary>
        /// The session has written back, so the owner is no longer UNANSWERED — but the work they
        /// asked for is very likely still running, so the tracker stays alive to catch the turn
        /// ending. Before this the first reply DELETED the tracker, which is why a small job
        /// finished in silence: the role commands order a receipt BEFORE the work starts
        /// ("ANSWER THE OWNER BEFORE YOU WORK"), so the tracker was always gone before the merge
        /// had even begun. The owner, 2026-08-25: *"If I say to do merge, it does it, then the
        /// terminal completes the operation and stops, and I haven't received anything telling me
        /// 'done'."*
        /// </summary>
        public bool Answered;
    }

    long _lastUpdateId = initialLastUpdateId;
    DateTime _lastLimitCheckUtc = DateTime.MinValue;

    readonly IEngineStateStore _engineStateStore = engineStateStore;

    /// <summary>
    /// Injected for the deadline sweep and the dispatcher pause ONLY — see <see cref="IClock"/> for
    /// why this is not adopted across the file's other hundred-odd clock reads.
    /// </summary>
    readonly IClock _clock = clock;

    /// <summary>
    /// High-risk decisions the owner has TAPPED and not yet confirmed by typing the code back.
    ///
    /// <para>
    /// A LIST, NOT A MAP KEYED BY ORCHESTRATION, and the map was a defect. Keyed by orchestration it
    /// documented "newest wins" — but the older question's message is still on screen showing ITS
    /// code, its buttons are already consumed, and it is still open. So: two high-risk questions in
    /// one topic, tap both, scroll back and type the first code, and the answer is "that is not the
    /// code" for a decision the app itself put on the screen. Several can be live at once and the
    /// typed code is matched against all of them in that topic — the codes are distinct, so there is
    /// no ambiguity to resolve.
    /// </para>
    /// <para>
    /// Guarded by _ownerStateLock, like the open questions it shadows.
    /// </para>
    /// </summary>
    readonly List<PendingConfirmationRecord> _pendingConfirmations = [.. restoredState.PendingConfirmations];

    /// <summary>
    /// When the dispatcher may start work again, or null while it is running. Persisted, because a
    /// pause the app forgets on restart is a pause that lifts by crashing — and a limit that
    /// crash-loops the sessions is exactly the situation in which the app gets restarted.
    /// </summary>
    DateTime? _dispatchPausedUntilUtc = restoredState.DispatchPausedUntilUtc;
    string? _dispatchPauseReason = restoredState.DispatchPauseReason;

    /// <summary>
    /// When the pause last read the usage probes. ITS OWN STAMP, not the alert scan's: that one is
    /// only advanced on ticks where the alert scan actually runs, and the alert scan returns early
    /// while muted — so sharing it would make the pause check fire every 2 s under DND and once a
    /// minute otherwise. Two consumers, two clocks, no coupling between a mute and a spend.
    /// </summary>
    DateTime _lastDispatchPauseCheckUtc = DateTime.MinValue;

    /// <summary>App-wide Do-Not-Disturb: everything is kept and replayed when it goes off.</summary>
    volatile bool _telegramMuted;

    /// <summary>App-wide silence: everything is DROPPED while it lasts (the owner works at the PC).</summary>
    volatile bool _silenceAllTopics;

    public event Action<string>? OrchestrationActivity;
    public event Action<bool>? MutedChanged;
    public event Action<bool>? SilenceAllChanged;

    /// <summary>
    /// Turns the periodic status's screenshots on or off, APP-WIDE and persisted — the owner asked
    /// for it to "work app wise, independently from where I place the command", so it lives in
    /// config.json rather than on any one orchestration.
    /// </summary>
    public void Set_StatusScreenshots(bool enabled)
    {
        var current = _configProvider.Get_Current();

        if (current.TelegramStatusScreenshots == enabled)
            return;

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Factory.Create_WithStatusScreenshots(current, enabled), _paths);

        _log.Log_Info(GLOBAL_ORCH_ID, enabled
            ? "Status screenshots ON — the periodic status carries a picture of each session's terminal"
            : "Status screenshots OFF — the periodic status is text only");
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
        // The channel lock now mediates every write in the system and had no voice at all: a wedged
        // channel was silent, a broken lock was silent, and the bool that says "this write did not
        // happen" is discarded by almost every call site. Its failures go to the log from here on —
        // decision 21's rule (the line goes to orchestrator.log.jsonl, which the app tails) applied
        // to the thing every append now passes through.
        ChannelLock_Diagnostics.Set_Sink(message => _log.Log_Warning(GLOBAL_ORCH_ID, message));

        GeneralChannel_Initializer.Ensure_Exists(_paths);

        List<Task> loops = [Run_Supervised_Async("mirror", Run_MirrorLoop_Async, cancellationToken)];

        if (_telegramClient != null)
            loops.Add(Run_Supervised_Async("inbound", Run_InboundLoop_Async, cancellationToken));

        // NOT A LOOP AND NOT AWAITED. It runs once, works through whatever the last process could
        // not delete, and ends — so it is neither supervised nor part of the WhenAll below: a bridge
        // that would not start until Telegram answered a housekeeping delete would be a bridge the
        // owner loses whenever Telegram is slow.
        _ = Task.Run(() => Sweep_PendingTopicDeletes_Async(cancellationToken), cancellationToken);

        _log.Log_Info(GLOBAL_ORCH_ID, _telegramClient == null
            ? "Bridge started (file-only mode — Telegram not configured)"
            : "Bridge started (Telegram mirror + inbound routing active)");

        try
        {
            await Task.WhenAll(loops);
        }
        finally
        {
            // ONE LAST DRAIN. Announce no longer writes, so anything queued when the loops stop would
            // otherwise die with the process — the one real cost of making the drain the single
            // writer. This does not eliminate that cost: an announcement made while this final drain
            // is itself blocked is still lost. It narrows it to the exposure the process already has
            // for any write in flight when it dies, rather than adding a new one.
            //
            // In a finally so it runs on the cancellation path too, which is the ordinary way this
            // method ends.
            Drain_PendingAnnouncements();

            // In-flight print turns die with the bridge (process trees killed); their state was not
            // advanced, so the same entries are pending at the next start.
            await _printTurns.Stop_Async();

            // THE LAST WRITE, FORCED. Every other call skips a cursor identical to the one on disk,
            // which is right thirty times a minute and wrong exactly once: if the remembered cursor
            // has drifted from the file for any reason, no later tick exists to correct it. The
            // write costs nothing here and what it protects against is BridgeState_Store's silent
            // one-way hole — entries appended before the next start never mirrored at all.
            Persist_BridgeState(force: true);

            // The buffered turn-log lines are the trace of the turns that were running when the app
            // stopped, which is the tail most worth having. Same guarantee as every append: it never
            // throws, because losing the tail is never worth failing the shutdown.
            Running.TurnLog.TurnLog_Store.Flush_All();
        }
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

                // The loudest thing this app can say: nothing else will reach them until they act.
                TelegramSendSounds.Rings,
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
        // OWNED BY THE LOOP, so it dies with it: this method is relaunched by Run_Supervised_Async after
        // a fault, and a watcher outliving the loop that reads it would be a handle nobody wakes.
        using var waker = ChannelChangeWaker_Factory.Create(
            _paths.Root,
            line => _log.Log_Warning(GLOBAL_ORCH_ID, line));

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
                // WAS A BARE Task.Delay, until 2026-09-09. Measured on the VPS that day: 11–12 s median
                // from the owner's Telegram message to their supervisor's turn starting, and this wait
                // is on that path TWICE — the tick that writes their message into the channel is not
                // the tick that carries the answer back. It now ends on a channel write as well as on
                // the tick; everything else in this loop is unchanged, and on a machine whose watcher
                // never fires so is this.
                await waker.Wait_ForChangeOrTick_Async(_timing.MirrorTickMilliseconds, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    async Task Execute_MirrorTick_Async(CancellationToken cancellationToken)
    {
        // THE DENOMINATOR. Since the inter-tick wait can end on a filesystem event, a cost measured
        // over a wall-clock window covers an unknown number of ticks; counting them here is what lets
        // the cost test divide. Free in production — the counter is scoped to a test's async flow.
        Diagnostics.TickIo_Counters.Count_TickEntered();

        // ONE allowance for the whole tick's WAITING. Without it this method's worst case is
        // "appends × the per-call budget", and four of the steps below append inside a
        // foreach(session) -> foreach(member) nest — so the member count was the multiplier and ten
        // members could spend ~15 s of waiting inside a 2 s loop, stalling the poll, the mirror, the
        // tailer, compaction and the status push behind it. Uncontended writes charge ~0 ms and are
        // unaffected; a spent allowance means blocked channels fail fast and retry next tick, which
        // is a defined path (logged, and the owner's message goes back in its buffer).
        using var tickAllowance = ChannelWrite_Lock.Open_TickAllowance(TimeSpan.FromMilliseconds(_timing.TickLockAllowanceMilliseconds));

        // ONE ROSTER FOR THE WHOLE TICK — see _sessionsThisTick. Taken here, before anything reads
        // it, and released in the finally so that a tick which throws cannot leave a stale roster
        // behind for the next one.
        _sessionsThisTick = _store.Load_All();

        // STARTED HERE rather than at the method's first line: Count_TickEntered and the allowance/
        // roster setup above are µs-cheap bookkeeping, not the ~20-step sequence stage/15's 30 s
        // per-message brake can park — starting the clock after them keeps the measurement pointed at
        // the thing SLOW_TICK_THRESHOLD_MILLISECONDS exists to catch.
        var tickStartedUtc = _clock.UtcNow;

        try
        {
            await Execute_MirrorTick_Inside_Snapshot_Async(cancellationToken);
        }
        finally
        {
            _sessionsThisTick = null;
        }

        // CANCELLATION CANNOT PRODUCE A SPURIOUS LINE HERE: a cancelled await throws
        // OperationCanceledException out of the try above, past this call, to Run_MirrorLoop_Async's
        // own catch — so a tick aborted by shutdown is never measured and never logged as slow. Only
        // a tick that ran to completion (the DND-early-return path included) reaches this line.
        Report_SlowTick_IfNeeded(_clock.UtcNow - tickStartedUtc);
    }

    /// <summary>
    /// See SLOW_TICK_THRESHOLD_MILLISECONDS for the number and the evidence, and _tickWasSlowLastTime
    /// for the once-per-spell rule this follows. The message states the measured duration; a
    /// timestamped warning next to the "Telegram 'editMessageText' failed" lines stage/15's brake
    /// produces is already enough to correlate the two. There is no slowest STEP to name: doing that
    /// would mean instrumenting each of Execute_MirrorTick_Inside_Snapshot_Async's ~20 steps
    /// individually, which is the restructuring this was written specifically not to do — the total
    /// plus the log's own timestamp is the cheap version.
    /// </summary>
    void Report_SlowTick_IfNeeded(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds > SLOW_TICK_THRESHOLD_MILLISECONDS)
        {
            if (!_tickWasSlowLastTime)
            {
                _tickWasSlowLastTime = true;
                _log.Log_Warning(GLOBAL_ORCH_ID,
                    $"Mirror tick took {elapsed.TotalSeconds:F1} s (threshold "
                        + $"{SLOW_TICK_THRESHOLD_MILLISECONDS / 1000.0:F0} s) — the tick's own period "
                        + "is 2 s, so something inside it (stage/15's 30 s per-message edit brake is "
                        + "the known cause) parked the mirror, the owner's deliveries and the deadline "
                        + "sweep behind it. Logged once per spell: silent again until a tick lands "
                        + "back under the threshold.");
            }
        }
        else
        {
            _tickWasSlowLastTime = false;
        }
    }

    /// <summary>
    /// The tick itself. Split from <see cref="Execute_MirrorTick_Async"/> for one reason only: the
    /// roster snapshot has to be released on every exit path, including the exceptional ones, and a
    /// try/finally wrapped around a two-hundred-line body would have re-indented all of it.
    /// </summary>
    async Task Execute_MirrorTick_Inside_Snapshot_Async(CancellationToken cancellationToken)
    {

        // ABOVE EVERYTHING THAT SPENDS THE ACCOUNT, and above the DND gate far below. A pause is
        // not a message: it is the app deciding not to spend an allowance it is about to exhaust,
        // and DND means "do not disturb me", not "stop managing the account". Putting the decision
        // under the mute would mean the one state in which nobody is watching is also the state in
        // which nothing stops sixty sessions failing identically. See DispatchPause_Gate.
        await Update_DispatchPause_Async(cancellationToken);

        bool dispatchPaused;

        lock (_ownerStateLock)
            dispatchPaused = Limits.DispatchPause_Gate.Is_Paused(_dispatchPausedUntilUtc, _clock.UtcNow);

        // DEFERRED, NOT DROPPED, while paused. Request files stay on disk untouched, so the work the
        // owner asked for happens the moment the window resets — the protocol is already re-entrant
        // and that is what makes deferring free here.
        Process_PendingRequests(dispatchPaused);

        // After closes are processed, so a freshly-closed session is not immediately revived.
        // A respawn is a LAUNCH: while the account is out of allowance it buys a session that
        // fails on its first turn, and a crash-loop counter that climbs for a cause that has
        // nothing to do with the session. Work already running is never touched.
        if (!dispatchPaused)
        {
            _watchdog.Check_AndRestart_DeadSessions();
            Persist_EngineState_IfRespawnCountsMoved();
        }

        // Print-run sessions: one `claude -p` turn per inbound entry. ABOVE the DND gate on purpose
        // — mute pauses OUTBOUND Telegram, and a member's work is not that. The tick never blocks:
        // it only starts turns, on background tasks, and only where none is in flight.
        _printTurns.Tick(DateTime.Now);

        // Before anything that could write to a channel: the flag is what keeps a supervisor's
        // watcher silent, and a tick that appends before reconciling it would litter the meeting.
        Sync_MeetingFlags();

        // Owner texts flow to the agents regardless of DND — mute only pauses OUTBOUND.
        await Flush_OwnerDeliveries_Async(cancellationToken);

        // AFTER the owner's delivery, and that ORDER IS THE POINT. Both draw on the one allowance
        // above, so whichever runs first can spend it — and several wedged channels retrying
        // announcements would leave nothing for the owner's own message, which is the highest-value
        // write in the system and the one a person is waiting on. Announcement retries are already
        // late by definition and lose nothing by waiting another tick.
        //
        // This costs announcement ordering NOTHING: nothing between here and the tick's start
        // announces, so a queued announcement still lands ahead of any this tick produces.
        Drain_PendingAnnouncements();

        // Lapsing a stale close sends the owner nothing and closes nothing, so it runs even while
        // muted. Behind the gate, DND froze the only thing that disarms a live confirmation button.
        Expire_StaleCloseConfirmations();

        // Same reason, and it must not wait for a restart: a member close parked before the
        // 2026-08-13 directive has a live button that now points at a decision the owner no longer
        // makes. Every tick rather than once at startup, so it is idempotent and cannot be skipped by
        // whatever order the app happens to come up in.
        Release_ParkedMemberCloses();


        // ABOVE THE GATE because it emits nothing — no Telegram, no channel entry, not even a log
        // line. What it does is record what each channel already contained the first time the app saw
        // it, and a mute must not delay that: everything below returns while muted, so a channel born
        // during a mute was first seen hours later and its whole accumulated content was absorbed as
        // history (rev-6 F2). Same reasoning as the two calls above it — inbound flows, lapsing sends
        // nothing — and it is the sweeps minus their reporting.
        Baseline_UnseenChannels_Silently();

        // ABOVE THE GATE, for the reason the pause above it is: a deadline is a clock event, not a
        // disturbance. The message that carried the deadline TOLD the owner what would happen at it,
        // so honouring it while they are muted is keeping that promise; freezing it would mean a
        // mute silently converts every bounded question into an unbounded one. What it produces —
        // a channel entry and an edit to a message already on the screen — generates no
        // notification, and the entry replays in the catch-up burst like everything else.
        await Resolve_QuestionDeadlines_Async(cancellationToken);
        // ABOVE THE DND GATE ON PURPOSE. This writes a local file for the supervisor's own terminal
        // status line and sends nothing anywhere. Below the gate it would freeze the moment the owner
        // pressed 🔕 — and DND means "pause OUTBOUND Telegram", not "stop the app from telling this
        // machine what the ledger says". The same placement is what keeps it working when Telegram is
        // not configured at all, and for orchestrations that have no topic.
        //
        // NOTHING IN THE SUITE PINS THIS LINE'S POSITION. What to write is decided in
        // Planning.ProgressArtefact_Decider and is covered there; WHERE the decision is asked from is
        // a property of the order of statements in this method, which no pure function can observe.
        // Moving this call below the return seven lines down compiles, passes every test, and quietly
        // reintroduces exactly the bug described above. If you are that edit: don't.
        Refresh_ProgressArtefacts();

        // ABOVE THE DND GATE for the same reason as the line above it: this sends the owner nothing.
        // It reads what a plan backend says was approved upstream, writes those requests into PLAN.md,
        // and reports lines that have closed — none of which is Telegram traffic, and all of which
        // must keep working while the owner is not being disturbed and on machines with no bot token
        // at all. With no backend configured (the default) it returns without touching a file, and
        // when there is one the pass runs off this thread so a slow adapter cannot stall the tick.
        Start_PlanBackendPass();

        // DND HOLDS ONLY WHAT RINGS — the owner's ruling of 2026-09-09 (brief C): "🌙 holds only what
        // rings (supervisor entries, actionable alerts); PULSE and the dashboard keep updating
        // silently".
        //
        // Everything below this line stays held: tailing is skipped entirely, so offsets freeze and
        // unmute delivers the whole backlog in one catch-up burst (supervisors' questions included),
        // and crash-loop alerts stay queued in the watchdog for the same reason.
        //
        // THE TWO SILENT SURFACES RUN FIRST INSTEAD OF BEING SKIPPED WITH THE REST. They are the two
        // things the owner READS when they come back rather than things that reach them — the
        // check-in ritual is "show me where everything stands", and it was reading a PULSE and a
        // dashboard frozen at the moment the mute went on. A status surface that stops updating while
        // the owner is away is not quiet, it is WRONG, and it is wrong exactly when it is being used.
        //
        // WHAT THEY COST, precisely, because the first version of this comment overstated it. Every
        // write is SILENT, and neither reads the tailer — they build from the channel files and the
        // ledger directly, so no offset moves and nothing the catch-up burst needs is consumed. But
        // they are not all "one message edited in place": a buried PULSE is still MOVED under 🌙, and
        // a move is a delete plus a send, which mints a new message and spends the per-minute send
        // budget. That budget blocks rather than drops, so nothing is lost — the unmute burst simply
        // shares an allowance PULSE used while nobody was reading. Bounded by the quiet window to one
        // move per burst of traffic, and by the substance rule to moves that carry news.
        //
        // What they cannot do is reach past their own gates: PULSE still refuses to POST or move
        // under 🔕, and both surfaces answer None when their text has not changed, which under a mute
        // is most ticks.
        if (_telegramMuted && _telegramClient != null)
        {
            await Refresh_TopicStatusLines_Async(cancellationToken);
            await Push_GeneralDashboard_Async(cancellationToken);

            return;
        }

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

        // A trailing entry the tailer can parse but may not release, because the file does not end
        // with a line break. It is not lost — it emits as soon as anything appends a header — but
        // until then it is invisible while its sender believes it was delivered, so the silence ends
        // here even though the emission does not change. Once per spell: the condition persists for
        // as long as the file stays unterminated, and a line every 2 s would bury the log it lives in.
        foreach (var heldFile in pollResult.HeldTrailingEntryFiles)
        {
            if (_heldTrailingEntryFiles.Add(heldFile))
                _log.Log_Warning(GLOBAL_ORCH_ID, $"Channel's last entry is parsed but HELD — the file does not end with a line break, so nothing will mirror it until the next append: {heldFile}");
        }

        _heldTrailingEntryFiles.IntersectWith(pollResult.HeldTrailingEntryFiles);

        foreach (var append in pollResult.CompletedAppends)
        {
            if (!Is_MirrorAttemptDue(append.Channel.FilePath))
                continue;

            var delivered = await Mirror_Append_Async(append, cancellationToken);
            Raise_OrchestrationActivity(append.Channel.OrchId);
            await Settle_MirrorAttempt_Async(append, delivered, cancellationToken);
        }

        await Check_UsageLimits_Async(cancellationToken);

        // Every tick, so a mode toggled from the APP's card or checkboxes reaches the Telegram
        // topic name too — not just the ones toggled by a Telegram command. It only calls the API
        // when a name actually changed.
        await Sync_TopicNames_BestEffort_Async(cancellationToken);

        await Check_LedgerHealth_Async(cancellationToken);
        await Check_ChannelShapes_Async(cancellationToken);
        Expire_StaleAwaitingAnswerFlags();
        await Check_AwayMode_Async(cancellationToken);
        await Push_AwayDigests_Async(cancellationToken);
        await Push_GeneralDashboard_Async(cancellationToken);

        // Cheap: guarded by a remembered name, so it is an API call only when the desired name
        // actually changes. Here as well as on the /screens reply so an app restart, or a config
        // edited on disk, puts the camera back rather than leaving the topic list lying.
        if (_telegramClient != null)
            await Sync_GeneralTopicName_BestEffort_Async(_telegramClient, cancellationToken);

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

        return DateTime.UtcNow - lastAttemptUtc >= TimeSpan.FromSeconds(_timing.MirrorRetryBackoffSeconds);
    }

    /// <summary>
    /// Decides what happens to the entries just attempted. Confirming is what lets the persisted
    /// cursor move past them, so NOT confirming is the retry: the tailer re-emits them next poll.
    /// Before this, the cursor advanced during the read and a failed send dropped the owner's
    /// messages permanently — the outage of 2026-08-11 lost every entry that met a 502.
    /// </summary>
    int Count_Parked(string channelFilePath)
    {
        return _parkedUndelivered.TryGetValue(channelFilePath, out var parked) ? parked.Count : 0;
    }

    /// <summary>
    /// Keeps the entries the mirror could not deliver, in the order the channel recorded them.
    ///
    /// <para>
    /// ONLY WHAT WOULD HAVE BEEN SENT — <c>Select_MirrorableEntries</c>, the mirror's own predicate.
    /// An entry the mirror deliberately does not push was never owed to the phone.
    /// </para>
    /// <para>
    /// PAST THE CAP IT STOPS AND SAYS SO, once. An outage long enough to fill it is one the channel
    /// file is the record of; what must not happen is a bridge holding a backlog until it dies.
    /// </para>
    /// </summary>
    void Park_Undelivered(ICompletedChannelAppend append)
    {
        if (!_parkedUndelivered.TryGetValue(append.Channel.FilePath, out var parked))
        {
            parked = [];
            _parkedUndelivered[append.Channel.FilePath] = parked;
        }

        // THE SAME PREDICATE THE MIRROR ITSELF USES, so the digest carries what would have been
        // sent and nothing else. Parking every entry of the append would pad it with the ones the
        // phone was never owed — narration the filter suppresses, app entries in a spoke — and
        // invent deliveries that were never going to happen.
        foreach (var entry in Select_MirrorableEntries(append))
        {
            if (parked.Count >= Mirroring.UndeliveredDigest_Builder.MAX_PARKED_ENTRIES)
            {
                _log.Log_Warning(
                    append.Channel.OrchId,
                    $"The undelivered backlog for '{Path.GetFileName(append.Channel.FilePath)}' is full at {Mirroring.UndeliveredDigest_Builder.MAX_PARKED_ENTRIES} entries — further entries are in the channel file only");

                return;
            }

            parked.Add((_clock.UtcNow, entry.Author.ToString(), entry.Subject, entry.Body));
        }
    }

    /// <summary>
    /// ONE DOCUMENT, ON THE FIRST SEND THAT WORKS — never a burst of replayed messages. A catch-up
    /// that scrolls the owner's phone for a minute is a second failure, not a recovery.
    ///
    /// <para>
    /// CLEARED BEFORE THE SEND, deliberately, and the trade is stated rather than hidden: a digest
    /// whose own upload fails is lost, while clearing it afterwards would re-send the same document
    /// on every subsequent successful append until it happened to work — a loop the owner cannot
    /// stop. The entries are in the channel file either way, and the Error line naming the outage
    /// is already in the log.
    /// </para>
    /// </summary>
    async Task Deliver_UndeliveredDigest_IfAny_Async(ICompletedChannelAppend append, CancellationToken cancellationToken)
    {
        if (!_parkedUndelivered.TryGetValue(append.Channel.FilePath, out var parked) || parked.Count == 0)
            return;

        _parkedUndelivered.Remove(append.Channel.FilePath);

        var client = _telegramClient;

        if (client == null)
            return;

        try
        {
            var threadId = await Resolve_ThreadId_OrNull_Async(append.Channel, cancellationToken);

            await client.Send_Document_Async(
                threadId,
                Mirroring.UndeliveredDigest_Builder.FILE_NAME,
                Mirroring.UndeliveredDigest_Builder.Build_Content(parked),
                Mirroring.UndeliveredDigest_Builder.Build_CaptionHtml(parked.Count, parked[0].WhenUtc, parked[^1].WhenUtc),

                // IT RINGS. These are entries the owner was owed and never got — the digest is the
                // delivery, not a status note about one.
                TelegramSendSounds.Rings,
                cancellationToken);

            _log.Log_Info(
                append.Channel.OrchId,
                $"Delivered the undelivered-entries digest ({parked.Count}) for '{Path.GetFileName(append.Channel.FilePath)}' now that Telegram is answering again");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(
                append.Channel.OrchId,
                $"The undelivered-entries digest ({parked.Count}) for '{Path.GetFileName(append.Channel.FilePath)}' could not be uploaded — the entries remain in the channel file",
                ex);
        }
    }

    async Task Settle_MirrorAttempt_Async(ICompletedChannelAppend append, bool delivered, CancellationToken cancellationToken)
    {
        var channelFilePath = append.Channel.FilePath;

        if (delivered)
        {
            _mirrorRetryFirstFailureUtc.Remove(channelFilePath);
            _mirrorRetryLastAttemptUtc.Remove(channelFilePath);
            _tailer.Confirm_Append(channelFilePath);

            // THE PHONE IS ANSWERING AGAIN, so what it missed goes out now — once, as a document.
            await Deliver_UndeliveredDigest_IfAny_Async(append, cancellationToken);
            return;
        }

        // READ THROUGH THE INJECTED CLOCK, not DateTime.UtcNow. This is a DEADLINE read rather than
        // a sleep — the distinction IBridgeEngineTiming's own summary draws — so the clock is what
        // a test steps to reach the give-up, and the window stays the shipped 30 minutes in
        // production instead of becoming a knob nobody sets.
        var nowUtc = _clock.UtcNow;

        _mirrorRetryLastAttemptUtc[channelFilePath] = nowUtc;

        if (!_mirrorRetryFirstFailureUtc.TryGetValue(channelFilePath, out var firstFailureUtc))
        {
            firstFailureUtc = nowUtc;
            _mirrorRetryFirstFailureUtc[channelFilePath] = firstFailureUtc;
        }

        if (nowUtc - firstFailureUtc < TimeSpan.FromMinutes(MIRROR_RETRY_WINDOW_MINUTES))
            return;

        Park_Undelivered(append);

        // The window is spent, so this confirm lets the cursor move past the entries — but they are
        // PARKED above, not dropped, and the next send that works carries them as one document.
        // Said at Error and naming the channel, because the alternative — a channel that quietly
        // never mirrors again — is the exact failure the owner reported: cut off, with no way to
        // know.
        _log.Log_Error(
            append.Channel.OrchId,
            $"Telegram mirror gave up after {MIRROR_RETRY_WINDOW_MINUTES} minutes of retries — {Count_Parked(channelFilePath)} entr{(Count_Parked(channelFilePath) == 1 ? "y" : "ies")} from '{Path.GetFileName(channelFilePath)}' are PARKED and will be delivered as a digest when Telegram answers again",
            null);

        _mirrorRetryFirstFailureUtc.Remove(channelFilePath);
        _mirrorRetryLastAttemptUtc.Remove(channelFilePath);
        _tailer.Confirm_Append(channelFilePath);
    }

    /// <summary>
    /// DISCOVERS the channels and hands each to <see cref="Channel_CompactionStep.Compact_IfAllowed"/>.
    /// It no longer archives anything itself and no longer re-anchors the cursor — both moved into
    /// the step, along with the guards, so the suite drives the same code the tick does.
    ///
    /// <para>
    /// What stays true here: it runs AFTER the mirror poll, and discovery is deliberately WIDER than
    /// the poll — which is exactly why the step's first guard exists, since a channel the poll
    /// skipped has a frozen cursor that must not be re-anchored to a rewritten file.
    /// </para>
    /// <para>
    /// The archive-and-re-anchor reasoning lives with the code that does it. This docstring described
    /// this method's old body for two commits after that body moved (rev-7, 2026-08-13) — the same
    /// prose-outliving-code shape the comment inside the loop already had to correct once.
    /// </para>
    /// </summary>
    void Compact_LongChannels()
    {
        foreach (var channel in ChannelDiscovery.Find_ChannelFiles(_paths))
        {
            // Guards, archive and re-anchor all live in the step, because a guard that only exists
            // here is a guard whose only proof is a copy of itself in a test file. That reason is
            // sound and it is the only one: the sentence that used to sit here — "this engine cannot
            // be constructed from a test" — was FALSE. BridgeEngine_Factory.Create is public, and
            // ChannelCompactionLoopProbeTests drives this very loop through it.
            var newLength = Channel_CompactionStep.Compact_IfAllowed(_tailer, channel.FilePath, _log, channel.OrchId);

            if (newLength == null)
                continue;

            _log.Log_Info(channel.OrchId, $"Channel compacted — older entries archived beside it ({Path.GetFileName(channel.FilePath)})");
        }
    }

    /// <summary>
    /// A session respawning repeatedly without coming alive is INVISIBLE from the phone — escalate it.
    /// <para>
    /// The queue is DRAINED by <c>Take_PendingCrashLoopAlerts</c>, so a silenced topic used to lose
    /// its alerts outright: taken, skipped, never re-queued. The tick's own comment promises the
    /// opposite — "crash-loop alerts stay queued in the watchdog until unmute" — which is true of
    /// app-wide DND (it returns above this) and was false of a meeting, which runs on through it
    /// (rev-7 P6, 2026-08-13). They are held here instead, newest per orchestration, and delivered
    /// when the topic can hear again.
    /// </para>
    /// </summary>
    async Task Send_CrashLoopAlerts_Async(CancellationToken cancellationToken)
    {
        // TryAdd, not assignment: an identical repeat is the same alert, while a SIBLING session's
        // alert differs in its text and must survive alongside it. Bounded because the watchdog
        // emits each one exactly once, at the threshold.
        foreach (var alert in _watchdog.Take_PendingCrashLoopAlerts())
            _heldCrashLoopAlerts.TryAdd((alert.OrchId, alert.AlertText), new CrashLoopAlertHold(default, 0));

        if (_telegramClient == null)
        {
            // Nothing will ever deliver these, so holding them is a leak rather than a promise —
            // but it is still a DROP, and this was the one exit of four that took it silently, in a
            // method whose whole point is that a lost alert says so (rev-7). Logged per alert, and
            // only when there is something to lose: on a machine with no bot configured this path
            // runs every tick, and an unconditional line would bury the log it lives in.
            foreach (var (orchId, alertText) in _heldCrashLoopAlerts.Keys)
                _log.Log_Warning(orchId, $"Crash-loop alert dropped undelivered — Telegram is not configured, so nothing can ever deliver it: {alertText}");

            _heldCrashLoopAlerts.Clear();
            return;
        }

        foreach (var (key, hold) in _heldCrashLoopAlerts.ToList())
        {
            var heldSession = _store.Get_Session_OrNull(key.OrchId);

            // A CLOSED orchestration's held alert is dropped, and BOTH reasons are real.
            //
            // Close_Orchestration asks for the Telegram topic to be DELETED —
            // Delete_TelegramTopic_FireAndForget, called immediately after _store.Close_Orchestration.
            // Stated precisely, because the looser version of this sentence was itself a finding
            // (rev-6 F7): the call is conditional on there being a topic id, it is fire-and-forget,
            // and it swallows-and-logs its failure — so a delete refused for want of rights leaves
            // the topic alive. The stored TelegramTopicId is never cleared by any of this; what goes
            // is the topic, not the id. When the delete does land, every later send against that id
            // fails for ever; before it lands there is a window in which the alert WOULD arrive —
            // the owner texted that a session is crash-looping in an orchestration they just ended.
            //
            // It is still NOT the bound below: that covers the cases nothing here can see — a topic
            // the owner deleted from their phone, revoked bot rights — which Telegram answers 400
            // for ever regardless of what this session thinks its state is.
            // GENERAL IS NOT A CLOSED ORCHESTRATION, and treating it as one silenced the session
            // that is the owner's own counterpart. General keeps no session.json and never gets
            // one, so Get_Session_OrNull("general") returns null ALWAYS — not on an edge, on every
            // tick — and this exit read that null as "closed, topic deleted". Its crash-loop alert
            // was therefore discarded every single time, logged at INFO as an expected ending, for
            // the one orchestration that is never closed and whose topic is alive and receiving.
            // The watchdog emits once per episode, so the escalation was gone for good (rev-7 G1).
            //
            // The check is now the QUESTION IT MEANT: is this orchestration closed? General cannot
            // be, and an unknown orchId still can — a session.json that has gone means the
            // orchestration went with it.
            if (key.OrchId != ChannelDiscovery.GENERAL_ORCH_ID && (heldSession == null || heldSession.ClosedUtc != null))
            {
                _heldCrashLoopAlerts.Remove(key);

                // TWO STATES, TWO MESSAGES, because only one of them is a close. Get_Session_OrNull
                // returns null on exactly one condition — session.json is not there — and
                // Close_Orchestration PRESERVES that file (CreateFrom_Existing_Closed, saved back).
                // So a closed orchestration always has a session.json, and an absent one was never
                // closed: the single message asserted "the orchestration is closed" on the one
                // disjunct where closure is definitionally impossible (rev-6 F6).
                //
                // G4 removed a deletion this line never checked, and replaced it with "so nothing is
                // watching its topic" — which this line never checked either, and which is LESS
                // checkable than the claim it replaced: several states satisfy ClosedUtc != null with
                // the topic alive and readable (rev-6 F5). Each message now stops at what was
                // computed, which for the closed case is the closure and nothing else.
                if (heldSession == null)
                    _log.Log_Warning(key.OrchId, $"Crash-loop alert dropped — there is no session.json for this orchestration: {key.AlertText}");
                else
                    _log.Log_Info(key.OrchId, $"Crash-loop alert dropped — the orchestration is closed: {key.AlertText}");

                continue;
            }

            // AND A BOUND, because the hold turned "one failed send" into a retry with no ceiling.
            // It counts ATTEMPTS, never elapsed time: a meeting or a DND spell holds the alert
            // WITHOUT trying, and a wall-clock bound would let a long meeting spend the budget and
            // discard an alert that was never once offered to Telegram.
            //
            // The give-up says WHICH alert and WHY. An alert that quietly stops retrying is the
            // lost-alert failure returning through the door the hold just closed (decision 21).
            if (hold.Attempts >= CRASH_LOOP_ALERT_MAX_ATTEMPTS)
            {
                _heldCrashLoopAlerts.Remove(key);
                _log.Log_Warning(key.OrchId, $"Crash-loop alert GIVEN UP undelivered after {hold.Attempts} failed sends: {key.AlertText}");
                continue;
            }

            // The EFFECTIVE MODE, not silence alone. Gating on Is_TopicSilenced pushed this straight
            // to the phone for a topic explicitly set to DEFERRED — bypassing the frozen cursor that
            // deferral promises — while app-wide DND held it, because the tick returns above this
            // line. The two DNDs behaved oppositely for the same alert, and neither behaviour was
            // written down anywhere (rev-6 F9, 2026-08-13). Held for both now: it is the same
            // question, and the hold above is what makes holding safe rather than lossy.
            if (Resolve_EffectiveMode(key.OrchId) != TelegramDeliveryModes.Normal)
                continue;

            // BACKOFF INSTEAD OF DROPPING. This removed the alert BEFORE the attempt, with a real
            // reason: retrying every tick against a failing endpoint earns a server-side throttle.
            // That reasoning does not survive the watchdog's ONE-SHOT semantics — it emits at
            // CRASH_LOOP_THRESHOLD and the counter resets only when the slot comes alive — so a
            // single 502 meant the owner was never told at all. Holding with a backoff answers the
            // throttle concern without paying for it in lost alerts (rev-6 F3, 2026-08-13).
            if (hold.LastAttemptUtc != default && DateTime.UtcNow - hold.LastAttemptUtc < TimeSpan.FromSeconds(_timing.MirrorRetryBackoffSeconds))
                continue;

            // The attempt is counted BEFORE it is made, so a send that throws still spends one — the
            // bound must count what was tried, not what came back.
            _heldCrashLoopAlerts[key] = new CrashLoopAlertHold(DateTime.UtcNow, hold.Attempts + 1);

            try
            {
                // NULL-CONDITIONAL, and it is load-bearing rather than defensive: General has no
                // session, and a null thread id is how this client addresses the General topic. The
                // non-conditional form was safe only while the exit above dropped every sessionless
                // orchestration — the bug that exit had. Fixing one without the other would have
                // turned a silent discard into a NullReferenceException on the same path.
                await _telegramClient.Send_Message_Async(heldSession?.TelegramTopicId, key.AlertText, TelegramSendSounds.Rings, cancellationToken);

                // Dropped only after a CONFIRMED send — the rule 71a849a applied to three memos
                // while this site, its own immediate predecessor, contradicted it.
                _heldCrashLoopAlerts.Remove(key);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // FILTERED, and the filter is the fix. An HttpClient TIMEOUT throws
                // TaskCanceledException, which IS an OperationCanceledException — so the unfiltered
                // form rethrew a timeout as if the app were shutting down, and this loop died with
                // the mirror tick around it.
                //
                // WHAT THAT BUYS, narrowed to what it delivers (rev-6, 2026-08-14). The tick survives
                // a timeout here only when it had nothing else to send: the very next call,
                // Send_StallAlerts_Async, rethrows bare on the same shape, and so do the budget
                // alerts and the channel poll. Against a wedged endpoint every send times out, so
                // the tick still dies one call later. One site is fixed, not the tick.
                //
                // NOR WAS THIS THE PATH THAT LOGGED NOTHING. The rethrow reached
                // Run_MirrorLoop_Async's `catch (Exception ex)` and was logged as an ERROR carrying
                // the whole exception. The real gain is ATTRIBUTION — which orchestration, which
                // alert — and the price is a LEVEL: an ERROR with a stack becomes a WARNING with
                // ex.Message. Worth it, and said out loud so nobody meets it as a surprise.
                //
                // What is unchanged and was always true: the attempt is counted one line above the
                // send, so a timeout spent the give-up budget while this handler added nothing of its
                // own to the log.
                //
                // AND THIS SITE WAS NOT THE OUTLIER — counted in this file at this sha rather than
                // asserted: 43 `catch (OperationCanceledException`, 6 filtered, 37 unfiltered (5 and
                // 38 before this change). Two unfiltered handlers sit 13 lines below a filtered one
                // near the top, so even "the handlers at the top" does not hold. The 37 are their own
                // ledger line, not this commit's (rev-7 G2, rev-6 F1/F2/F3, 2026-08-14).
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(key.OrchId, $"Crash-loop alert send failed, holding it for retry: {ex.Message}");
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

        foreach (var session in Sessions_ThisTick())
        {
            if (session.ClosedUtc != null)
                continue;

            var quietFor = Get_OrchestrationQuietFor(session);

            if (quietFor.TotalMinutes < STALL_ALERT_MINUTES)
            {
                // Traffic resumed. The remembered question key is deliberately KEPT: the alert is
                // once per question now, and traffic that does not answer it — the supervisor's own
                // follow-up, an app entry — must not buy a second ⚠️ about the same question.
                continue;
            }

            // Someone is actually working, or worked inside the window: a long thinking turn or a turn
            // that has just ended, not a stall. Wider than "mid-turn" on purpose — see the method.
            if (Has_AnySessionWorkedWithin(session, STALL_ALERT_MINUTES))
                continue;

            // SAME SHAPE AS THE BUDGET ALERT, found by sweeping the file for it rather than by a
            // review: this took the token first, so both a meeting and a thrown send spent it on an
            // alert nobody received. It is less severe only because it has a release above (traffic
            // resuming clears it), so the loss is confined to the current stall rather than the
            // process — the token is now taken after a confirmed send, like the other two.
            if (Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
                continue;

            // AWAY MODE SUPPRESSES THIS ENTIRELY. "Waiting on your reply and nothing is running" is,
            // while away, a description of the owner's own deliberate absence — decision 15's exact
            // test: an alert they cannot act on does not go to Telegram. The away digest already
            // carries the one fact this would have added, and the token is NOT taken here, so the
            // first tick after they return alerts normally if the stall is still real.
            if (Is_AwayMode())
                continue;

            // ONLY FOR A REAL QUESTION, AND ONCE PER QUESTION (owner's ruling, 2026-09-09). The old
            // rule was "the session spoke last", which is not a debt: a supervisor reporting
            // progress, or saying that nothing more is needed, has asked for nothing. It produced
            // six false alerts across two topics in one evening — including one at 17:30, thirty
            // minutes after the supervisor wrote "Nothing more needed from you".
            var unansweredQuestion = Status.OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
                ChannelHistory_Cache.Read_Entries(_paths.Get_OwnerChannelFile(session.OrchId)));

            if (unansweredQuestion == null)
                continue;

            // WHAT IT SAYS, NOT WHERE IT SITS. See the field's own summary for the two silent
            // failures the `[n]` produced.
            var questionKey = Status.OwnerOwesReply_Decider.Identify_Question(unansweredQuestion);

            // UNDER THE LOCK, all three accesses, because this map stopped being tick-thread-only on
            // 2026-09-10: the owner speaking clears it, and that happens on the INBOUND loop. A plain
            // Dictionary read concurrently with a Remove is not merely stale, it can corrupt or throw.
            bool alreadyAlertedForThisQuestion;

            lock (_ownerStateLock)
                alreadyAlertedForThisQuestion =
                    _stallAlertedQuestionKeyByOrchId.TryGetValue(session.OrchId, out var alreadyAlertedFor)
                    && alreadyAlertedFor == questionKey;

            if (alreadyAlertedForThisQuestion)
                continue;

            // NEVER WHILE THE SUPERVISOR IS PAUSED FOR A USAGE LIMIT (owner's ruling, same day, from
            // the alert they got at 18:38). A paused turn has an appointment: the state file carries
            // the instant it resumes, and until then "nothing is running" is true but "has been
            // waiting on your reply" is not — the supervisor is not waiting on them, it is waiting
            // on the clock. The deferral notice the dispatcher writes is an APP entry, which the
            // quiet clock deliberately ignores, so nothing else on this path could see it.
            if (Is_SupervisorPausedForUsageLimit(session))
            {
                _log.Log_Info(session.OrchId, "Stall alert withheld: the session is paused for a usage limit, not waiting on the owner");
                continue;
            }

            // THE SEVENTH SITE THAT NAMED A SUPERVISOR, and the one SpeakerLabel_Formatter's summary
            // predicted: prose rather than a prefix, so the coloured label could not be dropped in and
            // it was written by hand. A basic orchestration has never had a supervisor.
            var speaker = Mirroring.SpeakerLabel_Formatter.Describe_Noun(
                isGeneral: false,
                isBasic: Sessions.OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc));

            // Says what is actually true now: they are the one holding it up. The old wording blamed
            // a session that had done nothing wrong, which is why it read as nonsense.
            var alertText = $"⚠️ {session.DisplayName ?? session.OrchId}: {speaker} has been waiting on your reply for {SessionDuration_Formatter.Describe(quietFor)} and nothing is running.";

            try
            {
                await _telegramClient.Send_Message_Async(session.TelegramTopicId, alertText, TelegramSendSounds.Rings, cancellationToken);

                // After a CONFIRMED send, so a failed one retries next tick — and keyed on the
                // question, so the next alert needs a new one.
                lock (_ownerStateLock)
                    _stallAlertedQuestionKeyByOrchId[session.OrchId] = questionKey;
                _log.Log_Warning(session.OrchId, alertText);
            }
            // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
            // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
            // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
            // Cost HERE: every REMAINING session's stall alert in the same sweep, and the tick below it.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        {
            // A RETIRED MEMBER DOES NOT VOUCH FOR A LIVE ORCHESTRATION (rev-8's F5). Every other member
            // loop in this file carries this guard; this one was the exception, and the direction is
            // one-way: the span is the MINIMUM, so a closed member's channel can only LOWER it and
            // therefore only ever mask a stall.
            //
            // Reachable by the ordinary route rather than an exotic one: a member is normally closed
            // just after filing its last report, so at the moment of closing its last conversation
            // entry is RECENT by construction — and for the next twenty-five minutes that farewell
            // vouches for everybody else's silence.
            if (member.ClosedUtc != null)
                continue;

            channelFiles.Add(Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId));
        }

        foreach (var channelFile in channelFiles)
        {
            if (!File.Exists(channelFile))
                continue;

            var entries = ChannelHistory_Cache.Read_Entries(channelFile);
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
    /// <summary>
    /// Did any session in this orchestration demonstrably WORK inside the window — not "say" anything,
    /// but write a transcript?
    ///
    /// EVIDENCE OF LIFE OUTRANKS AN AGENT'S OWN STAMP, and that is the whole reason this is wider than
    /// the mid-turn question it replaces (rev-8's F3). The quiet span is computed from `DateText`, which
    /// item 12 declares untrusted input, and the trusted reader refuses only stamps in the FUTURE — a
    /// stamp drifted into the PAST passes unchallenged. A supervisor that runs a forty-minute turn and
    /// stamps its entry with the time it read at turn START looks forty minutes silent the moment the
    /// turn ends, and the owner is texted about a session that had just spoken. The channel cannot
    /// refute that stamp; the filesystem can, because the app writes these files rather than an agent.
    ///
    /// THIS NARROWS THE ALERT AND THE NARROWING IS DELIBERATE: an orchestration whose sessions worked
    /// inside the window but have stopped SPEAKING is no longer alerted on. We hold evidence of life
    /// inside the window, so claiming a stall would be asserting more than we know — and this is the
    /// same judgement the mid-turn check already made, over a longer horizon.
    /// </summary>
    bool Has_AnySessionWorkedWithin(IOrchestrationSession session, int minutes)
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
            var lastActivityUtc = SessionActivity_Probe.Get_LastActivityUtc_OrNull(usageFile);

            if (lastActivityUtc != null && (DateTime.UtcNow - lastActivityUtc.Value).TotalMinutes < minutes)
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
        foreach (var session in Sessions_ThisTick())
        {
            if (session.ClosedUtc != null)
                continue;

            Nudge_IdleSupervisor(session);

            List<string> awaitingVerdict = [];

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId);

                if (!File.Exists(channelFile))
                    continue;

                var entries = ChannelHistory_Cache.Read_Entries(channelFile);
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

                // WORKING MEANS DO NOT DISTURB — and the app now answers that from its OWN dispatcher
                // rather than from a status-line file a headless session never writes. This guard has
                // been silently false on every bridge-driven host since the day it was written, which
                // is why members inside long turns were nudged every eight minutes for being busy:
                // a member in a turn cannot consume its channel, so its unread entries age past the
                // threshold and it is woken for the very reason it should be left alone. Each of those
                // wakes costs it a turn.
                var working = Resolve_MemberWorking(Running.SessionRoles.Implementer, session.OrchId, member.MemberId);

                if (MemberWorking_Decider.Is_Busy(working))
                    continue;

                // UNKNOWN FALLS BACK, it does not decide. A terminal-run member does render a status
                // line, so the old probe is the right answer for it — and for a bridge-driven member
                // the app has simply learnt nothing yet, which is not permission to wake it on the
                // strength of a file that does not exist.
                if (working == WorkingVerdicts.Unknown
                    && Is_SessionMidTurn(Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE)))
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
                    // ALWAYS ANSWERABLE, and the two null guards that used to be here were the second
                    // route back into the loop: a null skipped the gate AND skipped the record, so a
                    // channel with no conversation entry was nudged forever. It is now keyed on a
                    // sentinel — see Nudge_Decider.NO_CONVERSATION_YET, including why it cannot be
                    // one of the channel's own entries.
                    var conversationIdentity = Nudge_Decider.Identify_NudgeSubject(entries, channelFile);

                    if (_nudgedAboutEntry.TryGetValue(memberKey, out var alreadyNudgedAbout)
                        && alreadyNudgedAbout == conversationIdentity)
                        continue;

                    var nudged = await Nudge_Implementer_Async(session, member.MemberId, channelFile, entries[^1], quietFor, dormantMidWork, cancellationToken);

                    // BOTH memos are conditional on the nudge existing. The first starts the orphan
                    // clock — miss this and a member that never received a nudge is killed and
                    // respawned for not answering it, losing its context. The second suppresses
                    // re-nudging about this same entry forever.
                    if (!nudged)
                        continue;

                    // THE SAME CLOCK THE ESCALATION READS. This stamp starts the orphan window and
                    // the escalation measures it with _clock; taking one from DateTime.UtcNow and the
                    // other from the injected clock is how a window silently becomes a different
                    // length under a test, and how a fake clock proves nothing.
                    _nudgedMemberUtc[memberKey] = _clock.UtcNow;
                    _nudgedAboutEntry[memberKey] = conversationIdentity;

                    // The anti-loop memory is only worth having if it outlives the process: the app
                    // is closed and reopened to rebuild it, and a forgotten memory re-nudges every
                    // member about a thing it has already nudged them about.
                    Persist_EngineState();

                    continue;
                }

                // ESCALATION — and it no longer touches the process. See OrphanEscalation_Decider for
                // the measured incident that ended the kill: 19 ORPHANED events in three hours, every
                // one of them a false positive, on a host where the evidence the old test demanded
                // cannot exist at all.
                var memberUsageFile = Path.Combine(
                    _paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE);

                var escalation = OrphanEscalation_Decider.Decide(
                    Is_BridgeDriven(Running.SessionRoles.Implementer, session.OrchId, member.MemberId),
                    _clock.UtcNow - nudgedUtc,
                    TimeSpan.FromMinutes(ORPHAN_CONFIRM_MINUTES),
                    // EITHER SOURCE OF LIFE COUNTS. The app's own dispatcher answers for a
                    // bridge-driven member; the status-line probe answers for a terminal one. Asking
                    // both means neither host is judged on evidence it cannot produce.
                    MemberWorking_Decider.Is_Busy(Resolve_MemberWorking(Running.SessionRoles.Implementer, session.OrchId, member.MemberId))
                        || SessionActivity_Probe.Is_MidTurn(memberUsageFile),
                    SessionActivity_Probe.Get_LastActivityUtc_OrNull(memberUsageFile),
                    nudgedUtc);

                if (!OrphanEscalation_Decider.Clears_TheClock(escalation))
                    continue;

                _nudgedMemberUtc.Remove(memberKey);

                // WRITTEN DOWN EVERY TIME, including — especially — the quiet outcomes. The common
                // case is now silence toward the owner, and silence with no record reads exactly like
                // a detector somebody switched off.
                _log.Log_Info(session.OrchId, OrphanEscalation_Decider.Describe(escalation, member.MemberId));

                if (!OrphanEscalation_Decider.Reports(escalation))
                    continue;

                // A REPORT, NOT A RESPAWN. The supervisor can look at the member, ask it something, or
                // close and re-add it — all of which it can already do, and all of which are decisions
                // this loop has no business taking on evidence this thin.
                Append_SupervisorAttention_UnlessMeeting(
                    session.OrchId,
                    $"{member.MemberId} may be deaf to wakes",
                    $"{member.MemberId} was nudged {ORPHAN_CONFIRM_MINUTES} minutes ago, took no turn since, and has no "
                    + "tool call in flight. It may be fine — check its channel before doing anything. If it really is "
                    + "deaf, close it and add a replacement; the app will not restart it for you.",
                    Resolve_Presence(session.OrchId));
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
            // FIRST SIGHT, BEFORE THIS SWEEP READS. Whatever is already in the file at that instant
            // is history and goes into the memo below; the read on the next line is the later of the
            // two, so anything appearing between them is new and gets reported rather than absorbed.
            Baseline_IfUnseen(channel);

            // BRACKET THE READ. `imp-2` named the one writer neither dead hypothesis covers:
            // Channel_Compactor rewrites the live file wholesale rather than appending, which is a
            // far wider window for a reader. This stat is what lets the next occurrence say whether
            // ANY writer touched the file while it was being read — one stamp at report time would
            // have nothing to compare against. It costs one stat per channel per tick, on a path
            // that already reads every channel's full text.
            var beforeRead = ChannelFile_Snapshot.Take_OrUnknown(channel.FilePath);

            var malformed = ChannelShape_Validator.Find_MalformedHeaders(UsageTotals_Reader.Read_Text_Safe(channel.FilePath));

            if (malformed.Count == 0)
                continue;

            List<(int LineNumber, string Line)> unreported = [];

            // MASTER'S SHAPE ON PURPOSE — this memo is NOT this branch's to fix. The defect is real
            // (the memo is committed here, before the append that reports these entries, so a failed
            // append marks them reported for ever and the memo has no release) and it was fixed here
            // independently, in the same lines, by `fix/atomic-channel-appends`. Two implementations
            // of one fix carried most of that pair's 23 conflict regions, the largest count in the
            // repo, and the class — "a memo recording work as done, moved to after the append
            // succeeded", seven sites — belongs to that branch by ruling (supervisor, 2026-08-14).
            //
            // So these lines are byte-identical to master, deliberately, so that fix applies cleanly.
            // UNTIL IT MERGES THIS SITE IS UNPROTECTED: the append below throws rather than returning
            // false, and a throw here takes the rest of the mirror tick with it.
            foreach (var entry in malformed)
            {
                // NEW IS THE WHOLE QUESTION NOW. This used to be `isNew && !isFirstSight`, because
                // this sweep took its own first sight and had to suppress what was already in the
                // file. Baseline_IfUnseen above has put exactly those entries in this memo at the
                // instant sight was taken, so anything still new here arrived afterwards — which is
                // the definition of the thing worth reporting.
                //
                // CONTAINS, NOT ADD, and that is the half this branch contributes: the memo is
                // written only once the report has LANDED, below. Adding here would mark a header
                // reported by the act of noticing it, so a locked channel would silence it for ever —
                // and the memo is the only record that it was not reported.
                if (!_reportedMalformedHeaders.Contains(ChannelShape_Validator.Build_MemoKey(channel.FilePath, entry.Line)))
                    unreported.Add(entry);
            }

            if (unreported.Count == 0)
                continue;

            // THE BYTES, to the log only — nobody with a phone can act on a hex dump (decision 15).
            //
            // LOGGED BEFORE THE APPEND, AND THAT ORDERING IS DELIBERATE — do not "tidy" it to sit
            // after the append to match the memo below it. A MEMO must be recorded after a confirmed
            // write, because it must never record work that did not happen. A DIAGNOSTIC must be
            // written before, because it must not vanish in exactly the case it exists to explain:
            // an append that fails is the occurrence, and logging afterwards loses the evidence for
            // it. Two different things, two different correct orderings, and they do not conflict
            // (supervisor's ruling, 2026-08-14).
            //
            // Without this, the only record of an occurrence was the report itself — and twice on
            // 2026-08-13 that report could not settle the question its own subject was sitting on.
            var fileAcrossRead = ChannelFile_Snapshot.Describe_ChangeAcrossRead(beforeRead, ChannelFile_Snapshot.Take_OrUnknown(channel.FilePath));

            foreach (var entry in unreported)
                _log.Log_Warning(channel.OrchId, $"Malformed header — {Path.GetFileName(channel.FilePath)} line {entry.LineNumber} — {ChannelShape_Validator.Diagnose(entry.Line)} {fileAcrossRead}");

            if (!ChannelAppender.Append_AppEntry(
                    channel.FilePath, AppEntryAudiences.Agent,
                    $"{unreported.Count} entr{(unreported.Count == 1 ? "y is" : "ies are")} INVISIBLE — malformed header",
                    ChannelShape_Validator.Build_ReportBody(unreported),
                    DateTime.Now))
            {
                _log.Log_Warning(channel.OrchId, $"{Path.GetFileName(channel.FilePath)}: {unreported.Count} invisible entr(ies) could not be reported (channel locked) — NOT marked as reported, the next tick retries");
                continue;
            }

            foreach (var entry in unreported)
                _reportedMalformedHeaders.Add(ChannelShape_Validator.Build_MemoKey(channel.FilePath, entry.Line));

            _log.Log_Warning(channel.OrchId, $"{Path.GetFileName(channel.FilePath)}: {unreported.Count} malformed entry header(s) — those entries were never mirrored");
            Raise_OrchestrationActivity(channel.OrchId);

            // On the OWNER channel the loss is the owner's: the content never reached their phone.
            if (channel.IsOwnerChannel)
                await Alert_MalformedOwnerEntries_Async(channel.OrchId, unreported.Count, cancellationToken);
        }

        Screen_ChannelIndexSequences();
    }

    /// <summary>
    /// THE OTHER HALF of the shape check above: header lines that parse PERFECTLY and should not be
    /// entries at all. A header quoted inside another entry's body is the case — it parses, so
    /// `Find_MalformedHeaders` skips it by design, and the app then reads the quotation as a real
    /// entry, attributing a body to whoever was quoted and consuming an index a later entry collides
    /// with. It happened here on 2026-08-13, twice in one evening, to two different members — the
    /// second time inside the entry reporting the first.
    ///
    /// LOG ONLY. Not a channel entry and not Telegram: an index that runs backwards is a diagnostic
    /// the owner cannot act on (decision 15), and the actionable half already owns the channel-entry
    /// path directly above. It is also a SCREEN — roughly half its hits are legitimate crossings where
    /// two authors allocated one index in the same minute — so it must never post as if it had found
    /// a defect.
    /// </summary>
    void Screen_ChannelIndexSequences()
    {
        foreach (var channel in ChannelDiscovery.Find_ChannelFiles(_paths))
        {
            // Same as the sweep above, and for the same reason: sight is taken once, by whoever
            // reaches the file first, and it is taken before this read.
            Baseline_IfUnseen(channel);

            var crossings = ChannelIndexSequence_Screen.Find_Crossings(
                ChannelIndexSequence_Screen.Read_Headers(
                    UsageTotals_Reader.Read_Text_Safe(Channel_Compactor.Build_ArchiveFilePath(channel.FilePath)),
                    UsageTotals_Reader.Read_Text_Safe(channel.FilePath)));

            if (crossings.Count == 0)
                continue;

            foreach (var crossing in crossings)
            {
                // Same as the sweep above: what was in the file when it was first read is already
                // in this memo, so NEW means it arrived after that.
                if (_screenedIndexCrossings.Add(ChannelIndexSequence_Screen.Build_MemoKey(channel.FilePath, crossing)))
                    _log.Log_Warning(channel.OrchId, $"{Path.GetFileName(channel.FilePath)}: {ChannelIndexSequence_Screen.Describe_Crossing(crossing)}");
            }
        }
    }

    /// <summary>
    /// FIRST SIGHT MUST NOT WAIT FOR THE UNMUTE. Both sweeps above run BELOW the DND gate, so while
    /// Telegram is muted neither of them sees anything — but orchestrations can still be CREATED under
    /// DND. A channel born during a mute was therefore first SEEN at unmute, hours later, and
    /// everything that had accumulated in it meanwhile was absorbed as "history" and could never be
    /// reported (rev-6 F2 against `27b216c`).
    ///
    /// <para>
    /// THIS PASS RUNS ONCE PER CHANNEL AND NEVER AGAIN, and that is the correctness core rather than an
    /// optimisation. Running it on every muted tick would read each new offence as it appeared and
    /// record it as history — F2's own failure, moved from the unmute to the mute and made invisible in
    /// a different place. Once per channel means the history is what was there when the app first saw
    /// the file, and anything that arrives afterwards is genuinely new to the sweeps.
    /// </para>
    /// <para>
    /// IT READS. Registering sight WITHOUT reading would change what the two sets MEAN — from "the app
    /// has seen this file's contents" to "the app has seen this file exists" — and at unmute every
    /// historical crossing and malformed header in a pre-existing channel would be reported as new.
    /// That is the same waterfall from the other direction, and it hits hardest on a machine that
    /// starts muted.
    /// </para>
    /// <para>
    /// IT COMPOSES NOTHING ITSELF. The keys come from `Find_MalformedHeaders` and `Build_DedupeKey` —
    /// the sweeps' own functions — because a baseline that computed a key even slightly differently
    /// would record keys that never match, and every offence would be reported for ever. That failure
    /// would look exactly like the bug this closes (decision 12).
    /// </para>
    /// <para>
    /// UNCONDITIONAL, not muted-only: unmuted the outcome is identical to before — the pass records the
    /// history that each sweep's own first-sight branch would have recorded, and the sweeps then find
    /// nothing new — so DND and normal operation share one path and cannot drift apart. The cost is one
    /// read of the live file and its archive, ONCE per channel for the life of the process; a channel
    /// already baselined costs a set lookup and no I/O.
    /// </para>
    /// <para>
    /// THE SWEEPS NO LONGER TAKE THEIR OWN FIRST SIGHT. Each calls <see cref="Baseline_IfUnseen"/> on
    /// the channel it is about to read, so whoever reaches a file first — this pass or either sweep —
    /// takes sight of it once and absorbs both memos at that instant. There is no longer a moment
    /// where one consumer has seen a channel and another has not, which is where an arriving offence
    /// used to be absorbed as history by whichever got there second.
    /// </para>
    /// </summary>
    void Baseline_UnseenChannels_Silently()
    {
        Apply_Baselines(ChannelDiscovery.Find_ChannelFiles(_paths));
    }

    /// <summary>
    /// First sight of ONE channel, taken by whichever consumer reached it first. Called by both sweeps
    /// BEFORE they read the file, so the baseline's read is the earlier of the two and anything
    /// appearing between them is new rather than absorbed.
    /// </summary>
    void Baseline_IfUnseen(IDiscoveredChannel channel)
    {
        Apply_Baselines([channel]);
    }

    /// <summary>
    /// THE ONLY PLACE `_channelsFirstSighted` IS EVER REGISTERED, and the only place either memo is
    /// seeded with history. One registration is what forces one absorption: a caller that registered
    /// sight without recording both memos would leave the other consumer either re-announcing history
    /// or swallowing a new offence.
    /// </summary>
    void Apply_Baselines(IReadOnlyList<IDiscoveredChannel> channels)
    {
        foreach (var baseline in ChannelBaseline_Pass.Build_ForUnseenChannels(channels, _channelsFirstSighted))
        {
            _channelsFirstSighted.Add(baseline.ChannelFilePath);

            foreach (var key in baseline.MalformedKeys)
                _reportedMalformedHeaders.Add(key);

            foreach (var key in baseline.CrossingKeys)
                _screenedIndexCrossings.Add(key);
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

                // SILENT, though it wears a ⚠️: the fix is the session's, it has already been asked
                // for it, and the re-posted entries will ring on their own when they arrive.
                TelegramSendSounds.Silent,
                cancellationToken);
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        // Cost HERE: the owner is never told their entry is unreadable, and the tick dies with the notice.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        if (Is_Working(
                Running.SessionRoles.Supervisor, session.OrchId,
                Running.SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID,
                OwnerFacingSession_Locator.Get_UsageFile(_paths, session.OrchId, session)))
            return;

        List<string> waitingMembers = [];

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId);

            if (!File.Exists(channelFile))
                continue;

            var entries = ChannelHistory_Cache.Read_Entries(channelFile);

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

        // DEFERRED, NOT DROPPED — and BELOW the release above, which is reconciliation rather than
        // attention, exactly as Sync_Flag is in the ledger check. An earlier version of this bail sat
        // at the top of the method with a comment claiming nothing was reconciled below it; that line
        // IS the reconciliation, and it is the only release this key has (the member-scoped sites key
        // on "orchId/memberId", a disjoint namespace, and the stored timestamp is never read, so
        // nothing else and no expiry can heal it).
        //
        // What that cost: a spell that ENDED during a meeting — the owner directs the supervisor to
        // answer the reports, which is work a meeting explicitly continues — kept its token, and the
        // NEXT spell, with a genuinely unanswered report in it, could not be nudged at all
        // (rev-7 P2, 2026-08-13).
        var presence = Resolve_Presence(session.OrchId);

        if (OwnerPresence_Policy.Suppresses_SupervisorAttention(presence))
            return;

        // Once per quiet spell, not once per tick.
        if (_nudgedMemberUtc.ContainsKey(session.OrchId))
            return;

        // The token is spent only on a nudge that LANDED — the memo goes AFTER, because this key's
        // only release is the empty-waiting-list branch above, so recording it for an entry that was
        // never written leaves the supervisor un-nudged for the whole remaining stall. The helper
        // answers false for both reasons that can stop it: the owner is at the terminal, or the
        // channel was locked for the whole budget (which it names in the log).
        if (!Append_SupervisorAttention_UnlessMeeting(
                session.OrchId,
                $"unread reports waiting on you — {string.Join(", ", waitingMembers)}",
                $"{string.Join(", ", waitingMembers)} filed entries you have not answered, and nothing has moved since. Read each of those channels from your last entry down and give a verdict. If your monitor is no longer running, arm a fresh one.",
                presence))
            return;

        _nudgedMemberUtc[session.OrchId] = DateTime.UtcNow;

        // INFO, NOT WARNING. Nothing has failed here: this is routine coaching aimed at a SESSION,
        // and the app panel gives Warning the amber "awaiting review" brush with no textual level
        // tag, so an advisory logged at Warning is indistinguishable from a fault at a glance. The
        // owner counted them, 2026-08-25: *"the log is full of yellow messages"* — 92 of the 104
        // that day were lines like this one. It is the same argument this file already accepts for
        // Telegram a few lines below ("a ⚠️ for it reads like a fault report the owner must act
        // on"), applied to the colour instead of the channel.
        _log.Log_Info(session.OrchId, $"Supervisor had unanswered reports from {string.Join(", ", waitingMembers)} — nudged");
    }

    /// <summary>Returns whether the nudge was actually written — see the guard at the append.</summary>
    async Task<bool> Nudge_Implementer_Async(
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

        // Returns whether the nudge was actually delivered, and the caller MUST honour it. The memo
        // it writes on return starts the ORPHAN CLOCK: a member that does not wake within
        // ORPHAN_CONFIRM_MINUTES is killed and respawned, losing its context. So a nudge that was
        // never written would have the app destroy a healthy session for failing to answer a message
        // it was never sent — a locked channel escalating into the most destructive act the app has.
        if (!ChannelAppender.Append_AppEntry(channelFile, AppEntryAudiences.Agent, subject, body, DateTime.Now))
        {
            _log.Log_Warning(session.OrchId, $"{memberId} needed a nudge but the channel was locked — NOT nudged, and deliberately not counted as nudged; the next tick retries");
            return false;
        }

        var reason = dormantMidWork ? "went dormant mid-task" : "had unread traffic";
        // INFO, NOT WARNING. Nothing has failed here: this is routine coaching aimed at a SESSION,
        // and the app panel gives Warning the amber "awaiting review" brush with no textual level
        // tag, so an advisory logged at Warning is indistinguishable from a fault at a glance. The
        // owner counted them, 2026-08-25: *"the log is full of yellow messages"* — 92 of the 104
        // that day were lines like this one. It is the same argument this file already accepts for
        // Telegram a few lines below ("a ⚠️ for it reads like a fault report the owner must act
        // on"), applied to the colour instead of the channel.
        _log.Log_Info(session.OrchId, $"{memberId} {reason} for {Nudge_Wording.Describe_QuietFor(quietFor)} — nudged");
        Raise_OrchestrationActivity(session.OrchId);

        // The owner is NOT told. This is routine self-healing that already worked — the nudge is
        // written, the member wakes, the work continues — and a "⚠️" for it reads like a fault
        // report the owner must act on. Owner: "I don't want to receive, in telegram, stuff that is
        // not an actual problem; those messages look like problems."
        //
        // It stays in the log, where diagnosing this belongs. If a nudge does NOT work, the
        // orphan-recovery path speaks up — and that one IS a real problem, because context is lost.
        await Task.CompletedTask;
        return true;
    }

    // Recover_OrphanedImplementer_Async LIVED HERE, and it is gone on purpose rather than left
    // unreachable. It killed a member's process tree and respawned it whenever the escalation could
    // not prove the member alive; OrphanEscalation_Decider now reports instead, and a destructive
    // method with no callers is a loaded gun somebody rewires in six months.
    //
    // Introduced once and narrowed five times, every narrowing reacting to a false positive, with no
    // case recorded anywhere in this repo of it rescuing a genuinely stuck member. Its founding
    // incident (docs/investigations/2026-08-07-orphaned-session-watchers.md) was resolved by a human
    // typing into a terminal, before the code existed, and that write-up already said it: "the
    // watchdog never kills — it only spawns."
    //
    // Nudge_Wording.RESPAWN_SUBJECT stays where it is: Nudge_Decider reads it to recognise the
    // respawn entries already sitting in members' channels from before this change.

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
        foreach (var session in Sessions_ThisTick())
        {
            if (session.ClosedUtc != null)
                continue;

            _ledgerDebtSinceUtc.TryGetValue(session.OrchId, out var ledgerDebtSinceUtc);

            // The obligation is DURABLE, and the flag file is what carries it. This dictionary is
            // in-memory and BridgeState_Store persists only offsets and the last update id, so an
            // app restart used to empty it — after which Is_LedgerBehind returned false and
            // Sync_Flag DELETED the flag. In a system whose own lifecycle tree-kills and respawns
            // everything, that made the ledger debt droppable by restarting, and the comment on the
            // Stop hook claiming the enforcement was "delayed, never skipped" was simply false.
            //
            // The flag's own write time is when the debt was incurred, so re-seeding from it costs
            // no new persistence and lets the ordinary comparison clear it once PLAN.md is newer.
            if (ledgerDebtSinceUtc == default)
                ledgerDebtSinceUtc = Read_LedgerDebtStamp_OrDefault(session.OrchId);

            var isBehind = LedgerHealth_Tracker.Is_LedgerBehind(_paths, session.OrchId, ledgerDebtSinceUtc == default ? null : ledgerDebtSinceUtc);

            // The ORDER of the two halves is the correctness here, so the step owns it: the flag is
            // reconciled even in a meeting (lifting a block is not an interruption), while the alert
            // and its once-per-spell token are deferred. LedgerHealth_Step's own doc has the wedge
            // that the other order produces.
            // ONE read of presence for this orchestration's whole ledger decision — the mirror loop
            // decides here while the inbound loop can flip presence, so asking twice lets the token
            // be committed on one answer and the append refused on the other (rev-7 P5).
            var presence = Resolve_Presence(session.OrchId);

            var ledgerOutcome = LedgerHealth_Step.Reconcile(
                _paths,
                session.OrchId,
                isBehind,
                alreadyReported: _ledgerBehindReportedOrchIds.Contains(session.OrchId),
                suppressed: OwnerPresence_Policy.Suppresses_SupervisorAttention(presence));

            // FORGETTING is reconciliation and happens regardless; REMEMBERING is a claim that the
            // alert went out, so it waits for the append (see Nudge_IdleSupervisor for the same rule
            // and the same reason: the safe wrapper returns false where it once threw).
            if (!ledgerOutcome.RemembersReported)
                _ledgerBehindReportedOrchIds.Remove(session.OrchId);

            // The set is the "already told them" record and it is added only once the telling
            // SUCCEEDED. Adding it as the condition rather than as the consequence meant a locked
            // channel silenced the warning permanently while the flag file kept blocking the
            // supervisor's turn end — a deadlock with nothing anywhere explaining it. The helper
            // answers false for both reasons: the owner is at the terminal, or the channel stayed
            // locked (which it names in the log, and the next tick retries).
            if (ledgerOutcome.ShouldAppendAlert
                && Append_SupervisorAttention_UnlessMeeting(
                    session.OrchId,
                    "PLAN.md is behind your verdicts",
                    "You accepted implementer work without updating the task ledger, so the owner's progress bar is now wrong. Update PLAN.md before your next turn ends — the turn-end hook will block until you do.",
                    presence))
            {
                _ledgerBehindReportedOrchIds.Add(session.OrchId);
                // Info, not Warning: the flag IS the action and the turn-end hook enforces it. See the
                // note on the nudge sites — the owner reads amber as a fault.
                _log.Log_Info(session.OrchId, "Ledger is behind the supervisor's verdicts — flagged for the turn-end hook");
            }

            Report_LedgerShape(session);
            Report_StaleInProgress(session, presence);
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
        // Above the FINGERPRINT, not at the append: recording the offending set while suppressed
        // marks this shape as already reported, and the complaint never comes back after the meeting.
        //
        // Safe at the TOP here, unlike the nudge above, and for a reason worth stating rather than
        // asserting: the memo below is CONTENT-ADDRESSED, not a one-shot token. Any later change to
        // the offending set differs from what is remembered and fires on its own, and a set that
        // cleared during the meeting simply re-records as empty afterwards. A tick skipped here
        // therefore cannot strand anything — which is exactly what a skipped tick DOES do to a
        // presence token (rev-7 P2) or to a flag nothing else deletes (LedgerHealth_Step).
        var presence = Resolve_Presence(session.OrchId);

        if (OwnerPresence_Policy.Suppresses_SupervisorAttention(presence))
            return;

        var planFile = _paths.Get_PlanFile(session.OrchId);

        if (!File.Exists(planFile))
            return;

        var complaints = PlanShape_Validator.Find_UnrepresentableLines(UsageTotals_Reader.Read_Text_Safe(planFile));
        var fingerprint = string.Join("\n", complaints);

        // Re-report only when the offending set CHANGES, so a warning cannot become background noise.
        if (_reportedLedgerShapeByOrchId.TryGetValue(session.OrchId, out var reported) && reported == fingerprint)
            return;

        // No complaints: record the clean fingerprint and stop. Nothing is written, so there is
        // nothing that can fail to be written.
        if (complaints.Count == 0)
        {
            _reportedLedgerShapeByOrchId[session.OrchId] = fingerprint;
            return;
        }

        // Fingerprint AFTER the warning lands: it suppresses re-reporting until the offending set
        // changes, so recording it for a warning that was never written hides the problem until the
        // supervisor happens to edit those same lines.
        if (!Append_SupervisorAttention_UnlessMeeting(
            session.OrchId,
            "PLAN.md has lines that cannot show progress",
            $"{string.Join("\n", complaints)}\n\nUntil these are split, work on them renders as zero movement on the owner's bar no matter how often you update the ledger.",
            presence))
            return;

        _reportedLedgerShapeByOrchId[session.OrchId] = fingerprint;

        // INFO RATHER THAN WARNING, and deliberately the opposite call to the guard-not-in-force
        // line below: nothing has FAILED here. The panel gives Warning the amber
        // `StateAwaitingReview` brush and Error the red `StateBlocked` one, with no textual level
        // tag — so an advisory logged at Warning is an amber line among white ones and reads as a
        // fault. The owner read exactly that and reported it as "error messages about the format of
        // Plan.md" (2026-08-25). The actionable copy is the `[agent]` channel entry above, which
        // goes to the session that can split the line; this is only the app saying it sent one.
        // Same shape, and the same level, as the idle-member advisory in Retirement_Advisor.
        _log.Log_Info(session.OrchId, $"PLAN.md shape advisory sent to the supervisor — {complaints.Count} line(s) cannot show progress");
    }

    /// <summary>
    /// A `- [>]` line while NOTHING has been mid-turn for ten minutes is a false claim, and this is
    /// the guarantee the owner asked for after a session broke the rule it had just written down
    /// (2026-08-14): *"it absolutely must be guaranteed that it won't be messed up in the future by
    /// other sessions either."*
    ///
    /// It ARMS THE LEDGER DEBT rather than only complaining, because a complaint is what the written
    /// rule already was. The turn-end hook then blocks until PLAN.md is touched, and every honest
    /// answer — `[x]` finished, `[!]` waiting on something named, `[-]` dropped, or genuinely still
    /// `[>]` and back at work — is one edit that clears it.
    ///
    /// <see cref="StaleInProgress_Detector"/> holds the reasoning, including why this does not look
    /// for the word "merge" anywhere.
    /// </summary>
    void Report_StaleInProgress(IOrchestrationSession session, Telegram.OwnerPresenceModes presence)
    {
        // The quiet clock is per orchestration and starts the first tick that finds it quiet WITH an
        // unworked claim on the board. Restarting it whenever a session speaks is what makes this a
        // measure of quiet rather than of elapsed time.
        var working = Is_AnySessionWorking(session);

        if (working)
            _quietSinceUtc.Remove(session.OrchId);
        else if (!_quietSinceUtc.ContainsKey(session.OrchId))
            _quietSinceUtc[session.OrchId] = DateTime.UtcNow;

        var progress = Planning.PlanLedger_Parser.Parse_OrNull(
            UsageTotals_Reader.Read_Text_Safe(_paths.Get_PlanFile(session.OrchId)));

        // Tracked EVERY tick, busy or idle. The age rule is the whole point of the owner's ruling of
        // 2026-08-19, and a clock that ran only while the orchestration was quiet would have exactly
        // the blind spot they hit: the busiest orchestration is never checked at all.
        var firstSeen = Note_InProgressLines(session.OrchId, progress);

        // THE IDLE RULE FIRST, because it is the stronger claim — nothing is running AND the ledger
        // says otherwise. The age rule is weaker: the sessions may be perfectly busy and only the
        // LINE has gone still. So it speaks only when the idle rule is silent, and each keeps its own
        // wording rather than one message hedging between two different reasons.
        var unworked = _quietSinceUtc.TryGetValue(session.OrchId, out var quietSince)
            ? Planning.StaleInProgress_Detector.Find_UnworkedInProgressLines(
                progress, anySessionWorking: working, quietFor: DateTime.UtcNow - quietSince)
            : [];

        IReadOnlyList<string> byAge = unworked.Count > 0
            ? []
            : Planning.StaleInProgress_Detector.Find_UnmovedInProgressLines(progress, firstSeen, DateTime.UtcNow);

        if (unworked.Count == 0 && byAge.Count == 0)
        {
            // Released, so a set that comes back after being put right is flagged again.
            _reportedStaleInProgress.Remove(session.OrchId);
            return;
        }

        var offending = unworked.Count > 0 ? unworked : byAge;

        var describe = unworked.Count > 0
            ? Planning.StaleInProgress_Detector.Describe(offending)
            : Planning.StaleInProgress_Detector.Describe_Unmoved(offending);

        // CONTENT-ADDRESSED, like the shape complaint: it re-fires when the offending SET changes,
        // so a session that fixes one line and leaves another still hears about the one it left.
        var signature = string.Join("\n", offending);

        if (_reportedStaleInProgress.TryGetValue(session.OrchId, out var reported) && reported == signature)
            return;

        if (!Append_SupervisorAttention_UnlessMeeting(
                session.OrchId,
                "PLAN.md claims work that nobody is doing",
                describe,
                presence))
            return;

        _reportedStaleInProgress[session.OrchId] = signature;

        // The debt AFTER the telling lands, so a session is never blocked by a demand it was never
        // sent — the deadlock this file has already paid for once.
        _ledgerDebtSinceUtc[session.OrchId] = DateTime.UtcNow;

        // Info, not Warning — an advisory about ledger SHAPE, not a failure. Same reasoning as the
        // nudge sites above.
        _log.Log_Info(session.OrchId, unworked.Count > 0
            ? $"PLAN.md claims {unworked.Count} line(s) in progress while nothing is running — flagged for the turn-end hook"
            : $"PLAN.md has {byAge.Count} line(s) claiming [>] unchanged for over an hour — flagged for the turn-end hook");
    }

    /// <summary>Runaway guard: a per-orchestration token ceiling the owner sets in config.json.</summary>
    async Task Send_BudgetAlerts_Async(CancellationToken cancellationToken)
    {
        var budgetTokens = _configProvider.Get_Current().OrchestrationTokenBudget;

        if (_telegramClient == null || budgetTokens == null || budgetTokens.Value <= 0)
            return;

        foreach (var session in Sessions_ThisTick())
        {
            if (session.ClosedUtc != null)
                continue;

            var (_, tokens) = UsageTotals_Reader.Build_OrchestrationTotals(_paths, session);

            // The token is spent only on an alert that actually WENT OUT — this took it before
            // consulting the mode (rev-7 P1), and then before the send itself (rev-6). Nothing
            // anywhere releases it, so either order lost the alert for the life of the process.
            if (!BudgetAlert_Planner.Should_Send(
                    tokens,
                    budgetTokens.Value,
                    alreadyAlerted: _budgetAlertedOrchIds.Contains(session.OrchId),
                    Resolve_EffectiveMode(session.OrchId)))
                continue;

            var alertText = $"⚠️ {session.DisplayName ?? session.OrchId}: {UsageTotals_Reader.Format_Tokens(tokens)} used — past the {UsageTotals_Reader.Format_Tokens(budgetTokens.Value)} budget you set.";

            try
            {
                await _telegramClient.Send_Message_Async(session.TelegramTopicId, alertText, TelegramSendSounds.Rings, cancellationToken);

                // THE ONLY WRITE. After a CONFIRMED send, so a failure retries on the next tick
                // instead of being remembered as delivered — the rule from "the owner's answer
                // survives a failed Telegram send", which this file had not carried across.
                _budgetAlertedOrchIds.Add(session.OrchId);
                _log.Log_Warning(session.OrchId, alertText);
            }
            // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
            // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
            // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
            // Cost HERE: every REMAINING session's budget alert in the same sweep.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
    /// <summary>
    /// The account's worst live reading per usage window, from the status-line probe files.
    ///
    /// <para>
    /// ONE READER, TWO CONSUMERS (decision 12). The alert scan and the dispatcher pause ask exactly
    /// the same question of exactly the same files, and a second copy of this loop is a second place
    /// for the two window-selection rules below to drift — at which point the app could pause on one
    /// reading and alert about another.
    /// </para>
    /// </summary>
    Dictionary<string, (double Percent, DateTime? WindowResetsAtUtc)> Read_CurrentLimitWindows()
    {
        Dictionary<string, (double Percent, DateTime? WindowResetsAtUtc)> maxPercents = [];

        // FROM THE INJECTED CLOCK, both readings, because this method decides which windows are still
        // live and the pause decides what to do about them — two clocks for one decision is two ways
        // to disagree. In production the injected clock IS the system clock, so nothing changes; what
        // it buys is that "the window came back and dispatch resumed" is a property a test can move
        // the clock across instead of waiting out.
        var nowUtc = _clock.UtcNow;
        var nowLocal = nowUtc.ToLocalTime();

        // Only probe files with a window that has not already reset. Probe files are never
        // deleted, so without this the alert scan folded five-day-old closed orchestrations into
        // "the account right now" — which is how .limit-alerts.json latched at 100% and stopped
        // alerting entirely.
        // Read_Text_Safe rather than File.ReadAllText: a live session rewriting its probe file
        // used to throw a sharing violation out of this loop and abort the whole check.
        foreach (var usageFile in RateLimits_Reader.Find_UsageFiles_WithLiveWindow(_paths, nowLocal))
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

        return maxPercents;
    }

    async Task Check_UsageLimits_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null || _telegramMuted)
            return;

        if ((DateTime.UtcNow - _lastLimitCheckUtc).TotalSeconds < LIMIT_CHECK_INTERVAL_SECONDS)
            return;

        _lastLimitCheckUtc = DateTime.UtcNow;

        try
        {
            var maxPercents = Read_CurrentLimitWindows();

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
                await _telegramClient.Send_Message_Async(null, alertText, TelegramSendSounds.Rings, cancellationToken);
            }

            Save_LimitAlertState(state);
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        // Cost HERE: the REMAINING thresholds in the same loop — a timeout on the 90% alert can swallow the 100% one.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(GLOBAL_ORCH_ID, "Usage limit check failed", ex);
        }
    }

    long? _generalDashboardMessageId;
    string? _generalDashboardText;
    bool _generalDashboardIdLoaded;
    DateTime? _generalDashboardFailedAtUtc;

    /// <summary>
    /// ONE MESSAGE IN GENERAL, EDITED IN PLACE: every open orchestration at a glance, so the owner
    /// sees the whole machine without asking and without a notification per update. The per-topic
    /// status line already works this way; this is the same idea one level up.
    ///
    /// It writes only when the TEXT CHANGED — the shared decider's rule — which is why the composer
    /// puts no clock in it. Everything else here is execution: the decisions that can be pure
    /// functions are, because this class is internal sealed with no InternalsVisibleTo and nothing
    /// decided inside it can be reached by the suite.
    /// </summary>
    async Task Push_GeneralDashboard_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        // SILENCED DROPS THIS, DEFERRED DOES NOT — narrowed on the owner's ruling of 2026-09-09.
        // The gate used to refuse every mode but Normal, and the dashboard was the loudest thing in
        // the app when it was written: it posted with sound. It is a silent edit of one message now,
        // so under 🌙 there is nothing to protect the owner from, and freezing it costs them the one
        // surface their check-in ritual reads. Under 🔕 they are watching the same thing live in a
        // terminal and asked not to have it twice, which is the mode's whole contract.
        if (Resolve_EffectiveMode(ChannelDiscovery.GENERAL_ORCH_ID) == TelegramDeliveryModes.Silenced)
            return;

        // Backoff after a failure. Without it the retry is a 2-second hammer at an endpoint that is
        // already failing — the shape that earns a bot a server-side throttle. The failure stamp is
        // what holds it off, because the text has not changed and so cannot.
        if (_generalDashboardFailedAtUtc != null
            && (DateTime.UtcNow - _generalDashboardFailedAtUtc.Value).TotalSeconds < _timing.MirrorRetryBackoffSeconds)
            return;

        Load_GeneralDashboardMessageId_Once();

        var text = Telegram.GeneralDashboard_Composer.Compose(
            Build_ProgressReportText(null),
            _configProvider.Get_Current().TelegramStatusScreenshots);
        var action = Telegram.TopicStatusLine_Decider.Decide(text, _generalDashboardText, _generalDashboardMessageId);

        if (action == Telegram.TopicStatusActions.None)
            return;

        try
        {
            // GENERAL'S COMMAND BAR RIDES ON THE DASHBOARD, for the reason the topic bar rides on
            // PULSE: this is the one message in General the app already keeps current and already
            // keeps near the bottom, so the buttons stay within reach without a pin the owner has
            // refused.
            //
            // THE ROW-AWARE CALLS, NOT THE PLAIN ONES. A plain edit sends no reply_markup and
            // Telegram reads the absence as "remove the keyboard", so editing this message the old
            // way would strip the bar off it on the very next tick — the same trap the per-topic
            // line documents at its own edit.
            var commandButtonRows = Build_GeneralCommandButtonRows();

            if (action == Telegram.TopicStatusActions.Edit && _generalDashboardMessageId != null)
            {
                await _telegramClient.Edit_MessageTextWithButtonRows_Async(_generalDashboardMessageId.Value, text, commandButtonRows, cancellationToken);
            }
            else
            {
                var messageId = await _telegramClient.Send_MessageWithButtonRows_Async(null, text, commandButtonRows, TelegramSendSounds.Silent, cancellationToken);

                if (messageId == null)
                    return;

                _generalDashboardMessageId = messageId;
                Save_GeneralDashboardMessageId(messageId.Value);
            }

            _generalDashboardText = text;
            _generalDashboardFailedAtUtc = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // "not modified" is Telegram agreeing with us: the desired state already holds, so it is a
            // SUCCESS. Recording the text is what stops it being retried every tick for ever.
            if (Telegram.TopicStatusLine_Decider.Is_MessageAlreadyCurrent(ex.Message))
            {
                _generalDashboardText = text;
                return;
            }

            // The message is gone — the topic was cleared, or the owner deleted it. Forget the id so
            // the next tick posts a fresh dashboard instead of editing into a hole for ever.
            if (Telegram.TopicStatusLine_Decider.Is_MessageGone(ex.Message))
            {
                _generalDashboardMessageId = null;
                _generalDashboardText = null;
                Delete_GeneralDashboardState_BestEffort();
                return;
            }

            _generalDashboardFailedAtUtc = DateTime.UtcNow;
            _log.Log_Warning(GLOBAL_ORCH_ID, $"General dashboard not updated ({ex.Message}) — retrying after the backoff");
        }
    }

    /// <summary>
    /// Read ONCE per process, not per tick: the file only ever changes because this class wrote it.
    /// A miss here would be re-read 30 times a minute for the lifetime of the app.
    /// </summary>
    void Load_GeneralDashboardMessageId_Once()
    {
        if (_generalDashboardIdLoaded)
            return;

        _generalDashboardIdLoaded = true;
        _generalDashboardMessageId = Telegram.GeneralDashboard_Store.Parse_MessageId_OrNull(
            Read_FileText_Safe(_paths.GeneralDashboardStateFile));
    }

    void Save_GeneralDashboardMessageId(long messageId)
    {
        try
        {
            File.WriteAllText(_paths.GeneralDashboardStateFile, Telegram.GeneralDashboard_Store.To_Json(messageId));
        }
        catch (Exception ex)
        {
            // Costs one duplicate dashboard after the next restart, never this one's update.
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Could not remember the General dashboard message id: {ex.Message}");
        }
    }

    void Delete_GeneralDashboardState_BestEffort()
    {
        try
        {
            if (File.Exists(_paths.GeneralDashboardStateFile))
                File.Delete(_paths.GeneralDashboardStateFile);
        }
        catch
        {
            // The id is already forgotten in memory, which is what the next tick reads.
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
            //
            // The topic's OWN deferral is asked about separately from the effective mode, because
            // presence turns a Deferred topic into a Silenced one — which would poll it and consume
            // the very backlog the deferral was holding. See Freezes_Offsets.
            if (EffectiveMode_Resolver.Freezes_Offsets(Resolve_EffectiveMode(channel.OrchId), session.TelegramMode))
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
            var entries = ChannelHistory_Cache.Read_Entries(append.Channel.FilePath);

            foreach (var index in supervisorEntryIndexes)
            {
                if (!Planning.LedgerHealth_Tracker.Is_VerdictAt(entries, index))
                    continue;

                _ledgerDebtSinceUtc[append.Channel.OrchId] = DateTime.UtcNow;
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

                // TWO REFUSALS SURVIVE THE FILTER'S REMOVAL, and neither is remembered for a later
                // release — which is why the suppressed-entry filing that used to sit here is gone.
                //
                // What reaches this branch now is an owner RESTATEMENT (their own words quoted back,
                // which must not be replayed at all) or an EMPTY body. The empty one is the sharp
                // case and it is why the filing had to go rather than merely stop mattering: it is
                // not a restatement, so it was filed and then released five minutes later, RINGING,
                // wearing "nothing has moved for 5 min — sending you the last thing it said". A
                // blank message, with a notification, about nothing.
                //
                // The subject is still passed because Should_Push's signature carries it for the
                // callers that predate this change.
                if (!OwnerPush_Policy.Should_Push(entry.RawText, ownerIsWaiting, entry.Subject))
                    continue;

                lock (_ownerStateLock)
                {
                }

                // The flag is deliberately NOT cleared here — it is cleared after the send below.
                // Clearing it at this point consumed the owner's wait on an ATTEMPT: when the send
                // then failed, the append was left unconfirmed (by design, so it retries), but the
                // re-emitted entry now read the flag as false, re-evaluated as ordinary narration
                // and was SUPPRESSED. The answer to a question the owner actually asked was dropped
                // silently — they asked, the supervisor replied, and nothing ever reached them.
                answersTheOwnersWait = true;
            }

            // THE SPEAKER PREFIX IS HELD APART FROM THE AGENT'S WORDS for the whole of this block.
            // It is app chrome glued to the first line, and every marker below is anchored at column
            // 0 — so a question whose first line was `QUESTION:` was invisible to the extractor and
            // survived only because a derived question stood behind it. Composed back at the end.
            var (speaker, text) = MirrorText_Formatter.Format_Parts(append.Channel, entry);

            // Special lines in the entry become REAL Telegram artifacts, never raw text:
            // IMAGE: <path> lines upload as photos; OPTION: <label> lines render as inline
            // decision buttons the owner can tap instead of typing.
            var photoPaths = Extract_MarkerLines(ref text, Channels.ChannelGrammar.Bare(Channels.ChannelGrammar.IMAGE));

            // ATTACH: <path> lines upload as DOCUMENTS — an HTML mockup, a CSV, a report — under
            // EntryAttachment_Policy's containment, which IMAGE: never had (see the policy's header).
            var attachmentPaths = Extract_MarkerLines(ref text, Channels.ChannelGrammar.Bare(Channels.ChannelGrammar.ATTACH));
            // The five lines a question owes the owner. Extracted here, judged by
            // OwnerQuestion_Contract, and forwarded ONLY complete — see Refuse_Question below.
            var optionLabels = Extract_MarkerLines(ref text, OwnerQuestion_Contract.OPTION_MARKER);
            var questionLines = Extract_MarkerLines(ref text, OwnerQuestion_Contract.QUESTION_MARKER);
            var recommendLines = Extract_MarkerLines(ref text, OwnerQuestion_Contract.RECOMMEND_MARKER);
            var riskLines = Extract_MarkerLines(ref text, OwnerQuestion_Contract.RISK_MARKER);
            var rowLines = Extract_MarkerLines(ref text, OwnerQuestion_Contract.ROW_MARKER);

            // What happens if the owner never answers. Both optional, both agent-written and
            // therefore untrusted — QuestionDirectives_Parser drops anything it cannot read rather
            // than guessing, and a DEFAULT without a DEADLINE is dropped as meaningless.
            var deadlineValues = Extract_MarkerLines(ref text, QuestionDirectives_Parser.DEADLINE_MARKER);
            var defaultValues = Extract_MarkerLines(ref text, QuestionDirectives_Parser.DEFAULT_MARKER);
            var directives = QuestionDirectives_Parser.Parse(deadlineValues, defaultValues, optionLabels.Count);

            // AND THE DECLARED STATE COMES OUT OF THE TEXT, like every other marker above it. It is
            // read for PULSE's second field (Build_TopicStatusFields) and rendered there; leaving it
            // in the body sends the owner the same sentence twice on every supervisor turn, once
            // wearing a bare protocol keyword. The value is deliberately discarded here: this call
            // exists for the REMOVAL, and the field's own reader parses the entry from the channel.
            Extract_MarkerLines(ref text, DeclaredState_Parser.MARKER.TrimEnd(':'));

            // COMPLETE OR NOT AT ALL. The body still reaches the owner — a formatting fault must
            // never cost them a message — but an incomplete question grows no buttons, and the
            // agent is told every missing line at once so a refusal is one round trip and not four.
            OwnerQuestion? question = null;
            var draft = new OwnerQuestionDraft(questionLines, optionLabels, recommendLines, riskLines, rowLines);

            if (OwnerQuestion_Contract.Is_Attempted(draft))
            {
                var faults = OwnerQuestion_Contract.Check(draft);

                if (faults.Count == 0)
                    question = OwnerQuestion_Contract.Build(draft);
                else
                    Refuse_Question(append.Channel, faults);
            }

            // SPLIT, NEVER DROPPED, and numbered when there is more than one piece. This used to be
            // a bare TelegramMessage_Chunker.Chunk(text), which measures the MARKDOWN while Telegram
            // counts the HTML this path renders it into — see OwnerMessage_Chunker for the wedge
            // that produced, and for why an owner-facing message being long is a splitting problem
            // and never a reason for the owner to receive nothing.
            // FOLDED FIRST, THEN CHUNKED, and the order is the whole design: the fold is a
            // PRESENTATION choice — the opening in the clear, the rest behind one tap — while 4096 is
            // a hard refusal. See OwnerMessage_Folder, which degrades to the chunker's own output for
            // every entry it cannot improve on.
            // COMPOSED BACK HERE, and nowhere earlier: everything above reads the agent's own words.
            text = speaker + text;

            var prose = _configProvider.Get_Current().TelegramProse;
            var pieces = OwnerMessage_Folder.Fold_ForOwner(text, prose.FoldLongEntriesAbove);

            try
            {
                foreach (var piece in pieces)
                    Remember_TopicMessage(threadId, await Send_MirrorPiece_Async(threadId, piece, Resolve_EntrySound(entry), cancellationToken));

                // ALSO, never INSTEAD. Every piece above has already been sent; the file is a
                // convenience for an entry long enough that reading it in the chat is the work.
                await Send_EntryDocument_BestEffort_Async(
                    threadId, pieces.Count, prose.AttachEntriesAbove, entry.Subject, text,
                    append.Channel.OrchId, Resolve_EntrySound(entry), cancellationToken);

                // Counts toward away detection: a supervisor message that reached the phone and is
                // so far unanswered. Only the supervisor's own voice counts — app notices and
                // presence lines are not something the owner is expected to reply to.
                if (append.Channel.IsOwnerChannel && ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author) && pieces.Count > 0)
                {
                    Nudge_IfTooVerbose(append.Channel.OrchId, text, pieces.Count);
                    Coach_OnContractFaults(append.Channel, entry.Body);

                    if (Note_SupervisorSpokeToOwner_AndJustWentQuiet(append.Channel.OrchId))
                        await Enter_QuietMode_Async(append.Channel.OrchId, cancellationToken);
                }

                // The buttons NEVER ride on the body. Agents write long, thorough messages, and
                // options hanging off the bottom of one arrive on a phone as a wall of text with
                // taps underneath and no visible question. They get their own short message.
                if (question != null)
                {
                    await Send_QuestionWithButtons_Async(
                        threadId, question, append.Channel,
                        directives.Deadline, directives.DefaultOptionIndex, cancellationToken);
                }

                foreach (var photoPath in photoPaths)
                    await Send_EntryPhoto_BestEffort_Async(threadId, photoPath, append.Channel, Resolve_EntrySound(entry), cancellationToken);

                foreach (var attachmentPath in attachmentPaths)
                    await Send_EntryAttachment_BestEffort_Async(threadId, attachmentPath, append.Channel, Resolve_EntrySound(entry), cancellationToken);

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

                    Persist_EngineState();
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
    /// Sends one mirrored chunk, ALWAYS as HTML, because a channel entry is Markdown: agents write
    /// `**bold**`, bullet lists and fenced mockups because that is how they write, and until
    /// 2026-09-07 the mirror sent every one of them through plain `sendMessage`. The owner read
    /// `**GOAL 2: V1 LIVE**` on their phone, markers and all.
    ///
    /// <para>
    /// THE CHUNKING HAPPENS ON THE MARKDOWN, before this method, and that ordering is load-bearing:
    /// <see cref="TelegramMessage_Chunker"/> splits on line boundaries and each chunk is rendered
    /// from Markdown on its own, so a split can never land inside a tag this renderer opened. The
    /// one construct a split does cut is a fenced block, and the renderer closes an unterminated
    /// fence itself rather than emitting half a &lt;pre&gt;.
    /// </para>
    /// <para>
    /// The 400-only fallback, and why a timeout must NOT take it, is argued in
    /// <see cref="TelegramProse_Sender"/> — this site is where that rule was first written down
    /// (the sixteen-site sweep regression rev-6 caught), and moving the send there is what stops it
    /// being re-derived per call site.
    /// </para>
    /// </summary>
    /// <param name="piece">
    /// Both readings of the same message, from <see cref="OwnerMessage_Folder"/>: the HTML the
    /// primary send uses, and the Markdown the plain-text fallback re-sends. They are not
    /// interchangeable once a fold is involved — the HTML carries a collapsed quotation that has no
    /// Markdown source — which is why the pair travels together instead of being re-derived here.
    /// </param>
    /// <summary>
    /// WHO WROTE IT DECIDES WHETHER IT RINGS — the owner's ruling of 2026-09-09, in one place.
    ///
    /// <para>
    /// *"If the supervisor writes to me, I must know it — that rings. Status, receipts and app
    /// bookkeeping do not ring."* So an entry whose author SPEAKS TO THE OWNER (the supervisor, or
    /// the solo that stands in for one) arrives with a notification; an App entry — a confirmation,
    /// a coaching line, a status post — arrives silently and is there when they next look.
    /// </para>
    /// <para>
    /// <see cref="ChannelAuthor_Kinds.Speaks_ToOwner"/> is the same predicate the away-detection and
    /// the stall alert already use for "was that the supervisor talking", so a new author kind
    /// cannot ring here while counting as silence there.
    /// </para>
    /// </summary>
    static TelegramSendSounds Resolve_EntrySound(Channels.ChannelEntry.IChannelEntry entry)
    {
        return ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author) ? TelegramSendSounds.Rings : TelegramSendSounds.Silent;
    }

    async Task<long?> Send_MirrorPiece_Async(long? threadId, (string Markdown, string Html) piece, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        var client = _telegramClient
            ?? throw new Exception("Send_MirrorPiece_Async called without a Telegram client");

        return await TelegramProse_Sender.Send_Rendered_Async(
            client, _log, GLOBAL_ORCH_ID, threadId, piece.Html, piece.Markdown, sound, cancellationToken);
    }

    /// <summary>
    /// The entry itself, attached as <c>&lt;subject-slug&gt;.md</c>, when it took more messages than
    /// the owner's <c>attachEntriesAbove</c> allows for.
    ///
    /// <para>
    /// BEST EFFORT, LIKE THE ENTRY PHOTO, and for a stronger reason: the messages are already on the
    /// phone by the time this runs. A failed upload must therefore cost the attachment and nothing
    /// else — letting it throw would return false from Mirror_Append_Async, leave the append
    /// unconfirmed, and re-send every chunk of a message the owner has already read.
    /// </para>
    /// </summary>
    async Task Send_EntryDocument_BestEffort_Async(
        long? threadId,
        int deliveredMessages,
        int attachAbove,
        string? subject,
        string markdown,
        string orchId,
        TelegramSendSounds sound,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_telegramClient == null || !OwnerDocument_Builder.Should_Attach(deliveredMessages, attachAbove))
                return;

            await _telegramClient.Send_Document_Async(
                threadId,
                OwnerDocument_Builder.Build_FileName(subject),
                OwnerDocument_Builder.Build_Content(markdown),
                OwnerDocument_Builder.Build_CaptionHtml(markdown),
                sound,
                cancellationToken);
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled; a bare rethrow would escalate a failed upload into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Entry document not attached ({deliveredMessages} messages): {ex.Message}");
        }
    }

    /// <summary>
    /// A topic's OWN mode wins over the app-wide setting — "silence just this one while I work in
    /// its terminal" must survive someone flipping the global DND, and vice versa. Only when the
    /// topic is Normal does the app-wide setting apply.
    /// </summary>
    TelegramDeliveryModes Resolve_EffectiveMode(string orchId)
    {
        // GATHERS, decides nothing — the ORDER of these opinions is the decision, and it is not one
        // a reader or a test could see while it lived here (rev-4, 2026-08-13).
        return EffectiveMode_Resolver.Resolve(
            Resolve_Presence(orchId),
            isGeneral: orchId == ChannelDiscovery.GENERAL_ORCH_ID,
            topicMode: _store.Get_Session_OrNull(orchId)?.TelegramMode ?? TelegramDeliveryModes.Normal,
            appWideDeferred: _telegramMuted,
            appWideSilenced: _silenceAllTopics);
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
        OwnerQuestion question,
        Channels.DiscoveredChannel.IDiscoveredChannel channel,
        TimeSpan? deadline,
        int? defaultOptionIndex,
        CancellationToken cancellationToken)
    {
        var questionPrompt = QuestionPrompt_Builder.Build(question.Question);
        var optionLabels = question.Options;

        var client = _telegramClient
            ?? throw new Exception("Send_QuestionWithButtons_Async called without a Telegram client");

        // A SECOND OPEN QUESTION IS COACHED, NOT REFUSED — and the refusal was tried first.
        //
        // `kit/commands/supervisor.md:323` carries "A QUESTION ENDS YOUR TURN — one open question at
        // a time" as a HARD RULE, and 721 lines later the same file says "build that and ask in
        // passing". Nothing here ever counted, so the owner ended up with a merge question, 004, 267
        // and 277 live at once and reported that they could not answer a moving target.
        //
        // ENFORCING IT AS A HARD CAP BROKE SOMETHING REAL, which is how this ended up advisory:
        // DecisionStateSurvivesARestartTests opens two questions in one orchestration on purpose,
        // /pending filters a topic's questions in the plural, and every question already carries its
        // own nonce, deadline and default. The app deliberately supports several open decisions and
        // made each one individually resolvable. The hole was never the count — it was that a TYPED
        // reply could not be attributed, and AnswerBinding_Decider now closes exactly that: with two
        // open, a typed reply binds nothing and the owner taps the one they meant.
        //
        // So the count is a smell to tell the session about, not a thing to prevent. The audience is
        // Agent, so this coaching never reaches the phone.
        // DECIDED BEFORE THE SEND, so the coaching below is not written about questions this send is
        // about to close — and swept AFTER it, so a send that fails leaves the older ones open.
        var toSupersede = Find_QuestionsToSupersede(channel.OrchId);

        if (toSupersede.Count == 0 && Would_BeASecondOpenQuestion(channel.OrchId))
        {
            _log.Log_Info(
                channel.OrchId,
                "a second question went out while one was still open — the owner must tap, since a typed reply cannot be bound");

            ChannelAppender.Append_AppEntry(
                channel.FilePath,
                AppEntryAudiences.Agent,
                "a second question while one is still open",
                "A question of yours was already open with the owner when this one went out. Both are "
                + "live and both are tappable, but a TYPED reply now binds to neither — the app cannot "
                + "tell which one they meant, so it will leave both open rather than guess. Prefer "
                + "waiting for the first answer.",
                DateTime.Now);
        }

        var prompt = questionPrompt;

        // THE OPTIONS MOVE INTO THE MESSAGE WHEN THEY ARE TOO LONG TO READ ON A BUTTON. The owner,
        // 2026-08-24: "buttons don't wrap, so when a session asks me a question I often can't read all
        // the button text." Telegram truncates a long label with an ellipsis and there is no markup
        // that changes that, so the only place the full wording can live is the message itself — and
        // the button then carries a number pointing at the line they can read. Short options are left
        // exactly as they were: numbering two-word choices would be worse than the problem.
        var layout = Telegram.OptionButtons_Layout.Build(optionLabels);
        var promptWithOptions = layout.OptionListText == null ? prompt : $"{prompt}\n\n{layout.OptionListText}";

        // THE RECOMMENDATION RIDES WITH THE QUESTION, not in the body above it. The body is what
        // the owner scrolls past on a lock screen; this message is what they answer from, and a
        // question they cannot answer without scrolling back is the one they defer. The row code is
        // beside it for the same reason — asked for three times in one afternoon (2026-09-07),
        // because "272, 267" and "the trial one" were the same conversation an hour apart.
        var recommendation = question.Recommendation;

        var promptWithGuidance = $"{promptWithOptions}\n\n💡 {recommendation}"
            + (question.RowCode == null ? string.Empty : $"\n📎 {question.RowCode}");

        var guardrails = _configProvider.Get_Current().Guardrails;

        // THE OPTIONS AND THE BODY COUNT TOO, and reading the question line alone was a hole with a
        // natural shape: `QUESTION: How should I proceed?` / `OPTION: Push the release branch to
        // main` / `OPTION: Hold` classified as safe, and one tap on a pocketed phone delivered the
        // push with no code. QuestionPrompt_Builder also caps a derived question at one sentence and
        // strips fenced blocks, so a ```rm -rf``` shown to the owner was invisible here as well.
        //
        // A false positive costs one typed code. A false negative costs the operation this whole
        // gate exists for, so the surface being matched is deliberately the widest one the owner
        // actually reads.
        // DECLARED OR DETECTED, never declared INSTEAD of detected: `RISK: low` on a question whose
        // own option says "push to main" does not unlock it. A declaration can only ever ADD a lock,
        // which is what makes it safe to let the asker write one.
        var matchedPattern = HighRisk_Classifier.Find_MatchedPattern_OrNull(
            Compose_RiskSurface(questionPrompt, optionLabels),
            guardrails.HighRiskPatterns);

        var isHighRisk = question.DeclaredHighRisk || matchedPattern != null;

        // A HIGH-RISK QUESTION LOSES ITS DEFAULT HERE, at the point of asking, rather than being
        // trusted not to have one. The agent may well have written DEFAULT: 1 on a push question in
        // good faith; nothing downstream may act on it.
        var effectiveDefaultIndex = isHighRisk ? null : defaultOptionIndex;

        var askedUtc = _clock.UtcNow;
        var deadlineUtc = deadline == null ? (DateTime?)null : askedUtc + deadline.Value;

        var promptWithTerms = Compose_QuestionTerms(promptWithGuidance, optionLabels, isHighRisk, deadlineUtc, effectiveDefaultIndex);

        var buttons = Register_Buttons(threadId, optionLabels, layout.ButtonLabels, promptWithTerms, isHighRisk, out var buttonGroupId);

        // THROUGH THE RENDERER like the mirrored body above it, and for the same reason: this text is
        // the agent's QUESTION: line and their OPTION: wording, so it carries their Markdown. The
        // BUTTONS do not — a label is never parsed, whatever it contains.
        var messageId = await TelegramProse_Sender.Send_WithButtons_Async(
            client, _log, channel.OrchId, threadId, promptWithTerms, buttons,

            // A QUESTION RINGS. It is the supervisor's, it stops their work until it is answered,
            // and it is the one shape the owner has always wanted to be interrupted for.
            TelegramSendSounds.Rings,
            cancellationToken);

        Remember_TopicMessage(threadId, messageId);

        // Remembered UNANSWERED, so away mode can mark it parked. This is the exact thing that made
        // the owner's plane landing unusable: a screen of questions with no way to tell which were
        // still live.
        if (messageId != null)
        {
            lock (_ownerStateLock)
            {
                _openQuestions[messageId.Value] = new OpenQuestionRecord
                {
                    MessageId = messageId.Value,
                    OrchId = channel.OrchId,
                    Text = promptWithTerms,
                    AskedUtc = askedUtc,
                    ButtonGroupId = buttonGroupId,
                    DeadlineUtc = deadlineUtc,
                    DefaultOptionIndex = effectiveDefaultIndex,
                    IsHighRisk = isHighRisk,
                };
            }

            if (isHighRisk)
            {
                _log.Log_Info(
                    channel.OrchId,
                    matchedPattern != null
                        ? $"Question classified HIGH RISK (matched '{matchedPattern}') — a tap will require the read-back code"
                        : "Question classified HIGH RISK (declared by the asker, no pattern matched) — a tap will require the read-back code");
            }

            await Supersede_OlderQuestions_Async(channel, toSupersede, cancellationToken);

            // It asked; now it stops. The hook refuses every tool until the owner answers — unless
            // the owner is IN this orchestration's terminal, where the answer is being typed at the
            // session itself and the flag would freeze the very conversation it is waiting for.
            if (channel.IsOwnerChannel)
            {
                if (OwnerPresence_Policy.Should_RaiseAwaitingAnswer(Resolve_Presence(channel.OrchId)))
                    Raise_AwaitingAnswerFlag(channel.OrchId);
                else
                    _log.Log_Info(channel.OrchId, "Terminal mode: question asked WITHOUT the awaiting-answer block — the owner is in this session's terminal");
            }
        }

        // The question, its buttons and its deadline are one decision and are saved together: a
        // crash between them would leave a keyboard on the phone with no question behind it, or a
        // question with no way to answer by tapping.
        Persist_EngineState();
    }

    /// <summary>
    /// Everything a high-risk pattern may be found in: the question and every option label — what
    /// the owner is actually deciding. ONE composition, used by both the decision and the log line
    /// that explains it, because two would be two places for the surface to drift.
    ///
    /// <para>
    /// THE ENTRY BODY WAS IN HERE AND IS NOT ANY MORE. It was added so that
    /// `QUESTION: How should I proceed?` / `OPTION: Push the release branch to main` could not
    /// classify as safe — but that danger is in the OPTION, which is still read. What the body
    /// added was the narrative around the question, and on 2026-09-07 it locked four pure product
    /// questions in one afternoon because the prose said "the deployed engine crashes on these
    /// keys" and "a deploy check already blocks this from shipping". Nothing was being deployed.
    /// A false positive is not free: it is a 4-digit code in front of a decision that needed none,
    /// and a lock that fires on what the agent happened to mention is one the owner learns to type
    /// through — which costs exactly the operation this gate exists for.
    /// </para>
    /// </summary>
    static string Compose_RiskSurface(string questionPrompt, IReadOnlyList<string> optionLabels)
    {
        return $"{questionPrompt}\n{string.Join('\n', optionLabels)}";
    }

    /// <summary>
    /// Appends the TERMS of the question to its own text: what happens if nobody answers, and
    /// whether a tap will be enough.
    ///
    /// <para>
    /// IN THE MESSAGE, NOT ONLY IN THE APP. A deadline the owner cannot see is a decision taken
    /// behind their back — they scroll past a question, it lapses, and the first they know of it is
    /// the consequence. Stating it is what makes the default legitimate.
    /// </para>
    /// <para>
    /// It also becomes the text every later edit is built from — the reminder, the answered record,
    /// the timeout notice — so the terms stay attached to the question in the chat history rather
    /// than living only in a field.
    /// </para>
    /// </summary>
    static string Compose_QuestionTerms(
        string promptWithOptions,
        IReadOnlyList<string> optionLabels,
        bool isHighRisk,
        DateTime? deadlineUtc,
        int? defaultOptionIndex)
    {
        List<string> terms = [];

        if (isHighRisk)
            terms.Add("🔐 High risk — a tap is not enough: you will be asked to type a 4-digit code shown here.");

        if (deadlineUtc != null)
        {
            if (defaultOptionIndex != null && defaultOptionIndex.Value < optionLabels.Count)
            {
                // The owner counts from 1, as the numbered list under the question does.
                terms.Add($"⏳ If you do not answer by {deadlineUtc.Value:HH:mm} UTC, option {defaultOptionIndex.Value + 1} ({optionLabels[defaultOptionIndex.Value]}) is taken.");
            }
            else
            {
                terms.Add($"⏳ If you do not answer by {deadlineUtc.Value:HH:mm} UTC, this is DENIED (timeout).");
            }
        }

        if (terms.Count == 0)
            return promptWithOptions;

        return $"{promptWithOptions}\n\n{string.Join('\n', terms)}";
    }

    /// <summary>
    /// <paramref name="optionTexts"/> is what the SESSION receives when a button is tapped;
    /// <paramref name="buttonLabels"/> is what the OWNER sees on it. They are parallel lists, matched
    /// by index, and they were one list until the owner reported that a long option is unreadable on
    /// a phone button — the label may now be shortened and numbered, while the text handed back to
    /// the session stays whole.
    /// </summary>
    IReadOnlyList<(string Data, string Label)> Register_Buttons(
        long? threadId,
        IReadOnlyList<string> optionTexts,
        IReadOnlyList<string> buttonLabels,
        string questionText,
        bool isHighRisk,
        out long groupId)
    {
        if (buttonLabels.Count != optionTexts.Count)
            throw new Exception($"Register_Buttons got {buttonLabels.Count} labels for {optionTexts.Count} options — they are matched by index");

        List<(string Data, string Label)> buttons = [];

        // ONE NONCE FOR THE WHOLE QUESTION, and the index is what distinguishes the options. That is
        // what makes a payload readable — "nonce X, option 2" — without a lookup, and it keeps every
        // sibling of one keyboard obviously related in the log.
        var nonce = CallbackToken.New_Nonce();

        var expiresUtc = _clock.UtcNow.AddMinutes(_configProvider.Get_Current().Guardrails.ButtonExpiryMinutes);

        lock (_buttonLock)
        {
            // One GROUP per button message: the first tap invalidates all its siblings.
            _buttonGroupSequence++;
            groupId = _buttonGroupSequence;

            for (var index = 0; index < optionTexts.Count; index++)
            {
                var data = CallbackToken.Build(nonce, index);

                _buttonOptions[data] = new PendingButtonRecord
                {
                    Data = data,
                    ThreadId = threadId,
                    OptionText = optionTexts[index],
                    GroupId = _buttonGroupSequence,
                    QuestionText = questionText,
                    ExpiresUtc = expiresUtc,
                    IsHighRisk = isHighRisk,
                };

                _buttonOrder.Enqueue(data);
                buttons.Add((data, buttonLabels[index]));
            }

            // Every question also offers ONE way to ask back — and it used to offer two. "❔ Explain
            // the options" spent the buttons and asked the supervisor to explain and re-ask; this
            // one left the question and its keyboard untouched. Now that a tap here closes the
            // question like any other, the two are the same gesture under two labels, and the owner
            // had to pick between synonyms before they could ask their real question.
            //
            // The button's label is short; the text the supervisor receives is the full instruction,
            // which is why the two differ here.
            var talkData = CallbackToken.Build(nonce, optionTexts.Count);

            _buttonOptions[talkData] = new PendingButtonRecord
            {
                Data = talkData,
                ThreadId = threadId,
                OptionText = OwnerPush_Policy.TALK_REQUEST,
                QuestionText = questionText,
                GroupId = _buttonGroupSequence,
                ExpiresUtc = expiresUtc,

                // ASKING TO TALK IS NEVER HIGH RISK, whatever the question is about. It takes no
                // decision — it asks the supervisor to explain — so putting a code in front of it
                // would make the safe way out of a dangerous question the hardest button to press.
                IsHighRisk = false,

                // It consumes the group and closes the question exactly like an option; what it does
                // not do is record a choice.
                AnswersNothing = true,
            };

            _buttonOrder.Enqueue(talkData);
            buttons.Add((talkData, OwnerPush_Policy.TALK_LABEL));

            while (_buttonOrder.Count > BUTTON_REGISTRY_CAP)
                _buttonOptions.Remove(_buttonOrder.Dequeue());
        }

        return buttons;
    }

    async Task Send_EntryPhoto_BestEffort_Async(long? threadId, string photoPath, IDiscoveredChannel channel, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        try
        {
            if (_telegramClient == null)
                return;

            if (!Approve_OwnerFile(channel, photoPath, asPicture: true))
                return;

            await _telegramClient.Send_Photo_Async(threadId, photoPath, sound, cancellationToken);
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        // Cost HERE: a BEST-EFFORT photo — a path whose entire contract is that failing costs nothing.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(channel.OrchId, $"Entry photo send failed for '{photoPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Whether a file an agent named may be sent, and the refusal WRITTEN DOWN when it may not.
    ///
    /// <para>
    /// SHARED BY BOTH MARKERS, and that is the fix rather than a tidy-up. `IMAGE:` checked only that
    /// the file existed and then handed an HTML mockup to `sendPhoto`; Telegram answered `400
    /// IMAGE_PROCESS_FAILED`, the catch below logged a warning, and NOBODY was told — not the owner,
    /// not the agent. Measured 2026-09-08 04:02: four mockups the owner had asked for, all four
    /// dropped, and the supervisor telling him in good faith that it had sent them and they had
    /// gone nowhere. A silent drop is the worst shape a failure can take here, because the session
    /// then argues with the owner from a false premise.
    /// </para>
    /// <para>
    /// THE ROOTS ARE THREE, and the third is where the files actually are: an agent writing
    /// something FOR the owner puts it in `~/mockups/`, which is neither the repository nor the
    /// channel folder. A containment that forbids the one place the workflow uses is a containment
    /// nobody can obey — while `~/.ssh` stays as far outside it as it ever was.
    /// </para>
    /// </summary>
    bool Approve_OwnerFile(IDiscoveredChannel channel, string path, bool asPicture)
    {
        var session = _store.Get_Session_OrNull(channel.OrchId);
        List<string> allowedRoots = [];

        if (!string.IsNullOrWhiteSpace(session?.RepoPath))
            allowedRoots.Add(session.RepoPath);

        var channelFolder = Path.GetDirectoryName(channel.FilePath);

        if (!string.IsNullOrWhiteSpace(channelFolder))
            allowedRoots.Add(channelFolder);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(home))
            allowedRoots.Add(Path.Combine(home, OWNER_FILES_FOLDER));

        var exists = File.Exists(path);
        var length = exists ? new FileInfo(path).Length : 0L;

        // MEASURED ONLY FOR A PICTURE, and only when the file is really there: Telegram's dimension
        // rule applies to sendPhoto alone, and reading a header off a missing path buys a caught
        // exception rather than an answer. Null when the format cannot be measured — which means
        // ALLOW, see ImageDimensions_Reader.
        var dimensions = exists && asPicture ? Telegram.ImageDimensions_Reader.Read_FromFile_OrNull(path) : null;

        var verdict = EntryAttachment_Policy.Decide(path, allowedRoots, exists, length, asPicture, dimensions);

        if (verdict == AttachmentVerdicts.Send)
            return true;

        var reason = EntryAttachment_Policy.Describe(verdict, path, allowedRoots, asPicture, dimensions);
        _log.Log_Warning(channel.OrchId, reason);

        // NOT DEDUPED, unlike contract coaching: every refused file is a file the owner did not get,
        // and the agent must know each time. Audience Agent — never the phone.
        ChannelAppender.Append_AppEntry(
            channel.FilePath, AppEntryAudiences.Agent, "a file you sent the owner was NOT delivered", reason, DateTime.Now);

        return false;
    }

    /// <summary>Where an agent puts something it made FOR the owner, under their home.</summary>
    const string OWNER_FILES_FOLDER = "mockups";

    /// <summary>
    /// One <c>ATTACH:</c> line → one <c>sendDocument</c>, or one refusal the AGENT reads. Best effort
    /// like the photo: the body is already on the phone, so a failed upload costs the attachment and
    /// nothing else. The policy is <see cref="EntryAttachment_Policy"/>; this is only its point of
    /// effect — the roots it may attach from are the orchestration's repository and its own
    /// supervision folder, resolved here because only the engine knows both.
    /// </summary>
    async Task Send_EntryAttachment_BestEffort_Async(
        long? threadId, string attachmentPath, IDiscoveredChannel channel, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        try
        {
            if (_telegramClient == null)
                return;

            if (!Approve_OwnerFile(channel, attachmentPath, asPicture: false))
                return;

            var bytes = await File.ReadAllBytesAsync(attachmentPath, cancellationToken);
            var fileName = Path.GetFileName(attachmentPath);
            var captionHtml = $"📎 {System.Net.WebUtility.HtmlEncode(fileName)}";

            await _telegramClient.Send_Document_Async(threadId, fileName, bytes, captionHtml, sound, cancellationToken);
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled. Canonical account in Refresh_TopicStatusLines_Async.
        // Cost HERE: a BEST-EFFORT attachment — the body it belongs to is already on the phone.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(channel.OrchId, $"Entry attachment send failed for '{attachmentPath}': {ex.Message}");
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
            var topicId = await _telegramClient.Create_ForumTopic_Async(channel.OrchId, Resolve_TopicColour_OrNull(session.RepoName), cancellationToken);
            _store.Set_TelegramTopicId(channel.OrchId, topicId);
            _log.Log_Info(channel.OrchId, $"Telegram topic created (thread id {topicId})");
            Remove_TopicCreationPin_FireAndForget(channel.OrchId, topicId);
            return topicId;
        }
        // DELIBERATELY NOT FILTERED, AND THIS COMMENT IS THE REASON — DO NOT "COMPLETE" THE SWEEP HERE.
        //
        // FOURTEEN sibling sites on the tick path took `when (cancellationToken.IsCancellationRequested)`
        // so an HttpClient timeout stops being read as a shutdown. THIS ONE MUST NOT, and the asymmetry
        // is not an oversight: at every other site falling through to the generic catch costs a LOG LINE
        // and a retry next tick. Here it costs a MISDELIVERY — the catch below returns null, and a null
        // thread id mirrors the entry to the GENERAL topic instead of the orchestration's own.
        //
        // A lost tick is recoverable and invisible. A message delivered to the wrong topic is neither:
        // the owner reads it in the wrong conversation and has no way to tell it was misrouted, and
        // nothing anywhere records that it went to the wrong place. Aborting the tick is the cheaper
        // failure, so a transient timeout is left to abort.
        //
        // If this ever needs to change, the fix is to make the null case STOP MIRRORING rather than
        // redirect — not to add the filter here.
        //
        // THE COUNT ABOVE HAS BEEN WRONG TWICE AND IS RECONCILED HERE SO IT CANNOT DRIFT SILENTLY AGAIN.
        // It first said "sixteen", which was the number of sites ADDRESSED — this one among them, and it
        // did not take the filter. Corrected to fifteen, which was right for that commit and wrong one
        // commit later, because reverting Send_MirrorChunk_Async and wrapping a new call site both moved
        // it. The arithmetic, verifiable by grep at any time:
        //
        //     6  filtered at master 2110c56
        //  + 14  sibling sites converted here (11 of the original 12, B1, B2 and Flush_OwnerDeliveries)
        //  +  1  NEW try/catch wrapping the unprotected call in Announce_SupervisorFree_Async
        //  = 21  filtered now, of 44 total
        //
        // Send_MirrorChunk_Async (renamed Send_MirrorPiece_Async when the fold gave it two readings of
        // the same message to send) is the twelfth of the original twelve and was REVERTED as a regression,
        // which is why it is 11 and not 12. A durable comment carrying a count owes the reader the sum
        // that produces it; without one, the next person to move a site has no way to tell whether the
        // number was already stale.
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

    /// <summary>
    /// <paramref name="dispatchPaused"/> defers ONLY the requests that start work.
    ///
    /// <para>
    /// It gated all of them for one commit, which was wrong in the direction that matters: closing an
    /// orchestration, closing a member and setting DND cost nothing, and the first two FREE sessions.
    /// So the owner, looking at an app that had stopped by itself because the account was spent, typed
    /// /close and watched nothing happen — with /limits explaining the pause but not why their close
    /// had vanished. A pause that also removes the ability to stop things is not a safety measure.
    /// </para>
    /// </summary>
    void Process_PendingRequests(bool dispatchPaused)
    {
        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        foreach (var malformedRequest in pending.MalformedRequests)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Malformed request file deleted — {malformedRequest.Reason}: {malformedRequest.FilePath}");

            // Tell the AGENT why, in its own channel — a silently deleted request file used to
            // look to the supervisor like an action that simply never happened.
            if (malformedRequest.OrchId != null && _store.Get_Session_OrNull(malformedRequest.OrchId) != null)
                Append_OrchestrationAppEntry(malformedRequest.OrchId, AppEntryAudiences.Agent, "request REJECTED", $"Your request file was rejected: {malformedRequest.Reason}. Fix it and drop a new file (same action string).");

            Delete_RequestFile(malformedRequest.FilePath);
        }

        // THE THREE THAT SPAWN. Left on disk while paused, so they run at the resume.
        if (!dispatchPaused)
        {
            Process_StartRequests(pending);
            Process_AddImplementerRequests(pending);
            Process_PromoteOrchestrationRequests(pending);
        }

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
                    // A BASIC ORCHESTRATION HAS NO SUPERVISOR TO RE-MODEL, and spawning one here does
                    // not just add a session — it flips the shape PERMANENTLY. `Respawn_Supervisor`
                    // stamps `SupervisorSpawnedUtc`, the factory merges it with a plain coalesce and
                    // no wasSet escape hatch, and nothing anywhere clears it, including the close
                    // paths. So "use fable for the CRM one" about a basic orchestration used to put a
                    // supervisor beside the solo on one channel and make every later promotion
                    // request answer "already has a supervisor" for ever.
                    //
                    // The concierge is explicitly authorised to drop this request for any orch id, so
                    // the guard belongs here rather than in its instructions.
                    if (Sessions.OrchestrationShape.Is_BasicOrchestration(_store.Get_Session(request.OrchId).SupervisorSpawnedUtc))
                    {
                        Append_OrchestrationAppEntry(
                            request.OrchId, AppEntryAudiences.Owner,
                            "model change REFUSED — this is a basic orchestration and has no supervisor",
                            "A basic orchestration is one session with no supervisor, so there is no supervisor model to set. Spawning one here would permanently turn it into a crew and block any real promotion.\n\n"
                            + "Use role 'implementer' to change the model of the session that IS here.");

                        Archive_ResolvedRequest_BestEffort(request.SourceFilePath, "no-supervisor");
                        continue;
                    }

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
                    request.OrchId, AppEntryAudiences.Owner,
                    $"model set: {request.Role} → {request.Model} — {request.Reason}",
                    "Affected sessions respawned on the new model; they resume from their channels.");
            }
            catch (Exception ex)
            {
                _log.Log_Error(request.OrchId, $"set-model {request.Role} → '{request.Model}' failed", ex);
                Append_OrchestrationAppEntry(request.OrchId, AppEntryAudiences.Owner, $"set-model FAILED: {request.Role} → {request.Model}", $"Error: {ex.Message}");
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

                // THE TOPIC NAME IS NOT PUSHED HERE, AND THAT IS THE FIX. This used to hand
                // `request.Name` straight to editForumTopic — the RAW display name, with no glyphs.
                // Every decoration the owner reads their topic list by (💻 presence, 🔕/🌙 delivery,
                // ❓/⛔ reply-wanted, 🧪 awaiting-test, ✅ done, ✈ away) was wiped by a rename that
                // knew about none of them. The owner reported it as "the pc icon goes away by itself",
                // and it needed no state change at all — naming an orchestration was enough.
                //
                // It was worse than cosmetic because it was fire-and-forget: it raced
                // Sync_TopicNames_BestEffort_Async within this very tick and could land AFTER it, so
                // Telegram kept the bare name while `_appliedTopicNames` had already recorded the
                // decorated one as applied. That dictionary has no Remove, so its guard then
                // suppressed every future correction for the life of the process.
                //
                // Set_DisplayName above is the whole job. Process_PendingRequests runs at the top of
                // the tick and Sync_TopicNames_BestEffort_Async near the end of the SAME tick, so the
                // new name still reaches Telegram this tick — fully decorated, through the one gated
                // path, with no second writer to race it. Do not add a direct rename back here.
                // NO TERMINAL RENAME HERE EITHER, and for a different reason than the topic above.
                // A live rename was attempted from 2026-08-06 until the owner reported on 2026-08-21
                // that "the terminal renaming is not working": it called SetWindowText, which changes
                // the OS caption of the wt.exe window and nothing Windows Terminal actually draws. It
                // returned true every time. The name is carried at SPAWN instead, so a running window
                // takes it at its next respawn — the trade the owner chose, knowing the cost.
                //
                // The log says that plainly rather than claiming a rename, because the old line
                // ("Named 'X'") read as though the terminals had been renamed too.
                // SUPERVISOR TOO, not just members. Members excludes it (it has its own pid field),
                // so keying the note off Members alone would stay silent for a crew whose implementers
                // have all been closed — while the supervisor's own terminal sits there, still titled
                // with the old name, which is exactly the window the note is about.
                var hasLiveWindow = session.SupervisorPid != null
                    || session.CommunicatorSpawnedUtc != null
                    || session.Members.Any(member => member.ClosedUtc == null);

                var windowNote = hasLiveWindow
                    ? " — open terminals keep their current title until their next respawn"
                    : string.Empty;

                _log.Log_Info(request.OrchId, $"Named '{request.Name}'{windowNote}");
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

    /// <summary>
    /// THE COLOUR THIS REPOSITORY'S TOPICS ARE CREATED WITH — brief F1; the rotation itself is
    /// <see cref="TopicColor_Rotation"/>'s and the file format is
    /// <see cref="ConfigRepoColor_Writer"/>'s. This is only the point of effect, which is the one
    /// place that knows which repository a topic belongs to.
    ///
    /// <para>
    /// ASSIGNED ON FIRST USE AND WRITTEN DOWN, because the rotation depends on what has already
    /// been handed out and the repo list is reordered at runtime — a colour derived from a position
    /// would change under the owner every time they dragged a row. A repository not in config.json
    /// at all (removed while an orchestration on it is still open) gets no colour rather than a
    /// wrong one.
    /// </para>
    /// <para>
    /// NEVER FAILS THE TOPIC. A colour is the least important thing happening on this path; every
    /// way of not getting one ends in null, and the topic is created in Telegram's default.
    /// </para>
    /// </summary>
    int? Resolve_TopicColour_OrNull(string repoName)
    {
        try
        {
            var repos = _configProvider.Get_Current().Repos;
            var repo = repos.FirstOrDefault(entry => string.Equals(entry.Name, repoName, StringComparison.OrdinalIgnoreCase));

            if (repo == null)
                return null;

            if (repo.TopicColor != null)
                return repo.TopicColor;

            var inUse = repos.Where(entry => entry.TopicColor != null).Select(entry => entry.TopicColor!.Value).ToList();
            var colour = TopicColor_Rotation.Pick_ForNewRepo(inUse);

            // A colour that cannot be persisted is still USED for this topic — the alternative is a
            // repository whose topics are all Telegram's default while the file stays unwritable.
            // The next topic re-picks; the rotation is deterministic, so it very likely picks the
            // same one again.
            if (!ConfigRepoColor_Writer.Persist_Colour(_paths, repo.Name, colour))
                _log.Log_Warning(GLOBAL_ORCH_ID, $"Topic colour for repo '{repo.Name}' could not be written to config.json — this topic uses it, the next one re-picks");

            return colour;
        }
        catch (Exception ex)
        {
            // Broad by intent: this must never be the reason a topic is not created.
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Could not resolve a topic colour for repo '{repoName}' ({ex.Message}) — creating the topic in Telegram's default colour");
            return null;
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
                    Append_GeneralAppEntry(AppEntryAudiences.Owner,
                        $"start-orchestration FAILED: '{request.RepoQuery}'",
                        $"Could not resolve repo '{request.RepoQuery}' to exactly one configured repo. Known repos: {known}. Ask the owner which one is meant, then drop a new request.");
                    continue;
                }

                // THE REQUEST WINS, THE CONFIG SETTLES THE SILENCE. A default is what decides when
                // nothing was said; it never overrules a request that named its shape. Resolved here,
                // at the moment of effect, rather than in the reader — config.json can change under a
                // request that has been sitting in the folder, and the shape the owner gets should be
                // the one their config says NOW.
                var configuredDefaultIsBasic = _configProvider.Get_Current().Defaults.OrchestrationIsBasic;
                var isBasic = request.IsBasic ?? configuredDefaultIsBasic;

                var session = isBasic
                    ? _launcher.Start_BasicOrchestration(repo.Name, repo.Path)
                    : _launcher.Start_Orchestration(repo.Name, repo.Path);

                // THE TASK IS FILED AFTER THE LAUNCH, NEVER BEFORE, and the order is the whole
                // mechanism. A bridge-driven session baselines its channels at REGISTRATION
                // (Running.SessionRunner.BridgeDrivenRunnerModel): anything already in owner-channel.md
                // when the supervisor is registered is absorbed as HISTORY and starts no turn. Written
                // here — after Start_Orchestration has returned, so after the registration — the task is
                // traffic, and it is the first thing the new session is handed.
                //
                // FROM owner, because that is whose words these are. The app already appends the owner's
                // Telegram messages to this file under that author (Append_OwnerEntry is the same call
                // the inbound bridge makes), so the supervisor meets its first task in exactly the shape
                // every later one arrives in, and nothing new had to be invented to carry it.
                var taskFiled = !string.IsNullOrWhiteSpace(request.Task)
                    && ChannelAppender.Append_OwnerEntry(_paths.Get_OwnerChannelFile(session.OrchId), request.Task!, DateTime.Now);

                if (!string.IsNullOrWhiteSpace(request.Task) && !taskFiled)
                {
                    // SAID TO THE OWNER, not swallowed. The channel was held for the whole budget by
                    // another writer — vanishingly unlikely on a channel created seconds ago, and if it
                    // ever happens the orchestration is up with nothing to do, which is precisely the
                    // state that looks like the app working and is not.
                    _log.Log_Error(session.OrchId, $"The task that came with the start request could not be appended to '{session.OrchId}' owner channel — the orchestration is up but has not been told what to do", null);
                    Append_GeneralAppEntry(AppEntryAudiences.Owner,
                        $"orchestration '{session.OrchId}' started WITHOUT its task",
                        $"Orchestration '{session.OrchId}' is up, but its owner channel was locked and the task could not be written into it. Tell it what you need in its own topic.");
                }

                if (taskFiled)
                {
                    // THE OWNER IS WAITING FOR AN ANSWER TO IT, and nothing else would ever say so.
                    // The flag is raised when the owner types into a TOPIC; this task arrived through
                    // the concierge instead, so without this line the crew's first reply is filtered
                    // as narration (OwnerPush_Policy: a supervisor entry pushes only when it asks,
                    // answers something asked, or reports being blocked). The orchestration would come
                    // up, get a topic, do the work and tell the owner nothing — asked from the phone,
                    // answered into a room the phone never rang for.
                    lock (_ownerStateLock)
                        _ownerAwaitingAnswer.Add(session.OrchId);

                    // Persisted for the reason R1 gives about its own flag: an answer in flight across
                    // a restart must not be silently downgraded to narration on the way back up.
                    Persist_EngineState();
                }

                var crew = isBasic
                    ? "One solo session spawned — no supervisor, no implementers; you talk to it directly."
                    : "Supervisor and implementer imp-1 spawned;";

                var task = taskFiled
                    ? " Its task is already in its owner channel and it starts on it."
                    : string.Empty;

                // WHO CHOSE THE SHAPE, said in the entry the owner reads. It is the only place a
                // mistyped defaults.orchestrationMode can become visible: the config layer has no log
                // of its own, so a word neither 'basic' nor 'full' falls back silently there and would
                // otherwise look like a key that reads right and never takes effect. Here the first
                // orchestration after the edit says which shape was used and why.
                var chose = request.IsBasic == null
                    ? $" The request named no mode, so the configured default ({(configuredDefaultIsBasic ? OrchestrationModes.BASIC : OrchestrationModes.FULL)}) chose the shape."
                    : string.Empty;

                Append_GeneralAppEntry(AppEntryAudiences.Owner,
                    $"orchestration '{session.OrchId}' started",
                    $"Orchestration '{session.OrchId}' started on repo '{repo.Name}' ({repo.Path}). {crew} its Telegram topic appears on its first channel entry.{task}{chose}");
            }
            catch (Exception ex)
            {
                _log.Log_Error(GLOBAL_ORCH_ID, $"start-orchestration for '{request.RepoQuery}' failed", ex);
                Append_GeneralAppEntry(AppEntryAudiences.Owner,
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
                var session = _launcher.Add_Member(request.OrchId, request.Kind, request.Model);
                var newMember = session.Members[session.Members.Count - 1];
                var kindWord = request.Kind.ToString().ToLowerInvariant();

                // The model is part of what the owner is paying for, so it rides beside the reason.
                var modelWord = request.Model == null ? "" : $" ({request.Model})";

                var briefingHint = request.Kind == MemberKinds.Reviewer
                    ? $"New reviewer '{newMember.MemberId}' spawned for orchestration '{request.OrchId}' — READ-ONLY (it cannot edit or commit). Its channel is {newMember.MemberId}/channel.md — brief it there, and the brief MUST name a review DEPTH (quick | standard | deep | max) and exactly what to review."
                    : $"New implementer '{newMember.MemberId}' spawned for orchestration '{request.OrchId}'. Its channel is {newMember.MemberId}/channel.md — brief it there.";

                // The REASON rides in the subject because App entries mirror subject-only — the
                // owner must never see a session appear (and burn tokens) without knowing why.
                Append_OrchestrationAppEntry(
                    request.OrchId, AppEntryAudiences.Owner,
                    $"{kindWord} '{newMember.MemberId}'{modelWord} added — {request.Reason}",
                    briefingHint);
            }
            catch (Exception ex)
            {
                _log.Log_Error(request.OrchId, $"add-{request.Kind.ToString().ToLowerInvariant()} failed", ex);
                Append_OrchestrationAppEntry(request.OrchId, AppEntryAudiences.Owner, $"add-{request.Kind.ToString().ToLowerInvariant()} FAILED", $"Error: {ex.Message}");
            }
            finally
            {
                Delete_RequestFile(request.SourceFilePath);
            }
        }
    }

    /// <summary>
    /// A member close EXECUTES ON ARRIVAL. Owner directive 2026-08-13, reversing their own decision of
    /// 2026-08-12 in their own words: *"currently I'm being asked for confirmation for the closure of
    /// each element of the session, any reviewer or implementer. That wasn't what I wanted, I wanted
    /// to be asked for confirmation to close the entire orchestration session. I trust the supervisor
    /// to manage its subordinate windows."*
    ///
    /// THE ORCHESTRATION CLOSE KEEPS ITS TAP — see <see cref="Process_CloseOrchestrationRequests"/>.
    /// That one is irreversible: it ends every session including the supervisor's and deletes the
    /// topic. This one ends a session whose replacement costs a spawn. The two actions share
    /// <see cref="CloseConfirmation_Parking"/> and every sweep around it, so the difference between
    /// them now rests on nothing but which of these two methods a request reaches. It is worth
    /// knowing that is the whole of the distinction.
    ///
    /// The 2026-08-12 guard was not wrong for its own reason — a close does throw away a session's
    /// work, and before it there was no owner-facing route at all. What the owner corrected is WHOSE
    /// judgement that spends: retiring a finished member is the supervisor's own crew management, and
    /// asking them to approve each one made them the bottleneck on a decision they had delegated.
    /// </summary>
    /// <summary>
    /// A solo asking for its basic orchestration to become a full crew.
    ///
    /// TWO REFUSALS BEFORE THE OWNER IS EVER INVOLVED, and both go to the SOLO rather than to them.
    /// A request that cannot be honoured is not a decision anybody should be asked to adjudicate: the
    /// owner's tap answers "should this become a crew", never "is this request well-formed". Parking
    /// a broken one spends a tap on "no" and teaches them to distrust the button.
    ///
    /// The handover requirement is the one that matters. The solo's session ENDS on promotion and its
    /// in-context state dies with it; the channel is the only thing the supervisor inherits, so a
    /// promotion granted without that entry silently discards whatever was never written down.
    /// </summary>
    void Process_PromoteOrchestrationRequests(IPendingRequests pending)
    {
        foreach (var request in pending.PromoteOrchestrationRequests)
        {
            try
            {
                var session = _store.Get_Session_OrNull(request.OrchId);

                if (session == null || session.ClosedUtc != null)
                {
                    Append_GeneralAppEntry(AppEntryAudiences.Owner, 
                        $"promote-orchestration FAILED: '{request.OrchId}'",
                        $"No open orchestration '{request.OrchId}' — nothing was promoted.");

                    Archive_ResolvedRequest_BestEffort(request.SourceFilePath, "unpromotable");
                    continue;
                }

                // THE SAME RULE THE EXECUTION USES, so the answer at park time and the answer at tap
                // time cannot differ in kind — only in how stale they are. A half-promoted
                // orchestration (stamped, solo still running) is INCOMPLETE rather than "already a
                // crew", so the retry the failure message asks for is allowed through instead of
                // being refused by the app's own guard.
                var readiness = Sessions.OrchestrationShape.Decide_PromotionReadiness(
                    session.SupervisorSpawnedUtc,
                    session.Members.Any(member => member.ClosedUtc == null && Sessions.MemberKind_Ids.Resolve_Kind(member.MemberId) == Sessions.MemberKinds.Solo));

                if (readiness == Sessions.PromotionReadiness.AlreadyACrew || readiness == Sessions.PromotionReadiness.NothingToPromote)
                {
                    // AGENT, NOT OWNER. Every one of these four notices is addressed to the SOLO —
                    // the method's own summary says "TWO REFUSALS BEFORE THE OWNER IS EVER INVOLVED,
                    // and both go to the SOLO rather than to them", and solo.md promises the same:
                    // "it refuses it to YOU rather than bothering the owner with it". The audience
                    // argument said Owner, so every refusal, and the HELD notice, put a push on the
                    // owner's phone about a conversation between the app and a session — including
                    // one whose whole text is "The owner has NOT been asked".
                    Append_OrchestrationAppEntry(
                        request.OrchId, AppEntryAudiences.Agent,
                        readiness == Sessions.PromotionReadiness.AlreadyACrew
                            ? "promotion REFUSED — this orchestration already has a supervisor"
                            : "promotion REFUSED — there is no solo session here to promote",
                        readiness == Sessions.PromotionReadiness.AlreadyACrew
                            ? "A promotion turns a basic orchestration into a full crew, and this one is already a crew. Nothing was changed and the owner was not asked."
                            : "A promotion replaces the solo session with a supervisor, and this orchestration has no live solo. Nothing was changed and the owner was not asked.");

                    Archive_ResolvedRequest_BestEffort(request.SourceFilePath, readiness == Sessions.PromotionReadiness.AlreadyACrew ? "already-a-crew" : "nothing-to-promote");
                    continue;
                }

                if (!HandoverEntry_Detector.Has_HandoverEntry(Read_OwnerChannelEntries(request.OrchId)))
                {
                    Append_OrchestrationAppEntry(
                        request.OrchId, AppEntryAudiences.Agent,
                        "promotion REFUSED — file your HANDOVER entry first, then ask again",
                        "Your session ENDS when a promotion happens, and everything you know that is not in this channel dies with it. The supervisor that replaces you inherits this file and nothing else.\n\n"
                        + $"So append an entry whose SUBJECT carries `{HandoverEntry_Detector.HANDOVER_MARKER}` — where the work really stands, what you tried that did not work, what is half-done and in which files, and the traps — and then drop the request again.\n\n"
                        + "The owner has NOT been asked and nothing was changed.");

                    Archive_ResolvedRequest_BestEffort(request.SourceFilePath, "no-handover-entry");
                    continue;
                }

                var parkedPath = CloseConfirmation_Parking.Park(_paths, request.SourceFilePath);

                _log.Log_Info(request.OrchId, $"promote-orchestration held for the owner's confirmation ({parkedPath})");

                Append_OrchestrationAppEntry(
                    request.OrchId, AppEntryAudiences.Agent,
                    "promotion HELD — the owner confirms this with a tap",
                    $"Nothing has changed yet and you are still the session here. The owner has been asked.\n\n"
                    + $"Reason relayed: {request.Reason}\n\n"
                    + $"You will get an entry here either way. If they do not answer within {CloseConfirmation_Parking.EXPIRY_HOURS} hours it lapses and you are told — do NOT re-drop it in the meantime, and carry on working.");
            }
            catch (Exception ex)
            {
                // Fail closed and say so: the guard could not be honoured, so NOTHING was promoted.
                _log.Log_Error(request.OrchId, "promote-orchestration could not be held for confirmation — NOT promoted", ex);

                Append_OrchestrationAppEntry(
                    request.OrchId, AppEntryAudiences.Agent,
                    "promotion NOT held — nothing was changed",
                    $"Your promotion request could not be held for the owner's confirmation ({ex.Message}), so it was not acted on and you are still the session here. Ask again if it is still wanted.");

                Archive_ResolvedRequest_BestEffort(request.SourceFilePath, "unheld");
            }
        }
    }

    /// <summary>
    /// The orchestration's owner channel across its WHOLE history — the file the solo writes and the
    /// supervisor inherits, plus the archive compaction has moved older entries into. Empty when it
    /// cannot be read, which the caller treats as "no handover entry": a channel this app cannot read
    /// is not evidence that the solo wrote one.
    ///
    /// A LIVE-FILE READ WAS A DIRECT HIT ON DECISION 13. `Channel_Compactor` moves all but the newest
    /// 45 entries out once a channel passes 90, and `owner-channel.md` is on its list — so a solo that
    /// filed its handover, was declined or lapsed once, and kept working would eventually be told to
    /// "file your HANDOVER entry first", instructing it to do the thing it had already done. That is
    /// the option-lab-2 shape the decision was written from, and the repo already ships the helper
    /// that spans both files.
    /// </summary>
    IReadOnlyList<Channels.ChannelEntry.IChannelEntry> Read_OwnerChannelEntries(string orchId)
    {
        return ChannelHistory_Counter.Read_Entries(_paths.Get_OwnerChannelFile(orchId));
    }

    void Process_CloseImplementerRequests(IPendingRequests pending)
    {
        foreach (var request in pending.CloseImplementerRequests)
        {
            // A SOLO IS THE ORCHESTRATION, so closing one through this action would end every session
            // in a BASIC orchestration with nobody asked — and the whole-orchestration confirmation is
            // the one the owner explicitly kept. `Execute_CloseImplementer` has no kind check, so
            // before member closes stopped waiting for a tap this route was gated by accident; it is
            // gated on purpose now.
            //
            // REFUSED, NOT REROUTED. Routing it would turn one request kind silently into another, and
            // this file has already paid for a request whose kind and effect disagreed. The requester
            // is told which action to use, so nothing is lost but a round trip.
            //
            // HERE AND NOT INSIDE `Execute_CloseImplementer`, and that is deliberate: a guard belongs
            // where untrusted input ENTERS, not where the trusted internal caller acts.
            //
            // BE PRECISE ABOUT WHAT IS TRUE NOW. At this sha `Execute_CloseImplementer` has exactly one
            // caller — this loop — so guarding either place would behave identically today, and this
            // placement buys nothing yet. It is chosen for what it keeps possible: the basic→full
            // promotion being built on imp-2's branch closes `solo-1` through `_store.Close_Member`
            // directly, so it passes through nothing here, and a guard inside the execution would be
            // the thing that broke when that work lands.
            if (MemberKind_Ids.Resolve_Kind(request.MemberId) == MemberKinds.Solo)
            {
                _log.Log_Warning(request.OrchId, $"close-implementer named the solo '{request.MemberId}' — refused, a solo close is an orchestration close");

                Append_OrchestrationAppEntry(
                    request.OrchId, AppEntryAudiences.Owner,
                    $"close of '{request.MemberId}' REFUSED — a solo is the whole orchestration",
                    $"'{request.MemberId}' is the only session here, so closing it ends the orchestration — and that is the one close the owner still confirms themselves. "
                    + "Nothing was closed. If you mean to end this orchestration, use close-orchestration and they will be asked to confirm.");

                Archive_ResolvedRequest_BestEffort(request.SourceFilePath, "refused-solo");
                continue;
            }

            var executed = false;

            try
            {
                Execute_CloseImplementer(request.OrchId, request.MemberId, request.Reason);
                executed = true;
            }
            catch (Exception)
            {
                // Already logged and reported into the requester's channel by Execute_CloseImplementer,
                // which reports before it rethrows. Swallowed here so one member's failure cannot take
                // down the tick and with it every other orchestration's traffic.
            }
            finally
            {
                // THE LABEL IS THE AUDIT TRAIL AND IT MUST NOT LIE. An unknown member id throws inside
                // Close_Member before anything is killed, so the member is still running — filing that
                // as "executed" records a close that never happened, in the folder that exists
                // precisely because "who asked, and what became of it" was once unanswerable.
                Archive_ResolvedRequest_BestEffort(request.SourceFilePath, executed ? "executed" : "failed");
            }
        }
    }

    /// <summary>
    /// Member closes parked before the 2026-08-13 directive are RELEASED, never executed.
    ///
    /// A parked request is one the supervisor asked for and the owner never answered, and it may be
    /// hours old. This codebase already decided what a stale close is worth, in the lapse wording it
    /// has used all along: *"a close must reflect the situation at the moment it is confirmed, not a
    /// stale one"*. Executing it now would apply a new policy retroactively to a decision the owner
    /// declined to make — and the member may since have been briefed with new work, finished, or been
    /// closed another way. Dropping costs one re-drop; executing costs a live session's context.
    ///
    /// It goes out through the existing lapse path rather than a new one, so the registrations behind
    /// any live button are cleared by the same code that always cleared them. A released request whose
    /// button stayed armed is exactly the immortal-button defect
    /// <see cref="Resolve_CloseConfirmations_Async"/> documents.
    /// </summary>
    void Release_ParkedMemberCloses()
    {
        foreach (var parkedPath in CloseConfirmation_Parking.Find_Parked(_paths))
        {
            if (Is_BeingResolved(parkedPath))
                continue;

            if (ParkedCloseRequest_Reader.Read_OrNull(parkedPath)?.Kind != ParkedCloseKinds.Implementer)
                continue;

            // PER ITEM, for the reason written six lines above this method and then not honoured here:
            // one unreadable or unwritable parked file must not take down the tick, and with it every
            // orchestration's traffic. This sweep runs every tick and touches files another process
            // may be moving, so it is the one most likely to meet a transient IO failure.
            try
            {
                Release_ParkedMemberClose(parkedPath);
            }
            catch (Exception ex)
            {
                _log.Log_Error(GLOBAL_ORCH_ID, $"could not release parked member close '{parkedPath}' — it stays parked and the next tick retries", ex);
            }
        }
    }

    /// <summary>
    /// The ONE member-close execution — the same shape as <see cref="Execute_Close"/> for an
    /// orchestration, so a close cannot come to mean two different things depending on which door it
    /// walked through. Since 2026-08-13 its only live caller is the request arriving
    /// (<see cref="Process_CloseImplementerRequests"/>); the owner's tap no longer reaches it.
    /// </summary>
    /// <remarks>
    /// It REPORTS and then rethrows, which is the contract <see cref="Execute_Close"/> already has
    /// and every caller relies on: they swallow, because they run on the inbound loop with nobody
    /// watching. Reporting anywhere but here would mean a failed close is silent to the one session
    /// waiting on it.
    /// </remarks>
    void Execute_CloseImplementer(string orchId, string memberId, string reason)
    {
        try
        {
            // SAID BEFORE THE SPOKE STOPS BEING A SOURCE — see UndeliveredSpokeTraffic_Reporter for
            // why a line and not a drain, and for what the member digest widened.
            UndeliveredSpokeTraffic_Reporter.Log_BeforeClosing(_paths, _log, orchId, memberId);

            _store.Close_Member(orchId, memberId);
            SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_ImplementerPidFile(orchId, memberId));

            // AGENT, NOT OWNER — changed on 2026-09-07 with the evidence the original judgement
            // lacked. In two hours the owner received seven member-lifecycle notices and reported
            // them as noise: a close is an after-the-fact notice of a decision the SUPERVISOR took,
            // with nothing to do and nothing to undo, and unlike an ADD it starts nothing spending.
            //
            // NOTHING IS LOST. The periodic STATUS block enumerates every member and marks a closed
            // one "closed", so the roster still reaches the owner — in context, beside what the rest
            // of the crew is doing, instead of as its own interruption.
            Append_OrchestrationAppEntry(
                orchId, AppEntryAudiences.Agent,
                $"member '{memberId}' closed — {reason}",
                $"'{memberId}' is retired: its terminal was closed and its channel stays on disk as audit trail. Your crew is yours to manage, so this took effect on your request without asking the owner.");
        }
        catch (Exception ex)
        {
            _log.Log_Error(orchId, $"close-implementer '{memberId}' failed", ex);

            Append_OrchestrationAppEntry(
                orchId, AppEntryAudiences.Owner,
                $"close of '{memberId}' FAILED — it is still running",
                $"The close did not complete ({ex.Message}), so nothing was closed. Drop the request again, or say so if it keeps failing — do NOT go and check whether it is alive, that is the app's job and never yours.");

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
                    request.OrchId, AppEntryAudiences.Agent,
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
                    request.OrchId, AppEntryAudiences.Agent,
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
            {
                // STAMPED BEFORE THE ASK, not after it. The stamp is what a later start reads to
                // know a delete is owed; written after the attempt it would be missing for exactly
                // the case it exists to cover — the process dying while the delete was failing.
                _store.Mark_TopicDeletePending(orchId);
                Delete_TelegramTopic_FireAndForget(orchId, session.TelegramTopicId.Value);
            }

            Append_GeneralAppEntry(AppEntryAudiences.Owner,
                $"orchestration '{orchId}' closed — {reason}",
                $"{authorisation} Asked by: {requester}. Sessions ended; folder kept as audit trail; Telegram topic deleted.");
        }
        catch (Exception ex)
        {
            _log.Log_Error(orchId, "close-orchestration failed", ex);
            Append_GeneralAppEntry(AppEntryAudiences.Owner, $"close-orchestration FAILED: '{orchId}'", $"Error: {ex.Message}");

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
            _log.Log_Warning(GLOBAL_ORCH_ID, $"A parked request {what} and no orchestration could be named to tell: {parkedPath}");
            return;
        }

        // NEUTRAL: this is only reached for a request that could not be parsed, so its kind is
        // unknown and naming a close would be inventing one.
        Append_OrchestrationAppEntry(orchId, AppEntryAudiences.Agent, $"your request {what} — nothing was done", advice);
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
            if (Decide_ParkedAction(parkedPath) == ParkedConfirmationActions.Expire)
                Expire_CloseConfirmation(parkedPath);
        }
    }

    /// <summary>
    /// What this tick should do with a parked request — read from the ONE table both sweeps share.
    ///
    /// They used to carry a guard chain each, and that is how they came to disagree about which
    /// requests were live: a dropped `continue` in this one let the ask sweep post fresh buttons every
    /// two seconds for a request that had already lapsed. The order those guards must run in encodes
    /// four production failures and now lives in `ParkedConfirmation_Planner`, where the suite can ask
    /// about it — this method is left with the three facts and none of the reasoning.
    /// </summary>
    ParkedConfirmationActions Decide_ParkedAction(string parkedPath)
    {
        bool alreadyAsked;

        lock (_closeConfirmationLock)
            alreadyAsked = _closeConfirmations.Values.Any(confirmation => confirmation.ParkedPath == parkedPath);

        return ParkedConfirmation_Planner.Decide(
            Is_BeingResolved(parkedPath),
            CloseConfirmation_Parking.Is_Expired(parkedPath, DateTime.UtcNow),
            alreadyAsked);
    }

    async Task Resolve_CloseConfirmations_Async(CancellationToken cancellationToken)
    {
        foreach (var parkedPath in CloseConfirmation_Parking.Find_Parked(_paths))
        {
            // The three guards this loop used to carry — being resolved, expired, already asked —
            // are one decision now, shared with the expiry sweep. Each of them was written after a
            // live failure and their ORDER is what mattered; both the reasoning and the ordering are
            // in `ParkedConfirmation_Planner`, and tested there.
            if (Decide_ParkedAction(parkedPath) == ParkedConfirmationActions.Ask)
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

        // The snapshot follows the registry, or the file keeps naming a prompt that no longer exists
        // anywhere — the same lie in the other direction.
        Persist_EngineState();
    }

    bool Is_BeingResolved(string parkedPath)
    {
        lock (_closeConfirmationLock)
            return _closeConfirmationsResolving.Contains(parkedPath);
    }

    /// <summary>
    /// When the agent parked this request — the same clock <see cref="CloseConfirmation_Parking.Is_Expired"/>
    /// runs on, so the deadline written into the snapshot is the deadline the tap will be judged by
    /// rather than a second, nearly-equal one.
    ///
    /// NULL RATHER THAN A FALLBACK when it cannot be stat'ed. <c>File.GetLastWriteTimeUtc</c> answers
    /// the year 1601 for a missing file, and 1601 plus twelve hours is a deadline that reads as
    /// "expired four centuries ago" in a record whose whole job is to be believed by a human.
    /// </summary>
    static DateTime? Read_ParkedSince_OrNull(string parkedPath)
    {
        try
        {
            return File.Exists(parkedPath) ? File.GetLastWriteTimeUtc(parkedPath) : null;
        }
        catch
        {
            // Locked, denied, unreadable sector: the cause changes nothing — we cannot date it.
            return null;
        }
    }

    async Task Ask_OwnerToConfirmClose_Async(string parkedPath, CancellationToken cancellationToken)
    {
        var request = ParkedCloseRequest_Reader.Read_OrNull(parkedPath);

        if (request == null)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Parked close request is unreadable — archived unexecuted: {parkedPath}");
            Archive_ResolvedRequest_BestEffort(parkedPath, "unreadable");
            Report_UnhonouredCloseRequest(parkedPath, "could not be read", "It was archived unexecuted and nothing was done. Drop a fresh, valid request if you still want it.");
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

        // Same reasoning one level down, PER KIND: a question whose answer can no longer change
        // anything must not be asked. It covered the implementer close only — a two-armed guard
        // written when there were two kinds — so a promotion whose solo had been closed meanwhile was
        // still offered, and the only tap available led to "promotion FAILED after the owner
        // confirmed it".
        //
        // A switch rather than another `if` chain: the arms are the enum, so a fourth kind arrives
        // here as an unhandled case to answer rather than as silence that happens to read as "ask".
        var mootBecause = request.Kind switch
        {
            ParkedCloseKinds.Implementer
                when session.Members.FirstOrDefault(member => member.MemberId == request.MemberId)?.ClosedUtc != null
                => $"'{request.MemberId}' is already closed",

            ParkedCloseKinds.Promotion
                when !OrchestrationShape.Can_StillPromote(OrchestrationShape.Decide_PromotionReadiness(
                    session.SupervisorSpawnedUtc,
                    OrchestrationShape.Has_LiveSolo(session.Members)))
                => "there is nothing left to promote",

            _ => null,
        };

        if (mootBecause != null)
        {
            _log.Log_Info(request.OrchId, $"Parked request is moot — {mootBecause}");
            Archive_ResolvedRequest_BestEffort(parkedPath, "moot");
            return;
        }

        // No way to ask means no way to confirm, and this guard fails CLOSED: it keeps waiting and
        // eventually lapses. Nothing is closed on a machine that cannot reach the owner.
        //
        // BUT IT SAYS SO, ONCE. This returned in total silence — no log line, no channel entry —
        // every tick until the request lapsed twelve hours later. Decision 21: a component that
        // cannot evaluate its predicate SAYS SO rather than granting silent consent. It is also the
        // likeliest explanation for the owner's *"it was reasoning for like 5 minutes and then
        // nothing happened"*: a basic orchestration only gets a Telegram topic on its first MIRRORED
        // entry, so a solo whose entries were all agent-tagged or suppressed sits here for ever with
        // its request parked and nobody told.
        if (_telegramClient == null || session.TelegramTopicId == null)
        {
            if (_confirmationsUnaskable.Add(session.OrchId))
            {
                _log.Log_Warning(session.OrchId, _telegramClient == null
                    ? "A parked request cannot be put to the owner — Telegram is not configured. It waits, and will lapse."
                    : "A parked request cannot be put to the owner — this orchestration has no Telegram topic yet. It waits, and will lapse.");

                Append_OrchestrationAppEntry(
                    session.OrchId, AppEntryAudiences.Agent,
                    "your parked request cannot be put to the owner yet",
                    _telegramClient == null
                        ? "Telegram is not configured on this machine, so the confirmation cannot be asked for. Your request is HELD and will lapse. Ask the owner directly."
                        : "This orchestration has no Telegram topic yet, so the confirmation cannot be asked for — a topic appears on the first entry of yours that reaches the owner's phone. Your request is HELD until then and lapses after that.");
            }

            return;
        }

        // Askable again — re-arm the notice, so a later spell of the same fault is reported rather
        // than swallowed as already-said.
        _confirmationsUnaskable.Remove(session.OrchId);

        // What is being ended mid-flight, named at the moment they decide. It does NOT block the
        // close: a ledger that can refuse to let an orchestration end is the tail wagging the dog,
        // and it is the same shape as every deadlock removed tonight — an enforcement demanding an
        // action some other state forbids. The owner is already tapping; give them the fact.
        //
        // Read only for an ORCHESTRATION close. The ledger belongs to the orchestration, so it says
        // nothing about one member being safe to retire, and the builder deliberately leaves it out
        // of that prompt — reading it here anyway would be work whose only possible use is to
        // mislead.
        //
        // DELIBERATELY TWO-ARMED OVER A THREE-VALUED ENUM — do not "fix" it. A promotion ENDS nothing,
        // so it belongs on the same side as a member close, and it is already there. Said explicitly
        // because a sweep of this file found six two-armed branches that were wrong and this is the
        // one that is right: the next person enumerating them should be able to stop here in a second
        // rather than reason it out again, or worse, "correct" it.
        var unresolved = request.Kind != ParkedCloseKinds.Orchestration
            ? null
            : Planning.PlanProgress_Formatter.Describe_UnresolvedAtClose_OrNull(
                Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(request.OrchId))));

        var text = CloseConfirmationPrompt_Builder.Build(request, unresolved);

        var confirmData = $"close-yes-{Guid.NewGuid():N}";
        var declineData = $"close-no-{Guid.NewGuid():N}";

        try
        {
            // THE BUTTONS FOLLOW THE KIND, like the prompt above them. They were hard-coded "Close
            // it" / "Keep it open" while the prompt already said "Turn 'X' into a full crew?" — so
            // the owner would have confirmed a crew by tapping CLOSE, and the safe-looking tap would
            // have declined a promotion nobody knew had been misread.
            var (confirmLabel, declineLabel) = CloseConfirmationPrompt_Builder.Build_ButtonLabels(request.Kind);

            var messageId = await _telegramClient.Send_MessageWithButtons_Async(
                session.TelegramTopicId,
                text,
                [(confirmData, confirmLabel), (declineData, declineLabel)],

                // A CONFIRMATION IS A QUESTION: closing an orchestration or promoting one to a full
                // crew waits on this tap, so it rings like any other decision.
                TelegramSendSounds.Rings,
                cancellationToken);

            Remember_TopicMessage(session.TelegramTopicId, messageId);

            var askedUtc = DateTime.UtcNow;
            var parkedUtc = Read_ParkedSince_OrNull(parkedPath);

            CloseConfirmation Build_Registration(bool confirms) => new()
            {
                OrchId = request.OrchId,
                ParkedPath = parkedPath,
                Confirms = confirms,
                PromptMessageId = messageId,
                Kind = request.Kind.ToString(),
                MemberId = request.MemberId,
                Requester = request.Requester,
                AskedUtc = askedUtc,
                ExpiresUtc = parkedUtc?.AddHours(CloseConfirmation_Parking.EXPIRY_HOURS),
            };

            DateTime? promptFromABygoneProcessUtc;
            bool alreadyAskedInThisRun;

            lock (_closeConfirmationLock)
            {
                _closeConfirmations[confirmData] = Build_Registration(confirms: true);
                _closeConfirmations[declineData] = Build_Registration(confirms: false);

                // CONSUMED, both of them. The restart fact is true once — a second prompt in this run
                // was dropped by this host, not by a restart, and saying "a restart" for it would put
                // a wrong cause in the journal that reads exactly like the right one.
                promptFromABygoneProcessUtc = _closeConfirmationsFromABygoneProcess.TryGetValue(parkedPath, out var bygone)
                    ? bygone
                    : null;

                _closeConfirmationsFromABygoneProcess.Remove(parkedPath);
                alreadyAskedInThisRun = !_closeConfirmationsAskedInThisRun.Add(parkedPath);
            }

            // OUTSIDE the lock, like every other save on this path: Persist_EngineState takes the
            // button and owner-state locks and re-takes this one, and a fixed order is only fixed
            // while nobody nests it from the other side.
            Persist_EngineState();

            _log.Log_Info(
                request.OrchId,
                CloseConfirmationPrompt_Builder.Describe_AskForTheJournal(
                    request,
                    parkedUtc == null ? TimeSpan.Zero : askedUtc - parkedUtc.Value,
                    promptFromABygoneProcessUtc,
                    alreadyAskedInThisRun));
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        // Cost HERE: the owner is never asked, so the close request stays unresolved and the tick that would re-ask is gone.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                // NEUTRAL ON PURPOSE, and this is the one branch where it cannot be otherwise: it
                // runs because the parked file is gone or expired, so the kind is unknowable and
                // "nothing closed" would be a guess — wrong on the tap the owner most wants to
                // believe, where they tapped "✅ Make it a crew".
                await client.Answer_CallbackQuery_Async(
                    tap.CallbackQueryId,
                    $"{(stillParked ? "expired" : "already resolved")} — {CloseConfirmationPrompt_Builder.Describe_NothingDone(null)}",
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
                $"A confirmation was tapped after it {(stillParked ? "expired" : "was already resolved")} — NOTHING was done ({confirmation.ParkedPath})");

            if (expired && stillParked)
                Expire_CloseConfirmation(confirmation.ParkedPath);

            return true;
        }

        // READ ONCE, used by the toast and by the post-tap edit below. Both describe the same tap, so
        // reading the file twice would let them disagree if it were archived in between — and the
        // toast was a kind-blind literal: the owner tapped "✅ Make it a crew" and their phone
        // flashed "closing…". That was the third owner-visible string on this one tap.
        var tappedKind = ParkedCloseRequest_Reader.Read_OrNull(confirmation.ParkedPath)?.Kind;

        try
        {
            await client.Answer_CallbackQuery_Async(
                tap.CallbackQueryId,
                CloseConfirmationPrompt_Builder.Build_TapToast(tappedKind, confirmation.Confirms),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(confirmation.OrchId, $"answerCallbackQuery failed on a close confirmation: {ex.Message}");
        }

        // A TAP IS THE OWNER SPEAKING, and this path returns before the generic routing that would
        // otherwise say so. Without it, declining a close woke the supervisor into a session where
        // the awaiting-answer hook denied every tool call until the flag expired — the owner would
        // have said "keep it open" and got a deadlocked supervisor for the answer.
        if (Note_OwnerSpoke_AndWasAway())
            await Exit_AwayMode_Async(cancellationToken);

        Clear_OpenQuestions(confirmation.OrchId);
        Clear_AwaitingAnswerFlag(confirmation.OrchId);
        Discard_PendingConfirmations(confirmation.OrchId);

        // THE ONE MUTATION SITE THAT DID NOT SAVE, and the only one — every other write to the five
        // persisted maps is followed by a save on some path. Without it a closed orchestration's
        // questions came back from the snapshot on the next restart, went past their deadline, and
        // had `question DEFAULTED on timeout` written into the owner channel of an orchestration that
        // had been closed hours earlier: a stale snapshot reanimating a dead session.
        Persist_EngineState();

        var result = confirmation.Confirms
            ? Execute_ConfirmedClose(confirmation)
            : Decline_CloseConfirmation(confirmation);

        // THE DECISION IS RECORDED AFTER THE OUTCOME IS KNOWN, and it used to be recorded before.
        //
        // The old order had a stated reason — "confirming deletes the topic, and an edit sent
        // afterwards would have nowhere to land". It is INAPPLICABLE to member closes, which delete no
        // topic; and for orchestration closes it was a GENUINE GUARANTEE rather than a race, because
        // the edit was awaited and the executor is synchronous, so the edit's round-trip finished
        // before deleteForumTopic was constructed. Moving it gives that up knowingly.
        //
        // THE TRADE IS STILL RIGHT: the guarantee protected a message being destroyed in the same
        // breath — the prompt lives IN the topic the close deletes, so nothing durable was bought by
        // it, while the durable record goes to the General topic, which is never deleted. And on every
        // outcome where the topic SURVIVES (member closes, declines, NotAttempted, and orchestration
        // closes whose delete fails) this order is the only one that can tell the truth.
        //
        // Uncertain is NOT in that list, and it was: rev-6 corrected its own argument after this
        // comment quoted it. Where the topic stands depends on WHERE the throw landed —
        // Execute_Close deletes the topic at :2542 and appends to the general channel at :2544, so the
        // canonical Uncertain (that append failing) has the topic already being torn down, exactly as
        // Closed does. A throw at :2537-:2539 leaves it standing. It belongs on neither side of an
        // unconditional list, so it is on neither; the argument only ever needed one member.
        //
        // THIS ORDER IS PINNED, and an earlier version of this comment claimed it could not be. Which
        // sentence belongs to which outcome is covered in CloseConfirmationPrompt_Builder; that the
        // edit happens AFTER the outcome is known is asserted by
        // CloseTapArchiveProbeTests.ACloseThatThrewNeverTellsTheOwnerItSucceeded, which drives a real
        // tap through a close that throws and reads the text this line sends. An edit written before
        // the attempt can only ever claim success, so "Closed — you confirmed" on a failed close
        // proves the edit ran first.
        //
        // The claim it replaces — "the suite cannot reach this, BridgeEngineModel is internal sealed
        // with no InternalsVisibleTo" — is true of a decision made INSIDE this class and false of its
        // EFFECTS: BridgeEngine_Factory is public and takes interfaces only, so the engine can be
        // driven end to end. CloseImplementerGuardProbeTests did that first, and its summary records
        // two members declaring the same wiring unpinnable before a reviewer pinned it. Do not re-add
        // the stronger claim.
        if (tap.MessageId != null)
        {
            try
            {
                await client.Edit_MessageText_Async(
                    tap.MessageId.Value,
                    CloseConfirmationPrompt_Builder.Describe_Decision(confirmation.OrchId, result.Request, result.Outcome),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // ROUTINE ONLY WHERE THE TOPIC IS BEING DELETED UNDERNEATH THIS EDIT, which is not the
                // same as "the close succeeded". A successful MEMBER close returns Closed too and
                // deletes no topic, so keying the quiet path on the outcome alone silenced the case
                // where the prompt is still standing with two live buttons on it.
                //
                // Exact rather than approximate here: we are holding a live client and a message
                // inside the orchestration's own topic, so an orchestration close on this path always
                // started the deletion.
                var topicIsBeingDeleted =
                    result.Outcome == CloseTapOutcomes.Closed
                    && result.Request?.Kind == ParkedCloseKinds.Orchestration;

                var message = $"Could not record the close decision on the prompt: {ex.Message}";

                // A warning that fires on the healthy path is how a log stops being read; a failed
                // edit anywhere else means the owner is looking at a prompt that says something untrue.
                if (topicIsBeingDeleted)
                    _log.Log_Info(confirmation.OrchId, message);
                else
                    _log.Log_Warning(confirmation.OrchId, message);
            }
        }

        return true;
    }

    /// <summary>
    /// Runs the close and SAYS WHAT HAPPENED. It returned void, which is precisely why its caller
    /// could not wait for it and announced success up front instead.
    ///
    /// WHICH outcome is chosen is not decided here — <see cref="CloseTapOutcome_Decider"/> owns that,
    /// because a decision made in this class cannot be reached by the suite and this one is the fix.
    /// </summary>
    CloseTapResult Execute_ConfirmedClose(CloseConfirmation confirmation)
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
                $"A confirmed request could not be read — NOTHING was done, left parked to be re-asked ({confirmation.ParkedPath})");

            // NOT archived. A sharing violation at tap time is transient, and archiving would throw
            // away a close the owner had already approved with no way back. Left parked, this heals
            // itself: the registrations are already gone, so the next sweep asks again — and if the
            // file is genuinely corrupt, Ask_OwnerToConfirmClose_Async has the same null check and
            // files it as unreadable there.
            //
            // Told to the REQUESTER, in its own channel, because that is where this guard promised
            // an answer either way — the general channel cannot be read by the session waiting.
            // NEUTRAL, because the file this branch exists for is the one that could not be read —
            // so the kind is unknowable and "close" would be a guess in front of a solo that had
            // asked to be promoted.
            Append_OrchestrationAppEntry(
                confirmation.OrchId, AppEntryAudiences.Agent,
                "NOT executed — your request could not be read just now",
                "The owner's tap arrived, but your request file could not be read at that moment, so nothing was done. It has been left in place and they will be asked again shortly. Do not re-drop it.");

            return new CloseTapResult(CloseTapOutcome_Decider.Decide(null, null), null);
        }

        Exception? failure = null;

        // WHAT THE TAP ACTUALLY AUTHORISED, set by the arm that runs rather than derived from the
        // kind afterwards. It was `Kind == Promotion ? "promoted" : "closed"` — which archived the
        // UNKNOWN-KIND arm, the one that deliberately does nothing, under the label "closed". The
        // comment three lines below said a wrong label leaves an audit trail saying the opposite of
        // what happened, and the arm that produced one was added in the same commit as the comment.
        //
        // IT IS ONLY HALF THE ARCHIVE WORD. This says what was authorised; whether it COMPLETED is
        // the outcome's to say, and a run that threw is filed "uncertain" whatever arm it was in.
        var archiveLabel = "unexecuted";

        try
        {
            // The kind decides what the tap ends, and it comes from the FILE rather than from
            // anything remembered alongside the button. A prompt that said "member" must never be
            // able to execute an orchestration close because some other state disagreed.
            //
            // EVERY KIND IS NAMED, and the catch-all `else` that used to sit here is gone. It read as
            // "orchestration is the default", which was true while there were two kinds and became a
            // live hazard the moment a third existed: a PROMOTION falling into it would have closed
            // the orchestration the owner had just agreed to EXPAND — the worst outcome this feature
            // can produce, from the one tap they were most confident about.
            if (request.Kind == ParkedCloseKinds.Implementer)
            {
                // A MEMBER CLOSE NO LONGER REACHES A TAP (owner directive 2026-08-13), so this is a
                // button left over from before the change. It is refused rather than executed: the
                // request is stale for the same reason Release_ParkedMemberCloses gives, and the
                // sweep is about to release it anyway.
                //
                // THE BRANCH ITSELF STAYS, and deleting it is the trap. `else` here is
                // Execute_Close — the whole orchestration — so a member-kind file falling through
                // this test would end every session in it, which is the exact substitution the
                // comment above about reading the kind from the FILE exists to prevent. Unreachable
                // is not the same as safe when the fallthrough is irreversible.
                _log.Log_Warning(confirmation.OrchId, $"a tap arrived for parked close-implementer '{request.MemberId}' — refused, member closes no longer wait for the owner");

                Append_OrchestrationAppEntry(
                    confirmation.OrchId, AppEntryAudiences.Owner,
                    $"close of '{request.MemberId}' NOT executed — that button predates the rule change",
                    "Member closes no longer wait for the owner, so this parked request was released rather than executed and nothing was closed. Drop it again if it still applies; it takes effect immediately.");
            }
            else if (request.Kind == ParkedCloseKinds.Promotion)
            {
                archiveLabel = "promoted";
                Execute_ConfirmedPromotion(confirmation.OrchId, request.Reason);
            }
            else if (request.Kind == ParkedCloseKinds.Orchestration)
            {
                archiveLabel = "closed";
                Execute_Close(
                    confirmation.OrchId,
                    request.Reason,
                    request.Requester,
                    "The owner confirmed it with a tap.");
            }
            else
            {
                // A kind this build cannot execute — from a newer build, or a file that parsed as
                // something this one does not know how to act on. NOTHING happens, and it says so:
                // silence here would archive an unexecuted request as though it had been done, which
                // is the same lie as executing the wrong thing, one step quieter.
                _log.Log_Warning(
                    confirmation.OrchId,
                    $"A confirmed request carried a kind this build cannot execute ('{request.Kind}') — NOTHING was done ({confirmation.ParkedPath})");
            }

        }
        catch (Exception ex)
        {
            // Already logged and reported to the general channel by Execute_Close. Swallowed HERE
            // because this runs on the inbound loop with nobody watching, and a throw would take the
            // loop down; the owner's own close does the opposite and surfaces it.
            //
            // SWALLOWED IS NOT UNREPORTED, and it used to be. Execute_Close marks the orchestration
            // closed before it kills the sessions, so a throw between those two can leave it flagged
            // closed with its terminals alive — and nothing re-offers it, because the store already
            // says closed. It is kept rather than discarded so the outcome can say we do not know,
            // instead of telling the owner it worked.
            failure = ex;
        }

        // ARCHIVED HERE RATHER THAN IN A `finally`, AND THE ORDER MATTERS MORE THAN IT LOOKS.
        //
        // Describe_ForArchive can throw — deliberately, because a close that was never attempted has
        // no archive word and that impossibility is worth stating. A throw raised inside a `finally`
        // REPLACES any exception still in flight, so putting it there made the guard's safety depend
        // on the catch above staying broad enough to leave nothing in flight. Narrow that catch later
        // for a perfectly good reason and the `finally` would discard the real exception and report
        // this one instead: the true failure invisible, the reported one a lie about it.
        //
        // The `finally` was guaranteeing nothing anyway. The catch swallows without rethrowing and the
        // try body has no return, so control reaches this line on both paths regardless — it was
        // redundant, and the redundancy was what carried the hazard.
        //
        // DO NOT MOVE THIS BACK INSIDE A `finally` for symmetry with the other archive call sites.
        //
        // The audit record is also what outlives the prompt: it filed "closed" whether or not the
        // executor threw, so the artefact a person reads while reconstructing an incident asserted the
        // very thing the owner's sentence was changed to stop asserting.
        //
        // TWO HALVES, TWO SOURCES. `archiveLabel` is what the tap authorised — "closed", "promoted",
        // or "unexecuted" for a kind this build cannot act on — and it comes from the arm that ran,
        // because deriving it from the kind afterwards is what filed a promotion under "closed". The
        // OUTCOME is whether that run completed, and it overrides on failure: a throw is "uncertain"
        // whichever arm it was in.
        var outcome = CloseTapOutcome_Decider.Decide(request, failure);

        Archive_ResolvedRequest_BestEffort(
            confirmation.ParkedPath,
            CloseTapOutcome_Decider.Describe_ForArchive(outcome, archiveLabel));

        return new CloseTapResult(outcome, request);
    }

    /// <summary>
    /// The owner agreed to spend a crew. The solo ends, a supervisor takes over its channel, imp-1
    /// spawns empty — all of it in the launcher, which is where the ORDER of those three steps is
    /// argued and where a failure at each one is survivable.
    ///
    /// It reports into the orchestration's own channel, which the new supervisor reads as its history:
    /// the entry is the first thing it sees about why it exists, sitting directly under the handover
    /// the solo was required to write.
    /// </summary>
    void Execute_ConfirmedPromotion(string orchId, string reason)
    {
        try
        {
            _launcher.Promote_ToFullCrew(orchId);

            _log.Log_Info(orchId, $"Promoted to a full crew on the owner's confirmation — {reason}");

            Append_OrchestrationAppEntry(
                orchId, AppEntryAudiences.Owner,
                "PROMOTED to a full crew — the owner confirmed",
                $"The solo session has ended and a supervisor has taken over this channel, with imp-1 spawned and waiting for a brief.\n\n"
                + $"Reason given: {reason}\n\n"
                + "Everything above is the history you inherit — the handover entry is in it. The Telegram topic is unchanged, so the owner is reading this same thread.");
        }
        catch (Exception ex)
        {
            // The orchestration is NOT left half-promoted silently. Whatever the launcher managed
            // before it threw, the channel says what was attempted, and the watchdog covers a
            // supervisor whose spawn was stamped but failed.
            _log.Log_Error(orchId, "Promotion to a full crew FAILED after the owner confirmed it", ex);

            Append_OrchestrationAppEntry(
                orchId, AppEntryAudiences.Owner,
                "promotion FAILED after the owner confirmed it",
                $"The owner approved the promotion and it could not be completed ({ex.Message}). Check which sessions are actually running before asking again.");
        }
    }

    CloseTapResult Decline_CloseConfirmation(CloseConfirmation confirmation)
    {
        var request = ParkedCloseRequest_Reader.Read_OrNull(confirmation.ParkedPath);

        // THE VERB COMES WITH THE PHRASE. This read "You asked to close {subject}" and the subject for
        // a promotion is "the promotion to a full crew" — so a solo whose promotion the owner refused
        // was told it had asked to CLOSE the promotion. The requester is told which of its asks was
        // refused; it may have more than one thing running.
        var askedFor = request == null
            ? "what you asked for"
            : CloseConfirmationPrompt_Builder.Describe_AskedFor(request);

        _log.Log_Info(confirmation.OrchId, "The owner declined a parked request");

        Append_OrchestrationAppEntry(
            confirmation.OrchId, AppEntryAudiences.Agent,
            $"{askedFor} — DECLINED by the owner, keep working",
            $"You asked for {askedFor} ({request?.Reason ?? "no reason recorded"}) and the owner said no — "
            + $"{CloseConfirmationPrompt_Builder.Describe_NothingDone(request?.Kind)}, and every session is still running.\n\n"
            + "Do NOT drop the request again. If you believe the work really is finished, say so in one line and let them answer.");

        Report_CloseOutcome_ToGeneral(confirmation.OrchId, "declined by the owner", request);
        // THE WORD COMES FROM THE DECIDER, not from a literal here. This was the one archive site
        // still choosing its own, which left Describe_ForArchive's Declined case reachable only from
        // the confirmed path — where it can never be selected — so the suite was asserting a branch
        // production does not call while the branch production DOES call went unpinned. Changing that
        // literal to "closed" filed every refused close as a completed one, and nothing reddened.
        Archive_ResolvedRequest_BestEffort(
            confirmation.ParkedPath,
            CloseTapOutcome_Decider.Describe_ForArchive(CloseTapOutcomes.Declined));

        // The request travels back for the same reason it does on the confirmed path: the sentence
        // replacing the prompt has to name what the prompt named. It is null here when the file could
        // not be read, and the wording falls back rather than guessing — `subject` above does the same.
        return new CloseTapResult(CloseTapOutcomes.Declined, request);
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
        //
        // It used to ask `Kind == Orchestration` and put the MEMBER ID in the other arm: a two-armed
        // test over three values, and a promotion carries no member id by construction — so a
        // declined promotion was filed as the close of a member with no name, in the one channel the
        // general supervisor reads to know what is running.
        Append_GeneralAppEntry(AppEntryAudiences.Agent, 
            $"{CloseConfirmationPrompt_Builder.Describe_AskedFor_ToGeneral(request, orchId)} — {outcome}, {CloseConfirmationPrompt_Builder.Describe_NothingDone(request?.Kind)}",
            $"Asked by: {request?.Requester ?? "unrecorded"}. Reason given: {request?.Reason ?? "none recorded"}. Its sessions are all still running.");
    }

    /// <summary>
    /// Disarms every button pointing at this parked request. EXTRACTED so the policy release shares
    /// it rather than reimplementing it: a released request whose registrations survived is the
    /// immortal-button defect <see cref="Resolve_CloseConfirmations_Async"/> documents, where a tap
    /// on a stale button closed an orchestration the owner had explicitly refused to close.
    /// </summary>
    void Clear_CloseConfirmationRegistrations(string parkedPath)
    {
        lock (_closeConfirmationLock)
        {
            foreach (var key in _closeConfirmations.Where(pair => pair.Value.ParkedPath == parkedPath).Select(pair => pair.Key).ToList())
                _closeConfirmations.Remove(key);
        }

        // As in Forget_CloseConfirmations_For: the file must stop naming a prompt that is gone.
        Persist_EngineState();
    }

    /// <summary>
    /// A member close that was parked before the 2026-08-13 directive. Released, never executed — see
    /// <see cref="Release_ParkedMemberCloses"/> for why a stale close is not authority to kill a
    /// session. Worded as its own outcome rather than reusing the lapse text, which would tell the
    /// supervisor its request sat for twelve hours when it may have sat for two minutes.
    /// </summary>
    void Release_ParkedMemberClose(string parkedPath)
    {
        var request = ParkedCloseRequest_Reader.Read_OrNull(parkedPath);

        Clear_CloseConfirmationRegistrations(parkedPath);

        if (request != null)
        {
            _log.Log_Info(request.OrchId, $"parked close-implementer '{request.MemberId}' released unexecuted — member closes no longer wait for the owner");

            Append_OrchestrationAppEntry(
                request.OrchId, AppEntryAudiences.Owner,
                $"close of '{request.MemberId}' RELEASED — member closes no longer need the owner",
                "The owner has changed this: closing an implementer or a reviewer is yours to decide and now takes effect the moment you drop the request. "
                + "This one was waiting for a tap that will not come, and it was NOT executed — it may be hours old, and a close must reflect the situation "
                + $"now rather than when it was asked. Nothing was closed and '{request.MemberId}' is still running. Drop it again if the close still applies; "
                + "it will take effect immediately. The whole-orchestration close is unchanged and still asks.");

            Report_CloseOutcome_ToGeneral(request.OrchId, "released unexecuted — member closes no longer ask the owner", request);
        }

        Archive_ResolvedRequest_BestEffort(parkedPath, "released");
    }

    void Expire_CloseConfirmation(string parkedPath)
    {
        var request = ParkedCloseRequest_Reader.Read_OrNull(parkedPath);

        Clear_CloseConfirmationRegistrations(parkedPath);

        if (request != null)
        {
            _log.Log_Info(request.OrchId, $"A parked request lapsed unanswered after {CloseConfirmation_Parking.EXPIRY_HOURS} h");

            // Same fix as the declined notice: the phrase brings its own verb, so this can no longer
            // render as "close of the promotion to a full crew LAPSED".
            Append_OrchestrationAppEntry(
                request.OrchId, AppEntryAudiences.Agent,
                $"{CloseConfirmationPrompt_Builder.Describe_AskedFor(request)} LAPSED — the owner never answered",
                $"Your request sat unanswered for {CloseConfirmation_Parking.EXPIRY_HOURS} hours, so it has expired and "
                + $"{CloseConfirmationPrompt_Builder.Describe_NothingDone(request.Kind)}. "
                + "It is not carried over: a decision must reflect the situation at the moment it is confirmed, not a stale one. Ask again if it still applies.");

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

    /// <summary>
    /// Deletes an orchestration's topic and KEEPS TRYING — the point of effect for brief E1; the
    /// decisions are <see cref="TopicDelete_Decider"/>'s.
    ///
    /// <para>
    /// It was one call, fire-and-forget, its failure swallowed into a log line and recorded nowhere
    /// (audit 2026-09-09). The owner's ruling is that closed topics ARE deleted, so a delete that
    /// silently did not happen is that ruling quietly not kept — an orphan topic on a phone that
    /// will hold thousands of them, with nothing on disk that any later start could act on.
    /// </para>
    /// <para>
    /// STILL DETACHED, and deliberately: closing an orchestration must not wait on Telegram. What
    /// changed is that the detached task is now BOUNDED (four attempts) and LEAVES A RECORD — the
    /// pending stamp is written by the caller BEFORE this runs, so a process killed at any point in
    /// the loop still hands the work to <see cref="Sweep_PendingTopicDeletes_Async"/> at the next
    /// start.
    /// </para>
    /// </summary>
    void Delete_TelegramTopic_FireAndForget(string orchId, long topicId)
    {
        _ = Task.Run(() => Delete_TelegramTopic_WithRetries_Async(orchId, topicId, CancellationToken.None));
    }

    /// <summary>
    /// The retry loop itself, awaitable so the start-up sweep can walk its backlog one at a time
    /// rather than firing every pending delete at Telegram's rate limit simultaneously.
    /// </summary>
    /// <summary>
    /// TOPICS THIS PROCESS HAS ALREADY TAKEN ON. Two paths reach the delete: the close itself
    /// (fire-and-forget, the moment the owner closes an orchestration) and the reconciliation sweep
    /// that runs at every start for deletes still owed on disk. They can collide — the sweep reads
    /// the sessions asynchronously at startup, so a close landing in that window is stamped
    /// "pending" in time for the sweep to pick it up as well.
    ///
    /// <para>
    /// WHICH BREAKS THE ONE PROMISE THIS FAMILY MAKES: a REFUSED delete is not retried inside the
    /// process, because a revoked right cannot change while the process runs. Two entries meant two
    /// attempts, and the only reason it did not also mean two alerts to the owner is that the
    /// "told them once" flag is on disk. Caught by
    /// <c>ADeleteTelegramWillNeverAccept_TellsTheOwnerOnce_AndNeverAgainAfterARestart</c>, which
    /// failed about one run in four under load and passed on its own — the shape of a race, and its
    /// assertion (one attempt) was right.
    /// </para>
    /// <para>
    /// PER PROCESS, NOT PERSISTED, deliberately: a restart is exactly when a delete SHOULD be tried
    /// again, and the sweep exists for that.
    /// </para>
    /// </summary>
    readonly HashSet<long> _topicDeletesTakenOn = [];

    readonly object _topicDeleteLock = new();

    bool Take_On_TopicDelete(long topicId)
    {
        lock (_topicDeleteLock)
            return _topicDeletesTakenOn.Add(topicId);
    }

    async Task Delete_TelegramTopic_WithRetries_Async(string orchId, long topicId, CancellationToken cancellationToken)
    {
        if (!Take_On_TopicDelete(topicId))
        {
            _log.Log_Info(orchId, $"Telegram topic {topicId} is already being deleted by this process — the second path (close or start-up sweep) stands down instead of attempting it again");
            return;
        }

        for (var attemptsMade = 1; ; attemptsMade++)
        {
            Exception? failure = null;

            try
            {
                var client = _telegramClient
                    ?? throw new Exception($"Telegram client vanished while deleting topic {topicId} of '{orchId}'");

                await client.Delete_ForumTopic_Async(topicId, cancellationToken);
            }
            // A SHUTDOWN IS NOT A REFUSAL. Rethrowing here would abandon the delete WITHOUT recording
            // anything — but the pending stamp is already on disk, so the next start picks it up.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _log.Log_Info(orchId, $"Telegram topic {topicId} delete abandoned at shutdown after attempt {attemptsMade} — still pending, the next start retries it");
                return;
            }
            catch (Exception ex)
            {
                // Broad by intent: the classification is TopicDelete_Decider's job and it reads the
                // type and the status, never this catch's shape.
                failure = ex;
            }

            var outcome = TopicDelete_Decider.Classify(failure);

            if (TopicDelete_Decider.Is_Settled(outcome))
            {
                Record_TopicDeleted(orchId, topicId, outcome, attemptsMade);
                return;
            }

            if (TopicDelete_Decider.Should_RetryNow(outcome, attemptsMade))
            {
                var retryAfterSeconds = (failure as Telegram.TelegramApiClient.TelegramApiException)?.RetryAfterSeconds;
                var delay = TopicDelete_Decider.Build_BackoffDelay(attemptsMade, retryAfterSeconds);

                _log.Log_Warning(orchId, $"Telegram deleteForumTopic({topicId}) attempt {attemptsMade} gave no answer ({failure?.Message}) — retrying in {delay.TotalSeconds:0.#} s");

                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _log.Log_Info(orchId, $"Telegram topic {topicId} delete abandoned at shutdown — still pending, the next start retries it");
                    return;
                }

                continue;
            }

            Report_TopicDeleteFailure(orchId, topicId, outcome, attemptsMade, failure);
            return;
        }
    }

    /// <summary>Writes the fact down and says which door it came through — deleted by us, or already gone.</summary>
    void Record_TopicDeleted(string orchId, long topicId, TopicDeleteOutcomes outcome, int attemptsMade)
    {
        try
        {
            _store.Mark_TopicDeleted(orchId);
        }
        catch (Exception ex)
        {
            // The topic IS gone; failing to write that down must not turn a success into a crash on a
            // detached task. The sweep will re-attempt at the next start and get AlreadyGone, which
            // settles it again — costing one API call, which is the right price for this failure.
            _log.Log_Warning(orchId, $"Telegram topic {topicId} is gone but session.json could not record it ({ex.Message}) — the next start will confirm it again");
            return;
        }

        _log.Log_Info(orchId, outcome == TopicDeleteOutcomes.AlreadyGone
            ? $"Telegram topic {topicId} was already gone (attempt {attemptsMade}) — recorded as deleted"
            : $"Telegram topic {topicId} deleted (attempt {attemptsMade})");
    }

    /// <summary>
    /// A delete that did not land. ONE line in the log every time; ONE message in General ever, and
    /// only for a refusal — an unknown outcome is not something the owner can act on (decision 15),
    /// and repeating a refusal at every start is decision 14's waterfall.
    /// </summary>
    void Report_TopicDeleteFailure(string orchId, long topicId, TopicDeleteOutcomes outcome, int attemptsMade, Exception? failure)
    {
        var alreadyReported = _store.Get_Session_OrNull(orchId)?.TelegramTopicDeleteFailureReported ?? false;

        _log.Log_Error(orchId, outcome == TopicDeleteOutcomes.Refused
            ? $"Telegram refused to delete topic {topicId} of '{orchId}' after {attemptsMade} attempt(s) — the topic stays on the owner's phone until the bot's rights are restored"
            : $"Telegram topic {topicId} of '{orchId}' still not deleted after {attemptsMade} attempt(s) — left pending, the next start retries it",
            failure);

        if (!TopicDelete_Decider.Should_ReportToOwner(outcome, alreadyReported))
            return;

        // Marked BEFORE the append, and that order is the guarantee: an append that throws leaves the
        // flag set and the owner untold once, which is a missing alert; the other order leaves the
        // flag unset after a successful append, which is the alert repeating at every start for ever.
        // One missed line beats a waterfall.
        try
        {
            _store.Mark_TopicDeleteFailureReported(orchId);
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Could not record that the topic-delete failure of '{orchId}' was reported ({ex.Message}) — the alert is suppressed for this run only");
            return;
        }

        Append_GeneralAppEntry(AppEntryAudiences.Owner,
            $"Telegram would not delete the topic of '{orchId}'",
            $"The orchestration is closed and its topic (thread {topicId}) is still in the group — Telegram refused the delete: {failure?.Message}. "
            + "The usual cause is the bot losing 'Manage topics' rights in the supergroup. Restore them and the next app start deletes it; "
            + "until then you can delete the topic yourself from Telegram. Said once — it is not repeated at every start.");
    }

    /// <summary>
    /// EVERY START PAYS OFF THE DELETES THE LAST ONE COULD NOT — the half of brief E1 that an
    /// in-process retry cannot cover, because the case that actually strands a topic is the app
    /// being closed or killed while the delete was still failing.
    ///
    /// <para>
    /// Sequential and detached: sequential because a backlog fired at once spends the group's whole
    /// message allowance on housekeeping, and detached because the bridge must come up whether or
    /// not Telegram is answering. Only orchestrations carrying a PENDING stamp are touched — see
    /// <see cref="TopicDeleteSweep_Planner"/> for why the stamp's absence is load-bearing.
    /// </para>
    /// </summary>
    async Task Sweep_PendingTopicDeletes_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        IReadOnlyList<Sessions.OrchestrationSession.IOrchestrationSession> pending;

        try
        {
            pending = TopicDeleteSweep_Planner.Select_PendingDeletes(_store.Load_All());
        }
        catch (Exception ex)
        {
            // Broad by intent: an unreadable session folder must not stop the bridge from starting.
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Topic-delete reconciliation could not read the sessions ({ex.Message}) — skipped for this start");
            return;
        }

        if (pending.Count == 0)
            return;

        _log.Log_Info(GLOBAL_ORCH_ID, $"Topic-delete reconciliation: {pending.Count} closed orchestration(s) still owe Telegram a topic delete");

        foreach (var session in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var topicId = session.TelegramTopicId;

            if (topicId == null)
                continue;

            await Delete_TelegramTopic_WithRetries_Async(session.OrchId, topicId.Value, cancellationToken);
        }
    }

    /// <summary>
    /// Returns whether the entry landed.
    /// <para>
    /// These two wrappers carry the widest blast radius in the file: most of their callers append
    /// AFTER something irreversible — an orchestration closed, a session killed and respawned, a
    /// request file deleted or parked — and the entry is the only thing that tells the agent the
    /// irreversible thing happened. A dropped one leaves an agent whose world changed underneath it
    /// with no record of why.
    /// </para>
    /// <para>
    /// The result is surfaced rather than swallowed here, and logged against the orchestration so
    /// the failure is attributable even where a caller ignores it. Callers that record state on the
    /// strength of the entry must check it; the ones that do not are listed in the sweep's report.
    /// </para>
    /// </summary>
    bool Append_GeneralAppEntry(AppEntryAudiences audience, string subject, string body)
    {
        if (!ChannelAppender.Append_AppEntry(_paths.GeneralChannelFile, audience, subject, body, DateTime.Now))
        {
            _log.Log_Warning(ChannelDiscovery.GENERAL_ORCH_ID, $"General channel entry '{subject}' was NOT written — the channel was locked for the whole budget");
            return false;
        }

        Raise_OrchestrationActivity(ChannelDiscovery.GENERAL_ORCH_ID);
        return true;
    }

    /// <summary>Returns whether the entry landed. See <see cref="Append_GeneralAppEntry"/>.</summary>
    bool Append_OrchestrationAppEntry(string orchId, AppEntryAudiences audience, string subject, string body)
    {
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(ownerChannel))
        {
            _log.Log_Warning(orchId, $"No owner-channel.md for '{orchId}' — app entry '{subject}' logged only");
            return false;
        }

        if (!ChannelAppender.Append_AppEntry(ownerChannel, audience, subject, body, DateTime.Now))
        {
            _log.Log_Warning(orchId, $"Owner-channel entry '{subject}' was NOT written — the channel was locked for the whole budget");
            return false;
        }

        Raise_OrchestrationActivity(orchId);
        return true;
    }

    /// <summary>
    /// Same rule as the mirror loop: only a cancelled TOKEN may end this loop. This is the one
    /// whose silent death took the owner's phone offline on 2026-08-11 — the long poll hung, the
    /// 90 s HttpClient timeout raised a TaskCanceledException, and the bare catch returned without
    /// logging a thing, so the log stayed quiet instead of filling with backoff lines.
    /// </summary>
    /// <summary>
    /// True while `getUpdates` is being refused with 409. It exists so the owner is told ONCE that
    /// their phone has stopped being read, and once when it starts again — the failure used to log
    /// an Error on every retry for as long as it lasted, which is the shape nobody reads.
    /// </summary>
    bool _inboundConflicted;

    /// <summary>
    /// THE ONE-TIME HANDSHAKE, before the first poll: name the bot, and clear any webhook.
    ///
    /// <para>
    /// A WEBHOOK IS INDISTINGUISHABLE FROM A SECOND POLLER — Telegram allows one delivery mechanism
    /// per token and answers `getUpdates` with the same 409 either way. One left behind by an
    /// experiment, or by another tool sharing the token, could not be cleared from here at all, so
    /// the app would have sat in a permanent conflict it was able to fix in one call.
    /// </para>
    /// <para>
    /// `drop_pending_updates: false` — whatever the owner sent while the webhook was in the way is
    /// still theirs, and dropping it is the silent loss this brief exists to remove.
    /// </para>
    /// <para>
    /// BEST-EFFORT, AND THE LOOP STARTS EITHER WAY. This is a diagnostic and a repair, not a
    /// precondition: a network blip at startup must not be the reason the bridge never polls.
    /// </para>
    /// </summary>
    async Task Claim_TelegramInbound_BestEffort_Async(ITelegramApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            _botUsername = await client.Get_BotUsername_Async(cancellationToken);

            await client.Delete_Webhook_Async(dropPendingUpdates: false, cancellationToken);

            _log.Log_Info(
                GLOBAL_ORCH_ID,
                $"Telegram inbound claimed on {Environment.MachineName} as {Describe_Bot()} — any webhook on this token was cleared, pending updates kept");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(
                GLOBAL_ORCH_ID,
                $"Telegram startup handshake (getMe + deleteWebhook) failed on {Environment.MachineName} — polling anyway: {ex.Message}");
        }
    }

    /// <summary>The bot's own name, once `getMe` has answered — used only in what the owner is told.</summary>
    string _botUsername = "";

    string Describe_Bot()
    {
        return string.IsNullOrWhiteSpace(_botUsername) ? "this bot" : $"@{_botUsername}";
    }

    /// <summary>
    /// ONE MESSAGE, ONE LOG LINE, PER STATE CHANGE — never per retry. The owner can act on this and
    /// on nothing else about it: the two hosts are on different machines, so no file either of them
    /// can write is visible to the other, and the fix is to stop one of them or set
    /// <c>telegramInbound: off</c> on it. So the message NAMES THIS MACHINE — without it the owner
    /// reads "another bridge is polling" and cannot tell which of the two is complaining.
    /// </summary>
    async Task Note_InboundConflicted_IfNew_Async(ITelegramApiClient client, CancellationToken cancellationToken)
    {
        if (_inboundConflicted)
            return;

        _inboundConflicted = true;

        _log.Log_Error(
            GLOBAL_ORCH_ID,
            $"Telegram getUpdates refused with 409 CONFLICT on {Environment.MachineName} — another poller holds {Describe_Bot()}'s token. Backing off and retrying; this line is not repeated until it changes.",
            null);

        await Send_DirectReply_BestEffort_Async(
            client,
            null,
            $"⚠️ another bridge is polling {Describe_Bot()} with this token, so I am not reading your messages on "
            + $"{Environment.MachineName}. Is the Windows app running as well as the server? Stop one of them, or set "
            + $"\"telegramInbound\": \"{Telegram.TelegramInbound_Modes.OFF_TEXT}\" in that host's config.json — it will still mirror, it just will not read.",
            cancellationToken);
    }

    async Task Note_InboundRecovered_IfWasConflicted_Async(ITelegramApiClient client, CancellationToken cancellationToken)
    {
        if (!_inboundConflicted)
            return;

        _inboundConflicted = false;

        _log.Log_Info(GLOBAL_ORCH_ID, $"Telegram getUpdates is answering again on {Environment.MachineName} — the 409 conflict is over");

        await Send_DirectReply_BestEffort_Async(
            client, null,
            $"✅ I am reading your messages again on {Environment.MachineName}.",
            cancellationToken);
    }

    /// <summary>Shared by the 409 branch and the generic one, so one backoff cannot drift from the other.</summary>
    async Task Backoff_Inbound_Async(int backoffMilliseconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(backoffMilliseconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The loop's own condition ends it; a cancelled delay is not an error.
        }
    }

    static int Next_InboundBackoff(int backoffMilliseconds)
    {
        return Math.Min(backoffMilliseconds * 2, INBOUND_ERROR_BACKOFF_MAX_MILLISECONDS);
    }

    async Task Run_InboundLoop_Async(CancellationToken cancellationToken)
    {
        var client = _telegramClient
            ?? throw new Exception("Inbound loop started without a Telegram client");

        var startupConfig = _configProvider.Get_Current();

        var supergroupChatId = startupConfig.TelegramSupergroupChatId
            ?? throw new Exception("Inbound loop started without a supergroup chat id");

        var ownerUserId = startupConfig.TelegramOwnerUserId
            ?? throw new Exception("Inbound loop started without an owner user id");

        // MIRROR-ONLY IS A HOST DECISION, TAKEN BEFORE THE FIRST POLL. One bot token allows one
        // poller; two hosts that cannot see each other's supervision root — the app on a desk, the
        // daemon on a VPS — can only be separated by telling one of them, and this is where it is
        // told. Entries still reach the phone; nothing is read back on this host.
        if (startupConfig.TelegramInbound == Telegram.TelegramInboundModes.Off)
        {
            _log.Log_Warning(
                GLOBAL_ORCH_ID,
                $"telegramInbound is '{Telegram.TelegramInbound_Modes.OFF_TEXT}' on {Environment.MachineName} — this host mirrors to Telegram but does NOT read the owner's messages or taps. Another host is expected to poll.");

            return;
        }

        var backoffMilliseconds = INBOUND_ERROR_BACKOFF_START_MILLISECONDS;

        await Register_BotCommands_BestEffort_Async(client, cancellationToken);
        await Claim_TelegramInbound_BestEffort_Async(client, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var json = await client.Get_UpdatesJson_Async(_lastUpdateId + 1, INBOUND_LONG_POLL_SECONDS, cancellationToken);

                // SAID ONCE WHEN IT COMES BACK, for the same reason it is said once when it breaks:
                // the owner was told their phone had stopped being read, so they are told when it
                // starts again — and not on every poll in between.
                await Note_InboundRecovered_IfWasConflicted_Async(client, cancellationToken);

                var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, supergroupChatId, ownerUserId);

                // Bot commands: /dnd acts directly (and must NOT auto-unmute); /summary and
                // /pending become canned English requests routed to the general supervisor.
                List<ITelegramOwnerMessage> routableMessages = [];
                List<(string Command, long? ThreadId)> modeCommands = [];
                List<long?> presenceCommands = [];

                foreach (var message in batch.OwnerMessages)
                {
                    // ALREADY DONE ONCE IS NEVER DONE TWICE. `_lastUpdateId` advances at the END of
                    // the batch, so a crash — or, before this stage, any escaped exception — made
                    // Telegram re-serve every update in it. On 2026-09-08 01:24-01:26Z a Windows-only
                    // window call threw DllNotFoundException on the Linux daemon and the same batch
                    // was replayed four times: four copies of the owner's message, four `/pc` flips.
                    if (Was_UpdateHandled(message.UpdateId))
                    {
                        _log.Log_Info(
                            Describe_MessageOrch(message),
                            $"Update {message.UpdateId} was already handled — skipped on replay instead of acting twice");

                        continue;
                    }

                    // ONE UPDATE CANNOT TAKE THE BATCH DOWN. Everything from here to the end of this
                    // iteration is this message's own work; a throw is logged against the message
                    // and the next update is still handled, which is what makes the offset advance
                    // past all of them at the end.
                    var isRoutable = false;

                    try
                    {
                    // Tracked so /clear can remove the owner's own messages too.
                    Remember_TopicMessage(message.MessageThreadId, message.MessageId);

                    // ANY message means the owner is here — including a bot command, which never
                    // reaches Route_OwnerMessage_Async and so would otherwise leave away mode on
                    // while they are visibly typing /resume at it.
                    if (Note_OwnerSpoke_AndWasAway())
                        await Exit_AwayMode_Async(cancellationToken);

                    var command = Get_BotCommand_OrNull(message.Text);

                    // ONLY `/pc` ENDS TERMINAL MODE (owner's ruling, 2026-08-21). An ordinary message
                    // ends nothing: this used to revoke terminal mode on any inbound text, so the owner
                    // set `/pc`, glanced at their phone, and lost the mode seconds later without a word.
                    // A `/pc` still ends every OTHER topic's terminal mode — they cannot sit at two.
                    Flip_OtherTerminals_IfPresenceCommand(message.MessageThreadId, command == "pc");

                    // THE READ-BACK, HANDLED HERE AND NOT IN Route_OwnerMessage_Async, deliberately.
                    // A tapped option arrives at Route as a SYNTHETIC owner message carrying the
                    // option's own text, so a check placed there could be satisfied by an option
                    // whose label happens to be four digits — a tap completing a confirmation the
                    // same tap was supposed to require a second gesture for. Here the message is
                    // genuinely typed, by definition.
                    if (command == null
                        && message.VoiceFileId == null
                        && message.PhotoFileId == null
                        && await Try_CompleteHighRiskConfirmation_Async(client, message, cancellationToken))
                    {
                        continue;
                    }

                    // Telegram's own command menu only allows [a-z0-9_], so the menu entries are
                    // mute_all/dnd_all while a hand-typed mute-all works just as well.
                    if (command == "pc")
                    {
                        // Deferred with the mode commands, and for the same reason: toggling must not
                        // race the ✓ acks of the batch it arrived in.
                        presenceCommands.Add(message.MessageThreadId);
                    }
                    else if (command == "dnd" || command == "mute" || command == "unmute"
                        || command == "dnd-all" || command == "mute-all" || command == "dnd_all" || command == "mute_all")
                    {
                        // Deferred until after the loop: toggling must not race the ✓ acks, and a
                        // /dnd must not be auto-unmuted by the very message that requested it.
                        modeCommands.Add((command, message.MessageThreadId));
                    }
                    else if (command == "summary")
                    {
                        routableMessages.Add(Build_GeneralCommandMessage(message, GENERAL_SUMMARY_REQUEST));
                    }
                    else if (command == "pending")
                    {
                        // ANSWERED BY THE APP, not by the general supervisor. It used to be routed as
                        // an English instruction, which meant the list cost a model turn, arrived
                        // whenever that session next ran, and was reconstructed from channel files by
                        // something that might be mid-turn on something else. The app is holding the
                        // decisions in a field — the same argument /progress already won.
                        await Send_PendingDecisions_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    // "left" is an ALIAS, not a second implementation: it is the word the owner used
                    // ("a slash command that lets me know what's left"), and two commands reading one
                    // ledger would drift apart — the second-copy hazard applied to features.
                    else if (command == "progress" || command == "left")
                    {
                        // Answered by the APP straight from PLAN.md — instant, and it works even
                        // while the supervisor is mid-turn (which is exactly when it gets asked).
                        await Send_ProgressReport_Async(client, message.MessageThreadId, command, cancellationToken);
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
                    else if (command == "limits")
                    {
                        await Send_LimitsReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "context")
                    {
                        await Send_ContextReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "show")
                    {
                        await Show_SessionWindow_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "screen")
                    {
                        await Send_SessionScreenshot_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "screens")
                    {
                        await Toggle_StatusScreenshots_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "organize")
                    {
                        await Organize_SessionWindows_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "organize_mains" || command == "organize-mains")
                    {
                        // The hyphen is accepted for the same reason mute-all is (see above): the
                        // Telegram menu only offers the underscore, and an owner typing the shape
                        // they remember should not be answered with silence — an unmatched command
                        // falls through to the catch-all and is delivered to the session as chat.
                        await Organize_MainWindows_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "merge")
                    {
                        await Ask_SessionToMerge_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "test")
                    {
                        await Toggle_AwaitingTest_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "done")
                    {
                        await Toggle_Done_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "refresh")
                    {
                        await Refresh_TopicName_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "switch")
                    {
                        await Switch_OrchestrationShape_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "close")
                    {
                        await Request_Close_FromCommand_Async(client, message.MessageThreadId, cancellationToken);
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
                    else if (command is "tail" or "log")
                    {
                        await Send_TurnLog_Async(client, message.MessageThreadId, command, message.Text, cancellationToken);
                    }
                    else if (command != null && command.StartsWith("imp", StringComparison.Ordinal))
                    {
                        await Send_ImplementerPeek_Async(client, message.MessageThreadId, command, message.Text, cancellationToken);
                    }
                    else
                    {
                        routableMessages.Add(message);
                        isRoutable = true;
                    }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // A COMMAND THAT THREW IS NOT RETRIED. Its handler is what failed — a missing
                        // OS capability, a Telegram call that timed out — and re-serving the update
                        // runs the same handler against the same world. Said at Error, named by
                        // update, because a command the owner typed and never got an answer to is a
                        // thing they will ask about.
                        _log.Log_Error(
                            Describe_MessageOrch(message),
                            $"Handling update {message.UpdateId} failed — this one update is dropped, the rest of the batch continues",
                            ex);
                    }
                    finally
                    {
                        // A ROUTABLE MESSAGE IS NOT DONE YET: it is marked below, once it has been
                        // routed and answered. Everything else — a command, a read-back code, a
                        // failure — ends here.
                        if (!isRoutable)
                            Note_UpdateHandled(message.UpdateId);
                    }
                }

                // The owner texting or tapping ANYTHING (except a mode command) lifts app-wide DND
                // — before routing, so the ✓ acks go out.
                if ((routableMessages.Count > 0 || batch.CallbackTaps.Count > 0) && _telegramMuted)
                    Set_TelegramMuted(false);

                foreach (var message in routableMessages)
                {
                    try
                    {
                        if (await Apply_HoldControlWord_Async(client, message, cancellationToken))
                            continue;

                        var outcome = await Route_OwnerMessage_Async(message, cancellationToken);

                        // THE ✓ MEANS "IT ARRIVED", AND NOTHING ELSE MAY WEAR IT. It used to be sent
                        // after the routing call whatever the routing did: a message into an unknown
                        // topic was dropped with a warning and ticked on the same screen, and one
                        // into a CLOSED orchestration was written into a channel nobody tails and
                        // ticked the same way. The owner's words for this brief: the bridge must
                        // never "tell me it was received when it was not".
                        if (!OwnerRoute_Wording.Deserves_Receipt(outcome))
                        {
                            var refusal = OwnerRoute_Wording.Describe_ForOwner_OrNull(outcome);

                            if (refusal != null)
                                await Send_DirectReply_BestEffort_Async(client, message.MessageThreadId, refusal, cancellationToken);

                            continue;
                        }

                        // While HELD the phone stays quiet: no per-message tick. The single WAIT
                        // acknowledgement already said "I have you" and is updated with the count
                        // instead; the ✓/✓✓ pair comes after GO.
                        if (Is_TargetHeld(message))
                            await Update_HoldReceipt_Async(client, message, cancellationToken);
                        else
                            await Send_ReceivedAck_Async(client, message, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Log_Error(
                            Describe_MessageOrch(message),
                            $"Routing update {message.UpdateId} failed — this one message is dropped, the rest of the batch continues",
                            ex);
                    }
                    finally
                    {
                        Note_UpdateHandled(message.UpdateId);
                    }
                }

                foreach (var tap in batch.CallbackTaps)
                {
                    // BY THE CALLBACK'S OWN ID, not by the update id. Telegram re-serves the whole
                    // update on a replay, and a tap acted on twice is a decision taken twice — the
                    // one class of duplicate this system cannot afford, since the decisions that
                    // reach it include pushes and deploys.
                    if (Was_TapHandled(tap.CallbackQueryId))
                    {
                        _log.Log_Info(GLOBAL_ORCH_ID, $"Callback {tap.CallbackQueryId} was already handled — skipped on replay instead of firing twice");
                        continue;
                    }

                    try
                    {
                        await Handle_CallbackTap_Async(client, tap, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Log_Error(GLOBAL_ORCH_ID, $"Handling callback {tap.CallbackQueryId} failed — this one tap is dropped, the rest of the batch continues", ex);
                    }
                    finally
                    {
                        Note_TapHandled(tap.CallbackQueryId);
                        Note_UpdateHandled(tap.UpdateId);
                    }
                }

                foreach (var modeCommand in modeCommands)
                    await Apply_ModeCommand_Async(client, modeCommand.Command, modeCommand.ThreadId, cancellationToken);

                foreach (var presenceThreadId in presenceCommands)
                    await Apply_PresenceCommand_Async(client, presenceThreadId, cancellationToken);

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
            catch (Telegram.TelegramApiClient.TelegramApiException conflict) when (conflict.StatusCode == TELEGRAM_CONFLICT_STATUS)
            {
                // 409 IS NOT "A FAILURE" — IT IS A NAMED SITUATION with an action attached, and it
                // read as any other getUpdates error: one Error line per retry, for ever, while the
                // owner's taps went to whichever host won the race. Telegram returns it when a
                // SECOND poller holds the token, or when a WEBHOOK is registered against it (the
                // startup handshake clears that one).
                await Note_InboundConflicted_IfNew_Async(client, cancellationToken);

                await Backoff_Inbound_Async(backoffMilliseconds, cancellationToken);
                backoffMilliseconds = Next_InboundBackoff(backoffMilliseconds);
                continue;
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

    async Task Register_BotCommands_BestEffort_Async(ITelegramApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            // THE LIST LIVES IN Telegram.BotCommandMenu (brief F2): setMyCommands preserves
            // order, so it IS the menu the owner scrolls, and inline here nothing could assert
            // it. The engine names it and sends it; the reasoning travelled with it.
            await client.Set_MyCommands_Async(
                Telegram.BotCommandMenu.ALL,
                cancellationToken);

            // AND PIN THE BUTTON THAT OPENS THAT MENU. Registering the commands only says what the
            // menu CONTAINS; without this the `/` in the message box is left to the client default
            // and appears in some chats and not others — which is exactly what the owner reported on
            // 2026-08-25, with the list itself correct everywhere.
            //
            // Inside the same try as the commands, deliberately: the two are one feature, and a menu
            // button pinned to a command list that failed to register would be a button onto nothing.
            await client.Set_ChatMenuButton_ToCommands_Async(cancellationToken);
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

    /// <summary>
    /// The sentence `/summary` becomes, whether the owner TYPED it or TAPPED it. A constant because
    /// the button and the command must ask the general supervisor the same question — two spellings
    /// of it would be two commands wearing one name, which is the drift the shared bar exists to
    /// prevent.
    /// </summary>
    const string GENERAL_SUMMARY_REQUEST = "Make a summary of what is going on across all orchestrations.";

    /// <summary>A command becomes a canned English request for the GENERAL supervisor (thread null = general channel).</summary>
    static ITelegramOwnerMessage Build_GeneralCommandMessage(ITelegramOwnerMessage original, string cannedText)
    {
        return Build_GeneralCommandMessage(original.UpdateId, original.MessageId, original.ChatId, original.FromUserId, cannedText);
    }

    /// <summary>
    /// The same canned request, built from IDS rather than from a message — because a TAP has no
    /// owner message to copy. `ITelegramCallbackTap` carries an update id and a message id and
    /// nothing else, so a tapped `/summary` passes zeros for chat and sender, exactly as the
    /// close-confirmation tap already does for its own synthetic message.
    ///
    /// <para>
    /// ONE CONSTRUCTION SITE ON PURPOSE. The `isAppComposed` flag below is the whole reason this
    /// helper exists, and a second place that built this message would be a second place that could
    /// forget it: left unmarked, the canned sentence met the typed-answer binding — so with one
    /// question open in General, asking for a summary filed that sentence as the owner's answer to
    /// it. Same defect the tap fix closed (stage 8a), one route further along.
    /// </para>
    /// </summary>
    static ITelegramOwnerMessage Build_GeneralCommandMessage(long updateId, long? messageId, long chatId, long fromUserId, string cannedText)
    {
        return TelegramOwnerMessage_Factory.Create(
            updateId, messageId, chatId, fromUserId, null, cannedText, null, null,
            isAppComposed: true);
    }

    /// <summary>
    /// /progress — the PLAN.md task ledger, straight from disk. In a topic: that orchestration's
    /// full ledger; in General: one line per open orchestration. Deliberately NOT routed to the
    /// supervisor: this is asked precisely when the supervisor is mid-turn and cannot answer.
    /// </summary>
    async Task Send_ProgressReport_Async(ITelegramApiClient client, long? messageThreadId, string command, CancellationToken cancellationToken)
    {
        var text = Build_ProgressReportText(messageThreadId, unfinishedOnly: command == "left");

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    /// <summary>
    /// Which log scope a message sent in this topic belongs to. Named for the QUESTION it answers
    /// rather than for the lookup it performs: the same `Find_ByTelegramTopicId_OrNull` appears
    /// inline all over this file answering "which session is this", and this one answers "where does
    /// a line ABOUT it get written" — which has a different answer when there is no session.
    ///
    /// GENERAL IS NOT GLOBAL, and the first version of this got that wrong. `GLOBAL_ORCH_ID` is the
    /// EMPTY string, and `OrchestrationLogModel` writes a per-orchestration file only for a non-empty
    /// id while the app's log panel renders an empty one as no scope at all — so a diagnostic about
    /// the General topic reached neither the general log nor the eye, in exactly the scope the
    /// commit beside it exists to police. `ChannelDiscovery.GENERAL_ORCH_ID` is "general", it has a
    /// real folder, and the launcher and the watchdog already log General-scope lines under it.
    ///
    /// A topic bound to NO session keeps the global id, and that is not the same oversight: an
    /// unrecognised topic genuinely has no orchestration to name, where General has one.
    /// </summary>
    string Resolve_LogScope_ForTopic(long? messageThreadId)
    {
        if (messageThreadId == null)
            return ChannelDiscovery.GENERAL_ORCH_ID;

        return _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value)?.OrchId ?? GLOBAL_ORCH_ID;
    }

    /// <summary>
    /// /tasks — the FULL ledger, done lines included. The owner asked to keep this level of detail
    /// when /progress was shortened: "keep the current level of detail in a NEW command." Shortening
    /// the one command they had would have removed the view rather than moved it.
    /// </summary>
    async Task Send_TaskListReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_TaskListText(messageThreadId);

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

    string Build_ProgressReportText(long? messageThreadId, bool unfinishedOnly = false)
    {
        if (messageThreadId != null)
        {
            var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

            if (session == null)
                return "no orchestration is bound to this topic";

            return Build_OrchestrationLedgerText(session.OrchId, session.DisplayName ?? session.OrchId, unfinishedOnly);
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
    /// The counts, then the ledger as written — every row, in the file's own order.
    ///
    /// It began as "what's left to do" and answered with up to forty raw lines including everything
    /// finished, which on a 207-line ledger is a message nobody reads. The answer to that was to hide
    /// rows; the owner overruled it on 2026-08-13 — "I want to see all the rows, it must not be
    /// truncated" — because hiding them hides the ledger author's failure to group into 7-8 macro
    /// tasks. Short message, short LEDGER: the length is the supervisor's problem, upstream of here.
    /// </summary>
    string Build_OrchestrationLedgerText(string orchId, string displayName, bool unfinishedOnly = false)
    {
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(orchId)));

        if (progress == null)
            return $"{displayName}: no task ledger yet — the supervisor writes PLAN.md once you approve a direction";

        // NO `LEFT:` HEADER, since 2026-08-13: the block underneath now carries `[x]` and `[-]` rows
        // by the owner's own directive, so a header announcing what is left contradicts its own
        // content — and they would read that as a bug in the same breath as the fix they asked for.
        // The counts line above already says how much is left, in numbers.
        // /left renders the SAME parse through a different filter — never a second read of the
        // file, which is the drift the alias comment was guarding against and still is.
        var ledger = unfinishedOnly
            ? Planning.PlanProgress_Formatter.Describe_Unfinished(progress)
            : Planning.PlanProgress_Formatter.Describe_Ledger(progress);

        return $"{Build_OrchestrationCountsLine(orchId, displayName)}\n{ledger}";
    }

    /// <summary>
    /// <paramref name="previous"/> is passed by the PERIODIC push alone. `/status` is on demand and
    /// answers "where is this now", so a delta against a message the owner may not have been looking
    /// at would be a number with no visible baseline.
    /// </summary>
    string Build_OrchestrationCountsLine(string orchId, string displayName, Planning.PlanProgressSnapshot? previous = null)
    {
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(orchId)));

        if (progress == null)
            return $"{displayName}: no task ledger yet";

        return $"{displayName}: {Planning.PlanProgress_Formatter.Describe_Counts(progress, previous)}";
    }

    /// <summary>
    /// THE SEAM'S ONE CALL SITE — every orchestration's round trip with its plan backend, once a
    /// minute. Everything it decides lives in <see cref="PlanBackend_Step"/> and
    /// <see cref="PlanBackendSync_Decider"/>, which the suite can reach; what stays here is the loop
    /// and the log line.
    ///
    /// <para>
    /// IT DOES NOT RUN ON THE TICK. An adapter is code from outside this repository, called
    /// synchronously and with no timeout it could be held to; on the mirror tick — whose own docstring
    /// warns that one slow step "could spend ~15 s of waiting inside a 2 s loop, stalling the poll, the
    /// mirror, the tailer, compaction and the status push behind it" — a single blocked HTTP call would
    /// stall the owner's messages. So the tick STARTS the pass and returns; a pass already running is
    /// simply not started again. Nothing here is ordered against the rest of the tick.
    /// </para>
    /// </summary>
    void Start_PlanBackendPass()
    {
        var backend = Resolve_PlanBackend();

        if (!PlanBackendSync_Decider.Should_Sync(backend, _planBackendLastSyncUtc, DateTime.UtcNow))
            return;

        if (Interlocked.CompareExchange(ref _planBackendPassRunning, 1, 0) != 0)
            return;

        _planBackendLastSyncUtc = DateTime.UtcNow;

        _ = Task.Run(() =>
        {
            try
            {
                Sync_PlanBackends(backend);
            }
            catch (Exception ex)
            {
                _log.Log_Error(GLOBAL_ORCH_ID, "Plan backend pass failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _planBackendPassRunning, 0);
            }
        });
    }

    void Sync_PlanBackends(IPlanBackend backend)
    {
        foreach (var session in _store.Load_All())
        {
            try
            {
                var outcome = PlanBackend_Step.Sync(
                    backend,
                    _paths,
                    session.OrchId,
                    session.DisplayName ?? session.OrchId,
                    session.ClosedUtc != null,
                    () => Build_LedgerClosureEvidence(session.OrchId),
                    DateTime.Now);

                if (outcome.DidAnything)
                {
                    _log.Log_Info(
                        session.OrchId,
                        $"Plan backend: {outcome.RequestsIngested} request(s) ingested, {outcome.RequestsAcknowledged} acknowledged, {outcome.RowsReportedClosed} row(s) reported closed"
                            + (outcome.OrchestrationClosedReported ? ", orchestration closure reported" : ""));
                }

                if (outcome.Failure != null)
                    _log.Log_Warning(session.OrchId, $"Plan backend: {outcome.Failure}");
            }
            catch (Exception ex)
            {
                // One orchestration's backend must not cost every other one its synchronisation —
                // the same containment Refresh_ProgressArtefacts uses below.
                _log.Log_Error(session.OrchId, "Plan backend sync failed", ex);
            }
        }
    }

    /// <summary>
    /// Reloaded only when the configured settings actually change — <see cref="PlanBackend_Loader"/>
    /// touches the filesystem and reflection, which is not a per-tick cost. A failed load is announced
    /// ONCE and then runs as PLAN.md alone: repeating a warning every minute for a path that will not
    /// fix itself is the waterfall this app exists to prevent, and saying nothing at all would leave the
    /// owner believing their planning system is connected.
    /// </summary>
    IPlanBackend Resolve_PlanBackend()
    {
        var settings = _configProvider.Get_Current().PlanBackend;

        if (!PlanBackendSync_Decider.Needs_Reload(_planBackendLoaded, _planBackendSettings, settings))
            return _planBackend!;

        var load = PlanBackend_Loader.Load(settings);

        _planBackend = load.Backend;
        _planBackendSettings = settings;
        _planBackendLoaded = true;

        if (load.Error != null)
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Plan backend: {load.Error}");
        else if (settings?.Is_External() == true)
            _log.Log_Info(GLOBAL_ORCH_ID, $"Plan backend: loaded '{settings?.TypeName}'");

        return _planBackend;
    }

    /// <summary>
    /// The conversation entry live when a closed row was seen. Built LAZILY by the step — only for a
    /// pass that actually has a row to report — because parsing a channel on the chance that something
    /// closed is a read per orchestration per minute for a message that is almost never sent. The
    /// FORMAT is <see cref="PlanRowEvidence_Builder"/>'s, where a test can reach it.
    /// </summary>
    PlanRowEvidence Build_LedgerClosureEvidence(string orchId)
    {
        return PlanRowEvidence_Builder.Build(
            ChannelEntry_Parser.Parse_All(Read_FileText_Safe(_paths.Get_OwnerChannelFile(orchId))),
            DateTime.UtcNow);
    }

    /// <summary>
    /// Publishes each live orchestration's ledger reading for the supervisor's terminal status line.
    /// Local files only — nothing here talks to Telegram, which is why it runs above the DND gate.
    ///
    /// WHAT TO DO lives in <see cref="Planning.ProgressArtefact_Decider"/>; this is left with doing
    /// it. The engine is `internal sealed` with no `InternalsVisibleTo`, so a rule decided in here
    /// cannot be reached by the suite at all — which is how three guards were once deleted at once
    /// without reddening anything.
    /// </summary>
    void Refresh_ProgressArtefacts()
    {
        foreach (var session in Sessions_ThisTick())
        {
            if (session.ClosedUtc != null)
                continue;

            try
            {
                Refresh_ProgressArtefact(session.OrchId);
            }
            catch (Exception ex)
            {
                // One unreadable orchestration folder must not cost every other one its progress.
                _log.Log_Error(session.OrchId, "Progress artefact refresh failed", ex);
            }
        }
    }

    void Refresh_ProgressArtefact(string orchId)
    {
        var artefactFile = _paths.Get_ProgressFile(orchId);
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(orchId)));
        var json = progress == null ? null : Planning.ProgressArtefact_Builder.Build_Json(progress);

        _progressArtefactByOrchId.TryGetValue(orchId, out var lastWritten);

        var action = Planning.ProgressArtefact_Decider.Decide(
            json,
            lastWritten,
            File.Exists(artefactFile) ? File.GetLastWriteTime(artefactFile) : null,
            DateTime.Now);

        if (action == Planning.ProgressArtefactActions.None)
            return;

        if (action == Planning.ProgressArtefactActions.Delete)
        {
            _progressArtefactByOrchId.Remove(orchId);
            File.Delete(artefactFile);
            return;
        }

        Storage.Atomic_FileWriter.Write_AllText(artefactFile, json!);
        _progressArtefactByOrchId[orchId] = json!;
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
    /// /screens — the app-wide switch for the periodic status's screenshots. A toggle rather than
    /// two commands, the same shape as /test, and it ignores which topic it was sent
    /// from: the owner asked for one that works "independently from where I place the command".
    /// </summary>
    async Task Toggle_StatusScreenshots_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var enabled = !_configProvider.Get_Current().TelegramStatusScreenshots;
        Set_StatusScreenshots(enabled);

        // The camera on the General topic is the AMBIENT reminder that it is on, so it is pushed with
        // the reply rather than waiting for the next tick — the owner asked for the two together.
        //
        // AN EXPLICIT OWNER ACTION OUTRANKS A PENDING BACKOFF, exactly as Forget_AppliedTopicName_Async
        // says for the per-orchestration topics. The stamp is there to stop a tick-rate spin, not to
        // make the owner wait up to 30 s for the toggle they just sent — and the desired name has
        // changed, so this attempt is a different one from whatever failed.
        _generalTopicNameRetryAfterUtc = null;

        await Sync_GeneralTopicName_BestEffort_Async(client, cancellationToken);

        var text = enabled
            ? "📸 Status screenshots ON — every half-hourly status carries a picture of the session's terminal, taken only while you are away from the PC."
            : "📸 Status screenshots OFF — the half-hourly status is text only from here on.";

        await Send_DirectReply_BestEffort_Async(client, messageThreadId, text, cancellationToken);
    }

    /// <summary>
    /// /limits — the 5-hour and weekly usage windows, per model where the status line reports
    /// them. Data comes from the status-line probe files; every session writes what its Claude
    /// Code version exposes, and the WORST (highest) percent per window is what matters.
    /// </summary>
    /// <summary>
    /// The owner's own close, from their phone — the gap that produced this: a solo asked to "close
    /// this session" had the mechanism available and no instruction for it, so it told them to use
    /// the desktop app, which is no help to someone on a phone (2026-08-19).
    ///
    /// IT WRITES THE SAME REQUEST A SESSION WOULD, deliberately, rather than calling
    /// <see cref="Close_Orchestration_ByOwner"/> straight away. Every close-orchestration request
    /// PARKS and is confirmed with a tap, whoever asked — so a mistyped /close cannot end an
    /// orchestration, and the confirmation the owner sees is the one they already know.
    ///
    /// That also keeps the invariant Process_CloseOrchestrationRequests documents intact: nothing in
    /// the JSON can claim a confirmation that did not happen, because no field can wave a request
    /// through. This adds a way to ASK, not a way to skip the asking.
    /// </summary>
    /// <summary>
    /// Marks an endeavour FINISHED BUT UNTESTED: muted exactly as /mute mutes, and flagged so the
    /// topic carries 🧪 instead of 🔕.
    ///
    /// Their workflow, made visible (2026-08-19): they were muting a completed endeavour and then
    /// remembering unaided which of the muted ones still needed testing before being closed.
    ///
    /// A TOGGLE, like every other command here — their correction, and the reason there is no
    /// /untest. Toggling off returns the topic to Normal, which is what /mute does and what "the
    /// same as mute" has to mean if the word is to be trusted.
    ///
    /// Topic-scoped only: there is no app-wide variant, because "everything is awaiting a test" is
    /// not a state a person is ever in — the flag exists to distinguish one endeavour from another.
    /// </summary>
    /// <summary>
    /// Hands the SESSION the landing ritual. It is an agent-tagged entry, so it wakes whoever is
    /// driving this orchestration without putting a wall of git instructions on the owner's phone —
    /// they asked for the operation, not for the recipe.
    /// </summary>
    /// <summary>
    /// Puts the session the owner talks to in front of them — the solo in a basic orchestration, the
    /// supervisor otherwise. Their ask, 2026-08-20: "/Show should bring the solo or sup in front of
    /// the screen."
    /// </summary>
    async Task Show_SessionWindow_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        if (await Refuse_IfNoWindowing_Async(client, "show", messageThreadId, cancellationToken))
            return;

        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, "/show works inside an orchestration's own topic.", cancellationToken);
            return;
        }

        var window = _hostWindowing.Find_OwnerFacingWindow_OrNull(session);

        if (window == null)
        {
            // SAID, NOT SWALLOWED: the owner is looking at a screen that did not change, and "no
            // window" is a fact they can act on — the session may have been closed or never spawned.
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, "No terminal window for this orchestration is on screen.", cancellationToken);
            return;
        }

        if (_hostWindowing.Try_Focus(window))
        {
            _log.Log_Info(session.OrchId, $"/show — brought '{window}' to the front");
            return;
        }

        // Windows refuses SetForegroundWindow in ordinary situations, so a real window can fail to
        // raise. Telling them beats a silent no-op in front of an unchanged screen.
        await Send_DirectReply_BestEffort_Async(client, messageThreadId, "Found the window but Windows would not raise it — click its taskbar icon.", cancellationToken);
    }

    /// <summary>
    /// Two buttons per row. Six commands stacked one-per-row — the shape every other keyboard here
    /// uses — would put a slab of buttons under the one message in the topic the owner reads all day.
    /// </summary>
    const int COMMAND_BUTTONS_PER_ROW = 2;

    IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Build_CommandButtonRows(long messageThreadId)
    {
        // THE HOLD STATE IS THE ENGINE'S, so the bar is built with it rather than by it: the
        // buttons class knows the shape of a toggle and nothing about the delivery buffer.
        var (isHolding, heldCount) = Read_HoldState(messageThreadId);

        return Chunk_IntoRows(Telegram.TopicCommandButtons.Build_ForTopic(messageThreadId, isHolding, heldCount));
    }

    /// <summary>
    /// Whether this topic is holding, and how many messages have arrived since — for the PULSE
    /// bar's toggle (brief D).
    ///
    /// <para>
    /// A topic whose state cannot be read falls back to "not holding", which renders the ⏸ that
    /// STARTS a hold. That is the lesser of two bad readings and not a good one: if the topic was
    /// in fact holding, the bar then offers the action already in effect and hides the way out of
    /// it, and the owner's only exit is typing GO. It is chosen because the alternative — showing
    /// ▶ GO on a topic that is not holding — invites a tap that silently does nothing at all, and
    /// because typed GO always works. An earlier version of this comment claimed the opposite and
    /// was simply wrong.
    /// </para>
    /// </summary>
    (bool IsHolding, int HeldCount) Read_HoldState(long messageThreadId)
    {
        try
        {
            var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId);

            if (session == null)
                return (false, 0);

            var targetKey = _paths.Get_OwnerChannelFile(session.OrchId);

            return _ownerDeliveryBuffer.Is_Holding(targetKey)
                ? (true, _ownerDeliveryBuffer.Count_Pending(targetKey))
                : (false, 0);
        }
        catch (Exception ex)
        {
            // Broad by intent: the bar is furniture on a message that is edited every tick, and a
            // throw here would take the whole status line down over a button label.
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Could not read the hold state for topic {messageThreadId} ({ex.Message}) — the bar shows ⏸");
            return (false, 0);
        }
    }

    /// <summary>
    /// GENERAL's own bar — `/summary /pending /limits /resume /dnd_all`, the owner's list of
    /// 2026-09-09.
    ///
    /// <para>
    /// THREAD ID ZERO IS DELIBERATE and is what the parser round-trips for General. General is not a
    /// topic, so there is no thread to name; the tap handler reads a zero as "use the tap's own
    /// thread", which in General is null, which every command below already treats as General. A
    /// sentinel would be a second spelling of the same nothing.
    /// </para>
    /// <para>
    /// The bar was BUILT AND UNIT-TESTED SINCE 2026-09-09 AND NEVER RENDERED: `Build_ForGeneral` had
    /// no production caller at all, and the wiring guard that would have caught the three unhandled
    /// buttons walked `Commands` only, never `GeneralCommands`. Both halves are fixed here — this
    /// call site, and the guard.
    /// </para>
    /// </summary>
    static IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Build_GeneralCommandButtonRows()
    {
        return Chunk_IntoRows(Telegram.TopicCommandButtons.Build_ForGeneral(0));
    }

    /// <summary>
    /// ONE chunker for both bars. The loop existed once per caller for as long as there was one
    /// caller; a second copy of it is how the two bars come to wrap differently for no reason anybody
    /// decided.
    /// </summary>
    static IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Chunk_IntoRows(IReadOnlyList<(string Data, string Label)> buttons)
    {
        List<IReadOnlyList<(string Data, string Label)>> rows = [];

        for (var index = 0; index < buttons.Count; index += COMMAND_BUTTONS_PER_ROW)
            rows.Add([.. buttons.Skip(index).Take(COMMAND_BUTTONS_PER_ROW)]);

        return rows;
    }

    /// <summary>
    /// Photographs the session window and sends the picture to the topic — the owner asked to be able
    /// to SEE a session from their phone, not just be told about it.
    ///
    /// It resolves the same window /show does, deliberately: the two commands would be a trap if
    /// "the session" meant one thing when raising it and another when photographing it.
    /// </summary>
    async Task Send_SessionScreenshot_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        if (await Refuse_IfNoWindowing_Async(client, "screen", messageThreadId, cancellationToken))
            return;

        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, "/screen works inside an orchestration's own topic.", cancellationToken);
            return;
        }

        var window = _hostWindowing.Find_OwnerFacingWindow_OrNull(session);

        if (window == null)
        {
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, "No terminal window for this orchestration is on screen.", cancellationToken);
            return;
        }

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);
        var mediaFolder = Path.Combine(Path.GetDirectoryName(channelFile)
            ?? throw new Exception($"Channel file '{channelFile}' has no parent folder"), "media");

        // Named from the channel's own clock so two screenshots a second apart cannot collide, and so
        // the file itself says when it was taken when the owner goes looking later.
        var imagePath = Path.Combine(mediaFolder, $"screen-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        var failureReason = await _hostWindowing.Try_Capture_Async(window, imagePath, cancellationToken);

        if (failureReason != null)
        {
            // SAID, NOT SWALLOWED, exactly as /show does it: the owner is holding a phone that showed
            // them nothing, and a reason is something they can act on.
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, failureReason, cancellationToken);
            return;
        }

        _log.Log_Info(session.OrchId, $"/screen — captured '{window}' to {imagePath}");

        try
        {
            // SILENT: they typed /screen a moment ago and are looking at the screen. A reply to a
            // command is app traffic, however welcome it is.
            await client.Send_Photo_Async(messageThreadId, imagePath, TelegramSendSounds.Silent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // NOT best-effort here, unlike an IMAGE: line in a channel entry. That one is a decoration
            // on an entry that arrived anyway; this one IS the answer to the command, so failing it
            // silently would leave the owner watching a topic where /screen did nothing at all.
            _log.Log_Warning(session.OrchId, $"/screen — capture succeeded but the upload failed: {exception.Message}");
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, $"Took the picture but could not send it: {exception.Message}", cancellationToken);
        }
    }

    /// <summary>
    /// Tiles this orchestration's terminals, exactly as the app's Organize button does — the same
    /// code, so the phone and the button cannot drift apart.
    /// </summary>
    async Task Organize_SessionWindows_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        if (await Refuse_IfNoWindowing_Async(client, "organize", messageThreadId, cancellationToken))
            return;

        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, "/organize works inside an orchestration's own topic.", cancellationToken);
            return;
        }

        var placed = _hostWindowing.Organize(session);

        _log.Log_Info(session.OrchId, $"/organize — tiled {placed} terminal(s)");

        await Send_DirectReply_BestEffort_Async(
            client, messageThreadId,
            placed == 0
                ? "No terminal windows for this orchestration are on screen."
                : $"🪟 tiled {placed} terminal{(placed == 1 ? "" : "s")}.",
            cancellationToken);
    }

    /// <summary>
    /// /organize_mains — ONE TILE PER ORCHESTRATION, showing the session the owner actually talks to.
    /// Their words, 2026-08-21: *"a new command: /organize_mains which does the terminal organization
    /// but for all sups and solos (no general sup)"*.
    ///
    /// /organize answers "show me everything in THIS orchestration". This answers the other question
    /// — "show me every orchestration at once" — so it takes the supervisor of each crew and the solo
    /// of each basic one, and never a communicator, an implementer or a reviewer.
    ///
    /// APP-WIDE, so it works from any topic and only uses the thread id to reply where they typed —
    /// the same shape as /resume. Requiring General would be a rule they would have to remember.
    ///
    /// GENERAL IS EXCLUDED STRUCTURALLY, not by a check: it has no session.json, so Load_All never
    /// yields it. That is worth stating because the exclusion is invisible in this code — and it is
    /// the right kind of invisible, since it is also the window they would be organizing FROM.
    /// </summary>
    async Task Organize_MainWindows_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        if (await Refuse_IfNoWindowing_Async(client, "organize_mains", messageThreadId, cancellationToken))
            return;

        List<Sessions.OrchestrationSession.IOrchestrationSession> open = [];

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc == null)
                open.Add(session);
        }

        var placed = _hostWindowing.Organize_MainWindows(open);

        _log.Log_Info(GLOBAL_ORCH_ID, $"/organize_mains — tiled {placed} main terminal(s) across {open.Count} open orchestration(s)");

        await Send_DirectReply_BestEffort_Async(
            client, messageThreadId,
            placed == 0
                ? "No supervisor or solo terminals are on screen."
                : $"🪟 tiled {placed} main terminal{(placed == 1 ? "" : "s")}.",
            cancellationToken);
    }

    async Task Ask_SessionToMerge_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                "/merge works inside an orchestration's own topic — there is no work here to land.",
                cancellationToken);

            return;
        }

        if (!Append_OrchestrationAppEntry(session.OrchId, AppEntryAudiences.Agent, MergeRitual_Wording.SUBJECT, MergeRitual_Wording.Build()))
        {
            // Told rather than swallowed: the session's own report is the only other feedback this
            // command has, and if the entry never landed that report is never coming.
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId, "/merge could not reach the session — nothing was asked. Try again.", cancellationToken);

            return;
        }

        _log.Log_Info(session.OrchId, "/merge — the session was asked to run the landing ritual");

        Raise_OrchestrationActivity(session.OrchId);

        await Send_DirectReply_BestEffort_Async(
            client, messageThreadId,
            "Asked. It merges, runs the full suite on the merged tree, and pushes only if that is green — then cleans up and reports.",
            cancellationToken);
    }

    async Task Toggle_AwaitingTest_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                "/test works inside an orchestration's own topic — there is nothing here to mark.",
                cancellationToken);

            return;
        }

        try
        {
            var turningOn = !session.AwaitingTest;

            // The MODE really is Silenced underneath — /test IS mute, so its behaviour is not
            // re-derived here, it is the same setting written by the same store call.
            _store.Set_AwaitingTest(session.OrchId, turningOn);
            _store.Set_TelegramMode(session.OrchId, turningOn ? TelegramDeliveryModes.Silenced : TelegramDeliveryModes.Normal);

            _log.Log_Info(session.OrchId, turningOn
                ? "/test — marked finished-but-untested, and muted"
                : "/test — the awaiting-test mark was cleared, back to Normal");

            Raise_OrchestrationActivity(session.OrchId);

            // BEFORE the new mode takes hold on the next tick, so the confirmation itself gets through.
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                turningOn
                    ? "🧪 marked for testing — muted like /mute, and the topic keeps 🧪 so you know not to close it yet."
                    : "🧪 cleared — this topic is back to normal.",
                cancellationToken);

            await Forget_AppliedTopicName_Async(session.OrchId, cancellationToken);
            await Sync_TopicNames_BestEffort_Async(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, "/test failed", ex);

            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId, $"/test failed — nothing was changed: {ex.Message}", cancellationToken);
        }
    }

    /// <summary>
    /// FINISHED, AND KEPT OPEN ON PURPOSE. The owner asked for this on 2026-08-21: they wanted the
    /// last step of their own workflow to have a mark, without paying /close for it — *"I still
    /// don't want to close the topic in case I have something else to do later"*.
    ///
    /// Built as a sibling of /test rather than as a new delivery mode, because it is the same shape:
    /// a persisted statement about the ENDEAVOUR that happens to mute the topic. /test says "I have
    /// not checked this yet", /done says "I have, and it is finished".
    /// </summary>
    /// <summary>
    /// RE-ASSERT THIS TOPIC'S NAME, whatever the app currently believes about it.
    ///
    /// `_appliedTopicNames` is a BELIEF, not an observation: the sync skips a topic whose wanted name
    /// equals the name it thinks it last applied, and nothing ever checks that against Telegram. So
    /// any divergence is permanent — a push that was refused but recorded as applied (the residual
    /// the sync's own catch admits to), a name edited by hand in the Telegram client, a topic
    /// restored from under the app. The glyph the owner sees then has no route back to the truth,
    /// and the app is sincerely reporting that everything is fine.
    ///
    /// The owner, 2026-08-25, after a ❓ that stayed through their answer: *"It's incredibly
    /// frustrating because I don't understand if I need to intervene or not. If you really can't fix
    /// this thing that we've worked on so much, at least give me a command that lets me remove it
    /// manually by my own choice."*
    ///
    /// This is that command, and it is deliberately dumber than a fix for any one cause: it drops the
    /// belief and the backoff, recomputes from persisted state, and pushes. It therefore repairs a
    /// stuck glyph WITHOUT needing to know which of the causes above produced it.
    /// </summary>
    /// <summary>
    /// How long a `/switch` stays armed waiting for its confirming repeat. Long enough to read the
    /// warning on a phone and think; short enough that a `/switch` sent an hour ago cannot be
    /// completed by one sent now, when the owner has forgotten the first.
    /// </summary>
    const int SWITCH_CONFIRM_WINDOW_SECONDS = 120;

    /// <summary>Orchestrations where one `/switch` has been seen and is waiting for its repeat.</summary>
    readonly Dictionary<string, DateTime> _switchArmedUtc = [];

    /// <summary>
    /// Orchestrations already told that their parked request cannot be put to the owner. A set rather
    /// than a flag per request because the fault is a property of the ORCHESTRATION (no topic, or no
    /// Telegram at all), and the notice must be said once rather than every two seconds until it
    /// lapses.
    /// </summary>
    readonly HashSet<string> _confirmationsUnaskable = [];

    /// <summary>
    /// ONE COMMAND, BOTH DIRECTIONS — the owner's ask, 2026-08-25: *"there needs to be a command that
    /// imposes a standardized procedure, handoff writing and promotion to sup or solo. This command
    /// must be bidirectional — with the same command I transform an orchestra into solo, and a solo
    /// into an orchestra."*
    ///
    /// The direction is READ FROM THE SHAPE rather than typed, which is the whole point: the owner
    /// sends one verb and never has to remember which of two commands this topic needs.
    ///
    /// WHY IT CONFIRMS BY REPEAT rather than by a tap. The agent-initiated promotion parks a request
    /// and asks for a tap, and that is right — a SESSION is asking to spend the owner's money, so the
    /// owner must agree. Here the owner is the one typing, so a button asking them to confirm their
    /// own command is a hop, and hops are what broke the feature they are complaining about: *"it was
    /// reasoning for like 5 minutes and then nothing happened."* The repeat is stated explicitly in
    /// the first reply, with the count of sessions that will die, so it is the opposite of the silent
    /// toggle `/done` was.
    ///
    /// THE HANDOVER GATE IS KEPT, in both directions, because it protects something a confirmation
    /// cannot: whatever the outgoing session worked out and never wrote down dies with its terminal.
    /// It is also the "standardized procedure" half of the ask — the app states the requirement and
    /// tells the session to write it, instead of the session reasoning about the protocol itself.
    /// </summary>
    async Task Switch_OrchestrationShape_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                "/switch changes an orchestration between one session and a full crew — there is nothing here to switch.",
                cancellationToken);

            return;
        }

        var promoting = Sessions.OrchestrationShape.Would_Promote(session.SupervisorSpawnedUtc);

        // ASKED AT THE MOMENT OF EFFECT, the rule both launcher methods already follow.
        var canAct = promoting
            ? Sessions.OrchestrationShape.Can_StillPromote(Sessions.OrchestrationShape.Decide_PromotionReadiness(
                session.SupervisorSpawnedUtc, Sessions.OrchestrationShape.Has_LiveSolo(session.Members)))
            : Sessions.OrchestrationShape.Can_StillDemote(Sessions.OrchestrationShape.Decide_DemotionReadiness(
                session.SupervisorSpawnedUtc, Sessions.OrchestrationShape.Has_LiveSolo(session.Members)));

        if (!canAct)
        {
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                promoting
                    ? "/switch — there is no live session here to promote. Nothing was changed."
                    : "/switch — this is already one session. Nothing was changed.",
                cancellationToken);

            return;
        }

        // The author who must have written the handover is the one about to be REPLACED.
        var outgoingAuthor = promoting ? Channels.ChannelAuthors.Solo : Channels.ChannelAuthors.Supervisor;
        var outgoingName = promoting ? "solo" : "supervisor";

        if (!HandoverEntry_Detector.Has_HandoverEntry(Read_OwnerChannelEntries(session.OrchId), outgoingAuthor))
        {
            // THE APP ASKS THE SESSION, rather than leaving the owner to. This is the "imposes a
            // standardized procedure" half: the session is told exactly what to write and why, in its
            // own channel, where its watcher will wake it.
            Append_OrchestrationAppEntry(
                session.OrchId, AppEntryAudiences.Agent,
                $"the owner asked to switch this to {(promoting ? "a full crew" : "one session")} — write your HANDOVER now",
                $"Your session ENDS when this happens, and everything you know that is not in this channel dies with it.\n\n"
                + $"Append an entry whose SUBJECT carries `{HandoverEntry_Detector.HANDOVER_MARKER}` and put in it: where the work actually stands, what you tried that did NOT work, what is half-done and in which files, and the traps.\n\n"
                + "Nothing has changed yet and you are still the session here. The owner completes the switch once your entry is filed.");

            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                $"✋ Not yet — the {outgoingName} has not written its HANDOVER, and everything it knows that is not in the channel would be lost. I have just asked it to write one. Send /switch again once it has.",
                cancellationToken);

            return;
        }

        var armedUtc = _switchArmedUtc.TryGetValue(session.OrchId, out var stamp) ? stamp : (DateTime?)null;
        var armed = armedUtc != null && (DateTime.UtcNow - armedUtc.Value).TotalSeconds <= SWITCH_CONFIRM_WINDOW_SECONDS;

        if (!armed)
        {
            _switchArmedUtc[session.OrchId] = DateTime.UtcNow;

            var doomed = promoting
                ? session.Members.Count(m => m.ClosedUtc == null && Sessions.MemberKind_Ids.Resolve_Kind(m.MemberId) == Sessions.MemberKinds.Solo)
                : session.Members.Count(m => m.ClosedUtc == null && Sessions.MemberKind_Ids.Resolve_Kind(m.MemberId) != Sessions.MemberKinds.Solo) + 1;

            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                promoting
                    ? $"⚙️ This will turn the topic into a FULL CREW: the solo ends and a supervisor plus imp-1 take over this same conversation. {doomed} session(s) will be closed.\n\nSend /switch again within 2 minutes to go ahead."
                    : $"⚙️ This will turn the topic back into ONE SESSION: the supervisor and every member end, and a solo takes over this same conversation. {doomed} session(s) will be closed.\n\nSend /switch again within 2 minutes to go ahead.",
                cancellationToken);

            return;
        }

        _switchArmedUtc.Remove(session.OrchId);

        try
        {
            if (promoting)
                _launcher.Promote_ToFullCrew(session.OrchId);
            else
                _launcher.Demote_ToBasic(session.OrchId);

            // READ BACK, never assumed. Both launcher methods return early without acting when the
            // shape moved under them, and reporting a switch that did not happen is the failure the
            // promotion path already had: it announced "PROMOTED" over a silent early return.
            var after = _store.Get_Session(session.OrchId);
            var switched = Sessions.OrchestrationShape.Would_Promote(after.SupervisorSpawnedUtc) != promoting;

            _log.Log_Info(session.OrchId, switched
                ? $"/switch — {(promoting ? "promoted to a full crew" : "demoted to one session")} by the owner"
                : "/switch — the launcher declined; the shape is unchanged");

            await Forget_AppliedTopicName_Async(session.OrchId, cancellationToken);
            await Sync_TopicNames_BestEffort_Async(cancellationToken);

            Raise_OrchestrationActivity(session.OrchId);

            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                switched
                    ? (promoting
                        ? "✅ Now a full crew. A supervisor has taken over this conversation and imp-1 is waiting for a brief — it reads everything above, including the handover."
                        : "✅ Now one session. A solo has taken over this conversation and reads everything above, including the handover.")
                    : "/switch did not change anything — the shape moved before it ran. Send it again if it is still wanted.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, "/switch failed", ex);

            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId, $"/switch failed: {ex.Message}", cancellationToken);
        }
    }

    async Task Refresh_TopicName_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                "/refresh re-syncs an orchestration topic's name — there is nothing here to re-sync.",
                cancellationToken);

            return;
        }

        try
        {
            await Forget_AppliedTopicName_Async(session.OrchId, cancellationToken);
            await Sync_TopicNames_BestEffort_Async(cancellationToken);

            var wantedName = Build_WantedTopicName(session);
            var applied = _appliedTopicNames.TryGetValue(session.OrchId, out var name) && name == wantedName;

            _log.Log_Info(session.OrchId, applied
                ? $"/refresh — topic name re-asserted as '{wantedName}'"
                : $"/refresh — topic name '{wantedName}' was NOT accepted by Telegram this attempt");

            // THE NAME IS QUOTED EITHER WAY. The owner is using this command precisely because they
            // no longer trust what the topic list shows, so a bare "done" would be asking them to
            // take the app's word for it a second time.
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                applied
                    ? $"♻ re-synced — this topic is now named “{wantedName}”. If a glyph in it still looks wrong, the state behind it is real and not a stale name."
                    : $"♻ tried, and Telegram has not accepted “{wantedName}” yet. It retries on its own; send /refresh again in a moment.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, "/refresh failed", ex);

            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId, $"/refresh failed — the name was not changed: {ex.Message}", cancellationToken);
        }
    }

    async Task Toggle_Done_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                "/done works inside an orchestration's own topic — there is nothing here to mark.",
                cancellationToken);

            return;
        }

        try
        {
            // `/done` IS NOT A TOGGLE ANY MORE, and this is the whole fix.
            //
            // It was one, and every single use of it in this machine's history was undone within
            // seconds: 2026-08-21 14:21:48 marked and 14:22:05 cleared (17 s), 2026-08-24 08:53:45
            // and 08:54:02 (17 s), 2026-08-24 21:48:32 and 21:48:55 (23 s). A rename takes a moment
            // to surface in Telegram's topic list, so when nothing appeared the owner sent the
            // command again — which is the natural thing to do, and which silently un-marked it.
            // They reported the result as the tick never being added at all (2026-08-25): *"the
            // /done command still doesn't put the check in the topic name. It simply doesn't
            // happen — it sends a confirmation message that starts with the check, but doesn't
            // change the topic name."*
            //
            // Rewording the two replies to be distinguishable was tried first and did not hold —
            // the second press happens before the first reply has been read. So the command now
            // only ever means what its name says, and repeating it is harmless.
            //
            // NOTHING IS LOST WITH THE TOGGLE. Un-finishing already has its own route, it is more
            // natural, and this reply has always advertised it: writing anything in the topic clears
            // the mark and unmutes (Wake_DoneTopic_IfNeeded). An endeavour you are talking about
            // again is not a finished one.
            var alreadyDone = session.Done;

            _store.Set_Done(session.OrchId, true);

            // FINISHING SUPERSEDES "still to be tested". Leaving 🧪 set behind the scenes would mean
            // clearing /done later silently restores a reminder the owner has already discharged.
            if (session.AwaitingTest)
                _store.Set_AwaitingTest(session.OrchId, false);

            // Muted underneath, exactly as /test is — a finished endeavour should stop texting them.
            _store.Set_TelegramMode(session.OrchId, TelegramDeliveryModes.Silenced);

            _log.Log_Info(session.OrchId, alreadyDone
                ? "/done — already finished, the mark and the mute were re-asserted"
                : "/done — marked finished and muted, topic kept open");

            Raise_OrchestrationActivity(session.OrchId);

            // THE NAME IS PUSHED BEFORE THE REPLY, so the reply can state what the topic actually
            // reads. The owner's complaint is that the confirmation and the topic list disagreed,
            // and they had no way to tell which was right without hunting for the topic; a
            // confirmation that quotes the name it just set closes that gap, and a rename that
            // failed no longer hides behind a cheerful ✅.
            await Forget_AppliedTopicName_Async(session.OrchId, cancellationToken);
            await Sync_TopicNames_BestEffort_Async(cancellationToken);

            var marked = _store.Get_Session_OrNull(session.OrchId) ?? session;
            var wantedName = Build_WantedTopicName(marked);
            var renamed = _appliedTopicNames.TryGetValue(session.OrchId, out var applied) && applied == wantedName;

            var reply = renamed
                ? $"✅ marked finished — the topic is now “{wantedName}”, and it is muted but stays open. Write anything here to un-finish it; that alone wakes it up. Sending /done again changes nothing."
                : $"✅ marked finished and muted — but Telegram has not accepted the new topic name “{wantedName}” yet. It retries on its own; the mark itself is saved. Write anything here to un-finish it.";

            await Send_DirectReply_BestEffort_Async(client, messageThreadId, reply, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, "/done failed", ex);

            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId, $"/done failed — nothing was changed: {ex.Message}", cancellationToken);
        }
    }

    /// <summary>
    /// A FINISHED TOPIC WAKES UP THE MOMENT THE OWNER USES IT AGAIN, because /done is the one state
    /// in this app deliberately designed to OUTLIVE the work it describes. They kept the topic open
    /// *"in case I have something else to do later"*, so later is the case that has to work.
    ///
    /// Without this it does not. /done mutes, nothing else in the app ever clears a per-topic mute,
    /// and Silenced DROPS rather than defers — so the owner would come back, type, get the ✓ tick
    /// (acks are ungated), watch the session genuinely start work, and never hear another word from
    /// it. One-way and silent, with no self-healing.
    ///
    /// The app has been caught by exactly this shape before, and wrote the rule down: *"a mode they
    /// must remember to turn off is one they get trapped by"* — terminal mode, which for the same
    /// reason now exits itself when they text from Telegram.
    ///
    /// ONLY REAL MESSAGES REACH HERE. Recognised commands are dispatched before routing, so reading
    /// a finished topic with /progress or /tasks inspects it without declaring it unfinished; it
    /// takes actual new work to bring it back.
    /// </summary>
    void Wake_DoneTopic_IfNeeded(IOrchestrationSession session)
    {
        if (!session.Done)
            return;

        // Both halves, because /done set both: the flag is what the glyph reads, and the mode is
        // what actually decides whether they hear anything back.
        _store.Set_Done(session.OrchId, false);
        _store.Set_TelegramMode(session.OrchId, TelegramDeliveryModes.Normal);

        _log.Log_Info(session.OrchId, "the owner wrote to a finished topic — ✅ cleared and unmuted so their reply can reach them");
    }

    async Task Request_Close_FromCommand_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var session = messageThreadId == null ? null : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null || session.ClosedUtc != null)
        {
            // General has no orchestration of its own, and a closed one has nothing left to end.
            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId,
                "/close works inside an orchestration's own topic — there is nothing here to close.",
                cancellationToken);

            return;
        }

        try
        {
            var path = Path.Combine(_paths.RequestsFolder, $"close-{session.OrchId}-{Guid.NewGuid():N}.json");

            File.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    action = "close-orchestration",
                    orchId = session.OrchId,
                    requester = "owner",
                    reason = "/close from Telegram",
                }));

            _log.Log_Info(session.OrchId, "/close — a close-orchestration request was written on the owner's behalf; they will be asked to confirm");
        }
        catch (Exception ex)
        {
            // Told, rather than swallowed: the confirmation prompt is the only feedback this command
            // has, so a failure that said nothing would read as the app ignoring them.
            _log.Log_Error(session.OrchId, "/close could not write the close request", ex);

            await Send_DirectReply_BestEffort_Async(
                client, messageThreadId, $"/close failed — nothing was closed: {ex.Message}", cancellationToken);
        }
    }

    async Task Send_LimitsReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_LimitsReportText();

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_LimitsReportText()
    {
        // The live-window filter is what keeps the "seen from" model list honest too: a five-day-old
        // closed orchestration was still contributing its model name to a report about right now.
        var now = DateTime.Now;
        var windows = RateLimits_Reader.Read_WorstAcrossSessions(RateLimits_Reader.Find_UsageFiles_WithLiveWindow(_paths, now), now);

        // FIRST LINE WHEN IT IS ON. The owner asks /limits precisely when things feel stuck, and
        // "the app has stopped starting sessions until 19:40" is the answer to the question they are
        // actually asking — it must not sit under the percentages, or below a "nothing to report".
        var pauseLine = Describe_DispatchPause_OrNull();

        if (windows.Count == 0)
        {
            return pauseLine ?? "no CURRENT limit windows to report — either every window on disk has already reset, or this Claude Code version's status line carries no limit data at all (the automatic alerts read the same probe files, so they are idle for whichever reason applies)";
        }

        List<string> lines = [];

        if (pauseLine != null)
            lines.Add(pauseLine);

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
    /// The dispatcher pause as one line, or null when it is running. Reads the same fields the tick
    /// writes — never a second computation of whether it is paused.
    /// </summary>
    string? Describe_DispatchPause_OrNull()
    {
        DateTime? pausedUntilUtc;
        string? reason;

        // Read under the lock the writer takes — see Update_DispatchPause_Async for why a 16-byte
        // field read from the other loop is not free.
        lock (_ownerStateLock)
        {
            pausedUntilUtc = _dispatchPausedUntilUtc;
            reason = _dispatchPauseReason;
        }

        if (!Limits.DispatchPause_Gate.Is_Paused(pausedUntilUtc, _clock.UtcNow))
            return null;

        return $"⏸ DISPATCH PAUSED — {reason ?? "a usage limit was reached"}. No new sessions are started or respawned; work already running finishes. Resuming at {pausedUntilUtc:HH:mm} UTC.";
    }

    /// <summary>
    /// /context — context-window usage per session. In a topic: that orchestration's sessions
    /// broken down; in General: all active orchestrations and their worst context window.
    /// </summary>
    async Task Send_ContextReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_ContextReportText(messageThreadId);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    /// <summary>
    /// GATHERS, and composes nothing: every judgement - closed sessions, unknown readings, which
    /// session is fullest, when a figure is old enough to date - is Status.ContextReport_Composer's,
    /// where the suite can reach it.
    /// </summary>
    string Build_ContextReportText(long? messageThreadId)
    {
        if (messageThreadId != null)
        {
            var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

            if (session == null)
                return "no orchestration is bound to this topic";

            return Status.ContextReport_Composer.Build_ForOrchestration(
                session.DisplayName ?? session.OrchId,
                Read_ContextRows(session),
                DateTime.UtcNow);
        }

        List<Status.ContextReport_Composer.OrchestrationRows> orchestrations = [];

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            orchestrations.Add(new Status.ContextReport_Composer.OrchestrationRows(
                session.DisplayName ?? session.OrchId,
                Read_ContextRows(session)));
        }

        return Status.ContextReport_Composer.Build_ForEveryOrchestration(
            UsageTotals_Reader.Read_ContextUsage_OrNull(
                Path.Combine(_paths.GeneralFolder, UsageTotals_Reader.SESSION_USAGE_FILE)),
            orchestrations);
    }

    /// <summary>
    /// Every probe source of one orchestration, read once, carrying whether its session is closed.
    /// The source list is UsageTotals_Reader's, so /context, /cost and /tokens can never disagree
    /// about which sessions exist or where each one writes.
    /// </summary>
    IReadOnlyList<Status.ContextReport_Composer.ContextRow> Read_ContextRows(IOrchestrationSession session)
    {
        List<Status.ContextReport_Composer.ContextRow> rows = [];

        foreach (var source in UsageTotals_Reader.Build_ProbeSources(_paths, session))
        {
            rows.Add(new Status.ContextReport_Composer.ContextRow(
                source.Label,
                Is_ClosedMember(session, source.Label),
                UsageTotals_Reader.Read_ContextUsage_OrNull(source.File)));
        }

        return rows;
    }

    /// <summary>
    /// Whether a probe source's label names a member that has been closed. The supervisor and
    /// communicator labels are not members and are never closed by this test - their absence is
    /// already expressed by having no probe file to read.
    /// </summary>
    static bool Is_ClosedMember(IOrchestrationSession session, string label)
    {
        foreach (var member in session.Members)
        {
            if (member.MemberId == label)
                return member.ClosedUtc != null;
        }

        return false;
    }

    // THERE IS NO /compact HERE, DELIBERATELY, and it is not an omission to be filled in.
    //
    // A running Claude Code session cannot be made to compact from outside. That was established by
    // measurement on 2026-08-21, not by reading: WriteConsoleInput into the session's console reaches
    // cmd.exe but not claude.exe, and SendInput with the terminal forced to the foreground delivered
    // 54 synthetic keystrokes to a live session which submitted none of them. Claude Code exposes no
    // control file, socket, signal or CLI verb for it either — PreCompact can BLOCK a compaction that
    // is already happening, never request one.
    //
    // A draft of this file did ship a /compact that appended "please compact if beneficial" to the
    // channel and answered the owner with a ✓. That is the Try_Rename_ByTitleFragment failure again
    // (see TerminalWindow_Focuser): a verb that reports success and changes nothing, with the log
    // then agreeing it worked. The session it asks has no way to honour the request.
    //
    // The owner dropped the command on being told, 2026-08-21: sessions auto-compact on their own.
    // If it is ever wanted again, the honest routes are `claude --autocompact <tokens>` at SPAWN
    // (a real flag on this version) or a handover-and-respawn, which resets the window for real
    // because the channel survives the restart.

    /// <summary>
    /// /diff — GROUND TRUTH from git, not agent prose: branch, ahead/behind, dirty files and the
    /// latest commits for the repo and every worktree the orchestration uses.
    /// </summary>
    async Task Send_GitReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_GitReportText(messageThreadId);

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

            await Forget_AppliedTopicName_Async(session.OrchId, cancellationToken);
            await Sync_TopicNames_BestEffort_Async(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, $"'{command}' failed", ex);
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, $"could not change the mode: {ex.Message}", cancellationToken);
        }
    }

    /// <summary>
    /// `/pc` — the owner is (or is no longer) at THIS orchestration's terminal. Scoped to the topic
    /// it was typed in, like the mode commands; a `/pc_all` is deliberately NOT built yet, but this
    /// is one loop away from being one.
    /// </summary>
    async Task Apply_PresenceCommand_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        // NO THREAD ID IS THE GENERAL TOPIC, and the owner sits at that terminal too — it is the
        // session they talk to most. General keeps no session.json, so its meeting flag IS its
        // presence rather than a projection of one.
        if (messageThreadId == null)
        {
            var generalPresence = OwnerPresence_Policy.Toggle(Resolve_Presence(ChannelDiscovery.GENERAL_ORCH_ID));

            Sync_MeetingFlag_AndReport(ChannelDiscovery.GENERAL_ORCH_ID, generalPresence);
            Apply_Presence_ToAwaitingAnswerFlag(ChannelDiscovery.GENERAL_ORCH_ID, generalPresence);
            _log.Log_Info(ChannelDiscovery.GENERAL_ORCH_ID, $"Owner presence → {generalPresence}");
            Tell_Supervisor_AboutPresence(ChannelDiscovery.GENERAL_ORCH_ID, generalPresence);

            await Send_DirectReply_BestEffort_Async(client, messageThreadId, Describe_Presence(generalPresence), cancellationToken);
            return;
        }

        var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null)
        {
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, "/pc works in an orchestration's topic or in General — it says which terminal you are sitting at.", cancellationToken);
            return;
        }

        try
        {
            var newPresence = OwnerPresence_Policy.Toggle(session.OwnerPresence);

            _store.Set_OwnerPresence(session.OrchId, newPresence);
            Sync_MeetingFlag_AndReport(session.OrchId, newPresence);
            Apply_Presence_ToAwaitingAnswerFlag(session.OrchId, newPresence);
            _log.Log_Info(session.OrchId, $"Owner presence → {newPresence}");
            Raise_OrchestrationActivity(session.OrchId);

            Tell_Supervisor_AboutPresence(session.OrchId, newPresence);

            // Sent BEFORE the new presence takes hold on the next tick, so the confirmation itself
            // is not the first thing terminal mode drops.
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, Describe_Presence(newPresence), cancellationToken);

            await Forget_AppliedTopicName_Async(session.OrchId, cancellationToken);
            await Sync_TopicNames_BestEffort_Async(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Log_Error(session.OrchId, "'/pc' failed", ex);
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, $"could not change presence: {ex.Message}", cancellationToken);
        }
    }

    static string Describe_Presence(OwnerPresenceModes presence)
    {
        return presence == OwnerPresenceModes.Terminal
            ? "💻 TERMINAL — I will not text this topic, and its supervisor will not stop to wait for a tap. Talk to it in its terminal. It stays on until you send /pc again — texting here will not end it."
            : "📱 REMOTE — texting resumes, and questions here will wait for your answer again.";
    }

    /// <summary>
    /// The supervisor must be TOLD, not left to infer it: in terminal mode it should ask in the
    /// terminal and stop writing QUESTION:/OPTION: lines that will never be tapped.
    /// </summary>
    void Tell_Supervisor_AboutPresence(string orchId, OwnerPresenceModes presence)
    {
        var subject = presence == OwnerPresenceModes.Terminal
            ? "the owner is now AT YOUR TERMINAL — talk to them there"
            : "the owner is back on Telegram — questions are texted again";

        var text = presence == OwnerPresenceModes.Terminal
            ? "They are sitting in front of this session (💻). Ask them in the terminal, in plain prose: do NOT write "
                + "QUESTION:/OPTION: lines, because nothing is being texted and there are no buttons to tap.\n\n"
                + "You will also NOT be stopped after asking — the awaiting-answer block is off while they are here, "
                + "so keep working unless what you asked actually gates the next step. This stays on until they "
                + "send /pc again — their ordinary messages do NOT end it — and you get an entry here when it does."
            : "They are on their phone again (📱). Questions are texted, the awaiting-answer block is back on, and "
                + "the usual protocol applies: ask ONE question, with options, and stop.";

        var channelFile = orchId == ChannelDiscovery.GENERAL_ORCH_ID
            ? _paths.GeneralChannelFile
            : _paths.Get_OwnerChannelFile(orchId);

        Append_AppEntry_Safe(channelFile, Channels.AppEntryAudiences.Agent, subject, text, DateTime.Now);
        Raise_OrchestrationActivity(orchId);
    }

    /// <summary>
    /// `/pc` in one topic ends terminal mode in every OTHER topic — nobody sits at two terminals at
    /// once. Nothing else ends it: an ordinary message used to revoke it everywhere, which is what the
    /// owner reported on 2026-08-21 as the icon "going away by itself". The decision is
    /// <see cref="OwnerPresenceFlip_Planner"/>'s, including the reversal's reasoning; this does the moving.
    /// </summary>
    void Flip_OtherTerminals_IfPresenceCommand(long? messageThreadId, bool isPresenceCommandItself)
    {
        var textedOrchId = messageThreadId == null
            ? ChannelDiscovery.GENERAL_ORCH_ID
            : _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value)?.OrchId;

        // General carries its presence in the FLAG rather than a session, so it is gathered by hand
        // and moved by hand — it has no session.json for the store to update.
        List<OrchestrationPresence> presences =
        [
            new(ChannelDiscovery.GENERAL_ORCH_ID, Resolve_Presence(ChannelDiscovery.GENERAL_ORCH_ID)),
        ];

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc == null)
                presences.Add(new OrchestrationPresence(session.OrchId, session.OwnerPresence));
        }

        foreach (var orchId in OwnerPresenceFlip_Planner.Resolve_Flips(presences, textedOrchId, isPresenceCommandItself))
        {
            if (orchId != ChannelDiscovery.GENERAL_ORCH_ID)
                _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Remote);

            Sync_MeetingFlag_AndReport(orchId, OwnerPresenceModes.Remote);

            _log.Log_Info(
                orchId,
                orchId == textedOrchId
                    ? "Owner presence → Remote (they texted this topic)"
                    : "Owner presence → Remote (they texted Telegram, so they are not at this terminal either)");

            Tell_Supervisor_AboutPresence(orchId, OwnerPresenceModes.Remote);
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

        await Forget_AllAppliedTopicNames_Async(cancellationToken);
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
    /// <remarks>
    /// ASKED THROUGH THE CLASSIFIER RATHER THAN SPELT AGAIN. This used to hold its own copy of the
    /// TOPIC_NOT_MODIFIED test, which made it invisible to the suite and — worse — available to only
    /// whichever call site remembered it existed. The General-topic sync did not, and spun.
    /// Decision 12: one implementation, and it lives where a test can reach it.
    /// </remarks>
    static bool Is_TopicAlreadyNamed(Exception exception)
    {
        return TelegramAttempt_Gate.Classify_Failure(exception) == TopicNameAttemptOutcomes.Applied;
    }

    async Task Sync_TopicNames_BestEffort_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        await _topicNameSyncGate.WaitAsync(cancellationToken);

        try
        {
            await Sync_TopicNames_Inside_Gate_Async(cancellationToken);
        }
        finally
        {
            _topicNameSyncGate.Release();
        }
    }

    /// <summary>
    /// AN EXPLICIT OWNER ACTION OUTRANKS THE MEMO. The applied-name map exists to skip pointless API
    /// calls, and it is right about that almost always — but when it is wrong it is wrong in the one
    /// direction that cannot self-correct, because being wrong means SKIPPING. It records a name as
    /// applied on a genuine Telegram refusal too, which the code above says plainly is "a dictionary
    /// saying applied about a name that is not".
    ///
    /// So the commands that change what the name should SAY — mode, presence, /done, /test — drop
    /// their entry first and push unconditionally. It costs one edit on an action the owner takes by
    /// hand, and it means the thing they just asked for is never suppressed by a stale belief. The
    /// periodic tick keeps the memo, which is where the saving actually lives.
    /// </summary>
    /// <remarks>
    /// AWAITED, NEVER Wait()ed. The gate is deliberately held across Telegram calls, so a blocking
    /// acquire here would park a thread-pool thread for as long as an HTTP timeout — on the inbound
    /// command loop, which is the owner's own keystrokes.
    /// </remarks>
    async Task Forget_AppliedTopicName_Async(string orchId, CancellationToken cancellationToken)
    {
        await _topicNameSyncGate.WaitAsync(cancellationToken);

        try
        {
            _appliedTopicNames.Remove(orchId);

            // A pending backoff is also a reason to skip, and an owner action should not wait it out.
            _topicNameRetryAfterUtc.Remove(orchId);
        }
        finally
        {
            _topicNameSyncGate.Release();
        }
    }

    /// <summary>Same, for the app-wide commands: every open topic's name changes at once.</summary>
    async Task Forget_AllAppliedTopicNames_Async(CancellationToken cancellationToken)
    {
        await _topicNameSyncGate.WaitAsync(cancellationToken);

        try
        {
            _appliedTopicNames.Clear();
            _topicNameRetryAfterUtc.Clear();
        }
        finally
        {
            _topicNameSyncGate.Release();
        }
    }

    /// <summary>
    /// The ONE place a topic's name is composed. Extracted so a command that changes what the name
    /// should SAY can also TELL the owner what it will say, without a second copy of the expression
    /// drifting away from this one — the failure decision 12 in CLAUDE.md is about.
    /// </summary>
    string Build_WantedTopicName(Sessions.OrchestrationSession.IOrchestrationSession session)
    {
        var baseName = TelegramDeliveryMode_Glyphs.Strip_Glyph(session.DisplayName ?? session.OrchId);

        // FOUR INPUTS LEFT AND TWO ARRIVED, on 2026-09-10. Mode, away, quiet and presence moved to
        // PULSE's header, so nothing about DELIVERY renames a topic any more — away and quiet were
        // the expensive pair, both app-wide, so one toggle used to rename every open topic at once
        // and every rename writes a service message into the thread it renames. Paused-for-a-limit
        // and closed arrived, because both are facts about the WORK, which is what this surface is
        // for.
        //
        // They travel as a named record rather than as seven positional arguments, four of them
        // bool: any two of those could be swapped with everything still compiling, and this caller
        // is `internal sealed`, so the suite could not see the swap either.
        return TelegramDeliveryMode_Glyphs.Compose_TopicName(
            baseName,
            new TelegramDeliveryMode_Glyphs.TopicNameFlags(
                OwnerReply: Last_OwnerReplyState(session.OrchId),
                IsPausedForUsageLimit: Is_SupervisorPausedForUsageLimit(session),
                IsClosed: session.ClosedUtc != null,
                IsAwaitingTest: session.AwaitingTest,
                IsDone: session.Done));
    }

    async Task Sync_TopicNames_Inside_Gate_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        foreach (var session in _store.Load_All())
        {
            if (session.TelegramTopicId == null)
                continue;

            // A CLOSED ORCHESTRATION IS SKIPPED ONLY ONCE ITS TOPIC IS GONE, which is what makes 🏁
            // reachable at all. This loop refused every closed session outright, so the CLOSED glyph
            // the owner asked for on 2026-09-10 could never be composed into a name — a glyph built,
            // documented, unit-tested and never rendered, which is precisely the `Build_ForGeneral`
            // defect this same branch went and fixed. A review caught it before it shipped.
            //
            // THE PREDICATE IS THE DELETE MARKER, not the close. Closing normally DELETES the topic,
            // and renaming a topic that no longer exists is churn plus a failure per tick — so the
            // window this glyph is for is the one where the delete did not happen: Telegram refuses
            // some (past 48 hours, or without `can_delete_messages`) and stage 9a's own path can run
            // out of attempts. In that window the endeavour is over and its topic still wears a
            // working name, which the owner cannot tell from a live one. `TelegramTopicDeletedUtc` is
            // the fact that says which case this is.
            if (session.ClosedUtc != null && session.TelegramTopicDeletedUtc != null)
                continue;

            var wantedName = Build_WantedTopicName(session);

            // THE BELIEF EXPIRES. Without this the guard below is unfalsifiable: it compares what we
            // want against what we THINK we sent, so a divergence from what Telegram actually holds
            // can never be noticed, let alone corrected. Dropping the memo on a timer turns the skip
            // into a cache rather than a verdict — the re-push is a no-op the API answers
            // TOPIC_NOT_MODIFIED to, which the gate already reads as Applied.
            var revalidated = _topicNameRevalidatedUtc.TryGetValue(session.OrchId, out var lastCheck) ? lastCheck : DateTime.MinValue;

            if ((DateTime.UtcNow - revalidated).TotalMinutes >= TOPIC_NAME_REVALIDATE_MINUTES)
            {
                _topicNameRevalidatedUtc[session.OrchId] = DateTime.UtcNow;
                _appliedTopicNames.Remove(session.OrchId);
            }

            if (_appliedTopicNames.TryGetValue(session.OrchId, out var applied) && applied == wantedName)
                continue;

            // AND THE SECOND QUESTION, WHICH USED TO BE THE SAME ONE. An attempt whose outcome we could
            // not learn holds this orchestration back for a WHILE — not for ever, which is what
            // recording it as applied did, and not for two seconds, which is what recording nothing did.
            var retryAfter = _topicNameRetryAfterUtc.TryGetValue(session.OrchId, out var stamp) ? stamp : (DateTime?)null;

            if (!TelegramAttempt_Gate.Is_AttemptDue(retryAfter, DateTime.UtcNow))
                continue;

            try
            {
                await _telegramClient.Edit_ForumTopic_Async(session.TelegramTopicId.Value, wantedName, cancellationToken);
                _appliedTopicNames[session.OrchId] = wantedName;
                _topicNameRetryAfterUtc.Remove(session.OrchId);
            }
            // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
            // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
            // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
            // Cost HERE: every REMAINING session's topic name, on a best-effort path.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (Telegram.TelegramError_Table.Classify(400, ex.Message) == Telegram.TelegramErrorCases.TopicGone)
            {
                // THE TOPIC IS NOT THERE — TOPIC_ID_INVALID. Terminal for this id: there is nothing to
                // rename, and no number of retries will make one. So the stored id is FORGOTTEN and
                // the session is marked as having had its topic deleted, which is the fact that was
                // missing.
                //
                // MEASURED IN PRODUCTION, 2026-09-10 20:56, six of these at start for four CLOSED
                // orchestrations — and it is a regression I introduced. Stage 8d changed this loop's
                // skip from "closed" to "closed AND the topic is recorded deleted", so that 🏁 could
                // ever be drawn. The four topics HAD been deleted, before E1 existed to write the
                // record — so they carried no TelegramTopicDeletedUtc, stopped being skipped, and were
                // renamed into nothing. Writing the marker here is what closes that gap for every
                // session that predates E1: the first attempt learns the truth and the loop skips it
                // for good.
                //
                // AHEAD OF THE NOT-MODIFIED CATCH because both read a 400 and this one is terminal;
                // behind it, a gone topic would be cached as correctly named and retried at every
                // revalidation for as long as the app ran.
                _log.Log_Info(session.OrchId, $"Topic {session.TelegramTopicId} is gone (Telegram: {ex.Message}) — forgetting its id; it will not be renamed again.");

                _store.Mark_TopicDeleted(session.OrchId);
                _appliedTopicNames.Remove(session.OrchId);
                _topicNameRetryAfterUtc.Remove(session.OrchId);
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
                //
                // BUT A TIMEOUT IS NOT A REAL FAILURE — IT IS "WE DO NOT KNOW", the same principle
                // applied at Narrate_BusySupervisor_Async and not carried here when the filter above
                // was added. The reasoning for this write is sound for the exceptions that used to
                // arrive; the filter changed WHICH ones arrive, and that was not revisited.
                //
                // Writing the cache on a timeout records a name that may never have been applied as
                // applied, and `_appliedTopicNames` has no Remove and no Clear anywhere in this file —
                // the entry then survives for the life of the process. The owner toggles a mode, the
                // edit times out, and the topic keeps showing the OLD glyph until the mode changes
                // again or the app restarts, while the log says "sync failed" in the same breath as
                // the code records success. Decision 11 makes that glyph the owner-visible truth of a
                // passing state, so the stale name is not cosmetic.
                //
                // THE THREE BUCKETS, decided in TelegramAttempt_Gate where the suite can reach them. An
                // earlier version of this used `ex is not OperationCanceledException`, which is the
                // two-bucket test rev-6 proved insufficient for the identical decision one method away —
                // same class, two predicates, one commit. The predicate is now one predicate, and it
                // lives somewhere it can be tested.
                //
                // BACKOFF REUSES MirrorRetryBackoffSeconds (30 s in production) rather than inventing a value: this
                // file already has one retry window with that meaning, applied through
                // Is_MirrorAttemptDue, and a second magic number would be worse than the one being
                // explained. Thirty seconds takes a failing sync from ~30 attempts a minute to 2, and
                // bounds an owner-visible glyph delay at 30 s. Do not "fix" it into a bespoke constant.
                // THE REFUSAL BRANCH NOW ONLY EVER SEES A GENUINE REFUSAL, which is what makes writing
                // the applied name here defensible at all. rev-10's F1 was that this branch uses the
                // applied-name dictionary as a retry suppressor — "this name is applied" written for a
                // name Telegram just refused, the same conflation the unknown branch was split to end.
                // Its owner-visible half is closed by the classification: a 429 and every 5xx are
                // OutcomeUnknown now, so they are stamped and retried rather than recorded as applied
                // for the life of the process.
                //
                // THE RESIDUAL, STATED RATHER THAN CLAIMED AWAY: for a real refusal this write is still
                // a dictionary saying "applied" about a name that is not. The BEHAVIOUR is right — an
                // invalid name will not become valid, so it must not be retried until the wanted name
                // changes, and the guard above does exactly that — but the map is not honest about what
                // it holds. Closing that wants a third memo keyed on the refused name, which is not
                // taken here because nothing observable depends on it.
                if (TelegramAttempt_Gate.Classify_Failure(ex) == TopicNameAttemptOutcomes.OutcomeUnknown)
                    _topicNameRetryAfterUtc[session.OrchId] = TelegramAttempt_Gate.Build_RetryAfterUtc(DateTime.UtcNow, _timing.MirrorRetryBackoffSeconds);
                else
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

        // THE OVERRIDE THE HELP TEXT PROMISES. Appending fresh traffic below wakes a session that was
        // idle for the ordinary reason; it does nothing for one the dispatcher is refusing to run
        // before an appointment (IPrintTurnDispatcher.Clear_LimitDeferrals's own doc explains why that
        // appointment can also just be wrong). This must run before or after the appends indifferently —
        // it only ever touches RetryNotBeforeUtc, never a channel.
        //
        // GUARDED, BECAUSE IT RUNS FIRST (F7, 2026-09-09). Everything the owner asked for is below this
        // line: an exception escaping here aborted /resume before a single channel was appended, was
        // logged as a Telegram backoff, and had the update redelivered and retried for ever — the one
        // command that exists for "nothing else will speak to these sessions again" being the one a
        // single unreadable state file could cancel. Clear_LimitDeferrals contains its own per-session
        // failures; this covers the rest of it (the registration scan included), so the wake still
        // happens and the log says the override did not.
        var clearedAppointments = 0;

        try
        {
            clearedAppointments = _printTurns.Clear_LimitDeferrals();
        }
        catch (Exception ex)
        {
            _log.Log_Error(GLOBAL_ORCH_ID, "/resume could not clear the usage-limit appointments — the wake below still ran, so a session that was merely idle is moving; one that is waiting on a limit is NOT, and needs /resume again", ex);
        }

        var wokenSessions = 0;
        var wokenOrchestrations = 0;
        List<string> notWoken = [];

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            wokenOrchestrations++;

            // Every counter increments only on a written entry. The total is reported to the owner
            // below as "go ahead sent to N sessions", and /resume is the one command with no retry —
            // it exists for the usage-limit reset, where nothing else will speak to a session again.
            // Counting an append that did not happen tells the owner a session was woken and leaves
            // it asleep, which is the exact failure /resume is the remedy for.
            if (ChannelAppender.Append_AppEntry(_paths.Get_OwnerChannelFile(session.OrchId), AppEntryAudiences.Agent, SUBJECT, body, DateTime.Now))
                wokenSessions++;
            else
                notWoken.Add($"{session.OrchId}/supervisor");

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                if (ChannelAppender.Append_AppEntry(
                        Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId), AppEntryAudiences.Agent, SUBJECT, body, DateTime.Now))
                    wokenSessions++;
                else
                    notWoken.Add($"{session.OrchId}/{member.MemberId}");
            }

            Raise_OrchestrationActivity(session.OrchId);
        }

        // The general supervisor too — it has the same problem and its own channel.
        if (ChannelAppender.Append_AppEntry(_paths.GeneralChannelFile, AppEntryAudiences.Agent, SUBJECT, body, DateTime.Now))
            wokenSessions++;
        else
            notWoken.Add("general");

        _log.Log_Info(GLOBAL_ORCH_ID, $"/resume — woke {wokenSessions} session(s) across {wokenOrchestrations} orchestration(s)");

        // Named, not counted: "3 of 5" leaves the owner to work out which two are still asleep, and
        // /resume is exactly when they cannot afford to guess.
        if (notWoken.Count > 0)
            _log.Log_Warning(GLOBAL_ORCH_ID, $"/resume could NOT wake (channel locked): {string.Join(", ", notWoken)}");

        // THE APPOINTMENTS ARE REPORTED, NOT JUST THE WAKES (F7, 2026-09-09). Dropping a usage-limit
        // appointment is the thing /resume is FOR, and the reply used to count only channel appends —
        // so the owner sending it at the reset read the same sentence whether it had freed five parked
        // sessions or none. Said only when there were some: "cleared 0" on every /resume is noise, and
        // decision 15's test is whether the line is one the owner can act on.
        var clearedNote = clearedAppointments == 0
            ? string.Empty
            : $" — cleared {clearedAppointments} usage-limit appointment{(clearedAppointments == 1 ? "" : "s")}";

        await Send_DirectReply_BestEffort_Async(
            client,
            messageThreadId,
            $"▶ go ahead sent to {wokenSessions} session{(wokenSessions == 1 ? "" : "s")} across {wokenOrchestrations} orchestration{(wokenOrchestrations == 1 ? "" : "s")} (+ general){clearedNote}",
            cancellationToken);
    }

    async Task Send_MemberStatusReport_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var text = Build_MemberStatusText(messageThreadId);

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
    /// silently for as long as it is the last thing in the topic.
    ///
    /// Four properties matter and each has a test:
    ///   - a change edits;
    ///   - an IDENTICAL line does nothing, because an edit that writes the same text is a wasted API
    ///     call and, against the 429 limit we already have open on the ledger, a real cost;
    ///   - a RESTART edits the existing message rather than posting a second one — the id is read
    ///     from session.json, not from memory;
    ///   - a line BURIED by later traffic, in a topic that has since been quiet for
    ///     TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS, is
    ///     deleted and written again at the bottom. Telegram cannot move a message, so this is the
    ///     only way to put the current state where the owner is looking when they enter the chat.
    ///
    /// The repost is the ONE action here that notifies, and everything about it is arranged so that
    /// it cannot become a waterfall: the quiet window bounds it to one ping per quiet period, the
    /// delivery gate blocks it in a silenced topic exactly as it blocks a first post, and it never
    /// fires while the line is already last.
    /// </summary>
    /// <summary>
    /// An entry's own header stamp, or null when it cannot be trusted. Agent-written (decision 12),
    /// so a stamp in the FUTURE reads as absent rather than as a confident wrong clock — the same
    /// refusal <c>Describe_SinceStamp_OrNull</c> makes for a duration.
    /// </summary>
    static DateTime? Read_TrustedStamp_OrNull(string? stampText)
    {
        if (string.IsNullOrWhiteSpace(stampText))
            return null;

        return SessionDuration_Formatter.Try_ReadTrustedStamp(stampText, DateTime.Now, out var stamp) ? stamp : null;
    }

    /// <summary>
    /// WHEN THE SESSION THAT TALKS TO THE OWNER RESUMES, if it is waiting out a usage limit. The
    /// dispatcher records the instant only for a limit that NAMED its reset — see
    /// <c>IPrintSessionState.RetryNotBeforeUtc</c> — which is precisely the case where the app can
    /// say something certain instead of "idle — waiting", the line the owner read for two hours on
    /// 2026-09-09 while a turn had an appointment.
    /// </summary>
    DateTime? Read_UsageLimitResumeAt_OrNull(IOrchestrationSession session)
    {
        var resumeAt = Read_ResumeAt_OrNull(Running.SessionRoles.Supervisor, session.OrchId, Running.SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

        if (resumeAt != null)
            return resumeAt;

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null || Sessions.MemberKind_Ids.Resolve_Kind(member.MemberId) != Sessions.MemberKinds.Solo)
                continue;

            var soloResumeAt = Read_ResumeAt_OrNull(Running.SessionRoles.Solo, session.OrchId, member.MemberId);

            if (soloResumeAt != null)
                return soloResumeAt;
        }

        return null;
    }

    DateTime? Read_ResumeAt_OrNull(Running.SessionRoles role, string orchId, string memberId)
    {
        try
        {
            var stateFile = Running.PrintSessionState.PrintSessionState_Store.Get_StateFile(_paths, role, orchId, memberId);
            var state = Running.PrintSessionState.PrintSessionState_Store.Read_OrNull(stateFile);

            return state?.RetryNotBeforeUtc != null && Limits.DispatchPause_Gate.Is_Paused(state.RetryNotBeforeUtc, _clock.UtcNow)
                ? state.RetryNotBeforeUtc.Value.ToLocalTime()
                : null;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(orchId, $"Could not read '{memberId}' state for its usage-limit appointment ({ex.Message}) — the status line omits it");
            return null;
        }
    }

    /// <summary>
    /// WHAT ONLY THE ENGINE CAN KNOW ABOUT A TOPIC'S STATUS LINE, gathered in one place: the state
    /// the supervisor DECLARED, what the owner is being waited on for, a usage-limit pause, and when
    /// the last thing happened.
    ///
    /// <para>
    /// PULSE's builder is handed per-member channels and a ledger. It cannot read owner-channel.md,
    /// the open-question registry or a member's state file — which is exactly where the four fields
    /// the owner asked for live. Handing them in as DATA is also what keeps the builder pure enough
    /// to test: every one of these is a line the old status message got WRONG by inferring it.
    /// </para>
    /// <para>
    /// EVERY FIELD DEGRADES TO NOTHING. A missing STATE: line is a blank row, not a guessed state; an
    /// unreadable channel loses the row rather than the tick. That is the direction chosen after
    /// 2026-09-09, where the owner read "waiting on you" about a supervisor that was not waiting and
    /// "idle — waiting" about one that was paused.
    /// </para>
    /// </summary>
    Telegram.TopicStatusFields Build_TopicStatusFields(IOrchestrationSession session)
    {
        try
        {
            var entries = ChannelHistory_Cache.Read_Entries(_paths.Get_OwnerChannelFile(session.OrchId));

            // The session that TALKS TO THE OWNER, whichever this orchestration has: a basic
            // orchestration's solo declares its state exactly as a supervisor does.
            var lastSpoken = MemberState_Resolver.Find_LastEntryBy_OrNull(entries, ChannelAuthors.Supervisor)
                ?? MemberState_Resolver.Find_LastEntryBy_OrNull(entries, ChannelAuthors.Solo);

            var declared = DeclaredState_Parser.Find_OrNull(lastSpoken?.Body);

            List<Telegram.TopicOwnerAsk> asks = [];

            lock (_ownerStateLock)
            {
                foreach (var question in _openQuestions.Values)
                {
                    if (question.OrchId == session.OrchId)
                        // THE SOURCE IS A DOOR TO KNOCK ON, not a category. TopicOwnerAsk's own
                        // summary says it: "'your browser pass' is a job, 'sup: your browser pass'
                        // is a job with a door to knock on" — and "question: …" is the category.
                        asks.Add(new Telegram.TopicOwnerAsk(Describe_Speaker(session.OrchId), question.Text, question.AskedUtc.ToLocalTime()));
                }
            }

            return new Telegram.TopicStatusFields
            {
                SupervisorDeclaredState = declared,

                // THE STAMP IS THE ENTRY'S OWN, and it is read through the trusted reader rather than
                // parsed here: a header stamp is agent-written (decision 12), and a future one must
                // produce nothing instead of a confident wrong clock.
                SupervisorDeclaredAt = declared == null ? null : Read_TrustedStamp_OrNull(lastSpoken?.DateText),
                UsageLimitResumeAt = Read_UsageLimitResumeAt_OrNull(session),
                OwnerAsks = asks,

                // THE THREE THAT MOVED OFF THE TOPIC NAME on 2026-09-10. `Mode` is deliberately NOT
                // among them: the planner already takes it for its own delivery gate, and asking the
                // engine to pass the same value twice is how two surfaces come to disagree about
                // whether a topic is muted.
                //
                // PRESENCE IS PASSED EVEN THOUGH THE MODE ALREADY FOLDED IT IN, and that is not the
                // same mistake. `Resolve_EffectiveMode` maps Terminal presence to Silenced, so by the
                // time the planner sees a mode, 💻 and a hand-typed /mute are indistinguishable — and
                // the header has to tell them apart, because one says "you are sitting in front of
                // this" and the other says "you asked me to stop". The cost is that the two readings
                // are taken from different snapshots and could disagree for one tick; the worst that
                // renders is 💻 beside a mode glyph that has just changed.
                IsAway = Is_AwayMode(),
                IsQuiet = Is_Quiet(session.OrchId),
                Presence = session.OwnerPresence,
            };
        }
        catch (Exception ex)
        {
            // ONE TOPIC'S EXTRA FIELDS ARE NEVER WORTH THE TICK. Everything here is a read of a file
            // an agent writes; the line still renders from what the builder can see for itself.
            _log.Log_Warning(session.OrchId, $"Could not gather the status-line fields ({ex.Message}) — the line is drawn without them");

            return default;
        }
    }

    async Task Refresh_TopicStatusLines_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        foreach (var session in Sessions_ThisTick())
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
            // Read ONCE and shared with the unchanged-for tracker below: two reads of the same file
            // in one tick can disagree, and the tracker deciding "changed" against figures the line
            // never showed would restart the clock on a line that did not move.
            var ledger = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(session.OrchId)));

            var members = Build_TopicStatusMembers(session);

            // Filled HERE because these entries are already read for the status line — the topic-name
            // sync then spends nothing to draw the glyph.
            lock (_ownerStateLock)
                _ownerReplyStateByOrchId[session.OrchId] = Resolve_OwnerReplyState(session, members);

            // Rides the SAME ledger read the status line just did — a movement notice that cost its
            // own parse would be a third reading of one file per tick.
            await Tell_LedgerMovement_Async(session, ledger, cancellationToken);

            // NO TITLE ANY MORE. The line used to open with the orchestration's name and the owner
            // had it removed on 2026-08-24: "the name of the topic is not needed, I already know
            // where I am, and if I don't I have it at the top of the screen." It opens with PULSE
            // instead, which is the part that was actually missing — telling this constantly-edited
            // line apart from the half-hourly STATUS digest.
            // THE BAR IS BUILT BEFORE THE DECISION, because the decision has to see it (brief D).
            // The hold toggle's label carries the held count, so a hold that catches three
            // messages changes the bar and not one character of the text.
            IReadOnlyList<IReadOnlyList<(string Data, string Label)>> commandButtonRows =
                session.TelegramTopicId == null ? [] : Build_CommandButtonRows(session.TelegramTopicId.Value);

            var plan = Telegram.TopicStatusLine_Planner.Plan(
                ledger,
                members,
                DateTime.Now,
                session.StatusLineMessageId,
                lastText,
                Resolve_EffectiveMode(session.OrchId),
                _statusLineFailedAtByOrchId.ContainsKey(session.OrchId) ? lastFailedAttemptAt : null,
                _timing.MirrorRetryBackoffSeconds,
                Find_NewestTopicMessage_OrNull(session.TelegramTopicId),
                _repostImpossibleOrchIds.Contains(session.OrchId),
                Note_FiguresAndDescribe_UnchangedFor(session.OrchId, ledger),
                // The supervisor has no member row on this line, so its context rides on the title.
                // A basic orchestration has no supervisor file and this reads null, which is right:
                // its solo carries the figure on its own row.
                UsageTotals_Reader.Read_ContextUsage_OrNull(
                    Path.Combine(_paths.Get_OrchestrationFolder(session.OrchId), UsageTotals_Reader.SESSION_USAGE_FILE)),
                Build_TopicStatusFields(session));

            var action = plan.Action;
            var text = plan.Text;

            // WHAT IS REMEMBERED IS THE WHOLE RENDERING, text and button labels, and the planner is
            // told "unchanged" only when the whole rendering is unchanged. It still decides on, and
            // sends, the text alone. Without this a button-only change is swallowed: the count the
            // owner is meant to read never leaves this process, and a bar that ends up wrong for
            // any reason is never repainted, because a quiet orchestration's text does not move.
            var renderKey = Telegram.TopicStatusLine_RenderKey.Build(text, commandButtonRows);

            // UNDER THE SAME BACK-OFF AS THE PLANNER'S OWN DECISION. This promotion used to consult
            // only the rendering, and a FAILED edit leaves `lastText` at the last text that was sent
            // (the catch below keeps it stale on purpose) — so after one 429 the text "differed" on
            // every tick and the planner's None was overruled every 2 s for as long as Telegram kept
            // refusing. Measured 2026-09-10, 20:56–21:52 on the VPS: 357 of 382 rate-limit refusals
            // were this line, retried at the tick rate with `retry_after` counting down 34, 31, 29 …
            // The 30-second back-off existed, was tested, and never reached this branch.
            var attemptDue = Telegram.TopicStatusLine_Planner.Is_AttemptDue(
                _statusLineFailedAtByOrchId.ContainsKey(session.OrchId) ? lastFailedAttemptAt : null,
                DateTime.Now,
                _timing.MirrorRetryBackoffSeconds);

            if (action == Telegram.TopicStatusActions.None
                && attemptDue
                && session.StatusLineMessageId != null
                && lastText != null
                && renderKey != lastText)
            {
                action = Telegram.TopicStatusActions.Edit;
            }

            if (action == Telegram.TopicStatusActions.None)
                continue;

            // WHETHER THE OLD MESSAGE IS ALREADY GONE. Once the delete has succeeded the stored id
            // names nothing, so a later failure must forget it — otherwise a quiet orchestration,
            // whose text never changes, never attempts the edit that would discover the dead id, and
            // loses its status line for good.
            //
            // WHICH HANDLERS HONOUR IT, precisely — the earlier wording claimed "every failure path
            // below", and two of the four do not. `Is_MessageGone` forgets the id anyway, so it needs
            // nothing; the not-modified catch and the generic catch each check it below; and the
            // cancellation rethrow deliberately does not, because the app is stopping and the id in
            // session.json is discovered dead by the first edit after the restart.
            var oldStatusMessageDeleted = false;

            try
            {
                // The id re-checked rather than asserted through .Value: the decider guarantees it,
                // but a guarantee that lives in another file is not one the compiler can see.
                if (action == Telegram.TopicStatusActions.Edit && session.StatusLineMessageId != null)
                {
                    // THE ROW-AWARE EDIT, NOT THE PLAIN ONE. The plain edit sends no reply_markup and
                    // Telegram reads that as "remove the keyboard" — so editing this message the old
                    // way would strip the command bar off it two seconds after it was posted.
                    await _telegramClient.Edit_MessageTextWithButtonRows_Async(session.StatusLineMessageId.Value, text, commandButtonRows, cancellationToken);
                }
                else
                {
                    // DELETE FIRST, AND ONLY THEN SEND. Telegram cannot move a message, so a repost is
                    // a delete plus a post — and the order is the whole invariant: exactly ONE status
                    // message per topic, always. Posting first and deleting after leaves two of them
                    // up for as long as the second call takes, and leaves two of them up FOREVER if it
                    // fails, which is the precise defect this feature was built to prevent.
                    //
                    // A FAILED DELETE THEREFORE MUST NOT POST, and none of the three ways it can fail
                    // does: a REFUSAL latches this topic and returns, just below; a message already
                    // GONE reaches Is_MessageGone, which forgets the id so the next tick posts fresh;
                    // anything else reaches the generic catch, which keeps the message and its id
                    // untouched and retries behind the backoff.
                    if (action == Telegram.TopicStatusActions.Repost && session.StatusLineMessageId != null)
                    {
                        // THE REFUSAL IS CAUGHT AROUND THE DELETE ITSELF, not around the whole
                        // attempt. Guarding the outer catch on `action == Repost` instead read as
                        // "this action does a delete, so a refusal wording must have come from it" —
                        // and `not enough rights` is wording Telegram also emits on the SEND. A repost
                        // whose delete SUCCEEDED and whose send then threw it would latch the topic
                        // while the stored id pointed at a message that had just been deleted: the
                        // exact hazard the null-return branch below already guards, entered by the
                        // door beside it. Which CALL threw is a fact; which action was attempted is an
                        // inference, and the inference was wrong.
                        try
                        {
                            await _telegramClient.Delete_Message_Async(session.StatusLineMessageId.Value, cancellationToken);
                        }
                        catch (Exception exception) when (Telegram.TopicStatusLine_Decider.Is_DeleteRefused(exception.Message))
                        {
                            // REFUSED, not failed: this message can never be deleted, so it can never
                            // be moved. Retrying is the loop rev-1 found, and the loop starves the
                            // EDIT with it because the repost overrides the decider. Latch it and the
                            // topic goes back to editing in place — master's behaviour.
                            //
                            // The message is STILL UP and its id is still good, so nothing is
                            // forgotten here, and no backoff is stamped: there is nothing to retry,
                            // and stamping one would delay the very edit this falls back to.
                            _repostImpossibleOrchIds.Add(session.OrchId);
                            _log.Log_Warning(session.OrchId, $"Topic status line cannot be moved — it will be edited in place from now on ({exception.Message})");
                            continue;
                        }

                        // Everything else the delete can throw — transient, or a message already gone
                        // — deliberately propagates to the outer catches, which know how to clear an
                        // id and how to back off.
                        oldStatusMessageDeleted = true;
                    }

                    var messageId = await _telegramClient.Send_MessageWithButtonRows_Async(
                        session.TelegramTopicId, text, commandButtonRows, TelegramSendSounds.Silent, cancellationToken);

                    if (messageId == null)
                    {
                        // The old message is already deleted by now, so keeping its id would leave the
                        // topic pointing at nothing. Forget the id AND the remembered text (which
                        // would otherwise silence the fresh post as identical) and let the next tick
                        // start over. On a first POST there is nothing to undo, which is why this is
                        // not unconditional.
                        if (oldStatusMessageDeleted)
                            Forget_StatusLineMessage(session.OrchId);

                        continue;
                    }

                    _store.Set_StatusLineMessageId(session.OrchId, messageId.Value);
                }

                _statusLineTextByOrchId[session.OrchId] = renderKey;
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
                //
                // UNLESS THE OLD MESSAGE IS ALREADY DELETED, which is the worst combination in this
                // method and the reason the check is here rather than argued away: advancing the cache
                // while the id is dead makes Decide answer None on every later tick, so no edit is
                // ever attempted, the dead id is never discovered, and the topic holds ZERO status
                // lines permanently — the precise failure the flag exists to close, through the one
                // door that did not check it.
                //
                // UNREACHABLE TODAY: "message is not modified" is an editMessageText error and this
                // branch is only reached after a send. It is written anyway because the guarantee then
                // rests on the code rather than on Telegram's choice of wording, which nothing here
                // controls and no test can see.
                if (oldStatusMessageDeleted)
                    Forget_StatusLineMessage(session.OrchId);
                else
                    _statusLineTextByOrchId[session.OrchId] = renderKey;
            }
            catch (Exception exception) when (Telegram.TopicStatusLine_Decider.Is_MessageGone(exception.Message))
            {
                // TERMINAL for this message id: the message it names no longer exists, which is what
                // /clear leaves behind — the topic is torn down and recreated while the id survives in
                // session.json. Retrying could never succeed, so the id is FORGOTTEN and the next tick
                // posts a fresh line. Without this the orchestration never gets a status line again
                // for the life of the machine.
                Forget_StatusLineMessage(session.OrchId);

                // The latch belonged to the message that has just stopped existing: a 48-hour window
                // dies with it, so the fresh line posted next tick deserves its one attempt.
                _repostImpossibleOrchIds.Remove(session.OrchId);

                _log.Log_Warning(session.OrchId, $"Topic status message is gone — posting a new one next tick ({exception.Message})");
            }
            catch (Exception exception)
            {
                // Never fatal: a status line that cannot be drawn must not stop the mirror. The
                // remembered text is deliberately NOT updated, so the next tick retries — but BACKED
                // OFF, because a 429 answered at the tick rate inverts the cadence from once a minute
                // to thirty times a minute per topic and sustains the throttling that caused it.
                //
                // UNLESS THE OLD MESSAGE IS ALREADY GONE. A repost deletes before it sends, so a send
                // that throws here leaves the stored id naming a deleted message — and retrying an
                // EDIT against it is not the recovery it looks like: a quiet orchestration's text
                // never changes, so the edit is never attempted and the id is never discovered dead.
                // Forgetting it costs one extra post; keeping it costs the status line permanently.
                if (oldStatusMessageDeleted)
                    Forget_StatusLineMessage(session.OrchId);

                _statusLineFailedAtByOrchId[session.OrchId] = DateTime.Now;
                _log.Log_Warning(session.OrchId, $"Topic status line could not be updated — {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Drops everything remembered about a topic's status message, for when the message it refers to
    /// no longer exists.
    ///
    /// BOTH HALVES, ALWAYS, which is why this is one method and not two lines repeated three times:
    /// clearing the id without the remembered text leaves the next tick comparing the same text
    /// against itself, answering None, and never posting the replacement — the id is forgotten and
    /// the line never comes back, which is the failure this is supposed to prevent.
    /// </summary>
    void Forget_StatusLineMessage(string orchId)
    {
        _store.Clear_StatusLineMessageId(orchId);
        _statusLineTextByOrchId.Remove(orchId);
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
        foreach (var session in Sessions_ThisTick())
        {
            if (session.ClosedUtc != null)
                continue;

            var markerFile = Path.Combine(_paths.Get_OrchestrationFolder(session.OrchId), Status.GuardNotInForce_Marker.FILE_NAME);

            if (!File.Exists(markerFile))
                continue;

            var markerText = Read_FileText_Safe(markerFile);

            var description = Status.GuardNotInForce_Marker.Describe_OrNull(markerText);

            if (description == null)
            {
                File.Delete(markerFile);
                continue;
            }

            // The same inability, again, is not a second fact. hook-log.sh overwrites one marker
            // rather than appending and says the judgement about repetition belongs here — it did not
            // exist, so three identical alerts landed in twelve minutes on 2026-08-13. The marker is
            // still DELETED when suppressed: the fact is recorded, and leaving the file would only
            // re-ask the same question every two seconds.
            _reportedGuardsByOrchId.TryGetValue(session.OrchId, out var lastReport);

            if (!Status.GuardReport_Decider.Should_Report(markerText, lastReport?.MarkerText, lastReport?.ReportedAt, DateTime.Now))
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

                // THE BODY IS THE MARKER'S OWN SENTENCE AND NOTHING ELSE. It used to append "this is
                // almost always the machine rather than the code — a machine that cannot fork cannot
                // run them", which is a CAUSE nobody established: the marker says which predicate
                // could not be evaluated and why, and the app has no way to know whether that was the
                // machine or the guard. Pinned by GuardReportProbeTests, which asserts the body
                // equals Describe_OrNull and names both invented claims.
                var reported = Append_SupervisorAttention_UnlessMeeting(
                    session.OrchId,
                    Status.GuardNotInForce_Marker.ENTRY_SUBJECT,
                    description,
                    Resolve_Presence(session.OrchId));

                // The marker goes ONLY if the report went, and it is DEFERRED rather than dropped
                // for either reason the report can be withheld: the owner is at the terminal, or the
                // channel stayed locked. The catch below states this contract and used to enforce it
                // for free, because a failed append THREW; once the appender started returning false
                // instead, nothing threw, the delete ran anyway, and a report that was never written
                // destroyed the record that would have retried it. Deleting it during a meeting
                // would be the same loss by the other route — a standing warning that the guard is
                // not running, dropped instead of held, with the supervisor never learning of it.
                if (!reported)
                {
                    _log.Log_Warning(session.OrchId, "Guard-not-in-force report was not appended (the owner is at the terminal, or the channel was locked) — the marker survives and the next tick retries");
                    continue;
                }

                // Recorded only after the append SUCCEEDED — a report that was not made must not
                // start a cooldown, or the failure silences the next thirty minutes as well. Master's
                // check above now makes that explicit where this comment used to be the only guard.
                _reportedGuardsByOrchId[session.OrchId] = new GuardReportRecord
                {
                    MarkerText = markerText,
                    ReportedAt = DateTime.Now,
                };

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
        foreach (var session in Sessions_ThisTick())
        {
            if (session.ClosedUtc != null)
                continue;

            // Above the SIGNATURE, not at the append: storing it while suppressed marks this exact
            // set of idle members as already flagged, and the flag never returns after the meeting.
            //
            // Safe at the TOP, for the same reason as Report_LedgerShape and NOT for the reason the
            // nudge needed: the signature below is CONTENT-ADDRESSED, so any later change to who is
            // idle fires on its own and a tick skipped here strands nothing.
            var presence = Resolve_Presence(session.OrchId);

            if (OwnerPresence_Policy.Suppresses_SupervisorAttention(presence))
                continue;

            List<Status.IdleMember.IIdleMember> idle = [];

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId);

                if (!File.Exists(channelFile))
                    continue;

                var entries = ChannelHistory_Cache.Read_Entries(channelFile);

                if (!Status.Retirement_Advisor.Should_SuggestClosing(entries, Nudge_Decider.Has_BeenBriefed(channelFile), DateTime.Now))
                    continue;

                var idleFor = Status.Retirement_Advisor.Describe_IdleFor_OrNull(entries, DateTime.Now);

                if (idleFor == null)
                    continue;

                idle.Add(Status.IdleMember.IdleMember_Factory.Create(member.MemberId, idleFor));
            }

            // The key is the member SET — never the rendered line, which carries a duration that moves
            // every minute and defeated this comparison for six hours on 2026-08-13.
            var signature = Status.Retirement_Advisor.Build_FlagKey(idle);

            _flaggedIdleMembersByOrchId.TryGetValue(session.OrchId, out var lastSignature);

            if (signature == (lastSignature ?? ""))
                continue;

            // Nobody idle is FORGETTING — recorded regardless, so the next idle set flags again. A
            // non-empty signature CLAIMS the flag was delivered, so it waits for the append: it
            // re-fires only when the idle SET changes, and recording it for a flag that was never
            // written means this exact set is never flagged again.
            if (idle.Count == 0)
            {
                _flaggedIdleMembersByOrchId[session.OrchId] = signature;
                continue;
            }

            if (!Append_SupervisorAttention_UnlessMeeting(
                    session.OrchId,
                    Status.Retirement_Advisor.FLAG_SUBJECT,
                    $"{signature} — each declared STANDING BY and has nothing owed. Close what you are finished with: an idle member holds a window, a watcher and a context, and bills for all three. This is a REMINDER, not an instruction — if you still want one of them, keep it and ignore this.",
                    presence))
                continue;

            _flaggedIdleMembersByOrchId[session.OrchId] = signature;
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

            // WHOLE HISTORY, not the live file: both consumers of this list ask a question about the
            // channel's story — the status line picks the last real subject, the builder resolves the
            // member's state — and a compacted channel answers neither from its live half alone.
            var entries = ChannelHistory_Counter.Read_AllEntries(
                Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId));

            var usageFile = Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE);
            var contextUsage = UsageTotals_Reader.Read_ContextUsage_OrNull(usageFile);

            members.Add(Telegram.TopicStatusMember.TopicStatusMember_Factory.Create(member.MemberId, entries, isClosed: false, contextUsage));
        }

        return members;
    }


    string Build_MemberStatusText_ForSession(IOrchestrationSession session, Planning.PlanProgressSnapshot? previous = null)
    {
        var orchFolder = _paths.Get_OrchestrationFolder(session.OrchId);
        var supervisorUsage = Path.Combine(orchFolder, UsageTotals_Reader.SESSION_USAGE_FILE);

        // Whose move it is, read once for this whole status block: the supervisor row and a solo's
        // member row are the same conversation, so they must not answer it differently.
        var ownerOwesReply = Status.OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
            ChannelHistory_Cache.Read_Entries(_paths.Get_OwnerChannelFile(session.OrchId))) != null;

        var supervisorContextSuffix = Build_ContextSuffix_ForSupervisor(supervisorUsage);
        var supervisorLine = Is_Working(
            Running.SessionRoles.Supervisor, session.OrchId,
            Running.SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID, supervisorUsage)
            ? $"working now{Describe_Activity_Suffix(supervisorUsage)}{supervisorContextSuffix}"
            : ownerOwesReply ? $"{MemberState_Descriptor.WAITING_ON_OWNER}{supervisorContextSuffix}" : $"idle — waiting{supervisorContextSuffix}";

        // WHICH ROWS this carries is StatusRoster_Builder's — including the one that must NOT be
        // here for a basic orchestration. See that class for why the decision moved out.
        List<string> memberLines = [];

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
            {
                memberLines.Add($"- {member.MemberId}: closed");
                continue;
            }

            var memberFolder = _paths.Get_ImplementerFolder(session.OrchId, member.MemberId);
            var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId);
            var entries = ChannelHistory_Counter.Read_AllEntries(channelFile);
            var declared = MemberState_Resolver.Resolve(entries);
            // THE LINE THE OWNER ACTUALLY READS. In the fincanva-1 run of 2026-09-07 this said
            // "briefed — not started yet" for five members that were running turns for two hours, and
            // the words "working now" never appeared once — because the file it read cannot exist on
            // a headless host. It asks the app now.
            var workingNow = Is_Working(
                Running.SessionRoles.Implementer, session.OrchId, member.MemberId,
                Path.Combine(memberFolder, UsageTotals_Reader.SESSION_USAGE_FILE));

            var lastWrite = File.Exists(channelFile)
                ? $" · last wrote {SessionDuration_Formatter.Describe(DateTime.UtcNow - File.GetLastWriteTimeUtc(channelFile))} ago"
                : "";

            // Only the owner-facing session can be waiting on the OWNER; an implementer waits on its
            // supervisor, and handing the owner that queue would be telling them to clear one that is
            // not theirs.
            var memberOwesTheOwner = ownerOwesReply
                && Sessions.MemberKind_Ids.Resolve_Kind(member.MemberId) == Sessions.MemberKinds.Solo;

            // The same field, the same threshold and the same wording as the away variant of this
            // digest and as the status line — all three go through ContextVisibility_Policy and
            // ContextUsage_Formatter, so the owner can never be shown a member on one surface and
            // not on another at the same percentage.
            var memberContext = UsageTotals_Reader.Read_ContextUsage_OrNull(
                Path.Combine(memberFolder, UsageTotals_Reader.SESSION_USAGE_FILE));

            var memberContextSuffix = Status.ContextVisibility_Policy.Show_Member_InPeriodicDigest(member.MemberId, memberContext)
                ? $" · {Formatting.ContextUsage_Formatter.Describe_OrNull(memberContext)}"
                : "";

            memberLines.Add($"- {member.MemberId}: {MemberState_Descriptor.Describe_ForOwner(declared, workingNow, memberOwesTheOwner)}{memberContextSuffix}{lastWrite}");
        }

        // The header carries the ledger counts, so "who is doing what" and "how far along are we"
        // arrive in one answer — the owner asked for both without having to send /progress too.
        // Same builder /progress uses, so the two can never quote different figures.
        return Status.StatusRoster_Builder.Build(
            Build_OrchestrationCountsLine(session.OrchId, session.DisplayName ?? session.OrchId, previous),
            Sessions.OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc),
            supervisorLine,
            memberLines);
    }

    /// <summary>" — editing Foo.cs" when the transcript says so, empty when it cannot be read.</summary>
    static string Describe_Activity_Suffix(string usageFilePath)
    {
        var activity = SupervisorActivity_Describer.Describe_OrNull(usageFilePath);

        return activity == null ? "" : $" — {activity}";
    }

    /// <summary>
    /// The supervisor's context field for the half-hourly digest — always shown when there is a
    /// reading, per <see cref="Status.ContextVisibility_Policy.Show_Supervisor"/>. Neither the
    /// threshold nor the wording is decided here; both belong to one place each so this surface
    /// cannot drift from the status line.
    /// </summary>
    static string Build_ContextSuffix_ForSupervisor(string usageFilePath)
    {
        var context = UsageTotals_Reader.Read_ContextUsage_OrNull(usageFilePath);

        if (!Status.ContextVisibility_Policy.Show_Supervisor(context))
            return "";

        return $" · {Formatting.ContextUsage_Formatter.Describe_OrNull(context)}";
    }

    // Describe_SessionActivity LIVED HERE and is deleted rather than left for someone to reach for.
    // It had no callers, and it answered "working now" or an idle word straight from the status-line
    // probe — the exact shape that told the owner a working member was idle on every headless host.
    // Is_Working is the replacement, and it takes the identity this one had no way to ask for.

    // Describe_DeclaredState USED TO LIVE HERE and it is gone, not moved. It took the same three
    // arguments as MemberState_Descriptor.Describe_ForOwner and answered the same question, so it
    // was item 12's second copy wearing the comment that warns about second copies — and the copies
    // had already drifted where it mattered most. Its working-now branch printed
    // "working now (channel says: {declared})", which for an open writing window interpolated the
    // descriptor's OTHER wording, "idle — writing window left open", producing the single line the
    // owner sent back on 2026-08-24:
    //
    //     solo-1: working now (channel says: idle — writing window left open)
    //
    // One session, one instant, both words. Describe_ForOwner has always resolved that pair
    // correctly — "working now (writing window open)" — because it treats working-now as outranking
    // the declaration instead of quoting the declaration verbatim beside it.

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
            // THE ONE COMPOSER, not a second copy of the expression. This site duplicated
            // `Build_WantedTopicName`'s argument list verbatim, which is the drift decision 12 is
            // about: the eight-argument call was written twice and the recreated topic would have
            // kept whichever glyph set the last editor forgot to change here.
            var topicName = Build_WantedTopicName(session);

            // Recreate rather than delete-by-id: it is the only way to leave the topic genuinely
            // empty, and it cannot touch a neighbouring topic by accident.
            await client.Delete_ForumTopic_Async(messageThreadId ?? throw new Exception($"orchestration '{session.OrchId}' has no topic id to clear"), cancellationToken);

            var newTopicId = await client.Create_ForumTopic_Async(topicName, Resolve_TopicColour_OrNull(session.RepoName), cancellationToken);
            _store.Set_TelegramTopicId(session.OrchId, newTopicId);

            _appliedTopicNames[session.OrchId] = topicName;

            // AND THE RETRY STAMP GOES WITH THE TOPIC IT WAS ABOUT (rev-10's F2). The stamp means "an
            // attempt on this orchestration told us nothing"; once the topic has been deleted and
            // recreated, the thing it was about no longer exists, and leaving it would gate the NEW
            // topic's first name sync for up to the remainder of 30 s.
            //
            // It bites exactly when the glyph carries information: the name applied at creation is the
            // two-argument decoration, so an away or quiet glyph still has to be synced afterwards —
            // and that sync is the one being held. Bounded and self-healing, which is why it is LOW,
            // but it is a stale memo about a deleted object and those do not improve with age.
            _topicNameRetryAfterUtc.Remove(session.OrchId);

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

            // The undeletable message went with the old topic — the new one starts unlatched.
            _repostImpossibleOrchIds.Remove(session.OrchId);

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

    /// <summary>
    /// /tail and /log — the WINDOW a bridge-driven session does not have. A print or stream session
    /// runs headless, so "what is it doing" and "what happened in that turn" had no answer at all
    /// except the entry it eventually wrote; the bridge records every turn beside the session's
    /// state file and these two read it back. No model is involved, so both are free and instant.
    /// </summary>
    async Task Send_TurnLog_Async(ITelegramApiClient client, long? messageThreadId, string command, string rawText, CancellationToken cancellationToken)
    {
        var text = Build_TurnLogText(messageThreadId, command, rawText);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    string Build_TurnLogText(long? messageThreadId, string command, string rawText)
    {
        if (messageThreadId == null)
            return $"send /{command} inside an orchestration's topic (e.g. /{command} 1)";

        var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        if (session == null)
            return "no orchestration is bound to this topic";

        var argument = Read_CommandArgument(rawText);
        var target = Running.TurnLog.TurnLog_Locator.Resolve_OrNull(session, argument);

        if (target == null)
            return Running.TurnLog.TurnLog_Locator.Describe_Choices(session, $"/{command}");

        var logFile = Running.TurnLog.TurnLog_Locator.Get_LogFile(_paths, session.OrchId, target.Value);

        return command == "log"
            ? Running.TurnLog.TurnLog_Formatter.Format_LastTurn(target.Value.MemberId, Running.TurnLog.TurnLog_Store.Read_LastTurn(logFile, TURN_LOG_SCAN_RECORDS))
            : Running.TurnLog.TurnLog_Formatter.Format_Tail(target.Value.MemberId, Running.TurnLog.TurnLog_Store.Read_LastRecords(logFile, Running.TurnLog.TurnLog_Formatter.DEFAULT_TAIL_EVENTS));
    }

    /// <summary>Everything after the command word: "/tail imp-2" -> "imp-2", "/tail" -> "".</summary>
    static string Read_CommandArgument(string rawText)
    {
        var trimmed = rawText.Trim();
        var space = trimmed.IndexOf(' ');

        return space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
    }

    /// <summary>/imp 2 — the latest entries of one implementer's spoke, which never reaches Telegram otherwise.</summary>
    async Task Send_ImplementerPeek_Async(ITelegramApiClient client, long? messageThreadId, string command, string rawText, CancellationToken cancellationToken)
    {
        var text = Build_ImplementerPeekText(messageThreadId, command, rawText);

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
        var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, memberId);
        var entries = ChannelHistory_Cache.Read_Entries(channelFile);

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

        // The hold button, for the same reason: it is a control the APP owns, not an answer to
        // forward to a session.
        if (await Try_HandleHoldTap_Async(client, tap, cancellationToken))
            return;

        // The topic's standing command bar, for the same reason again — and it must come BEFORE the
        // generic path below, which does not recognise the data and would answer the owner "expired"
        // for a button that is meant to work every time they press it.
        if (await Try_HandleTopicCommandTap_Async(client, tap, cancellationToken))
            return;

        PendingButtonRecord? registered;
        TapOutcomes outcome;

        lock (_buttonLock)
        {
            var found = _buttonOptions.TryGetValue(tap.Data, out registered);

            outcome = PendingDecision_Gate.Classify(
                tap.Data,
                found,
                registered?.ExpiresUtc ?? default,
                registered?.IsHighRisk ?? false,
                _clock.UtcNow);

            // SINGLE-USE: the first tap consumes the WHOLE option group — a second tap (or a
            // sibling button) resolves to "no longer open" instead of double-firing a decision.
            // A LAPSED group is consumed too: leaving it registered means every later tap pays
            // another expiry check on a decision that can never be taken again.
            //
            // NO BUTTON IS EXEMPT ANY MORE. "Let's talk" used to keep its group live, on the
            // reasoning that discussing a decision must not take the question off the phone. What
            // the owner actually got was a button whose tap changed nothing on screen — they tapped
            // it twelve times in one afternoon — while the app went on holding a live question they
            // had visibly stopped answering. The discussion ends in a fresh question instead.
            if (registered != null && outcome != TapOutcomes.Unknown && outcome != TapOutcomes.NotOurs)
            {
                List<string> groupKeys = [.. _buttonOptions.Where(pair => pair.Value.GroupId == registered.GroupId).Select(pair => pair.Key)];

                foreach (var key in groupKeys)
                    _buttonOptions.Remove(key);
            }
        }

        // SAID, NOT SWALLOWED — and said with the reason. A payload that parses as one of ours and
        // matches no live decision is either a keyboard the owner scrolled back to, or a replayed
        // token; either way the app just refused to act on a tap, which is a thing that must appear
        // somewhere a human can find it. This is the class of event the single "expired" answer used
        // to hide, because a consumed button and an invented one read identically.
        if (outcome is TapOutcomes.Unknown or TapOutcomes.Expired)
        {
            var parsed = CallbackToken.Parse_OrNull(tap.Data);
            var describedToken = parsed == null ? "an unrecognised payload" : $"nonce {parsed.Value.Nonce} option {parsed.Value.OptionIndex}";

            _log.Log_Warning(
                GLOBAL_ORCH_ID,
                outcome == TapOutcomes.Expired
                    ? $"Callback REFUSED ({describedToken}): the decision had expired. The owner was asked to type their choice."
                    : $"Callback REFUSED ({describedToken}): no live decision holds it — already answered, or replayed. The owner was asked to type their choice.");
        }

        try
        {
            // Must always be answered or the button spinner hangs on the phone.
            await client.Answer_CallbackQuery_Async(tap.CallbackQueryId, PendingDecision_Gate.Describe_ForOwner(outcome), cancellationToken);
        }
        // FILTERED — THE TOKEN DECIDES, as at the ~30 other sites in this file. An HttpClient timeout
        // surfaces as TaskCanceledException with the token NOT cancelled, and rethrowing it here
        // escaped the whole inbound batch. `_lastUpdateId` only advances at the END of that batch, so
        // Telegram then re-served every message in it — including a `/pc`, which is a blind Toggle and
        // therefore flipped terminal mode straight back OFF about five seconds later. That was the one
        // remaining way `/pc` could end by itself, which the owner's ruling does not allow.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"answerCallbackQuery failed: {ex.Message}");
        }

        if (registered == null || outcome is TapOutcomes.Unknown or TapOutcomes.Expired or TapOutcomes.NotOurs)
        {
            // The group was consumed above on an expiry, so the persisted state has to follow it
            // down — otherwise a restart brings the dead keyboard back.
            if (outcome == TapOutcomes.Expired)
                Persist_EngineState();

            return;
        }

        if (outcome == TapOutcomes.NeedsConfirmation)
        {
            await Begin_HighRiskConfirmation_Async(client, tap, registered, cancellationToken);
            return;
        }

        // Rewrite the question message to RECORD what the tap did — "❓ … / ✅ deep" for a choice,
        // and the acknowledgement for "💬 Let's talk", which records no choice because none was
        // made. Telegram's tap acknowledgement is a transient toast and the keyboard vanishes, so
        // without this the chat keeps no trace of what was picked — the owner scrolls back and
        // cannot tell what they answered. Editing the text also drops the keyboard, so it replaces
        // the strip step.
        //
        // EVERY TAP EDITS ITS OWN MESSAGE, and the one that did not is the owner's request here:
        // *"Let's talk does nothing when I tap it — it stays there, all the other options stay too.
        // Make it behave like the other buttons."*
        if (tap.MessageId != null)
        {
            // Answered — it must never be marked "parked" by a later away-mode sweep.
            lock (_ownerStateLock)
            {
                if (_openQuestions.Remove(tap.MessageId.Value))
                {
                    Note_QuestionClosed(
                        tap.MessageId.Value,
                        registered.AnswersNothing
                            ? QuestionClosure_Wording.TALK_REQUEST
                            : QuestionClosure_Wording.TAPPED_OPTION);
                }
            }

            // The stored QuestionText is the MARKDOWN that was sent, so the rewrite must render
            // it again — an HTML send followed by a plain edit would put the markers back.
            var rewrite = registered.AnswersNothing
                ? QuestionPrompt_Builder.Build_TalkText(registered.QuestionText)
                : QuestionPrompt_Builder.Build_AnsweredText(registered.QuestionText, registered.OptionText);

            await Rewrite_AnsweredQuestion_WithRetry_Async(client, tap.MessageId.Value, rewrite, cancellationToken);
        }

        // SAVED BEFORE THE ANSWER IS ROUTED, and the reason is a TRADE rather than a safety net —
        // the comment that used to sit here ("routing is what can fail") named the wrong half.
        //
        // Crash between these two lines and the answer is lost: buttons consumed, question closed,
        // the message already edited to show the choice. Crash the other way round and the answer is
        // delivered while the keyboard survives the restart, so the owner can take the SAME decision
        // twice — and the decisions that reach this line include pushes and deploys.
        //
        // A lost answer is recoverable and visible: the supervisor is still blocked, the stall alert
        // fires, and the owner is asked again. A double push is neither. So this order is chosen
        // knowing what it costs; the thing that would remove the trade altogether is a durable
        // outbox, which is a bigger change than this stage.
        Persist_EngineState();

        await Route_TapAsOwnerMessage_Async(tap, registered, cancellationToken);
    }

    /// <summary>
    /// A tap IS an owner message: the tapped text goes through the normal pipeline (aggregation,
    /// delivery receipts) into the topic the buttons live in.
    ///
    /// <para>
    /// ONE ROUTE FOR BOTH KINDS OF TAP — the answer that closes a question, and the "let's talk"
    /// that deliberately does not. What differs between them is everything ABOVE this point
    /// (consuming the group, editing the message, closing the question); what a session receives is
    /// the same shape either way, and writing it twice would be two places for that to drift.
    /// </para>
    /// </summary>
    async Task Route_TapAsOwnerMessage_Async(
        ITelegramCallbackTap tap,
        PendingButtonRecord registered,
        CancellationToken cancellationToken)
    {
        // MARKED AS APP-COMPOSED, which is the root fix for every button rather than for one of
        // them: the text is the option's, not the owner's keyboard, so it must never be bound as a
        // typed answer to whatever OTHER question happens to be open. See
        // ITelegramOwnerMessage.IsAppComposed for the 2026-09-09 pair of taps this comes from.
        var syntheticMessage = TelegramOwnerMessage_Factory.Create(
            tap.UpdateId, tap.MessageId, 0, 0, registered.ThreadId ?? tap.MessageThreadId, registered.OptionText, null, null,
            isAppComposed: true);

        await Route_OwnerMessage_Async(syntheticMessage, cancellationToken);
    }

    /// <summary>
    /// THE SECOND GESTURE. A tap on a high-risk option does not take the decision — it opens a
    /// read-back: the message is edited to show a four-digit code, and only that code, typed back
    /// into the topic, releases the answer to the session.
    ///
    /// <para>
    /// WHY A TAP IS NOT ENOUGH HERE. Every other decision in this system is recoverable by asking
    /// again. A push, a deploy, a spend or a recursive delete is not, and the device it is taken on
    /// is a phone that spends its day unlocked in a pocket or on a desk. The rule is the aviation
    /// read-back: the second action must be deliberate and must be composed by the person looking at
    /// the screen, which a tap on a notification is not.
    /// </para>
    /// <para>
    /// THE MESSAGE IS EDITED, NEVER REPLACED (decision 14). The code appears on the question the
    /// owner is already looking at, so there is nothing to scroll for and nothing new to notify.
    /// </para>
    /// <para>
    /// THE CODE IS NEVER LOGGED. It goes into the Telegram message and into the state file, and
    /// nowhere else — see <see cref="ConfirmationCode"/> for why that boundary is where it is.
    /// </para>
    /// </summary>
    async Task Begin_HighRiskConfirmation_Async(
        ITelegramApiClient client,
        ITelegramCallbackTap tap,
        PendingButtonRecord registered,
        CancellationToken cancellationToken)
    {
        // THE GENERAL TOPIC IS A SCOPE LIKE ANY OTHER HERE, and treating it as "no orchestration" was
        // a defect that hit the common case: the general supervisor is precisely the session that
        // discusses shipping and deploying, so its questions are the ones most likely to be high
        // risk — and every one of them answered "that decision belongs to no open orchestration",
        // for ever, with the keyboard already consumed and the buttons coming back on every restart.
        var orchId = Resolve_DecisionScope_ForThread(registered.ThreadId ?? tap.MessageThreadId);

        var guardrails = _configProvider.Get_Current().Guardrails;
        var code = ConfirmationCode.Generate();

        var confirmation = new PendingConfirmationRecord
        {
            Code = code,
            ThreadId = registered.ThreadId ?? tap.MessageThreadId,
            MessageId = tap.MessageId,
            OrchId = orchId,
            OptionText = registered.OptionText,
            QuestionText = registered.QuestionText,
            ExpiresUtc = _clock.UtcNow.AddMinutes(guardrails.HighRiskCodeExpiryMinutes),
        };

        lock (_ownerStateLock)
        {
            // THE QUESTION STAYS OPEN, and this is the fix to the worst thing a tap could do.
            //
            // Removing it here meant a high-risk question with `DEADLINE: 30m` — whose whole contract
            // is "denied at 30 minutes" — became invisible to the deadline sweep the moment it was
            // tapped. The owner taps, sees the code, is interrupted, never types it: the code lapses
            // in ten minutes and NOTHING notices, the awaiting-answer flag is never cleared, and the
            // supervisor's hook denies every tool call for ever. A tap was the one gesture that
            // converted a bounded question into an unbounded one, which is the exact inversion the
            // deadline exists to prevent.
            //
            // Left open, the sweep still denies it on time and takes the confirmation down with it.
            _pendingConfirmations.RemoveAll(existing => existing.MessageId == confirmation.MessageId && existing.OrchId == orchId);
            _pendingConfirmations.Add(confirmation);
        }

        // Saved BEFORE the edit: a crash between the two leaves a code the app still honours and a
        // message that does not show it, which the owner recovers from by tapping again. The reverse
        // — a code on screen that the app has never heard of — cannot be recovered from at all.
        Persist_EngineState();

        if (tap.MessageId != null)
        {
            try
            {
                await TelegramProse_Sender.Edit_Async(
                    client, _log, orchId, tap.MessageId.Value,
                    QuestionPrompt_Builder.Build_ConfirmationText(registered.QuestionText, registered.OptionText, code, guardrails.HighRiskCodeExpiryMinutes),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The code exists and is live; what failed is showing it. Sending it as its own
                // message is the one case where a new message beats an edit — without it the owner
                // is holding a decision they have no way to complete.
                _log.Log_Warning(orchId, $"High-risk confirmation edit failed: {ex.Message} — sending the code as a message instead");

                await Send_DirectReply_BestEffort_Async(
                    client,
                    confirmation.ThreadId,
                    QuestionPrompt_Builder.Build_ConfirmationText(registered.QuestionText, registered.OptionText, code, guardrails.HighRiskCodeExpiryMinutes),
                    cancellationToken);
            }
        }

        _log.Log_Info(orchId, "High-risk decision tapped — awaiting the read-back code before the answer is delivered");
    }

    /// <summary>
    /// The scope a DECISION belongs to: an orchestration id, or the General channel's own id when the
    /// tap arrived in the General topic (which carries no thread id — the absence IS how this app
    /// tells it apart).
    ///
    /// <para>
    /// Separate from <see cref="Resolve_OrchId_ForThread_OrNull"/> deliberately, and adding it fixed a
    /// defect in the common case rather than an edge: that method answers "which SESSION owns this
    /// topic" and General owns none, which is right for /pending's scoping and wrong for a decision.
    /// The general supervisor is precisely the session that discusses shipping and deploying, so its
    /// questions are the ones most likely to be high risk — and every one of them used to answer
    /// "that decision belongs to no open orchestration", for ever, with the keyboard already
    /// consumed and the buttons coming back on every restart.
    /// </para>
    /// </summary>
    string Resolve_DecisionScope_ForThread(long? messageThreadId)
    {
        return Resolve_OrchId_ForThread_OrNull(messageThreadId) ?? ChannelDiscovery.GENERAL_ORCH_ID;
    }

    /// <summary>Drops every pending read-back for a scope — used when its session ends.</summary>
    void Discard_PendingConfirmations(string orchId)
    {
        lock (_ownerStateLock)
            _pendingConfirmations.RemoveAll(confirmation => confirmation.OrchId == orchId);
    }

    /// <summary>Which orchestration a Telegram topic belongs to, or null for General and unknowns.</summary>
    string? Resolve_OrchId_ForThread_OrNull(long? messageThreadId)
    {
        if (messageThreadId == null)
            return null;

        var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        return session == null || session.ClosedUtc != null ? null : session.OrchId;
    }

    /// <summary>
    /// Completes — or refuses — a high-risk decision the owner has tapped, from the code they typed.
    ///
    /// <para>
    /// Returns true when the message WAS the second gesture (right code, wrong code, or a code
    /// arriving too late) and must therefore not also reach the session as ordinary chat. Returns
    /// false for anything else, so a message that merely happens to be sent while a confirmation is
    /// open flows through untouched.
    /// </para>
    /// <para>
    /// AN UNRELATED MESSAGE DOES NOT CANCEL THE CONFIRMATION. The owner types "hang on" and then the
    /// code; cancelling on the first would make the flow unusable for the one person it exists for.
    /// It lapses on its own clock instead, which is the only thing that can end it besides the code.
    /// </para>
    /// </summary>
    async Task<bool> Try_CompleteHighRiskConfirmation_Async(
        ITelegramApiClient client,
        ITelegramOwnerMessage message,
        CancellationToken cancellationToken)
    {
        var orchId = Resolve_DecisionScope_ForThread(message.MessageThreadId);
        var looksLikeACode = ConfirmationCode.Looks_LikeACode(message.Text);

        PendingConfirmationRecord? confirmation;
        bool anyLiveInThisTopic;

        lock (_ownerStateLock)
        {
            // MATCHED AGAINST EVERY LIVE READ-BACK IN THIS TOPIC, not against one. The codes are
            // distinct, so scrolling back to an earlier question and typing ITS code resolves to
            // that question — which is what the owner means and what the screen still shows them.
            // Keyed by orchestration with "newest wins", the first of two taps became unanswerable
            // by tap, unanswerable by code, and still displaying a code the app had discarded.
            confirmation = _pendingConfirmations.FirstOrDefault(candidate =>
                candidate.OrchId == orchId
                && _clock.UtcNow < candidate.ExpiresUtc
                && ConfirmationCode.Matches(message.Text, candidate.Code));

            anyLiveInThisTopic = _pendingConfirmations.Any(candidate =>
                candidate.OrchId == orchId && _clock.UtcNow < candidate.ExpiresUtc);

            if (confirmation != null)
            {
                _pendingConfirmations.Remove(confirmation);

                // The decision is taken, so the question it belongs to is answered. It was left OPEN
                // while the read-back ran, on purpose — see Begin_HighRiskConfirmation_Async.
                if (confirmation.MessageId != null && _openQuestions.Remove(confirmation.MessageId.Value))
                    Note_QuestionClosed(confirmation.MessageId.Value, QuestionClosure_Wording.CONFIRMED_HIGH_RISK);
            }
        }

        if (confirmation == null)
        {
            // NOT A CODE, OR NOTHING LIVE TO MATCH IT AGAINST. Either way the message is ordinary and
            // must reach the session untouched: the owner types "hang on" and then the code, and a
            // lapsed read-back must not eat something they sent an hour later about something else.
            if (!looksLikeACode || !anyLiveInThisTopic)
                return false;

            // THE READ-BACK SURVIVES A WRONG CODE, within its own window: a mistyped digit costs a
            // retype, not the decision. The window is what bounds the attempts.
            _log.Log_Warning(orchId, "A high-risk read-back code did not match any live decision — nothing was taken");
            await Send_DirectReply_BestEffort_Async(client, message.MessageThreadId, "🔐 That is not the code. Check the message above and try again.", cancellationToken);
            return true;
        }

        // Saved before the answer is routed, for the trade the tap path spells out: losing an answer
        // is recoverable and visible, taking a push twice is not.
        Persist_EngineState();

        _log.Log_Info(orchId, "High-risk decision CONFIRMED by read-back code — the answer is being delivered");

        if (confirmation.MessageId != null)
        {
            try
            {
                await TelegramProse_Sender.Edit_Async(
                    client, _log, orchId, confirmation.MessageId.Value,
                    QuestionPrompt_Builder.Build_AnsweredText(confirmation.QuestionText, confirmation.OptionText),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Cosmetic: the decision is taken either way, and the session is about to be told.
                _log.Log_Warning(orchId, $"Confirmed-question edit failed: {ex.Message}");
            }
        }

        // The TAP's text, released by the code — app-composed for the same reason the tap itself is.
        var syntheticMessage = TelegramOwnerMessage_Factory.Create(
            message.UpdateId, message.MessageId, 0, 0, confirmation.ThreadId ?? message.MessageThreadId, confirmation.OptionText, null, null,
            isAppComposed: true);

        await Route_OwnerMessage_Async(syntheticMessage, cancellationToken);
        return true;
    }

    /// <summary>
    /// /pending — every decision waiting on the owner, answered from the app's own state.
    /// </summary>
    async Task Send_PendingDecisions_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        List<OpenQuestionRecord> questions;
        List<PendingConfirmationRecord> confirmations;

        lock (_ownerStateLock)
        {
            questions = [.. _openQuestions.Values];
            confirmations = [.. _pendingConfirmations];
        }

        // IN A TOPIC, ONLY THAT TOPIC'S. Asked inside an orchestration the owner means "what is
        // waiting on me HERE"; asked in General they mean everything. The same scope rule /progress
        // and /cost already follow.
        var orchId = Resolve_OrchId_ForThread_OrNull(messageThreadId);

        if (orchId != null)
        {
            questions = [.. questions.Where(question => question.OrchId == orchId)];
            confirmations = [.. confirmations.Where(confirmation => confirmation.OrchId == orchId)];
        }

        var text = PendingDecisions_Report.Build(questions, confirmations, _clock.UtcNow);

        await Send_DirectReply_BestEffort_Async(client, messageThreadId, text, cancellationToken);
    }

    /// <summary>
    /// Reminds, defaults or denies every question whose window has moved on.
    ///
    /// <para>
    /// THE REMINDER IS AN EDIT (decision 14) and happens once. A default is applied by DELIVERING
    /// the option's own text to the session exactly as a tap would, so nothing downstream needs to
    /// know the difference — and an app entry says, in the channel the owner reads back, that it was
    /// a timeout rather than them.
    /// </para>
    /// <para>
    /// BEST-EFFORT PER QUESTION. One question whose Telegram edit fails must not stop the sweep from
    /// reaching the next one; the decision has already been taken in state by then.
    /// </para>
    /// </summary>
    async Task Resolve_QuestionDeadlines_Async(CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;

        // LAPSED READ-BACKS ARE SWEPT HERE, not only when the owner happens to type again. Their only
        // other evaluation is on an inbound message, so a code nobody ever typed sat in the state
        // file for ever and /pending printed "the code has EXPIRED" on every restart with no path to
        // clear it. The question it belongs to is deliberately left alone: it is still open, still
        // bounded by its own deadline, and still answerable by typing.
        List<PendingConfirmationRecord> lapsed;

        lock (_ownerStateLock)
        {
            lapsed = [.. _pendingConfirmations.Where(confirmation => nowUtc >= confirmation.ExpiresUtc)];

            foreach (var confirmation in lapsed)
                _pendingConfirmations.Remove(confirmation);
        }

        foreach (var confirmation in lapsed)
        {
            // READ, NOT ASSUMED. This line used to say "the question is still open" without ever
            // looking, and on 2026-09-09 at ~16:03Z it said it about a question that had been
            // stamped closed minutes earlier.
            var closure = Read_QuestionClosure(confirmation.MessageId);

            _log.Log_Warning(
                confirmation.OrchId,
                QuestionClosure_Wording.Describe_LapsedReadBack(closure.StillOpen, closure.Reason));
        }

        List<OpenQuestionRecord> due;

        lock (_ownerStateLock)
        {
            due = [.. _openQuestions.Values.Where(question =>
                QuestionDeadline_Planner.Decide(
                    question.AskedUtc, question.DeadlineUtc, question.ReminderSent,
                    question.IsHighRisk, question.DefaultOptionIndex, nowUtc) != QuestionDeadlineActions.None)];
        }

        if (due.Count == 0)
        {
            if (lapsed.Count > 0)
                Persist_EngineState();

            return;
        }

        foreach (var question in due)
        {
            var action = QuestionDeadline_Planner.Decide(
                question.AskedUtc, question.DeadlineUtc, question.ReminderSent,
                question.IsHighRisk, question.DefaultOptionIndex, nowUtc);

            try
            {
                if (action == QuestionDeadlineActions.Remind)
                    await Remind_AboutQuestion_Async(question, nowUtc, cancellationToken);
                else
                    await Close_QuestionOnDeadline_Async(question, action, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(question.OrchId, $"Question deadline handling failed: {ex.Message}");
            }
        }

        Persist_EngineState();
    }

    async Task Remind_AboutQuestion_Async(OpenQuestionRecord question, DateTime nowUtc, CancellationToken cancellationToken)
    {
        lock (_ownerStateLock)
        {
            // MARKED FIRST, so a failing edit cannot turn the once-only reminder into a per-tick
            // one. A reminder that is missed is a reminder; a reminder every two seconds is the
            // waterfall decision 14 exists to prevent.
            if (!_openQuestions.TryGetValue(question.MessageId, out var live))
                return;

            _openQuestions[question.MessageId] = live with { ReminderSent = true };
        }

        var client = _telegramClient;

        if (client == null || question.DeadlineUtc == null)
            return;

        var remaining = question.DeadlineUtc.Value - nowUtc;

        await TelegramProse_Sender.Edit_Async(
            client, _log, question.OrchId, question.MessageId,
            QuestionPrompt_Builder.Build_TimedOutText(question.Text, $"Still waiting — about {Math.Max(1, (int)remaining.TotalMinutes)} minutes left."),
            cancellationToken);
    }

    async Task Close_QuestionOnDeadline_Async(OpenQuestionRecord question, QuestionDeadlineActions action, CancellationToken cancellationToken)
    {
        var appliedDefault = action == QuestionDeadlineActions.ApplyDefault;

        string? chosenOptionText = null;

        lock (_ownerStateLock)
        {
            if (!_openQuestions.Remove(question.MessageId))
                return;

            Note_QuestionClosed(question.MessageId, QuestionClosure_Wording.DEADLINE);

            // A READ-BACK BELONGS TO ITS QUESTION AND DIES WITH IT. Left behind, it would keep a live
            // code for a decision that has just been denied on timeout — the owner types the code
            // they can still see on screen and takes an answer the app has already refused.
            _pendingConfirmations.RemoveAll(confirmation => confirmation.MessageId == question.MessageId);
        }

        lock (_buttonLock)
        {
            List<PendingButtonRecord> groupButtons = [.. _buttonOptions.Values.Where(button => button.GroupId == question.ButtonGroupId)];

            if (appliedDefault && question.DefaultOptionIndex != null)
            {
                // The option's own text, taken from the button that would have delivered it — so a
                // default and a tap hand the session the identical words, and there is no second
                // place where "what option 2 means" is decided.
                var index = question.DefaultOptionIndex.Value;
                var parsedIndexes = groupButtons
                    .Select(button => (Button: button, Parsed: CallbackToken.Parse_OrNull(button.Data)))
                    .Where(pair => pair.Parsed != null && pair.Parsed.Value.OptionIndex == index)
                    .ToList();

                chosenOptionText = parsedIndexes.Count == 1 ? parsedIndexes[0].Button.OptionText : null;
            }

            foreach (var button in groupButtons)
                _buttonOptions.Remove(button.Data);
        }

        // A DEFAULT THAT CANNOT BE RESOLVED BECOMES A DENY, never a guess. The buttons are the only
        // record of what the option said, and if the registry has already evicted them there is
        // nothing left to deliver — answering with the wrong option is worse than answering nothing.
        if (appliedDefault && chosenOptionText == null)
        {
            appliedDefault = false;
            _log.Log_Warning(question.OrchId, "A question's default could not be resolved to an option — it was DENIED on timeout instead of guessed");
        }

        var outcome = appliedDefault
            ? $"No answer by the deadline — option {question.DefaultOptionIndex + 1} was taken automatically."
            : "No answer by the deadline — DENIED (timeout).";

        _log.Log_Warning(question.OrchId, $"Question closed on its deadline: {outcome}");

        // THE CHANNEL IS THE RECORD. It is what the owner reads back in the catch-up burst, and what
        // the away digest is built from — so a decision taken in their absence is visible in the
        // same place every other decision is, rather than only in a Telegram edit they may never
        // scroll to.
        Append_OrchestrationAppEntry(
            question.OrchId,
            AppEntryAudiences.Owner,
            appliedDefault ? "question DEFAULTED on timeout" : "question DENIED on timeout",
            $"{outcome}\n\nThe question was: {question.Text}");

        if (_telegramClient != null)
        {
            try
            {
                await TelegramProse_Sender.Edit_Async(
                    _telegramClient, _log, question.OrchId, question.MessageId,
                    QuestionPrompt_Builder.Build_TimedOutText(question.Text, outcome),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(question.OrchId, $"Timed-out question edit failed: {ex.Message}");
            }
        }

        // The SESSION is told either way: an implementer blocked on a question needs the deny as
        // much as it needs the default, or the deadline merely moves the stall somewhere quieter.
        var session = _store.Get_Session_OrNull(question.OrchId);

        // AND IT IS TOLD IN ITS OWN TOPIC OR NOT AT ALL. Route_OwnerMessage_Async resolves the
        // orchestration from the THREAD ID and treats a null one as the General topic — so an
        // orchestration whose topic has been deleted (a close raced with a deadline) would have had
        // its refusal delivered to the general supervisor, as if the owner had said it there. The
        // app entry above is already written, so the orchestration still has the record; what is
        // skipped here is only the owner-voiced delivery, and skipping it beats misdirecting it.
        if (session?.TelegramTopicId == null)
        {
            _log.Log_Warning(question.OrchId, "The timed-out question's topic is gone — the outcome was written to the channel but not delivered as an owner message");
            return;
        }

        // The owner said nothing at all here, so this is the clearest app-composed message of the
        // three: binding it to another open question would file a sentence the owner never wrote as
        // their answer to a decision they never saw.
        var syntheticMessage = TelegramOwnerMessage_Factory.Create(
            0, null, 0, 0, session.TelegramTopicId,
            appliedDefault
                ? chosenOptionText ?? ""
                : "No — the deadline passed with no answer from me. Treat this as a refusal and say what you need instead.",
            null, null,
            isAppComposed: true);

        await Route_OwnerMessage_Async(syntheticMessage, cancellationToken);
    }

    /// <summary>
    /// Pauses the dispatcher when the account is at or over the configured share of a usage window,
    /// and resumes it when the window resets.
    ///
    /// <para>
    /// ONE ALERT, WITH A TIME ON IT. Sixty sessions hitting the same limit produce sixty identical
    /// failures today; this produces one message saying when the allowance comes back. The alert is
    /// outbound and is therefore suppressed while muted — but the pause and the resume are not
    /// messages and happen regardless, which is why this method sits above the DND gate.
    /// </para>
    /// </summary>
    async Task Update_DispatchPause_Async(CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;

        DateTime? pausedUntilUtc;
        string? previousReason;

        // GUARDED, because the mirror loop writes these and the inbound loop reads them for /limits.
        // A DateTime? is 16 bytes and its store is not atomic on any platform, so an unsynchronised
        // read could print — or PERSIST — a torn instant that then survives the restart.
        lock (_ownerStateLock)
        {
            pausedUntilUtc = _dispatchPausedUntilUtc;
            previousReason = _dispatchPauseReason;
        }

        // Still inside the window: nothing to decide. Re-reading the probes here would let a
        // still-high percentage extend the pause indefinitely past the reset it was measured
        // against, which is how a five-hour pause becomes a permanent one.
        if (Limits.DispatchPause_Gate.Is_Paused(pausedUntilUtc, nowUtc))
            return;

        var wasPaused = pausedUntilUtc != null;

        // THROTTLED LIKE THE ALERT SCAN IT SHARES A READER WITH — reading the probes means globbing
        // the supervision root and parsing one JSON per session, and at the 2 s tick rate that is
        // thirty sweeps a minute for a number that moves once a turn. A pause that has just LAPSED is
        // exempt: it must be re-decided at its own instant, not up to a minute later.
        if (!wasPaused && (nowUtc - _lastDispatchPauseCheckUtc).TotalSeconds < LIMIT_CHECK_INTERVAL_SECONDS)
            return;

        _lastDispatchPauseCheckUtc = nowUtc;

        var thresholdPercent = _configProvider.Get_Current().Guardrails.DispatchPauseThresholdPercent;

        DateTime? pauseUntilUtc = null;
        string? bindingWindow = null;
        var bindingPercent = 0d;

        // THE BINDING WINDOW IS THE ONE THAT COMES BACK LAST, not the first one enumerated. Probe
        // files are globbed, so dictionary order is arbitrary — and picking the first over-threshold
        // window meant a weekly at 99% resetting in three days could lose to a five-hour at 96%
        // resetting in twenty minutes. Dispatch would resume on the five-hour's clock, launch
        // sessions into a weekly allowance that is still spent, and pause again.
        foreach (var pair in Read_CurrentLimitWindows())
        {
            var candidate = Limits.DispatchPause_Gate.Decide_PauseUntil_OrNull(
                pair.Value.Percent, pair.Value.WindowResetsAtUtc, thresholdPercent, nowUtc);

            if (candidate == null || (pauseUntilUtc != null && candidate.Value <= pauseUntilUtc.Value))
                continue;

            pauseUntilUtc = candidate;
            bindingWindow = pair.Key;
            bindingPercent = pair.Value.Percent;
        }

        if (pauseUntilUtc != null)
        {
            var reason = $"the {bindingWindow} window was at {bindingPercent:0.#}%";

            lock (_ownerStateLock)
            {
                _dispatchPausedUntilUtc = pauseUntilUtc;
                _dispatchPauseReason = reason;
            }

            Persist_EngineState();

            // AN EXTENSION IS NOT AN EVENT. A window with no reset stamp gets a 30-minute fallback
            // pause, so a persistently over-threshold account used to produce a resume message and a
            // pause message every thirty minutes for ever — a stacking waterfall, which is the one
            // thing owner-facing repeats must never become. The state changes; the owner is told once
            // per episode, and /limits still answers whenever they ask.
            var alert = Limits.DispatchPause_Gate.Describe_Pause(bindingWindow ?? "usage", bindingPercent, pauseUntilUtc.Value);

            _log.Log_Warning(GLOBAL_ORCH_ID, wasPaused ? $"Dispatch pause EXTENDED — {alert}" : alert);

            if (!wasPaused)
                await Send_GeneralNotice_BestEffort_Async(alert, cancellationToken);

            return;
        }

        if (!wasPaused)
            return;

        lock (_ownerStateLock)
        {
            _dispatchPausedUntilUtc = null;
            _dispatchPauseReason = null;
        }

        Persist_EngineState();

        var resume = Limits.DispatchPause_Gate.Describe_Resume(previousReason ?? "the window reset");

        _log.Log_Info(GLOBAL_ORCH_ID, resume);
        await Send_GeneralNotice_BestEffort_Async(resume, cancellationToken);
    }

    /// <summary>
    /// An app-wide notice into the General topic. Suppressed while muted — it is outbound, and DND
    /// means exactly that; the state change it reports has already happened either way.
    /// </summary>
    async Task Send_GeneralNotice_BestEffort_Async(string text, CancellationToken cancellationToken)
    {
        var client = _telegramClient;

        if (client == null || _telegramMuted)
            return;

        await Send_DirectReply_BestEffort_Async(client, null, text, cancellationToken);
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

    /// <summary>
    /// <paramref name="sound"/> defaults to SILENT because that is what this method is: the app
    /// answering the owner, refusing a command, explaining itself. Fifty-odd call sites, and the
    /// owner's ruling covers all of them — *"status, receipts and app bookkeeping do not ring"*. The
    /// few that must ring say so at the call site, which is the only reason it is a parameter at all.
    /// </summary>
    async Task Send_DirectReply_BestEffort_Async(
        ITelegramApiClient client,
        long? messageThreadId,
        string text,
        CancellationToken cancellationToken,
        TelegramSendSounds sound = TelegramSendSounds.Silent)
    {
        try
        {
            // THROUGH THE RENDERER (owner's decision, 2026-09-09: "command replies go through the
            // HTML path"). Every reply on this route was a plain sendMessage, so a report carrying
            // the ledger's own `- [x]` lines, a `**bold**` heading or a backticked id arrived with
            // its markers showing — while the client's own doc comment claimed every owner-facing
            // send was HTML. The sender falls back to plain text on a parse refusal, so the worst
            // case is exactly today's behaviour.
            Remember_TopicMessage(
                messageThreadId,
                await TelegramProse_Sender.Send_Async(client, _log, GLOBAL_ORCH_ID, messageThreadId, text, sound, cancellationToken));
        }
        // FILTERED — THE TOKEN DECIDES, which is this file's canonical account (see
        // Refresh_TopicStatusLines_Async) applied to the one best-effort sender that still had the
        // bare rethrow. An HttpClient timeout surfaces as a TaskCanceledException with the token
        // NOT cancelled, so the bare form escalated a failed send into a shutdown.
        //
        // IT BECAME LOAD-BEARING WHEN A CALLER STARTED USING THIS FROM INSIDE A CATCH BLOCK (the
        // failed-photo reply below). There, an escape does not merely abandon the reply: it
        // abandons the CAPTION too, so the owner's message reaches nobody — and it escapes exactly
        // when Telegram is already degraded, which is the only time that reply is sent at all. A
        // "best effort" sender that can take its caller down is not best effort.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

                // The typed WAIT gets the same ▶ GO button the tapped one does. Two ways in, one way
                // out: an owner who typed WAIT should not have to type GO because the button only
                // appears on the path they did not take.
                (string Data, string Label)[] releaseButton =
                    [(HoldButton_Data.Build(HoldButtonActions.Go, message.MessageThreadId), HoldButton_Data.GO_LABEL)];

                if (existingTickId != null)
                {
                    await client.Edit_MessageTextWithButtons_Async(existingTickId.Value, Build_HoldReceiptText(heldAlready), releaseButton, cancellationToken);
                    receiptId = existingTickId;
                }
                else
                {
                    receiptId = await client.Send_MessageWithButtons_Async(message.MessageThreadId, Build_HoldReceiptText(heldAlready), releaseButton, TelegramSendSounds.Silent, cancellationToken);
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

            // The receipt the owner did not get per message, now that the thought is complete. It
            // reacts to the GO message itself — which is the last thing they sent, so the mark
            // lands where they are looking, exactly as it does for an ordinary message.
            await Send_ReceivedAck_Async(client, message, cancellationToken);

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

    /// <summary>The supervisor or ANY open member mid-turn — i.e. the orchestration is alive.</summary>
    bool Is_AnySessionWorking(IOrchestrationSession session)
    {
        var orchFolder = _paths.Get_OrchestrationFolder(session.OrchId);

        if (Is_Working(
                Running.SessionRoles.Supervisor, session.OrchId,
                Running.SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID,
                Path.Combine(orchFolder, UsageTotals_Reader.SESSION_USAGE_FILE)))
            return true;

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            var memberUsage = Path.Combine(
                _paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE);

            if (Is_Working(Running.SessionRoles.Implementer, session.OrchId, member.MemberId, memberUsage))
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
    /// <summary>
    /// Every app-generated entry whose PURPOSE is to get the supervisor working — nudges, ledger
    /// complaints, idle flags, the periodic status. ONE choke point rather than a guard at each call
    /// site: terminal mode has to suppress all of them, and six scattered checks is five chances to
    /// add a seventh site later without noticing it interrupts the owner's meeting.
    /// <para>
    /// Returns whether it wrote. Traffic that is NOT attention-seeking keeps the raw appender: the
    /// presence entries themselves (one of which is the resume signal), /resume, and away mode.
    /// </para>
    /// </summary>
    /// <param name="presence">
    /// The presence the CALLER already decided on. It is a parameter rather than a second
    /// <c>Resolve_Presence</c> because these decisions run on the mirror loop while `/pc` is handled
    /// on the inbound loop, so a flip can land between the two reads: the caller commits its
    /// once-per-spell token on the first value and this method then refuses on the second, spending
    /// the token on an entry nobody receives — the exact defect the callers exist to avoid
    /// (rev-7 P5, 2026-08-13). Passing it also keeps this the single choke point: a new site must
    /// supply the input, and cannot quietly skip the check.
    /// </param>
    bool Append_SupervisorAttention_UnlessMeeting(string orchId, string subject, string body, OwnerPresenceModes presence, Channels.AppEntryAudiences audience = Channels.AppEntryAudiences.Agent)
    {
        if (OwnerPresence_Policy.Suppresses_SupervisorAttention(presence))
            return false;

        // The return value means "an entry is on disk", so a failed append must answer FALSE. It
        // used to be an unconditional true because the append could only throw; now that a throw is
        // caught, saying true would be the same defect this file spent the evening fixing — a
        // caller logging a success for something that never landed.
        if (!Append_AppEntry_Safe(_paths.Get_OwnerChannelFile(orchId), audience, subject, body, DateTime.Now))
            return false;

        Raise_OrchestrationActivity(orchId);
        return true;
    }

    /// <summary>
    /// Where the owner is for this orchestration. GENERAL has no session.json, so its meeting FILE is
    /// the state rather than a projection of it — the owner sits at that terminal too, and refusing
    /// it presence would have left the one session they talk to most as the only one that cannot go
    /// quiet.
    /// </summary>
    OwnerPresenceModes Resolve_Presence(string orchId)
    {
        if (orchId == ChannelDiscovery.GENERAL_ORCH_ID)
        {
            return Status.MeetingFlag_Marker.Is_InMeeting(_paths, orchId)
                ? OwnerPresenceModes.Terminal
                : OwnerPresenceModes.Remote;
        }

        return _store.Get_Session_OrNull(orchId)?.OwnerPresence ?? OwnerPresenceModes.Remote;
    }

    /// <summary>
    /// Makes every meeting flag match its session's presence. Runs on every tick because it is two
    /// file existence checks per orchestration, and because it is what stops a flag outliving the
    /// mode: an app that died mid-meeting clears the flag as soon as it is running again.
    /// </summary>
    void Sync_MeetingFlags()
    {
        foreach (var session in Sessions_ThisTick())
        {
            // A closed orchestration is never in a meeting, whatever its last presence said.
            var presence = session.ClosedUtc == null ? session.OwnerPresence : OwnerPresenceModes.Remote;

            Sync_MeetingFlag_AndReport(session.OrchId, presence);
        }
    }

    /// <summary>
    /// The ONE route every meeting-flag write goes through, so no site can forget to report a
    /// failure. A flag that cannot be deleted silences a watcher permanently, and a session that has
    /// stopped hearing anyone looks identical from outside to one that is simply quiet — so the
    /// failure is named in the log rather than swallowed (decision 21: a guard that cannot evaluate
    /// its predicate says so).
    /// </summary>
    bool Sync_MeetingFlag_AndReport(string orchId, OwnerPresenceModes presence)
    {
        var changed = Status.MeetingFlag_Marker.Sync(_paths, orchId, presence, out var failure);

        if (failure != null)
            _log.Log_Warning(orchId, failure);
        else if (changed)
            _log.Log_Info(orchId, $"Meeting flag {(presence == OwnerPresenceModes.Terminal ? "raised" : "cleared")} — this session's watcher goes {(presence == OwnerPresenceModes.Terminal ? "silent" : "live")}");

        return changed;
    }

    /// <summary>
    /// EVERY append this engine makes goes through here, and the reason is a whole tick rather than
    /// one entry.
    ///
    /// <para>
    /// <c>ChannelAppender</c> throws on failure, and these calls sit on the mirror tick with no
    /// try around them — so one throw escapes to <see cref="Run_MirrorLoop_Async"/>, is logged as the
    /// generic "Mirror tick failed", and skips **the entire rest of that tick**: the poll, the
    /// mirror, the ledger check, the status push, compaction, <c>Persist_BridgeState</c>. A failed
    /// Telegram send loses one alert; a failed local append loses one alert AND everything downstream
    /// of it, with one line in the log that names neither the file nor the operation.
    /// </para>
    /// <para>
    /// AND THE TRIGGER IS REAL, NOT THEORETICAL: <c>File.AppendAllText</c> opens the target
    /// deny-write, so two concurrent appenders do not interleave — the second throws. This app runs
    /// two loops that both append. `imp-9` measured 40 parallel appends producing 13 IOExceptions.
    /// </para>
    /// <para>
    /// This is HALF a fix and must not be read as the whole one. It stops a throw taking the tick
    /// down and names what failed; it does NOT stop the throw. `imp-9`'s <c>ChannelWrite_Lock</c>
    /// removes the trigger inside the app, and the two are needed together: without the lock this
    /// still drops entries (loudly, one at a time), and without this a single unlucky append still
    /// costs a tick. Neither branch is sufficient alone.
    /// </para>
    /// </summary>
    /// <param name="audience">
    /// WHO the entry is for, decided at the point of writing rather than guessed from its wording
    /// later. The mirror routes on this tag, so an Agent entry never reaches the phone.
    /// </param>
    bool Append_AppEntry_Safe(string channelFilePath, Channels.AppEntryAudiences audience, string subject, string body, DateTime nowLocal)
    {
        try
        {
            // THE APPENDER'S OWN ANSWER IS THE ANSWER. Since `imp-9`'s ChannelWrite_Lock landed,
            // Append_AppEntry no longer throws on contention — it returns false when the channel
            // stayed locked for the whole budget. Discarding that and returning an unconditional
            // true would reintroduce the exact defect this wrapper's callers guard against: a memo
            // recorded for an entry that is not on disk, suppressing the retry the next tick owes.
            if (ChannelAppender.Append_AppEntry(channelFilePath, audience, subject, body, nowLocal))
                return true;

            _log.Log_Warning(
                GLOBAL_ORCH_ID,
                $"Channel append could not take the lock — '{subject}' to '{channelFilePath}' was NOT written; nothing is recorded as done, so the next tick retries it");

            return false;
        }
        catch (Exception exception)
        {
            // Decision 21: name WHICH operation failed and on WHICH path. "Append failed" would be
            // the same silence in different words, and this entry is now lost — nothing retries it.
            _log.Log_Warning(
                GLOBAL_ORCH_ID,
                $"Channel append FAILED and the entry is lost — '{subject}' to '{channelFilePath}' — {exception.GetType().Name}: {exception.Message}");

            return false;
        }
    }

    /// <summary>The owner's own words, same protection and the same lost-entry warning.</summary>
    void Append_OwnerEntry_Safe(string channelFilePath, string messageText, DateTime nowLocal)
    {
        try
        {
            ChannelAppender.Append_OwnerEntry(channelFilePath, messageText, nowLocal);
        }
        catch (Exception exception)
        {
            _log.Log_Warning(
                GLOBAL_ORCH_ID,
                $"OWNER message append FAILED and the message is lost — '{channelFilePath}' — {exception.GetType().Name}: {exception.Message}");
        }
    }

    void Raise_AwaitingAnswerFlag(string orchId)
    {
        Status.AwaitingAnswerFlag_Marker.Raise(_paths, orchId, out var failure);

        if (failure != null)
            _log.Log_Warning(orchId, failure);
    }

    void Clear_AwaitingAnswerFlag(string orchId)
    {
        Status.AwaitingAnswerFlag_Marker.Clear(_paths, orchId, out var failure);

        if (failure != null)
            _log.Log_Warning(orchId, failure);
    }

    /// <summary>
    /// What a presence change does to an already-raised block, which is the half `/pc` was missing:
    /// it stopped the NEXT block and left the current one standing for its full ten-minute expiry,
    /// with the owner in front of a session that would not answer them.
    /// </summary>
    void Apply_Presence_ToAwaitingAnswerFlag(string orchId, OwnerPresenceModes presence)
    {
        if (Status.AwaitingAnswerFlag_Marker.Apply_Presence(_paths, orchId, presence, out var failure))
            _log.Log_Info(orchId, "The owner is at the terminal — the question block was lifted so the session can talk to them");

        if (failure != null)
            _log.Log_Warning(orchId, failure);
    }

    /// <summary>
    /// A supervisor left waiting on an owner who never answers must not stay frozen forever — past
    /// the cap it may work again, and the quiet/away machinery covers a genuinely absent owner.
    /// </summary>
    void Expire_StaleAwaitingAnswerFlags()
    {
        foreach (var session in Sessions_ThisTick())
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

    /// <summary>
    /// The owner engaged — whatever they said, the conversation moves again. Returns what it closed,
    /// so the caller can take the keyboards down too; a caller that only needs the state cleared can
    /// ignore it.
    /// </summary>
    /// <summary>
    /// Whether this orchestration already has the owner's attention on a question.
    ///
    /// <para>
    /// PER ORCHESTRATION, NOT GLOBAL. Two orchestrations are two topics on the phone and two
    /// separate conversations; a question in one says nothing about the other, and capping across
    /// them would make a busy project silence a quiet one. Within a topic a short reply cannot be
    /// told apart, which is the whole reason for the cap.
    /// </para>
    /// </summary>
    /// <summary>
    /// Checks an owner-facing entry against <see cref="OwnerMessage_Contract"/> and writes what is
    /// wrong back into the session's own channel.
    ///
    /// <para>
    /// THE MESSAGE STILL GOES. A validator that dropped an owner-facing entry would turn a formatting
    /// fault into a lost answer, which is the worst outcome this system has — so the owner gets it and
    /// the session gets told. The audience is Agent, so the coaching never reaches the phone.
    /// </para>
    /// <para>
    /// ONE COACHING PER FAULT SET, not one per entry: a supervisor that writes the same shape three
    /// times running has been told once, and repeating it would spend the channel this exists to keep
    /// readable. The memory is per orchestration and dies with the process, which is the right
    /// lifetime — a fresh run deserves to be told again.
    /// </para>
    /// </summary>
    void Coach_OnContractFaults(Channels.DiscoveredChannel.IDiscoveredChannel channel, string body)
    {
        var faults = OwnerMessage_Contract.Check(body);

        if (faults.Count == 0)
            return;

        var signature = string.Join(",", faults);

        lock (_ownerStateLock)
        {
            if (_lastContractFaults.TryGetValue(channel.OrchId, out var previous) && previous == signature)
                return;

            _lastContractFaults[channel.OrchId] = signature;
        }

        _log.Log_Info(channel.OrchId, $"owner-message contract: {signature}");

        List<string> lines = [];

        foreach (var fault in faults)
            lines.Add($"- {OwnerMessage_Contract.Describe(fault)}");

        ChannelAppender.Append_AppEntry(
            channel.FilePath,
            AppEntryAudiences.Agent,
            "the entry you just sent the owner breaks the message contract",
            "It reached them anyway — this is not a rejection. Fix the shape on the next one:\n"
            + string.Join("\n", lines),
            DateTime.Now);
    }

    /// <summary>
    /// A question that will not be forwarded, said to the AGENT in full.
    ///
    /// <para>
    /// NOT DEDUPED, unlike <see cref="Coach_OnContractFaults"/>: a repeated formatting fault is
    /// worth saying once, but every refused question is a decision the owner never saw, and the
    /// session is standing there waiting for an answer that cannot come. Audience Agent, so it
    /// never reaches the phone — an alert the owner cannot act on is noise (owner, 2026-08-10).
    /// </para>
    /// </summary>
    void Refuse_Question(IDiscoveredChannel channel, IReadOnlyList<QuestionFaults> faults)
    {
        _log.Log_Warning(channel.OrchId, $"question NOT forwarded — {string.Join(", ", faults)}");

        List<string> lines = [];

        foreach (var fault in faults)
            lines.Add($"- {OwnerQuestion_Contract.Describe(fault)}");

        ChannelAppender.Append_AppEntry(
            channel.FilePath,
            AppEntryAudiences.Agent,
            "your question was NOT sent to the owner — it is incomplete",
            "The body reached them; the question and its buttons did not, so nobody is going to answer it. "
            + "Ask again with every line present:\n"
            + string.Join("\n", lines),
            DateTime.Now);
    }

    /// <summary>
    /// Whether this member's session is driven by the bridge rather than by a spawned shell — the
    /// same question <c>SessionWatchdogModel.Is_PrintRun</c> asks, deliberately phrased the same way
    /// so the two cannot drift apart.
    ///
    /// <para>
    /// BOTH HALVES ARE NEEDED, and the config half is the one that is easy to forget: the state file
    /// says a session WAS registered as bridge-driven, and nothing deletes it when the role is
    /// flipped back to terminal. Answering from the file alone would exempt that slot for ever.
    /// </para>
    /// <para>
    /// READ PER TICK, NEVER CACHED. The runner is owner-configurable and can change under a running
    /// session; a cached answer would keep exempting a slot that stopped being exempt.
    /// </para>
    /// </summary>
    bool Is_BridgeDriven(Running.SessionRoles role, string orchId, string memberId)
    {
        var runner = _configProvider.Get_Current().Runners.Get_ForRole(role).Runner;

        return Running.Runner_Support.Is_BridgeDriven(runner)
            && Running.Runner_Support.Supports(runner, role)
            && Running.PrintSessionState.PrintSessionState_Store.Exists(_paths, role, orchId, memberId);
    }

    /// <summary>
    /// Whether a member is working, answered from what THIS app wrote — the dispatcher's in-flight map
    /// and the turn it recorded when it finished — instead of from a status-line file that a headless
    /// session never produces.
    ///
    /// <para>
    /// UNKNOWN IS RETURNED, NOT SWALLOWED. Every caller has to decide what to do about not knowing,
    /// and the ones that used to get a bare <c>false</c> were the ones that told the owner a working
    /// member was idle. See <see cref="MemberWorking_Decider"/> for the measured incident.
    /// </para>
    /// </summary>
    /// <summary>
    /// "Is it working?" for a surface that must answer yes or no. Asks what the app knows first and
    /// falls back to the status-line probe only when it knows nothing — which is the right answer for
    /// a terminal-run session, and no worse than today's for anything else.
    ///
    /// <para>
    /// A SURFACE THAT CAN SAY NOTHING SHOULD USE <see cref="Resolve_MemberWorking"/> DIRECTLY and
    /// render Unknown as an omission. This overload exists for the lines whose grammar has only two
    /// branches; it is the smaller half of the fix, not the whole of it.
    /// </para>
    /// </summary>
    bool Is_Working(Running.SessionRoles role, string orchId, string memberId, string usageFilePath)
    {
        return Resolve_MemberWorking(role, orchId, memberId) switch
        {
            // QUEUED COUNTS AS OCCUPIED here: this answer feeds "may it be disturbed", and a queued
            // session can no more read a nudge than a running one. What it must NOT feed is the
            // wording — the narration and the status text ask Resolve_MemberWorking themselves.
            WorkingVerdicts.Working or WorkingVerdicts.Queued => true,
            WorkingVerdicts.Idle => false,
            _ => SessionActivity_Probe.Is_MidTurn(usageFilePath),
        };
    }

    WorkingVerdicts Resolve_MemberWorking(Running.SessionRoles role, string orchId, string memberId)
    {
        if (!Is_BridgeDriven(role, orchId, memberId))
            return WorkingVerdicts.Unknown;

        var stateFile = Running.PrintSessionState.PrintSessionState_Store.Get_StateFile(_paths, role, orchId, memberId);
        var state = Running.PrintSessionState.PrintSessionState_Store.Read_OrNull(stateFile);

        DateTime? lastTurnEndedUtc = state == null || state.ExecutedTurns.Count == 0
            ? null
            : state.ExecutedTurns[^1].EndedUtc;

        return MemberWorking_Decider.Decide(
            state != null,
            _printTurns.Is_TurnInFlight(orchId, memberId),
            _printTurns.Is_TurnQueued(orchId, memberId),
            lastTurnEndedUtc,
            _clock.UtcNow);
    }

    /// <summary>
    /// The open questions of this topic asked BEFORE the owner last replied in words — the ones a
    /// newer question from the same asker will close as superseded. Empty when the owner has not
    /// spoken since the oldest of them: two parallel questions with no reply between stay open.
    /// </summary>
    IReadOnlyList<OpenQuestionRecord> Find_QuestionsToSupersede(string orchId)
    {
        lock (_ownerStateLock)
        {
            if (!_ownerRepliedInWordsUtcByOrchId.TryGetValue(orchId, out var repliedUtc))
                return [];

            return [.. _openQuestions.Values.Where(question => question.OrchId == orchId && question.AskedUtc < repliedUtc)];
        }
    }

    /// <summary>
    /// CLOSES THE OLDER QUESTIONS THE OWNER HAD ALREADY REPLIED TO IN WORDS, now that the asker has
    /// moved on to a new one. The same three steps a tap takes — out of the registry with a recorded
    /// reason, buttons consumed so a late tap is refused rather than routed as a stale answer, the
    /// message rewritten (under the rate-limit retry, like every owner-facing rewrite) — plus the
    /// read-back it may have been waiting for. Then the asker is told, once, in its own channel.
    /// </summary>
    async Task Supersede_OlderQuestions_Async(
        Channels.DiscoveredChannel.IDiscoveredChannel channel,
        IReadOnlyList<OpenQuestionRecord> toSupersede,
        CancellationToken cancellationToken)
    {
        if (toSupersede.Count == 0 || _telegramClient == null)
            return;

        List<OpenQuestionRecord> superseded = [];

        lock (_ownerStateLock)
        {
            foreach (var question in toSupersede)
            {
                // Re-checked under the lock: a tap or a typed answer may have closed it meanwhile.
                if (!_openQuestions.Remove(question.MessageId))
                    continue;

                Note_QuestionClosed(question.MessageId, QuestionClosure_Wording.SUPERSEDED);
                _pendingConfirmations.RemoveAll(confirmation => confirmation.MessageId == question.MessageId);
                superseded.Add(question);
            }
        }

        if (superseded.Count == 0)
            return;

        lock (_buttonLock)
        {
            foreach (var question in superseded)
            {
                List<string> groupKeys = [.. _buttonOptions.Where(pair => pair.Value.GroupId == question.ButtonGroupId).Select(pair => pair.Key)];

                foreach (var key in groupKeys)
                    _buttonOptions.Remove(key);
            }
        }

        Persist_EngineState();

        _log.Log_Info(
            channel.OrchId,
            $"{superseded.Count} older open question(s) superseded — the owner had replied in words and a newer question followed");

        ChannelAppender.Append_AppEntry(
            channel.FilePath,
            AppEntryAudiences.Agent,
            "your earlier open question was superseded by this one",
            $"The owner had replied in words while {(superseded.Count == 1 ? "an earlier question of yours was" : $"{superseded.Count} earlier questions of yours were")} still open, "
            + "and you have now asked a new one — so the earlier one(s) are closed as superseded and say so on the phone. "
            + "If one of them still needs a decision, ask it again.",
            DateTime.Now);

        foreach (var question in superseded)
        {
            await Rewrite_AnsweredQuestion_WithRetry_Async(
                _telegramClient, question.MessageId,
                QuestionPrompt_Builder.Build_SupersededText(question.Text),
                cancellationToken);
        }
    }

    bool Would_BeASecondOpenQuestion(string orchId)
    {
        lock (_ownerStateLock)
            return _openQuestions.Values.Any(question => question.OrchId == orchId);
    }

    /// <summary>
    /// Remembers WHAT closed a question. Callers hold <c>_ownerStateLock</c> — it is written at the
    /// same instant as the removal it explains, because a reason recorded a few lines later is a
    /// reason that can be missed by an early return.
    /// </summary>
    void Note_QuestionClosed(long messageId, string reason)
    {
        if (!_closedQuestionReasons.ContainsKey(messageId))
            _closedQuestionOrder.Enqueue(messageId);

        _closedQuestionReasons[messageId] = reason;

        while (_closedQuestionOrder.Count > CLOSED_QUESTION_MEMORY)
            _closedQuestionReasons.Remove(_closedQuestionOrder.Dequeue());
    }

    /// <summary>
    /// Whether that question is still open, and if it is not, what closed it — read as one pair
    /// under one lock, so the two halves cannot describe two different instants.
    /// </summary>
    (bool StillOpen, string? Reason) Read_QuestionClosure(long? messageId)
    {
        if (messageId == null)
            return (false, null);

        lock (_ownerStateLock)
        {
            if (_openQuestions.ContainsKey(messageId.Value))
                return (true, null);

            return (false, _closedQuestionReasons.TryGetValue(messageId.Value, out var reason) ? reason : null);
        }
    }

    List<(long MessageId, long ButtonGroupId, string QuestionText)> Clear_OpenQuestions(string orchId)
    {
        List<(long MessageId, long ButtonGroupId, string QuestionText)> answered = [];

        lock (_ownerStateLock)
        {
            foreach (var pair in _openQuestions)
            {
                if (pair.Value.OrchId == orchId)
                    answered.Add((pair.Key, pair.Value.ButtonGroupId, pair.Value.Text));
            }

            foreach (var question in answered)
            {
                _openQuestions.Remove(question.MessageId);
                Note_QuestionClosed(question.MessageId, QuestionClosure_Wording.TYPED_ANSWER);
            }
        }

        return answered;
    }

    /// <summary>
    /// AN ANSWER IS AN ANSWER, WHICHEVER WAY IT ARRIVED — so a typed reply closes the question on
    /// the phone exactly as a tap does.
    ///
    /// Only the tap path ever took a keyboard down. Answer the same question in writing — which the
    /// owner does whenever the reply needs more than a label — and the buttons stayed live under a
    /// question that was already settled. Two things followed, and the owner reported the second:
    /// the topic still READ as an open question, and a later tap on those still-live buttons
    /// re-entered the tap handler and injected the tapped label as a SECOND owner message,
    /// contradicting the answer they had actually given.
    ///
    /// THE RECORD MATTERS AS MUCH AS THE KEYBOARD, and leaving it out was this fix's first miss. A
    /// tap rewrites its question to carry the choice underneath; removing the buttons alone still
    /// left the owner scrolling back to a question with no sign it had been answered, which is the
    /// half they actually reported. So the typed path writes the same record, with their own words
    /// in place of a label.
    ///
    /// BEST-EFFORT ON PURPOSE, AND IN THAT ORDER. The state is already cleared by the time this
    /// runs; a keyboard that cannot be removed is a cosmetic residue, and it must never take the
    /// owner's message down with it. The edit is attempted first because it removes the keyboard as
    /// a side effect — Edit_MessageText_Async deliberately sends no reply_markup — and a failed edit
    /// falls back to removing the buttons alone, exactly as the tap path does: the record is nice, a
    /// live keyboard on an answered question is a bug.
    ///
    /// AN ANSWER BELONGS TO ITS QUESTION, and the old rule here did not believe that. It was stated
    /// in this summary as <i>any owner message answers whatever was pending</i>, and it swept the
    /// whole orchestration on every inbound message. The owner reported both halves of the damage:
    /// their own question "A che punto siamo?" filed as `✅ answered:` under a merge question, and
    /// one reply closing four questions at once with the same words written under each.
    ///
    /// <see cref="AnswerBinding_Decider"/> now decides, and it declines far more often than it
    /// binds. What it declines stays OPEN — buttons live, glyph on, deadline and reminder still
    /// running, still listed in <c>/pending</c> — because a question nobody answered should look
    /// like a question nobody answered.
    ///
    /// The TAPPED question is not in this list: <see cref="Handle_CallbackTap_Async"/> removes its
    /// own entry before routing, and consumes its own group. It used to close every OTHER question
    /// in the orchestration on the way past, which is the same defect wearing a different hat; the
    /// decider stops that too, since a tap echo arriving with two questions still open reads as
    /// ambiguous rather than as an answer to both.
    /// </summary>
    async Task Close_AnsweredQuestions_Async(string orchId, string answerText, CancellationToken cancellationToken)
    {
        int openCount;

        // OPEN IS BINDABLE AGAIN, and there is no longer a third state between them. A question the
        // owner asked to talk about used to stay open-but-not-bindable; that tap now closes it, so
        // the count and the removal below read the same registry with the same rule — which is what
        // stops a decider and a remover from agreeing only by coincidence.
        lock (_ownerStateLock)
            openCount = _openQuestions.Values.Count(question => question.OrchId == orchId);

        var binding = AnswerBinding_Decider.Decide(openCount, answerText);

        if (!AnswerBinding_Decider.Binds(binding))
        {
            // WRITTEN DOWN EVERY TIME. A question that stays open because of a rule is a fact the
            // owner may ask about later, and an unexplained open question is indistinguishable from
            // the defect this replaced.
            if (openCount > 0)
                _log.Log_Info(orchId, AnswerBinding_Decider.Describe(binding, openCount));

            return;
        }

        var answered = Clear_OpenQuestions(orchId);

        if (answered.Count == 0)
            return;

        lock (_buttonLock)
        {
            List<string> staleTickets = [.. _buttonOptions
                .Where(pair => answered.Any(question => question.ButtonGroupId == pair.Value.GroupId))
                .Select(pair => pair.Key)];

            foreach (var ticket in staleTickets)
                _buttonOptions.Remove(ticket);
        }

        // The question is closed and its keyboard is dead in state; without this a restart brings
        // both back and the owner can tap an answer they have already given in words.
        Persist_EngineState();

        if (_telegramClient == null)
            return;

        foreach (var question in answered)
            await Record_AnsweredQuestion_BestEffort_Async(question, answerText, cancellationToken);
    }

    async Task Record_AnsweredQuestion_BestEffort_Async(
        (long MessageId, long ButtonGroupId, string QuestionText) question,
        string answerText,
        CancellationToken cancellationToken)
    {
        var client = _telegramClient;

        if (client == null)
            return;

        // The question is already out of _openQuestions by the time this runs, so its text comes
        // from what the clear handed back — there is nothing left to look up.
        var questionText = question.QuestionText;

        if (questionText.Length == 0)
        {
            await Remove_Buttons_BestEffort_Async(client, question.MessageId, cancellationToken);
            return;
        }

        await Rewrite_AnsweredQuestion_WithRetry_Async(
            client, question.MessageId,
            QuestionPrompt_Builder.Build_AnsweredByMessageText(questionText, answerText),
            cancellationToken);
    }

    /// <summary>
    /// Rewrites an answered question's message — and, when Telegram answers with a rate limit,
    /// TRIES AGAIN after the wait Telegram named, off the inbound loop, up to
    /// <see cref="RateLimitedRetry_Policy.MAX_ATTEMPTS"/>.
    ///
    /// <para>
    /// The first attempt is inline, so the common case changes nothing. On a 429 the retries run on
    /// a detached task: the tap has already been routed by the time this matters, and holding the
    /// inbound batch for half a minute would delay every other tap and message in it. Only when the
    /// last attempt fails does the old fallback run — remove the keyboard, best effort — and one
    /// warning names how long was waited. Measured 2026-09-10 21:11:40: both the rewrite and the
    /// keyboard removal took ONE 429 each and gave up, and the owner's phone kept a live keyboard on a
    /// question they had just answered.
    /// </para>
    /// </summary>
    async Task Rewrite_AnsweredQuestion_WithRetry_Async(ITelegramApiClient client, long messageId, string markdown, CancellationToken cancellationToken)
    {
        Exception firstFailure;

        try
        {
            await TelegramProse_Sender.Edit_Async(client, _log, GLOBAL_ORCH_ID, messageId, markdown, cancellationToken);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            firstFailure = ex;
        }

        var wait = RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(firstFailure, attemptsMade: 1, DateTime.UtcNow);

        if (wait == null)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Answered-question edit failed: {firstFailure.Message}");

            // The record is nice; a live keyboard on an already-answered question is a BUG,
            // so fall back to at least removing it.
            await Remove_Buttons_BestEffort_Async(client, messageId, cancellationToken);
            return;
        }

        // THE CLOCK IS PASSED because the failure may now be a shut door rather than a refusal:
        // TelegramHeldException carries an ABSOLUTE deadline (the window some other surface was
        // given on this same message), and turning that into a wait needs to know what time it is.
        // Without it the policy would read a held door as "not a rate limit" and give up — the tap
        // would go back to leaving a live keyboard on an answered question, which is the exact
        // defect d22240f fixed.
        _log.Log_Info(GLOBAL_ORCH_ID, $"Answered-question edit of message {messageId} was rate-limited — retrying in {wait.Value.TotalSeconds:0} s, off the inbound loop");

        // DETACHED ON PURPOSE, and with the flow suppressed like every other detached task in this
        // engine: it writes to Telegram only, never to a channel.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => Retry_AnsweredQuestionEdit_Async(client, messageId, markdown, wait.Value, cancellationToken), CancellationToken.None);
        }
    }

    async Task Retry_AnsweredQuestionEdit_Async(ITelegramApiClient client, long messageId, string markdown, TimeSpan firstWait, CancellationToken cancellationToken)
    {
        var wait = firstWait;
        var attemptsMade = 1;
        var waitedFor = TimeSpan.Zero;

        try
        {
            while (true)
            {
                await Task.Delay(wait, cancellationToken);
                waitedFor += wait;
                attemptsMade++;

                try
                {
                    await TelegramProse_Sender.Edit_Async(client, _log, GLOBAL_ORCH_ID, messageId, markdown, cancellationToken);
                    _log.Log_Info(GLOBAL_ORCH_ID, $"Answered-question edit of message {messageId} landed on attempt {attemptsMade} after waiting {waitedFor.TotalSeconds:0} s");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var next = RateLimitedRetry_Policy.Wait_BeforeNextAttempt_OrNull(ex, attemptsMade, DateTime.UtcNow);

                    if (next == null)
                    {
                        _log.Log_Warning(GLOBAL_ORCH_ID, $"Answered-question edit failed after {attemptsMade} attempts and {waitedFor.TotalSeconds:0} s of waiting: {ex.Message}");
                        await Remove_Buttons_BestEffort_Async(client, messageId, cancellationToken);
                        return;
                    }

                    wait = next.Value;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown mid-wait: the rewrite is cosmetic, the answer was routed long ago.
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
    /// <summary>
    /// The ⏸/▶ button under a receipt — the owner's one-tap WAIT and GO.
    ///
    /// It does exactly what the typed words do, by calling the same buffer, because two ways to hold
    /// that behave differently is worse than one way that is slow to type. What the button adds is
    /// the seconds: typing WAIT loses a race with the aggregation window often enough that the owner
    /// noticed, and a tap is the difference between catching the message and not.
    ///
    /// The button message ITSELF becomes the receipt — edited in place, ⏸ Wait to ▶ GO and back —
    /// so a conversation does not grow a new message per hold. Repeats edit, they never stack
    /// (decision 14).
    ///
    /// Returns false for a tap that is not ours, so an option answer falls through untouched.
    /// </summary>
    async Task<bool> Try_HandleHoldTap_Async(ITelegramApiClient client, ITelegramCallbackTap tap, CancellationToken cancellationToken)
    {
        var parsed = HoldButton_Data.Parse_OrNull(tap.Data);

        if (parsed == null)
            return false;

        var (action, threadId) = parsed.Value;
        var targetKey = Resolve_TargetChannelFile_OrNull(threadId);

        // ANSWERED FIRST, WHATEVER HAPPENS NEXT: an unanswered callback leaves the button spinning
        // on the phone, which reads as the app being dead at the exact moment they are asking it to
        // stop something.
        await Answer_CallbackTap_BestEffort_Async(
            client, tap.CallbackQueryId, targetKey == null ? "no orchestration in this topic" : "✓", cancellationToken);

        if (targetKey == null)
            return true;

        if (action == HoldButtonActions.Hold)
        {
            _ownerDeliveryBuffer.Hold(targetKey, DateTime.UtcNow);
            _log.Log_Info(Describe_ThreadOrch(threadId), "Owner tapped WAIT — delivery held until GO");
        }
        else
        {
            _ownerDeliveryBuffer.Release(targetKey);
            _log.Log_Info(Describe_ThreadOrch(threadId), "Owner tapped GO — releasing held messages");
        }

        var heldCount = _ownerDeliveryBuffer.Count_Pending(targetKey);

        // THE STATUS LINE IS NOT A RECEIPT, AND A TAP ON IT MUST NOT REWRITE IT (brief D).
        //
        // The toggle now also lives on PULSE, and this handler's whole job used to be "rewrite the
        // message the button was tapped on" — which was safe while that message was a ✓ receipt
        // that existed to be rewritten. Applied to the status line it is destructive: the text
        // becomes "⏸ holding…" and the four button rows collapse to a single ▶ GO, so /pending,
        // /left, /tail sup, /limits, /merge and /close vanish from the topic on one tap. And it
        // does not heal: the status refresh only repaints when its own content changes.
        //
        // So the status line repaints itself through Refresh_TopicStatusLines_Async — which now
        // notices a button-only change — and is never touched from here. Only a real receipt is.
        var tappedTheStatusLine = tap.MessageId != null
            && _store.Find_ByTelegramTopicId_OrNull(threadId ?? 0)?.StatusLineMessageId == tap.MessageId;

        if (tap.MessageId != null && !tappedTheStatusLine)
        {
            lock (_ownerStateLock)
            {
                if (action == HoldButtonActions.Hold)
                    _holdReceipts[targetKey] = new HoldReceipt { MessageId = tap.MessageId, HeldCount = heldCount };
                else
                    _holdReceipts.Remove(targetKey);
            }

            await Rewrite_HoldButtonMessage_BestEffort_Async(client, tap.MessageId.Value, action, threadId, heldCount, cancellationToken);
        }
        else if (action == HoldButtonActions.Go)
        {
            // A hold entered from the bar has no receipt message, so there is nothing to forget
            // except the entry itself — left behind, it would make Update_HoldReceipt_Async keep
            // rewriting a message that no longer represents a hold.
            lock (_ownerStateLock)
                _holdReceipts.Remove(targetKey);
        }

        // GO means "I am done typing", so the wait for the next mirror tick — up to 2 s — is dead
        // time. Same reasoning as the typed GO, which flushes immediately for exactly this reason.
        if (action == HoldButtonActions.Go)
            await Flush_OwnerDeliveries_Async(cancellationToken);

        return true;
    }

    async Task Rewrite_HoldButtonMessage_BestEffort_Async(
        ITelegramApiClient client, long messageId, HoldButtonActions action, long? threadId, int heldCount, CancellationToken cancellationToken)
    {
        // After a HOLD the button becomes the release; after a GO it goes back to offering a hold,
        // because the next message is already on its way and they may want to stop that one too.
        var nextAction = action == HoldButtonActions.Hold ? HoldButtonActions.Go : HoldButtonActions.Hold;
        var nextLabel = action == HoldButtonActions.Hold ? HoldButton_Data.GO_LABEL : HoldButton_Data.HOLD_LABEL;
        var text = action == HoldButtonActions.Hold ? Build_HoldReceiptText(heldCount) : "✓";

        try
        {
            await client.Edit_MessageTextWithButtons_Async(
                messageId, text, [(HoldButton_Data.Build(nextAction, threadId), nextLabel)], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The hold itself already happened; a stale button label is cosmetic beside that.
            _log.Log_Warning(Describe_ThreadOrch(threadId), $"Hold button not rewritten: {ex.Message}");
        }
    }

    /// <summary>
    /// A tap on the topic's standing command bar. Returns false when the data is not ours, so an
    /// unrecognised tap falls through to the handlers below exactly as it always has.
    ///
    /// It calls the SAME method the typed command calls. A second implementation of /show that only
    /// buttons could reach is precisely how a button and the command it names come to mean different
    /// things — the hazard this repo has already paid for once with /progress and /left.
    /// </summary>
    async Task<bool> Try_HandleTopicCommandTap_Async(ITelegramApiClient client, ITelegramCallbackTap tap, CancellationToken cancellationToken)
    {
        var parsed = Telegram.TopicCommandButtons.Parse_OrNull(tap.Data);

        if (parsed == null)
            return false;

        // ANSWERED BEFORE THE WORK, not after. /screen maximises a window, waits for it to paint and
        // uploads a picture — the better part of a second — and an unanswered callback leaves the
        // button spinning on the owner's phone for the whole of it.
        await Answer_CallbackTap_BestEffort_Async(client, tap.CallbackQueryId, "✓", cancellationToken);

        // A TAP IS THE OWNER SPEAKING - the close-confirmation tap already treats it that way, and
        // Note_OwnerSpoke_AndWasAway's own doc says so in as many words ("including tapping a
        // button"). This bar was the one tap path that never said it, which went from harmless to
        // wrong the moment /pc got a button: the owner would tap "I am at my pc" and stay marked
        // away until they typed something into Telegram, which is the exact chore /pc exists to
        // spare them.
        if (Note_OwnerSpoke_AndWasAway())
            await Exit_AwayMode_Async(cancellationToken);

        // The id encoded in the button is the topic the bar was drawn FOR; the tap's own is the
        // fallback for a button minted before that was carried.
        var threadId = parsed.Value.MessageThreadId != 0 ? parsed.Value.MessageThreadId : tap.MessageThreadId;

        switch (parsed.Value.Command)
        {
            case "show":
                await Show_SessionWindow_Async(client, threadId, cancellationToken);
                return true;

            case "screen":
                await Send_SessionScreenshot_Async(client, threadId, cancellationToken);
                return true;

            // THE FOUR THE OWNER PUT ON THE BAR (2026-09-09). Each already existed as a TYPED
            // command; what was missing was a tap route to it, and EveryTopicButtonIsWiredTests is
            // the thing that noticed — it walks TopicCommandButtons.Commands and demands a case here
            // for every button the bar renders.
            case "pending":
                await Send_PendingDecisions_Async(client, threadId, cancellationToken);
                return true;

            case "left":
                await Send_ProgressReport_Async(client, threadId, "left", cancellationToken);
                return true;

            // THE VERB CARRIES ITS ARGUMENT, space included: /tail takes a member id, and the bar
            // offers the one reading the owner actually wants — the supervisor's own turn log. The
            // typed command parses the argument out of the message; here it is part of the payload,
            // so the same handler is reached with the same words.
            case "tail sup":
                await Send_TurnLog_Async(client, threadId, "tail", "/tail sup", cancellationToken);
                return true;

            case "limits":
                await Send_LimitsReport_Async(client, threadId, cancellationToken);
                return true;

            case "merge":
                await Ask_SessionToMerge_Async(client, threadId, cancellationToken);
                return true;

            case "test":
                await Toggle_AwaitingTest_Async(client, threadId, cancellationToken);
                return true;

            case "pc":
                // A TAPPED /pc MUST END TERMINAL MODE ELSEWHERE TOO, and this is the one line the
                // typed command does not hand over. The inbound loop calls this for every owner
                // MESSAGE; taps are drained further down and never meet it, so without it, tapping
                // /pc here would leave another topic's 💻 lit - and "nobody sits at two terminals"
                // is the whole reason the flip exists.
                Flip_OtherTerminals_IfPresenceCommand(threadId, isPresenceCommandItself: true);

                // NOT deferred the way the typed command is. That deferral keeps the toggle from
                // racing the ✓ acks of the batch it arrived in; this tap was acknowledged above,
                // before the switch, so there is nothing left to race.
                await Apply_PresenceCommand_Async(client, threadId, cancellationToken);
                return true;

            // GENERAL'S THREE, unrendered until 2026-09-10 and unhandled with them. A tap on any of
            // these used to reach the `default:` arm below and be told the button came from an older
            // build — while the bar it came from had never been drawn by any build at all.
            case "summary":
                // ROUTED, not answered here. /summary is the one General command whose answer is a
                // model's to write: the general supervisor reads every channel and says what is going
                // on. The app knows the counts (that is /left) and not the meaning.
                await Route_OwnerMessage_Async(
                    Build_GeneralCommandMessage(tap.UpdateId, tap.MessageId, 0, 0, GENERAL_SUMMARY_REQUEST),
                    cancellationToken);
                return true;

            case "resume":
                await Resume_AllSessions_Async(client, threadId, cancellationToken);
                return true;

            case "dnd_all":
                // THE LITERAL COMMAND STRING, because that is the argument this method takes: it
                // derives the wanted mode from the "mute"/"dnd" stem and the app-wide scope from the
                // "_all" suffix. Passing the string the button already carries keeps the tap and the
                // typed command on one implementation.
                //
                // NOT deferred the way the typed command is. That deferral keeps the toggle from
                // racing the ✓ acks of the batch it arrived in — this tap was acknowledged above,
                // before the switch, so there is nothing left to race. Same reasoning as /pc.
                await Apply_ModeCommand_Async(client, "dnd_all", threadId, cancellationToken);
                return true;

            case "close":
                // Parks the same request a session would, so a MISTAP cannot end an orchestration:
                // the owner still confirms with ✅/✋. That is why this calls the command's own
                // method rather than the close path underneath it - a second route that skipped the
                // prompt is exactly the drift this shared button set exists to prevent.
                await Request_Close_FromCommand_Async(client, threadId, cancellationToken);
                return true;

            // /refresh NO LONGER RENDERS A BUTTON (the owner replaced it with /pc and /close on
            // 2026-09-07), and this case deliberately stays. The status line is REPOSTED when it
            // gets buried, so superseded pulse messages keep their old keyboard on the owner's
            // phone indefinitely - tapping one should still refresh the topic rather than be told
            // the button is from an older build, which is what the default arm below would say.
            case "refresh":
                await Refresh_TopicName_Async(client, threadId, cancellationToken);
                return true;

            default:
                // Our prefix, a command this build does not render: a bar left on a message by an
                // older build. Swallowing it silently would be a button that does nothing forever.
                _log.Log_Warning(GLOBAL_ORCH_ID, $"Topic command button '{parsed.Value.Command}' is not one this build offers — ignored.");
                await Send_DirectReply_BestEffort_Async(client, threadId, $"That button (/{parsed.Value.Command}) is from an older version of the app — send the command instead.", cancellationToken);
                return true;
        }
    }

    async Task Answer_CallbackTap_BestEffort_Async(
        ITelegramApiClient client, string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        try
        {
            await client.Answer_CallbackQuery_Async(callbackQueryId, text, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"answerCallbackQuery failed: {ex.Message}");
        }
    }

    /// <summary>The log scope for a tap, which carries a topic and no message to read one from.</summary>
    string Describe_ThreadOrch(long? messageThreadId)
    {
        if (messageThreadId == null)
            return ChannelDiscovery.GENERAL_ORCH_ID;

        return _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value)?.OrchId ?? GLOBAL_ORCH_ID;
    }

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
            // WITH the release button: a plain edit sends no reply_markup, which Telegram reads as
            // "remove the keyboard" — so counting up the held messages would silently take away the
            // ▶ GO the owner is meant to press.
            await client.Edit_MessageTextWithButtons_Async(
                receipt.MessageId.Value,
                Build_HoldReceiptText(receipt.HeldCount),
                [(HoldButton_Data.Build(HoldButtonActions.Go, message.MessageThreadId), HoldButton_Data.GO_LABEL)],
                cancellationToken);
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
        return Resolve_TargetChannelFile_OrNull(message.MessageThreadId);
    }

    /// <summary>
    /// The same question from a THREAD ID alone, for the hold button: a callback tap arrives with a
    /// topic and no message, so it cannot go through the overload above. One implementation, because
    /// a second spelling of "which channel is this topic" is how the button would come to hold a
    /// different channel than the word does.
    /// </summary>
    string? Resolve_TargetChannelFile_OrNull(long? messageThreadId)
    {
        if (messageThreadId == null)
            return _paths.GeneralChannelFile;

        var session = _store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value);

        return session == null ? null : _paths.Get_OwnerChannelFile(session.OrchId);
    }

    /// <summary>
    /// The log scope for an owner message, which is the topic question with the thread id already in
    /// hand. A THIN ADAPTER, not a second implementation: this method and the one that used to sit
    /// beside the ledger reports answered the identical question forty lines apart and DISAGREED on
    /// the General branch — one returned "general", the other the empty string, and the empty one
    /// silently lost its diagnostic. Rule 12 is what makes that possible; one body is what closes it.
    /// </summary>
    string Describe_MessageOrch(Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message)
    {
        return Resolve_LogScope_ForTopic(message.MessageThreadId);
    }

    /// <summary>
    /// IT REPORTS WHAT BECAME OF THE MESSAGE, because the caller sends the ✓ and the ✓ is a claim.
    /// This returned void, so `Send_ReceivedAck_Async` ran after it whatever it had done — a message
    /// into an unknown topic was dropped with a warning and ticked anyway, and one into a closed
    /// orchestration was written into a channel nobody tails and ticked the same way.
    ///
    /// <para>
    /// THE CLOSED CHECK IS HERE AND NOT IN THE STORE, deliberately. `Find_ByTelegramTopicId_OrNull`
    /// has some thirty call sites — reports, glyph sweeps, `/clear`, the close flow itself — and
    /// several of them legitimately want a session that is closed. This is the one place that WRITES
    /// the owner's words into a channel file, so it is the one place the question "is anyone still
    /// reading this?" belongs.
    /// </para>
    /// </summary>
    async Task<OwnerRouteOutcomes> Route_OwnerMessage_Async(Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message, CancellationToken cancellationToken)
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
                return OwnerRouteOutcomes.UnknownTopic;
            }

            // A CLOSED ORCHESTRATION'S CHANNEL IS AN ARCHIVE. Its tailers are stopped and its
            // terminals are gone, so an append here is a write nobody will ever read — and the
            // topic can outlive the close, because the delete that should remove it is
            // fire-and-forget (brief E1). The owner is told instead.
            if (session.ClosedUtc != null)
            {
                _log.Log_Warning(
                    session.OrchId,
                    $"Owner message arrived in the topic of a CLOSED orchestration (closed {session.ClosedUtc:yyyy-MM-dd HH:mm}Z) — not appended, and the owner was told");

                return OwnerRouteOutcomes.ClosedOrchestration;
            }

            orchId = session.OrchId;
            channelFile = _paths.Get_OwnerChannelFile(orchId);

            Wake_DoneTopic_IfNeeded(session);
        }

        string segmentText;

        if (message.VoiceFileId != null)
        {
            var voiceText = await Build_VoiceEntryText_OrNull_Async(message, channelFile, orchId, cancellationToken);

            // Not configured or failed — the owner already got a direct reply; nothing to route,
            // and NO ✓ either: a tick under "voice failed — please type it" says the opposite.
            if (voiceText == null)
                return OwnerRouteOutcomes.AnsweredDirectly;

            segmentText = voiceText;
        }
        else if (message.PhotoFileId != null)
        {
            segmentText = await Build_PhotoEntryText_Async(message, channelFile, orchId, cancellationToken);
        }
        else if (message.Document != null)
        {
            segmentText = await Build_DocumentEntryText_Async(message, channelFile, orchId, cancellationToken);
        }
        else
        {
            segmentText = message.Text;
        }

        // AFTER the three paths, not inside one: the owner can reply with text, a voice note or a
        // photo, and the quote is what they were pointing at in every case.
        segmentText = OwnerReplyContext_Formatter.Prepend_OrSame(segmentText, message.ReplyToText);

        lock (_deliveryLock)
        {
            _deliveryTargets[channelFile] = (orchId, message.MessageThreadId);
        }

        // Any word from the owner — including a button tap — means they are back.
        if (Note_OwnerSpoke_AndWasAway())
            await Exit_AwayMode_Async(cancellationToken);

        // ...and it answers whatever was asked, whether or not it answers it. The conversation
        // unfreezes and everything the supervisor queued behind the question flows now — and the
        // keyboard comes down with it, so a question answered IN WRITING is as closed on the phone as
        // one answered by tapping.
        //
        // EXCEPT WHEN THE APP WROTE THE TEXT. A tap, a released read-back and an applied default all
        // arrive here as owner messages, and the binding below reads "exactly one question open" as
        // "this answers it". The tapped question is removed before routing, so one OTHER question
        // still open is the ordinary case, not a rare one — and on 2026-09-09 that is precisely what
        // stamped a talk request onto an orphaned-processes question nobody ever decided.
        if (!message.IsAppComposed)
        {
            await Close_AnsweredQuestions_Async(orchId, segmentText, cancellationToken);

            // Stamped whether or not the reply bound: what matters later is that the owner SPOKE
            // after a question was asked, and a bound reply leaves nothing open to supersede anyway.
            lock (_ownerStateLock)
                _ownerRepliedInWordsUtcByOrchId[orchId] = _clock.UtcNow;
        }
        Clear_AwaitingAnswerFlag(orchId);

        // THE STALL ALERT'S MEMORY IS SETTLED BY THE OWNER SPEAKING, and forgetting it here is what
        // keeps "once per question" from becoming "once per WORDING, for the life of the process".
        //
        // The key is a hash of what the question said (2026-09-10), which fixed the duplicate-`[n]`
        // failures and opened a new one: agents reuse subjects and bodies, so a supervisor asking the
        // BYTE-IDENTICAL question again two hours later hashed to the remembered key and was never
        // alerted about. Keeping the memo across an answer was right for traffic that does not answer
        // — a supervisor's own follow-up must not buy a second ⚠️ about the same debt — and wrong for
        // traffic that does. This is the site that knows the difference: the owner has just spoken
        // into this orchestration, so whatever they owed, they no longer owe.
        lock (_ownerStateLock)
        {
            _stallAlertedQuestionKeyByOrchId.Remove(orchId);
        }

        // The owner is engaged, so nothing is deadlocked — a suppressed entry from before must not
        // surface later, out of context, as if it were still waiting for them.
        lock (_ownerStateLock)
        {

            // Whatever the supervisor says next is the answer to this, and it MUST reach them.
            _ownerAwaitingAnswer.Add(orchId);
        }

        // R1 SURVIVES A RESTART TOO, which it did not before. The flag is what makes the answer to
        // the owner's own question push instead of being re-read as narration, and losing it while
        // an answer was still in flight dropped that answer silently — the exact failure R1 names,
        // reached by closing the app instead of by a failed send.
        Persist_EngineState();

        _ownerDeliveryBuffer.Add_Segment(channelFile, segmentText, DateTime.UtcNow);
        _log.Log_Info(orchId, "Owner message buffered (aggregation window running)");

        return OwnerRouteOutcomes.Routed;
    }

    async Task Flush_OwnerDeliveries_Async(CancellationToken cancellationToken)
    {
        if (!_ownerDeliveryBuffer.Has_PendingDeliveries())
            return;

        // KNOWN DEFECT, NOT FIXED HERE, AND IT BELONGS TO SOMEONE ELSE'S CLASS — do not fix it in
        // passing. `Take_ReadyDeliveries` REMOVES every ready key from the buffer before this loop body
        // runs, so the record of the work is destroyed before the work is known to have succeeded. Any
        // escape from the loop therefore skips the remaining deliveries' `Append_OwnerEntry` below, and
        // those owner messages are LOST rather than delayed — nothing re-delivers them.
        //
        // The catch filter further down closes the escape route this loop had; it does NOT close the
        // drain. That is the "memo that records work as done, moved from before the append to after it
        // succeeded" class, owned by imp-9 across seven sites — and one class fixed by two members in
        // two branches is how a merge grows conflict regions. The same shape sits one line down inside
        // the try, where `_pendingOwnerReplies[...]` is only registered on the success path, so a
        // failed receipt leaves the owner's wait untracked and the receipt frozen on "thinking…".
        foreach (var delivery in _ownerDeliveryBuffer.Take_ReadyDeliveries(DateTime.UtcNow))
        {
            // TAKE_READYDELIVERIES HAS ALREADY EMPTIED THE BUFFER FOR EVERY KEY IN THIS BATCH, so from
            // here the local variables are the only copy of the owner's words. The append's own
            // failure is handled below with a put-back; this wrapper covers the OTHER ways out, which
            // were not — anything that throws between here and the append destroys the text outright,
            // and any escape from the loop destroys every delivery still to come in the batch too.
            try
            {
                await Deliver_OwnerMessage_Async(delivery, cancellationToken);
            }
            catch (Exception exception)
            {
                // The ORIGINAL, never the working copy: a half-processed string becoming the
                // owner's message is worse than a late one, and it would be near-impossible to
                // diagnose from outside.
                _ownerDeliveryBuffer.Restore_Segment(delivery.Key, delivery.Value.Text, delivery.Value.FirstOrdinal);
                _ownerDeliveryBuffer.Release(delivery.Key);

                _log.Log_Error(
                    GLOBAL_ORCH_ID,
                    $"Owner message for '{Path.GetFileName(delivery.Key)}' failed mid-delivery — it is back in the buffer and the next tick retries it",
                    exception);

                // CONTINUE, deliberately, including on cancellation. Rethrowing here is what let one
                // failure take the rest of the batch down with it: every remaining delivery had
                // already been removed from the buffer and would never be re-delivered. Each key is
                // independent, and the put-back above means a cancelled run loses nothing either.
            }
        }
    }

    /// <summary>
    /// One owner message, from the buffer to the supervisor's channel and back to the owner as a
    /// receipt. Throws on any failure that is not the append's own — the caller puts the message back.
    /// </summary>
    async Task Deliver_OwnerMessage_Async(KeyValuePair<string, IReadyDelivery> delivery, CancellationToken cancellationToken)
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
                return;
            }
        }

        // WAIT RACES THE AGGREGATION WINDOW, and it was losing. The window is 4 seconds, so a message
        // is usually already TAKEN from the buffer by the time the owner types "wait" — and a take is
        // irreversible, so the hold set a moment later applied to nothing and the message went out
        // anyway. Measured on da-vinci-fintech-suite-6, 2026-08-15: buffered 08:36:47, WAIT accepted
        // 08:36:52, delivered 08:37:04. The owner saw ✓✓ and "thinking…" seconds after their own wait
        // and read it exactly right — "it seems it's already working on what I wrote despite the
        // wait." It was.
        //
        // Re-asked HERE, immediately before the append, because that is the last moment the answer is
        // still true: everything above (target lookup, the append's own preparation) can take
        // seconds. Put back rather than dropped — the segment keeps its ordinal, so it lands in the
        // owner's original order when GO comes.
        //
        // THE HONEST LIMIT: once the append has landed the session may already have read it, and no
        // amount of checking can un-send it. This narrows the window to the append itself; it does
        // not close it.
        if (_ownerDeliveryBuffer.Is_Holding(delivery.Key))
        {
            _ownerDeliveryBuffer.Restore_Segment(delivery.Key, delivery.Value.Text, delivery.Value.FirstOrdinal);

            _log.Log_Info(target.OrchId, "Owner message held mid-delivery — WAIT arrived after it left the buffer; it is back in the buffer until GO");
            return;
        }

        var deliveryText = delivery.Value.Text;

        // Counted BEFORE the owner entry lands, so a later increase can only mean the session
        // answered THIS message. SESSION, not supervisor: a basic orchestration is answered by its
        // solo, and counting supervisors alone made this number unable to rise there at all.
        var ownerAnswerCountBefore = Count_OwnerAnswerEntries(delivery.Key);

        // Take_ReadyDeliveries REMOVED this from the buffer before we got here, so the only copy
        // of the owner's message is the local variable. A failed append that simply fell through
        // would destroy it — which is worse than the collision the lock exists to prevent, and is
        // why "fail the write" cannot mean "drop the write" on this path.
        if (!ChannelAppender.Append_OwnerEntry(delivery.Key, deliveryText, DateTime.Now))
        {
            // Put it back and mark it ready: the owner has already waited out one aggregation
            // window and must not serve a second one for a lock they know nothing about.
            //
            // delivery.Value, THE ORIGINAL — not deliveryText. NOTHING may put an output of the
            // outbound pipeline back into the input side: until rev-9 this put back the string the
            // Italian layer (removed 2026-09-09) had rewritten, so the buffer stopped holding the
            // owner's message and started holding a machine paraphrase of it, re-paraphrased on every
            // subsequent lock. The rule outlives the layer that taught it.
            _ownerDeliveryBuffer.Restore_Segment(delivery.Key, delivery.Value.Text, delivery.Value.FirstOrdinal);
            _ownerDeliveryBuffer.Release(delivery.Key);

            _log.Log_Warning(target.OrchId,
                $"Owner message NOT delivered — '{Path.GetFileName(delivery.Key)}' stayed locked by another writer for the whole budget; it is back in the buffer and the next tick retries it");

            return;
        }

        _log.Log_Info(target.OrchId, "Owner message delivered to the supervisor");
        Raise_OrchestrationActivity(target.OrchId);

        // AN OWNER MESSAGE PUTS THE LEDGER IN DEBT, exactly as a verdict does, and this is the half
        // that was missing (owner, 2026-08-14). They asked for six things over two hours and the bar
        // stayed at 3/3 the whole time, because nothing in this system connects "the owner asked for
        // something" to "the ledger has a line for it".
        //
        // IT IS THE ONLY ROUTE THAT WORKS FOR A SOLO. The verdict arming below fires on a supervisor
        // entry in a SPOKE, and a basic orchestration has no spokes and no verdicts — so a solo could
        // never be flagged for a stale ledger at all, whatever it did. The owner's reading was exact:
        // *"you are just a session like any other. If you failed to upgrade the plan file any other
        // future session also might fail."*
        //
        // THE OBLIGATION IS TO TOUCH PLAN.md, not to invent a task. A message that needs no new work
        // still needs its row in OWNER REQUESTS — which the role commands already mandate "the moment
        // the request arrives" — and that write clears the debt. So there is no case where this asks
        // for something the protocol did not already ask for.
        //
        // GENERAL is excluded: it has no PLAN.md and never had one, so a debt there could never be
        // paid and would block that session for ever.
        if (target.OrchId != ChannelDiscovery.GENERAL_ORCH_ID)
            _ledgerDebtSinceUtc[target.OrchId] = DateTime.UtcNow;

        if (_telegramClient == null || _telegramMuted)
            return;

        try
        {
            // The batch's ✓ becomes "✓✓" — plus a handoff line ONLY when the recipient is busy and
            // the owner needs to know who covers the wait. A FREE recipient gets no words at all:
            // "thinking…" is what the typing bubble says, natively and silently, and
            // Resolve_PendingOwnerReplies_Async keeps it up for as long as the wait lasts (owner,
            // 2026-09-07: every exchange arrived as two status messages plus the answer).
            // ✓ → ✓✓ AND NOTHING ELSE (owner's decision, 2026-09-09). The busy handoff line used to
            // ride the receipt: *"✓✓ · 🔴 Sup: mid-task. Your message is delivered; they pick it up
            // when this turn ends"* — fifteen of them in a single export, the same sentence under
            // every message they sent. It told them nothing they could not see from the typing
            // bubble, and it told them at the one moment they were certainly looking.
            //
            // The busy sentence survives only where it says something NEW: the counting edit after
            // three minutes of waiting, a held message, a handoff. Those are edits of this same
            // tick, decided in Narrate_BusySupervisor_Async, not a second message here.
            var receiptText = "✓✓";

            // IT ONLY EVER EDITS. With no tick to edit, nothing is sent: a fresh message saying
            // "delivered" is precisely the noise this replaces, and the owner watched their own
            // message arrive.
            var carriesInformation = false;

            var (receiptMessageId, receiptWasReaction) = await Publish_DeliveryReceipt_Async(
                _telegramClient, target.ThreadId, receiptText, sendWhenNothingToEdit: carriesInformation, cancellationToken);

            await Show_Typing_BestEffort_Async(_telegramClient, target.ThreadId, cancellationToken);

            // Tracked until the supervisor actually answers — the owner must never be left
            // with a bubble that never resolves into anything.
            lock (_ownerStateLock)
            {
                _pendingOwnerReplies[target.OrchId] = new PendingOwnerReply
                {
                    ThreadId = target.ThreadId,
                    ReceiptMessageId = receiptMessageId,

                    // Publish returns null for BOTH "the reaction landed" and "there was nothing to
                    // edit", so the fact is taken from the receipt path itself rather than inferred
                    // from a null.
                    ReceiptWasReaction = receiptWasReaction,
                    OwnerAnswerCountAtDelivery = ownerAnswerCountBefore,
                    DeliveredUtc = DateTime.UtcNow,
                    Nudged = false,
                };
            }
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        //
        // It composes with this branch's put-back rather than colliding with it: the append has already
        // SUCCEEDED by the time control reaches here, so an escape from this block would have the
        // caller restore a message that was in fact delivered. The filter keeps a timeout local, and
        // leaves only real cancellation — where the in-memory buffer dies with the process anyway — as
        // the route out.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(target.OrchId, $"Delivery receipt send failed: {ex.Message}");
        }
    }



    /// <summary>
    /// The owner's "is it doing anything?" answered the way every chat app answers it — with the
    /// typing bubble, not with a message. Cadenced, best-effort, never a notification: Telegram
    /// clears it on its own after ~5 s or when the next real message lands, so an outage can at
    /// worst leave the bubble absent, never a stale line in the topic.
    /// </summary>
    async Task Show_Typing_BestEffort_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        var key = messageThreadId ?? 0;
        var now = DateTime.UtcNow;

        lock (_ownerStateLock)
        {
            if (_lastTypingSentUtcByThread.TryGetValue(key, out var lastSentUtc)
                && (now - lastSentUtc).TotalSeconds < TYPING_REFRESH_SECONDS)
            {
                return;
            }

            // Stamped BEFORE the call, so a failing endpoint is retried on the cadence, not every tick.
            _lastTypingSentUtcByThread[key] = now;
        }

        try
        {
            await client.Send_TypingAction_Async(messageThreadId, cancellationToken);
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so a bare rethrow would escalate a failed refresh into a shutdown.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Typing indicator refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// WHO the owner is being told about — "🔴 Sup" for a crew, "🟠 Solo" for a basic orchestration,
    /// "🟡 Gen-Sup" for the General topic.
    ///
    /// A basic orchestration has NO supervisor, and every narration line about one used to say "Sup"
    /// anyway (owner, 2026-08-14: *"the app didn't realize this is a 'solo' session … otherwise I get
    /// confused"*). The basic/crew question is <see cref="Sessions.OrchestrationShape"/>'s, read from
    /// the supervisor spawn stamp rather than from the roster — that class documents why a member-id
    /// scan reads a PROMOTED orchestration as basic for ever.
    ///
    /// An orchestration this engine cannot find is described as a supervisor, deliberately: the
    /// unknown case is the crew case everywhere else in this file, and inventing "Solo" for a session
    /// whose shape we could not read would put a WRONG certainty in front of the owner.
    /// </summary>
    string Describe_Speaker(string orchId)
    {
        if (orchId == ChannelDiscovery.GENERAL_ORCH_ID)
            return Mirroring.SpeakerLabel_Formatter.GENERAL;

        var session = _store.Get_Session_OrNull(orchId);

        return Mirroring.SpeakerLabel_Formatter.Describe(
            isGeneral: false,
            isBasic: session != null && Sessions.OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc));
    }

    /// <summary>
    /// THE RECEIPT IS A REACTION NOW — brief D. 👀 goes on the owner's OWN message the moment the
    /// bridge has appended it; <see cref="Publish_DeliveryReceipt_Async"/> replaces it with 👌 when
    /// a session picks it up. No bot message at all in the ordinary case.
    ///
    /// <para>
    /// After brief C the ✓ was already silent, but it was still a LINE IN THE TOPIC for every line
    /// the owner wrote — fifteen of them in one export. A reaction sits on the thing it is about
    /// and costs nothing.
    /// </para>
    /// <para>
    /// THE FALLBACK IS THE OLD PATH, UNCHANGED, and it is not optional: Telegram answers 400 for a
    /// message it will not let a bot react to and 429 under load, and an acknowledgement that
    /// silently did not happen is the owner watching their message vanish. When the reaction cannot
    /// be set — including when the update carried no message id to react to — this sends exactly
    /// the ✓ it always did, and the ✓✓ edit still finds it.
    /// </para>
    /// </summary>
    async Task Send_ReceivedAck_Async(ITelegramApiClient client, ITelegramOwnerMessage message, CancellationToken cancellationToken)
    {
        var messageThreadId = message.MessageThreadId;

        if (message.MessageId != null && await Try_React_Async(client, message.MessageId.Value, OwnerReaction_Emoji.RECEIVED, cancellationToken))
        {
            lock (_receiptLock)
                _reactedOwnerMessageIdByThread[messageThreadId ?? 0] = message.MessageId.Value;

            return;
        }

        try
        {
            // THE TICK CARRIES THE HOLD BUTTON, because it lands exactly where the owner is already
            // looking — directly under what they just sent — and a tap beats typing WAIT by the
            // seconds that decide whether the hold catches the message at all. Their words:
            // "clicking a button is faster than typing wait."
            var messageId = await client.Send_MessageWithButtons_Async(
                messageThreadId,
                "✓",
                [(HoldButton_Data.Build(HoldButtonActions.Hold, messageThreadId), HoldButton_Data.HOLD_LABEL)],

                // A RECEIPT NEVER RINGS. They sent the message it acknowledges a second ago — they
                // are holding the phone. "I got it" as a notification is the purest form of the
                // noise this brief removes.
                TelegramSendSounds.Silent,
                cancellationToken);

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

            // THE HIGHEST id wins, not the last one recorded: these arrive from a batch of updates
            // and from concurrent sends, so "most recently handed to this method" is not "latest in
            // the chat". An out-of-order id overwriting a higher one would tell the status line it is
            // no longer buried when it still is.
            //
            // DateTime.Now, LOCAL, because the planner compares it against the one local clock this
            // file uses everywhere — read the Is_AttemptDue comment before changing that. Arrival is
            // when the app learned of the message rather than Telegram's own `date`: for the quiet
            // window, which asks whether the conversation has stopped, they differ by the poll
            // latency and never by enough to matter.
            if (!_newestTopicMessageByThread.TryGetValue(key, out var newest) || messageId.Value > newest.MessageId)
                _newestTopicMessageByThread[key] = new Telegram.TopicStatusLine_Planner.TopicNewestMessage(messageId.Value, DateTime.Now);
        }
    }

    /// <summary>
    /// What the status-line planner is told about the topic's traffic. Absent means the app knows
    /// nothing about this topic yet — a fresh start, or a topic that has said nothing since — and the
    /// planner treats that as "not buried" rather than guessing.
    /// </summary>
    Telegram.TopicStatusLine_Planner.TopicNewestMessage? Find_NewestTopicMessage_OrNull(long? messageThreadId)
    {
        lock (_knownMessageIdsLock)
        {
            return _newestTopicMessageByThread.TryGetValue(messageThreadId ?? 0, out var newest) ? newest : null;
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

    /// <summary>
    /// One reaction attempt, and whether it landed. Never throws for a refusal — the whole point is
    /// that a caller can fall back to the message receipt.
    /// </summary>
    async Task<bool> Try_React_Async(ITelegramApiClient client, long messageId, string emoji, CancellationToken cancellationToken)
    {
        // THE PERMITTED SET IS CONSULTED, not merely documented. OwnerReaction_Emoji exists so a
        // later edit picks from Telegram's fixed list rather than from taste; a constant nothing
        // reads is a rule that is not enforced, and the failure it prevents (a 400 for an emoji
        // bots may not set) is one that would otherwise send every message down the fallback path
        // while looking like a Telegram outage.
        if (!OwnerReaction_Emoji.Is_Permitted(emoji))
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Reaction '{emoji}' is not one a bot may set — falling back to the ✓ receipt rather than asking Telegram to refuse it");
            return false;
        }

        try
        {
            await client.Set_MessageReaction_Async(messageId, emoji, cancellationToken);
            return true;
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout arrives as a TaskCanceledException
        // with the token NOT cancelled; read as a shutdown it would abandon the whole inbound
        // batch over a receipt. This file's canonical account is in Refresh_TopicStatusLines_Async.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Broad by intent: 400 for a message Telegram will not let a bot react to, 429 under
            // load, or no answer at all — every one of them means the same thing to the caller,
            // which is "send the tick instead".
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Reaction {emoji} on message {messageId} was refused ({ex.Message}) — falling back to the ✓ receipt");
            return false;
        }
    }

    /// <summary>
    /// The owner message wearing 👀 in this thread, if any, consumed on read — the reaction twin of
    /// <see cref="Take_ReceiptMessageId_OrNull"/>, and consumed for the same reason: the next batch
    /// gets its own receipt rather than overwriting one that has already been answered.
    /// </summary>
    long? Take_ReactedOwnerMessageId_OrNull(long? messageThreadId)
    {
        lock (_receiptLock)
        {
            var key = messageThreadId ?? 0;

            if (!_reactedOwnerMessageIdByThread.TryGetValue(key, out var messageId))
                return null;

            _reactedOwnerMessageIdByThread.Remove(key);
            return messageId;
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
    /// THE AWAY DIGEST, AND NOTHING ELSE ANY MORE. It was <c>Push_PeriodicStatus_Async</c> when it
    /// pushed a fifteen-line status into every topic every thirty minutes; brief C deleted that
    /// (owner, 2026-09-09 — one status surface per topic, and it is PULSE) and left the method
    /// named for the thing it no longer does, with a bare <c>continue</c> where the status used to
    /// be. What survives is the AWAY digest, which is not a cadence: it fires only while the owner
    /// is away and only when its content has changed.
    /// <para>
    /// The half-hourly SLOT still governs it — that is why the slot planner is still here — but a
    /// slot boundary is now permission to consider sending, not a reason to send.
    /// </para>
    /// </summary>
    async Task Push_AwayDigests_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        // ONE reading of the clock for the whole sweep. Taking it per session would let a sweep that
        // straddles a boundary split the batch across two slots — the trickle, in miniature.
        var now = DateTime.Now;

        foreach (var session in Sessions_ThisTick())
        {
            if (session.ClosedUtc != null || session.TelegramTopicId == null)
                continue;

            // MEETING: skipped BEFORE the stamp, deliberately. Stamping here would restart the
            // 30-minute clock on every tick of the meeting, so the owner would leave terminal mode
            // and then wait up to half an hour for the first status. Leaving the stamp alone means
            // the very next tick after they return posts one — which IS the "what waited while we
            // talked" summary, built by the formatter that already exists rather than a second copy.
            if (OwnerPresence_Policy.Suppresses_SupervisorAttention(session.OwnerPresence))
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
                // ONLY WHEN SOMETHING CHANGED (owner's call, 2026-08-19). An unchanged digest is not
                // merely redundant on their phone: it is APPENDED TO THE CHANNEL, and an append is
                // what a session's watcher fires on — so it wakes a session that has nothing to do,
                // which writes STANDING BY, which re-arms both of the app's OTHER alert paths. That is
                // 30-minute limit cycle in AwayDigest_Decider's docstring. Not sending it is what
                // breaks the loop, so this is a correctness guard rather than a politeness one.
                var digest = Build_AwayUpdateText(session);

                if (!AwayDigest_Decider.Should_Send(Last_AwayDigest_OrNull(session.OrchId), digest))
                    continue;

                // REMEMBERED ONLY ON A CONFIRMED WRITE. Recording it first would let a channel that
                // stayed locked for the whole budget count as a delivery, and because an unchanged
                // digest is never re-sent, that away spell would go silent entirely.
                //
                // THE PICTURE IS APPENDED AFTER THE DECISION AND IS NOT REMEMBERED WITH IT. Deciding
                // on the digest TEXT and storing the digest TEXT is what keeps the no-change rule
                // above intact: a fresh timestamped IMAGE: path differs on every single pass, so
                // folding it into either side would make every digest look changed and restart the
                // exact 30-minute limit cycle that rule exists to break.
                if (Post_StatusEntry(session.OrchId, digest + await Build_StatusScreenshotMarker_OrEmpty_Async(session, cancellationToken), session.OwnerPresence))
                    Remember_AwayDigest(session.OrchId, digest);

                continue;
            }

            // THE HALF-HOURLY STATUS IS GONE (owner's decision, 2026-09-09). It sent a fresh
            // fifteen-line message every thirty minutes — ten of them in five and a half hours in
            // one topic, three of them identical at 19:00, 19:30 and 20:00 with every member
            // closed. Its own trigger was wrong (it read PLAN.md's in-progress lines, not whether
            // any session had worked), and half its content was wrong or jargon: "5 running" with
            // nine members closed, "now: FIN-D-293a step 6" repeated for five hours after 293 was
            // merged, "idle — writing window left open".
            //
            // ONE STATUS SURFACE PER TOPIC, and it is PULSE: one message at the bottom, edited in
            // place, silent. A cadence that posts is a waterfall by construction — the owner's own
            // word for it — however good its content is. What survives here is the AWAY digest
            // above, which is not a cadence: it fires only while the owner is away and only when
            // its content has changed.
            continue;
        }
    }

    /// <summary>
    /// The picture of the session's terminal that rides the AWAY DIGEST (owner, 2026-08-24), so the
    /// update SHOWS what is happening as well as saying it. Returns the IMAGE: line to append, or
    /// an empty string when there is nothing to show. (It said "the periodic status" until brief C
    /// removed that; the digest is the only thing left that carries a picture.)
    ///
    /// THE QUEUEING THE OWNER ASKED FOR IS NOT HERE — it is in <see cref="WindowFocus.TerminalWindow_Capturer"/>,
    /// which serialises every capture process-wide. This sweep is sequential already; the reason the
    /// gate cannot live in this loop is /screen, which arrives from the phone on the inbound loop and
    /// would otherwise raise a second window into the middle of this one's photograph.
    ///
    /// ONLY WHILE THE OWNER IS AWAY FROM THE MACHINE — their call, 2026-08-24, asked as a choice and
    /// answered "only take the periodic screenshot when I am away from the PC". A capture is not a
    /// passive read: it raises and maximises a real window, so taking one while they are working
    /// steals the foreground from whatever they are doing, every half hour, once per topic.
    ///
    /// The per-session skip further up does NOT already cover this. It suppresses the status for the
    /// orchestration whose terminal they are sitting in; every OTHER topic would still fire, and it
    /// is those windows flashing over their work that they were being asked about.
    /// </summary>
    async Task<string> Build_StatusScreenshotMarker_OrEmpty_Async(IOrchestrationSession session, CancellationToken cancellationToken)
    {
        // OFF UNTIL THEY TURN IT ON (/screens). Their words: "there should be a command that starts
        // adding screenshots to the status messages so that I can enable it" — so the default is no
        // pictures, and nothing here happens until the persisted flag says otherwise.
        if (!_configProvider.Get_Current().TelegramStatusScreenshots)
            return string.Empty;

        if (Is_OwnerAtThePc())
            return string.Empty;

        var window = _hostWindowing.Find_OwnerFacingWindow_OrNull(session);

        if (window == null)
            return string.Empty;

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);
        var directory = Path.GetDirectoryName(channelFile);

        if (directory == null)
            return string.Empty;

        var imagePath = Path.Combine(directory, "media", $"status-{DateTime.Now:yyyyMMdd-HHmm}.png");

        var failureReason = await _hostWindowing.Try_Capture_Async(window, imagePath, cancellationToken);

        if (failureReason == null)
            return $"\nIMAGE: {imagePath}";

        // BEST-EFFORT, unlike /screen. There the picture IS the answer, so failing it silently would
        // leave the owner with nothing; here it accompanies a status that must still arrive, so the
        // reason goes to the log and the status goes out without it.
        _log.Log_Warning(session.OrchId, $"Periodic status screenshot skipped: {failureReason}");

        return string.Empty;
    }

    /// <summary>What the General topic is called when nothing is decorating it.</summary>
    const string GENERAL_TOPIC_BASE_NAME = "General";

    /// <summary>
    /// The last General-topic name this process actually pushed. Null until the first push, which is
    /// deliberate: a restart then re-asserts the name once, so a flag edited on disk while the app
    /// was down cannot leave the topic list contradicting the config.
    /// </summary>
    string? _appliedGeneralTopicName;

    /// <summary>
    /// When a General-topic rename whose outcome we could NOT learn may be tried again. The sibling of
    /// <see cref="_topicNameRetryAfterUtc"/>, and here for the same reason: an unknown outcome must
    /// suppress retries for a while and then retry, rather than for ever or for two seconds.
    /// </summary>
    DateTime? _generalTopicNameRetryAfterUtc;

    /// <summary>
    /// KEEPS THE GENERAL TOPIC CALLED "General", and nothing else — since 2026-09-10.
    ///
    /// <para>
    /// It used to put the camera here while status screenshots were on (owner, 2026-08-24), because
    /// the topic list is the one surface visible without opening anything. The owner moved it to the
    /// DASHBOARD's header instead, and the reason is the one that moved the other five mode glyphs
    /// off the orchestration names: a name change is an `editForumTopic` plus a service message in
    /// the thread, so flipping a setting announced itself back to the owner who had just flipped it.
    /// The dashboard is the message General already keeps current, and its header costs a silent edit.
    /// </para>
    /// <para>
    /// THE METHOD STAYS, and it is not dead: it is the MIGRATION. Every existing supergroup has its
    /// General topic named "📸 General" right now, and this is the only code that can rename it back.
    /// It also keeps doing the job it always did — re-asserting the name once per process, so a topic
    /// renamed by hand or by an older build converges. WRITING A KNOWN NAME rather than decorating
    /// what is there is still the design: Telegram offers no cheap read of General's name, and a
    /// decorate-in-place that cannot read the previous value is how an emoji gets applied twice.
    /// </para>
    /// </summary>
    async Task Sync_GeneralTopicName_BestEffort_Async(ITelegramApiClient client, CancellationToken cancellationToken)
    {
        var desired = GENERAL_TOPIC_BASE_NAME;

        if (_appliedGeneralTopicName == desired)
            return;

        if (!TelegramAttempt_Gate.Is_AttemptDue(_generalTopicNameRetryAfterUtc, DateTime.UtcNow))
            return;

        try
        {
            await client.Edit_GeneralForumTopic_Async(desired, cancellationToken);
            _appliedGeneralTopicName = desired;
            _generalTopicNameRetryAfterUtc = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (Is_TopicAlreadyNamed(ex))
        {
            // THE ORDINARY CASE AT EVERY APP START, AND IT IS NOT AN ERROR. The memo begins null while
            // Telegram already holds the right name, so the first push is always the one Telegram
            // refuses with TOPIC_NOT_MODIFIED. Not remembering it meant trying again on the next tick,
            // for ever: the owner reported this as "I get this error many many times in the activity
            // log", which is the same spin Sync_TopicNames_Inside_Gate_Async was fixed for and this
            // copy never was. Nothing is logged — the name is what we want, which is success.
            _appliedGeneralTopicName = desired;
            _generalTopicNameRetryAfterUtc = null;
        }
        catch (Exception exception)
        {
            // Cosmetic, and never the reason a toggle looks like it failed — the flag is saved before
            // this runs.
            //
            // THE SAME THREE BUCKETS AS THE PER-ORCHESTRATION SYNC, for the same reasons, decided in the
            // same place. An outcome we could not learn (a timeout, a dropped connection, a 429, a 5xx)
            // is stamped and retried after the backoff; a genuine refusal is remembered as applied so it
            // is retried when the wanted name CHANGES rather than on the next tick. Writing the memo on
            // a refusal is the same honest-behaviour/dishonest-map trade documented at the sibling site:
            // an invalid name will not become valid by being sent again two seconds later.
            if (TelegramAttempt_Gate.Classify_Failure(exception) == TopicNameAttemptOutcomes.OutcomeUnknown)
                _generalTopicNameRetryAfterUtc = TelegramAttempt_Gate.Build_RetryAfterUtc(DateTime.UtcNow, _timing.MirrorRetryBackoffSeconds);
            else
                _appliedGeneralTopicName = desired;

            _log.Log_Warning(GLOBAL_ORCH_ID, $"Could not rename the General topic: {exception.Message}");
        }
    }

    /// <summary>
    /// Is the owner AT THE MACHINE, anywhere? Terminal presence is per orchestration and at most one
    /// can hold it — a /pc ends every other topic's — so this is "does any live session have them".
    ///
    /// It asks through <see cref="OwnerPresence_Policy.Suppresses_SupervisorAttention"/> rather than
    /// comparing the enum again: that predicate already IS "the owner is in this orchestration's
    /// terminal", and a second spelling of it here is how the two would answer differently later.
    /// </summary>
    bool Is_OwnerAtThePc()
    {
        // GENERAL IS ASKED SEPARATELY, and it is not a detail. It keeps no session.json, so
        // Load_All cannot see it AT ALL - and it is the session the owner talks to most, which
        // makes "/pc held only in General" the likeliest way for this to be true. Reading it
        // through Resolve_Presence is what the rest of the file already does for General.
        if (OwnerPresence_Policy.Suppresses_SupervisorAttention(Resolve_Presence(ChannelDiscovery.GENERAL_ORCH_ID)))
            return true;

        return Sessions_ThisTick().Any(session =>
            session.ClosedUtc == null && OwnerPresence_Policy.Suppresses_SupervisorAttention(session.OwnerPresence));
    }

    /// <summary>The slot this orchestration last spent, or null when it has never been seen.</summary>
    DateTime? Last_PeriodicStatusSlot_OrNull(string orchId)
    {
        return _lastPeriodicStatusSlot.TryGetValue(orchId, out var slot) ? slot : null;
    }

    /// <summary>Per orchestration: each currently-open `[>]` line, and when it first appeared in that shape.</summary>
    readonly Dictionary<string, Dictionary<string, DateTime>> _inProgressSinceByOrchId = [];

    /// <summary>
    /// Records this tick's `[>]` lines and returns when each was first seen. Lines that have changed
    /// or left `[>]` are DROPPED, so editing a line to say where it has got to resets its clock —
    /// that edit is a report, which is exactly what the age rule is asking for.
    /// </summary>
    IReadOnlyDictionary<string, DateTime> Note_InProgressLines(string orchId, Planning.PlanProgress.IPlanProgress? progress)
    {
        lock (_ownerStateLock)
        {
            if (!_inProgressSinceByOrchId.TryGetValue(orchId, out var seen))
            {
                seen = [];
                _inProgressSinceByOrchId[orchId] = seen;
            }

            var current = progress?.InProgressTasks ?? [];

            foreach (var line in current)
                seen.TryAdd(line, DateTime.UtcNow);

            foreach (var gone in seen.Keys.Where(line => !current.Contains(line)).ToList())
                seen.Remove(gone);

            return seen;
        }
    }

    sealed class FiguresStamp
    {
        public int Done;
        public int Total;
        public DateTime SinceUtc;
    }

    /// <summary>Per orchestration: the ledger figures currently on the status line, and since when.</summary>
    readonly Dictionary<string, FiguresStamp> _figuresSinceByOrchId = [];

    /// <summary>
    /// Per orchestration: whether this topic is waiting on the OWNER, and whether that has stopped
    /// the work — the glyph the topic list carries.
    ///
    /// CACHED RATHER THAN COMPUTED WHERE IT IS USED, and that is the whole reason it exists as a
    /// field. Sync_TopicNames_BestEffort_Async runs EVERY tick; resolving this there would read
    /// every member channel of every orchestration every two seconds, which is the per-tick channel
    /// cost this file has already been trimmed for once. It is filled in on the status-line pass,
    /// which has those entries in hand anyway, so the glyph costs no extra read at all.
    ///
    /// Absent means None: a topic the status-line pass has not reached yet shows no glyph rather
    /// than a guessed one.
    /// </summary>
    readonly Dictionary<string, Telegram.OwnerReplyStates> _ownerReplyStateByOrchId = [];

    /// <summary>
    /// Per orchestration: the ledger as it read on the previous tick, so a MOVEMENT can be spotted.
    ///
    /// The owner asked to hear when a line finishes and the next one starts (2026-08-20) — following
    /// a session otherwise meant asking it. A transition happens once and is therefore told once:
    /// there is no timer here and no cadence, only a comparison, which is what keeps this from
    /// becoming the waterfall this file spent a day removing.
    /// </summary>
    readonly Dictionary<string, Planning.PlanProgress.IPlanProgress> _lastLedgerReadingByOrchId = [];

    /// <summary>Orchestrations already told they were finished — so the recap lands exactly once.</summary>
    readonly HashSet<string> _recappedOrchIds = [];

    /// <summary>
    /// BLOCKING WINS over merely wanted: a member that has declared BLOCKED ON OWNER cannot proceed,
    /// and "someone is waiting" understates that. Read from the entries the caller already has.
    /// </summary>
    Telegram.OwnerReplyStates Resolve_OwnerReplyState(IOrchestrationSession session, IReadOnlyList<Telegram.TopicStatusMember.ITopicStatusMember> members)
    {
        foreach (var member in members)
        {
            if (member.IsClosed)
                continue;

            if (Status.MemberState_Resolver.Resolve(member.Entries) == Status.MemberStates.BlockedOnOwner)
                return Telegram.OwnerReplyStates.Blocking;
        }

        var ownerEntries = ChannelHistory_Cache.Read_Entries(_paths.Get_OwnerChannelFile(session.OrchId));

        // A QUESTION, not merely the last word. OwnerOwesReply_Decider answers "whose move is it",
        // which is true after every report the session writes — including its answer to the owner —
        // so driving the glyph from it left ❓ permanently lit on topics with nothing pending. The
        // owner reads this glyph as "a non-blocking question is waiting", and now it is one.
        return OwnerQuestionPending_Decider.Decide(ownerEntries)
            ? Telegram.OwnerReplyStates.Wanted
            : Telegram.OwnerReplyStates.None;
    }

    Telegram.OwnerReplyStates Last_OwnerReplyState(string orchId)
    {
        lock (_ownerStateLock)
            return _ownerReplyStateByOrchId.TryGetValue(orchId, out var state) ? state : Telegram.OwnerReplyStates.None;
    }

    /// <summary>
    /// Records the figures this tick and answers how long they have stood still — null when they
    /// just moved, and null on FIRST SIGHT, which is the honest answer rather than zero: the app
    /// has no idea how long they had been that way before it started looking.
    ///
    /// Note-and-answer in ONE call on purpose. Split into "note" and "ask", the two would be one
    /// missed call apart from a clock that never restarts — a line frozen at "unchanged 3 h" while
    /// the numbers underneath it moved every few minutes, which is worse than saying nothing.
    /// </summary>
    TimeSpan? Note_FiguresAndDescribe_UnchangedFor(string orchId, Planning.PlanProgress.IPlanProgress? progress)
    {
        if (progress == null || progress.Total <= 0)
            return null;

        lock (_ownerStateLock)
        {
            if (!_figuresSinceByOrchId.TryGetValue(orchId, out var stamp)
                || stamp.Done != progress.Done
                || stamp.Total != progress.Total)
            {
                _figuresSinceByOrchId[orchId] = new FiguresStamp
                {
                    Done = progress.Done,
                    Total = progress.Total,
                    SinceUtc = DateTime.UtcNow,
                };

                return null;
            }

            return DateTime.UtcNow - stamp.SinceUtc;
        }
    }

    /// <summary>
    /// Tells the owner that a line finished, that the next one started, and — once — that the whole
    /// endeavour is done.
    ///
    /// SENT STRAIGHT TO TELEGRAM, NEVER THROUGH THE CHANNEL, and that is the load-bearing choice. A
    /// channel append wakes the session, and the whole point of this message is to save the owner
    /// asking — waking a working session to announce its own progress would cost a turn to tell it
    /// what it just did. That is the away-digest loop exactly, and it is the reason this file exists
    /// in its current shape.
    /// </summary>
    async Task Tell_LedgerMovement_Async(IOrchestrationSession session, Planning.PlanProgress.IPlanProgress? ledger, CancellationToken cancellationToken)
    {
        if (ledger == null)
            return;

        // IT CANNOT TELL, SO IT MUST NOT CONSUME. This method REMEMBERS the reading it compares
        // against, and everything it has to say goes out through `Send_AwayNotice_Async`, which
        // returns silently in any mode but Normal. Gated after the write instead of before it, every
        // muted tick advanced the remembered ledger while the notice it produced was dropped on the
        // floor: on unmute `previous == ledger`, nothing was worth telling, and the accumulated
        // movement of the whole mute was gone. The end-of-endeavour recap was worse — `_recappedOrchIds`
        // had already been armed, so `Add` returned false and the recap could never be sent again for
        // the life of the process. The owner would simply never be told their endeavour had finished.
        //
        // It has been latent since the per-topic modes existed (a 🌙 topic hits this on any tick), and
        // it became the common case on 2026-09-10, when app-wide DND started running this refresh
        // instead of skipping it. Returning here leaves the previous reading standing, so the FIRST
        // Normal tick compares against it and reports the whole delta at once — which is what
        // decision 9 promises a mute delivers.
        if (Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
            return;

        Planning.PlanProgress.IPlanProgress? previous;

        lock (_ownerStateLock)
        {
            _lastLedgerReadingByOrchId.TryGetValue(session.OrchId, out previous);
            _lastLedgerReadingByOrchId[session.OrchId] = ledger;

            // Work reappearing un-arms the recap, so a reopened endeavour can announce its end again.
            if (!Planning.LedgerTransition_Detector.Is_EndOfEndeavour(ledger))
                _recappedOrchIds.Remove(session.OrchId);
        }

        // FIRST SIGHT SAYS NOTHING. With no previous reading every line looks new, so an app restart
        // would announce an entire ledger at once — the loudest possible way to say nothing happened.
        if (previous == null)
            return;

        var transition = Planning.LedgerTransition_Detector.Compare(previous, ledger);

        if (transition.IsWorthTelling)
            // A LEDGER LINE MOVING IS APP BOOKKEEPING. The owner cannot act on "task 4 is now in
            // progress" (decision 15), and after 2026-09-09 an app write does not ring.
            await Send_AwayNotice_Async(session, Planning.LedgerTransition_Wording.Describe(transition), TelegramSendSounds.Silent, cancellationToken);

        if (!Planning.LedgerTransition_Detector.Is_EndOfEndeavour(ledger))
            return;

        lock (_ownerStateLock)
        {
            if (!_recappedOrchIds.Add(session.OrchId))
                return;
        }

        await Send_AwayNotice_Async(
            session,
            Planning.LedgerTransition_Wording.Describe_Recap(session.DisplayName ?? session.OrchId, ledger),
            TelegramSendSounds.Silent,
            cancellationToken);
    }

    /// <summary>The away digest last sent for this orchestration, or null in a fresh away spell.</summary>
    string? Last_AwayDigest_OrNull(string orchId)
    {
        lock (_ownerStateLock)
            return _lastAwayDigestByOrchId.TryGetValue(orchId, out var digest) ? digest : null;
    }

    /// <summary>Recorded only AFTER a confirmed append — a digest remembered but never written would
    /// silence the whole away spell, since an unchanged one is never re-sent.</summary>
    void Remember_AwayDigest(string orchId, string digest)
    {
        lock (_ownerStateLock)
            _lastAwayDigestByOrchId[orchId] = digest;
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

            // The next away spell starts from null, so its FIRST digest always sends rather than
            // being compared against a snapshot from hours ago and silently swallowed.
            _lastAwayDigestByOrchId.Clear();

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
    void Nudge_IfTooVerbose(string orchId, string mirroredText, int deliveredMessages)
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

        // Return deliberately discarded, and this one is safe for a reason the memo sites are not:
        // the state recorded above is a COOLDOWN, not a record that the work is done. It expires by
        // itself after NUDGE_COOLDOWN_MINUTES, so a lost nudge costs at most one un-nudged message
        // rather than suppressing the nudge forever. Recording it after the append instead would
        // reopen the double-nudge race the lock above exists to close — a worse trade for a smaller
        // problem. The lock's own diagnostics report the failure either way.
        var nudged = ChannelAppender.Append_AppEntry(
            _paths.Get_OwnerChannelFile(orchId), AppEntryAudiences.Agent,
            "that message was too long for a phone",
            Brevity_Policy.Build_NudgeBody(mirroredText, deliveredMessages),
            DateTime.Now);

        // The return is consulted for the SENTENCE ONLY, and that does not disturb the deliberate
        // discard above: nothing is queued, nothing is retried, and the cooldown still records before
        // the append. It used to say "nudged" unconditionally, so a locked channel produced a
        // confident wrong statement sitting beside the lock's own diagnostic contradicting it.
        _log.Log_Info(
            orchId,
            nudged
                ? $"Supervisor message exceeded the brevity cap ({Brevity_Policy.Count_Lines(mirroredText)} lines) — nudged"
                : $"Supervisor message exceeded the brevity cap ({Brevity_Policy.Count_Lines(mirroredText)} lines) — NOT nudged, the channel was locked; the cooldown still applies, so the next overlong message in {Brevity_Policy.NUDGE_COOLDOWN_MINUTES} minutes is the next chance");
    }

    /// <summary>
    /// QUEUES an announcement. It never writes — <see cref="Drain_PendingAnnouncements"/> is the only
    /// thing that writes one, and that is the entire ordering guarantee.
    /// <para>
    /// These are the one class of channel write a return-value check cannot save: they fire on the
    /// EDGE, and by the time the append runs the transition is already recorded in the mode state,
    /// so there is no memo to withhold — withholding one would mean refusing to change the mode.
    /// A lost entry means the supervisor is never told the owner went away and keeps asking them
    /// questions, which is what away mode exists to stop.
    /// </para>
    /// <para>
    /// THIS USED TO APPEND DIRECTLY AND GUARD THE ORDER WITH A <c>Has_Queued_For</c> CHECK, and that
    /// guard could not work: an announcement whose append is still WAITING on the channel lock is in
    /// neither state — not written, not queued — so a concurrent announcement saw an empty queue and
    /// overtook it. The two producers really do sit on different loops (away mode is ENTERED on the
    /// mirror tick and EXITED on the inbound loop) and the thing that ends away mode is the owner
    /// texting, which IS the inbound loop's traffic. The supervisor's last word would be "went away"
    /// while the owner was present, so it would stop asking a present owner questions — the inversion
    /// away mode exists to manage. (rev-10, F1 on d0054fb.)
    /// </para>
    /// <para>
    /// ONE WRITER REMOVES THE RACE RATHER THAN GUARDING IT. A single writer draining a per-channel
    /// FIFO cannot interleave with itself, and there is no state an announcement can be in that the
    /// next writer cannot see. The cost is that every announcement waits up to one tick (≤2 s, against
    /// a supervisor watcher polling at 5 s) and that anything still queued at exit is lost — which is
    /// why <see cref="Run_Async"/> drains once more on the way out.
    /// </para>
    /// </summary>
    void Announce(string orchId, string channelFile, AppEntryAudiences audience, string subject, string body)
    {
        var dropped = _pendingAnnouncements.Queue(orchId, channelFile, audience, subject, body, DateTime.UtcNow);

        if (dropped != null)
            _log.Log_Error(orchId,
                $"Announcement queue for {Path.GetFileName(channelFile)} is full ({IPendingAnnouncements.PER_CHANNEL_CAP}) — DROPPED the oldest, '{dropped.Subject}' queued at {dropped.QueuedUtc:HH:mm:ss}Z. That channel has been unwritable long enough to lose announcements.",
                null);
    }

    /// <summary>
    /// Retries queued announcements. Runs inside the mirror tick, so its waiting is drawn from the
    /// tick's own allowance rather than added on top of it.
    /// </summary>
    void Drain_PendingAnnouncements()
    {
        if (_pendingAnnouncements.Count == 0)
            return;

        var delivered = _pendingAnnouncements.Drain(pending =>
            ChannelAppender.Append_AppEntry(pending.ChannelFile, pending.Audience, pending.Subject, pending.Body, DateTime.Now));

        if (delivered > 0)
            _log.Log_Info(GLOBAL_ORCH_ID, $"Delivered {delivered} queued announcement(s)");

        // THE FAILURE IS REPORTED HERE, not at Announce. Queuing is now the ordinary path — every
        // announcement is queued — so a line there would say nothing and fire constantly. What is
        // worth saying is that something is STILL waiting after a drain, which means a channel is
        // genuinely locked against us rather than merely behind by a tick.
        var stillWaiting = _pendingAnnouncements.Count;

        if (stillWaiting > 0)
            _log.Log_Warning(GLOBAL_ORCH_ID,
                $"{stillWaiting} announcement(s) could not be written — the channel stayed locked; it is queued and the next tick retries it");
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

        // THE MODE-TRANSITION ANNOUNCEMENTS DISCARD THE RETURN, AND A BOOL CHECK CANNOT FIX THEM.
        // These fire on the EDGE: the guard above returns early once newMode == previousMode, so by
        // the time the append runs the transition is already recorded in the mode state itself. There
        // is no memo here to withhold — withholding one would mean not changing the mode, which is
        // not ours to refuse. A lost entry means the supervisor is never told the owner went away and
        // keeps asking them questions, which is precisely what away mode exists to stop.
        //
        // Closing it properly needs a pending-announcement queue that survives to the next tick, which
        // is a mechanism rather than a return check, so it is NOT done here and is reported as an open
        // gap rather than left to look finished. The same applies to the quiet, away-on and away-off
        // announcements below. The lock's diagnostics name the channel and the wait on every failure,
        // so none of these is silent — only unretried.
        if (newMode == TelegramDeliveryModes.Deferred)
        {
            Announce(orchId,
                _paths.Get_OwnerChannelFile(orchId), AppEntryAudiences.Agent,
                "the owner switched this topic to Do-Not-Disturb — treat it as AWAY",
                "They set DND deliberately, so this is not a guess: they are away and nothing you write reaches them "
                + "until they switch back.\n\n"
                + "Behave exactly as in AWAY MODE: ask NOTHING, park what you need from them, decide and delegate "
                + "everything you safely can, and leave the owner-approval and merge gates standing. The app queues a "
                + "short status for them and keeps only the newest, so they return to the CURRENT state instead of a "
                + "backlog. You get an entry here when they switch back.");

            Raise_OrchestrationActivity(orchId);
            return;
        }

        if (previousMode == TelegramDeliveryModes.Deferred && newMode == TelegramDeliveryModes.Normal)
        {
            Announce(orchId,
                _paths.Get_OwnerChannelFile(orchId), AppEntryAudiences.Agent,
                "Do-Not-Disturb is off — the owner is back",
                "Normal mode. Re-ask ONLY what still matters, rewritten against the CURRENT state, and drop what "
                + "events have overtaken. One line on what you decided while they were away.");

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

        Announce(orchId,
            _paths.Get_OwnerChannelFile(orchId), AppEntryAudiences.Agent,
            "HOLD — the owner has not answered your last messages",
            $"{AwayMode_Policy.QUIET_THRESHOLD} of your messages are unanswered. They may simply be mid-task, so nothing is being "
            + "assumed yet — but STOP sending them anything more for now: no questions, no options, no updates.\n\n"
            + "Park what you would have asked (keep the list; you will re-ask from it) and carry on with what you can "
            + $"decide and delegate yourself. If they stay silent for {AwayMode_Policy.AWAY_AFTER_MINUTES} minutes you will get an "
            + "AWAY MODE ON entry; if they reply, everything returns to normal on its own.");

        Raise_OrchestrationActivity(orchId);

        // The owner gets ONE line marking the boundary in the conversation: everything above it was
        // asked, nothing below will be until they reply. The topic glyph says WHAT, this says WHERE.
        var session = _store.Get_Session_OrNull(orchId);

        if (session != null)
            await Send_AwayNotice_Async(session, AwayMode_Policy.QUIET_ON_NOTICE, TelegramSendSounds.Silent, cancellationToken);
    }

    /// <summary>
    /// Flips away mode on once the owner has visibly stopped reading, and keeps the short updates
    /// coming while it is on. The supervisor is told through its channel (that is the only thing it
    /// reads); the owner is told on Telegram.
    /// </summary>
    async Task Check_AwayMode_Async(CancellationToken cancellationToken)
    {
        // ASKED OUTSIDE THE LOCK, on purpose: it reads every session.json off disk, and
        // _ownerStateLock is taken on the inbound path too - holding it across file I/O is how a
        // tick and an owner's message come to wait on each other.
        var ownerAtAPc = Is_OwnerAtThePc();

        bool shouldEnter;
        bool shouldLeave;

        lock (_ownerStateLock)
        {
            var anyQuiet = _awayTrackers.Values.Any(tracker => tracker.IsQuiet);

            shouldEnter = !_awayActive && AwayMode_Policy.Should_EnterAway(anyQuiet, ownerAtAPc, _lastOwnerMessageUtc, DateTime.UtcNow);
            shouldLeave = AwayMode_Policy.Should_LeaveAway(_awayActive, ownerAtAPc);

            if (shouldEnter)
                _awayActive = true;

            // Cleared HERE rather than through Note_OwnerSpoke_AndWasAway, which would also stamp
            // the silence clock and zero every quiet tracker. The owner has not spoken - they are
            // at a keyboard - so away ends and nothing else is claimed on their behalf.
            if (shouldLeave)
                _awayActive = false;
        }

        // Mutually exclusive by construction: entering needs away off, leaving needs it on.
        if (shouldEnter)
            await Enter_AwayMode_Async(cancellationToken);
        else if (shouldLeave)
            await Exit_AwayMode_Async(cancellationToken);
    }

    /// <summary>
    /// APP-WIDE. Every open orchestration is told, the general supervisor is told, every topic gets
    /// the ✈ glyph and one notice. The app coordinates all of it directly — supervisors relaying
    /// this to each other would be slower, lossier, and would cost tokens to do worse.
    /// </summary>
    async Task Enter_AwayMode_Async(CancellationToken cancellationToken)
    {
        _log.Log_Info(GLOBAL_ORCH_ID, "AWAY MODE ON (app-wide) — owner unresponsive; every supervisor told to proceed without questions");

        Announce(GLOBAL_ORCH_ID,
            _paths.GeneralChannelFile, AppEntryAudiences.Agent,
            "AWAY MODE ON — the owner is not reading",
            "Every orchestration has been told directly; you do not need to relay it. Ask them nothing until the "
            + "AWAY MODE OFF entry arrives.");

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            Announce(session.OrchId,
                _paths.Get_OwnerChannelFile(session.OrchId), AppEntryAudiences.Agent,
                "AWAY MODE ON — the owner is not reading",
                "They have not answered. Assume they are unavailable, NOT ignoring you.\n\n"
                + "Until further notice: ask NOTHING. Park every question you would have asked (keep a list — you will "
                + "re-ask the ones that still matter). Decide everything you can safely decide yourself and keep the "
                + "implementers working; the owner-approval gate and the merge gate still stand, so work that genuinely "
                + "needs their decision waits rather than proceeding without it.\n\n"
                + "The app posts a short update to them every 30 min — you do not need to. When they return you get an "
                + "AWAY MODE OFF entry; then re-ask ONLY what is still relevant, updated to the current state, and drop "
                + "what events have overtaken.");

            Raise_OrchestrationActivity(session.OrchId);

            await Park_OpenQuestions_Async(session.OrchId, cancellationToken);
            // AWAY MODE FIRES AFTER FIFTEEN MINUTES OF THE OWNER'S OWN SILENCE — which is to say,
            // usually because they are asleep. Waking them to say "you seem to be away" is the
            // purest possible case of an alert they cannot act on.
            await Send_AwayNotice_Async(session, AwayMode_Policy.AWAY_ON_NOTICE, TelegramSendSounds.Silent, cancellationToken);
        }
    }

    async Task Exit_AwayMode_Async(CancellationToken cancellationToken)
    {
        _log.Log_Info(GLOBAL_ORCH_ID, "AWAY MODE OFF (app-wide) — owner is back");

        Announce(GLOBAL_ORCH_ID,
            _paths.GeneralChannelFile, AppEntryAudiences.Agent,
            "AWAY MODE OFF — the owner is back",
            "Every orchestration has been told directly.");

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            Announce(session.OrchId,
                _paths.Get_OwnerChannelFile(session.OrchId), AppEntryAudiences.Agent,
                "AWAY MODE OFF — the owner is back",
                "Normal mode: they are reading and can answer within a short time.\n\n"
                + "Go through the questions you parked. Re-ask ONLY the ones that still matter, rewritten against the "
                + "CURRENT state (facts may have moved while they were away), and say in one line what you decided "
                + "yourself in the meantime. Drop the rest without ceremony — a re-asked obsolete question is exactly "
                + "the mess this mode exists to prevent.");

            Raise_OrchestrationActivity(session.OrchId);

            // AND ONCE PER OPEN ORCHESTRATION, so five open topics meant five notifications saying
            // the same thing about a state the owner had just ended themselves by speaking.
            await Send_AwayNotice_Async(session, AwayMode_Policy.AWAY_OFF_NOTICE, TelegramSendSounds.Silent, cancellationToken);
        }
    }

    /// <summary>
    /// RENDERED, LIKE EVERY OTHER SUPERVISOR ENTRY (owner's decision, 2026-09-09). This send was a
    /// plain <c>sendMessage</c>, and it is one of the two paths that RESEND SOMETHING THE SUPERVISOR
    /// ALREADY WROTE — the silent-deadlock release and the ledger-movement notice. So the owner
    /// received the supervisor's own Markdown with its markers showing: `**bold**`, `## heading`,
    /// backticks, at 18:00 and 18:18 on 2026-09-09. Nothing was wrong with the renderer; these two
    /// sites simply never reached it.
    /// </summary>
    async Task Send_AwayNotice_Async(IOrchestrationSession session, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        if (_telegramClient == null || Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
            return;

        try
        {
            await TelegramProse_Sender.Send_Async(
                _telegramClient, _log, session.OrchId, session.TelegramTopicId, text, sound, cancellationToken);
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        // Cost HERE: the away notice, plus the rest of the away sweep and the tick.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
            {
                _openQuestions.Remove(entry.MessageId);
                Note_QuestionClosed(entry.MessageId, QuestionClosure_Wording.AWAY_PARKED);
            }
        }

        if (parked.Count > 0)
            Persist_EngineState();

        foreach (var entry in parked)
        {
            try
            {
                await TelegramProse_Sender.Edit_Async(
                    _telegramClient, _log, orchId, entry.MessageId,
                    $"{entry.Text}{AwayMode_Policy.PARKED_SUFFIX}", cancellationToken);
            }
            // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
            // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
            // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
            // Cost HERE: every REMAINING parked question in the same loop.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
    /// <returns>Whether the entry was actually written — see the note at the append.</returns>
    bool Post_StatusEntry(string orchId, string text, OwnerPresenceModes presence)
    {
        // Suppressed WITHOUT spending the slot during a meeting (see Push_AwayDigests_Async), so
        // the first tick after the owner leaves terminal mode posts a fresh status — which IS the
        // "what waited while we talked" summary, built by the formatter that already exists.
        //
        // The presence is the CALLER's — the one it already decided the slot on — so the decision
        // and the append cannot disagree about where the owner is (rev-7 P5).
        //
        // The helper's return is now HANDED BACK rather than discarded, and the reason it used to be
        // discarded is worth keeping: a dropped periodic status is SUPERSEDED rather than lost — the
        // next one carries the current state, and the Deferred path collapses queued statuses to the
        // newest for the same reason. That held while every slot posted unconditionally.
        //
        // The away digest broke the premise. It is change-gated (owner's call, 2026-08-19), so an
        // unchanged digest is never re-sent — and a spell whose one append was dropped by a locked
        // channel would then go silent ENTIRELY rather than merely late. The old comment here ended
        // "nothing records it as done, so nothing is left claiming work that did not happen"; that
        // invariant is exactly what a remembered-but-unwritten digest would break, so the away caller
        // records its delivery only on a true. The periodic caller still discards it, for the
        // original reason.
        return Append_SupervisorAttention_UnlessMeeting(orchId, MirrorText_Formatter.STATUS_SUBJECT_PREFIX, text, presence, Channels.AppEntryAudiences.Owner);
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

            var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId);
            var entries = ChannelHistory_Counter.Read_AllEntries(channelFile);
            var usageFile = Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE);

            memberLines.Add($"{member.MemberId}: {Describe_AwayMemberState(
                member.MemberId, entries, usageFile,
                Is_Working(Running.SessionRoles.Implementer, session.OrchId, member.MemberId, usageFile))}");
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

    /// <summary>
    /// <paramref name="working"/> is passed IN rather than read here: the caller knows the member's
    /// identity and can ask the app, while this method only ever had a path — and that path is a
    /// status-line file no headless session writes. The path is still needed for the context figure,
    /// which is honest about being absent.
    /// </summary>
    static string Describe_AwayMemberState(
        string memberId,
        IReadOnlyList<Channels.ChannelEntry.IChannelEntry> entries,
        string usageFilePath,
        bool working)
    {
        var state = MemberState_Resolver.Resolve(entries);

        if (state == MemberStates.BlockedOnOwner)
            return "BLOCKED — needs you";

        var lastBrief = entries.LastOrDefault(e => e.Author == ChannelAuthors.Supervisor);

        var task = lastBrief == null
            ? ""
            : $" — {TextSummary_Formatter.Summarize_Task(lastBrief.Subject, TextSummary_Formatter.CARD_TASK_WORDS)}";

        // A member reaches the digest at the digest's own threshold, which is lower than the
        // status line's — see ContextVisibility_Policy for why the two differ. A solo is always
        // shown, and the policy knows that from the member id so this call site does not have to.
        var context = UsageTotals_Reader.Read_ContextUsage_OrNull(usageFilePath);

        var contextSuffix = Status.ContextVisibility_Policy.Show_Member_InPeriodicDigest(memberId, context)
            ? $" · {Formatting.ContextUsage_Formatter.Describe_OrNull(context)}"
            : "";

        if (working)
            return $"working{task}{contextSuffix}";

        if (state == MemberStates.AwaitingSupervisorReview)
            return $"report filed, awaiting review{contextSuffix}";

        return $"idle{task}{contextSuffix}";
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
    async Task Narrate_BusySupervisor_Async(string orchId, PendingOwnerReply pending, string supervisorUsageFile, bool queued, CancellationToken cancellationToken)
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

        // QUEUED IS NOT "AT IT". The line used to say "still at it" for as long as the in-flight table
        // held the session, and on 2026-09-10 that was 29 minutes of a supervisor that had not begun.
        var text = queued
            ? Build_QueuedNarration(Describe_Speaker(orchId), isFirst, waitedFor)
            : isFirst
                ? Build_FirstNarration(Describe_Speaker(orchId), activity)
                : $"{Describe_Speaker(orchId)}: still at it{(activity == null ? "" : $" — {activity}")} · your message has been waiting {waitedFor}";

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
                // NO CANVAS: two different states, and reading them the same way is what silently
                // switched this narration off for every orchestration when the tick became a
                // reaction (brief D).
                //
                // A FAILED EDIT still says nothing (owner, 2026-09-09: "never a second message for
                // the same state"). That branch is how they got the edited receipt AND a separate
                // "mid-task…" message at 17:03: the id was cleared by a failure and the fallback
                // then SENT, so the message was the second about one state.
                //
                // A RECEIPT THAT WAS A REACTION is not that case. There is no bot message and there
                // never was one, so this line is the FIRST about the state, not the second — and
                // brief D keeps the busy sentence "as a message only when it carries information",
                // which after three minutes of waiting it does. It is sent once and EDITED on every
                // repeat afterwards, which is what NarrationMessageId is for, so a long turn still
                // costs exactly one line.
                if (!pending.ReceiptWasReaction)
                {
                    _log.Log_Info(orchId, "Busy narration had no receipt to edit — saying nothing rather than sending a second message about the same state");

                    pending.LastNarratedUtc = now;
                    return;
                }

                var firstNarrationId = await Send_NarrationMessage_OrNull_Async(_telegramClient, pending.ThreadId, text, cancellationToken);

                if (firstNarrationId != null)
                    pending.NarrationMessageId = firstNarrationId;

                pending.LastNarratedUtc = now;
                return;
            }

            pending.LastNarratedUtc = now;
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        // Cost HERE: the owner's busy narration, and every later stage of the tick.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A TIMEOUT IS NOT "THE MESSAGE IS GONE" — IT IS "WE DO NOT KNOW", and the reset below
            // asserts the stronger fact from the weaker signal. This bug PRE-DATES the filter above
            // and would have been made reachable by it: without this guard, filtering a wedged
            // endpoint converts a timeout into a discarded message id, the next repeat SENDS instead
            // of EDITS, and one narration becomes a pile of them — the waterfall CLAUDE.md item 14
            // exists to prevent, arriving through the fix for something else.
            //
            // A timeout leaves the message very probably still there and still editable, so the ids
            // are KEPT and the next repeat edits as normal. If it really is gone, the edit fails
            // again with a non-timeout error and the reset runs then.
            //
            // NOT PINNED, AND THE CONSTANTS ABOVE ARE WHY — do not read the absent test as an
            // oversight. This branch is only observable on the SECOND narration: the first must fail
            // with a timeout, and the repeat must then be watched to see whether it EDITS (ids kept,
            // correct) or SENDS (ids cleared, the waterfall). NARRATION_FIRST_DELAY_SECONDS = 45 plus
            // NARRATION_REPEAT_SECONDS = 180 puts the earliest observation 225 SECONDS out, against a
            // suite that runs in about 80 — and a slow suite stops being run, which trades one pinned
            // `if` for an unmeasured everything.
            //
            // Making those two windows injectable unlocks this, Announce_SupervisorFree_Async's
            // fallback skip below, rev-5's R2 on the nudge windows and imp-6's G3 — four blocked tests,
            // one seam. Deferred until after the merge on purpose: this file is touched by fourteen
            // branches and is the worst hotspot on the conflict map, so the seam is worth building and
            // building it here first is not.
            // TWO BUCKETS FOR A THREE-BUCKET WORLD was the first version of this line, and rev-6 was
            // right to file it: `ex is not OperationCanceledException` established the invariant for
            // exactly ONE weak signal. A Wi-Fi drop throws HttpRequestException, which said "gone",
            // cleared both ids, and produced the very waterfall the guard exists to prevent — plus a
            // third message, because a destroyed receipt id also pushes Announce_SupervisorFree down
            // its null-receipt path.
            //
            // A TRANSPORT failure never tells us the message is gone; it tells us the round trip did
            // not complete.
            //
            // CLASSIFIED THROUGH THE ONE PLACE THAT DECIDES IT, and this line is why. rev-9's F1 was
            // "one class, two predicates, in one commit"; the first fix lifted the topic-name copy into
            // TelegramAttempt_Gate and left this one written out inline. They then AGREED, which is not
            // the same as being one rule — decision 12's "all agreeing today and none joined to the
            // others" is exactly two copies that match until one of them is edited. Worse here than the
            // general case: the lifted copy is pinned by seven controls and this one is not asserted by
            // anything, so a drift would be silent in precisely this direction.
            //
            // THE CLASSIFICATION IS SHARED; THE CONSEQUENCE IS NOT. This site decides whether to DISCARD
            // state, the topic-name site decides whether to SUPPRESS RETRIES — opposite actions on the
            // same question, and collapsing them to make the sharing tidier would trade one defect for
            // another.
            var couldNotReachTelegram =
                TelegramAttempt_Gate.Classify_Failure(ex) == TopicNameAttemptOutcomes.OutcomeUnknown;

            // THE LIMIT THAT USED TO BE STATED HERE IS CLOSED. A 429 and every 5xx were
            // indistinguishable from a genuine "the message is gone" 400, because the client threw a
            // plain Exception for any non-2xx and the status was formatted into a message string and
            // lost — not unavailable, DISCARDED. TelegramApiException now carries it, so a retryable
            // status reaches this predicate as OutcomeUnknown and the ids are KEPT.
            //
            // That was the case rev-6's F5 described: a 429 during a burst cleared both ids, the next
            // repeat SENT instead of EDITING, and the owner got the decision-14 waterfall — plus a third
            // message, because a destroyed receipt id also pushes Announce_SupervisorFree down its
            // null-receipt path. It took three findings from three reviewers before the shared-client
            // change was priced against the whole pattern rather than one symptom at a time.
            //
            // It is strictly better than what it replaces — master cleared unconditionally on all of
            // these — and it is NOT the whole invariant. Do not read it as established.
            var lostTheMessage = !couldNotReachTelegram;

            // A failed EDIT must not freeze the narration forever on a dead message id: drop it so
            // the next repeat sends a fresh line and starts editing that one instead. A receipt we
            // cannot edit is dead for the turn-ended announcement too, so it goes with it —
            // otherwise the same dead id would be re-adopted as the canvas on every later repeat.
            if (lostTheMessage)
            {
                if (isReceiptCanvas)
                    pending.ReceiptMessageId = null;

                pending.NarrationMessageId = null;
            }

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
    /// <summary>
    /// "Turn ended" alone is a READINESS signal — it says a message would be picked up now. What the
    /// owner asked for on 2026-08-25 is a COMPLETION signal, and the difference is content: *"the
    /// terminal completes the operation and stops, and I haven't received anything telling me
    /// 'done'."*
    ///
    /// WHAT THIS USED TO DO AND NO LONGER NEEDS TO. A session's closing report ("merged, 214 tests
    /// green") was narration by shape — no question, no marker — so OwnerPush_Policy suppressed it,
    /// and this method rescued it from the suppressed-entry store to serve as the completion the
    /// owner had asked for. Nothing is suppressed since 2026-09-09: the report reaches them, rung
    /// and rendered, at the moment it is written. So what is left here is the tick itself, which is
    /// the app saying the turn ended — silent, and only when it says something the owner does not
    /// already have.
    /// </summary>
    /// <summary>
    /// The FIRST busy line when the receipt was a reaction, so there is nothing to edit yet. Silent,
    /// like everything the app writes about itself (brief C), and its id becomes the canvas every
    /// later repeat edits — one line for a turn of any length.
    /// </summary>
    async Task<long?> Send_NarrationMessage_OrNull_Async(ITelegramApiClient client, long? threadId, string narration, CancellationToken cancellationToken)
    {
        try
        {
            return await client.Send_Message_Async(threadId, narration, TelegramSendSounds.Silent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best effort by construction: the owner already has the typing bubble, and a busy
            // sentence that failed to send is not worth failing a tick for.
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Busy narration could not be sent: {ex.Message}");
            return null;
        }
    }

    (string? Text, bool IsCompletion) Build_TurnEndedText(string orchId, PendingOwnerReply pending)
    {
        var speaker = Describe_Speaker(orchId);

        if (!pending.Answered)
        {
            // THE ✓✓ PREFIX ONLY WHERE A ✓ WAS ACTUALLY SHOWN. Since brief D the ordinary receipt
            // is a reaction on the owner's own message, so this line would open by referring to a
            // tick they never saw — and on the reaction path it is a fresh message rather than an
            // edit of that tick, which makes the reference doubly wrong.
            var prefix = pending.ReceiptWasReaction ? "" : "✓✓  ·  ";

            return ($"{prefix}{speaker}: turn ended — free now, they are reading this", false);
        }

        // THE "LAST WORDS" HALF IS GONE WITH THE FILTER (2026-09-09). It existed to rescue a
        // closing report that OwnerPush_Policy had suppressed as narration — and nothing is
        // suppressed any more, so by the time a turn ends the owner has ALREADY read those words,
        // rung, rendered, at the moment they were written. Replaying them under a "turn ended" line
        // would be the same message twice.
        string? lastWords = null;

        // ANSWERED, AND NOTHING WAS LEFT UNSAID: the answer the owner is reading IS the completion,
        // and the bubble going down under it says the turn ended. "done for now — turn ended" after
        // it was the second of two status messages per exchange (owner, 2026-09-07). Null, not a
        // line: the caller sends nothing.
        if (string.IsNullOrWhiteSpace(lastWords))
            return (null, true);

        // The entry's own text carries its speaker glyph already, so this adds only the fact the
        // owner cannot see from it: that the session has STOPPED, rather than being mid-sentence.
        return ($"{lastWords}\n\n✓✓  ·  turn ended — {speaker} is free", true);
    }

    async Task Announce_SupervisorFree_Async(string orchId, PendingOwnerReply pending, CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        if (Resolve_EffectiveMode(orchId) != TelegramDeliveryModes.Normal)
            return;

        var (turnEndedText, isCompletion) = Build_TurnEndedText(orchId, pending);

        if (turnEndedText == null)
        {
            _log.Log_Info(orchId, "Turn ended after the owner was answered — nothing further to say, nothing sent");
            return;
        }

        // The narration line, when one was drawn, is the message the owner is looking at; the tick
        // is the fallback. Neither exists for a free recipient answered inside the narration delay,
        // which is the case that now says nothing at all above.
        var canvasMessageId = pending.NarrationMessageId ?? pending.ReceiptMessageId;

        // No receipt to edit — one failed narration edit is enough to drop the id — so SEND it.
        // The owner's complaint that created this announcement was being left watching a "busy"
        // line that never changed, and a transient Telegram error silently reproducing that exact
        // silence is the same defect wearing a different hat.
        //
        // A COMPLETION IS SENT, NEVER EDITED, and this is the one place decision 14 does not reach.
        // "Repeats edit, they never stack" is about a line that keeps SAYING THE SAME THING — the
        // busy narration counting up, the ✓ becoming ✓✓. A Telegram edit raises NO notification, so
        // an edit is precisely how you tell someone something without telling them: the owner's
        // complaint is that a finished job reaches them as silence, and quietly rewriting a receipt
        // they have already read reproduces it exactly. New information the owner is waiting for
        // gets a message; a repeat of information they have gets an edit.
        if (canvasMessageId == null || isCompletion)
        {
            // WRAPPED AT THE CALL SITE, NOT IN THE SHARED METHOD. This call sat outside any try, and
            // Send_DirectReply_BestEffort_Async's own OperationCanceled catch is bare — so a Telegram
            // timeout escaped this method, escaped Resolve_PendingOwnerReplies_Async, and killed the
            // rest of the tick, from a method whose name promises BEST EFFORT.
            //
            // The shared method's catch is deliberately left alone: it has callers this change has not
            // read, and filtering it would decide for all of them at once. The narrow fix belongs where
            // the unprotected call is.
            //
            // The route here is reached when the receipt id is null, which is what a failed narration
            // edit produces — so the two sites compound, and the conditional reset in
            // Narrate_BusySupervisor_Async narrows how often that happens without closing it.
            try
            {
                // RENDERED (owner's decision, 2026-09-09). This is the second of the two paths that
                // RESEND WHAT THE SUPERVISOR ALREADY WROTE — a completion carries their last words
                // above the tick — and it went out as plain text, so their Markdown arrived with the
                // markers showing. It is also silent: the words themselves already rang when they
                // were mirrored; this is the app saying the turn ended.
                await TelegramProse_Sender.Send_Async(
                    _telegramClient, _log, orchId, pending.ThreadId, turnEndedText, TelegramSendSounds.Silent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(orchId, $"Turn-ended announcement send failed: {ex.Message}");
            }

            return;
        }

        try
        {
            await _telegramClient.Edit_MessageText_Async(canvasMessageId.Value, turnEndedText, cancellationToken);
        }
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        // Cost HERE: the owner is never told the turn ended, and the rest of the tick goes with it.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // THE LINE MUST MATCH WHAT ACTUALLY HAPPENS NEXT. "sending it instead" was written when
            // every failure fell through to the fallback; on the timeout path it does not, so the two
            // outcomes get two lines. A log that narrates the branch not taken is worse than none —
            // whoever reads it is reconstructing a failure they cannot reproduce.
            if (ex is OperationCanceledException)
                _log.Log_Error(orchId, $"Turn-ended announcement DROPPED for this turn — the endpoint timed out and the announcement does not retry: {ex.Message}", ex);
            else
                _log.Log_Warning(orchId, $"Turn-ended announcement edit failed, sending it instead: {ex.Message}");

            // A FALLBACK IS FOR "THAT CALL FAILED", NOT FOR "THE ENDPOINT IS UNREACHABLE". After a
            // timeout the fallback send is against an endpoint that has just proved it does not
            // answer, so it is guaranteed to fail and costs a SECOND HttpClient timeout inside the
            // same tick — two ~90-second waits in a loop that ticks every 2 seconds, which is worse
            // than the abort the filter above removes.
            //
            // AND THE ANNOUNCEMENT IS LOST FOR THIS TURN. An earlier version of this comment claimed
            // "the next tick finds the turn still ended and tries again" — that was FALSE and is
            // corrected here rather than quietly deleted, because it justified the early return with
            // an outcome the code does not produce. The caller sets `pending.TurnEndAnnounced = true`
            // BEFORE calling this method, and that field has exactly ONE assignment in this file and no
            // reset anywhere, so the guard can never re-enter. Master lost it too — its bare rethrow
            // unwound with the latch already set — so this is not a regression, but the return makes
            // the loss QUIETER and the log line below now says so plainly.
            //
            // THE REAL DEFECT IS LATCHING BEFORE THE CALL, and it is not fixed here: recording work as
            // done before it has succeeded is the class imp-9 owns across seven sites, and two members
            // fixing one class in two branches is how a merge grows conflict regions. Named, not taken.
            //
            // NOT PINNED, AND THE COST WAS MEASURED RATHER THAN GUESSED. Reaching here needs
            // `pending.LastNarratedUtc != default`, so a test must first spend NARRATION_FIRST_DELAY_
            // SECONDS = 45 and then flip the supervisor from mid-turn to free by rewriting usage files
            // under a running engine. Worse, the harness cannot currently tell the two outcomes apart:
            // FailableTelegram_Fake.Count_Attempts_Containing counts by TEXT FRAGMENT and the edit
            // above and the fallback send below both carry turnEndedText, so it needs a fake that
            // records the METHOD — plus a positive control proving a NON-timeout failure still sends
            // the fallback, because asserting only the absence is the nothing-is-ALLOW trap.
            //
            // ~60 s and a harness change to pin one `if`. The same seam named at Narrate_BusySupervisor_
            // Async covers this too; see there for why it is deferred until after the merge.
            if (ex is OperationCanceledException)
                return;

            // Same reasoning as above: the signal matters more than which message carries it.
            await Send_DirectReply_BestEffort_Async(_telegramClient, pending.ThreadId, turnEndedText, cancellationToken);
        }
    }

    static string Build_FirstNarration(string speaker, string? activity)
    {
        var doing = activity == null ? "mid-task" : $"mid-task — {activity}";

        return $"{speaker}: {doing}. Your message is delivered; they pick it up when this turn ends.";
    }

    /// <summary>
    /// The honest line for a session whose turn is admitted but has no slot: it is not working on
    /// anything, so "mid-task" and "still at it" would both be false. Same wording as the status
    /// surfaces (<see cref="MemberWorking_Decider.QUEUED_WORDS"/>), so the phone and the card agree.
    /// </summary>
    internal static string Build_QueuedNarration(string speaker, bool isFirst, string waitedFor)
    {
        return isFirst
            ? $"{speaker}: {MemberWorking_Decider.QUEUED_WORDS}. Your message is delivered; they pick it up the moment a slot frees."
            : $"{speaker}: {MemberWorking_Decider.QUEUED_WORDS} · your message has been waiting {waitedFor}";
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

            var ownerAnswerCount = Count_OwnerAnswerEntries(ownerChannel);

            // Answered: the session that talks to the owner wrote back — the supervisor of a crew,
            // or the solo of a basic orchestration. The mirrored entry IS the feedback.
            //
            // ANSWERED IS NOT FINISHED, so this no longer deletes the tracker. The reply that lands
            // here is nearly always the receipt the role commands mandate BEFORE the work begins,
            // and dropping the tracker on it left nothing in the app watching for the turn to end —
            // so a job the owner asked for could be done, the terminal go quiet, and they be told
            // nothing at all. Marking it instead keeps the ONE thing that matters afterwards: the
            // turn-end announcement below. The nudge is disarmed by the same flag, because the owner
            // HAS been answered and must never be told otherwise.
            if (!pending.Answered && ownerAnswerCount > pending.OwnerAnswerCountAtDelivery)
                pending.Answered = true;

            var supervisorUsageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, orchId, _store.Get_Session_OrNull(orchId));

            var supervisorBusy = Is_Working(
                Running.SessionRoles.Supervisor, orchId,
                Running.SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID, supervisorUsageFile);

            // ASKED SEPARATELY, because Is_Working folds Queued into "occupied" on purpose (a queued
            // session cannot be disturbed either) — but what the owner is TOLD must not.
            var supervisorQueued = Resolve_MemberWorking(
                Running.SessionRoles.Supervisor, orchId,
                Running.SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID) == WorkingVerdicts.Queued;

            // THE BUBBLE IS THE WHOLE "THINKING…" STORY NOW: up while the session is mid-turn, and
            // while a free session has not yet picked the message up — down at the nudge, the one
            // moment "an answer is coming" stops being true enough to imply. After the answer it
            // stays up only while the session is still working, which is exactly what it means.
            if (_telegramClient != null
                && Resolve_EffectiveMode(orchId) == TelegramDeliveryModes.Normal
                && (supervisorBusy || (!pending.Answered && !pending.Nudged)))
            {
                await Show_Typing_BestEffort_Async(_telegramClient, pending.ThreadId, cancellationToken);
            }

            // The communicator's whole job, done from this loop: while the supervisor is mid-turn
            // and the owner is waiting, say concretely what it is doing. First after ~45 s (an idle
            // supervisor answers for itself well inside that, which is the better outcome), then
            // every ~3 minutes for as long as it stays busy.
            if (supervisorBusy)
            {
                await Narrate_BusySupervisor_Async(orchId, pending, supervisorUsageFile, supervisorQueued, cancellationToken);

                // AND TELL THE SESSION, not only the owner. This `continue` used to skip everything
                // below it, including the one channel entry that says "the owner is still waiting for
                // your reply" — so for exactly as long as a session stayed busy, the owner was
                // narrated at and the session was told NOTHING. A session that works for hours (an
                // Arb Studio supervisor does) therefore never learned a question had arrived, and the
                // owner watched "still at it" answer nothing.
                //
                // Their report, 2026-08-25: *"I asked him several times how the live following
                // implementation was going, and he never responded."* Not a slow answer. No answer.
                //
                // IT IS NOT THE NUDGE, deliberately. That one sets `Nudged`, which starts the ORPHAN
                // CLOCK — a member that does not wake within ORPHAN_CONFIRM_MINUTES is killed and
                // respawned. Arming that against a session whose only offence is being MID-TURN would
                // destroy healthy work for failing to answer inside a window it was never idle in.
                // This is a lighter thing: one agent-tagged entry, once, that costs the owner nothing
                // and arms nothing.
                //
                // The append is itself what delivers it: the session's watcher fires on the channel
                // changing, so the entry is waiting to be read at the end of the turn it is currently
                // inside — which is the first moment it could act on it anyway.
                // NOT WHILE QUEUED. "You have been mid-turn since it arrived" is false of a session
                // that has not started, and the entry would only be read when the queued turn — which
                // already carries the owner's message — finally runs. Written once it is running.
                if (!supervisorQueued
                    && !pending.BusyNoticeWritten
                    && (DateTime.UtcNow - pending.DeliveredUtc).TotalSeconds >= OWNER_REPLY_GRACE_SECONDS)
                {
                    pending.BusyNoticeWritten = ChannelAppender.Append_AppEntry(
                        ownerChannel, AppEntryAudiences.Agent,
                        "the owner is waiting on you — answer them at your next boundary",
                        "A message from the owner above is still unanswered and you have been mid-turn since it arrived, so nothing has told you until now.\n\n"
                        + "You are NOT being asked to stop what you are doing. Answer at your next boundary — one line is enough, and saying what you are in the middle of counts as an answer. If they asked something your current work does not touch, answer THAT rather than reporting progress they did not ask for.",
                        DateTime.Now);
                }

                continue;
            }

            // The turn the owner was waiting on has ENDED. Say so, once.
            //
            // TWO WAYS TO EARN THIS LINE, and the second is new. The first is the old one: the owner
            // was shown a "busy" line and it must stop lying. The second is that the session ANSWERED
            // and has now gone quiet — which is the small-job case, where no narration ever fired
            // because the whole thing took less than the 45 s that arms it, and the owner was
            // therefore told nothing at all from the receipt onwards.
            if (!pending.TurnEndAnnounced && (pending.Answered || pending.LastNarratedUtc != default))
            {
                pending.TurnEndAnnounced = true;
                await Announce_SupervisorFree_Async(orchId, pending, cancellationToken);
            }

            // ANSWERED AND IDLE — the tracker has done its whole job and must not outlive it. The
            // nudge below is deliberately downstream of this `continue`: the owner HAS been answered,
            // so telling the session it never replied would be false, and telling the owner an answer
            // is coming would be worse.
            if (pending.Answered)
            {
                lock (_ownerStateLock)
                {
                    _pendingOwnerReplies.Remove(orchId);
                }

                continue;
            }

            if (pending.Nudged || (DateTime.UtcNow - pending.DeliveredUtc).TotalSeconds < OWNER_REPLY_GRACE_SECONDS)
                continue;

            // Neither the memo NOR the owner-facing receipt may run ahead of the nudge. `Nudged` is
            // one-per-pending-reply forever, and the receipt below tells the owner "nudged, an
            // answer is coming" — so a failed append here would burn the only nudge this reply ever
            // gets AND assert to the owner that a message was sent that does not exist.
            if (!ChannelAppender.Append_AppEntry(
                    ownerChannel, AppEntryAudiences.Agent,
                    "the owner is still waiting for your reply",
                    "Your turn ended without answering the owner's message above. Reply now, even one line (what you are doing / what you are waiting on). The owner is looking at an unanswered receipt.",
                    DateTime.Now))
            {
                _log.Log_Warning(orchId, "Owner reply nudge could not be appended (channel locked) — NOT marked as nudged and the owner's receipt is left alone; the next tick retries");
                continue;
            }

            pending.Nudged = true;

            _log.Log_Warning(orchId, "Owner message went unanswered past the grace window — supervisor nudged");
            Raise_OrchestrationActivity(orchId);

            if (_telegramClient == null || Resolve_EffectiveMode(orchId) != TelegramDeliveryModes.Normal)
                continue;

            // THE SPEAKER IS RESOLVED, NEVER SPELLED. This line was the last hard-coded "🔴 Sup" in
            // the app and it survived the sweep that removed the others, so a basic orchestration —
            // which has no supervisor at all — kept telling the owner about one. Every other
            // owner-facing line here already asks Describe_Speaker; a literal beside them is a copy
            // that cannot be kept in step.
            var text = $"✓✓  ·  {Describe_Speaker(orchId)}: turn ended without a reply — nudged, an answer is coming";

            // The same canvas the busy narration draws on: a receipt that was never published (a
            // free recipient gets none now) does not turn this into a second message when a
            // narration line already stands.
            var nudgeCanvasMessageId = pending.NarrationMessageId ?? pending.ReceiptMessageId;

            try
            {
                if (nudgeCanvasMessageId != null)
                    await _telegramClient.Edit_MessageText_Async(nudgeCanvasMessageId.Value, text, cancellationToken);
                else
                    await Send_DirectReply_BestEffort_Async(_telegramClient, pending.ThreadId, text, cancellationToken);
            }
            // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
            // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
            // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
            // Cost HERE: every REMAINING orchestration's stale receipt — the owner keeps staring at one frozen on 'thinking…'.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
    /// <summary>
    /// Entries by whoever answers the owner here — supervisor OR solo. Named for the QUESTION rather
    /// than for one of the two roles that answer it: as `Count_SupervisorEntries` it read as correct
    /// while being permanently zero on every basic orchestration, which is what kept the "the owner
    /// is still waiting" nudge firing for messages that had been answered.
    /// </summary>
    static int Count_OwnerAnswerEntries(string channelFile)
    {
        return ChannelHistory_Counter.Count_OwnerFacingEntries(channelFile);
    }

    /// <summary>
    /// Turns the last ✓ of the batch into the final receipt, in place. Falls back to sending a new
    /// message when there is nothing to edit or the edit fails (Telegram refuses very old edits) —
    /// but only when <paramref name="sendWhenNothingToEdit"/> says the text is worth a message of its
    /// own. A bare ✓✓ is not: it confirms what the typing bubble already implies, and as a fresh
    /// message it was one of the two status lines per exchange the owner asked to lose.
    /// </summary>
    async Task<(long? MessageId, bool WasReaction)> Publish_DeliveryReceipt_Async(ITelegramApiClient client, long? messageThreadId, string text, bool sendWhenNothingToEdit, CancellationToken cancellationToken)
    {
        // 👀 BECOMES 👌 (brief D). When the receipt was a reaction there is no bot message to edit,
        // and the owner's own bubble carries the state instead: seen, then picked up.
        //
        // TRIED FIRST, and it returns immediately on success — the ✓✓ text below belongs to the
        // fallback path only. A reaction that has since become unsettable (the owner deleted the
        // message) falls through to that path, which sends nothing unless the caller said the text
        // carries information, so a lost 👌 costs the glyph and never a new line in the topic.
        var reactedMessageId = Take_ReactedOwnerMessageId_OrNull(messageThreadId);

        if (reactedMessageId != null
            && await Try_React_Async(client, reactedMessageId.Value, OwnerReaction_Emoji.PICKED_UP, cancellationToken))
        {
            return (null, true);
        }

        var messageId = Take_ReceiptMessageId_OrNull(messageThreadId);

        if (messageId != null)
        {
            try
            {
                await client.Edit_MessageText_Async(messageId.Value, text, cancellationToken);
                return (messageId, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(GLOBAL_ORCH_ID, sendWhenNothingToEdit
                    ? $"Receipt edit failed, sending a new message: {ex.Message}"
                    : $"Receipt edit failed; the bare ✓✓ is not worth a new message, so none is sent: {ex.Message}");
            }
        }

        if (!sendWhenNothingToEdit)
            return (null, false);

        try
        {
            return (await client.Send_Message_Async(messageThreadId, text, TelegramSendSounds.Silent, cancellationToken), false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Receipt send failed: {ex.Message}");
            return (null, false);
        }
    }

    /// <summary>
    /// Downloads an owner-sent image beside the channel (media/) and references it with an
    /// 'IMAGE: &lt;path&gt;' line — the supervisor Reads the file to inspect the screenshot.
    /// </summary>
    /// <summary>
    /// Downloads the voice note and runs the CONFIGURED transcription command; the transcript
    /// becomes the message text, delivered like any owner text.
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

    /// <summary>
    /// Telegram's own ceiling for what a BOT may download — 20 MB. It is not a policy choice, which
    /// is why it is stated as a fact and not as a setting: past it the download call fails, so the
    /// only useful thing to do is say so before spending it.
    /// </summary>
    const long OWNER_DOCUMENT_MAX_BYTES = 20L * 1024 * 1024;

    /// <summary>
    /// A FILE THE OWNER ATTACHED, SAVED BESIDE THE CHANNEL AND NAMED IN IT.
    ///
    /// <para>
    /// Documents were dropped in silence: the parser knew text, photo and voice, so a file with no
    /// caption produced no owner message at all and the offset advanced over it. A file WITH a
    /// caption was worse — the caption arrived as an ordinary message, so the owner watched their
    /// words land and had every reason to think the file had landed too.
    /// </para>
    /// <para>
    /// IT NEVER SWALLOWS THE OWNER'S WORDS, which is why this returns text rather than null the way
    /// the voice path does. A caption is a message in its own right: whatever happens to the bytes,
    /// what they wrote reaches the session, and the entry says plainly whether the file came with
    /// it. When the file did NOT arrive the owner is also told directly, because a note in a channel
    /// they do not read is not an answer.
    /// </para>
    /// <para>
    /// THE NAME IS SANITISED, NOT TRUSTED — see <see cref="OwnerFileName_Sanitizer"/>: it comes from
    /// the sending device, and joining it to a folder unchecked is how a write lands outside that
    /// folder.
    /// </para>
    /// </summary>
    async Task<string> Build_DocumentEntryText_Async(
        Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message,
        string channelFile,
        string orchId,
        CancellationToken cancellationToken)
    {
        var document = message.Document
            ?? throw new Exception("Build_DocumentEntryText_Async called without a document");

        var describedName = string.IsNullOrWhiteSpace(document.FileName) ? "a file" : document.FileName;
        var caption = message.Text.Length == 0 ? $"(sent {describedName}, no caption)" : message.Text;

        // REFUSED BEFORE IT IS FETCHED when Telegram already told us how big it is. Spending the
        // download to discover a limit that was declared in the update is a slow way to fail.
        if (document.SizeBytes != null && document.SizeBytes > OWNER_DOCUMENT_MAX_BYTES)
        {
            await Refuse_OversizedDocument_Async(message, describedName, document.SizeBytes.Value, cancellationToken);

            return $"{caption}\n\n(The owner attached {describedName}, {Describe_Megabytes(document.SizeBytes.Value)} — over the 20 MB limit, so it was NOT downloaded. They were told to share a path instead.)";
        }

        try
        {
            var client = _telegramClient
                ?? throw new Exception("Document message arrived without a Telegram client");

            var bytes = await client.Download_File_Async(document.FileId, cancellationToken);

            // AND CHECKED AGAIN AFTER THE FACT, because `file_size` is optional in the update: a
            // document that declared nothing is only measurable once it is here.
            if (bytes.LongLength > OWNER_DOCUMENT_MAX_BYTES)
            {
                await Refuse_OversizedDocument_Async(message, describedName, bytes.LongLength, cancellationToken);

                return $"{caption}\n\n(The owner attached {describedName}, {Describe_Megabytes(bytes.LongLength)} — over the 20 MB limit, so it was discarded. They were told to share a path instead.)";
            }

            var mediaFolder = Path.Combine(Path.GetDirectoryName(channelFile)
                ?? throw new Exception($"Channel file '{channelFile}' has no parent folder"), "media");
            Directory.CreateDirectory(mediaFolder);

            var safeName = Telegram.OwnerFileName_Sanitizer.Sanitize(document.FileName, $"tg-doc-{message.UpdateId}");
            var filePath = Path.Combine(mediaFolder, $"tg-doc-{message.UpdateId}-{safeName}");

            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

            _log.Log_Info(orchId, $"Owner document downloaded to {filePath} ({bytes.LongLength} bytes)");

            return $"{caption}\n\nFILE: {filePath}\n(The owner sent this file — Read it to inspect it.)";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(orchId, $"Owner document download failed for '{describedName}'", ex);

            await Send_DirectReply_BestEffort_Async(
                _telegramClient!, message.MessageThreadId,
                $"📎 I could not download {describedName} — your message went through, the file did not. Send it again, or put it somewhere I can read and tell me the path.",
                cancellationToken);

            return $"{caption}\n\n(The owner attached {describedName} but downloading it FAILED: {ex.Message})";
        }
    }

    async Task Refuse_OversizedDocument_Async(
        Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message,
        string describedName,
        long sizeBytes,
        CancellationToken cancellationToken)
    {
        _log.Log_Warning(
            Describe_MessageOrch(message),
            $"Owner document '{describedName}' is {sizeBytes} bytes — over the {OWNER_DOCUMENT_MAX_BYTES}-byte Telegram download limit; refused with a reply");

        if (_telegramClient == null)
            return;

        await Send_DirectReply_BestEffort_Async(
            _telegramClient, message.MessageThreadId,
            $"📎 {describedName} is {Describe_Megabytes(sizeBytes)} — Telegram only lets me download files up to 20 MB. "
            + "Your message went through; the file did not. Put it somewhere I can read and tell me the path.",
            cancellationToken);
    }

    static string Describe_Megabytes(long sizeBytes)
    {
        return $"{sizeBytes / (double)(1024 * 1024):0.#} MB";
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

            // AND THE OWNER IS TOLD, in one line, exactly as the document path tells them. The two
            // paths were asymmetric for no reason anyone chose: a failed DOCUMENT got a reply, a
            // failed PHOTO got a log line and a sentence in the channel — which the AGENT reads and
            // the owner never does. So the owner watched a picture leave their phone, saw the ✓✓,
            // and the session it was meant for silently never had it. That is the silent-drop shape
            // this file has paid for twice (2026-09-07 mockups, 2026-09-08 IMAGE_PROCESS_FAILED).
            await Send_DirectReply_BestEffort_Async(
                _telegramClient!, message.MessageThreadId,
                "🖼 I could not download that image — your message went through, the picture did not. Send it again, or put it somewhere I can read and tell me the path.",
                cancellationToken);

            return $"{caption}\n\n(The owner sent an image but downloading it FAILED: {ex.Message})";
        }
    }

    /// <summary>
    /// The orchestrations this pass should reason about: the tick's own snapshot while a tick is
    /// running, and a fresh read from the store otherwise.
    ///
    /// The fallback is not defensive padding — it is the contract. Every sweep that calls this has
    /// the tick as its only caller today, and the day one of them is also called from a Telegram
    /// command it must read the disk rather than a roster some other thread happens to be holding.
    /// </summary>
    IReadOnlyList<IOrchestrationSession> Sessions_ThisTick()
    {
        return _sessionsThisTick ?? _store.Load_All();
    }

    /// <summary>
    /// Writes the mirror cursor — but ONLY when it says something the file does not already say.
    ///
    /// <para>
    /// <paramref name="force"/> is for shutdown, and it is not belt and braces: the skip above is
    /// only ever correct while <see cref="_persistedOffsets"/> is what the file holds, and the one
    /// thing that can break that is a write that failed. <c>Atomic_FileWriter</c> throws on failure
    /// and this method does not catch — so a failed write leaves the remembered cursor UNCHANGED and
    /// the next tick tries again — but the last write of the process has no next tick, so it does not
    /// get to rely on that.
    /// </para>
    /// </summary>
    void Persist_BridgeState(bool force = false)
    {
        lock (_stateLock)
        {
            var offsets = _tailer.Get_OffsetsSnapshot();

            if (!force && _persistedUpdateId == _lastUpdateId && Is_SameCursor(_persistedOffsets, offsets))
                return;

            BridgeState_Store.Save(_paths, offsets, _lastUpdateId, sendBudget);

            // AFTER the write, never before: remembering a cursor the disk never took is how the
            // skip turns into a lost cursor rather than a saved write.
            _persistedOffsets = offsets;
            _persistedUpdateId = _lastUpdateId;
        }
    }

    /// <summary>
    /// Whether two cursors would produce the same file. Same count and same value for every key —
    /// a channel that disappeared from the snapshot changes the count, so no key needs checking in
    /// the other direction.
    /// </summary>
    static bool Is_SameCursor(IReadOnlyDictionary<string, long>? persisted, IReadOnlyDictionary<string, long> current)
    {
        if (persisted == null || persisted.Count != current.Count)
            return false;

        foreach (var pair in current)
        {
            if (!persisted.TryGetValue(pair.Key, out var persistedOffset) || persistedOffset != pair.Value)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Writes the bridge's DECISION state to disk — every open question, every live button, every
    /// high-risk confirmation in flight, the nudge memory, the crash-loop counters and the
    /// dispatcher pause.
    ///
    /// <para>
    /// CALLED AT EACH DECISION EVENT, not on a timer. These change a handful of times an hour, not
    /// thirty times a minute like the cursor beside them, so writing on the event costs nothing and
    /// removes the window a tick-based save would leave: a tap taken in the two seconds before the
    /// next tick is exactly the tap most likely to be followed by whatever killed the process.
    /// </para>
    /// <para>
    /// TAKES BOTH LOCKS, IN THIS ORDER, and never calls anything that takes them again. The button
    /// registry and the owner-state maps are guarded separately and are read together only here;
    /// fixing the order in one place is what keeps that from becoming a deadlock the first time
    /// somebody adds a second reader.
    /// </para>
    /// <para>
    /// NEVER THROWS: the store swallows and reports its own write failures, because everything in
    /// the snapshot is still live in these fields and this process is unaffected by a disk that
    /// cannot take it.
    /// </para>
    /// </summary>
    void Persist_EngineState()
    {
        EngineStateSnapshot snapshot;

        // TAKEN AND RELEASED FIRST, never nested inside the two below. The close-confirmation lock is
        // held across sweeps and taps that know nothing about the button registry; entering it from
        // inside _buttonLock here would create the one ordering the rest of this file cannot see, and
        // the deadlock would surface as a bridge that stops answering the phone.
        List<CloseConfirmationRecord> closeConfirmations;

        lock (_closeConfirmationLock)
        {
            // ONE ROW PER PARKED REQUEST, not per button: the registry keys the confirm and the
            // decline separately off a single prompt, and a reader counting rows would see two
            // decisions where the owner sees one question.
            closeConfirmations =
            [
                .. _closeConfirmations.Values
                    .GroupBy(confirmation => confirmation.ParkedPath)
                    .Select(group => group.First())
                    .Select(confirmation => new CloseConfirmationRecord
                    {
                        ParkedPath = confirmation.ParkedPath,
                        OrchId = confirmation.OrchId,
                        Kind = confirmation.Kind,
                        MemberId = confirmation.MemberId,
                        Requester = confirmation.Requester,
                        AskedUtc = confirmation.AskedUtc,
                        ExpiresUtc = confirmation.ExpiresUtc,
                        PromptMessageId = confirmation.PromptMessageId,
                    }),
            ];
        }

        lock (_buttonLock)
        {
            List<PendingButtonRecord> buttons = [];

            // Written in _buttonOrder, not dictionary order, so the FIFO eviction the cap depends on
            // survives a restart in the same order it had before it.
            foreach (var data in _buttonOrder)
            {
                if (_buttonOptions.TryGetValue(data, out var button))
                    buttons.Add(button);
            }

            lock (_ownerStateLock)
            {
                snapshot = new EngineStateSnapshot
                {
                    OwnerAwaitingAnswer = [.. _ownerAwaitingAnswer],
                    NudgedAboutEntry = new Dictionary<string, string>(_nudgedAboutEntry),
                    PendingButtons = buttons,
                    OpenQuestions = [.. _openQuestions.Values],
                    PendingConfirmations = [.. _pendingConfirmations],
                    CloseConfirmations = closeConfirmations,
                    ConsecutiveRespawns = _watchdog.Get_ConsecutiveRespawns(),
                    ButtonGroupSequence = _buttonGroupSequence,
                    DispatchPausedUntilUtc = _dispatchPausedUntilUtc,
                    DispatchPauseReason = _dispatchPauseReason,
                };
            }
        }

        _engineStateStore.Save(snapshot);
    }

    /// <summary>
    /// The crash-loop counters as they were last persisted, so a tick that changed nothing does not
    /// rewrite the state file thirty times a minute. A COUNT AND A SUM rather than the dictionary
    /// itself: the only transition that matters here is a counter going up, and both move when one
    /// does.
    /// </summary>
    (int Slots, long Total) _persistedRespawnCounts;

    void Persist_EngineState_IfRespawnCountsMoved()
    {
        var counters = _watchdog.Get_ConsecutiveRespawns();
        var signature = (counters.Count, counters.Values.Sum(count => (long)count));

        if (signature == _persistedRespawnCounts)
            return;

        _persistedRespawnCounts = signature;
        Persist_EngineState();
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


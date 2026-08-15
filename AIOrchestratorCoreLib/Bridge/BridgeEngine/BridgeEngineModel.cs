using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using AIOrchestratorCoreLib.Bridge.PendingAnnouncements;
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
using AIOrchestratorCoreLib.Tailing;
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
    /// One alert per stall/budget EPISODE — cleared when traffic resumes (stalls only). Both are
    /// written ONLY after a confirmed send, so an alert nobody received never marks itself delivered.
    /// </summary>
    readonly HashSet<string> _stallAlertedOrchIds = [];
    readonly HashSet<string> _budgetAlertedOrchIds = [];
    /// <summary>When each member was nudged — the nudge doubles as the PROBE that proves a watcher exists.</summary>
    readonly Dictionary<string, DateTime> _nudgedMemberUtc = [];

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
    /// Lost on restart, which costs ONE extra nudge per member. Visible, cheap, self-correcting —
    /// and the alternative is a third meaning in the map that can respawn a session.
    /// </summary>
    readonly Dictionary<string, string> _nudgedAboutEntry = [];

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
    readonly Dictionary<string, DateTime> _ledgerDebtSinceUtc = [];
    readonly HashSet<string> _ledgerBehindReportedOrchIds = [];
    readonly Dictionary<string, string> _reportedLedgerShapeByOrchId = [];

    /// <summary>
    /// Since when NOTHING in this orchestration has been mid-turn — the clock behind the stale
    /// `- [>]` check. Removed the moment anything speaks, so it measures QUIET rather than age.
    /// </summary>
    readonly Dictionary<string, DateTime> _quietSinceUtc = [];

    /// <summary>Which stale-in-progress SET was last reported, so a fix to one line still leaves the rest heard.</summary>
    readonly Dictionary<string, string> _reportedStaleInProgress = [];
    readonly Dictionary<string, (string Line, DateTime SentUtc)> _lastHandoffLineByOrchId = [];
    readonly Lock _stateLock = new();
    readonly IOwnerDeliveryBuffer _ownerDeliveryBuffer = OwnerDeliveryBuffer_Factory.Create(OWNER_AGGREGATION_SECONDS, OWNER_HOLD_CAP_SECONDS);

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

    /// <summary>
    /// Presence of this file in an orchestration folder stops its supervisor dead. The name is
    /// FORWARDED from the marker rather than restated: two copies of a filename that a bash hook
    /// also hard-codes is one drift away from a block nothing can clear.
    /// </summary>
    public const string AWAITING_ANSWER_FLAG_FILE = Status.AwaitingAnswerFlag_Marker.FILE_NAME;

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
        // ONE allowance for the whole tick's WAITING. Without it this method's worst case is
        // "appends × the per-call budget", and four of the steps below append inside a
        // foreach(session) -> foreach(member) nest — so the member count was the multiplier and ten
        // members could spend ~15 s of waiting inside a 2 s loop, stalling the poll, the mirror, the
        // tailer, compaction and the status push behind it. Uncontended writes charge ~0 ms and are
        // unaffected; a spent allowance means blocked channels fail fast and retry next tick, which
        // is a defined path (logged, and the owner's message goes back in its buffer).
        using var tickAllowance = ChannelWrite_Lock.Open_TickAllowance(ChannelWrite_Lock.DEFAULT_TICK_ALLOWANCE);

        Process_PendingRequests();

        // After closes are processed, so a freshly-closed session is not immediately revived.
        _watchdog.Check_AndRestart_DeadSessions();

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
        await Push_GeneralDashboard_Async(cancellationToken);

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
            if (hold.LastAttemptUtc != default && DateTime.UtcNow - hold.LastAttemptUtc < TimeSpan.FromSeconds(MIRROR_RETRY_BACKOFF_SECONDS))
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
                await _telegramClient.Send_Message_Async(heldSession?.TelegramTopicId, key.AlertText, cancellationToken);

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

            // Someone is actually working, or worked inside the window: a long thinking turn or a turn
            // that has just ended, not a stall. Wider than "mid-turn" on purpose — see the method.
            if (Has_AnySessionWorkedWithin(session, STALL_ALERT_MINUTES))
                continue;

            // SAME SHAPE AS THE BUDGET ALERT, found by sweeping the file for it rather than by a
            // review: this took the token first, so both a meeting and a thrown send spent it on an
            // alert nobody received. It is less severe only because it has a release above (traffic
            // resuming clears it), so the loss is confined to the current stall rather than the
            // process — the token is now taken after a confirmed send, like the other two.
            if (_stallAlertedOrchIds.Contains(session.OrchId))
                continue;

            if (Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
                continue;

            // ONLY WHEN THE OWNER OWES A REPLY (their ruling, 2026-08-15). Quiet alone was the old
            // trigger and it fired on the owner's own silence: the session had nothing to do and was
            // idle exactly as designed, and they were told to wake something that was not asleep.
            // The other direction — the owner spoke and the SESSION went quiet — is already covered
            // by the reply nudge, which wakes the session instead of asking them to.
            if (!Status.OwnerOwesReply_Decider.Decide(
                    ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(_paths.Get_OwnerChannelFile(session.OrchId)))))
                continue;

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
                await _telegramClient.Send_Message_Async(session.TelegramTopicId, alertText, cancellationToken);

                // After a CONFIRMED send, so a failed one retries next tick.
                _stallAlertedOrchIds.Add(session.OrchId);
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

                var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId);

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

                    _nudgedMemberUtc[memberKey] = DateTime.UtcNow;
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
        if (Is_SessionMidTurn(OwnerFacingSession_Locator.Get_UsageFile(_paths, session.OrchId, session)))
            return;

        List<string> waitingMembers = [];

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null)
                continue;

            var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, member.MemberId);

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

        _log.Log_Warning(session.OrchId, $"Supervisor had unanswered reports from {string.Join(", ", waitingMembers)} — nudged");
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
        return true;
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

            // The kill and the respawn above have already happened and cannot be undone, so this
            // entry is the ONLY thing that tells the respawned session why it restarted and where to
            // resume. If it did not land, the session wakes with no explanation — and the owner must
            // not then be told the orphan was handled. Escalated rather than logged: an unexplained
            // respawn is a member that will sit there having lost its context and not know it.
            if (!ChannelAppender.Append_AppEntry(
                    Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, memberId), AppEntryAudiences.Agent,
                    // The constant, not the text: Nudge_Decider has to recognise this entry as the app's
                    // own wake rather than something to nudge the member about, and two copies of a string
                    // are two copies that can drift.
                    Nudge_Wording.RESPAWN_SUBJECT,
                    "Your previous session went idle with nothing listening for new traffic, so the app restarted you. Your files and this channel are intact — read it from the top of the unanswered traffic and continue. Arm your watcher with the baseline captured BEFORE you read.",
                    DateTime.Now))
            {
                _log.Log_Error(
                    session.OrchId,
                    $"{memberId} was respawned but the explanation could not be appended (channel locked) — it is awake with no idea why it restarted",
                    null);

                return;
            }

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
        // FILTERED — THE TOKEN DECIDES. An HttpClient timeout surfaces as a TaskCanceledException
        // with the token NOT cancelled, so the bare rethrow escalated a failed send into a shutdown.
        // Canonical account in Refresh_TopicStatusLines_Async; not repeated at each site on purpose.
        // Cost HERE: a member whose monitor is dead is not recovered, and the tick that would retry it goes too.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                _log.Log_Warning(session.OrchId, "Ledger is behind the supervisor's verdicts — flagged for the turn-end hook");
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
        _log.Log_Warning(session.OrchId, $"PLAN.md shape problems: {complaints.Count}");
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
        {
            _quietSinceUtc.Remove(session.OrchId);
            _reportedStaleInProgress.Remove(session.OrchId);
            return;
        }

        if (!_quietSinceUtc.TryGetValue(session.OrchId, out var quietSince))
        {
            _quietSinceUtc[session.OrchId] = DateTime.UtcNow;
            return;
        }

        var progress = Planning.PlanLedger_Parser.Parse_OrNull(
            UsageTotals_Reader.Read_Text_Safe(_paths.Get_PlanFile(session.OrchId)));

        var unworked = Planning.StaleInProgress_Detector.Find_UnworkedInProgressLines(
            progress, anySessionWorking: false, quietFor: DateTime.UtcNow - quietSince);

        if (unworked.Count == 0)
            return;

        // CONTENT-ADDRESSED, like the shape complaint: it re-fires when the offending SET changes,
        // so a session that fixes one line and leaves another still hears about the one it left.
        var signature = string.Join("\n", unworked);

        if (_reportedStaleInProgress.TryGetValue(session.OrchId, out var reported) && reported == signature)
            return;

        if (!Append_SupervisorAttention_UnlessMeeting(
                session.OrchId,
                "PLAN.md claims work that nobody is doing",
                Planning.StaleInProgress_Detector.Describe(unworked),
                presence))
            return;

        _reportedStaleInProgress[session.OrchId] = signature;

        // The debt AFTER the telling lands, so a session is never blocked by a demand it was never
        // sent — the deadlock this file has already paid for once.
        _ledgerDebtSinceUtc[session.OrchId] = DateTime.UtcNow;

        _log.Log_Warning(session.OrchId, $"PLAN.md claims {unworked.Count} line(s) in progress while nothing is running — flagged for the turn-end hook");
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
                await _telegramClient.Send_Message_Async(session.TelegramTopicId, alertText, cancellationToken);

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

        // The same gate the other outbound sites use. DND holds and Silenced drops, and a dashboard
        // that ignored the owner's own switch would be the loudest thing in the app.
        if (Resolve_EffectiveMode(ChannelDiscovery.GENERAL_ORCH_ID) != TelegramDeliveryModes.Normal)
            return;

        // Backoff after a failure. Without it the retry is a 2-second hammer at an endpoint that is
        // already failing — the shape that earns a bot a server-side throttle. The failure stamp is
        // what holds it off, because the text has not changed and so cannot.
        if (_generalDashboardFailedAtUtc != null
            && (DateTime.UtcNow - _generalDashboardFailedAtUtc.Value).TotalSeconds < MIRROR_RETRY_BACKOFF_SECONDS)
            return;

        Load_GeneralDashboardMessageId_Once();

        var text = Telegram.GeneralDashboard_Composer.Compose(Build_ProgressReportText(null));
        var action = Telegram.TopicStatusLine_Decider.Decide(text, _generalDashboardText, _generalDashboardMessageId);

        if (action == Telegram.TopicStatusActions.None)
            return;

        try
        {
            if (action == Telegram.TopicStatusActions.Edit && _generalDashboardMessageId != null)
            {
                await _telegramClient.Edit_MessageText_Async(_generalDashboardMessageId.Value, text, cancellationToken);
            }
            else
            {
                var messageId = await _telegramClient.Send_Message_Async(null, text, cancellationToken);

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
            var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(append.Channel.FilePath));

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
            // DELIBERATELY BARE — DO NOT "COMPLETE" THE SWEEP HERE. It was filtered once, in the
            // sixteen-site pass, and that was a REGRESSION which rev-6 caught; this comment replaces
            // the wrong one.
            //
            // The sweep's premise does not hold at this site. It assumed a bare rethrow kills the tick.
            // Here it does not: the ONLY caller is Mirror_Append_Async, whose own OperationCanceled
            // catch is ALREADY filtered, with a generic sibling that logs at ERROR and returns false so
            // the tailer re-emits the entry. A timeout was therefore caught one frame up and turned
            // into an orderly retry — the desired behaviour, already in place.
            //
            // Filtering here made a timeout fall into the catch below, which is written for "Telegram
            // rejected malformed HTML" — an instant 400 — and which FALLS THROUGH TO A SECOND LIVE CALL
            // (the plain-text send at the end of this method) against a host that has just proved it
            // does not answer. Two ~90-second waits inside a loop that ticks every 2 seconds. It also
            // misdiagnosed a network timeout as "HTML mockup send rejected", dropped the severity from
            // ERROR to WARNING, and — if the HTML send reached Telegram and only the RESPONSE timed out
            // — posted a DUPLICATE to the owner's topic.
            //
            // This is the rule stated ~100 lines below at Announce_SupervisorFree_Async and applied
            // there and at Publish_DeliveryReceipt_Async: A FALLBACK IS FOR "THAT CALL FAILED", NOT FOR
            // "THE ENDPOINT IS UNREACHABLE". Three identical shapes; two were reasoned about correctly
            // and this one was swept.
            //
            // THE TEST BEFORE FILTERING ANY SITE IS NOT "is it bare" — it is "does an escape here reach
            // an UNFILTERED frame, and does the generic catch below make another live call".
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
        // Send_MirrorChunk_Async is the twelfth of the original twelve and was REVERTED as a regression,
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

    void Process_PendingRequests()
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

        Process_StartRequests(pending);
        Process_AddImplementerRequests(pending);
        Process_PromoteOrchestrationRequests(pending);
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
            // THROUGH THE BUILDER, never spelled here. These two lines were the last copies of the
            // window-title rule, and they drifted the same way every other copy did: a solo's window
            // is titled "SOLO · <orch>" while this built "SOLO-1 · <orch>" from the member id, so a
            // basic orchestration's only terminal was the one window a rename could never find.
            var supervisorFragment = Spawning.SessionWindowTitle_Builder.Build_ForSupervisor(session.OrchId);
            TerminalWindow_Focuser.Try_Rename_ByTitleFragment(supervisorFragment, $"{supervisorFragment} · {name}");

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var memberFragment = Spawning.SessionWindowTitle_Builder.Build_ForMember(member.MemberId, session.OrchId);
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
                    Append_GeneralAppEntry(AppEntryAudiences.Owner,
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

                Append_GeneralAppEntry(AppEntryAudiences.Owner,
                    $"orchestration '{session.OrchId}' started",
                    $"Orchestration '{session.OrchId}' started on repo '{repo.Name}' ({repo.Path}). {crew} its Telegram topic appears on its first channel entry.");
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
                var session = _launcher.Add_Member(request.OrchId, request.Kind);
                var newMember = session.Members[session.Members.Count - 1];
                var kindWord = request.Kind.ToString().ToLowerInvariant();

                var briefingHint = request.Kind == MemberKinds.Reviewer
                    ? $"New reviewer '{newMember.MemberId}' spawned for orchestration '{request.OrchId}' — READ-ONLY (it cannot edit or commit). Its channel is {newMember.MemberId}/channel.md — brief it there, and the brief MUST name a review DEPTH (quick | standard | deep | max) and exactly what to review."
                    : $"New implementer '{newMember.MemberId}' spawned for orchestration '{request.OrchId}'. Its channel is {newMember.MemberId}/channel.md — brief it there.";

                // The REASON rides in the subject because App entries mirror subject-only — the
                // owner must never see a session appear (and burn tokens) without knowing why.
                Append_OrchestrationAppEntry(
                    request.OrchId, AppEntryAudiences.Owner,
                    $"{kindWord} '{newMember.MemberId}' added — {request.Reason}",
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
                    Append_OrchestrationAppEntry(
                        request.OrchId, AppEntryAudiences.Owner,
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
                        request.OrchId, AppEntryAudiences.Owner,
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
                    request.OrchId, AppEntryAudiences.Owner,
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
                    request.OrchId, AppEntryAudiences.Owner,
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
            _store.Close_Member(orchId, memberId);
            SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_ImplementerPidFile(orchId, memberId));

            Append_OrchestrationAppEntry(
                orchId, AppEntryAudiences.Owner,
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
                Delete_TelegramTopic_FireAndForget(orchId, session.TelegramTopicId.Value);

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

        if (_configProvider.Get_Current().TelegramItalianLayer)
            text = await _translator.Translate_ToItalian_Async(text, cancellationToken);

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
                cancellationToken);

            Remember_TopicMessage(session.TelegramTopicId, messageId);

            lock (_closeConfirmationLock)
            {
                _closeConfirmations[confirmData] = new CloseConfirmation { OrchId = request.OrchId, ParkedPath = parkedPath, Confirms = true, PromptMessageId = messageId };
                _closeConfirmations[declineData] = new CloseConfirmation { OrchId = request.OrchId, ParkedPath = parkedPath, Confirms = false, PromptMessageId = messageId };
            }

            _log.Log_Info(request.OrchId, $"Asked the owner to confirm closing '{request.OrchId}' (asked by {request.Requester})");
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
                List<long?> presenceCommands = [];

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

                    // ARRIVING FROM TELEGRAM AT ALL proves the owner is not at this orchestration's
                    // terminal, so terminal mode ends by itself — the same shape as the auto-unmute,
                    // and for the same reason: a mode they must remember to turn off is one they get
                    // trapped by. /pc excludes itself, or the command would be undone by its own
                    // delivery.
                    Flip_ToRemote_IfOwnerTextedFromTelegram(message.MessageThreadId, command == "pc");

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
                    // NOT "what's LEFT" any more, since 2026-08-13: in a topic the command prints the
                    // whole ledger, done and dropped rows included. Same class as the kit line that
                    // told supervisors it would shorten a long ledger for them — text promising the
                    // old behaviour, in the one place the owner reads BEFORE running the command.
                    //
                    // BOTH SCOPES, and the first attempt at this string got that wrong. "every row"
                    // is true in a topic and false in General, where Build_ProgressReportText emits
                    // one counts line per open orchestration and no rows at all. The phrasing it
                    // replaced — "what's LEFT to do" — happened to be true in both, because a count
                    // IS an answer to what is left. A correction has to be checked in every scope the
                    // thing it corrects runs in, or it is the same defect with a newer date.
                    ("progress", "This topic's task ledger, every row — in General, one line per orchestration"),
                    ("left", "Same as /progress"),
                    ("tasks", "The FULL ledger of this orchestration, done lines included"),
                    ("cost", "What this topic has cost, per session — in General, per orchestration"),
                    ("tokens", "Token and usage totals"),
                    ("limits", "5-hour and weekly usage limits"),
                    ("diff", "What the repo and worktrees ACTUALLY contain"),
                    ("imp", "Latest traffic of an implementer (/imp 2)"),
                    ("summary", "What is going on across all orchestrations"),
                    ("pending", "Open questions awaiting me"),
                    ("resume", "Wake EVERY session — use when the usage limit resets"),
                    ("clear", "Wipe THIS topic's messages (the sessions keep running)"),
                    // "THIS topic" WAS A LIE IN GENERAL, and this is the worst instance of the class
                    // the two entries above were fixed for: in General the BARE command takes the
                    // app-wide path (`Apply_ModeCommand_Async` — `session == null` routes to
                    // `Apply_AppWideMode_Async`, as that method's own docstring already said). So an
                    // owner reading "THIS topic — drop its messages" in the pinned General topic and
                    // tapping /mute silences EVERY orchestration — and Silenced DROPS rather than
                    // defers, so traffic from every session is destroyed until they notice. The reply
                    // does say "everywhere", but a correction after the fact is exactly what the
                    // /progress fix rejected as sufficient: the menu is what they read BEFORE tapping.
                    ("mute", "Toggle 🔕 this topic — drop its messages (in General: everywhere)"),
                    ("dnd", "Toggle 🌙 this topic — hold its messages for later (in General: everywhere)"),
                    ("mute_all", "Toggle 🔕 everywhere"),
                    ("dnd_all", "Toggle 🌙 everywhere"),
                    ("pc", "Toggle 💻 THIS topic — I'm at its terminal, don't text or block"),
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
    async Task Send_ProgressReport_Async(ITelegramApiClient client, long? messageThreadId, string command, CancellationToken cancellationToken)
    {
        var text = await Translate_LedgerText_Async(Build_ProgressReportText(messageThreadId), command, messageThreadId, cancellationToken);

        foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            await Send_DirectReply_BestEffort_Async(client, messageThreadId, chunk, cancellationToken);
    }

    /// <summary>
    /// The Italian layer, with the ledger's SHAPE checked on the way back — and the English original
    /// sent instead if it did not survive.
    ///
    /// This is the last step on the owner's directive path and was the only one with no guarantee:
    /// the whole message went through a `claude -p` subprocess and nothing compared what returned. A
    /// model handed forty rows, several near-identical, is being invited to summarise — and rule 11
    /// makes the Italian layer persisted and the owner's normal mode, so this is the production path
    /// rather than an edge case.
    ///
    /// The DECISION is in Planning.LedgerTranslation_Verifier, where the suite can reach it. This
    /// method is left with the call and the fallback, deliberately: two findings in a row landed
    /// inside this class, which is internal sealed and unreachable from the tests.
    ///
    /// THE FALLBACK IS NOT ANNOUNCED TO THE OWNER (rule 15): they cannot act on it, and the English
    /// text arriving in place of Italian is the signal. The log line is for us.
    /// </summary>
    async Task<string> Translate_LedgerText_Async(string englishText, string command, long? messageThreadId, CancellationToken cancellationToken)
    {
        if (!_configProvider.Get_Current().TelegramItalianLayer)
            return englishText;

        var translated = await _translator.Translate_ToItalian_Async(englishText, cancellationToken);

        // The translator returns the ORIGINAL on failure or timeout, by contract, so that case passes
        // the check rather than tripping a fallback for a translation that never happened.
        var shapeChange = Planning.LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(englishText, translated);

        if (shapeChange == null)
            return translated;

        // WHICH command, WHICH orchestration, and WHAT changed. This line is the whole diagnostic
        // surface for the failure the verifier exists to detect, because rule 15 correctly keeps it
        // off the owner's phone — so an unattributable "shape changed" would mean reproducing it by
        // hand to learn anything. The General topic names itself: see Resolve_LogScope_ForTopic.
        _log.Log_Warning(
            Resolve_LogScope_ForTopic(messageThreadId),
            $"/{command}: the Italian layer changed the ledger's shape ({shapeChange}) — sending the English original rather than a rearranged ledger");

        return englishText;
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
        var text = await Translate_LedgerText_Async(Build_TaskListText(messageThreadId), "tasks", messageThreadId, cancellationToken);

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
    /// The counts, then the ledger as written — every row, in the file's own order.
    ///
    /// It began as "what's left to do" and answered with up to forty raw lines including everything
    /// finished, which on a 207-line ledger is a message nobody reads. The answer to that was to hide
    /// rows; the owner overruled it on 2026-08-13 — "I want to see all the rows, it must not be
    /// truncated" — because hiding them hides the ledger author's failure to group into 7-8 macro
    /// tasks. Short message, short LEDGER: the length is the supervisor's problem, upstream of here.
    /// </summary>
    string Build_OrchestrationLedgerText(string orchId, string displayName)
    {
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(orchId)));

        if (progress == null)
            return $"{displayName}: no task ledger yet — the supervisor writes PLAN.md once you approve a direction";

        // NO `LEFT:` HEADER, since 2026-08-13: the block underneath now carries `[x]` and `[-]` rows
        // by the owner's own directive, so a header announcing what is left contradicts its own
        // content — and they would read that as a bug in the same breath as the fix they asked for.
        // The counts line above already says how much is left, in numbers.
        return $"{Build_OrchestrationCountsLine(orchId, displayName)}\n{Planning.PlanProgress_Formatter.Describe_Ledger(progress)}";
    }

    string Build_OrchestrationCountsLine(string orchId, string displayName)
    {
        var progress = Planning.PlanLedger_Parser.Parse_OrNull(Read_FileText_Safe(_paths.Get_PlanFile(orchId)));

        if (progress == null)
            return $"{displayName}: no task ledger yet";

        return $"{displayName}: {Planning.PlanProgress_Formatter.Describe_Counts(progress)}";
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
        foreach (var session in _store.Load_All())
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
            ? "💻 TERMINAL — I will not text this topic, and its supervisor will not stop to wait for a tap. Talk to it in its terminal; anything you send here puts it back to remote."
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
                + "so keep working unless what you asked actually gates the next step. Anything they send from "
                + "Telegram puts this topic back to remote, and you get an entry here when it happens."
            : "They are on their phone again (📱). Questions are texted, the awaiting-answer block is back on, and "
                + "the usual protocol applies: ask ONE question, with options, and stop.";

        var channelFile = orchId == ChannelDiscovery.GENERAL_ORCH_ID
            ? _paths.GeneralChannelFile
            : _paths.Get_OwnerChannelFile(orchId);

        Append_AppEntry_Safe(channelFile, Channels.AppEntryAudiences.Agent, subject, text, DateTime.Now);
        Raise_OrchestrationActivity(orchId);
    }

    /// <summary>
    /// Any owner message ARRIVING FROM TELEGRAM proves they are holding a phone, so it ends EVERY
    /// meeting rather than only the one whose topic they typed in — they cannot be at a terminal and
    /// texting. This was topic-scoped, which left terminal mode with a single exit and an owner who
    /// walked away without toggling leaving that orchestration silent indefinitely (rev-4 F6).
    /// The decision is <see cref="OwnerPresenceFlip_Planner"/>'s; this does the moving.
    /// </summary>
    void Flip_ToRemote_IfOwnerTextedFromTelegram(long? messageThreadId, bool isPresenceCommandItself)
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
                baseName, Resolve_EffectiveMode(session.OrchId), Is_AwayMode(), Is_Quiet(session.OrchId), session.OwnerPresence);

            if (_appliedTopicNames.TryGetValue(session.OrchId, out var applied) && applied == wantedName)
                continue;

            // AND THE SECOND QUESTION, WHICH USED TO BE THE SAME ONE. An attempt whose outcome we could
            // not learn holds this orchestration back for a WHILE — not for ever, which is what
            // recording it as applied did, and not for two seconds, which is what recording nothing did.
            var retryAfter = _topicNameRetryAfterUtc.TryGetValue(session.OrchId, out var stamp) ? stamp : (DateTime?)null;

            if (!TopicNameSync_Gate.Is_AttemptDue(retryAfter, DateTime.UtcNow))
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
                // THE THREE BUCKETS, decided in TopicNameSync_Gate where the suite can reach them. An
                // earlier version of this used `ex is not OperationCanceledException`, which is the
                // two-bucket test rev-6 proved insufficient for the identical decision one method away —
                // same class, two predicates, one commit. The predicate is now one predicate, and it
                // lives somewhere it can be tested.
                //
                // BACKOFF REUSES MIRROR_RETRY_BACKOFF_SECONDS (30 s) rather than inventing a value: this
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
                if (TopicNameSync_Gate.Classify_Failure(ex) == TopicNameAttemptOutcomes.OutcomeUnknown)
                    _topicNameRetryAfterUtc[session.OrchId] = TopicNameSync_Gate.Build_RetryAfterUtc(DateTime.UtcNow, MIRROR_RETRY_BACKOFF_SECONDS);
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
    /// silently for as long as it is the last thing in the topic.
    ///
    /// Four properties matter and each has a test:
    ///   - a change edits;
    ///   - an IDENTICAL line does nothing, because an edit that writes the same text is a wasted API
    ///     call and, against the 429 limit we already have open on the ledger, a real cost;
    ///   - a RESTART edits the existing message rather than posting a second one — the id is read
    ///     from session.json, not from memory;
    ///   - a line BURIED by later traffic, in a topic that has since been quiet for two minutes, is
    ///     deleted and written again at the bottom. Telegram cannot move a message, so this is the
    ///     only way to put the current state where the owner is looking when they enter the chat.
    ///
    /// The repost is the ONE action here that notifies, and everything about it is arranged so that
    /// it cannot become a waterfall: the quiet window bounds it to one ping per quiet period, the
    /// delivery gate blocks it in a silenced topic exactly as it blocks a first post, and it never
    /// fires while the line is already last.
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
                MIRROR_RETRY_BACKOFF_SECONDS,
                Find_NewestTopicMessage_OrNull(session.TelegramTopicId),
                _repostImpossibleOrchIds.Contains(session.OrchId));

            var action = plan.Action;
            var text = plan.Text;

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
                    await _telegramClient.Edit_MessageText_Async(session.StatusLineMessageId.Value, text, cancellationToken);
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

                    var messageId = await _telegramClient.Send_Message_Async(session.TelegramTopicId, text, cancellationToken);

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
                    _statusLineTextByOrchId[session.OrchId] = text;
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
        foreach (var session in _store.Load_All())
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
        foreach (var session in _store.Load_All())
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

                var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));

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
            var workingNow = SessionActivity_Probe.Is_MidTurn(Path.Combine(memberFolder, UsageTotals_Reader.SESSION_USAGE_FILE));

            var lastWrite = File.Exists(channelFile)
                ? $" · last wrote {SessionDuration_Formatter.Describe(DateTime.UtcNow - File.GetLastWriteTimeUtc(channelFile))} ago"
                : "";

            memberLines.Add($"- {member.MemberId}: {Describe_DeclaredState(declared, workingNow)}{lastWrite}");
        }

        // The header carries the ledger counts, so "who is doing what" and "how far along are we"
        // arrive in one answer — the owner asked for both without having to send /progress too.
        // Same builder /progress uses, so the two can never quote different figures.
        return Status.StatusRoster_Builder.Build(
            Build_OrchestrationCountsLine(session.OrchId, session.DisplayName ?? session.OrchId),
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
            var topicName = TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                baseName, Resolve_EffectiveMode(session.OrchId), Is_AwayMode(), Is_Quiet(session.OrchId), session.OwnerPresence);

            // Recreate rather than delete-by-id: it is the only way to leave the topic genuinely
            // empty, and it cannot touch a neighbouring topic by accident.
            await client.Delete_ForumTopic_Async(messageThreadId ?? throw new Exception($"orchestration '{session.OrchId}' has no topic id to clear"), cancellationToken);

            var newTopicId = await client.Create_ForumTopic_Async(topicName, cancellationToken);
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
        var channelFile = Channels.MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, memberId);
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
        foreach (var session in _store.Load_All())
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

    /// <summary>
    /// The log scope for an owner message, which is the topic question with the thread id already in
    /// hand. A THIN ADAPTER, not a second implementation: this method and the one used by the ledger
    /// translator answered the identical question forty lines apart and DISAGREED on the General
    /// branch — one returned "general", the other the empty string, and the empty one silently lost
    /// its diagnostic. Rule 12 is what makes that possible; one body is what closes it.
    /// </summary>
    string Describe_MessageOrch(Telegram.TelegramOwnerMessage.ITelegramOwnerMessage message)
    {
        return Resolve_LogScope_ForTopic(message.MessageThreadId);
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
            // were not — a translator that throws destroys the text outright, and any escape from the
            // loop destroys every delivery still to come in the batch as well.
            try
            {
                await Deliver_OwnerMessage_Async(delivery, cancellationToken);
            }
            catch (Exception exception)
            {
                // The ORIGINAL, never the possibly-half-translated working copy: a partially
                // translated string becoming the owner's message is worse than a late one, and it
                // would be near-impossible to diagnose from outside.
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

        var deliveryText = delivery.Value.Text;

        // Italian layer: the SESSION must only ever see English — translate the aggregated
        // owner text before it touches the channel. Already-English text passes unchanged.
        if (_configProvider.Get_Current().TelegramItalianLayer)
            deliveryText = await _translator.Translate_ToEnglish_Async(deliveryText, cancellationToken);

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
            // delivery.Value, THE ORIGINAL — not deliveryText. This put back the TRANSLATED string
            // until rev-9 caught it: with the Italian layer on, the buffer stopped holding the
            // owner's message and started holding a machine translation of it, which the retry then
            // ran through the translator AGAIN. The owner's words were replaced by a paraphrase of
            // themselves and re-paraphrased on every subsequent lock. Translation belongs on the way
            // OUT; nothing may put an output of that pipeline back into the input side.
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
        var speaker = Describe_Speaker(orchId);

        // The SESSION THAT TALKS TO THE OWNER, never "the supervisor": in a basic orchestration that
        // is the solo, and reading the empty supervisor slot made a working solo look idle.
        var supervisorUsageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, orchId, _store.Get_Session_OrNull(orchId));

        if (orchId == ChannelDiscovery.GENERAL_ORCH_ID)
        {
            return Is_SessionMidTurn(supervisorUsageFile)
                ? $"{speaker}: busy — will read this the moment the current turn ends"
                : $"{speaker}: thinking…";
        }

        if (!Is_SessionMidTurn(supervisorUsageFile))
            return $"{speaker}: thinking…";

        // Say WHAT it is doing, not just that it is busy — read straight off its transcript, which
        // is where the communicator used to read it, minus the session and the turn it cost.
        var activity = SupervisorActivity_Describer.Describe_OrNull(supervisorUsageFile);

        return activity == null
            ? $"{speaker}: busy mid-task — they'll pick this up when the current turn ends"
            : $"{speaker}: busy — {activity} — they'll pick this up when the current turn ends";
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
                Post_StatusEntry(session.OrchId, Build_AwayUpdateText(session), session.OwnerPresence);
                continue;
            }

            // Nothing running: the supervisor's rule was to stop the cadence, not to report
            // "no change" forever. The slot is spent above, so work starting mid-slot waits for
            // the next boundary like everyone else rather than firing on its own schedule.
            if (!Has_WorkInFlight(session))
                continue;

            Post_StatusEntry(session.OrchId, Build_PeriodicStatusText(session), session.OwnerPresence);
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

        // Return deliberately discarded, and this one is safe for a reason the memo sites are not:
        // the state recorded above is a COOLDOWN, not a record that the work is done. It expires by
        // itself after NUDGE_COOLDOWN_MINUTES, so a lost nudge costs at most one un-nudged message
        // rather than suppressing the nudge forever. Recording it after the append instead would
        // reopen the double-nudge race the lock above exists to close — a worse trade for a smaller
        // problem. The lock's own diagnostics report the failure either way.
        var nudged = ChannelAppender.Append_AppEntry(
            _paths.Get_OwnerChannelFile(orchId), AppEntryAudiences.Agent,
            "that message was too long for a phone",
            Brevity_Policy.Build_NudgeBody(mirroredText),
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
            await Send_AwayNotice_Async(session, AwayMode_Policy.AWAY_ON_NOTICE, cancellationToken);
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
                _openQuestions.Remove(entry.MessageId);
        }

        foreach (var entry in parked)
        {
            try
            {
                await _telegramClient.Edit_MessageText_Async(
                    entry.MessageId, $"{entry.Text}{AwayMode_Policy.PARKED_SUFFIX}", cancellationToken);
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
    void Post_StatusEntry(string orchId, string text, OwnerPresenceModes presence)
    {
        // Suppressed WITHOUT spending the slot during a meeting (see Push_PeriodicStatus_Async), so
        // the first tick after the owner leaves terminal mode posts a fresh status — which IS the
        // "what waited while we talked" summary, built by the formatter that already exists.
        //
        // The presence is the CALLER's — the one it already decided the slot on — so the decision
        // and the append cannot disagree about where the owner is (rev-7 P5).
        //
        // The helper's return is deliberately discarded, and only here: a dropped periodic status is
        // SUPERSEDED rather than lost — the next carries the current state, and the Deferred path
        // already collapses queued statuses to the newest for the same reason. Retrying it would
        // deliver a stale snapshot the following tick is about to replace. Nothing records it as
        // done, so nothing is left claiming work that did not happen.
        Append_SupervisorAttention_UnlessMeeting(orchId, MirrorText_Formatter.STATUS_SUBJECT_PREFIX, text, presence, Channels.AppEntryAudiences.Owner);
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
                pending.NarrationMessageId = await _telegramClient.Send_Message_Async(pending.ThreadId, text, cancellationToken);
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
            // TopicNameSync_Gate and left this one written out inline. They then AGREED, which is not
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
                TopicNameSync_Gate.Classify_Failure(ex) == TopicNameAttemptOutcomes.OutcomeUnknown;

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
    async Task Announce_SupervisorFree_Async(string orchId, PendingOwnerReply pending, CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        if (Resolve_EffectiveMode(orchId) != TelegramDeliveryModes.Normal)
            return;

        var turnEndedText = $"✓✓  ·  {Describe_Speaker(orchId)}: turn ended — free now, they are reading this";

        // No receipt to edit — one failed narration edit is enough to drop the id — so SEND it.
        // The owner's complaint that created this announcement was being left watching a "busy"
        // line that never changed, and a transient Telegram error silently reproducing that exact
        // silence is the same defect wearing a different hat.
        if (pending.ReceiptMessageId == null)
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
                await Send_DirectReply_BestEffort_Async(_telegramClient, pending.ThreadId, turnEndedText, cancellationToken);
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
            await _telegramClient.Edit_MessageText_Async(pending.ReceiptMessageId.Value, turnEndedText, cancellationToken);
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
            if (ownerAnswerCount > pending.OwnerAnswerCountAtDelivery)
            {
                lock (_ownerStateLock)
                {
                    _pendingOwnerReplies.Remove(orchId);
                }

                continue;
            }

            var supervisorUsageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, orchId, _store.Get_Session_OrNull(orchId));

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

            try
            {
                if (pending.ReceiptMessageId != null)
                    await _telegramClient.Edit_MessageText_Async(pending.ReceiptMessageId.Value, text, cancellationToken);
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


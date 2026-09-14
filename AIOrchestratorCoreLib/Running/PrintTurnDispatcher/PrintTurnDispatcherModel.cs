using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ClosingTurn;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnExecutor;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Running.PrintTurnDispatcher;

/// <summary>
/// THE QUEUE IS THE CHANNELS. A bridge-driven session has no process to hold a queue in, so its
/// pending work is defined by the files: every inbound entry of every channel it is woken by whose
/// identity is not yet in that channel's cursor is pending, and one turn takes all of them. That gives
/// FIFO per session, coalescing (entries that land within the window ride together, ACROSS channels),
/// and restart recovery (a bridge that died mid-turn finds the same entries pending) without a queue
/// object anywhere.
///
/// Per tick, per registered session: resolve its channels, read them, select the pending entries, wait
/// out the coalesce window, then start ONE turn on a background task — never more than one per session,
/// never more than the global and per-orchestration slots allow. The task runs the process, writes the
/// session's entries from the result — each part into the channel it was addressed to — records a
/// <c>turn_ended</c> entry, and persists the cursors. A failed attempt (timeout, non-zero exit,
/// <c>is_error</c>) is counted and retried after a backoff under the SAME request id; at
/// <see cref="MAX_ATTEMPTS"/> the session stalls — an alert entry, no further attempts — until the
/// pending set changes. ONE FAILURE IS NOT AN ATTEMPT: a usage-limit refusal that names its reset
/// buys an appointment instead (<see cref="Record_UsageLimit_IfNamed"/>), because three tries a
/// minute apart cannot outlast a quota window and spending them on one leaves the session with
/// nothing left when it reopens.
///
/// Idempotency: a request id already in the executed list is skipped without running. The prompt
/// of the first resumed turn after a bridge start lists the executed turns, so a transcript that
/// remembers answering does not answer twice (decision 8).
///
/// The slot limits are read ONCE, at construction — a semaphore cannot be resized — so changing
/// them in config.json takes effect at the next app start. Everything else (runner, resume mode,
/// timeout, coalesce window) is read on every tick.
///
/// <para>
/// HOW a turn reaches the model is NOT this class's business — that is
/// <see cref="ITurnExecutor"/>, picked per role from the configured runner. Everything above stays
/// identical whether the turn is one <c>claude -p</c> process or a line on a living one's stdin,
/// and that is the point: two transports must never grow two answers to "has this turn already
/// run".
/// </para>
/// <para>
/// NOR IS "WHICH CHANNELS" — that is <see cref="TurnSources_Resolver"/>, resolved fresh every tick so a
/// member added mid-life becomes a source of its supervisor's next turn with nothing re-registered.
/// </para>
/// </summary>
internal sealed class PrintTurnDispatcherModel : IPrintTurnDispatcher
{
    const int MAX_ATTEMPTS = PrintTurn_Words.MAX_ATTEMPTS;
    const string RUNNER_ENV_VAR = PrintTurn_Words.RUNNER_ENV_VAR;
    const string TURN_ENDED_SUBJECT = PrintTurn_Words.TURN_ENDED_SUBJECT;
    const string DEADLINE_KILLS_SUBJECT = PrintTurn_Words.DEADLINE_KILLS_SUBJECT;
    const string TURN_LIMITED_SUBJECT = PrintTurn_Words.TURN_LIMITED_SUBJECT;
    const string MISADDRESSED_SUBJECT = PrintTurn_Words.MISADDRESSED_SUBJECT;
    const string SUPERSEDED_FINAL_SUBJECT = PrintTurn_Words.SUPERSEDED_FINAL_SUBJECT;
    const int ENTRY_APPEND_ATTEMPTS = 3;
    const int ENTRY_APPEND_RETRY_MILLISECONDS = 300;
    /// <summary>
    /// How long cancelled turns get to observe their cancellation and unwind, AFTER the drain. Not
    /// the drain itself: that is <see cref="Get_DrainGrace"/>, everything one session's turn can
    /// still legitimately need, because a turn that is still running is worth exactly what it was
    /// worth before somebody asked the app to stop.
    /// </summary>
    static readonly TimeSpan CANCEL_GRACE = TimeSpan.FromSeconds(15);

    /// <summary>The bookkeeping either side of the turns the drain waits for — see <see cref="ClosingTurn_Rule.Resolve_DrainGrace"/>, which owns the sum.</summary>
    static readonly TimeSpan DRAIN_MARGIN = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The grace when config.json cannot be read at shutdown. COMPUTED from the default turn timeout
    /// rather than written out as a number: it was a literal 31 minutes, and when the closing turn
    /// added five minutes to what the path can need, the literal stayed where it was and said nothing
    /// (adversarial review, 2026-09-09).
    /// </summary>
    static readonly TimeSpan DRAIN_GRACE_FALLBACK = ClosingTurn_Rule.Resolve_DrainGrace(ShutdownGrace_Rule.DEFAULT_LONGEST_TURN_TIMEOUT, DRAIN_MARGIN);

    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IReadOnlyDictionary<SessionRunners, ITurnExecutor> _executors;
    readonly IOrchestrationLog _log;
    readonly TimeSpan _retryBackoff;

    readonly Lock _lock = new();
    readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// "No new turns" — the first half of stopping. Measured 2026-09-06→08 on the VPS: 21 daemon
    /// starts in 44 hours (deploys), and 17 in-flight turns, 112 M tokens, ended within four minutes
    /// of a `Daemon stopping` line — killed mid-work, their entries re-run from scratch at the next
    /// start. The turns ran under <see cref="_shutdown"/> alone, so Stop_Async cancelled them in the
    /// same instant it stopped admitting new ones. The two are now two signals: this one closes the
    /// door, <see cref="_shutdown"/> is pulled only when the drain grace has run out.
    /// </summary>
    readonly CancellationTokenSource _draining = new();

    /// <summary>
    /// KEYED CASE-INSENSITIVELY, because the key's INPUT is agent-written. Review finding 2026-09-10:
    /// <c>orchId</c> reaches a close straight from the request JSON with no trim and no
    /// canonicalisation, while the session store resolves an orchestration through a FILE PATH — which
    /// is case-insensitive on Windows, where the app runs. So a request saying <c>Fincanva-5</c> closed
    /// the member successfully and then missed every lookup here, which handed
    /// <see cref="Bridge.UndeliveredSpokeTraffic_Reporter"/> an empty set and resurrected the false
    /// alarm this branch exists to fix. Matching the store's own tolerance is the safe direction; the
    /// alternative is a lookup that silently disagrees with the operation that succeeded.
    /// </summary>
    readonly Dictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The subset of <see cref="_inFlight"/> that HOLDS ITS SLOTS and is executing. A key in the
    /// in-flight table and not here is queued behind the concurrency cap. Written under
    /// <see cref="_lock"/> the moment the last slot is acquired, removed with the in-flight entry.
    /// </summary>
    readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// WHAT EACH IN-FLIGHT TURN IS CARRYING, keyed exactly like <see cref="_inFlight"/> — written
    /// immediately before it in <see cref="Start_Turn"/> and removed beside it in
    /// <see cref="Execute_Turn_Async"/>'s <c>finally</c>, both under <see cref="_lock"/>.
    ///
    /// <para>
    /// NOT "the same two lines", which an earlier version of this comment claimed: the write precedes
    /// <see cref="_inFlight"/>'s by one line and the removal is in another method. If <c>Task.Run</c>
    /// itself threw, this would keep an entry for a turn that never existed and
    /// <see cref="Is_TurnInFlight"/> would say false while <see cref="Get_DeliveringIdentities"/> said
    /// otherwise — a permanent INFO where a WARNING is owed. Theoretical, and named here so the next
    /// reader checks rather than trusting a sentence (review finding, 2026-09-10).
    /// </para>
    ///
    /// <para>
    /// IT EXISTS BECAUSE THE CURSOR ANSWERS A DIFFERENT QUESTION. The cursor advances beside
    /// <see cref="Advance_Cursors"/> — when a turn COMPLETES — so for the whole of a turn's run the
    /// entries it is delivering still read as pending to anyone reading the state file. That is correct
    /// for delivery (a turn that fails must not lose them) and wrong for anyone asking "will these ever
    /// be handed over": measured in production 2026-09-10, that gap made
    /// <see cref="Bridge.UndeliveredSpokeTraffic_Reporter"/> announce a dropped final report 15 seconds
    /// before the turn carrying it succeeded.
    /// </para>
    /// <para>
    /// IDENTITIES, NOT INDEXES — <see cref="ChannelEntry_Digest"/>, the same key the cursor is built on
    /// (CLAUDE.md decision 12: the <c>[n]</c> is agent-written and has duplicated in production).
    /// </para>
    /// </summary>
    readonly Dictionary<string, IReadOnlySet<string>> _delivering = new(StringComparer.OrdinalIgnoreCase);

    static readonly IReadOnlySet<string> NOTHING_IN_FLIGHT = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>
    /// Case-insensitive for the same reason as <see cref="_inFlight"/>, and here it matters more: a
    /// differently-cased <c>orchId</c> would hand the session a FRESH tracker, i.e. a brand-new digest
    /// hold and a lost "has delivered traffic" flag.
    /// </summary>
    readonly Dictionary<string, SessionTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SemaphoreSlim> _orchestrationSlots = [];
    readonly HashSet<string> _warnedStaleRegistrations = [];
    readonly HashSet<string> _warnedArchiveGaps = [];
    readonly HashSet<string> _warnedBrokenSessions = [];
    readonly HashSet<string> _reportedConfigRejections = [];

    /// <summary>
    /// ONE comparer for source keys, everywhere. A session addresses a channel by a word it typed, so the
    /// lookup has to forgive case; the cursor set, the roster match and the factory's uniqueness check
    /// must then forgive it too, or two ids differing only in case pass validation and throw inside the
    /// turn — where the failure is caught, not counted, and retried for ever.
    /// </summary>
    static readonly StringComparer SOURCE_KEYS = StringComparer.OrdinalIgnoreCase;
    readonly SemaphoreSlim _globalSlots;
    readonly int _slotsPerOrchestration;

    public PrintTurnDispatcherModel(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestratorConfigProvider configProvider,
        IReadOnlyList<ITurnExecutor> executors,
        IOrchestrationLog log,
        TimeSpan retryBackoff)
    {
        _paths = paths;
        _store = store;
        _configProvider = configProvider;
        _executors = executors.ToDictionary(executor => executor.Kind);
        _log = log;
        _retryBackoff = retryBackoff;

        var limits = configProvider.Get_Current().Runners;
        _globalSlots = new SemaphoreSlim(limits.MaxConcurrentTurns, limits.MaxConcurrentTurns);
        _slotsPerOrchestration = limits.MaxConcurrentTurnsPerOrchestration;
    }

    /// <summary>What the dispatcher remembers about a session BETWEEN ticks and only until restart — nothing here is truth, the state file is.</summary>
    sealed class SessionTracker
    {
        /// <summary>
        /// The pending set as one string, so "has anything changed since I last looked" is one
        /// comparison across every channel at once. It is built from entry IDENTITIES and never from
        /// indices or counts — the same reason the cursor is (see <see cref="ChannelEntry_Digest"/>).
        /// </summary>
        public string PendingSignature = string.Empty;

        public DateTime PendingSeenAt;

        /// <summary>
        /// WHEN THE MEMBER TRAFFIC NOW WAITING FIRST TURNED UP — the digest's clock
        /// (<see cref="WakeUp_Policy"/>), null while nothing is being held.
        ///
        /// <para>
        /// IT IS NOT <see cref="PendingSeenAt"/>, AND IT MUST NOT BE. That one restarts every time the
        /// set changes, because its question is "has this stopped moving"; the digest's question is
        /// "how long has the oldest of these been waiting", and answering it with a stamp that resets
        /// would let a crew filing a report every four minutes push its own deadline out for ever.
        /// So it is set on the tick the first held entry appears and left alone until a turn takes
        /// them — which bounds the wait at the configured window whatever else lands meanwhile.
        /// </para>
        /// <para>
        /// AND IT IS SPENT WHERE THE ENTRIES ARE CONSUMED — the success path, beside
        /// <see cref="Advance_Cursors"/> — AND NOWHERE ELSE. Two reviews landed on this one line from
        /// opposite sides and both were right.
        /// </para>
        /// <para>
        /// 2026-09-09: the clearing sat on a LATER TICK, on the branch for a set that is non-empty and
        /// non-digestable — and the tick after a completed turn has an EMPTY set, so it returned above
        /// that line every single time. The <c>??=</c> then kept the FIRST report's instant for the
        /// life of the process, every later report read as already past the window, and the digest
        /// fired exactly ONCE per session per app life.
        /// </para>
        /// <para>
        /// 2026-09-10: the fix for that cleared it in <see cref="Start_Turn"/> instead — before the
        /// turn was admitted, let alone executed. A turn that starts and does NOT consume its entries
        /// (an error exit, a channel still locked when the reply is appended, a failure outside the
        /// process, the request-id idempotency skip) leaves them pending, and the next tick then found
        /// no hold on record and stamped a FRESH window on traffic that had already waited its full
        /// one: at <see cref="MAX_ATTEMPTS"/> failures, four windows instead of one, with the stall
        /// entry that is the only signal anything is wrong late by the same amount. So it is spent
        /// where the cursors advance, which is the one moment that means "these entries have been
        /// handed over": a failed turn leaves it alone, the retry inherits it, and a report already
        /// past its window goes at once.
        /// </para>
        /// <para>
        /// IN THE TRACKER AND NOT THE STATE FILE, like the deferral note above and for the same
        /// reason: a restart costs one early delivery (<see cref="HasDeliveredTraffic"/> is what keeps
        /// it early rather than late), and this is scheduling rather than the record decision 8 is
        /// enforced from.
        /// </para>
        /// <para>
        /// WRITTEN UNDER <c>_lock</c> ON BOTH SIDES, because the two sides are now two threads: the
        /// stamp is written by the mirror tick (<see cref="Resolve_DigestHold"/>) and spent by the
        /// turn's own background task (<see cref="Note_TrafficDelivered"/>), and a <c>DateTime?</c> is
        /// not written atomically. The claim of 2026-09-09 that it was "cleared under the in-flight
        /// lock" was true of the clearing and false of the stamping, which is worth nothing.
        /// </para>
        /// </summary>
        public DateTime? DigestHeldSince;

        /// <summary>
        /// FALSE UNTIL THIS DISPATCHER HAS HANDED THIS SESSION SOME TRAFFIC — which is the whole of
        /// the restart question for the digest: did this traffic arrive while I was watching, or was
        /// it already waiting when I started?
        ///
        /// <para>
        /// Traffic that arrived under observation can be held honestly, because
        /// <see cref="DigestHeldSince"/> records when it appeared. Traffic already pending before this
        /// dispatcher had handed anything over has waited an unknown time, so no stamp is written for
        /// it and <see cref="WakeUp_Policy.Resolve_WakeReason_OrNull"/> delivers it at once. REVIEW
        /// FINDING, 2026-09-09: without this the first tick after a restart stamped <c>nowLocal</c> on
        /// traffic that had already waited, so a restart RESTARTED the window — probed, a report filed
        /// at T+1 on a five-minute window went out at T+11, and 21 daemon restarts were measured in 44
        /// hours of VPS uptime. One EARLY delivery per session per process is the safe direction; one
        /// late delivery per restart, unbounded if restarts repeat, is not.
        /// </para>
        /// <para>
        /// IT IS "TRAFFIC WAS HANDED OVER", NOT "A TICK HAPPENED" AND NOT "A TURN STARTED" — and both
        /// of the wrong answers were tried. A flag set on the first TICK leaves the second tick, three
        /// seconds later and looking at the same already-waiting traffic, free to stamp a fresh window
        /// on it: the restart bug wearing a coalesce window. A flag set when a turn STARTED, which is
        /// what this field was for one commit, has every FAILED turn spend the exemption while
        /// consuming nothing, so the retry finds no hold and starts a fresh window: the same bug
        /// wearing a failure. Keying it on the moment the cursors advance cures both, which is why it
        /// is set there and only there.
        /// </para>
        /// <para>
        /// IT IS NOT <see cref="FirstTurnSinceStart"/>, though they are cleared side by side. That one
        /// is about the PROMPT — decision 8's "those turns are already done" preamble — and this one
        /// is about whether a hold can be timed honestly. Two questions, two fields; answering either
        /// with the other's field is how both findings above were written.
        /// </para>
        /// <para>
        /// A BOOT TURN SETS IT TOO, and it hands nothing over: it runs with an empty pending set by
        /// definition. That is the honest answer anyway, because the question this field asks is "was
        /// I watching when the traffic arrived", and a session whose greeting has completed has been
        /// watched by this dispatcher ever since. Precisely, then: it is set wherever a turn COMPLETED
        /// and its cursors were advanced — which for a boot turn is an advance of nothing.
        /// </para>
        /// </summary>
        public bool HasDeliveredTraffic;

        public DateTime? LastFailureAt;

        /// <summary>The pending set a stall happened on; null while nothing is stalled.</summary>
        public string? StalledOnSignature;

        /// <summary>
        /// The pending set the CURRENT usage-limit appointment has already been spoken about — first
        /// the traffic the refusal happened on, then whatever landed afterwards and was announced.
        /// Null while nothing is deferred.
        ///
        /// <para>
        /// IT IS A SIGNATURE AND NOT A FLAG because "has this been said" is a question about the
        /// TRAFFIC, not about the session: the owner writing twice during one appointment deserves to
        /// be told twice, and the same entries sitting there over forty ticks deserve to be told
        /// once. Same comparison the stall uses, for the same reason, and built by the same
        /// <see cref="Describe_PendingSignature"/> — never a second way of spelling it.
        /// </para>
        /// <para>
        /// IN THE TRACKER AND NOT THE STATE FILE, unlike the appointment itself. Losing it to a
        /// restart costs one repeated entry; putting it in the state file would put a UI-facing note's
        /// bookkeeping into the record decision 8 is enforced from.
        /// </para>
        /// </summary>
        public string? DeferralAnnouncedSignature;

        /// <summary>True until this dispatcher instance has run the session once — the "resumed after a restart" signal for the prompt.</summary>
        public bool FirstTurnSinceStart = true;
    }

    /// <summary>
    /// One source and what of it is waiting, as of this tick. It carries the PENDING ENTRIES AND NOTHING
    /// ELSE on purpose: an earlier shape also held the file's whole contents and the cursor it was read
    /// against, and neither had a reader — a snapshot nobody consumes is how the next person reasons
    /// from a stale copy of a file that has since been appended to. <see cref="Advance_Cursors"/> re-reads
    /// deliberately, and this leaves it nothing to re-read from.
    /// </summary>
    /// <param name="NothingEverDelivered">
    /// Whether this session has never been handed anything from the source — so everything pending on
    /// it is the first thing that channel has ever said. It is the digest's first-entry rule
    /// (<see cref="WakeUp_Policy.Contains_DigestableTraffic"/>): a spoke appears when a member is
    /// created, its first entry is that member's boot greeting, and holding a greeting costs a whole
    /// window before the new member can be briefed. Answered by
    /// <see cref="Nothing_EverDelivered"/> — read from the CURSOR and never from the entry's
    /// <c>[n]</c>, which is agent-written (CLAUDE.md decision 12).
    /// </param>
    readonly record struct SourceRead(ITurnSource Source, IReadOnlyList<IChannelEntry> Pending, bool NothingEverDelivered);

    public ReplyLinks.IReplyLinks ReplyLinks { get; } = Running.ReplyLinks.ReplyLinks_Factory.Create_InMemory();

    public int InFlightCount
    {
        get
        {
            lock (_lock)
                return _inFlight.Count;
        }
    }

    public bool Is_TurnInFlight(string orchId, string memberId)
    {
        lock (_lock)
            return _inFlight.ContainsKey(Describe_SessionKey(orchId, memberId));
    }

    public IReadOnlySet<string> Get_DeliveringIdentities(string orchId, string memberId)
    {
        lock (_lock)
            return _delivering.TryGetValue(Describe_SessionKey(orchId, memberId), out var carrying) ? carrying : NOTHING_IN_FLIGHT;
    }

    /// <summary>
    /// THE ONE SPELLING OF A SESSION'S KEY. It was five, and the comment that used to sit in
    /// <see cref="Is_TurnInFlight"/> asked for this in as many words ("two constructions of one key are
    /// two that can drift, and this one decides whether a working member is described as idle") while
    /// itself being the second. Collapsed on 2026-09-10, when a sixth was about to be added.
    /// </summary>
    static string Describe_SessionKey(string orchId, string memberId)
    {
        // Trimmed because the inputs are agent-written and the reader of the request JSON trims only
        // `reason`; the dictionaries keyed by this are case-insensitive, so casing is handled there
        // rather than by lowercasing a string that also reaches the log.
        return $"{orchId.Trim()}/{memberId.Trim()}";
    }

    public bool Is_TurnQueued(string orchId, string memberId)
    {
        var key = $"{orchId}/{memberId}";

        lock (_lock)
            return _inFlight.ContainsKey(key) && !_running.Contains(key);
    }

    /// <summary>
    /// THE OWNER'S PHONE LINE NEVER QUEUES BEHIND THE WORK IT DISPATCHED. The supervisor, a solo and
    /// the general supervisor are each ONE session with ONE turn at a time, and their turn is how the
    /// owner gets answered; the concurrency caps exist for the fan-out of implementers and reviewers.
    /// Measured 2026-09-10 21:18→21:47 on the VPS: five implementers briefed, three slots per
    /// orchestration, and the owner's message sat in the FIFO behind imp-7 and imp-8 for 29 minutes —
    /// the supervisor's turn started the second imp-5 was killed at its deadline. Exempting these
    /// roles adds at most one concurrent turn per orchestration (owner decision, 2026-09-10).
    /// </summary>
    internal static bool Is_ExemptFromSlots(SessionRoles role)
    {
        return role is SessionRoles.Supervisor or SessionRoles.Solo or SessionRoles.General;
    }

    public void Tick(DateTime nowLocal)
    {
        if (_draining.IsCancellationRequested || _shutdown.IsCancellationRequested)
            return;

        var configs = _configProvider.Get_Current().Runners;

        Report_ConfigRejections_Once(configs);

        foreach (var registered in Discover_RegisteredSessions())
        {
            // A STALE REGISTRATION IS NOT A MANDATE. The state file says a session WAS bridge-driven;
            // config.json says whether it still is. Flipped back to terminal, the launcher spawns a
            // window for this member — and without this check the dispatcher would keep firing
            // turns into the same channel, two sessions answering one brief. The file is
            // deleted by the launcher at the next spawn; until then, this is the gate.
            if (!Is_StillBridgeDriven(configs, registered.Role))
            {
                if (_warnedStaleRegistrations.Add(Describe_SessionKey(registered.OrchId, registered.MemberId)))
                    _log.Log_Warning(registered.OrchId, $"'{registered.MemberId}' has a bridge-driven registration but role '{SessionRole_Names.Get_ConfigKey(registered.Role)}' is now configured runner: {SessionRunner_Names.Get_Word(configs.Get_ForRole(registered.Role).Runner)} — no turns are dispatched for it (the registration is cleared at its next spawn)");

                continue;
            }

            try
            {
                Consider_Session(registered.StateFile, registered.Role, registered.OrchId, registered.MemberId, nowLocal, configs);
            }
            catch (Exception ex)
            {
                // SAID ONCE, NOT EVERY TICK. The commonest way to land here is a state file that cannot
                // be parsed — PrintSessionState_Store.Read_OrNull throws on one by design, because a
                // session whose identity is gone is not a default — and the tick runs every two seconds.
                // Unbounded, that is an error line every two seconds for as long as the app runs, which
                // buries the log it is written into. The stale-registration warning two blocks up already
                // dedupes for the same reason; this one did not.
                if (_warnedBrokenSessions.Add(Describe_SessionKey(registered.OrchId, registered.MemberId)))
                    _log.Log_Error(registered.OrchId, $"Print dispatcher: '{registered.MemberId}' could not be considered — it is skipped from now on and this is NOT repeated; fix or delete its {PrintSessionState_Store.STATE_FILE_NAME} and restart the app", ex);
            }
        }
    }

    /// <summary>
    /// THE /resume OVERRIDE — see <see cref="IPrintTurnDispatcher.Clear_LimitDeferrals"/> for why it
    /// exists. Reuses <see cref="Discover_RegisteredSessions"/> so this asks the exact question
    /// <see cref="Tick"/> asks ("which sessions does this dispatcher own"), never a second one that
    /// could drift from it. A state file it cannot READ OR WRITE is left alone and reported the same
    /// way <see cref="Tick"/> already reports one — this is not the place to invent a second failure
    /// mode for the same broken file, and not the place to let one broken file end the whole command
    /// either (F7).
    /// </summary>
    /// <returns>How many appointments were dropped — the number the owner's /resume reply quotes.</returns>
    public int Clear_LimitDeferrals()
    {
        var nowUtc = DateTime.UtcNow;

        List<(string OrchId, string MemberId, DateTime WaitingUntil)> cleared = [];
        List<string> skippedInFlight = [];

        foreach (var registered in Discover_RegisteredSessions())
        {
            var key = Describe_SessionKey(registered.OrchId, registered.MemberId);

            // THE WHOLE OPERATION IS IN THE TRY, NOT JUST THE READ (F7, 2026-09-09). An IO failure in
            // the WRITE used to escape into Resume_AllSessions_Async — which runs it BEFORE any channel
            // append — so one unwritable state file aborted the whole /resume, was logged as a Telegram
            // backoff, and the update was redelivered and retried for ever. One broken session must cost
            // one session.
            try
            {
                // UNDER THE SAME LOCK THE DISPATCHER STARTS TURNS UNDER, and skipping a session whose
                // turn is in flight (F6). Start_Turn takes this lock, so while it is held no turn can
                // BEGIN; a turn already running is the case the skip covers, because its own writes are
                // outside any lock and this one would be derived from a snapshot taken before them.
                // The read is inside too — re-reading outside it would be reasoning from a copy the
                // turn has since replaced, which is the bug in the first place.
                lock (_lock)
                {
                    if (_inFlight.ContainsKey(key))
                    {
                        skippedInFlight.Add(key);
                        continue;
                    }

                    var state = PrintSessionState_Store.Read_OrNull(registered.StateFile);

                    if (state?.RetryNotBeforeUtc == null)
                        continue;

                    PrintSessionState_Store.Write(registered.StateFile, PrintSessionState_Factory.CreateFrom_Existing_LimitDeferralCleared(state));
                    cleared.Add((registered.OrchId, registered.MemberId, state.RetryNotBeforeUtc.Value));
                }
            }
            catch (Exception ex)
            {
                if (_warnedBrokenSessions.Add(key))
                    _log.Log_Error(registered.OrchId, $"/resume: '{registered.MemberId}' could not be considered — it is skipped from now on and this is NOT repeated; fix or delete its {PrintSessionState_Store.STATE_FILE_NAME} and restart the app", ex);
            }
        }

        // LOGGED OUTSIDE THE LOCK. The log writes a file of its own, and holding the dispatcher's lock
        // across it would put the tick behind a second filesystem for as many sessions as are deferred.
        foreach (var (orchId, memberId, waitingUntil) in cleared)
            _log.Log_Info(orchId, $"'{memberId}' was waiting on a usage limit and would have run {LimitDeferral_Wording.Describe_Appointment(waitingUntil, nowUtc)} — /resume cleared it");

        // NAMED, NOT SWALLOWED. A session mid-turn is the one case /resume deliberately does not touch,
        // and a reader comparing "cleared N" against the number of waiting sessions needs to know why
        // the two differ.
        if (skippedInFlight.Count > 0)
            _log.Log_Info(string.Empty, $"/resume left {skippedInFlight.Count} session(s) alone because a turn is already running for them: {string.Join(", ", skippedInFlight)}");

        return cleared.Count;
    }

    public async Task Stop_Async()
    {
        // DRAIN FIRST, CANCEL SECOND. Closing the door stops Tick and aborts turns still queued for
        // a slot; the ones already running keep their token and finish on their own — entry appended,
        // cursors advanced, nothing to redo at the next start. Only when the grace is spent are they
        // cancelled, and then they get CANCEL_GRACE to unwind like before.
        _draining.Cancel();

        var running = Snapshot_InFlight();

        if (running.Length > 0)
        {
            var grace = Get_DrainGrace();

            // THE NUMBER AND THE REASON FOR IT. A stop can be waiting on a turn that was killed at the
            // deadline and is now spending its closing turn, so the wait the owner is watching is the
            // two of them end to end — saying "the turn timeout plus a minute" was the wrong number
            // AND the wrong explanation of why they are still sitting there.
            _log.Log_Info(string.Empty, $"Stopping — draining {running.Length} in-flight turn(s) before the sessions are killed (up to {grace.TotalMinutes:0.#} min: the turn timeout, the closing turn a turn killed at the deadline can still be owed, and a minute)");

            try
            {
                await Task.WhenAll(running).WaitAsync(grace);
                _log.Log_Info(string.Empty, "Drain complete — every in-flight turn ended on its own; nothing is left to redo at the next start");
            }
            catch (TimeoutException)
            {
                _log.Log_Warning(string.Empty, $"Drain grace of {grace.TotalMinutes:0.#} min elapsed with {InFlightCount} turn(s) still running — cancelling them; their entries stay pending for the next start");
            }
            catch
            {
                // A turn's own failure is recorded by Execute_Turn_Async; the drain only waits.
            }
        }

        _shutdown.Cancel();

        var cancelled = Snapshot_InFlight();

        try
        {
            if (cancelled.Length > 0)
                await Task.WhenAll(cancelled).WaitAsync(CANCEL_GRACE);
        }
        catch
        {
            // Turns end by cancellation or are abandoned after the grace — the app is exiting either way.
        }

        // AFTER the turns, never before: a resident process killed while its turn is still being
        // awaited turns an orderly shutdown into a structural failure and a fallback nobody asked
        // for. The app is exiting, so the ladder's memory would not survive to be wrong — but the
        // channel entry it writes on the way out would.
        foreach (var executor in _executors.Values)
        {
            try
            {
                await executor.Stop_Async();
            }
            catch (Exception ex)
            {
                _log.Log_Error(string.Empty, $"The {SessionRunner_Names.Get_Word(executor.Kind)} executor did not stop cleanly", ex);
            }
        }
    }

    Task[] Snapshot_InFlight()
    {
        lock (_lock)
            return [.. _inFlight.Values];
    }

    /// <summary>
    /// Everything one session's turn can still legitimately need — read at stop time, so a config edit
    /// made while the app ran counts. The sum is <see cref="ClosingTurn_Rule.Resolve_DrainGrace"/>'s,
    /// not restated here: the drain has to cover the work turn AND the closing turn behind it, and it
    /// went a whole stage covering only the first.
    /// </summary>
    TimeSpan Get_DrainGrace()
    {
        try
        {
            return ClosingTurn_Rule.Resolve_DrainGrace(TurnTimeout_Rule.Resolve_Longest(_configProvider.Get_Current().Runners), DRAIN_MARGIN);
        }
        catch
        {
            // An unreadable config at shutdown is not a reason to kill work faster than the default would.
            return DRAIN_GRACE_FALLBACK;
        }
    }

    /// <summary>
    /// EVERY REFUSAL THE CONFIG READER PRODUCED, SAID ONCE — the line
    /// <see cref="IRunnerConfigs.Rejections"/> exists to be, given a reader at last.
    ///
    /// <para>
    /// REVIEW FINDING, 2026-09-10: nothing in production read that list. <c>grep -rn Rejections</c>
    /// found the model, the factory, the interface and five test files, while the interface's own
    /// docstring claimed "one line each, logged once by the launcher" and no launcher did. So a
    /// <c>bg</c> role demoted to a terminal, a memory ceiling replaced, a digest window halved were
    /// all indistinguishable from settings that had been applied — the silence decision 21 forbids,
    /// in the one place an operator's own typing is being overruled.
    /// </para>
    /// <para>
    /// HERE BECAUSE THIS IS WHERE THE CONFIG IS READ AND A LOG IS TO HAND, not because refusals are
    /// the dispatcher's subject. An earlier version of this paragraph said the better home was the
    /// host, at startup, and that it was "reported rather than taken" — it was taken on 2026-09-10:
    /// <see cref="Report_ConfigRejections"/> is called from the engine's <c>Run_Async</c>, and this
    /// per-tick call stays for the refusal earned by an edit made while the app runs.
    /// </para>
    /// <para>
    /// ONCE PER DISTINCT LINE, AND NOT ONCE PER PROCESS. config.json is re-read every tick, so a
    /// refusal repeated every two seconds would bury the log it is written into (decision 14) — and a
    /// flag set at the first tick would swallow the refusal earned by an edit made while the app runs.
    /// The set of lines is the honest key: the same refusal is said once, a new one is said when it
    /// appears.
    /// </para>
    /// </summary>
    /// <summary>
    /// The same list, said at BOOT — called once from the engine's <c>Run_Async</c> so an operator
    /// whose setting was overruled reads it beside the startup banner rather than a tick later among
    /// session traffic, and reads it at all on a host that is draining (<see cref="Tick"/> returns
    /// before its own call in that state). Shares
    /// <see cref="Report_ConfigRejections_Once"/>'s dedupe, so the pair can never say a thing twice.
    /// </summary>
    public void Report_ConfigRejections()
    {
        Report_ConfigRejections_Once(_configProvider.Get_Current().Runners);
    }

    void Report_ConfigRejections_Once(IRunnerConfigs configs)
    {
        foreach (var rejection in configs.Rejections)
        {
            if (_reportedConfigRejections.Add(rejection))
                _log.Log_Warning(string.Empty, $"config.json: {rejection}");
        }
    }

    // ----- discovery -----

    IReadOnlyList<(string StateFile, SessionRoles Role, string OrchId, string MemberId)> Discover_RegisteredSessions()
    {
        List<(string, SessionRoles, string, string)> found = [];

        var generalFile = PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch.SessionLaunch_Factory.GENERAL_MEMBER_ID);

        if (File.Exists(generalFile))
            found.Add((generalFile, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch.SessionLaunch_Factory.GENERAL_MEMBER_ID));

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null)
                continue;

            // THE SUPERVISOR IS NOT IN THE MEMBER ROSTER — it is the orchestration itself — so a
            // loop over Members alone finds every implementer and never the one role the stream
            // runner exists for. Its registration lives under a role-prefixed name beside
            // session.json (PrintSessionState_Store knows where); found here or found nowhere.
            var supervisorFile = PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Supervisor, session.OrchId, SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

            if (File.Exists(supervisorFile))
                found.Add((supervisorFile, SessionRoles.Supervisor, session.OrchId, SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID));

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var role = SessionRole_Names.From_MemberKind(MemberKind_Ids.Resolve_Kind(member.MemberId));
                var stateFile = PrintSessionState_Store.Get_StateFile(_paths, role, session.OrchId, member.MemberId);

                if (File.Exists(stateFile))
                    found.Add((stateFile, role, session.OrchId, member.MemberId));
            }
        }

        return found;
    }

    /// <summary>
    /// Whether this registration is still one this dispatcher owns — the configured runner must be
    /// bridge-driven, must support the role, AND must be wired here. The third question is what
    /// stops a role configured for a transport nothing implements from being dispatched into a
    /// missing executor.
    /// </summary>
    bool Is_StillBridgeDriven(IRunnerConfigs configs, SessionRoles role)
    {
        var runner = configs.Get_ForRole(role).Runner;

        return Runner_Support.Is_BridgeDriven(runner) && Runner_Support.Supports(runner, role) && _executors.ContainsKey(runner);
    }

    // ----- per-session decision -----

    void Consider_Session(string stateFile, SessionRoles role, string orchId, string memberId, DateTime nowLocal, IRunnerConfigs configs)
    {
        var key = Describe_SessionKey(orchId, memberId);

        lock (_lock)
        {
            if (_inFlight.ContainsKey(key))
                return;
        }

        var state = PrintSessionState_Store.Read_OrNull(stateFile);

        if (state == null)
            return;

        var sources = TurnSources_Resolver.Resolve(_paths, _store, role, orchId, memberId);
        var reads = Read_Sources(stateFile, ref state, role, sources);

        if (reads.Count == 0)
            return;

        var ordered = PendingTraffic_Orderer.Order([.. reads.Select(read => (read.Source, read.Pending))]);

        // THE CHANNELS THIS SESSION HAS NEVER BEEN HANDED ANYTHING FROM — the digest's first-entry rule
        // (SourceRead.NothingEverDelivered). Built with the cursor set's own comparer, because a source
        // key is a word an agent typed and the two sets have to agree on what "the same channel" means.
        HashSet<string> firstContactSources = new(reads.Where(read => read.NothingEverDelivered).Select(read => read.Source.Key), SOURCE_KEYS);

        // THE BOOT TURN — the one turn that runs with NOTHING pending, and the only exception to
        // "an entry starts a turn".
        //
        // A bridge-driven session has no process until a turn runs, and no turn runs until an entry
        // arrives. For a member that is right: nobody is waiting on it. For the session that owns an
        // orchestration's OWNER channel it is a deadlock, because that session's greeting is what
        // creates the orchestration's Telegram topic (Bridge.OwnerPush_Policy.Is_OnlineGreeting) — so
        // the owner has nowhere to type, so no entry ever arrives, so the session never boots.
        // Measured on the VPS on 2026-09-06: a full crew started from the phone at 22:01:08 had no
        // topic until 22:07:30, and only because the task was appended to owner-channel.md by hand.
        // Under the terminal runner the supervisor was spawned WITH its role command and greeted at
        // once; the deadlock arrived with the bridge-driven runners, so it is closed where they made
        // it rather than papered over in the kit.
        if (ordered.Count == 0 && !Needs_BootTurn(state))
            return;

        var tracker = Get_Tracker(key);
        var signature = Describe_PendingSignature(ordered);

        if (tracker.PendingSignature != signature)
        {
            tracker.PendingSignature = signature;
            tracker.PendingSeenAt = nowLocal;
        }

        // THE DIGEST'S CLOCK STARTS ON THE FIRST HELD ENTRY AND IS NOT RESTARTED BY THE NEXT ONE — see
        // SessionTracker.DigestHeldSince, and Note_TrafficDelivered for where it is spent. READ ONCE
        // INTO A LOCAL: the stamp is written here and cleared on a turn's own background task, so
        // asking the tracker twice on one tick can get two answers, and the second reader is the log
        // line that says which rule released the traffic.
        var digestHeldSince = Resolve_DigestHold(tracker, nowLocal, WakeUp_Policy.Contains_DigestableTraffic(ordered, firstContactSources));

        // Entries still landing ride the same turn, whichever channel they land on: wait until the set
        // has been unchanged for the window — a SHORTER one when the owner is in it
        // (CoalesceWindow_Policy, owner decision 2026-09-09). Member traffic keeps the full window
        // because an extra supervisor wake costs ~1 M input tokens, measured; the owner's own message
        // does not, because those three seconds are a person waiting on their phone at the end of an
        // 11–12 s path. It is a shorter window and not NO window for the same reason: a turn that has
        // started cannot take what lands next, so waiving it outright bought the second turn instead of
        // saving it.
        if (nowLocal - tracker.PendingSeenAt < CoalesceWindow_Policy.Resolve_Window(ordered, configs.CoalesceWindow))
            return;

        // Stalled after MAX_ATTEMPTS: nothing runs until the pending set changes — which is what "new
        // traffic arrives" means once a cursor is a set of identities rather than a number.
        if (state.FailedAttempts >= MAX_ATTEMPTS && signature == tracker.StalledOnSignature)
            return;

        // A QUOTA IS NOT A BACKOFF. The turn has an appointment written in its state file (see
        // Record_Failure and IPrintSessionState.RetryNotBeforeUtc) and nothing runs before it —
        // including a turn woken by NEW traffic, because an entry landing in a channel does not give
        // the account its tokens back, and a session that tried anyway would spend an attempt to be
        // told the same thing again. Read from the STATE and not from the tracker, so a bridge
        // restarted inside the window keeps the appointment.
        //
        // BUT IT IS SAID NOW, WHICH IS THE HALF THAT WAS MISSING. This branch used to return in
        // silence, so the owner's message got its ✓ ack from the bridge and then nothing for hours.
        if (state.RetryNotBeforeUtc != null && nowLocal.ToUniversalTime() < state.RetryNotBeforeUtc.Value)
        {
            Announce_Deferral_IfTrafficIsNew(state, tracker, signature, ordered, state.RetryNotBeforeUtc.Value, nowLocal);
            return;
        }

        if (tracker.LastFailureAt != null && nowLocal - tracker.LastFailureAt.Value < _retryBackoff)
            return;

        // THE SUPERVISOR IS WOKEN TO DECIDE, NOT TO TAKE NOTE (spec §C4, measured 6–9 Sep 2026: 247 of
        // its ~400 wake-ups were member traffic, at ~1 M input tokens each). The owner and a member
        // that says it is blocked start a turn now, exactly as before; a member's ordinary report is
        // held for MemberDigestWindow so several of them ride ONE turn. Nothing is lost by being held
        // — a turn takes every pending entry, so held reports ride whatever starts the next one,
        // including the owner's own next message.
        //
        // LAST OF THE GATES, deliberately: every rule above it — the coalesce window, the stall, the
        // usage-limit appointment and its notice, the retry backoff — behaves exactly as it did, and
        // this only ever decides whether the turn STARTS. Four stages landed in this method today and
        // that is worth more than saving a tick's work.
        //
        // A SESSION THAT HAS NEVER TAKEN A TURN IS NEVER HELD. The boot turn reaches the policy as an
        // empty pending set and is released by it — but only while the set IS empty, and a supervisor
        // whose very first traffic is a member's report has a non-empty one. Its greeting is what
        // creates the orchestration's Telegram topic (Needs_BootTurn above), so holding that report for
        // the digest would hold the owner's own way in behind it.
        var wakeReason = Needs_BootTurn(state)
            ? "the session has not taken a turn yet"
            : WakeUp_Policy.Resolve_WakeReason_OrNull(ordered, firstContactSources, digestHeldSince, nowLocal, configs.MemberDigestWindow);

        if (wakeReason == null)
            return;

        // SAID ONCE PER TURN, not once per tick: a supervisor turn that did NOT happen leaves no
        // trace anywhere, so the line that says which rule released the traffic is the only way to
        // audit the digest from the log. Only when something was actually being held — an owner
        // message on a quiet channel is not news.
        if (digestHeldSince != null)
            _log.Log_Info(orchId, $"'{memberId}': {Describe_Traffic(ordered)} — {wakeReason}");

        Start_Turn(key, stateFile, state, With_AgentNotes(state, sources, ordered, nowLocal), sources, tracker, configs);
    }

    /// <summary>
    /// THE APP'S NOTES TO THIS SESSION RIDE THE TURN THAT IS STARTING — first, as context, before the
    /// traffic that started it — and never start one (see <see cref="PrintTurn_Trigger.Select_AgentNotes"/>
    /// for what they are and why nothing carried them before). Added AFTER every wake-up rule has
    /// decided, so a note can neither start a turn nor change which rule released one. A boot turn
    /// carries none: its empty pending set is what tells the executor it is a boot.
    /// </summary>
    IReadOnlyList<PendingEntry> With_AgentNotes(IPrintSessionState state, IReadOnlyList<ITurnSource> sources, IReadOnlyList<PendingEntry> ordered, DateTime nowLocal)
    {
        if (ordered.Count == 0)
            return ordered;

        var own = sources.FirstOrDefault(source => string.Equals(source.ChannelFilePath, state.ChannelFilePath, StringComparison.OrdinalIgnoreCase));
        var cursor = own == null ? null : state.Cursors.FirstOrDefault(candidate => SOURCE_KEYS.Equals(candidate.SourceKey, own.Key));

        if (own == null || cursor == null)
            return ordered;

        var notes = PrintTurn_Trigger.Select_AgentNotes(ChannelHistory_Cache.Read_Entries(own.ChannelFilePath), cursor, nowLocal);

        if (notes.Count == 0)
            return ordered;

        return [.. notes.Select(note => new PendingEntry(own, note)), .. ordered];
    }

    /// <summary>
    /// Whether this session still owes the owner the greeting nothing else can produce.
    ///
    /// <para>
    /// TWO ROLES, because exactly two write an orchestration's owner channel: the SUPERVISOR of a crew
    /// and the SOLO of a basic orchestration (<see cref="TurnSource.TurnSources_Resolver.Resolve_Own"/>
    /// maps both onto <c>owner-channel.md</c>). A member's greeting reaches nobody but its supervisor,
    /// and booting every member on registration would buy two model turns each — one to greet, one for
    /// the supervisor woken by the greeting — for something no owner is waiting on. The GENERAL
    /// supervisor is excluded for a different reason: its topic is the supergroup's General, pinned by
    /// the owner, so it is reachable before it has ever run.
    /// </para>
    /// <para>
    /// ONCE, and "never completed a turn" is the whole test. An app restart re-registers every session
    /// and finds <see cref="IPrintSessionState.ExecutedTurns"/> non-empty, so nothing is dispatched and
    /// no second greeting is filed. A boot turn that FAILED leaves the list empty and is retried — under
    /// <see cref="MAX_ATTEMPTS"/> and the retry backoff like any other turn, because a supervisor whose
    /// one and only turn died is exactly the case where giving up is silent.
    /// </para>
    /// </summary>
    static bool Needs_BootTurn(IPrintSessionState state)
    {
        return state.ExecutedTurns.Count == 0 && state.Role is SessionRoles.Supervisor or SessionRoles.Solo;
    }

    /// <summary>
    /// ONE ENTRY, ONCE, WHEN SOMETHING NEW LANDS ON A DEFERRED SESSION — and to the OWNER's audience
    /// for the roles that have one.
    ///
    /// <para>
    /// F5, probe 2026-09-09. <c>Consider_Session</c> returned on the appointment before it looked at
    /// what was pending, so a message typed into the topic got its ✓ from the bridge and then hours of
    /// silence, with nothing in the channel, nothing on the phone and nothing in the log. That is the
    /// waterfall's opposite failure and just as expensive: the owner cannot tell a quota wait from a
    /// dead app. This says which it is, and what ends it.
    /// </para>
    /// <para>
    /// IT DOES NOT CLEAR THE APPOINTMENT, and that is a decision rather than an omission. An owner
    /// message is a stronger signal than a parsed hour about WHAT MATTERS; it is no signal at all
    /// about whether the account has tokens. Running the turn on it would spend a real turn to be
    /// refused again, write a second identical deferral, and (before the cap) could re-park the
    /// session further out than the first refusal did — so the owner's own message would be what
    /// silenced them. The override they want already exists, is one word, and is now named in the
    /// entry: <c>/resume</c> drops every appointment and the next tick runs. Automatic beats explicit
    /// only when the automatic thing is right, and this one is a guess about someone else's quota.
    /// </para>
    /// <para>
    /// A FAILED APPEND LEAVES THE SIGNATURE UNSET, so the next tick tries again — the notice is worth
    /// a retry and there is nothing else to say if it never lands. It is not logged per tick: a
    /// channel locked for minutes would write a line every two seconds, and
    /// <c>ChannelLock_Diagnostics</c> already reports refused writes.
    /// </para>
    /// </summary>
    void Announce_Deferral_IfTrafficIsNew(IPrintSessionState state, SessionTracker tracker, string signature, IReadOnlyList<PendingEntry> pending, DateTime retryAtUtc, DateTime nowLocal)
    {
        // NOTHING PENDING IS NOT NEWS. A supervisor's boot turn reaches this branch with an empty set,
        // and "new traffic is waiting" about no traffic is exactly the confident wrong line the entry
        // exists to replace.
        if (pending.Count == 0 || signature == tracker.DeferralAnnouncedSignature)
            return;

        var appointment = LimitDeferral_Wording.Describe_Appointment(retryAtUtc, nowLocal.ToUniversalTime());

        var appended = ChannelAppender.Append_AppEntry(
            state.ChannelFilePath,
            Stall_Audience(state),
            $"{PrintTurn_Words.NEW_TRAFFIC_LIMITED_SUBJECT} {state.MemberId} — runs {appointment}",
            $"'{state.MemberId}' was refused for a usage limit and is waiting until {appointment}. What has just arrived is NOT lost — it is pending, it will ride the turn that runs then, and no attempt has been spent on it.\n\nIf it cannot wait, /resume drops the appointment and the session runs on the next tick.\n\n{Describe_Traffic(pending)}",
            DateTime.Now);

        if (appended)
            tracker.DeferralAnnouncedSignature = signature;
    }

    /// <summary>
    /// Reads every source, baselining the ones this session has never seen and dropping the cursors of
    /// sources it no longer has. A change to the cursor set is PERSISTED HERE, before any turn: a
    /// baseline taken on a tick that starts no turn must survive a restart, or the same history is
    /// absorbed and announced again on every tick for ever.
    /// </summary>
    IReadOnlyList<SourceRead> Read_Sources(string stateFile, ref IPrintSessionState state, SessionRoles role, IReadOnlyList<ITurnSource> sources)
    {
        var known = state.Cursors.ToDictionary(cursor => cursor.SourceKey, SOURCE_KEYS);

        // NO CURSORS AT ALL means this session has never been through here — a state file written before
        // sources existed, or one whose `sources` array could not be read. Its channel holds a whole life
        // of traffic and none of it has been handed over by anything that recorded the fact, so it is
        // HISTORY, absorbed exactly as registration absorbs it. Treating it as "nothing delivered" would
        // replay up to a full live channel into one turn, which is the very thing the store's own note
        // says must not happen. A session that HAS cursors and meets a NEW key is the opposite case, and
        // is handled below.
        var firstSightOfThisSession = state.Cursors.Count == 0;

        List<SourceRead> reads = [];
        List<ITurnCursor> cursors = [];
        var changed = false;

        foreach (var source in sources)
        {
            var entries = ChannelHistory_Cache.Read_Entries(source.ChannelFilePath);

            if (!known.TryGetValue(source.Key, out var cursor))
            {
                // A SOURCE THAT APPEARS WHILE THE SESSION IS RUNNING STARTS EMPTY, so everything in it
                // is traffic and none of it is absorbed. A channel that turns up now belongs to a member
                // that was created now, and its very first entry — the member's boot greeting, landing
                // between its spawn and the next tick — is exactly what a supervisor is here to answer.
                // Absorbing it would swallow the one entry this rule can ever see.
                cursor = firstSightOfThisSession
                    ? TurnCursor_Factory.Create_Baseline(source, role, entries)
                    : TurnCursor_Factory.Create_Empty(source);

                changed = true;

                if (cursor.Delivered.Count > 0)
                    _log.Log_Warning(state.OrchId, $"'{state.MemberId}' had no cursors at all, so channel '{source.Key}' was baselined on sight: the {cursor.Delivered.Count} inbound entr{(cursor.Delivered.Count == 1 ? "y" : "ies")} already in it are HISTORY and will not start a turn");
                else
                    _log.Log_Info(state.OrchId, $"'{state.MemberId}' is now also woken by channel '{source.Key}'");
            }

            cursors.Add(cursor);
            Warn_IfEntriesWereArchivedUndelivered(state, source, cursor, entries);

            reads.Add(new SourceRead(source, PrintTurn_Trigger.Select_Pending(role, entries, cursor), Nothing_EverDelivered(cursor)));
        }

        // A CURSOR IS NEVER DROPPED FOR A SOURCE THAT MERELY DID NOT RESOLVE THIS TICK. It used to be,
        // to keep the file tidy — and the roster read that decides is best-effort: an absent session.json
        // resolves to the owner channel alone, and rewriting the durable record from that transient
        // answer loses every spoke cursor irreversibly. The next successful read then meets them all as
        // unknown keys and re-delivers whole channels. Keeping a stale key costs one line in a JSON file
        // nobody counts; dropping it costs a supervisor re-answering every member it has.
        foreach (var cursor in state.Cursors)
        {
            if (!sources.Any(source => SOURCE_KEYS.Equals(source.Key, cursor.SourceKey)))
                cursors.Add(cursor);
        }

        if (changed)
        {
            state = PrintSessionState_Factory.CreateFrom_Existing_Cursors(state, cursors);
            PrintSessionState_Store.Write(stateFile, state);
        }

        return reads;
    }

    /// <summary>
    /// HAS THIS SESSION EVER BEEN HANDED ANYTHING FROM THIS CHANNEL — asked of the cursor, and asked
    /// in a way that CANNOT COME BACK TRUE. Two readers depend on it: the digest's first-entry
    /// exemption (<see cref="SourceRead.NothingEverDelivered"/>) and the archive-gap warning
    /// (<see cref="Warn_IfEntriesWereArchivedUndelivered"/>), and one implementation is the whole
    /// point — they were two copies of the same wrong test.
    ///
    /// <para>
    /// REVIEW FINDING, 2026-09-10, and it is CLAUDE.md decision 13's exact shape: a stored count
    /// re-derived from a live read. <see cref="TurnCursor_Factory.CreateFrom_Delivered"/> prunes
    /// <see cref="ITurnCursor.Delivered"/> to the identities still in the LIVE file, so after
    /// compaction, on a turn where that source had nothing pending, the set EMPTIES — and
    /// <c>Delivered.Count == 0</c> then said "first contact" about a member of many hours' standing.
    /// Its every report was exempt from the digest from then on, and the archive-gap warning returned
    /// early on exactly the channels compaction had touched, which are the only ones it exists for.
    /// </para>
    /// <para>
    /// <see cref="ITurnCursor.HighWaterIndex"/> is what makes the answer stick: it is only ever raised
    /// (<c>Math.Max</c>), it survives the prune, and zero is not an index a channel hands out — they
    /// are numbered from one. The delivered set stays in the test as the second half, so a channel
    /// whose only delivered entry carried an agent-typed <c>[0]</c> is still not called first contact.
    /// This is not the index deciding DELIVERY, which that field forbids and which
    /// <see cref="PrintTurn_Trigger.Select_Pending"/> still answers from identities alone; it is one
    /// boolean about whether anything ever happened here.
    /// </para>
    /// </summary>
    static bool Nothing_EverDelivered(ITurnCursor cursor)
    {
        return cursor.HighWaterIndex == 0 && cursor.Delivered.Count == 0;
    }

    /// <summary>
    /// THE ONE HOLE THIS CURSOR HAS, MADE AUDIBLE. Only the LIVE file is read, so entries compaction
    /// archived before the bridge ever handed them over are gone from its view — the same one-way hole
    /// <see cref="Bridge.BridgeState_Store"/> describes for the mirror, reachable here only if a session
    /// went undelivered for more than <see cref="Channel_Compactor.COMPACT_ABOVE_ENTRIES"/> entries.
    /// Reading the archive every tick is exactly what compaction exists to avoid, so this detects the
    /// gap instead of closing it: everything still live sitting above the highest index ever delivered
    /// means entries left in between. Said once per channel per app life.
    /// </summary>
    void Warn_IfEntriesWereArchivedUndelivered(IPrintSessionState state, ITurnSource source, ITurnCursor cursor, IReadOnlyList<IChannelEntry> entries)
    {
        if (Nothing_EverDelivered(cursor) || entries.Count == 0)
            return;

        var lowestLive = entries.Min(entry => entry.Index);

        if (lowestLive <= cursor.HighWaterIndex + 1)
            return;

        if (!_warnedArchiveGaps.Add($"{state.OrchId}/{state.MemberId}/{source.Key}"))
            return;

        _log.Log_Warning(state.OrchId, $"'{state.MemberId}': channel '{source.Key}' now starts at entry [{lowestLive}] but nothing above [{cursor.HighWaterIndex}] was ever delivered to this session — entries [{cursor.HighWaterIndex + 1}]–[{lowestLive - 1}] were archived without starting a turn and are only in '{Path.GetFileName(Channel_Compactor.Build_ArchiveFilePath(source.ChannelFilePath))}'");
    }

    /// <summary>
    /// Starts the turn and registers it as in-flight AS ONE STEP, and detaches it from the mirror
    /// tick's ambient state. Both halves are load-bearing:
    ///
    /// <para>
    /// REGISTERED UNDER THE LOCK, BEFORE the task can finish. Written the other way round — start,
    /// then record — a turn that completes synchronously (the idempotency skip does no I/O, and an
    /// uncontended semaphore completes inline) removes the key BEFORE this inserts it, leaving a
    /// finished task in the map that nothing will ever remove: that session is then skipped by the
    /// guard above for the life of the process. The removal in <c>Execute_Turn_Async</c>'s finally
    /// takes the same lock, so it simply waits for this insert.
    /// </para>
    /// <para>
    /// EXECUTION CONTEXT SUPPRESSED, because <c>ChannelWrite_Lock</c>'s allowance is an
    /// <c>AsyncLocal</c> and <c>Task.Run</c> captures the ambient context. This tick opened one;
    /// a turn inheriting it would, half an hour later, charge its channel appends against an
    /// allowance that ended with that tick — and that file says in as many words that the hazard is
    /// inert only while no detached lambda writes to a channel. This one writes the member's entry,
    /// so it must not inherit. A turn is its own flow: it opens no allowance and pays the plain
    /// per-call budget.
    /// </para>
    /// </summary>
    void Start_Turn(string key, string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources, SessionTracker tracker, IRunnerConfigs configs)
    {
        lock (_lock)
        {
            if (_inFlight.ContainsKey(key))
                return;

            // THE DIGEST'S HOLD IS NOT SPENT HERE, AND WAS FOR ONE COMMIT — see
            // SessionTracker.DigestHeldSince. Starting a turn is not consuming its entries: a turn
            // that fails, or is skipped as already executed, leaves them pending, and clearing the
            // stamp on the way in gave every such turn's retry a whole fresh window on traffic that
            // had already waited one. It is spent beside Advance_Cursors instead
            // (Note_TrafficDelivered), which is the one moment that means the entries have left.
            using var suppressed = ExecutionContext.SuppressFlow();

            // Recorded from the very entries this turn launches with, so what it is carrying is a fact
            // rather than a re-read of a file that may have been appended to since.
            _delivering[key] = pending.Select(item => ChannelEntry_Digest.Compute(item.Entry)).ToHashSet(StringComparer.Ordinal);

            _inFlight[key] = Task.Run(() => Execute_Turn_Async(key, stateFile, state, pending, sources, tracker, configs));
        }
    }

    /// <summary>
    /// THE DIGEST'S HOLD, SPENT — called from the two places a turn's entries are actually consumed
    /// (<see cref="Run_Turn_Async"/>'s success branch and <see cref="Close_Down_KilledTurn_Async"/>'s,
    /// both beside <see cref="Advance_Cursors"/>) and from nowhere else. That restriction IS the fix
    /// of 2026-09-10: see <see cref="SessionTracker.DigestHeldSince"/> for what calling it where a
    /// turn merely STARTS cost.
    ///
    /// <para>
    /// Two writes, one lock, because the stamp's other side is the mirror tick
    /// (<see cref="Resolve_DigestHold"/>) and this runs on the turn's background task.
    /// </para>
    /// </summary>
    void Note_TrafficDelivered(SessionTracker tracker)
    {
        lock (_lock)
        {
            tracker.DigestHeldSince = null;
            tracker.HasDeliveredTraffic = true;
        }
    }

    /// <summary>
    /// The instant the traffic now pending began to be held, starting the clock if this is the first
    /// tick that saw something holdable — and returning it, so the caller reads it ONCE rather than
    /// asking the tracker again a few lines later while a turn may be clearing it.
    ///
    /// <para>
    /// NOTHING IS STAMPED BEFORE THIS DISPATCHER HAS HANDED THIS SESSION SOMETHING
    /// (<see cref="SessionTracker.HasDeliveredTraffic"/>): traffic already pending then has waited an
    /// unknown time — the tracker is per-process — and a null stamp is what makes the policy deliver
    /// it instead of starting a fresh window on it.
    /// </para>
    /// </summary>
    DateTime? Resolve_DigestHold(SessionTracker tracker, DateTime nowLocal, bool digestableTrafficPending)
    {
        lock (_lock)
        {
            if (digestableTrafficPending && tracker.HasDeliveredTraffic)
                tracker.DigestHeldSince ??= nowLocal;

            return tracker.DigestHeldSince;
        }
    }

    SessionTracker Get_Tracker(string key)
    {
        lock (_lock)
        {
            if (!_trackers.TryGetValue(key, out var tracker))
            {
                tracker = new SessionTracker();
                _trackers[key] = tracker;
            }

            return tracker;
        }
    }

    SemaphoreSlim Get_OrchestrationSlots(string orchId)
    {
        lock (_lock)
        {
            if (!_orchestrationSlots.TryGetValue(orchId, out var slots))
            {
                slots = new SemaphoreSlim(_slotsPerOrchestration, _slotsPerOrchestration);
                _orchestrationSlots[orchId] = slots;
            }

            return slots;
        }
    }

    // ----- the turn -----

    async Task Execute_Turn_Async(string key, string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources, SessionTracker tracker, IRunnerConfigs configs)
    {
        // TWO TOKENS. A turn WAITING for a slot is admitted under both signals — a drain aborts it,
        // its entries stay pending. A turn RUNNING holds only the shutdown token, so a drain lets it
        // finish and only the exhausted grace cancels it.
        var cancellationToken = _shutdown.Token;
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, _draining.Token);
        var orchestrationSlots = Get_OrchestrationSlots(state.OrchId);

        try
        {
            if (Is_ExemptFromSlots(state.Role))
            {
                // No slot taken, none released: the exemption is the whole point, and taking a slot
                // "just to count it" would put the supervisor back in the queue it was lifted out of.
                Mark_Running(key);
                admission.Token.ThrowIfCancellationRequested();
                await Run_Turn_Async(stateFile, state, pending, sources, tracker, configs, cancellationToken);
                return;
            }

            // SAID ONCE, at the moment the wait is real: a turn that finds a free slot says nothing,
            // one that will queue says so — this line is the only trace of a queue the log ever had.
            if (_globalSlots.CurrentCount == 0 || orchestrationSlots.CurrentCount == 0)
                _log.Log_Info(state.OrchId, $"Turn for '{state.MemberId}' is queued — waiting for a free turn slot ({_slotsPerOrchestration} per orchestration)");

            await _globalSlots.WaitAsync(admission.Token);

            try
            {
                await orchestrationSlots.WaitAsync(admission.Token);

                try
                {
                    // A slot won in the same instant the door closed is not a mandate to start.
                    admission.Token.ThrowIfCancellationRequested();
                    Mark_Running(key);
                    await Run_Turn_Async(stateFile, state, pending, sources, tracker, configs, cancellationToken);
                }
                finally
                {
                    orchestrationSlots.Release();
                }
            }
            finally
            {
                _globalSlots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, or a drain that closed the door before this turn got a slot: the cursors were
            // not advanced, so the same entries are pending at the next start.
        }
        catch (Exception ex)
        {
            _log.Log_Error(state.OrchId, $"Turn for '{state.MemberId}' failed outside the process", ex);
            tracker.LastFailureAt = DateTime.Now;
            Record_OutOfProcessFailure(stateFile, state, pending, tracker, ex);
        }
        finally
        {
            lock (_lock)
            {
                _inFlight.Remove(key);
                _running.Remove(key);
                _delivering.Remove(key);
            }
        }
    }

    void Mark_Running(string key)
    {
        lock (_lock)
            _running.Add(key);
    }

    /// <summary>
    /// A FAILURE OUTSIDE THE PROCESS IS STILL A FAILED ATTEMPT. It used to be logged and nothing else,
    /// so <see cref="IPrintSessionState.FailedAttempts"/> never moved and <see cref="MAX_ATTEMPTS"/>
    /// could never be reached: a <c>claude</c> missing from PATH, or an IO error thrown while appending
    /// the entry, produced one error line per tick for ever — no stall, no alert in the channel, and
    /// nothing at all where a human looks. The stall path exists precisely for "this keeps failing", and
    /// a failure that cannot reach it is the silence this repo keeps paying for.
    ///
    /// <para>
    /// THE STATE IS RE-READ, not reused. The turn may have written it after the caller captured it —
    /// claiming the session id does exactly that — and incrementing a stale copy would undo that write.
    /// </para>
    /// <para>
    /// And it never throws. It runs inside a catch handler; an exception escaping here leaves the task
    /// faulted with nobody awaiting it, which is a worse silence than the one it is fixing.
    /// </para>
    /// </summary>
    void Record_OutOfProcessFailure(string stateFile, IPrintSessionState captured, IReadOnlyList<PendingEntry> pending, SessionTracker tracker, Exception cause)
    {
        try
        {
            var state = PrintSessionState_Store.Read_OrNull(stateFile) ?? captured;
            var failed = PrintSessionState_Factory.CreateFrom_Existing_AttemptFailed(state);

            PrintSessionState_Store.Write(stateFile, failed);

            if (failed.FailedAttempts < MAX_ATTEMPTS)
                return;

            tracker.StalledOnSignature = Describe_PendingSignature(pending);

            var requestId = PrintTurn_RequestId.Build(state.OrchId, state.MemberId, state.NextTurnNumber);
            var alert = $"Turn {requestId} failed {failed.FailedAttempts} times before the process could report anything — not retried until new traffic arrives in the channels";

            _log.Log_Error(state.OrchId, alert, null);

            ChannelAppender.Append_AppEntry(
                state.ChannelFilePath,
                Resolve_StallAlertAudience(state),
                StallAlert_Decider.Build_Subject(state.MemberId, state.NextTurnNumber, $"failed outside the process × {failed.FailedAttempts}"),
                $"{alert}\n\nrequest_id: {requestId}\n{Describe_Traffic(pending)}\nlast error: {cause.GetType().Name}: {cause.Message}",
                DateTime.Now);
        }
        catch (Exception ex)
        {
            _log.Log_Error(captured.OrchId, $"'{captured.MemberId}': the out-of-process failure could not be recorded either — the attempt counter did not move, so this turn keeps retrying", ex);
        }
    }

    async Task Run_Turn_Async(string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources, SessionTracker tracker, IRunnerConfigs configs, CancellationToken cancellationToken)
    {
        if (state.FailedAttempts >= MAX_ATTEMPTS)
        {
            // New traffic after a stall: the counter starts over for this turn.
            state = PrintSessionState_Factory.CreateFrom_Existing_AttemptsReset(state);
            tracker.StalledOnSignature = null;
        }

        // THE APPOINTMENT IS KEPT THE MOMENT THE TURN STARTS, so it is spent rather than left standing
        // for the half hour the turn may run. F6, reproduced 2026-09-09: /resume reads a session's
        // state file, derives a cleared copy from that SNAPSHOT and writes it back — with an appointment
        // still on file for a turn already in flight, that write landed on top of the turn's own record
        // and rolled it back (executed_turns 1 → 0, next_turn 3 → 2), so the same request id ran twice.
        // A decision-8 violation caused by the owner's own recovery command. Clear_LimitDeferrals now
        // also refuses a session with a turn in flight; this closes the other half, because the window
        // was open from the tick that admitted the turn until the turn finished.
        if (state.RetryNotBeforeUtc != null)
        {
            state = PrintSessionState_Factory.CreateFrom_Existing_LimitDeferralCleared(state);
            PrintSessionState_Store.Write(stateFile, state);
            tracker.DeferralAnnouncedSignature = null;
        }

        var turnNumber = state.NextTurnNumber;
        var requestId = PrintTurn_RequestId.Build(state.OrchId, state.MemberId, turnNumber);

        if (state.ExecutedTurns.Any(turn => turn.RequestId == requestId))
        {
            _log.Log_Warning(state.OrchId, $"Turn {requestId} was already executed — skipped, not re-run");
            PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_TurnSkipped(state));
            return;
        }

        var roleConfig = configs.Get_ForRole(state.Role);

        // THE GENERAL'S MODEL IS RE-READ HERE, because nothing else ever re-reads it. Every other
        // bridge-driven session passes through BridgeDrivenRunnerModel.Start again — a member is
        // re-registered when it is added or respawned, and that path updates the model when config.json
        // changed under it. A PRINT-RUN GENERAL never does: SessionWatchdog.Check_GeneralSupervisor
        // returns early for it by design (it has no process to be alive), so Spawn_GeneralSupervisor
        // is never called again and the model captured at first registration is frozen for the life of
        // the state file. Measured 2026-09-09: config.json said `generalSupervisorModel: opus` (later
        // sonnet) while the session had been running haiku since 2026-09-08 — an owner decision about
        // models that no restart could apply. Refreshed at the START OF A TURN, which is the point of
        // effect (decision 21), so the change lands on the next turn and is logged where it happens.
        state = Refresh_GeneralModel_IfChanged(stateFile, state);

        var fresh = roleConfig.Resume == ResumeModes.Fresh;
        var hasHistory = state.ExecutedTurns.Count > 0;

        // WHETHER THE ID IS SPENT, not whether a turn has succeeded. `--session-id` with a uuid the
        // CLI has already seen is refused outright ("Error: Session ID <uuid> is already in use.",
        // exit 1, measured on 2.1.261), so a first turn that timed out could otherwise never be
        // retried: attempts 2 and 3 would die on the flag and the session would stall for good.
        //
        // SPENT IS STILL NOT THE SAME AS EXISTS, and the gap is recovered from rather than predicted —
        // see TRANSCRIPT_GONE_SIGNAL and Record_Failure. Guessing here was tried and was wrong: the id
        // is claimed before the process starts, but an attempt that ran and merely answered badly DOES
        // have a transcript, and refusing to resume it would throw away work every time a model errored.
        // ONE SOURCE FOR "IS THIS ID SPENT": the flag. It used to be `SessionStarted || hasHistory`, and
        // the second half quietly outvoted the first — when the CLI told us the transcript was gone and
        // the id was given back, a session with turns behind it went on resuming the FRESH id it had just
        // been handed, and was refused again, every attempt, for ever. History is not evidence about the
        // id the session is holding NOW. It was only ever a fallback for a state file written before the
        // flag existed, and the store already applies exactly that fallback when the field is absent.
        var sessionUsed = state.SessionStarted;
        var resumeTranscript = !fresh && sessionUsed;
        var sessionId = fresh && sessionUsed ? Guid.NewGuid().ToString() : state.SessionId;

        // Claimed BEFORE the process starts, so a bridge that dies mid-turn still knows the id is
        // spent and resumes instead of colliding with itself.
        if (!resumeTranscript)
        {
            state = PrintSessionState_Factory.CreateFrom_Existing_SessionClaimed(state, sessionId);
            PrintSessionState_Store.Write(stateFile, state);
        }

        // NOT CLEARED HERE. It used to be, one line below this, before the turn's outcome was known —
        // so a first turn after a bridge restart that timed out or errored had attempts 2 and 3 resume
        // the very transcript that may remember answering, with the preamble that says "those turns are
        // done, do not repeat them" switched off. The decision-8 guard was off on exactly the retry it
        // exists for. It is cleared where a turn actually COMPLETES instead.
        var alreadyExecuted = tracker.FirstTurnSinceStart && hasHistory
            ? state.ExecutedTurns.Select(turn => turn.TurnNumber).ToList()
            : [];

        var executor = _executors[roleConfig.Runner];

        Dictionary<string, string> environment = new()
        {
            ["AIORCH_ROLE"] = SessionRole_Names.Get_EnvWord(state.Role),
            ["AIORCH_ID"] = state.OrchId,
            ["AIORCH_MEMBER"] = state.MemberId,
            [RUNNER_ENV_VAR] = SessionRunner_Names.Get_Word(executor.Kind),

            // THE ROOT TRAVELS WITH THE SESSION. The stage-2 live round lost its first two turns to
            // exactly this: under --root the member composed its channel path from the default home,
            // did not find the file, CREATED one in the real home and sat there until the timeout.
            // The hosts export it into their own environment and Process.Start copies the parent's,
            // so this is belt AND braces — but a dispatcher wired directly (a test, a future host)
            // has no parent that did, and the failure it produces is silent.
            [Composition.HostOptions.HostOptions_Factory.SUPERVISION_ROOT_ENV] = _paths.Root,
        };

        var attempt = state.FailedAttempts + 1;
        _log.Log_Info(state.OrchId, $"{SessionRunner_Names.Get_Word(executor.Kind)} turn {requestId} started — attempt {attempt}, {Describe_Traffic(pending)}, {(resumeTranscript ? "resume" : fresh ? "fresh session" : "first turn")} {sessionId}");

        var result = await executor.Execute_Async(state, roleConfig, sessionId, resumeTranscript, requestId, pending, sources, alreadyExecuted, environment, TurnTimeout_Rule.Resolve_ForRole(state.Role, configs), configs.MemberSilenceLimit, cancellationToken);

        // The runner rethrows on shutdown rather than reporting a timeout, so nothing below runs
        // for a turn the app cancelled: no failure counted, no turn_ended entry, no attempt spent.
        cancellationToken.ThrowIfCancellationRequested();

        var outcome = TurnOutcomes.Describe(result);

        if (TurnOutcomes.Is_Success(result))
        {
            // BEFORE THE TURN'S OWN ENTRY, in the order they were written. A final message the turn
            // then superseded is content the result does not carry, and this is the only place it
            // still exists — see ITurnResult.SupersededFinals.
            if (!await Write_SupersededFinals_Async(state, sources, result, requestId))
            {
                _log.Log_Error(state.OrchId, $"Turn {requestId} completed but a superseded final message could not be appended — the channel stayed locked; the turn will be retried", null);
                Record_Failure(stateFile, state, pending, tracker, result, requestId, attempt, executor, "entry not appended (channel locked)");
                return;
            }

            if (!(await Write_Reply_Async(state, sources, result.ResultText, Find_AnsweredOwnerEntry_OrNull(state, pending))).AllLanded)
            {
                _log.Log_Error(state.OrchId, $"Turn {requestId} completed but its entry could not be appended — the channel stayed locked; the turn will be retried", null);
                Record_Failure(stateFile, state, pending, tracker, result, requestId, attempt, executor, "entry not appended (channel locked)");
                return;
            }

            // AFTER BOTH ARE FILED, because it says both were.
            Append_SupersededNotice(state, result);

            Append_TurnEnded(state, requestId, attempt, pending, result, outcome, null);

            // A BOOT TURN ANSWERED NO ENTRY. Zero is not an index any channel has — they are numbered
            // from 1 — so the record reads as "none" rather than borrowing the first entry of a turn
            // it never saw, and the range in the turn_ended entry stays honest.
            var firstIndex = pending.Count == 0 ? 0 : pending[0].Entry.Index;
            var lastIndex = pending.Count == 0 ? 0 : pending[^1].Entry.Index;

            var executed = ExecutedTurn_Factory.Create(turnNumber, requestId, firstIndex, lastIndex, DateTime.UtcNow, outcome, result.TotalCostUsd, result.SessionId ?? sessionId);

            PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(state, executed, result.SessionId ?? sessionId, Advance_Cursors(state, sources, pending)));
            tracker.LastFailureAt = null;
            tracker.FirstTurnSinceStart = false;

            // THE ENTRIES HAVE LEFT, so the digest's hold is spent here and only here — see
            // SessionTracker.DigestHeldSince for the two ways of getting this line's position wrong.
            Note_TrafficDelivered(tracker);

            _log.Log_Info(state.OrchId, $"Turn {requestId} ended — {outcome}, {Describe_Cost(result)}, {result.Elapsed.TotalSeconds:F1} s wall");
            return;
        }

        // A DEADLINE KILL IS NOT AN ORDINARY FAILURE. The turn was working when the 30 minutes ran
        // out (measured: 32–92 tool calls, 7–30 M input tokens), so it gets one short turn to say
        // where it got to instead of having its entries re-run from scratch — the retry that
        // discarded ~14 hours of work in the 2026-09-07/09 window.
        if (ClosingTurn_Rule.Is_DeadlineKill(result))
        {
            await Close_Down_KilledTurn_Async(stateFile, state, pending, sources, tracker, configs, roleConfig, executor, environment, result, requestId, turnNumber, attempt, sessionId, cancellationToken);
            return;
        }

        Record_Failure(stateFile, state, pending, tracker, result, requestId, attempt, executor, null);
    }

    /// <summary>
    /// THE CLOSING TURN: <c>claude -p --resume &lt;the killed transcript&gt; --max-budget-usd 2.00</c>
    /// with "write where you are and stop", ONE attempt, its own short timeout — then the killed
    /// turn is recorded as executed and the entries its report ANSWERED stop being pending. The
    /// report is itself new traffic for the counterpart, so the next turn starts from the channel
    /// (and, in fresh mode, from a new session id) rather than replaying a brief the model has
    /// already half-answered. Spec section C1.3.
    ///
    /// <para>
    /// "ANSWERED" IS THE WHOLE OF WHAT IS CONSUMED, and it is one word that cost the owner an answer.
    /// The closing turn writes ONE entry and addresses it freely — it is handed the same <c>TO:</c>
    /// contract as every other turn — so a supervisor woken by the owner and killed at the deadline
    /// could file its "where I got to" to a spoke while the owner's question was marked delivered by
    /// it. Nothing reached the phone (both records are agent-audience) and there is nobody above a
    /// supervisor to re-brief it. A source the report did not write to keeps its cursor and is
    /// pending again on the next tick — see <see cref="Advance_Cursors"/>.
    /// </para>
    ///
    /// <para>
    /// THE ATTEMPT IS NOT SPENT when this works. A deadline kill that got its closing turn is a turn
    /// that ENDED, not one that failed: counting it would walk a session towards its stall for doing
    /// the one thing this path exists to make possible, and the 60-second backoff would delay traffic
    /// that is no longer pending anyway.
    /// </para>
    /// <para>
    /// EVERY BRANCH THAT CANNOT DO ITS JOB SAYS WHICH ONE — IN THE LOG AND IN THE RECORD — AND FALLS
    /// BACK TO TODAY'S BEHAVIOUR (decision 21): no transcript id to resume, a transport with no print
    /// rung to run it on, a closing turn that cannot be started, one that is itself killed or refused,
    /// one that ends cleanly and says nothing, a report that cannot be appended. Then the attempt
    /// counts and the entries are retried exactly as before this stage existed — worse than a report,
    /// and no worse than last week. The killed turn's own <c>turn_ended</c> is written ON the branch
    /// that was taken, naming it, because a record written before the attempt is a forecast.
    /// </para>
    /// <para>
    /// IN TRANSCRIPT MODE THE NEXT TURN STILL RESUMES THE KILLED TRANSCRIPT. Only Fresh mode mints a
    /// new session id, and only Fresh mode is where the "fresh start" half of C1.3 lands; a
    /// transcript-mode role keeps today's continuity, which is what its configuration asked for.
    /// </para>
    /// </summary>
    async Task Close_Down_KilledTurn_Async(
        string stateFile,
        IPrintSessionState state,
        IReadOnlyList<PendingEntry> pending,
        IReadOnlyList<ITurnSource> sources,
        SessionTracker tracker,
        IRunnerConfigs configs,
        RoleRunnerConfig.IRoleRunnerConfig roleConfig,
        ITurnExecutor executor,
        IReadOnlyDictionary<string, string> environment,
        ITurnResult killed,
        string requestId,
        int turnNumber,
        int attempt,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var closingRequestId = ClosingTurn_Words.Build_RequestId(requestId);
        var closingTimeout = ClosingTurn_Rule.Resolve_Timeout(configs.TurnTimeout);

        // THE KILLED TURN'S RECORD IS WRITTEN WHEN ITS OUTCOME IS KNOWN, NEVER BEFORE. It used to be
        // appended here, first, saying "a closing turn was run to write where it got to, and these
        // entries are not re-run" — true on ONE of the six branches below (adversarial review,
        // 2026-09-09). With no transcript id, no print rung, a closing turn that is itself killed, an
        // empty report or one that cannot be appended, the entries ARE re-run, and in the no-rung case
        // no closing turn is even started; under the stalling scenario that sentence went into one
        // channel three times. A record a supervisor reads is not a plan, it is what happened.
        void Record_KilledTurn(string note)
        {
            Append_TurnEnded(state, requestId, attempt, pending, killed, TurnOutcomes.TIMEOUT, note);
        }

        void Record_KilledTurn_Retried(string why)
        {
            Record_KilledTurn($"{ClosingTurn_Words.Describe_Kill(killed)} while working — {why}; these entries are retried as before");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _log.Log_Warning(state.OrchId, $"Turn {requestId} was {ClosingTurn_Words.Describe_Kill(killed)} but this session has no transcript id to resume — no closing turn is possible, so its entries stay pending and are retried as before");
            Record_KilledTurn_Retried("there is no transcript id to resume, so no closing turn was possible");
            Record_Failure(stateFile, state, pending, tracker, killed, requestId, attempt, executor, null, turnEndedAlreadyAppended: true);
            return;
        }

        _log.Log_Info(state.OrchId, $"Turn {requestId} was {ClosingTurn_Words.Describe_Kill(killed)} after {killed.Elapsed.TotalMinutes:F1} min{(killed.BrakeKill == null ? string.Empty : $" ({killed.BrakeKill})")} — running closing turn {closingRequestId} on session {sessionId} (up to {closingTimeout.TotalMinutes:F1} min, {ClosingTurn_Words.BUDGET_FLAG} {ClosingTurn_Words.Describe_Budget(ClosingTurn_Words.BUDGET_USD)})");

        ITurnResult? closing;

        try
        {
            closing = await executor.Execute_ClosingTurn_Async(state, roleConfig, sessionId, closingRequestId, sources, environment, closingTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The app is stopping: nothing is recorded and the entries are pending at the next
            // start, exactly as for a work turn cancelled at the same moment. NOT EVEN THE KILLED
            // TURN'S RECORD — writing one here would leave the only trace of this turn saying
            // something about a closing turn that was interrupted before it could mean anything.
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Error(state.OrchId, $"Closing turn {closingRequestId} could not be started — the killed turn's entries stay pending and are retried as before", ex);
            Record_KilledTurn_Retried("the closing turn could not be started");
            Record_Failure(stateFile, state, pending, tracker, killed, requestId, attempt, executor, null, turnEndedAlreadyAppended: true);
            return;
        }

        if (closing == null)
        {
            _log.Log_Warning(state.OrchId, $"'{state.MemberId}' runs on the {SessionRunner_Names.Get_Word(executor.Kind)} transport, which has no print rung wired beneath it: no closing turn can be run for {requestId}, so its entries stay pending and are retried as before");
            Record_KilledTurn_Retried($"the {SessionRunner_Names.Get_Word(executor.Kind)} transport has no print rung to run a closing turn on");
            Record_Failure(stateFile, state, pending, tracker, killed, requestId, attempt, executor, null, turnEndedAlreadyAppended: true);
            return;
        }

        var closingOutcome = TurnOutcomes.Describe(closing);

        if (!TurnOutcomes.Is_Success(closing))
        {
            _log.Log_Warning(state.OrchId, $"Closing turn {closingRequestId} reported nothing ({closingOutcome}, exit {closing.ExitCode}) — no state was written, so the killed turn's entries stay pending and are retried as before; {Tail(closing.RawStderr, 200)}");
            Record_KilledTurn_Retried($"the closing turn reported nothing ({closingOutcome})");
            Append_TurnEnded(state, closingRequestId, 1, pending, closing, closingOutcome, CLOSING_TURN_FAILED_NOTE);
            Record_Failure(stateFile, state, pending, tracker, killed, requestId, attempt, executor, null, turnEndedAlreadyAppended: true, closingResult: closing);
            return;
        }

        // AN EMPTY REPORT IS NOT A REPORT. Write_Reply_Async answers true for empty text — it files the
        // "(no message)" entry, which is right for an ordinary turn that chose to say nothing — so a
        // closing turn that came back with nothing at all still retired the brief and left the
        // supervisor a "(no message)" entry where the state of the work should have been. The one
        // thing this whole path exists to produce is the report, so no report is a failed closing turn.
        if (string.IsNullOrWhiteSpace(closing.ResultText))
        {
            _log.Log_Warning(state.OrchId, $"Closing turn {closingRequestId} ended cleanly but said nothing — there is no state to file, so the killed turn's entries stay pending and are retried as before");
            Record_KilledTurn_Retried("the closing turn ended cleanly but said nothing");
            Append_TurnEnded(state, closingRequestId, 1, pending, closing, TurnOutcomes.ERROR, CLOSING_TURN_EMPTY_NOTE);
            Record_Failure(stateFile, state, pending, tracker, killed, requestId, attempt, executor, null, turnEndedAlreadyAppended: true, closingResult: closing);
            return;
        }

        var delivery = await Write_Reply_Async(state, sources, closing.ResultText);

        if (!delivery.AllLanded)
        {
            _log.Log_Error(state.OrchId, $"Closing turn {closingRequestId} reported but its entry could not be appended — the channel stayed locked, so the killed turn's entries stay pending and are retried as before", null);
            Record_KilledTurn_Retried("the closing turn reported but its entry could not be appended (channel locked)");
            Append_TurnEnded(state, closingRequestId, 1, pending, closing, TurnOutcomes.ERROR, "closing turn — the state was reported but its entry could not be appended (channel locked)");
            Record_Failure(stateFile, state, pending, tracker, killed, requestId, attempt, executor, null, turnEndedAlreadyAppended: true, closingResult: closing);
            return;
        }

        // ONLY WHAT THE REPORT ANSWERED IS CONSUMED. The closing turn is asked where it got to, not to
        // answer the pending set, and it addresses its one entry freely — so a blanket advance retires
        // entries nothing replied to. The rest keep their cursors and are pending again next tick.
        HashSet<string> answered = new(delivery.AnsweredSourceKeys, SOURCE_KEYS);
        var unanswered = pending.Select(item => item.Source.Key).Distinct(SOURCE_KEYS).Where(key => !answered.Contains(key)).ToList();

        var killedNote = $"{ClosingTurn_Words.Describe_Kill(killed)} {KILLED_WHILE_WORKING_NOTE}";

        Record_KilledTurn(unanswered.Count == 0
            ? killedNote
            : $"{killedNote}, except {string.Join(", ", unanswered)} — the report did not address {(unanswered.Count == 1 ? "that channel" : "those channels")}, so its entries stay pending and are handed to the next turn");

        Append_TurnEnded(state, closingRequestId, 1, pending, closing, closingOutcome, CLOSING_TURN_NOTE);

        // ONE record for the pair, under the KILLED turn's number and request id — which is what
        // makes those entries stop being pending, and what stops the same request id ever running
        // again.
        //
        // THE COST IS UNKNOWN AND SAYS SO. It used to be `killed.TotalCostUsd ?? closing.TotalCostUsd`,
        // and a killed turn has no result document — so it was ALWAYS the closing turn's pennies,
        // filed as the cost of a turn that is 30 minutes of work in production (probed at $0.0127 on
        // 2026-09-09). Decision 10 wants one number that cannot disagree with itself; the closing
        // turn's spend is on the closing turn's own record, and the killed turn's is genuinely not
        // known here. A confident wrong number is worse than an absent one.
        var firstIndex = pending.Count == 0 ? 0 : pending[0].Entry.Index;
        var lastIndex = pending.Count == 0 ? 0 : pending[^1].Entry.Index;
        var executed = ExecutedTurn_Factory.Create(turnNumber, requestId, firstIndex, lastIndex, DateTime.UtcNow, TurnOutcomes.TIMEOUT, killed.TotalCostUsd, sessionId);

        var recorded = PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(state, executed, sessionId, Advance_Cursors(state, sources, pending, answered));

        PrintSessionState_Store.Write(stateFile, recorded);
        tracker.LastFailureAt = null;
        tracker.FirstTurnSinceStart = false;

        // THE HOLD IS SPENT ONLY IF THE ENTRIES WERE. A closing report is asked where the turn got to,
        // not to answer the pending set, so a source it did not address keeps its cursor and is
        // pending again on the next tick — and clearing the stamp would start a FRESH window on a
        // report that had already waited one, which is the failed-turn finding of 2026-09-10 applied
        // to the half of a turn that did not land. Left alone, the untouched stamp is already past its
        // window, so what the report did not answer goes on the very next tick.
        if (unanswered.Count == 0)
            Note_TrafficDelivered(tracker);

        _log.Log_Info(state.OrchId, $"Closing turn {closingRequestId} ended — {closingOutcome}, {Describe_Cost(closing)}, {closing.Elapsed.TotalSeconds:F1} s wall; {(unanswered.Count == 0 ? "the killed turn's entries are delivered" : $"the killed turn's entries are delivered except {string.Join(", ", unanswered)}, which the report did not address and which stay pending")} and the next turn for '{state.MemberId}' starts from the channel");

        Warn_IfDeadlineKillsRepeat(state, recorded.ExecutedTurns, requestId);
    }

    /// <summary>
    /// THE BOUND THAT REPLACED THE ONE THE CLOSING TURN REMOVED. A deadline kill deliberately spends no
    /// attempt, so <see cref="MAX_ATTEMPTS"/> — and the stall alert that is the app's only "this session
    /// keeps dying" — can never be reached by kills: probed on 2026-09-09, three briefs, three kills,
    /// three successful closing turns, <c>FailedAttempts</c> 0 and not one word anywhere.
    ///
    /// <para>
    /// IT ALERTS AND CHANGES NOTHING ELSE. The closing turns are working, the reports are landing and
    /// the entries are being answered; the wasteful retry this stage removed must not come back in the
    /// shape of a stall. What is wrong is that the work does not fit inside a turn, and the fix for
    /// that is a smaller brief — which is the reader's job, not the dispatcher's.
    /// </para>
    /// <para>
    /// ONE LINE EVERY <see cref="ClosingTurn_Words.KILLS_BEFORE_ALERT"/> KILLS, not one per kill after
    /// the third (decision 14: an owner-facing repeat that stacks is the waterfall this system exists
    /// to prevent). It goes where the stall alert goes — to the supervisor for a member, to the owner
    /// for a role whose own channel IS the owner's (decision 15: they can act on it, by asking for the
    /// work to be broken up).
    /// </para>
    /// </summary>
    void Warn_IfDeadlineKillsRepeat(IPrintSessionState state, IReadOnlyList<IExecutedTurn> executedTurns, string requestId)
    {
        var kills = ClosingTurn_Rule.Count_TrailingDeadlineKills(executedTurns);

        if (kills < ClosingTurn_Words.KILLS_BEFORE_ALERT || kills % ClosingTurn_Words.KILLS_BEFORE_ALERT != 0)
            return;

        // EVERY KIND OF KILL IS COUNTED HERE, and the advice differs by kind (review finding,
        // 2026-09-11): a deadline says the brief is bigger than a turn; the silence brake or the loop
        // detector says the member is stuck, not slow. Each turn_ended record names which fired.
        var alert = $"'{state.MemberId}' has been killed {kills} turns in a row — at the deadline, by the silence brake or by the loop detector; each turn_ended record says which. Every one reported where it got to, so nothing is lost, but no turn has finished its work";

        _log.Log_Warning(state.OrchId, alert);

        ChannelAppender.Append_AppEntry(
            state.ChannelFilePath,
            Stall_Audience(state),
            $"{DEADLINE_KILLS_SUBJECT} — '{state.MemberId}', {kills} turns in a row",
            $"{alert}\n\nNothing is retried and nothing is waiting: each killed turn was closed down and its entries answered. If they were deadline kills, the briefs are bigger than a turn — the next one wants to be smaller, or split. If the silence brake or the loop detector fired, the member is stuck rather than slow — look at what it was doing, not at the size of the brief.\n\nlast request_id: {requestId}\nturns: {string.Join(", ", executedTurns.TakeLast(kills).Select(turn => turn.RequestId))}",
            DateTime.Now);
    }

    /// <summary>What the killed turn's own record says, on the one branch where a closing turn did run and did report.</summary>
    const string KILLED_WHILE_WORKING_NOTE = "while working — a closing turn wrote where it got to, and these entries are not re-run";

    const string CLOSING_TURN_NOTE = "closing turn — where the killed turn got to, not a new answer to those entries";

    const string CLOSING_TURN_FAILED_NOTE = "closing turn — no state was reported, so the killed turn's entries are retried as before";

    const string CLOSING_TURN_EMPTY_NOTE = "closing turn — it ended cleanly and said nothing, so there is no state to file and the killed turn's entries are retried as before";

    /// <summary>
    /// EVERY DELIVERED ENTRY IS RECORDED, whichever channel it came from and whether or not the session
    /// answered that channel. Delivered means handed over; a supervisor that reads a spoke and says
    /// nothing has still read it, and re-handing it the same entry next tick would be a loop.
    ///
    /// <para>
    /// UNLESS THE TURN NEVER GOT TO READ THEM — <paramref name="answeredSourceKeys"/>, which only the
    /// closing turn passes. "Handed over" is the right test for a turn that ran to the end and chose
    /// its silence; it is the wrong test for a turn that was killed and whose replacement was asked a
    /// different question ("say where you got to"), not the pending one. Measured on the merged
    /// feature 2026-09-09: a supervisor woken by the owner, killed at the deadline, filed its report
    /// to a spoke — and the owner's question was retired by a report that never mentioned it, with
    /// both records agent-audience so nothing reached the phone and nobody above a supervisor to
    /// notice. A source the closing report did not write to keeps its cursor and is pending again.
    /// </para>
    ///
    /// <para>
    /// The live file is re-read here rather than reused from the tick's read, because the turn has since
    /// appended to it: pruning against a stale copy would keep identities compaction has moved out and,
    /// worse, would be a second opinion about what the file contains.
    /// </para>
    /// </summary>
    /// <summary>
    /// The general's configured model, applied to its registration when it differs. Members are left
    /// alone: their model may legitimately differ from the role default (a per-member choice in
    /// session.json, an orchestration override), and their re-registration path already reconciles it.
    /// </summary>
    IPrintSessionState Refresh_GeneralModel_IfChanged(string stateFile, IPrintSessionState state)
    {
        if (state.Role != SessionRoles.General)
            return state;

        string? configured;

        try
        {
            configured = _configProvider.Get_Current().GeneralSupervisorModel;
        }
        catch
        {
            // A config that cannot be read is not a reason to skip a turn: keep what the state has.
            return state;
        }

        if (string.Equals(configured, state.Model, StringComparison.Ordinal))
            return state;

        var refreshed = PrintSessionState_Factory.CreateFrom_Existing_Relaunched(state, state.WorkingDirectory, configured);
        PrintSessionState_Store.Write(stateFile, refreshed);

        _log.Log_Info(state.OrchId, $"General supervisor was registered with model '{state.Model ?? "(default)"}' and config.json now says '{configured ?? "(default)"}' — this turn runs on the configured one");

        return refreshed;
    }

    IReadOnlyList<ITurnCursor> Advance_Cursors(IPrintSessionState state, IReadOnlyList<ITurnSource> sources, IReadOnlyList<PendingEntry> pending, IReadOnlySet<string>? answeredSourceKeys = null)
    {
        var byKey = state.Cursors.ToDictionary(cursor => cursor.SourceKey, SOURCE_KEYS);

        List<ITurnCursor> advanced = [];

        foreach (var source in sources)
        {
            if (!byKey.TryGetValue(source.Key, out var cursor))
                continue;

            // THE CLOSING TURN'S EXCEPTION, and only its (see Close_Down_KilledTurn_Async). Nothing is
            // dropped here: the cursor is carried over untouched, so the source keeps its history and
            // its entries are pending again on the next tick.
            if (answeredSourceKeys != null && !answeredSourceKeys.Contains(source.Key))
            {
                advanced.Add(cursor);
                continue;
            }

            var delivered = pending.Where(item => SOURCE_KEYS.Equals(item.Source.Key, source.Key)).Select(item => item.Entry).ToList();
            var entries = ChannelHistory_Cache.Read_Entries(source.ChannelFilePath);

            advanced.Add(TurnCursor_Factory.CreateFrom_Delivered(cursor, state.Role, entries, delivered));
        }

        return advanced;
    }

    // ----- the reply -----

    /// <summary>
    /// The session's final message, filed into the channels it addressed. Returns whether every part
    /// landed — a false is a locked channel, which the caller treats as a failed turn so the same
    /// entries are still pending on the retry.
    ///
    /// <para>
    /// A SESSION WITH ONE CHANNEL IS NOT SPLIT. It was never told the format, and a report that happened
    /// to contain a line beginning "to:" would be cut in half by a rule written for somebody else.
    /// </para>
    /// <para>
    /// A PARTIAL WRITE COSTS A SECOND, DIFFERENT ANSWER — not a duplicate, and the difference matters.
    /// If one of several blocks cannot be appended — its channel held by another writer for the whole
    /// budget — the turn is reported failed and RE-RUN, so the model answers again: a member that already
    /// received verdict A can then receive verdict B and may have acted on the superseded one. The
    /// alternative is to accept the partial write, which loses the block that failed with nobody told and
    /// leaves the session it was for waiting for ever. Both are bad; this one is bad WHERE SOMEONE CAN SEE
    /// IT, which is the rule everywhere else here. (An earlier version of this comment claimed the retry
    /// merely duplicates what landed. It does not, and a comment that understates its own trade is how the
    /// next reader accepts it without noticing.)
    /// </para>
    /// </summary>
    /// <param name="resultText">
    /// NULLABLE, because <see cref="ITurnResult.ResultText"/> is: a turn can succeed and say nothing.
    /// Both splitters below already accept null and answer with the "(no message)" entry, so the only
    /// thing a non-null signature bought was a compiler warning at the one call site — and a signature
    /// that promises what its caller cannot give is a lie that the next reader resolves with a `!`.
    /// </param>
    /// <summary>
    /// FILES EVERY FINAL MESSAGE THE TURN SUPERSEDED, oldest first, through the SAME splitters and
    /// under the same author word as the turn's own entry — because that is what each of them was:
    /// a message the session wrote intending it to be its entry.
    ///
    /// <para>
    /// Measured 3× on 2026-09-09/10: a session writes its report, a BACKGROUND sub-agent
    /// (<c>Task</c> with <c>run_in_background</c>) returns, the CLI resumes the session, a later
    /// message is produced, and THAT becomes the entry. A 19,771-character report and a nine-agent
    /// review were lost that way, and both turns reported success.
    /// </para>
    /// <para>
    /// IT CAN COST A SECOND, DIFFERENT ANSWER, exactly as <see cref="Write_Reply_Async"/> can and for
    /// the same reason: a superseded final that lands, followed by a final one that cannot, fails the
    /// turn and re-runs it, so what landed here may be written again. That trade is the existing one
    /// — bad WHERE SOMEONE CAN SEE IT — and it is not made worse by writing these first: writing them
    /// AFTER the entry would file them behind the answer they preceded, which is the one ordering a
    /// reader cannot recover from.
    /// </para>
    /// </summary>
    async Task<bool> Write_SupersededFinals_Async(IPrintSessionState state, IReadOnlyList<ITurnSource> sources, ITurnResult result, string requestId)
    {
        if (result.SupersededFinals.Count == 0)
            return true;

        _log.Log_Info(state.OrchId, $"Turn {requestId} wrote {result.SupersededFinals.Count} final message(s) before the one that became its entry — filing {(result.SupersededFinals.Count == 1 ? "it" : "them")} first so nothing is lost");

        foreach (var superseded in result.SupersededFinals)
        {
            if (!(await Write_Reply_Async(state, sources, superseded)).AllLanded)
                return false;
        }

        return true;
    }

    /// <summary>
    /// TELLS THE SESSION, in its own channel, that it did this — the same shape as the misaddressed
    /// note, and for the same reason: the alternative is a channel that has grown an entry nobody
    /// asked for, with no way to find out why. Agent audience (decision 15): the action is the
    /// session's, and the owner cannot take it.
    /// </summary>
    void Append_SupersededNotice(IPrintSessionState state, ITurnResult result)
    {
        var count = result.SupersededFinals.Count;

        if (count == 0)
            return;

        var plural = count == 1 ? string.Empty : "s";

        ChannelAppender.Append_AppEntry(
            state.ChannelFilePath,
            AppEntryAudiences.Agent,
            $"{SUPERSEDED_FINAL_SUBJECT} — {state.MemberId}, {count} message{plural}",
            $"This turn wrote {count} final-looking message{plural} and then carried on working; a LATER message became the turn's result, and the result is what the bridge files as the entry. "
                + $"Nothing was dropped — {(count == 1 ? "the earlier message was" : "the earlier messages were")} filed above, in order, before the final one.\n\n"
                + "This is what a BACKGROUND sub-agent does when it returns after you have written your report: it re-opens the turn, and your next message replaces your entry. "
                + "Wait for every sub-agent to return BEFORE you write your final message.",
            DateTime.Now);
    }

    /// <summary>
    /// The owner entry this turn's answer answers: the LAST owner message it was handed from the
    /// session's own channel — the one the owner is looking at when the answer arrives. Null when the
    /// turn carried no owner message (member traffic, a boot).
    /// </summary>
    static int? Find_AnsweredOwnerEntry_OrNull(IPrintSessionState state, IReadOnlyList<PendingEntry> pending)
    {
        for (var index = pending.Count - 1; index >= 0; index--)
        {
            var item = pending[index];

            if (item.Entry.Author == ChannelAuthors.Owner && string.Equals(item.Source.ChannelFilePath, state.ChannelFilePath, StringComparison.OrdinalIgnoreCase))
                return item.Entry.Index;
        }

        return null;
    }

    async Task<ReplyDelivery> Write_Reply_Async(IPrintSessionState state, IReadOnlyList<ITurnSource> sources, string? resultText, int? answersOwnerEntry = null)
    {
        var author = SessionRole_Names.Get_Author(state.Role);
        var ownChannel = state.ChannelFilePath;

        // THE FIRST PART THAT LANDS IN THE SESSION'S OWN CHANNEL CARRIES THE LINK, and only that one:
        // it is the answer to the owner's message, and the phone threads it as a reply
        // (Running.ReplyLinks). Later parts follow it in the chat as they always did.
        var linkPending = answersOwnerEntry != null;

        async Task<bool> Append_Async(string target, string subject, string body)
        {
            var index = await Append_SessionEntry_WithRetry_OrNull_Async(target, author, subject, body);

            if (index == null)
                return false;

            if (linkPending && string.Equals(target, ownChannel, StringComparison.OrdinalIgnoreCase))
            {
                ReplyLinks.Record_Answer(ownChannel, index.Value, answersOwnerEntry!.Value);
                linkPending = false;
            }

            return true;
        }

        // WHICH FILES THE REPLY ACTUALLY REACHED, collected as they are written rather than worked out
        // again afterwards from the same text — a second reading of the addressing rule is how the
        // question "did this reply answer the owner" would come to have two answers.
        HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);

        if (sources.Count <= 1)
        {
            var (soleSubject, soleBody) = PrintTurnEntry_Splitter.Split(resultText);
            var soleLanded = await Append_Async(ownChannel, soleSubject, soleBody);

            if (soleLanded)
                written.Add(ownChannel);

            return Describe_Delivery(soleLanded, sources, written);
        }

        var byKey = sources.ToDictionary(source => source.Key, SOURCE_KEYS);
        var blocks = TurnReply_Splitter.Split(resultText);

        if (blocks.Count == 0)
        {
            var (emptySubject, emptyBody) = PrintTurnEntry_Splitter.Split(resultText);
            var emptyLanded = await Append_Async(ownChannel, emptySubject, emptyBody);

            if (emptyLanded)
                written.Add(ownChannel);

            return Describe_Delivery(emptyLanded, sources, written);
        }

        List<string> misaddressed = [];
        var allLanded = true;

        foreach (var block in blocks)
        {
            var target = ownChannel;

            if (block.SourceKey != null)
            {
                if (byKey.TryGetValue(block.SourceKey, out var source))
                    target = source.ChannelFilePath;
                else
                    misaddressed.Add(block.SourceKey);
            }

            var (subject, body) = PrintTurnEntry_Splitter.Split(block.Text);

            if (await Append_Async(target, subject, body))
                written.Add(target);
            else
                allLanded = false;
        }

        // NEVER SILENTLY REROUTED. The block is in the owner channel either way; this says it was meant
        // for somewhere else, so a supervisor addressing a member that has since closed — or mistyping
        // its id — reads about it instead of wondering why the member never answered.
        if (misaddressed.Count > 0)
        {
            ChannelAppender.Append_AppEntry(
                ownChannel,
                AppEntryAudiences.Agent,
                $"{MISADDRESSED_SUBJECT} {state.MemberId} — {string.Join(", ", misaddressed.Distinct())}",
                $"Part(s) of the last turn were addressed to {string.Join(", ", misaddressed.Distinct().Select(key => $"'{key}'"))}, which {(misaddressed.Distinct().Count() == 1 ? "is not a channel" : "are not channels")} this session is woken by. They were written HERE instead, in order.\n\nAddressable this turn: {string.Join(", ", sources.Select(source => source.Key))}",
                DateTime.Now);
        }

        return Describe_Delivery(allLanded, sources, written);
    }

    /// <summary>
    /// WHAT ONE REPLY DID: whether every part of it landed, and which of the session's sources it was
    /// an answer TO. The second half exists for the closing turn — see
    /// <see cref="Close_Down_KilledTurn_Async"/> — and every other caller reads only the first.
    /// </summary>
    readonly record struct ReplyDelivery(bool AllLanded, IReadOnlyCollection<string> AnsweredSourceKeys);

    /// <summary>The sources whose channel file the reply was written into — a source is answered when its file was written, and by nothing else.</summary>
    static ReplyDelivery Describe_Delivery(bool allLanded, IReadOnlyList<ITurnSource> sources, HashSet<string> writtenChannelFiles)
    {
        return new ReplyDelivery(allLanded, [.. sources.Where(source => writtenChannelFiles.Contains(source.ChannelFilePath)).Select(source => source.Key)]);
    }

    /// <param name="turnEndedAlreadyAppended">
    /// True only on the closing-turn path, which has already written the killed turn's record with a
    /// note naming the branch it took. Everything else about a failure — the attempt counter, the
    /// missing-transcript recovery, the runner change, the stall alert — is identical, and that is
    /// the point of routing through here rather than growing a second failure path.
    /// </param>
    /// <param name="closingResult">
    /// THE OTHER PROCESS THAT RAN UNDER THIS REQUEST ID, on the closing-turn path. It matters for one
    /// question and it is the important one: the CLOSING turn is the process that resumes the killed
    /// transcript, so it is the process that can come back with "No conversation found with session
    /// ID" — and every branch here was handed the KILLED turn's result, which cannot contain that
    /// sentence because it never passed <c>--resume</c>. Probed on 2026-09-09: the CLI said the
    /// transcript was gone, nothing listened, and the dead id stayed claimed to be resumed and
    /// refused for ever, which is the exact stall the recovery below was written to end.
    /// </param>
    void Record_Failure(string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, SessionTracker tracker, ITurnResult result, string requestId, int attempt, ITurnExecutor executor, string? note, bool turnEndedAlreadyAppended = false, ITurnResult? closingResult = null)
    {
        var outcome = note == null ? TurnOutcomes.Describe(result) : TurnOutcomes.ERROR;

        if (!turnEndedAlreadyAppended)
            Append_TurnEnded(state, requestId, attempt, pending, result, outcome, note);

        // THE TRANSCRIPT IS NOT THERE, AND THE CLI SAID SO. The id is claimed BEFORE the process starts,
        // so a first turn that dies before the CLI creates the transcript — an invalid --model, an auth
        // failure, the binary not on PATH — leaves every later attempt resuming something that was never
        // made. MEASURED 2026-09-06 against 2.1.263: `claude -p --resume <uuid never created>` exits 1
        // with "No conversation found with session ID: <uuid>". All three attempts died that way, the
        // session stalled, and new traffic only reset the counter and retried the same doomed resume:
        // recovery meant deleting print-session.json by hand.
        //
        // REACTED TO, NOT PREDICTED. The obvious guess — "never completed a turn, so do not resume" — is
        // wrong in the other direction: an attempt that ran and merely answered badly has a transcript
        // worth keeping, which is the case the previous stage measured and pinned. The CLI distinguishes
        // the two for us; this listens to it and unspends the id so the next attempt claims a fresh one.
        if (Transcript_IsGone(result) || (closingResult != null && Transcript_IsGone(closingResult)))
        {
            _log.Log_Warning(state.OrchId, $"'{state.MemberId}': the CLI has no transcript for session {state.SessionId} — the id was claimed but never created, so the next attempt starts a fresh one instead of resuming a conversation that does not exist");
            state = PrintSessionState_Factory.CreateFrom_Existing_SessionUnclaimed(state, Guid.NewGuid().ToString());
        }

        // THE ATTEMPTS BELONGED TO THE TRANSPORT THAT BROKE. A session that has just walked down the
        // fallback ladder starts its counter again, or the two counters coincide — three deaths and
        // three attempts — and it stalls on the rung it stepped off having never tried the one
        // below. Reported as attempt 1 of the new runner, because that is what it is.
        if (executor.Consume_RunnerChange(state.OrchId, state.MemberId))
        {
            _log.Log_Warning(state.OrchId, $"'{state.MemberId}' changed runner after {attempt} failed attempt(s) — the attempt counter starts again on the new one");
            state = PrintSessionState_Factory.CreateFrom_Existing_AttemptsReset(state);
            tracker.StalledOnSignature = null;
        }

        if (Record_UsageLimit_IfNamed(stateFile, state, pending, tracker, result, requestId))
            return;

        var failed = PrintSessionState_Factory.CreateFrom_Existing_AttemptFailed(state);
        PrintSessionState_Store.Write(stateFile, failed);
        tracker.LastFailureAt = DateTime.Now;

        if (failed.FailedAttempts >= MAX_ATTEMPTS)
        {
            tracker.StalledOnSignature = Describe_PendingSignature(pending);

            var alert = $"Turn {requestId} failed {failed.FailedAttempts} times ({outcome}{(note == null ? string.Empty : $": {note}")}) — not retried until new traffic arrives in the channels";
            _log.Log_Error(state.OrchId, alert, null);

            ChannelAppender.Append_AppEntry(
                state.ChannelFilePath,
                Resolve_StallAlertAudience(state),
                StallAlert_Decider.Build_Subject(state.MemberId, state.NextTurnNumber, $"{outcome} × {failed.FailedAttempts}"),
                $"{alert}\n\nrequest_id: {requestId}\n{Describe_Traffic(pending)}\nlast exit_code: {result.ExitCode}\napi_error_status: {Describe_ApiErrorStatus(result)}\nstderr (tail): {Tail(result.RawStderr, 600)}",
                DateTime.Now);

            return;
        }

        _log.Log_Warning(state.OrchId, $"Turn {requestId} attempt {attempt} {outcome}{(note == null ? string.Empty : $" ({note})")} — retry after {_retryBackoff.TotalSeconds:F0} s; {Tail(result.RawStderr, 200)}");
    }

    /// <summary>
    /// A QUOTA REFUSAL IS AN APPOINTMENT, NOT A FAILED ATTEMPT. Returns whether this failure was one:
    /// true means the turn has been scheduled and the caller must not count, back off or stall it.
    ///
    /// <para>
    /// MEASURED on the VPS 2026-09-08/09. <c>claude</c> refuses for quota with
    /// <c>api_error_status: 429</c> and a result of "You've hit your weekly limit · resets 5am
    /// (Europe/Berlin)". The dispatcher treated that like any other error — three attempts
    /// <see cref="_retryBackoff"/> apart, then a stall until new traffic arrived — so the three
    /// tries were spent inside the first three minutes of a window that had hours to run, and the
    /// orchestration sat dead for 167 to 509 minutes (twice, for a whole night). Three retries a
    /// minute apart cannot outlast a quota; waiting for the stated reset can.
    /// </para>
    /// <para>
    /// DECISION 21: A GUARD THAT CANNOT EVALUATE ITS PREDICATE SAYS SO AND ALLOWS. When the refusal
    /// names no clock this parser is sure of, the answer is false and EVERYTHING stays as it was —
    /// the counter, the backoff, the stall, the alert — plus one line in
    /// <c>orchestrator.log.jsonl</c> naming the text that could not be read. Nothing goes to the
    /// owner's phone for it (decision 15): there is nothing they can do about wording.
    /// </para>
    /// <para>
    /// THE APPOINTMENT IS IN THE STATE FILE, not in <paramref name="tracker"/>, so a bridge restart
    /// inside the window keeps it; the tracker's failure stamp is CLEARED so the two waits cannot
    /// both apply. Decision 8 is untouched: this schedules the PENDING turn under its existing
    /// request id, and every other guard — the pending-set signature, the executed-id skip, the
    /// coalesce window — is left exactly where it was, so a request closed or executed in the
    /// meantime is still never re-run.
    /// </para>
    /// </summary>
    bool Record_UsageLimit_IfNamed(string stateFile, IPrintSessionState state, IReadOnlyList<PendingEntry> pending, SessionTracker tracker, ITurnResult result, string requestId)
    {
        // THE WHOLE RESULT, NOT ITS PROSE. LimitReset_Parser.Is_Refusal also answers "did this turn
        // report success", which a text-only gate could not: probe 2026-09-09 parked a session that
        // had exited 0 for fourteen hours, because a successful reply the channel refused comes
        // through here with the MODEL'S words as the result text. See that method for both probes.
        if (!LimitReset_Parser.Is_Refusal(result))
            return false;

        var nowUtc = DateTime.UtcNow;
        var reading = LimitReset_Parser.Read_OrNull(result.ResultText, result.ApiErrorStatus, nowUtc);

        if (reading == null)
        {
            _log.Log_Warning(state.OrchId, $"Turn {requestId} was refused for a usage limit (api_error_status: {Describe_ApiErrorStatus(result)}) but no reset time could be read from it (none named, or one further out than the {LimitReset_Parser.MAX_DEFERRAL.TotalHours:0} h cap), so it keeps the ordinary {_retryBackoff.TotalSeconds:F0} s backoff and its {MAX_ATTEMPTS} attempts — unread text: '{Tail(result.ResultText ?? string.Empty, 200)}'");
            return false;
        }

        var retryAtUtc = reading.ResetsAtUtc + PrintTurn_Words.LIMIT_RESET_MARGIN;
        var appointment = LimitDeferral_Wording.Describe_Appointment(retryAtUtc, nowUtc);

        PrintSessionState_Store.Write(stateFile, PrintSessionState_Factory.CreateFrom_Existing_LimitDeferred(state, retryAtUtc));

        // The backoff must not ALSO hold this turn: the appointment is the whole schedule now, and a
        // stall signature left standing would refuse to run the turn when the window reopens.
        tracker.LastFailureAt = null;
        tracker.StalledOnSignature = null;

        // THE TRAFFIC THAT BOUGHT THE APPOINTMENT IS NOT "NEW" TRAFFIC. Consider_Session announces a
        // deferral to anything that lands afterwards (F5); recording the signature here is what keeps
        // it from announcing the very entries this turn was refused over.
        tracker.DeferralAnnouncedSignature = Describe_PendingSignature(pending);

        _log.Log_Info(state.OrchId, $"Turn {requestId} was refused for a usage limit — retry scheduled {appointment} (from {reading.Describe_Source()})");

        // CHECKED, LIKE Append_TurnEnded's (F5, 2026-09-09). The return was dropped here, so a locked
        // channel swallowed the one entry that says why the session has gone quiet — and the state file
        // still holds the appointment, so the silence is real and nothing anywhere accounts for it.
        if (!ChannelAppender.Append_AppEntry(
                state.ChannelFilePath,
                Stall_Audience(state),
                $"{TURN_LIMITED_SUBJECT} {state.MemberId} turn {state.NextTurnNumber} — resumes {appointment}",
                $"Turn {requestId} was refused for a usage limit and is scheduled to run again {appointment}, read from {reading.Describe_Source()}. Nothing is lost and nothing else is needed: the attempt was NOT counted against the {MAX_ATTEMPTS}-attempt limit, and the same traffic is still pending under the same request id.\n\nrequest_id: {requestId}\n{Describe_Traffic(pending)}\napi_error_status: {Describe_ApiErrorStatus(result)}",
                DateTime.Now))
        {
            _log.Log_Warning(state.OrchId, $"the usage-limit notice for {requestId} could not be appended (channel locked) — the appointment IS recorded in the state file, so the session resumes {appointment} with nothing in its channel saying why it went quiet");
        }

        return true;
    }

    /// <summary>
    /// AWAITED, NOT SLEPT. This parks up to <see cref="ENTRY_APPEND_ATTEMPTS"/> × 
    /// <see cref="ENTRY_APPEND_RETRY_MILLISECONDS"/> of waiting, and since a multi-source session writes
    /// one entry PER ADDRESSED BLOCK it can now run several times in one turn — on a threadpool thread,
    /// with up to <c>MaxConcurrentTurns</c> turns doing the same. Blocking it held threads the pool
    /// could have used; awaiting releases them.
    ///
    /// <para>
    /// The delay deliberately takes NO cancellation token. It is reached only after the model has
    /// already answered, so cancelling here would throw away a reply that was paid for — and the whole
    /// wait is under a second, which no shutdown notices.
    /// </para>
    /// </summary>
    static async Task<int?> Append_SessionEntry_WithRetry_OrNull_Async(string channelFilePath, ChannelAuthors author, string subject, string body)
    {
        for (var attempt = 0; attempt < ENTRY_APPEND_ATTEMPTS; attempt++)
        {
            if (ChannelAppender.Append_SessionEntry_OrNull(channelFilePath, author, subject, body, DateTime.Now) is int index)
                return index;

            await Task.Delay(ENTRY_APPEND_RETRY_MILLISECONDS);
        }

        return null;
    }

    /// <summary>
    /// The record the supervisor reads (agent audience — the owner cannot act on it, decision 15):
    /// outcome, cost, duration, api_error_status. A member that "forgets" to report does not
    /// vanish; a turn that failed is visible where the brief was written. It goes in the session's OWN
    /// channel whichever channels the turn read, so one turn leaves exactly one record.
    /// </summary>
    void Append_TurnEnded(IPrintSessionState state, string requestId, int attempt, IReadOnlyList<PendingEntry> pending, ITurnResult result, string outcome, string? note)
    {
        var body =
            $"request_id: {requestId}\n" +
            $"outcome: {outcome}{(note == null ? string.Empty : $" — {note}")}\n" +
            $"attempt: {attempt}\n" +
            $"{Describe_Traffic(pending)}\n" +
            $"cost_usd: {(result.TotalCostUsd?.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}\n" +
            $"duration_ms: {(result.DurationMs?.ToString() ?? "unknown")} (api {(result.DurationApiMs?.ToString() ?? "unknown")}), wall {result.Elapsed.TotalSeconds:F1} s\n" +
            $"api_error_status: {Describe_ApiErrorStatus(result)}\n" +
            $"exit_code: {result.ExitCode}{(result.TimedOut ? $" ({ClosingTurn_Words.Describe_Kill(result)})" : string.Empty)}\n" +
            $"session_id: {result.SessionId ?? state.SessionId}";

        if (!ChannelAppender.Append_AppEntry(state.ChannelFilePath, AppEntryAudiences.Agent, $"{TURN_ENDED_SUBJECT} {state.MemberId} turn {requestId[(requestId.LastIndexOf('/') + 1)..]} — {outcome}", body, DateTime.Now))
            _log.Log_Warning(state.OrchId, $"turn_ended for {requestId} could not be appended (channel locked) — the turn itself is recorded in the state file");
    }

    /// <summary>
    /// What the turn was handed, per channel: <c>entries: owner [4]–[5], imp-1 [12]</c>. The indices are
    /// there to be read by a human next to the file, not to be compared with anything — the cursor stopped
    /// depending on them for the reasons <see cref="ChannelEntry_Digest"/> gives.
    /// </summary>
    static string Describe_Traffic(IReadOnlyList<PendingEntry> pending)
    {
        // SAID, not left as a bare "entries:". The empty set has one cause and it is worth reading in
        // the log and in the turn_ended entry: this was the boot turn, run so the session could greet.
        if (pending.Count == 0)
            return "entries: none (boot turn)";

        List<string> parts = [];

        foreach (var group in pending.GroupBy(item => item.Source.Key, StringComparer.Ordinal))
        {
            // MIN AND MAX, not first and last: the entries were globally ordered by stamp before they
            // got here, so within one channel the first listed is not necessarily the lowest — and
            // "imp-1 [12]–[3]" is a range no reader can make sense of.
            var indices = group.Select(item => item.Entry.Index).ToList();
            var lowest = indices.Min();
            var highest = indices.Max();

            parts.Add(lowest == highest ? $"{group.Key} [{lowest}]" : $"{group.Key} [{lowest}]–[{highest}]");
        }

        return $"entries: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// The pending set as one comparable string. Identities, in the order the turn would read them, so
    /// any change — a new entry anywhere, one channel gaining traffic while another is quiet — produces a
    /// different signature and restarts the coalesce window.
    /// </summary>
    static string Describe_PendingSignature(IReadOnlyList<PendingEntry> pending)
    {
        return string.Join('|', pending.Select(item => $"{item.Source.Key}:{ChannelEntry_Digest.Compute(item.Entry)}"));
    }

    /// <summary>
    /// The CLI's own words for "that session id names nothing", measured on 2.1.263 (2026-09-06):
    /// <c>No conversation found with session ID: &lt;uuid&gt;</c>, exit 1. Matched on the stable half of the
    /// sentence, so a reworded suffix does not stop it being recognised.
    ///
    /// <para>
    /// A STRING IS A FRAGILE CONTRACT AND THAT IS ACCEPTED HERE, because of which way it fails: if a
    /// future CLI renames this, the recovery stops firing and the behaviour falls back to the stall it
    /// replaced — visible, and no worse than before. The fake emits the same sentence for an unknown
    /// <c>--resume</c>, so a rename turns a contract test red rather than wedging a live session.
    /// </para>
    /// </summary>
    const string TRANSCRIPT_GONE_SIGNAL = "No conversation found with session ID";

    static bool Transcript_IsGone(ITurnResult result)
    {
        return (result.RawStderr ?? string.Empty).Contains(TRANSCRIPT_GONE_SIGNAL, StringComparison.OrdinalIgnoreCase)
            || (result.RawStdout ?? string.Empty).Contains(TRANSCRIPT_GONE_SIGNAL, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// WHO CAN ACT ON A STALL. For a member the alert lands in its spoke, where the supervisor reads it —
    /// agent audience, and the owner is rightly not told (decision 15).
    ///
    /// <para>
    /// For a SOLO, a GENERAL supervisor or an orchestration SUPERVISOR the session's own channel IS the
    /// owner's channel, and the only agent that reads it is the stalled session itself. Filed as an agent
    /// entry it was suppressed from the mirror as well, so a solo orchestration that gave up after three
    /// failures went quiet with nothing on the owner's phone and nothing in their topic — the exact
    /// silence this alert exists to break. A session that has stopped answering is something the owner can
    /// act on, so for those roles it is addressed to them.
    /// </para>
    /// </summary>
    static AppEntryAudiences Stall_Audience(IPrintSessionState state)
    {
        return state.Role is SessionRoles.Solo or SessionRoles.General or SessionRoles.Supervisor
            ? AppEntryAudiences.Owner
            : AppEntryAudiences.Agent;
    }

    /// <summary>
    /// <see cref="Stall_Audience"/>, minus the repeats: a turn whose stall has already reached the
    /// owner files every later stall of the SAME turn for the session only. See
    /// <see cref="StallAlert_Decider"/> for the 2026-09-11 incident (three identical alerts on the
    /// phone for one turn) and for why the memory is the channel rather than the tracker.
    /// </summary>
    AppEntryAudiences Resolve_StallAlertAudience(IPrintSessionState state)
    {
        var roleAudience = Stall_Audience(state);

        if (roleAudience != AppEntryAudiences.Owner)
            return roleAudience;

        var audience = StallAlert_Decider.Resolve_Audience(roleAudience, ChannelHistory_Cache.Read_Entries(state.ChannelFilePath), state.MemberId, state.NextTurnNumber);

        if (audience != roleAudience)
            _log.Log_Info(state.OrchId, StallAlert_Decider.Describe_Repeat(state.MemberId, state.NextTurnNumber));

        return audience;
    }

    static string Describe_ApiErrorStatus(ITurnResult result)
    {
        return result.ApiErrorStatus?.ToString() ?? "none";
    }

    static string Describe_Cost(ITurnResult result)
    {
        return result.TotalCostUsd == null ? "cost unknown" : $"{result.TotalCostUsd.Value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} USD";
    }

    static string Tail(string text, int maxLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : "…" + trimmed[^maxLength..];
    }
}

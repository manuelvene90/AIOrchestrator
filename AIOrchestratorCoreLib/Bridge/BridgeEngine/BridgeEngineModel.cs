using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
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
using AIOrchestratorCoreLib.Usage;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
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

    /// <summary>Transcript/probe freshness that means "this session is working right now".</summary>
    const int SESSION_MIDTURN_SECONDS = 120;

    /// <summary>How long an implementer may leave a brief unanswered before the app nudges it.</summary>
    const int IMPLEMENTER_NUDGE_MINUTES = 8;

    /// <summary>How long the owner may wait for their supervisor's acknowledgement before the app steps in.</summary>
    const int OWNER_REPLY_GRACE_SECONDS = 150;

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

    /// <summary>The owner often texts several messages in a row — quiet time before delivery as ONE entry.</summary>
    const int OWNER_AGGREGATION_SECONDS = 8;

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
    readonly Dictionary<string, (long? ThreadId, string OptionText, long GroupId)> _buttonOptions = [];
    readonly Queue<string> _buttonOrder = new();
    readonly Lock _buttonLock = new();
    long _buttonSequence;
    long _buttonGroupSequence;

    /// <summary>One alert per stall/budget EPISODE — cleared when traffic resumes (stalls only).</summary>
    readonly HashSet<string> _stallAlertedOrchIds = [];
    readonly HashSet<string> _budgetAlertedOrchIds = [];
    /// <summary>When each member was nudged — the nudge doubles as the PROBE that proves a watcher exists.</summary>
    readonly Dictionary<string, DateTime> _nudgedMemberUtc = [];
    readonly Dictionary<string, (string Line, DateTime SentUtc)> _lastHandoffLineByOrchId = [];
    readonly Lock _stateLock = new();
    readonly IOwnerDeliveryBuffer _ownerDeliveryBuffer = OwnerDeliveryBuffer_Factory.Create(OWNER_AGGREGATION_SECONDS);
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
    /// Owner messages handed over and NOT yet answered by their supervisor. Tracked so a receipt
    /// can never stay frozen on "thinking…" — the owner always learns what became of what they
    /// sent, even if the supervisor goes idle without replying.
    /// </summary>
    readonly Dictionary<string, PendingOwnerReply> _pendingOwnerReplies = [];

    sealed class PendingOwnerReply
    {
        public long? ThreadId;
        public long? ReceiptMessageId;
        public int SupervisorEntryCountAtDelivery;
        public DateTime DeliveredUtc;
        public bool Nudged;
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

        var loops = new List<Task> { Run_MirrorLoop_Async(cancellationToken) };

        if (_telegramClient != null)
            loops.Add(Run_InboundLoop_Async(cancellationToken));

        _log.Log_Info(GLOBAL_ORCH_ID, _telegramClient == null
            ? "Bridge started (file-only mode — Telegram not configured)"
            : "Bridge started (Telegram mirror + inbound routing active)");

        await Task.WhenAll(loops);
    }

    async Task Run_MirrorLoop_Async(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Execute_MirrorTick_Async(cancellationToken);
            }
            catch (OperationCanceledException)
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

    /// <summary>
    /// Is THIS session working right now? Its status-line probe hands us the exact transcript
    /// path, and a transcript growing in the last couple of minutes means a turn is in flight.
    /// </summary>
    static bool Is_SessionMidTurn(string usageFilePath)
    {
        try
        {
            if (!File.Exists(usageFilePath))
                return false;

            var transcriptPath = RateLimits_Reader.Read_TranscriptPath_OrNull(UsageTotals_Reader.Read_Text_Safe(usageFilePath));
            var probedFile = transcriptPath != null && File.Exists(transcriptPath) ? transcriptPath : usageFilePath;

            return (DateTime.UtcNow - File.GetLastWriteTimeUtc(probedFile)).TotalSeconds < SESSION_MIDTURN_SECONDS;
        }
        catch
        {
            return false;
        }
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

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId);

                if (!File.Exists(channelFile))
                    continue;

                var entries = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile));
                var memberKey = $"{session.OrchId}/{member.MemberId}";

                // Nothing to answer, or the implementer already spoke last: not a missed hand-off.
                if (entries.Count == 0 || entries[^1].Author == ChannelAuthors.Implementer)
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
                    await Nudge_Implementer_Async(session, member.MemberId, channelFile, entries[^1], quietFor, cancellationToken);
                    _nudgedMemberUtc[memberKey] = DateTime.UtcNow;
                    continue;
                }

                // ESCALATION. The nudge CHANGED the channel; a live watcher fires on that within
                // seconds. Still frozen after the grace window ⇒ there is no listener at all (the
                // turn ended abnormally, or never reached the point where a watcher is armed).
                // Nothing the session can do about that — only a respawn brings it back, and it
                // resumes from its channel, which is the designed durable state.
                if ((DateTime.UtcNow - nudgedUtc).TotalMinutes < ORPHAN_CONFIRM_MINUTES)
                    continue;

                _nudgedMemberUtc.Remove(memberKey);
                await Recover_OrphanedImplementer_Async(session, member.MemberId, cancellationToken);
            }
        }
    }

    async Task Nudge_Implementer_Async(
        IOrchestrationSession session,
        string memberId,
        string channelFile,
        Channels.ChannelEntry.IChannelEntry lastEntry,
        TimeSpan quietFor,
        CancellationToken cancellationToken)
    {
        ChannelAppender.Append_AppEntry(
            channelFile,
            "unread traffic — you have not answered",
            $"Entry [{lastEntry.Index}] FROM {lastEntry.Author.ToString().ToLowerInvariant()} has been waiting {SessionDuration_Formatter.Describe(quietFor)} with no reply from you. Read this channel from your last entry down and act on it. If your watcher never fired, re-arm it with the baseline captured BEFORE you read.",
            DateTime.Now);

        _log.Log_Warning(session.OrchId, $"{memberId} had unread traffic for {SessionDuration_Formatter.Describe(quietFor)} — nudged");
        Raise_OrchestrationActivity(session.OrchId);

        // The channel nudge above ALWAYS happens (it is what unsticks the implementer);
        // only the owner-facing text respects the topic's mode.
        if (_telegramClient == null || Resolve_EffectiveMode(session.OrchId) != TelegramDeliveryModes.Normal)
            return;

        try
        {
            await _telegramClient.Send_Message_Async(
                session.TelegramTopicId,
                $"⚠️ {memberId} left a brief unanswered for {SessionDuration_Formatter.Describe(quietFor)} — nudged it.",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log_Warning(session.OrchId, $"Idle-implementer alert send failed: {ex.Message}");
        }
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
            _log.Log_Info(append.Channel.OrchId, $"[{append.Channel.SpokeName}] entry #{entry.Index} FROM {entry.Author}: {entry.Subject}");

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
            var text = MirrorText_Formatter.Format(append.Channel, entry);

            // Special lines in the entry become REAL Telegram artifacts, never raw text:
            // IMAGE: <path> lines upload as photos; OPTION: <label> lines render as inline
            // decision buttons the owner can tap instead of typing.
            var photoPaths = Extract_MarkerLines(ref text, "IMAGE");
            var optionLabels = Extract_MarkerLines(ref text, "OPTION");

            // Italian layer (live config): the owner reads Italian on the phone; sessions and
            // channels stay English. The speaker prefix ("🟢 Com: ") is split off DETERMINISTICALLY
            // and reattached — a live translation once mangled it into garbage. Presence lines
            // (implementer spokes' "online") are canned app strings and stay English entirely.
            if (_configProvider.Get_Current().TelegramItalianLayer && append.Channel.IsOwnerChannel)
            {
                var (speakerPrefix, content) = Split_SpeakerPrefix(text);
                text = speakerPrefix + await _translator.Translate_ToItalian_Async(content, cancellationToken);
            }

            var chunks = TelegramMessage_Chunker.Chunk(text);

            if (chunks.Count == 0 && optionLabels.Count > 0)
                chunks = ["…"];

            try
            {
                for (var i = 0; i < chunks.Count; i++)
                {
                    var isLastChunk = i == chunks.Count - 1;

                    if (isLastChunk && optionLabels.Count > 0)
                        await _telegramClient.Send_MessageWithButtons_Async(threadId, chunks[i], Register_Buttons(threadId, optionLabels), cancellationToken);
                    else
                        await _telegramClient.Send_Message_Async(threadId, chunks[i], cancellationToken);
                }

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

    IReadOnlyList<(string Data, string Label)> Register_Buttons(long? threadId, IReadOnlyList<string> optionLabels)
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

                _buttonOptions[data] = (threadId, label, _buttonGroupSequence);
                _buttonOrder.Enqueue(data);
                buttons.Add((data, label));
            }

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
                var session = _launcher.Add_Implementer(request.OrchId);
                var newMember = session.Members[session.Members.Count - 1];

                // The REASON rides in the subject because App entries mirror subject-only — the
                // owner must never see a session appear (and burn tokens) without knowing why.
                Append_OrchestrationAppEntry(
                    request.OrchId,
                    $"implementer '{newMember.MemberId}' added — {request.Reason}",
                    $"New implementer '{newMember.MemberId}' spawned for orchestration '{request.OrchId}'. Its channel is {newMember.MemberId}/channel.md — brief it there.");
            }
            catch (Exception ex)
            {
                _log.Log_Error(request.OrchId, "add-implementer failed", ex);
                Append_OrchestrationAppEntry(request.OrchId, "add-implementer FAILED", $"Error: {ex.Message}");
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
                    else if (command == "limits")
                    {
                        await Send_LimitsReport_Async(client, message.MessageThreadId, cancellationToken);
                    }
                    else if (command == "diff")
                    {
                        await Send_GitReport_Async(client, message.MessageThreadId, cancellationToken);
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
                    await Route_OwnerMessage_Async(message, cancellationToken);
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
            catch (OperationCanceledException)
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

    /// <summary>Registers the chat's ☰ command menu — two taps beat typing the check-in ritual.</summary>
    async Task Register_BotCommands_BestEffort_Async(ITelegramApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.Set_MyCommands_Async(
                [
                    ("progress", "Task ledger of this orchestration (all of them in General)"),
                    ("tokens", "Token and usage totals"),
                    ("limits", "5-hour and weekly usage limits"),
                    ("diff", "What the repo and worktrees ACTUALLY contain"),
                    ("imp", "Latest traffic of an implementer (/imp 2)"),
                    ("summary", "What is going on across all orchestrations"),
                    ("pending", "Open questions awaiting me"),
                    ("mute", "Toggle 🔕 THIS topic — drop its messages (I'm in its terminal)"),
                    ("dnd", "Toggle 🌙 THIS topic — hold its messages for later"),
                    ("mute_all", "Toggle 🔕 everywhere"),
                    ("dnd_all", "Toggle 🌙 everywhere"),
                ],
                cancellationToken);
        }
        catch (OperationCanceledException)
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
            original.UpdateId, original.ChatId, original.FromUserId, null, cannedText, null, null);
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

        var blockedPart = progress.Blocked > 0 ? $" · {progress.Blocked} BLOCKED" : "";
        var runningPart = progress.InProgress > 0 ? $" · {progress.InProgress} running" : "";

        return $"{displayName}: {progress.Done}/{progress.Total} done{runningPart}{blockedPart}";
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
    async Task Sync_TopicNames_BestEffort_Async(CancellationToken cancellationToken)
    {
        if (_telegramClient == null)
            return;

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null || session.TelegramTopicId == null)
                continue;

            var baseName = TelegramDeliveryMode_Glyphs.Strip_Glyph(session.DisplayName ?? session.OrchId);
            var wantedName = TelegramDeliveryMode_Glyphs.Decorate_TopicName(baseName, Resolve_EffectiveMode(session.OrchId));

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
            catch (Exception ex)
            {
                _log.Log_Warning(session.OrchId, $"Topic name sync failed: {ex.Message}");
            }
        }
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

    async Task Handle_CallbackTap_Async(ITelegramApiClient client, ITelegramCallbackTap tap, CancellationToken cancellationToken)
    {
        (long? ThreadId, string OptionText, long GroupId) registered;
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

        // Strip the keyboard from the message so the buttons visibly disappear after the choice.
        if (tap.MessageId != null)
        {
            try
            {
                await client.Remove_MessageButtons_Async(tap.MessageId.Value, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Log_Warning(GLOBAL_ORCH_ID, $"Button keyboard removal failed: {ex.Message}");
            }
        }

        // A tap IS an owner message: the chosen option text goes through the normal pipeline
        // (aggregation, translation, delivery receipts) into the topic the buttons live in.
        var syntheticMessage = TelegramOwnerMessage_Factory.Create(
            tap.UpdateId, 0, 0, registered.ThreadId ?? tap.MessageThreadId, registered.OptionText, null, null);

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
            await client.Send_Message_Async(messageThreadId, text, cancellationToken);
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

        _ownerDeliveryBuffer.Add_Segment(channelFile, segmentText, DateTime.UtcNow);
        _log.Log_Info(orchId, "Owner message buffered (aggregation window running)");
    }

    async Task Flush_OwnerDeliveries_Async(CancellationToken cancellationToken)
    {
        if (!_ownerDeliveryBuffer.Has_PendingDeliveries())
            return;

        foreach (var delivery in _ownerDeliveryBuffer.Take_ReadyDeliveries(DateTime.UtcNow))
        {
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
                _pendingOwnerReplies[target.OrchId] = new PendingOwnerReply
                {
                    ThreadId = target.ThreadId,
                    ReceiptMessageId = receiptMessageId,
                    SupervisorEntryCountAtDelivery = supervisorEntryCountBefore,
                    DeliveredUtc = DateTime.UtcNow,
                    Nudged = false,
                };
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

        // No communicator running (or its probe never appeared) — promise nothing it won't do.
        var communicatorAlive = File.Exists(_paths.Get_CommunicatorPidFile(orchId));

        return communicatorAlive
            ? "🔴 Sup: busy mid-task — 🟢 Com will keep you posted until he picks this up"
            : "🔴 Sup: busy mid-task — he'll pick this up when the current turn ends";
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
                Remember_ReceiptMessage(messageThreadId, messageId.Value);
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
    async Task Resolve_PendingOwnerReplies_Async(CancellationToken cancellationToken)
    {
        foreach (var orchId in _pendingOwnerReplies.Keys.ToList())
        {
            var pending = _pendingOwnerReplies[orchId];
            var ownerChannel = orchId == ChannelDiscovery.GENERAL_ORCH_ID
                ? _paths.GeneralChannelFile
                : _paths.Get_OwnerChannelFile(orchId);

            var supervisorEntryCount = Count_SupervisorEntries(ownerChannel);

            // Answered: the supervisor wrote to the owner. The mirrored entry IS the feedback.
            if (supervisorEntryCount > pending.SupervisorEntryCountAtDelivery)
            {
                _pendingOwnerReplies.Remove(orchId);
                continue;
            }

            if (pending.Nudged || (DateTime.UtcNow - pending.DeliveredUtc).TotalSeconds < OWNER_REPLY_GRACE_SECONDS)
                continue;

            // Still mid-turn: it is thinking for real, and the receipt is telling the truth.
            var supervisorUsageFile = orchId == ChannelDiscovery.GENERAL_ORCH_ID
                ? Path.Combine(_paths.GeneralFolder, UsageTotals_Reader.SESSION_USAGE_FILE)
                : Path.Combine(_paths.Get_OrchestrationFolder(orchId), UsageTotals_Reader.SESSION_USAGE_FILE);

            if (Is_SessionMidTurn(supervisorUsageFile))
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

    int Count_SupervisorEntries(string channelFile)
    {
        var count = 0;

        foreach (var entry in ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFile)))
        {
            if (entry.Author == ChannelAuthors.Supervisor)
                count++;
        }

        return count;
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

using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.WindowFocus;
using AIOrchestratorCoreLib.GeneralSupervision.PendingRequests;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Mirroring;
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
    readonly Lock _stateLock = new();
    readonly IOwnerDeliveryBuffer _ownerDeliveryBuffer = OwnerDeliveryBuffer_Factory.Create(OWNER_AGGREGATION_SECONDS);
    readonly Dictionary<string, (string OrchId, long? ThreadId)> _deliveryTargets = [];
    readonly Lock _deliveryLock = new();

    long _lastUpdateId = initialLastUpdateId;
    DateTime _lastLimitCheckUtc = DateTime.MinValue;
    volatile bool _telegramMuted;

    public event Action<string>? OrchestrationActivity;
    public event Action<bool>? MutedChanged;

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

        Persist_BridgeState();
    }

    /// <summary>A session respawning repeatedly without coming alive is INVISIBLE from the phone — escalate it.</summary>
    async Task Send_CrashLoopAlerts_Async(CancellationToken cancellationToken)
    {
        foreach (var alert in _watchdog.Take_PendingCrashLoopAlerts())
        {
            if (_telegramClient == null)
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

    /// <summary>"🔴 Sup: body" → ("🔴 Sup: ", "body") — the prefix must NEVER pass through the translator.</summary>
    static (string Prefix, string Content) Split_SpeakerPrefix(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"^(.{1,12}?: )(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);

        if (!match.Success)
            return (string.Empty, text);

        return (match.Groups[1].Value, match.Groups[2].Value);
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
                    $"model set: {request.Role} → {request.Model}",
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

                Append_OrchestrationAppEntry(
                    request.OrchId,
                    $"implementer '{newMember.MemberId}' added",
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
                    $"implementer '{request.MemberId}' closed",
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
                    $"orchestration '{request.OrchId}' closed",
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
                var dndRequested = false;
                long? dndThreadId = null;

                foreach (var message in batch.OwnerMessages)
                {
                    var command = Get_BotCommand_OrNull(message.Text);

                    if (command == "dnd")
                    {
                        dndRequested = true;
                        dndThreadId = message.MessageThreadId;
                    }
                    else if (command == "summary")
                    {
                        routableMessages.Add(Build_GeneralCommandMessage(message, "Make a summary of what is going on across all orchestrations."));
                    }
                    else if (command == "pending")
                    {
                        routableMessages.Add(Build_GeneralCommandMessage(message, "List every pending question that awaits me, and which topic to answer each in."));
                    }
                    else
                    {
                        routableMessages.Add(message);
                    }
                }

                // The owner texting or tapping ANYTHING (except /dnd) lifts DND — before routing,
                // so the ✓ acks go out.
                if ((routableMessages.Count > 0 || batch.CallbackTaps.Count > 0) && _telegramMuted)
                    Set_TelegramMuted(false);

                foreach (var message in routableMessages)
                {
                    await Route_OwnerMessage_Async(message, cancellationToken);
                    await Send_ReceivedAck_Async(client, message.MessageThreadId, cancellationToken);
                }

                foreach (var tap in batch.CallbackTaps)
                    await Handle_CallbackTap_Async(client, tap, cancellationToken);

                if (dndRequested)
                {
                    Set_TelegramMuted(true);
                    await Send_DirectReply_BestEffort_Async(client, dndThreadId, "🔕 texts muted — text anything to unmute", cancellationToken);
                }

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
                    ("summary", "What is going on across all orchestrations"),
                    ("pending", "Open questions awaiting me"),
                    ("dnd", "Mute texts (text anything to unmute)"),
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

            ChannelAppender.Append_OwnerEntry(delivery.Key, deliveryText, DateTime.Now);
            _log.Log_Info(target.OrchId, "Owner message delivered to the supervisor");
            Raise_OrchestrationActivity(target.OrchId);

            if (_telegramClient == null || _telegramMuted)
                continue;

            try
            {
                // Double tick = aggregation done, message actually handed to the Sup — followed
                // immediately by the "thinking" line so the owner knows the Sup has it now.
                await _telegramClient.Send_Message_Async(target.ThreadId, "✓✓", cancellationToken);
                await _telegramClient.Send_Message_Async(target.ThreadId, "🔴 Sup: thinking…", cancellationToken);
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

    /// <summary>Single tick = "received", sent immediately per message (delivery follows as ✓✓).</summary>
    async Task Send_ReceivedAck_Async(ITelegramApiClient client, long? messageThreadId, CancellationToken cancellationToken)
    {
        try
        {
            await client.Send_Message_Async(messageThreadId, "✓", cancellationToken);
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

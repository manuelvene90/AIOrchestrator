using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
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
    long initialLastUpdateId) : IBridgeEngine
{
    const int MIRROR_TICK_MILLISECONDS = 2000;
    const int INBOUND_LONG_POLL_SECONDS = 20;
    const int INBOUND_ERROR_BACKOFF_START_MILLISECONDS = 5000;
    const int INBOUND_ERROR_BACKOFF_MAX_MILLISECONDS = 60000;
    const int LIMIT_CHECK_INTERVAL_SECONDS = 60;

    const string GLOBAL_ORCH_ID = "";

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestratorConfigProvider _configProvider = configProvider;
    readonly IOrchestrationSessionStore _store = store;
    readonly IOrchestrationLauncher _launcher = launcher;
    readonly IOrchestrationLog _log = log;
    readonly IChannelTailer _tailer = tailer;
    readonly ITelegramApiClient? _telegramClient = telegramClient;
    readonly ISessionWatchdog _watchdog = watchdog;
    readonly Lock _stateLock = new();

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

        // DND: skip tailing entirely — offsets freeze, so unmute delivers everything pending
        // in one catch-up burst (including supervisors' questions that waited for the owner).
        if (_telegramMuted && _telegramClient != null)
            return;

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

                var alertText = $"⚠️ USAGE LIMIT ALERT — '{pair.Key}' is at {pair.Value:F0}% (crossed the {newlyCrossed.Value:F0}% threshold)";
                _log.Log_Warning(GLOBAL_ORCH_ID, alertText);
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

        var threadId = await Resolve_ThreadId_OrNull_Async(append.Channel, cancellationToken);

        foreach (var entry in append.Entries)
        {
            if (!MirrorText_Formatter.Should_Mirror(append.Channel, entry))
                continue;

            var text = MirrorText_Formatter.Format(append.Channel, entry);

            foreach (var chunk in TelegramMessage_Chunker.Chunk(text))
            {
                try
                {
                    await _telegramClient.Send_Message_Async(threadId, chunk, cancellationToken);
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
                    Close_TelegramTopic_FireAndForget(request.OrchId, session.TelegramTopicId.Value);

                Append_GeneralAppEntry(
                    $"orchestration '{request.OrchId}' closed",
                    $"Orchestration '{request.OrchId}' is closed: its supervisor and implementer terminals were closed, its folder stays on disk as audit trail, and its Telegram topic was closed.");
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

    void Close_TelegramTopic_FireAndForget(string orchId, long topicId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var client = _telegramClient
                    ?? throw new Exception($"Telegram client vanished while closing topic {topicId} of '{orchId}'");

                await client.Close_ForumTopic_Async(topicId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.Log_Error(orchId, $"Telegram closeForumTopic({topicId}) failed", ex);
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

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var json = await client.Get_UpdatesJson_Async(_lastUpdateId + 1, INBOUND_LONG_POLL_SECONDS, cancellationToken);
                var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, supergroupChatId, ownerUserId);

                foreach (var message in batch.OwnerMessages)
                    await Route_OwnerMessage_Async(message, cancellationToken);

                // The owner texting ANYTHING lifts DND — the catch-up burst follows on the next tick.
                if (batch.OwnerMessages.Count > 0 && _telegramMuted)
                    Set_TelegramMuted(false);

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

        var entryText = message.PhotoFileId == null
            ? message.Text
            : await Build_PhotoEntryText_Async(message, channelFile, orchId, cancellationToken);

        ChannelAppender.Append_OwnerEntry(channelFile, entryText, DateTime.Now);
        _log.Log_Info(orchId, "Owner message routed to the supervisor's channel");
        Raise_OrchestrationActivity(orchId);
    }

    /// <summary>
    /// Downloads an owner-sent image beside the channel (media/) and references it with an
    /// 'IMAGE: &lt;path&gt;' line — the supervisor Reads the file to inspect the screenshot.
    /// </summary>
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

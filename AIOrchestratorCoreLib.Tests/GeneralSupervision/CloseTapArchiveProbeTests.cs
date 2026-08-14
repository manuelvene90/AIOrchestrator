using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// THE PROPERTY, NOT THE STRUCTURE: a confirmed tap ARCHIVES the parked request, on the path where the
/// close ran and on the path where it threw — and the two records say different things.
///
/// It is written against the behaviour because the structure has already moved once. The archive lived
/// in a `finally`, which was deleted: the catch above swallows without rethrowing and the try body has
/// no return, so fall-through already guaranteed what the `finally` claimed to, and a `finally` that
/// guarantees nothing is pure risk surface — it was the one place a throwing call could replace an
/// exception still in flight. But "the try body has no return" is a fact about the code TODAY. Add an
/// early return later, for a perfectly good reason, and archiving is silently skipped with nothing
/// failing. These cases fail.
///
/// AND THE ENGINE IS REACHABLE, which is the part I had wrong. `BridgeEngineModel` is `internal sealed`
/// with no `InternalsVisibleTo`, so a rule DECIDED inside it cannot be asserted directly — but
/// `BridgeEngine_Factory` is public and takes interfaces only, so its EFFECTS can be observed through
/// the front door. <see cref="CloseImplementerGuardProbeTests"/> established that and its own summary
/// says two members had declared the same wiring unpinnable first.
/// </summary>
public class CloseTapArchiveProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly TappableTelegram_Fake _telegram = new();

    public CloseTapArchiveProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-taparchive-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        Directory.CreateDirectory(Path.GetDirectoryName(_paths.ConfigFile)!);
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, log, _telegram);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A fire-and-forget topic delete may still hold a handle; the temp folder is disposable.
        }
    }

    [Fact]
    public async Task AConfirmedCloseThatRanIsArchivedAsClosed()
    {
        var archived = await Drive_ToTheTap_Async(
            beforeTap: null,
            until: () => Archived_Names().Any(name => name.StartsWith("closed-")));

        Assert.True(archived, $"the resolved folder never received a 'closed-' record — archived: {string.Join(", ", Archived_Names())}");
        Assert.DoesNotContain(Archived_Names(), name => name.StartsWith("uncertain-"));
    }

    /// <summary>
    /// THE PATH THE BRANCH EXISTS FOR. The failure is injected where the executor genuinely can throw
    /// after the store has already been closed — the general-channel append, which sits inside the try
    /// after the close and the kill. Replacing that file with a DIRECTORY makes the write throw for a
    /// reason no production code has to be bent to produce.
    ///
    /// The archive must still happen, and it must NOT say "closed": that record is what a person reads
    /// while reconstructing the incident, and this branch exists because a half-close was being
    /// reported as a clean one.
    /// </summary>
    [Fact]
    public async Task AConfirmedCloseThatThrewIsArchivedAsUncertain()
    {
        var archived = await Drive_ToTheTap_Async(
            beforeTap: Break_TheGeneralChannel,
            until: () => Archived_Names().Any(name => name.StartsWith("uncertain-")));

        Assert.True(archived, $"the resolved folder never received an 'uncertain-' record — archived: {string.Join(", ", Archived_Names())}");
        Assert.DoesNotContain(Archived_Names(), name => name.StartsWith("closed-"));
    }

    /// <summary>
    /// THE ORDERING ITSELF, which I twice declared unpinnable and which is this branch's whole subject.
    ///
    /// The prompt used to be edited to "✅ Closed — you confirmed" BEFORE the close was attempted. That
    /// is invisible to any test of the sentence mapping — each outcome still maps faithfully — and it
    /// is invisible to the archive cases above, which never look at what the owner was shown. But it is
    /// perfectly visible HERE: an edit written before the attempt can only ever claim success, so on a
    /// close that threw, a prompt reading "Closed — you confirmed" proves the edit ran first.
    ///
    /// Move the edit back above the execution and this fails. Nothing else on the branch does.
    /// </summary>
    [Fact]
    public async Task ACloseThatThrewNeverTellsTheOwnerItSucceeded()
    {
        await Drive_ToTheTap_Async(
            beforeTap: Break_TheGeneralChannel,
            until: () => _telegram.EditedTexts.Count > 0);

        var decision = Assert.Single(_telegram.EditedTexts);

        Assert.Contains("did not complete", decision);
        Assert.DoesNotContain("✅", decision);
        Assert.DoesNotContain("Closed — you confirmed", decision);
    }

    [Fact]
    public async Task ACloseThatRanTellsTheOwnerSo()
    {
        await Drive_ToTheTap_Async(
            beforeTap: null,
            until: () => _telegram.EditedTexts.Count > 0);

        Assert.Contains("✅ Closed — you confirmed.", Assert.Single(_telegram.EditedTexts));
    }

    /// <summary>
    /// Ask, tap and outcome all inside ONE engine loop, and that is not tidiness.
    ///
    /// The registered callback data lives in memory. Cancelling the loop between the ask and the tap
    /// let the next loop re-ask and register FRESH data, so the tap carried an id nothing recognised
    /// and was discarded in silence — both cases then failed for a reason that had nothing to do with
    /// archiving. Diagnosed rather than worked around: the first version of this file did exactly that.
    ///
    /// The wait before tapping is on the PROMPT existing rather than on the request file disappearing.
    /// The file also disappears when the request is unreadable, and a wait with two routes to success
    /// pins neither.
    /// </summary>
    async Task<bool> Drive_ToTheTap_Async(Action? beforeTap, Func<bool> until, int maxMilliseconds = 20_000)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        File.WriteAllText(
            Path.Combine(_paths.RequestsFolder, "close-orch.json"),
            $$"""{"action":"close-orchestration","orchId":"{{session.OrchId}}","requester":"supervisor","reason":"work is done"}""");

        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        var deadline = Environment.TickCount64 + maxMilliseconds;
        var tapped = false;
        var held = false;

        while (Environment.TickCount64 < deadline)
        {
            if (!tapped)
            {
                if (_telegram.ConfirmData != null)
                {
                    beforeTap?.Invoke();
                    Queue_ConfirmingTap();
                    tapped = true;
                }
            }
            else if (until())
            {
                held = true;
                break;
            }

            await Task.Delay(50, CancellationToken.None);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way this loop ends.
        }

        Assert.True(tapped, "the owner was never asked to confirm, so no tap could be answered and nothing below means anything");

        return held;
    }

    /// <summary>
    /// Makes the general-channel append throw. A directory where a file is expected fails every write
    /// on every platform, and it fails INSIDE the executor's try rather than around it.
    /// </summary>
    void Break_TheGeneralChannel()
    {
        var file = _paths.GeneralChannelFile;

        if (File.Exists(file))
            File.Delete(file);

        Directory.CreateDirectory(file);
    }

    void Queue_ConfirmingTap()
    {
        _telegram.Queue_Updates(
            "{\"ok\":true,\"result\":[{\"update_id\":2001,\"callback_query\":{\"id\":\"cbq-1\","
            + $"\"data\":\"{_telegram.ConfirmData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":9100,\"message_thread_id\":{TOPIC_ID}}}}}}}]}}");
    }

    /// <summary>
    /// Asks the PRODUCTION accessor where the archive lives rather than rebuilding the path here. The
    /// first version of this helper guessed a folder name that does not exist, so both cases failed
    /// reporting an empty archive — a test looking in the wrong place is indistinguishable from the
    /// behaviour being absent, and it would have "proved" the archive missing after any rename too.
    /// </summary>
    IReadOnlyList<string> Archived_Names()
    {
        var resolved = CloseConfirmation_Parking.Get_ResolvedFolder(_paths);

        if (!Directory.Exists(resolved))
            return [];

        return Directory.GetFiles(resolved).Select(Path.GetFileName).Select(name => name!).ToList();
    }

}

/// <summary>
/// A Telegram client that records the confirm button's callback data — the tap cannot be forged
/// without it, because the engine registers the data it generated — and hands the inbound loop one
/// canned update when asked.
/// </summary>
internal sealed class TappableTelegram_Fake : ITelegramApiClient
{
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    readonly List<string> _editedTexts = [];
    string? _queuedUpdatesJson;
    long _nextMessageId = 9100;

    public string? ConfirmData { get; private set; }

    /// <summary>
    /// What the prompt was REPLACED with. This is the only place the ordering of the fix is visible
    /// from outside: an edit written before the close was attempted can only ever say it succeeded.
    /// </summary>
    public IReadOnlyList<string> EditedTexts
    {
        get
        {
            lock (_lock)
                return _editedTexts.ToList();
        }
    }

    public void Queue_Updates(string updatesJson)
    {
        lock (_lock)
            _queuedUpdatesJson = updatesJson;
    }

    public Task<long?> Send_MessageWithButtons_Async(
        long? messageThreadId,
        string text,
        IReadOnlyList<(string Data, string Label)> buttons,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            // The confirming button, by its label rather than its position: a prompt that reordered
            // its buttons would otherwise silently make this test tap "keep it open" and pass.
            foreach (var button in buttons)
            {
                if (button.Label.Contains("Close", StringComparison.OrdinalIgnoreCase))
                    ConfirmData = button.Data;
            }

            return Task.FromResult<long?>(_nextMessageId++);
        }
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        string? queued;

        lock (_lock)
        {
            queued = _queuedUpdatesJson;
            _queuedUpdatesJson = null;
        }

        if (queued != null)
            return queued;

        // Stands in for the long poll. Without it this loop spins hot for the whole run.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(_nextMessageId++);
    }

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(_nextMessageId++);
    }

    public Task<long> Create_ForumTopic_Async(string topicName, CancellationToken cancellationToken)
    {
        return Task.FromResult(7777L);
    }

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        lock (_lock)
            _editedTexts.Add(text);

        return Task.CompletedTask;
    }

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Photo_Async(long? messageThreadId, string filePath, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
}

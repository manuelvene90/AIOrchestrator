using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Time.Clock;
using AIOrchestratorCoreLib.Tests.Bridge;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// THE ONE ENGINE FIXTURE FOR THE SIBLING TESTS (plan 2026-09-23, Tasks 8, 9, 12, 14 and 15): the
/// constructor every engine test class used to copy, written once so it is reviewed once
/// (code-conventions: "share the wait/fake-Telegram helpers instead of adding a fifth copy").
///
/// <para>
/// WHAT IS REAL: the engine (<see cref="BridgeEngine_Factory.Create_WithDecisionState"/> on
/// <see cref="BridgeTestTiming.Fast"/>), the store, the launcher, the supervision tree, and a REAL git
/// repository with one REAL linked worktree (<see cref="WorktreePath"/>) — the sibling validator asks git
/// for the repo's worktrees, and a hand-made folder would be refused as worktree-not-of-repo. So this
/// fixture needs git on PATH and REFUSES to build without it (<see cref="GitWorktree_Tool"/>, decision 20).
/// </para>
/// <para>
/// WHAT IS FAKE, and why nothing here can start a <c>claude</c>: the spawner is
/// <see cref="RecordingSpawner_Fake"/>, and the config names no print or stream runner — the factory
/// wires the real per-OS <c>claude</c> into the print dispatcher, so a fixture naming one could start a
/// live process. Telegram is <see cref="ScriptedInbound_Fake"/>, which records button rows, taps and the
/// topic of every send; this fixture turns on its <see cref="ScriptedInbound_Fake.Use_DistinctTopicIds"/>,
/// because every claim a sibling test makes is about one topic against another.
/// </para>
/// <para>
/// TOPICS. A solo started here gets a hand-assigned topic from <see cref="FIRST_SOLO_TOPIC_ID"/> up; a
/// topic the ENGINE creates (a sibling's) comes from the fake, from
/// <see cref="ScriptedInbound_Fake.FIRST_DISTINCT_TOPIC_ID"/> up. The two ranges never meet.
/// </para>
/// </summary>
internal sealed class SiblingEngine_Harness : IDisposable
{
    public const long SUPERGROUP_CHAT_ID = -1002233445566;
    public const long OWNER_USER_ID = 555000111;
    public const long FIRST_SOLO_TOPIC_ID = 7373;
    public const string REPO_NAME = "AIOrchestrator";

    readonly string _tempRoot;
    long _nextSoloTopicId = FIRST_SOLO_TOPIC_ID;
    long _nextUpdateId = 9000;
    CancellationTokenSource? _runCancellation;
    Task? _runLoop;

    public ISupervisionPaths Paths { get; }
    public string RepoPath { get; }

    /// <summary>A real linked worktree of <see cref="RepoPath"/>, free for one sibling.</summary>
    public string WorktreePath { get; }

    public IOrchestrationSessionStore Store { get; }
    public IOrchestratorConfigProvider ConfigProvider { get; }
    public RecordingSpawner_Fake Spawner { get; } = new();
    public IOrchestrationLauncher Launcher { get; }
    public RecordingLog_Fake Log { get; } = new();
    public ScriptedInbound_Fake Telegram { get; } = new();
    /// <summary>
    /// THE SYSTEM CLOCK, NOT <see cref="FixedClock_Fake"/>. The engine hands its clock to the tailer,
    /// which releases a channel's LAST entry only once the file has been quiet for the trailing window —
    /// a deadline read. On a clock that never moves the window never elapses, so the last entry of every
    /// channel is held for ever: the inbound-loop tests work around that by appending a second entry
    /// "that only terminates the one above it". A sibling test's last entry is often the one it is
    /// about (the parent's "sibling started" line), so this fixture does not carry that trap.
    /// </summary>
    public IClock Clock { get; } = Clock_Factory.Create_System();
    public IEngineStateStore EngineState { get; } = EngineStateStore_Factory.Create_InMemory();
    public IBridgeEngine Engine { get; }

    public SiblingEngine_Harness()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-sibling-engine-{Guid.NewGuid():N}");

        // Under its own folder, so no orchestration folder of the supervision tree can share a name
        // with the repo or the worktree.
        (RepoPath, WorktreePath) = GitWorktree_Tool.Create_RepoWithWorktree(Path.Combine(_tempRoot, "git"), "limits");

        Paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(Paths.RequestsFolder);

        // Written exactly as TheInboundLoopSurvivesItsOwnBatchTests writes them: Telegram configured
        // (chat, owner, token), no repos, and NO runner key — see the class comment.
        File.WriteAllText(
            Paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(Paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        Store = OrchestrationSessionStore_Factory.Create(Paths);
        ConfigProvider = OrchestratorConfigProvider_Factory.Create(Paths);
        Launcher = OrchestrationLauncher_Factory.Create(Paths, ConfigProvider, Store, Spawner, Log);

        Telegram.Use_DistinctTopicIds();

        Engine = BridgeEngine_Factory.Create_WithDecisionState(
            Paths, ConfigProvider, Store, Launcher, Log, Telegram,
            EngineState, Clock,
            BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        // The loop is stopped BEFORE the folder goes, so a cancelled engine cannot outlive the tree it
        // writes into.
        if (_runCancellation != null && _runLoop != null)
        {
            Stop_Async(_runCancellation, _runLoop).GetAwaiter().GetResult();
            _runCancellation.Dispose();
        }

        TempTree.Delete_BestEffort(_tempRoot);
    }

    /// <summary>
    /// A running SOLO named <paramref name="name"/>, in its own topic, whose owner channel the engine has
    /// ALREADY SEEN — returns its orch id. Starts the engine loop on the first call.
    ///
    /// <para>
    /// WHY IT WAITS. The tailer takes a channel it has never seen at its CURRENT length
    /// (<c>ChannelTailerModel.Poll_OneChannel</c>), so an entry appended before that first sight is
    /// history and is never mirrored — a test would then fail for its own setup's sake. The wait is on
    /// the tailer's own record: the channel's key in the persisted bridge state, which the tick writes
    /// right after the poll that first saw it. No owner message is sent to force a pass, so the solo
    /// starts with an empty channel and no owner debt.
    /// </para>
    /// </summary>
    public async Task<string> Start_Solo_Async(string name)
    {
        var session = Launcher.Start_BasicOrchestration(REPO_NAME, RepoPath);
        Store.Set_DisplayName(session.OrchId, name);
        Store.Set_TelegramTopicId(session.OrchId, _nextSoloTopicId++);

        Start_Loop_IfNotRunning();

        var channel = Paths.Get_OwnerChannelFile(session.OrchId);

        Assert.True(
            await Wait_Until_Async(() => Is_SeenByTheTailer(channel), 20_000),
            $"the engine never saw '{channel}', so anything appended to it now would be absorbed as history.{Environment.NewLine}{Log.Dump()}");

        return session.OrchId;
    }

    /// <summary>The topic <see cref="Start_Solo_Async"/> gave this solo, or the one the engine created for it.</summary>
    public long? TopicOf(string orchId)
    {
        return Store.Get_Session(orchId).TelegramTopicId;
    }

    /// <summary>An entry written as the solo itself would write it, index allocated inside the channel lock.</summary>
    public void Append_Solo(string orchId, string subject, string body)
    {
        Append_AsSolo(Paths.Get_OwnerChannelFile(orchId), subject, body);
    }

    /// <summary>
    /// An entry in the solo's SIBLING OUTBOX, returning the <c>[n]</c> it got — the number a
    /// <c>spawn-sibling</c> request cites as <c>handover</c>. A subject carrying
    /// <see cref="HandoverEntry_Detector.HANDOVER_MARKER"/> makes it a HANDOVER entry.
    /// </summary>
    public int Append_Outbox(string orchId, string subject, string body = "The brief: the job, the files it owns, the files I keep, the base branch.")
    {
        var outbox = Paths.Get_SiblingOutboxFile(orchId);
        Append_AsSolo(outbox, subject, body);

        return ChannelEntry_Parser.Get_NextIndex(File.ReadAllText(outbox)) - 1;
    }

    /// <summary>Drops a request file exactly where a session drops one; returns its path.</summary>
    public string Drop_Request(string json)
    {
        var path = Path.Combine(Paths.RequestsFolder, $"sibling-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// Taps the NEWEST button whose label contains <paramref name="labelFragment"/>, as the owner would:
    /// the tap names the message that carried the button and that message's topic. Waits for the button
    /// to exist first, and fails naming what was sent when it never does.
    /// </summary>
    public async Task Tap_Async(string labelFragment)
    {
        Assert.True(
            await Wait_Until_Async(() => Telegram.Find_ButtonMessage_OrNull(labelFragment) != null, 20_000),
            $"no button '{labelFragment}' was ever sent.{Environment.NewLine}{Telegram.Dump_Sent()}{Environment.NewLine}{Log.Dump()}");

        var button = Telegram.Find_ButtonMessage_OrNull(labelFragment)!.Value;

        Telegram.Queue_Updates(Tap_Json(
            button.Data, button.MessageId, button.ThreadId ?? FIRST_SOLO_TOPIC_ID,
            Interlocked.Increment(ref _nextUpdateId), $"cbq-{Guid.NewGuid():N}"));
    }

    /// <summary>
    /// A callback-query update, as Telegram delivers a tap. COPIED from
    /// <c>TheInboundLoopSurvivesItsOwnBatchTests.Tap_Json</c> — a known duplicate: that class is not the
    /// sibling plan's to edit. The one change is the topic, a parameter here because a sibling test taps
    /// in more than one. The callback id is Telegram's own identity for one gesture, so a test delivering
    /// the SAME tap twice passes the same <paramref name="callbackId"/> twice.
    /// </summary>
    public static string Tap_Json(string callbackData, long questionMessageId, long topicId, long updateId, string callbackId)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"callback_query\":{{\"id\":\"{callbackId}\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{questionMessageId},\"message_thread_id\":{topicId},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}";
    }

    /// <summary>Polls <paramref name="condition"/> every 100 ms for up to <paramref name="maxMilliseconds"/>.</summary>
    public static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }

    /// <summary>The file names under <c>.requests/resolved</c> — each is <c>&lt;label&gt;-&lt;original name&gt;</c>.</summary>
    public IReadOnlyList<string> Archived_Names()
    {
        var resolved = CloseConfirmation_Parking.Get_ResolvedFolder(Paths);

        return Directory.Exists(resolved)
            ? [.. Directory.EnumerateFiles(resolved).Select(path => Path.GetFileName(path))]
            : [];
    }

    /// <summary>The orchestration's owner channel as it is on disk now.</summary>
    public string Channel(string orchId)
    {
        return File.ReadAllText(Paths.Get_OwnerChannelFile(orchId));
    }

    void Start_Loop_IfNotRunning()
    {
        if (_runLoop != null)
            return;

        _runCancellation = new CancellationTokenSource();
        _runLoop = Engine.Run_Async(_runCancellation.Token);
    }

    bool Is_SeenByTheTailer(string channelFilePath)
    {
        var wanted = Path.GetFullPath(channelFilePath);

        return BridgeState_Store.Load_OrEmpty(Paths).FileOffsets.Keys
            .Any(key => string.Equals(Path.GetFullPath(key), wanted, StringComparison.OrdinalIgnoreCase));
    }

    static void Append_AsSolo(string channelFilePath, string subject, string body)
    {
        Assert.True(
            ChannelAppender.Append_SessionEntry(channelFilePath, ChannelAuthors.Solo, subject, body, DateTime.Now),
            $"'{channelFilePath}' stayed locked, so the fixture could not write the entry the test is about.");
    }

    static async Task Stop_Async(CancellationTokenSource cancellation, Task loop)
    {
        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }
    }
}

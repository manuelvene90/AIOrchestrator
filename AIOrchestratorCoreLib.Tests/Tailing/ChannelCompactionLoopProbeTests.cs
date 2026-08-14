using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using System.Text;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Tailing;

/// <summary>
/// THE DISCOVERY LOOP, DRIVEN BY THE REAL ENGINE — the half of compaction that
/// <see cref="AIOrchestratorCoreLib.Tailing.Channel_CompactionStep"/> deliberately does not contain,
/// and that nothing covered until this file. The step's own tests call `Compact_IfAllowed` directly
/// at ten sites; none of them asks whether anything ever CALLS it.
///
/// IT EXISTS BECAUSE THE REASON IT DID NOT WAS FALSE. Two production comments here stated that the
/// engine is "unreachable from a test" and "cannot be constructed from a test". Both are wrong:
/// `BridgeEngine_Factory.Create` is public, and five test files were already driving the engine when
/// those sentences were written. The claim began life true-SHAPED and narrow — nothing covered
/// `Compact_LongChannels` — and was carried outward as a fact about the type, into four briefs and
/// two comments, until it read as an instruction not to try. An honestly-declared gap resting on a
/// false premise is worse than an undeclared one, and this is the repair: the comments are corrected
/// in the same commit that proves them wrong.
///
/// ONE TICK IS ENOUGH, and the ordering is the whole reason it works. Within a single
/// `Execute_MirrorTick_Async` the tailer polls first (so `Was_PolledInLastPoll` answers yes), the
/// mirror pass then returns true in file-only mode — there is no phone to reach, so the entries are
/// as delivered as they will ever be — which confirms the append and clears the undelivered guard,
/// and only then does `Compact_LongChannels` run. Remove any one of those three and the channel is
/// not eligible.
/// </summary>
public class ChannelCompactionLoopProbeTests : IDisposable
{
    const int ENTRIES_ABOVE_THRESHOLD = 95;
    const int ENTRIES_KEPT_BY_COMPACTION = 45;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public ChannelCompactionLoopProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-compactionloop-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create(_paths, configProvider, store, _launcher, log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// The loop finds an over-threshold implementer channel and compacts it. Asserted on BOTH sides
    /// of the move — 45 kept live and 50 in the archive — because a live file that merely shrank
    /// could equally be a truncation, which is the one outcome this whole subsystem exists to
    /// prevent.
    /// </summary>
    [Fact]
    public async Task TheEngineCompactsAnOverThresholdImplementerChannel()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, Find_MemberId(session, MemberKinds.Implementer));

        Write_Entries(channelFile, ENTRIES_ABOVE_THRESHOLD);

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Count_Entries(channelFile) == ENTRIES_KEPT_BY_COMPACTION),
            $"the engine never compacted the channel — it still holds {Count_Entries(channelFile)} entries");

        Assert.Equal(
            ENTRIES_ABOVE_THRESHOLD - ENTRIES_KEPT_BY_COMPACTION,
            Count_Entries(Channel_Compactor.Build_ArchiveFilePath(channelFile)));
    }

    /// <summary>
    /// The log line in the loop — the only part of `Compact_LongChannels` that is not a call to the
    /// step. It is what tells the owner's log that a channel moved, and it was unreachable from the
    /// step's own tests by construction.
    /// </summary>
    [Fact]
    public async Task TheEngineRecordsTheCompactionInTheOrchestrationLog()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, Find_MemberId(session, MemberKinds.Implementer));

        Write_Entries(channelFile, ENTRIES_ABOVE_THRESHOLD);

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Log_Contains(session.OrchId, "Channel compacted")),
            "the compaction was never recorded in the orchestration log");
    }

    /// <summary>
    /// THIS PINS A DEFECT, NOT A DESIRED BEHAVIOUR — F5, reported 2026-08-13 and not fixed.
    /// <see cref="ChannelDiscovery"/> matches the folder prefix `imp-` only, while member ids may be
    /// `rev-` or `solo-`. So a reviewer's channel is never compacted — and, through the same
    /// discovery, never shape-checked and never mirrored to the phone. A reviewer that fills its
    /// channel grows it without bound and speaks to the owner not at all.
    ///
    /// WHEN F5 IS FIXED THIS ASSERTION MUST BE INVERTED, and that is the point of writing it: the
    /// behaviour is currently invisible, so it can be changed by accident. A green test asserting a
    /// defect is only honest if it says it is doing that, which this one does.
    /// </summary>
    [Fact]
    public async Task AReviewerChannelIsNotCompacted_WhichIsTheDefectF5NotTheIntent()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var reviewerChannel = _paths.Get_ImplementerChannelFile(session.OrchId, Find_MemberId(session, MemberKinds.Reviewer));

        Write_Entries(reviewerChannel, ENTRIES_ABOVE_THRESHOLD);

        await Tick_Once_Async();

        Assert.Equal(ENTRIES_ABOVE_THRESHOLD, Count_Entries(reviewerChannel));
        Assert.False(File.Exists(Channel_Compactor.Build_ArchiveFilePath(reviewerChannel)));
    }

    /// <summary>
    /// Resolved through <see cref="MemberKind_Ids.Resolve_Kind"/> rather than by matching the id
    /// prefix here — the prefix rule is the very thing case F5 is about, and a test that re-states it
    /// locally would keep agreeing with itself after the production rule changed.
    /// </summary>
    static string Find_MemberId(IOrchestrationSession session, MemberKinds kind)
    {
        return session.Members.First(member => MemberKind_Ids.Resolve_Kind(member.MemberId) == kind).MemberId;
    }

    static void Write_Entries(string channelFilePath, int count)
    {
        var text = new StringBuilder();

        for (var index = 1; index <= count; index++)
            text.Append($"## [{index}] FROM implementer — 2026-08-13 09:00 — entry {index}\n\nbody {index}\n\n");

        File.WriteAllText(channelFilePath, text.ToString());
    }

    static int Count_Entries(string filePath)
    {
        return File.Exists(filePath)
            ? ChannelEntry_Parser.Parse_All(File.ReadAllText(filePath)).Count
            : 0;
    }

    bool Log_Contains(string orchId, string fragment)
    {
        var logFile = _paths.Get_OrchestrationLogFile(orchId);

        if (!File.Exists(logFile))
            return false;

        using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Contains(fragment);
    }

    async Task Tick_Once_Async()
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way this loop ends.
        }
    }

    static bool Wait_Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
                return true;

            Thread.Sleep(50);
        }

        return false;
    }
}

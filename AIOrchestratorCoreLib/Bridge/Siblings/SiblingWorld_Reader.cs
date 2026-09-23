using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.Git;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// GATHERS THE WORLD a <c>spawn-sibling</c> request is judged against, so
/// <see cref="SiblingRequest_Validator"/> can stay pure. Called at arrival and at the tap — rare events —
/// never on the 2-second tick, because it starts a <c>git</c> process (the plan's "never spawn git on the
/// tick" constraint).
///
/// <para>
/// NOTHING IS KEYED ON THE RAW ID (Task 6 carry, 2026-09-23). The request's orchId is a string a session
/// wrote; it is only ever COMPARED against the store's list. The outbox path and the git call are built
/// from the session the store returned, and only when it returned one — so an unknown or malicious id
/// reads no file, starts no process, and creates no folder. This reader writes nothing at all.
/// </para>
/// </summary>
public static class SiblingWorld_Reader
{
    /// <param name="ownParkedPath">
    /// The parked file of the request being judged, at tap time; null at arrival, when it is not parked
    /// yet. It is excluded from <see cref="SiblingWorld.OtherParkedSiblingRequests"/> — otherwise a parked
    /// request would find itself in the parking folder and refuse itself as "already held".
    /// </param>
    public static SiblingWorld Read(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestratorConfig config,
        ISpawnSiblingRequest request,
        string? ownParkedPath)
    {
        return Read(paths, store, config, request, ownParkedPath, GitSnapshot_Reader.Find_WorktreePaths);
    }

    /// <summary>
    /// The same read with the git call injected — the seam that lets a test COUNT git calls. "An unknown
    /// id starts no process" is otherwise indistinguishable from "git ran and listed nothing": both leave
    /// <see cref="SiblingWorld.RepoWorktreePaths"/> empty (decision 20, a state with two routes to it).
    /// Public because the test project sees only public CoreLib, the in-idiom alternative to
    /// InternalsVisibleTo (the BridgeEngine_Factory precedent).
    /// </summary>
    /// <param name="listWorktrees">Given a repo path, git's worktree list for it; production passes <see cref="GitSnapshot_Reader.Find_WorktreePaths"/>.</param>
    public static SiblingWorld Read(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestratorConfig config,
        ISpawnSiblingRequest request,
        string? ownParkedPath,
        Func<string, IReadOnlyList<string>> listWorktrees)
    {
        var sessions = store.Load_All();
        var requester = sessions.FirstOrDefault(session => string.Equals(session.OrchId, request.OrchId, StringComparison.Ordinal));

        // HISTORY, NOT THE LIVE FILE (decision 13). A request can wait on disk through a usage-limit pause,
        // and a busy outbox compacts past 90 entries, moving the HANDOVER entry into the archive; the live
        // file alone would then refuse a good request as no-handover-entry.
        IReadOnlyList<IChannelEntry> history = requester == null
            ? []
            : ChannelHistory_Counter.Read_Entries(paths.Get_SiblingOutboxFile(requester.OrchId));

        IReadOnlyList<string> worktrees = requester == null
            ? []
            : listWorktrees(requester.RepoPath);

        // A RELATIVE PATH IS NEVER ASKED ABOUT: Directory.Exists would answer for the app's own current
        // directory. The validator refuses it by name before it would read this.
        var worktreeExists = Path.IsPathFullyQualified(request.WorktreePath) && Directory.Exists(request.WorktreePath);

        return new SiblingWorld(
            requester,
            sessions,
            history,
            Read_OtherParked(paths, ownParkedPath),
            worktrees,
            worktreeExists,
            config.Endeavour.MaxOpenSiblings);
    }

    /// <summary>
    /// Every parked spawn-sibling request but the one being judged, through the reader's own strict parse
    /// (<see cref="OrchestrationRequests_Reader.Read_SpawnSiblingRequest_OrNull"/>): a parked close or
    /// promotion reads as null and is skipped, and so does a file that would no longer parse.
    /// </summary>
    static IReadOnlyList<ISpawnSiblingRequest> Read_OtherParked(ISupervisionPaths paths, string? ownParkedPath)
    {
        List<ISpawnSiblingRequest> others = [];

        foreach (var parkedPath in CloseConfirmation_Parking.Find_Parked(paths))
        {
            if (ownParkedPath != null && WorkingPath_Comparer.Are_Same(parkedPath, ownParkedPath))
                continue;

            var parked = OrchestrationRequests_Reader.Read_SpawnSiblingRequest_OrNull(parkedPath);

            if (parked != null)
                others.Add(parked);
        }

        return others;
    }
}

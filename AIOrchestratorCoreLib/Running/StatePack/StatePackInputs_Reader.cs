using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Git;
using AIOrchestratorCoreLib.Git.GitSnapshot;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// Gathers the pack's inputs from what the bridge already holds on disk. EVERY read is guarded and
/// a failure becomes a named line in <see cref="StatePackInputs.Unavailable"/>: a pack must never be
/// the reason a turn does not start, and an absent section must never look like an empty one.
///
/// <para>
/// WHO GETS WHAT. Members (implementer, reviewer, communicator) get their brief, their last own
/// entry and the PLAN.md lines that name them. The supervisor and the solo own the endeavour, so
/// they get the whole ledger and the tail of the owner channel — the owner's messages refer back
/// to earlier ones, and pending entries alone cannot answer them. The general supervisor keeps its
/// own CLAUDE.md as memory (decision 8) and gets only its last entry and the pending traffic.
/// </para>
/// </summary>
public static class StatePackInputs_Reader
{
    public const int OWNER_TAIL_COUNT = 5;
    public const int GIT_COMMITS = 3;

    public static StatePackInputs Read(ISupervisionPaths paths, IPrintSessionState state, string requestId, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources)
    {
        List<string> unavailable = [];
        var ownsTheEndeavour = state.Role == SessionRoles.Supervisor || state.Role == SessionRoles.Solo;
        var isMember = state.Role == SessionRoles.Implementer || state.Role == SessionRoles.Reviewer || state.Role == SessionRoles.Communicator;

        var history = Read_History(state.ChannelFilePath, unavailable);
        var ownAuthor = SessionRole_Names.Get_Author(state.Role);
        var lastOwn = history.LastOrDefault(entry => entry.Author == ownAuthor);
        var brief = isMember ? Brief_Finder.Find_OrNull(history) : null;

        var (planText, ledgerLines) = Read_Plan(paths, state, ownsTheEndeavour, unavailable);
        var gitLines = state.Role == SessionRoles.General ? [] : Read_Git(state.WorkingDirectory, unavailable);
        var ownerTail = ownsTheEndeavour ? Read_OwnerTail(paths, state, unavailable) : [];

        var progressNote = Read_Note_OrNull(
            StatePack_Locator.Get_ProgressFile_OrNull(paths, state.Role, state.OrchId, state.MemberId),
            StatePack_Locator.PROGRESS_FILE_NAME, unavailable);

        var conclusions = Read_Note_OrNull(
            StatePack_Locator.Get_ConclusionsFile_OrNull(paths, state.Role, state.OrchId, state.MemberId),
            StatePack_Locator.CONCLUSIONS_FILE_NAME, unavailable);

        return new StatePackInputs(state.OrchId, state.MemberId, state.Role, requestId, pending, sources, brief, lastOwn, ledgerLines, planText, gitLines, ownerTail, unavailable, progressNote, conclusions);
    }

    /// <summary>
    /// A side note the session keeps for itself — its progress note while it works, or its
    /// conclusions. Null when there is none, which is the ordinary case for a session that has not
    /// started one, and NOT named unavailable: absence here is a state, not a failure. An UNREADABLE
    /// one is named, rather than dropped silently — a session told nothing would re-explore what it
    /// had already saved, or re-propose what it had already ruled out.
    ///
    /// <para>
    /// ONE READER FOR BOTH, called twice with different labels. They differ only in where they live
    /// and how they truncate; a second copy of this method would be the thing decision 12 forbids.
    /// </para>
    /// </summary>
    static string? Read_Note_OrNull(string? file, string label, List<string> unavailable)
    {
        if (file == null || !File.Exists(file))
            return null;

        try
        {
            var text = File.ReadAllText(file).Trim();

            return text.Length == 0 ? null : text;
        }
        catch (Exception ex)
        {
            unavailable.Add($"{label}: {ex.Message}");
            return null;
        }
    }

    static IReadOnlyList<IChannelEntry> Read_History(string channelFilePath, List<string> unavailable)
    {
        try
        {
            return File.Exists(channelFilePath) ? ChannelHistory_Counter.Read_AllEntries(channelFilePath) : [];
        }
        catch (Exception ex)
        {
            unavailable.Add($"channel history ({Path.GetFileName(channelFilePath)}): {ex.Message}");
            return [];
        }
    }

    static (string? PlanText, IReadOnlyList<string> LedgerLines) Read_Plan(ISupervisionPaths paths, IPrintSessionState state, bool ownsTheEndeavour, List<string> unavailable)
    {
        if (state.Role == SessionRoles.General)
            return (null, []);

        var planFile = paths.Get_PlanFile(state.OrchId);

        try
        {
            if (!File.Exists(planFile))
            {
                unavailable.Add("PLAN.md: not found");
                return (null, []);
            }

            var text = UsageTotals_Reader.Read_Text_Safe(planFile);

            if (ownsTheEndeavour)
                return (text, []);

            var mine = text.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.TrimStart().StartsWith("- [", StringComparison.Ordinal) && line.Contains(state.MemberId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return (null, mine);
        }
        catch (Exception ex)
        {
            unavailable.Add($"PLAN.md: {ex.Message}");
            return (null, []);
        }
    }

    static IReadOnlyList<string> Read_Git(string workingDirectory, List<string> unavailable)
    {
        try
        {
            var snapshots = GitSnapshot_Reader.Read_RepoAndWorktrees(workingDirectory, GIT_COMMITS);

            if (snapshots.Count == 0 || !snapshots[0].IsRepository)
            {
                unavailable.Add($"git: {workingDirectory} is not a repository");
                return [];
            }

            // DISTINCT, because on macOS the temp root is `/var/...` while `git worktree list` answers
            // `/private/var/...`, so GitSnapshot_Reader cannot tell the repo from its own worktree entry
            // and describes the same tree twice (observed 2026-09-09 in this stage's tests). Two identical
            // lines carry no information; one line per distinct description does.
            return snapshots.Where(snapshot => snapshot.IsRepository).Select(Describe).Distinct(StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            unavailable.Add($"git: {ex.Message}");
            return [];
        }
    }

    public static string Describe(IGitSnapshot snapshot)
    {
        var commits = snapshot.RecentCommits.Count == 0 ? "no commits" : string.Join(" | ", snapshot.RecentCommits);
        return $"{snapshot.ShortPath} @ {snapshot.Branch}: {snapshot.DirtyFileCount} dirty, ahead {snapshot.AheadOfUpstream} / behind {snapshot.BehindUpstream}; last: {commits}";
    }

    static IReadOnlyList<IChannelEntry> Read_OwnerTail(ISupervisionPaths paths, IPrintSessionState state, List<string> unavailable)
    {
        var ownerChannel = paths.Get_OwnerChannelFile(state.OrchId);

        try
        {
            if (!File.Exists(ownerChannel))
                return [];

            var history = ChannelHistory_Counter.Read_AllEntries(ownerChannel);
            return history.Skip(Math.Max(0, history.Count - OWNER_TAIL_COUNT)).ToList();
        }
        catch (Exception ex)
        {
            unavailable.Add($"owner-channel.md: {ex.Message}");
            return [];
        }
    }
}

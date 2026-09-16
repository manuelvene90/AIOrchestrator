using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// Where a role's pack lives — beside the files that role already reads at boot, so the skill's
/// one sentence ("if `pack.md` exists in your folder, read it first") resolves the same way for
/// everyone: members and the solo in `<orch>/<member>/pack.md`, the supervisor next to its state
/// file as `<orch>/.supervisor.pack.md`, the general in its own folder.
/// </summary>
public static class StatePack_Locator
{
    public const string FILE_NAME = "pack.md";
    public const string SUPERVISOR_FILE_NAME = ".supervisor.pack.md";

    /// <summary>
    /// THE PROGRESS NOTE a member keeps WHILE it works, beside its pack — one line per verified step,
    /// what is done and what is next. A print turn cannot append to its channel mid-turn (its final
    /// message is its only entry), so when the silence brake, the loop detector or the ceiling cuts
    /// it, this file and its commits are the only record of how far it got. Anthropic's long-running
    /// harness keeps exactly this pair — a progress file and git commits — so a fresh session "reads
    /// the git logs and progress files to get up to speed" instead of re-exploring (research
    /// 2026-09-11). Written by the member, read by the bridge into the next pack.
    /// </summary>
    public const string PROGRESS_FILE_NAME = "progress.md";

    /// <summary>The member's progress note, or null for a role that has no member folder (supervisor, general).</summary>
    public static string? Get_ProgressFile_OrNull(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role is SessionRoles.General or SessionRoles.Supervisor
            ? null
            : Path.Combine(paths.Get_OrchestrationFolder(orchId), memberId, PROGRESS_FILE_NAME);
    }

    /// <summary>Where a finished task's note goes when a new brief starts a new one — appended, never lost.</summary>
    public const string PROGRESS_ARCHIVE_FILE_NAME = "progress.archive.md";

    /// <summary>
    /// A NEW BRIEF STARTS A NEW NOTE. The note is append-only for the member, so without this it would
    /// grow across every task it ever did, and the pack would tell a member starting brief B to
    /// "resume" brief A's next steps (review finding, 2026-09-11). Called when the entries a turn is
    /// handed include a new task (<see cref="Brief_Finder.Is_NewTask"/>): the old note is appended to
    /// <see cref="PROGRESS_ARCHIVE_FILE_NAME"/> and removed. Best effort — a note that cannot be moved
    /// is left where it is, which is the old behaviour, not a lost one.
    /// </summary>
    public static void Archive_ProgressNote_IfNewTask(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId, IReadOnlyList<Channels.ChannelEntry.IChannelEntry> handedEntries)
    {
        var note = Get_ProgressFile_OrNull(paths, role, orchId, memberId);

        if (note == null || !File.Exists(note) || !handedEntries.Any(Brief_Finder.Is_NewTask))
            return;

        try
        {
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(note)!, PROGRESS_ARCHIVE_FILE_NAME), File.ReadAllText(note).TrimEnd() + "\n\n");
            File.Delete(note);
        }
        catch
        {
            // Left in place: the member reads a stale note, as it did before this existed.
        }
    }

    /// <summary>The name the C1.2 block is stored under for a member — 2026-09-08 token-efficiency spec, §C1.2.</summary>
    public const string CONCLUSIONS_FILE_NAME = "state.md";

    /// <summary>
    /// The supervisor's, beside its pack in the orchestration folder, because the supervisor has no
    /// member folder to put one in.
    /// </summary>
    public const string SUPERVISOR_CONCLUSIONS_FILE_NAME = ".supervisor.state.md";

    /// <summary>
    /// WHERE A SESSION'S CONCLUSIONS LIVE — the one part of a turn a fresh session cannot re-derive.
    /// The brief is a channel entry, the ledger is PLAN.md, the code state is git: throw the
    /// transcript away and every one of them is still on disk and still true. *"We already tried this
    /// and it failed"* is on disk nowhere, and re-proposing a dead end costs a day and reports
    /// nothing — which is worse than a crash, because a crash is reported.
    ///
    /// <para>
    /// ONE ACCESSOR FOR BOTH HALVES. Members and the solo get <see cref="CONCLUSIONS_FILE_NAME"/> at
    /// the path C1.2 names, so when that plan ships the two meet at one path and one reader instead
    /// of growing a second convention for the same thing (CLAUDE.md decision 12).
    /// </para>
    /// <para>
    /// Null for the general supervisor, which is stateless across launches by owner directive
    /// (decision 8) and keeps its memory in its own CLAUDE.md.
    /// </para>
    /// </summary>
    public static string? Get_ConclusionsFile_OrNull(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.General => null,
            SessionRoles.Supervisor => Path.Combine(paths.Get_OrchestrationFolder(orchId), SUPERVISOR_CONCLUSIONS_FILE_NAME),
            _ => Path.Combine(paths.Get_OrchestrationFolder(orchId), memberId, CONCLUSIONS_FILE_NAME),
        };
    }

    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.General => Path.Combine(paths.GeneralFolder, FILE_NAME),
            SessionRoles.Supervisor => Path.Combine(paths.Get_OrchestrationFolder(orchId), SUPERVISOR_FILE_NAME),
            _ => Path.Combine(paths.Get_OrchestrationFolder(orchId), memberId, FILE_NAME),
        };
    }
}

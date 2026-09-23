using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// WHERE A SESSION RUNS — the one definition, read by the launcher (every spawn and respawn), the
/// sibling validator (which trees are taken) and the endeavour digest (which branch a sibling is on).
///
/// <para>
/// ONE DEFINITION BECAUSE RESUME DEPENDS ON IT (spec 2026-09-23 §7.1). Claude Code keeps its
/// conversations per working directory, and <c>ResumableSession_Resolver</c> only checks that the
/// transcript file still exists. So a sibling spawned in its worktree and respawned in the repo root
/// would not fail: it would start a fresh conversation, silently, with the log still saying it was
/// respawned. Two readers spelling "working path" two ways (decision 12) is exactly how one of them
/// would come to say the repo root.
/// </para>
/// <para>
/// NULL MEANS THE REPO, which is every session written before 2026-09-23 and every session that is
/// not a sibling: no existing orchestration moves.
/// </para>
/// </summary>
public static class WorkingPath_Resolver
{
    public static string Resolve(IOrchestrationSession session)
    {
        return session.WorkingPath ?? session.RepoPath;
    }
}

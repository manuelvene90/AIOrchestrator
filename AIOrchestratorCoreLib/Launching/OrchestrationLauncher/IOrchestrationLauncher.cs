using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Launching.OrchestrationLauncher;

/// <summary>
/// Starts supervision sessions: creates the orchestration on disk and spawns the terminal.
/// Shared by the app's UI buttons, the request-file protocol and the watchdog.
/// Orchestration ids are ALWAYS allocated automatically (repo-slug-n) — nobody types them.
/// </summary>
public interface IOrchestrationLauncher
{
    IOrchestrationSession Start_Orchestration(string repoName, string repoPath);

    /// <summary>ONE session talking straight to the owner — no supervisor, reviewer or gates.</summary>
    IOrchestrationSession Start_BasicOrchestration(string repoName, string repoPath);
    IOrchestrationSession Add_Implementer(string orchId);

    /// <summary>Adds a member of the given kind — a reviewer spawns read-only, with no worktree.</summary>
    IOrchestrationSession Add_Member(string orchId, MemberKinds kind);
    void Respawn_Supervisor(string orchId);
    void Respawn_Communicator(string orchId);
    void Respawn_Implementer(string orchId, string memberId);

    /// <summary>Spawns (or re-spawns) the general supervisor; resumes its previous conversation when one exists.</summary>
    void Spawn_GeneralSupervisor();
}

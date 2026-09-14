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

    /// <summary>A basic orchestration becomes a full crew: the solo ends, a supervisor takes over its channel, imp-1 spawns empty.</summary>
    IOrchestrationSession Promote_ToFullCrew(string orchId);

    /// <summary>A full crew becomes one session: the supervisor and every member end, a solo takes over the channel.</summary>
    IOrchestrationSession Demote_ToBasic(string orchId);

    /// <summary>Adds a member of the given kind — a reviewer spawns read-only, with no worktree.</summary>
    IOrchestrationSession Add_Member(string orchId, MemberKinds kind);

    /// <summary>
    /// Same, spawned on the model the requester chose for this task. The owner's per-orchestration
    /// override (set-model) still wins over it; the config default is the floor beneath both.
    /// </summary>
    IOrchestrationSession Add_Member(string orchId, MemberKinds kind, string? model);

    /// <summary>
    /// True when a session was actually started. False when none was — the kit gate refused, or the
    /// member was closed while the caller's snapshot was in flight — and the launcher has already
    /// logged why. The watchdog counts only true towards a crash loop: a refusal starts no process,
    /// so there is nothing that failed to come alive (2026-09-14, three false CRASH-LOOPING alerts).
    /// </summary>
    bool Respawn_Supervisor(string orchId);
    void Respawn_Communicator(string orchId);

    /// <summary>Same contract as <see cref="Respawn_Supervisor"/>: true only when a session was started.</summary>
    bool Respawn_Implementer(string orchId, string memberId);

    /// <summary>Spawns (or re-spawns) the general supervisor; true only when a session was started.</summary>
    bool Spawn_GeneralSupervisor();

}

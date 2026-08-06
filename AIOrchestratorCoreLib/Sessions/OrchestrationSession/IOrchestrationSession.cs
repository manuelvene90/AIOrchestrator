using AIOrchestratorCoreLib.Sessions.OrchestrationMember;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSession;

/// <summary>The persisted state of one orchestration (session.json).</summary>
public interface IOrchestrationSession
{
    string OrchId { get; }
    string RepoName { get; }
    string RepoPath { get; }
    DateTime CreatedUtc { get; }
    long? TelegramTopicId { get; }

    /// <summary>
    /// The TRUE session-host shell pid, synced from the pid file after spawn — null while a spawn
    /// is in flight. Informational only: liveness is the watchdog's job (pid files), and agents
    /// must never infer death from this field.
    /// </summary>
    int? SupervisorPid { get; }

    /// <summary>Stamped on every supervisor spawn — the watchdog's grace window against double-spawn races.</summary>
    DateTime? SupervisorSpawnedUtc { get; }

    /// <summary>Short human goal name (2-4 words) set by the supervisor once the goal is known; also the Telegram topic name.</summary>
    string? DisplayName { get; }

    /// <summary>Per-orchestration model overrides (owner: "use fable for this") — null = the config default.</summary>
    string? SupervisorModelOverride { get; }
    string? ImplementerModelOverride { get; }

    IReadOnlyList<IOrchestrationMember> Members { get; }

    /// <summary>Set when the general supervisor closed this orchestration. Folder stays as audit trail.</summary>
    DateTime? ClosedUtc { get; }
}

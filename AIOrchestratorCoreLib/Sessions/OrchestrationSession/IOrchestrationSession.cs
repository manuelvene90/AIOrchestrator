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
    int? SupervisorPid { get; }

    /// <summary>Stamped on every supervisor spawn — the watchdog's grace window against double-spawn races.</summary>
    DateTime? SupervisorSpawnedUtc { get; }

    /// <summary>Short human goal name (2-4 words) set by the supervisor once the goal is known; also the Telegram topic name.</summary>
    string? DisplayName { get; }

    IReadOnlyList<IOrchestrationMember> Members { get; }

    /// <summary>Set when the general supervisor closed this orchestration. Folder stays as audit trail.</summary>
    DateTime? ClosedUtc { get; }
}

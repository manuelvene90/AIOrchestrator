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
    IReadOnlyList<IOrchestrationMember> Members { get; }

    /// <summary>Set when the general supervisor closed this orchestration. Folder stays as audit trail.</summary>
    DateTime? ClosedUtc { get; }
}

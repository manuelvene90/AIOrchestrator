using AIOrchestratorCoreLib.Sessions.OrchestrationMember;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSession;

internal sealed class OrchestrationSessionModel(
    string orchId,
    string repoName,
    string repoPath,
    DateTime createdUtc,
    long? telegramTopicId,
    int? supervisorPid,
    DateTime? supervisorSpawnedUtc,
    string? displayName,
    IReadOnlyList<IOrchestrationMember> members,
    DateTime? closedUtc) : IOrchestrationSession
{
    public string OrchId { get; } = orchId;
    public string RepoName { get; } = repoName;
    public string RepoPath { get; } = repoPath;
    public DateTime CreatedUtc { get; } = createdUtc;
    public long? TelegramTopicId { get; } = telegramTopicId;
    public int? SupervisorPid { get; } = supervisorPid;
    public DateTime? SupervisorSpawnedUtc { get; } = supervisorSpawnedUtc;
    public string? DisplayName { get; } = displayName;
    public IReadOnlyList<IOrchestrationMember> Members { get; } = members;
    public DateTime? ClosedUtc { get; } = closedUtc;
}

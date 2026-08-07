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
    DateTime? communicatorSpawnedUtc,
    string? displayName,
    string? supervisorModelOverride,
    string? implementerModelOverride,
    IReadOnlyList<IOrchestrationMember> members,
    Telegram.TelegramDeliveryModes telegramMode,
    DateTime? closedUtc) : IOrchestrationSession
{
    public string OrchId { get; } = orchId;
    public string RepoName { get; } = repoName;
    public string RepoPath { get; } = repoPath;
    public DateTime CreatedUtc { get; } = createdUtc;
    public long? TelegramTopicId { get; } = telegramTopicId;
    public int? SupervisorPid { get; } = supervisorPid;
    public DateTime? SupervisorSpawnedUtc { get; } = supervisorSpawnedUtc;
    public DateTime? CommunicatorSpawnedUtc { get; } = communicatorSpawnedUtc;
    public string? DisplayName { get; } = displayName;
    public string? SupervisorModelOverride { get; } = supervisorModelOverride;
    public string? ImplementerModelOverride { get; } = implementerModelOverride;
    public IReadOnlyList<IOrchestrationMember> Members { get; } = members;
    public Telegram.TelegramDeliveryModes TelegramMode { get; } = telegramMode;
    public DateTime? ClosedUtc { get; } = closedUtc;
}

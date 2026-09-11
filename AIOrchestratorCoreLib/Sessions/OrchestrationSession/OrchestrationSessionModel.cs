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
    Telegram.OwnerPresenceModes ownerPresence,
    DateTime? closedUtc,
    long? statusLineMessageId,
    bool awaitingTest,
    bool done,
    DateTime? telegramTopicDeletePendingUtc,
    DateTime? telegramTopicDeletedUtc,
    bool telegramTopicDeleteFailureReported) : IOrchestrationSession
{
    public string OrchId { get; } = orchId;
    public string RepoName { get; } = repoName;
    public string RepoPath { get; } = repoPath;
    public DateTime CreatedUtc { get; } = createdUtc;
    public long? TelegramTopicId { get; } = telegramTopicId;
    public DateTime? TelegramTopicDeletePendingUtc { get; } = telegramTopicDeletePendingUtc;
    public DateTime? TelegramTopicDeletedUtc { get; } = telegramTopicDeletedUtc;
    public bool TelegramTopicDeleteFailureReported { get; } = telegramTopicDeleteFailureReported;

    public long? StatusLineMessageId { get; } = statusLineMessageId;
    public int? SupervisorPid { get; } = supervisorPid;
    public DateTime? SupervisorSpawnedUtc { get; } = supervisorSpawnedUtc;
    public DateTime? CommunicatorSpawnedUtc { get; } = communicatorSpawnedUtc;
    public string? DisplayName { get; } = displayName;
    public string? SupervisorModelOverride { get; } = supervisorModelOverride;
    public string? ImplementerModelOverride { get; } = implementerModelOverride;
    public IReadOnlyList<IOrchestrationMember> Members { get; } = members;
    public Telegram.TelegramDeliveryModes TelegramMode { get; } = telegramMode;
    public bool AwaitingTest { get; } = awaitingTest;
    public bool Done { get; } = done;

    public Telegram.OwnerPresenceModes OwnerPresence { get; } = ownerPresence;
    public DateTime? ClosedUtc { get; } = closedUtc;
}

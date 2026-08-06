using AIOrchestratorCoreLib.Sessions.OrchestrationMember;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSession;

public static class OrchestrationSession_Factory
{
    public static IOrchestrationSession Create(
        string orchId,
        string repoName,
        string repoPath,
        DateTime createdUtc,
        long? telegramTopicId,
        int? supervisorPid,
        IReadOnlyList<IOrchestrationMember> members)
    {
        return Create(orchId, repoName, repoPath, createdUtc, telegramTopicId, supervisorPid, null, null, null, null, null, members, null);
    }

    public static IOrchestrationSession Create(
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
        DateTime? closedUtc)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"OrchId must be non-empty (repo '{repoName}' at '{repoPath}')");

        return new OrchestrationSessionModel(
            orchId, repoName, repoPath, createdUtc, telegramTopicId, supervisorPid, supervisorSpawnedUtc,
            communicatorSpawnedUtc, displayName, supervisorModelOverride, implementerModelOverride, members, closedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithTopicId(IOrchestrationSession existing, long topicId)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            topicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.CommunicatorSpawnedUtc,
            existing.DisplayName, existing.SupervisorModelOverride, existing.ImplementerModelOverride,
            existing.Members, existing.ClosedUtc);
    }

    /// <summary>Also stamps SupervisorSpawnedUtc — the pid change IS a spawn (watchdog grace source).</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithSupervisorPid(IOrchestrationSession existing, int? pid)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, pid, DateTime.UtcNow, existing.CommunicatorSpawnedUtc,
            existing.DisplayName, existing.SupervisorModelOverride, existing.ImplementerModelOverride,
            existing.Members, existing.ClosedUtc);
    }

    /// <summary>Stamps the communicator spawn grace — its pid lives only in its pid file (the liveness source).</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithCommunicatorSpawnedNow(IOrchestrationSession existing)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, DateTime.UtcNow,
            existing.DisplayName, existing.SupervisorModelOverride, existing.ImplementerModelOverride,
            existing.Members, existing.ClosedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithDisplayName(IOrchestrationSession existing, string displayName)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.CommunicatorSpawnedUtc,
            displayName, existing.SupervisorModelOverride, existing.ImplementerModelOverride,
            existing.Members, existing.ClosedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithSupervisorModelOverride(IOrchestrationSession existing, string? model)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.CommunicatorSpawnedUtc,
            existing.DisplayName, model, existing.ImplementerModelOverride,
            existing.Members, existing.ClosedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithImplementerModelOverride(IOrchestrationSession existing, string? model)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.CommunicatorSpawnedUtc,
            existing.DisplayName, existing.SupervisorModelOverride, model,
            existing.Members, existing.ClosedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithMembers(
        IOrchestrationSession existing,
        IReadOnlyList<IOrchestrationMember> members)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.CommunicatorSpawnedUtc,
            existing.DisplayName, existing.SupervisorModelOverride, existing.ImplementerModelOverride,
            members, existing.ClosedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_Closed(IOrchestrationSession existing, DateTime closedUtc)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.CommunicatorSpawnedUtc,
            existing.DisplayName, existing.SupervisorModelOverride, existing.ImplementerModelOverride,
            existing.Members, closedUtc);
    }
}

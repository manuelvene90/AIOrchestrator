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
        return Create(orchId, repoName, repoPath, createdUtc, telegramTopicId, supervisorPid, null, null, members, null);
    }

    public static IOrchestrationSession Create(
        string orchId,
        string repoName,
        string repoPath,
        DateTime createdUtc,
        long? telegramTopicId,
        int? supervisorPid,
        DateTime? supervisorSpawnedUtc,
        string? displayName,
        IReadOnlyList<IOrchestrationMember> members,
        DateTime? closedUtc)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"OrchId must be non-empty (repo '{repoName}' at '{repoPath}')");

        return new OrchestrationSessionModel(
            orchId, repoName, repoPath, createdUtc, telegramTopicId, supervisorPid, supervisorSpawnedUtc, displayName, members, closedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithTopicId(IOrchestrationSession existing, long topicId)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            topicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.DisplayName, existing.Members, existing.ClosedUtc);
    }

    /// <summary>Also stamps SupervisorSpawnedUtc — the pid change IS a spawn (watchdog grace source).</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithSupervisorPid(IOrchestrationSession existing, int? pid)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, pid, DateTime.UtcNow, existing.DisplayName, existing.Members, existing.ClosedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithDisplayName(IOrchestrationSession existing, string displayName)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, displayName, existing.Members, existing.ClosedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithMembers(
        IOrchestrationSession existing,
        IReadOnlyList<IOrchestrationMember> members)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.DisplayName, members, existing.ClosedUtc);
    }

    public static IOrchestrationSession CreateFrom_Existing_Closed(IOrchestrationSession existing, DateTime closedUtc)
    {
        return Create(
            existing.OrchId, existing.RepoName, existing.RepoPath, existing.CreatedUtc,
            existing.TelegramTopicId, existing.SupervisorPid, existing.SupervisorSpawnedUtc, existing.DisplayName, existing.Members, closedUtc);
    }
}

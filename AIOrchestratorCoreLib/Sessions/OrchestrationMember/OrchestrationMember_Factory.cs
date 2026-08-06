namespace AIOrchestratorCoreLib.Sessions.OrchestrationMember;

public static class OrchestrationMember_Factory
{
    public static IOrchestrationMember Create(string memberId, int? pid, DateTime? spawnedUtc)
    {
        return Create(memberId, pid, spawnedUtc, null);
    }

    public static IOrchestrationMember Create(string memberId, int? pid, DateTime? spawnedUtc, DateTime? closedUtc)
    {
        if (string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException($"MemberId must be non-empty (pid was {pid})");

        return new OrchestrationMemberModel(memberId, pid, spawnedUtc, closedUtc);
    }

    public static IOrchestrationMember CreateFrom_Existing_Closed(IOrchestrationMember existing, DateTime closedUtc)
    {
        return Create(existing.MemberId, existing.Pid, existing.SpawnedUtc, closedUtc);
    }
}

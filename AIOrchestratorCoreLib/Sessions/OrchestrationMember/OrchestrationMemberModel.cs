namespace AIOrchestratorCoreLib.Sessions.OrchestrationMember;

internal sealed class OrchestrationMemberModel(
    string memberId,
    int? pid,
    DateTime? spawnedUtc,
    DateTime? closedUtc) : IOrchestrationMember
{
    public string MemberId { get; } = memberId;
    public int? Pid { get; } = pid;
    public DateTime? SpawnedUtc { get; } = spawnedUtc;
    public DateTime? ClosedUtc { get; } = closedUtc;
}

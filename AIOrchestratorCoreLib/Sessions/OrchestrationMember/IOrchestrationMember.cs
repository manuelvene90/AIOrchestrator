namespace AIOrchestratorCoreLib.Sessions.OrchestrationMember;

/// <summary>One implementer in an orchestration. MemberId is 'imp-n'.</summary>
public interface IOrchestrationMember
{
    string MemberId { get; }
    int? Pid { get; }
    DateTime? SpawnedUtc { get; }

    /// <summary>Set when the supervisor retired this member. The folder stays on disk as audit trail.</summary>
    DateTime? ClosedUtc { get; }
}

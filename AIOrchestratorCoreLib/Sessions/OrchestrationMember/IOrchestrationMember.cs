namespace AIOrchestratorCoreLib.Sessions.OrchestrationMember;

/// <summary>One implementer in an orchestration. MemberId is 'imp-n'.</summary>
public interface IOrchestrationMember
{
    string MemberId { get; }

    /// <summary>
    /// The TRUE session-host shell pid, synced from the pid file after spawn — null while a spawn
    /// is in flight. Informational only: liveness is the watchdog's job (pid files), and agents
    /// must never infer death from this field.
    /// </summary>
    int? Pid { get; }
    DateTime? SpawnedUtc { get; }

    /// <summary>Set when the supervisor retired this member. The folder stays on disk as audit trail.</summary>
    DateTime? ClosedUtc { get; }
}

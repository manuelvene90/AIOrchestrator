namespace AIOrchestratorCoreLib.GeneralSupervision.PromoteOrchestrationRequest;

/// <summary>
/// A request dropped by a SOLO session asking for its basic orchestration to become a full crew: the
/// solo ends, a supervisor spawns onto the same `owner-channel.md`, and `imp-1` spawns empty.
///
/// It is the one request a session makes about its OWN existence, and the owner confirms it with a
/// tap. Every other spend increase in this system is either owner-tapped or carries a reason to their
/// phone; this is the largest of them — a supervisor and an implementer, indefinitely, replacing one
/// session — and it is effectively one-way, so it gets both.
/// </summary>
public interface IPromoteOrchestrationRequest
{
    string OrchId { get; }

    /// <summary>
    /// WHY the work outgrew one session, in one short line. MANDATORY and relayed to the owner with
    /// the confirmation they are asked for — they are being asked to spend, so they are told what on.
    /// </summary>
    string Reason { get; }

    string SourceFilePath { get; }
}

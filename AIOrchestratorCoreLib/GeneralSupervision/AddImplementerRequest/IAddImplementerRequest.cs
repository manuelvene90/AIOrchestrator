using AIOrchestratorCoreLib.Sessions;

namespace AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;

/// <summary>
/// A request dropped by an orchestration SUPERVISOR (as a .json file under .requests/) asking the
/// app to spawn a new member for its orchestration — an implementer ("add-implementer") or a
/// reviewer ("add-reviewer").
/// </summary>
public interface IAddImplementerRequest
{
    string OrchId { get; }

    /// <summary>Implementer or reviewer — decided by the action string, carried through to the spawn.</summary>
    MemberKinds Kind { get; }

    /// <summary>
    /// WHY this member is being spawned, in one short line. MANDATORY: every autonomous action
    /// costs the owner tokens, so it is relayed to them with its reason — never silently.
    /// </summary>
    string Reason { get; }

    string SourceFilePath { get; }
}

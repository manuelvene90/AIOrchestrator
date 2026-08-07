namespace AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;

/// <summary>
/// A request dropped by an orchestration SUPERVISOR (as a .json file under .requests/) asking the
/// app to spawn a new implementer for its orchestration.
/// </summary>
public interface IAddImplementerRequest
{
    string OrchId { get; }

    /// <summary>
    /// WHY this implementer is being spawned, in one short line. MANDATORY: every autonomous
    /// action costs the owner tokens, so it is relayed to them with its reason — never silently.
    /// </summary>
    string Reason { get; }

    string SourceFilePath { get; }
}

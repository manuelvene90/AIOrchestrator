namespace AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;

/// <summary>
/// A request dropped by an orchestration SUPERVISOR (as a .json file under .requests/) asking the
/// app to spawn a new implementer for its orchestration.
/// </summary>
public interface IAddImplementerRequest
{
    string OrchId { get; }
    string SourceFilePath { get; }
}

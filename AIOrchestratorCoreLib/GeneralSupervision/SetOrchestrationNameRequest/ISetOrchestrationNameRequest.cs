namespace AIOrchestratorCoreLib.GeneralSupervision.SetOrchestrationNameRequest;

/// <summary>
/// A supervisor's request to set the orchestration's short goal name (2-4 words) — shown on the
/// app card and as the Telegram topic name.
/// </summary>
public interface ISetOrchestrationNameRequest
{
    string OrchId { get; }
    string Name { get; }
    string SourceFilePath { get; }
}

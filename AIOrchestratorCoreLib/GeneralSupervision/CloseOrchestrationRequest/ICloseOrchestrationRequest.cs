namespace AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;

/// <summary>The general supervisor's request to close a whole orchestration session.</summary>
public interface ICloseOrchestrationRequest
{
    string OrchId { get; }
    string SourceFilePath { get; }
}

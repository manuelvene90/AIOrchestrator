namespace AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;

/// <summary>The general supervisor's request to close a whole orchestration session.</summary>
public interface ICloseOrchestrationRequest
{
    string OrchId { get; }

    /// <summary>WHY it is being closed, in one short line — relayed to the owner, never silent.</summary>
    string Reason { get; }

    string SourceFilePath { get; }
}

namespace AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;

/// <summary>A supervisor's request to retire one implementer of its orchestration.</summary>
public interface ICloseImplementerRequest
{
    string OrchId { get; }
    string MemberId { get; }
    string SourceFilePath { get; }
}

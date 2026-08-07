namespace AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;

/// <summary>A supervisor's request to retire one implementer of its orchestration.</summary>
public interface ICloseImplementerRequest
{
    string OrchId { get; }
    string MemberId { get; }

    /// <summary>WHY it is being retired, in one short line — relayed to the owner, never silent.</summary>
    string Reason { get; }

    string SourceFilePath { get; }
}

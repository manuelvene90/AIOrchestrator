namespace AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;

internal sealed class CloseImplementerRequestModel(
    string orchId,
    string memberId,
    string reason,
    string sourceFilePath) : ICloseImplementerRequest
{
    public string OrchId { get; } = orchId;
    public string MemberId { get; } = memberId;
    public string Reason { get; } = reason;
    public string SourceFilePath { get; } = sourceFilePath;
}

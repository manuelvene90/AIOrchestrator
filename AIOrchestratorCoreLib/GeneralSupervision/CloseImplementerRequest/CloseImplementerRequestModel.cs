namespace AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;

internal sealed class CloseImplementerRequestModel(
    string orchId,
    string memberId,
    string sourceFilePath) : ICloseImplementerRequest
{
    public string OrchId { get; } = orchId;
    public string MemberId { get; } = memberId;
    public string SourceFilePath { get; } = sourceFilePath;
}

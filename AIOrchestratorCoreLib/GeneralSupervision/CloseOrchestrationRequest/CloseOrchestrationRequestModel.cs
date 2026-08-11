namespace AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;

internal sealed class CloseOrchestrationRequestModel(
    string orchId,
    string reason,
    string requester,
    string sourceFilePath) : ICloseOrchestrationRequest
{
    public string OrchId { get; } = orchId;
    public string Reason { get; } = reason;
    public string Requester { get; } = requester;
    public string SourceFilePath { get; } = sourceFilePath;
}

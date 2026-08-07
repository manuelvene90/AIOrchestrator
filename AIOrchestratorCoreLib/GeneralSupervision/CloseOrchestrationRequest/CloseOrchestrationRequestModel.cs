namespace AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;

internal sealed class CloseOrchestrationRequestModel(
    string orchId,
    string reason,
    string sourceFilePath) : ICloseOrchestrationRequest
{
    public string OrchId { get; } = orchId;
    public string Reason { get; } = reason;
    public string SourceFilePath { get; } = sourceFilePath;
}

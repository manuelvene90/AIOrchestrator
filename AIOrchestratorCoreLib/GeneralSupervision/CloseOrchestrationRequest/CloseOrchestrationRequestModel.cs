namespace AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;

internal sealed class CloseOrchestrationRequestModel(
    string orchId,
    string sourceFilePath) : ICloseOrchestrationRequest
{
    public string OrchId { get; } = orchId;
    public string SourceFilePath { get; } = sourceFilePath;
}

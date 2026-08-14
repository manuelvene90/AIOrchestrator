namespace AIOrchestratorCoreLib.GeneralSupervision.PromoteOrchestrationRequest;

internal sealed class PromoteOrchestrationRequestModel(
    string orchId,
    string reason,
    string sourceFilePath) : IPromoteOrchestrationRequest
{
    public string OrchId { get; } = orchId;
    public string Reason { get; } = reason;
    public string SourceFilePath { get; } = sourceFilePath;
}

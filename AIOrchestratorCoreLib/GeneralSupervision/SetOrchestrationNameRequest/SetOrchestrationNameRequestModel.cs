namespace AIOrchestratorCoreLib.GeneralSupervision.SetOrchestrationNameRequest;

internal sealed class SetOrchestrationNameRequestModel(
    string orchId,
    string name,
    string sourceFilePath) : ISetOrchestrationNameRequest
{
    public string OrchId { get; } = orchId;
    public string Name { get; } = name;
    public string SourceFilePath { get; } = sourceFilePath;
}

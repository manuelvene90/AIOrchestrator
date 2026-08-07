namespace AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;

internal sealed class AddImplementerRequestModel(
    string orchId,
    string reason,
    string sourceFilePath) : IAddImplementerRequest
{
    public string OrchId { get; } = orchId;
    public string Reason { get; } = reason;
    public string SourceFilePath { get; } = sourceFilePath;
}

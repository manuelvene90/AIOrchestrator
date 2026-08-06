namespace AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;

internal sealed class AddImplementerRequestModel(
    string orchId,
    string sourceFilePath) : IAddImplementerRequest
{
    public string OrchId { get; } = orchId;
    public string SourceFilePath { get; } = sourceFilePath;
}

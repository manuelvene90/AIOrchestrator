namespace AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;

internal sealed class MalformedRequestModel(
    string filePath,
    string reason) : IMalformedRequest
{
    public string FilePath { get; } = filePath;
    public string Reason { get; } = reason;
}

namespace AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;

internal sealed class MalformedRequestModel(
    string filePath,
    string reason,
    string? orchId) : IMalformedRequest
{
    public string FilePath { get; } = filePath;
    public string Reason { get; } = reason;
    public string? OrchId { get; } = orchId;
}

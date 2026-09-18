namespace AIOrchestratorCoreLib.GeneralSupervision.ClearDispatchPauseRequest;

internal sealed class ClearDispatchPauseRequestModel(
    string requester,
    string reason,
    string sourceFilePath) : IClearDispatchPauseRequest
{
    public string Requester { get; } = requester;
    public string Reason { get; } = reason;
    public string SourceFilePath { get; } = sourceFilePath;
}

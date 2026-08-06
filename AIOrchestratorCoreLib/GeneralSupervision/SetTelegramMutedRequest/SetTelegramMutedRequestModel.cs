namespace AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;

internal sealed class SetTelegramMutedRequestModel(
    bool muted,
    string sourceFilePath) : ISetTelegramMutedRequest
{
    public bool Muted { get; } = muted;
    public string SourceFilePath { get; } = sourceFilePath;
}

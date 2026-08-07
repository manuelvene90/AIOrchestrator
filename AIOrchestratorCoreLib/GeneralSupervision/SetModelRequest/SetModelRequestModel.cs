namespace AIOrchestratorCoreLib.GeneralSupervision.SetModelRequest;

internal sealed class SetModelRequestModel(
    string orchId,
    string role,
    string model,
    string reason,
    string sourceFilePath) : ISetModelRequest
{
    public string OrchId { get; } = orchId;
    public string Role { get; } = role;
    public string Model { get; } = model;
    public string Reason { get; } = reason;
    public string SourceFilePath { get; } = sourceFilePath;
}

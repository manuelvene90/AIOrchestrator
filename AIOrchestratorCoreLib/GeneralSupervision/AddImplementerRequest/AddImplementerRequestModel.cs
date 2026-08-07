using AIOrchestratorCoreLib.Sessions;

namespace AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;

internal sealed class AddImplementerRequestModel(
    string orchId,
    MemberKinds kind,
    string reason,
    string sourceFilePath) : IAddImplementerRequest
{
    public string OrchId { get; } = orchId;
    public MemberKinds Kind { get; } = kind;
    public string Reason { get; } = reason;
    public string SourceFilePath { get; } = sourceFilePath;
}

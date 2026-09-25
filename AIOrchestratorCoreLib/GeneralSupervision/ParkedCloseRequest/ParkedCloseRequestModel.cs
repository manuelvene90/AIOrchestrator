using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;

namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

internal sealed class ParkedCloseRequestModel(
    ParkedCloseKinds kind,
    string orchId,
    string? memberId,
    string requester,
    string reason,
    string parkedFilePath,
    ISpawnSiblingRequest? sibling) : IParkedCloseRequest
{
    public ParkedCloseKinds Kind { get; } = kind;
    public string OrchId { get; } = orchId;
    public string? MemberId { get; } = memberId;
    public string Requester { get; } = requester;
    public string Reason { get; } = reason;
    public string ParkedFilePath { get; } = parkedFilePath;
    public ISpawnSiblingRequest? Sibling { get; } = sibling;
}

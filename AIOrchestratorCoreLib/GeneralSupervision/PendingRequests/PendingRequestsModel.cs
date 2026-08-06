using AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetOrchestrationNameRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

namespace AIOrchestratorCoreLib.GeneralSupervision.PendingRequests;

internal sealed class PendingRequestsModel(
    IReadOnlyList<IStartOrchestrationRequest> startRequests,
    IReadOnlyList<IAddImplementerRequest> addImplementerRequests,
    IReadOnlyList<ICloseImplementerRequest> closeImplementerRequests,
    IReadOnlyList<ICloseOrchestrationRequest> closeOrchestrationRequests,
    IReadOnlyList<ISetTelegramMutedRequest> setTelegramMutedRequests,
    IReadOnlyList<ISetOrchestrationNameRequest> setOrchestrationNameRequests,
    IReadOnlyList<IMalformedRequest> malformedRequests) : IPendingRequests
{
    public IReadOnlyList<IStartOrchestrationRequest> StartRequests { get; } = startRequests;
    public IReadOnlyList<IAddImplementerRequest> AddImplementerRequests { get; } = addImplementerRequests;
    public IReadOnlyList<ICloseImplementerRequest> CloseImplementerRequests { get; } = closeImplementerRequests;
    public IReadOnlyList<ICloseOrchestrationRequest> CloseOrchestrationRequests { get; } = closeOrchestrationRequests;
    public IReadOnlyList<ISetTelegramMutedRequest> SetTelegramMutedRequests { get; } = setTelegramMutedRequests;
    public IReadOnlyList<ISetOrchestrationNameRequest> SetOrchestrationNameRequests { get; } = setOrchestrationNameRequests;
    public IReadOnlyList<IMalformedRequest> MalformedRequests { get; } = malformedRequests;
}

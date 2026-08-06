using AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

namespace AIOrchestratorCoreLib.GeneralSupervision.PendingRequests;

/// <summary>Everything found in .requests/ during one scan.</summary>
public interface IPendingRequests
{
    IReadOnlyList<IStartOrchestrationRequest> StartRequests { get; }
    IReadOnlyList<IAddImplementerRequest> AddImplementerRequests { get; }
    IReadOnlyList<ICloseImplementerRequest> CloseImplementerRequests { get; }
    IReadOnlyList<ICloseOrchestrationRequest> CloseOrchestrationRequests { get; }
    IReadOnlyList<ISetTelegramMutedRequest> SetTelegramMutedRequests { get; }
    IReadOnlyList<string> MalformedFiles { get; }
}

using AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.PromoteOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetModelRequest;
using AIOrchestratorCoreLib.GeneralSupervision.ClearDispatchPauseRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetOrchestrationNameRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
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
    IReadOnlyList<ISetOrchestrationNameRequest> SetOrchestrationNameRequests { get; }
    IReadOnlyList<IPromoteOrchestrationRequest> PromoteOrchestrationRequests { get; }
    IReadOnlyList<ISetModelRequest> SetModelRequests { get; }
    IReadOnlyList<IClearDispatchPauseRequest> ClearDispatchPauseRequests { get; }

    /// <summary>Sibling solos asked for by a solo (spec 2026-09-23 §4.1) — held for the owner's tap.</summary>
    IReadOnlyList<ISpawnSiblingRequest> SpawnSiblingRequests { get; }
    IReadOnlyList<IMalformedRequest> MalformedRequests { get; }
}

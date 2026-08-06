using AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

namespace AIOrchestratorCoreLib.GeneralSupervision.PendingRequests;

public static class PendingRequests_Factory
{
    public static IPendingRequests Create(
        IReadOnlyList<IStartOrchestrationRequest> startRequests,
        IReadOnlyList<IAddImplementerRequest> addImplementerRequests,
        IReadOnlyList<ICloseImplementerRequest> closeImplementerRequests,
        IReadOnlyList<ICloseOrchestrationRequest> closeOrchestrationRequests,
        IReadOnlyList<ISetTelegramMutedRequest> setTelegramMutedRequests,
        IReadOnlyList<string> malformedFiles)
    {
        return new PendingRequestsModel(
            startRequests,
            addImplementerRequests,
            closeImplementerRequests,
            closeOrchestrationRequests,
            setTelegramMutedRequests,
            malformedFiles);
    }
}

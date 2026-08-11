namespace AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;

public static class CloseOrchestrationRequest_Factory
{
    public static ICloseOrchestrationRequest Create(string orchId, string reason, string requester, bool ownerConfirmed, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"close-orchestration request needs an orchId (file '{sourceFilePath}')");

        if (string.IsNullOrWhiteSpace(requester))
            throw new ArgumentException($"close-orchestration request needs a requester (file '{sourceFilePath}')");

        return new CloseOrchestrationRequestModel(orchId, reason, requester, ownerConfirmed, sourceFilePath);
    }
}

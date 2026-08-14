namespace AIOrchestratorCoreLib.GeneralSupervision.PromoteOrchestrationRequest;

public static class PromoteOrchestrationRequest_Factory
{
    public static IPromoteOrchestrationRequest Create(string orchId, string reason, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"Request orchId must be non-empty (file '{sourceFilePath}')");

        return new PromoteOrchestrationRequestModel(orchId, reason, sourceFilePath);
    }
}

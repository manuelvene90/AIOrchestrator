namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

public static class StartOrchestrationRequest_Factory
{
    public static IStartOrchestrationRequest Create(string repoQuery, bool isBasic, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(repoQuery))
            throw new ArgumentException($"Request repoQuery must be non-empty (file '{sourceFilePath}')");

        return new StartOrchestrationRequestModel(repoQuery, isBasic, sourceFilePath);
    }
}

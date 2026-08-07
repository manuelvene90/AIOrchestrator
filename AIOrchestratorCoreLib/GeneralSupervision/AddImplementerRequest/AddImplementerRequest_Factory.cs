namespace AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;

public static class AddImplementerRequest_Factory
{
    public static IAddImplementerRequest Create(string orchId, string reason, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"Request orchId must be non-empty (file '{sourceFilePath}')");

        return new AddImplementerRequestModel(orchId, reason, sourceFilePath);
    }
}

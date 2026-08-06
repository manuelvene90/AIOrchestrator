namespace AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;

public static class CloseImplementerRequest_Factory
{
    public static ICloseImplementerRequest Create(string orchId, string memberId, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException($"close-implementer request needs orchId and memberId (got '{orchId}'/'{memberId}', file '{sourceFilePath}')");

        return new CloseImplementerRequestModel(orchId, memberId, sourceFilePath);
    }
}

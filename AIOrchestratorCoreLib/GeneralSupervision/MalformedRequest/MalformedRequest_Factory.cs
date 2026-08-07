namespace AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;

public static class MalformedRequest_Factory
{
    public static IMalformedRequest Create(string filePath, string reason, string? orchId)
    {
        return new MalformedRequestModel(filePath, reason, orchId);
    }
}

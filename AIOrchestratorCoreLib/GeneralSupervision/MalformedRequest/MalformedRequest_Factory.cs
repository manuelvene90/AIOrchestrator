namespace AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;

public static class MalformedRequest_Factory
{
    public static IMalformedRequest Create(string filePath, string reason)
    {
        return new MalformedRequestModel(filePath, reason);
    }
}

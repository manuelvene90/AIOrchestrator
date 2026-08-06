namespace AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;

public static class SetTelegramMutedRequest_Factory
{
    public static ISetTelegramMutedRequest Create(bool muted, string sourceFilePath)
    {
        return new SetTelegramMutedRequestModel(muted, sourceFilePath);
    }
}

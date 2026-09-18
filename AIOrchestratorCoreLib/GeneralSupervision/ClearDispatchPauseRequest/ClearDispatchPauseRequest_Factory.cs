namespace AIOrchestratorCoreLib.GeneralSupervision.ClearDispatchPauseRequest;

public static class ClearDispatchPauseRequest_Factory
{
    public static IClearDispatchPauseRequest Create(string requester, string reason, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(requester))
            throw new ArgumentException("a clear-dispatch-pause request names its requester", nameof(requester));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("a clear-dispatch-pause request carries a reason", nameof(reason));

        return new ClearDispatchPauseRequestModel(requester, reason, sourceFilePath);
    }
}

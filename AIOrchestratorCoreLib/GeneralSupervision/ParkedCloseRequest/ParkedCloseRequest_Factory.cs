namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

public static class ParkedCloseRequest_Factory
{
    public static IParkedCloseRequest Create_ForOrchestration(string orchId, string requester, string reason, string parkedFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"a parked close-orchestration request needs an orchId (file '{parkedFilePath}')");

        return new ParkedCloseRequestModel(ParkedCloseKinds.Orchestration, orchId, null, requester, reason, parkedFilePath);
    }

    /// <summary>
    /// The member id is REQUIRED here and not merely carried. An implementer close with no member
    /// named is not a close of "the whole thing" and must never be able to become one by defaulting:
    /// the kinds are separate so that a missing field fails to construct instead of widening.
    /// </summary>
    public static IParkedCloseRequest Create_ForImplementer(string orchId, string memberId, string requester, string reason, string parkedFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException($"a parked close-implementer request needs orchId and memberId (got '{orchId}'/'{memberId}', file '{parkedFilePath}')");

        return new ParkedCloseRequestModel(ParkedCloseKinds.Implementer, orchId, memberId, requester, reason, parkedFilePath);
    }

    /// <summary>
    /// A promotion names no member, and that is not the same silence as an orchestration close naming
    /// none: WHICH solo ends is not the requester's to choose, it is whichever one the orchestration
    /// has. Carrying a member id here would invite a request to name a different session and have it
    /// honoured.
    /// </summary>
    public static IParkedCloseRequest Create_ForPromotion(string orchId, string requester, string reason, string parkedFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"a parked promote-orchestration request needs an orchId (file '{parkedFilePath}')");

        return new ParkedCloseRequestModel(ParkedCloseKinds.Promotion, orchId, null, requester, reason, parkedFilePath);
    }
}

using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;

namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

public static class ParkedCloseRequest_Factory
{
    public static IParkedCloseRequest Create_ForOrchestration(string orchId, string requester, string reason, string parkedFilePath)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"a parked close-orchestration request needs an orchId (file '{parkedFilePath}')");

        return new ParkedCloseRequestModel(ParkedCloseKinds.Orchestration, orchId, null, requester, reason, parkedFilePath, null);
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

        return new ParkedCloseRequestModel(ParkedCloseKinds.Implementer, orchId, memberId, requester, reason, parkedFilePath, null);
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

        return new ParkedCloseRequestModel(ParkedCloseKinds.Promotion, orchId, null, requester, reason, parkedFilePath, null);
    }

    /// <summary>
    /// A sibling request carries the WHOLE spawn-sibling request, not a copy of its fields: the prompt, the
    /// re-validation at the tap and the birth all read it, and a second set of slots would be a second
    /// place for the name the owner approved to drift from the name the child is given. The orch id and
    /// the reason are the request's own, so the lifecycle's shared fields cannot disagree with it.
    /// </summary>
    public static IParkedCloseRequest Create_ForSibling(ISpawnSiblingRequest request, string requester, string parkedFilePath)
    {
        if (string.IsNullOrWhiteSpace(request.OrchId))
            throw new ArgumentException($"a parked spawn-sibling request needs an orchId (file '{parkedFilePath}')");

        return new ParkedCloseRequestModel(ParkedCloseKinds.Sibling, request.OrchId, null, requester, request.Reason, parkedFilePath, request);
    }
}

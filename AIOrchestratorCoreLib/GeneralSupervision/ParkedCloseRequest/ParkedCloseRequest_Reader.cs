using AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;

namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

/// <summary>
/// Reads a parked file back as whichever kind of close it is.
///
/// THE WHOLE CONFIRMATION LIFECYCLE USED TO ASSUME ONE KIND. Every parked path went through
/// <c>Read_CloseOrchestrationRequest_OrNull</c>, which returns null for a close-implementer file —
/// so parking one without this would have filed a valid request as "unreadable", told its requester
/// to drop a fresh one, and never asked the owner anything.
///
/// It is deliberately a READER over the existing strict parse rather than a parser of its own. A
/// second way to read a request is a second set of rules to keep in step, and the field that would
/// drift first is the one the owner reads.
/// </summary>
public static class ParkedCloseRequest_Reader
{
    /// <summary>
    /// What a close-implementer request records about who asked. It is a DESCRIPTION, not a name:
    /// that schema has no requester field, and inventing one here would show the owner an
    /// attribution nothing verified. The orchestration schema requires the real thing.
    /// </summary>
    public const string IMPLEMENTER_REQUESTER_DESCRIPTION = "a session in the orchestration (this request type records no name)";

    /// <summary>
    /// Same principle for a promotion: the schema records no requester, and only one session can ask
    /// — the solo, about itself. Describing it beats inventing a name the file does not carry.
    /// </summary>
    public const string PROMOTION_REQUESTER_DESCRIPTION = "the solo session of this orchestration";

    public static IParkedCloseRequest? Read_OrNull(string parkedFilePath)
    {
        var orchestrationRequest = OrchestrationRequests_Reader.Read_CloseOrchestrationRequest_OrNull(parkedFilePath);

        if (orchestrationRequest != null)
        {
            return ParkedCloseRequest_Factory.Create_ForOrchestration(
                orchestrationRequest.OrchId,
                orchestrationRequest.Requester,
                orchestrationRequest.Reason,
                parkedFilePath);
        }

        var implementerRequest = OrchestrationRequests_Reader.Read_CloseImplementerRequest_OrNull(parkedFilePath);

        if (implementerRequest != null)
        {
            return ParkedCloseRequest_Factory.Create_ForImplementer(
                implementerRequest.OrchId,
                implementerRequest.MemberId,
                IMPLEMENTER_REQUESTER_DESCRIPTION,
                implementerRequest.Reason,
                parkedFilePath);
        }

        var promotionRequest = OrchestrationRequests_Reader.Read_PromoteOrchestrationRequest_OrNull(parkedFilePath);

        if (promotionRequest != null)
        {
            return ParkedCloseRequest_Factory.Create_ForPromotion(
                promotionRequest.OrchId,
                PROMOTION_REQUESTER_DESCRIPTION,
                promotionRequest.Reason,
                parkedFilePath);
        }

        // Unreadable, or a kind that has no business being parked. Null means "do not act", and the
        // caller files it unexecuted — a request nobody can read is not authority to end anything.
        return null;
    }
}

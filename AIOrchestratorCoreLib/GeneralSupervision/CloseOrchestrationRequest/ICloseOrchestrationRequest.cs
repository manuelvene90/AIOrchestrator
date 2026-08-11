namespace AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;

/// <summary>
/// An AGENT's request to close a whole orchestration session — the only irreversible action here,
/// and never executed on arrival: it is held until the owner confirms it with a tap.
///
/// The owner's own closes never take this route. They come from the app's UI, where a modal has
/// already been answered, and call <c>IBridgeEngine.Close_Orchestration_ByOwner</c> directly — so
/// there is nothing in this shape that can assert a confirmation which did not happen.
/// </summary>
public interface ICloseOrchestrationRequest
{
    string OrchId { get; }

    /// <summary>WHY it is being closed, in one short line — relayed to the owner, never silent.</summary>
    string Reason { get; }

    /// <summary>
    /// WHO asked. The audit trail could not answer this on 2026-08-11, when 'ai-orchestrator-1'
    /// closed and nothing on disk named the author: the request file is deleted on execution, and
    /// its schema carried no attribution at all. It is also what the owner is shown when asked to
    /// confirm — "who wants this closed" is most of the judgement.
    /// </summary>
    string Requester { get; }

    string SourceFilePath { get; }
}

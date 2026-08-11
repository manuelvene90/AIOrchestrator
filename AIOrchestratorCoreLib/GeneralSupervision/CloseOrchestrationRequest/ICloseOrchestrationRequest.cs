namespace AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;

/// <summary>A request to close a whole orchestration session — the only irreversible action here.</summary>
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

    /// <summary>
    /// Set ONLY by the app's own UI, whose modal Yes/No dialog already IS the owner's confirmation —
    /// asking again in Telegram would be a second prompt for one decision.
    ///
    /// It is a guard against a fallible agent, NOT a security boundary: nothing stops an agent
    /// writing it, and it is deliberately absent from the role commands so none has reason to. What
    /// makes that acceptable is the archived request file, which now records who claimed what.
    /// </summary>
    bool OwnerConfirmed { get; }

    string SourceFilePath { get; }
}

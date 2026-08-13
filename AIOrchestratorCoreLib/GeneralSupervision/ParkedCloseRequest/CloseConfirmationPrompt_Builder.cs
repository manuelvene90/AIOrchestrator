namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

/// <summary>
/// The text the owner taps on, for either kind of close.
///
/// It lives out here rather than in the engine because <c>BridgeEngineModel</c> is
/// <c>internal sealed</c> with no <c>InternalsVisibleTo</c>: anything decided in there is unreachable
/// from the suite, and this is the sentence that decides whether the owner understands what they are
/// ending. A prompt that says "orchestration" while the tap retires one member is the worst possible
/// version of this feature — it would be a guard that MISLEADS at the only moment it is read.
/// </summary>
public static class CloseConfirmationPrompt_Builder
{
    public static string Build(IParkedCloseRequest request, string? unresolvedLedger)
    {
        return request.Kind switch
        {
            ParkedCloseKinds.Orchestration => Build_ForOrchestration(request, unresolvedLedger),
            ParkedCloseKinds.Implementer => Build_ForImplementer(request),
            ParkedCloseKinds.Promotion => Build_ForPromotion(request),
            _ => throw new ArgumentOutOfRangeException(nameof(request), $"unhandled close kind '{request.Kind}'"),
        };
    }

    /// <summary>
    /// WHAT a close would end, in the middle of a sentence — "close of X was declined", "X lapsed".
    ///
    /// One wording, because the held / declined / lapsed / failed notices all name it and they are
    /// written in four different places. Four copies of "this orchestration" is how the message that
    /// retires one member comes to say the whole thing is ending, which is the same fact carried
    /// twice that this repo keeps paying for.
    /// </summary>
    public static string Describe_Subject(IParkedCloseRequest request)
    {
        return request.Kind switch
        {
            ParkedCloseKinds.Orchestration => "this orchestration",
            ParkedCloseKinds.Implementer => $"'{request.MemberId}'",
            ParkedCloseKinds.Promotion => "the promotion to a full crew",
            _ => throw new ArgumentOutOfRangeException(nameof(request), $"unhandled close kind '{request.Kind}'"),
        };
    }

    static string Build_ForOrchestration(IParkedCloseRequest request, string? unresolvedLedger)
    {
        var unresolvedPart = unresolvedLedger == null ? "" : $"\n\n⚠️ {unresolvedLedger}";

        return
            $"⚠️ Close orchestration '{request.OrchId}'?\n\n"
            + $"Asked by: {request.Requester}\n"
            + $"Reason: {request.Reason}{unresolvedPart}\n\n"
            + "This ends every session in it and deletes this topic. It cannot be undone — the folder stays on disk as audit trail. Nothing happens unless you tap.";
    }

    /// <summary>
    /// NO LEDGER LINE HERE, and that is a decision. The ledger belongs to the orchestration, so its
    /// unresolved count says nothing about whether THIS member is safe to retire — showing it beside
    /// a one-member close would read as "these lines die with it", which is false and would push the
    /// owner toward keeping sessions alive for a reason that does not apply.
    ///
    /// What the owner needs instead is the scope: the rest keeps running.
    /// </summary>
    /// <summary>
    /// THE ONE PROMPT THAT ASKS FOR MORE RATHER THAN LESS, so it names the cost first: this is the
    /// owner agreeing to spend a supervisor and an implementer indefinitely, in place of one session.
    ///
    /// It says what SURVIVES as plainly as what ends, because the fear a reader brings to a tap that
    /// kills their session is the wrong fear here — the conversation, the topic and the history all
    /// carry over untouched, and only the session itself is replaced.
    ///
    /// And it says ONE-WAY, because there is no demotion: the way back is closing the orchestration
    /// and starting a basic one, which loses this channel. That is exactly the sort of thing a person
    /// discovers afterwards unless the prompt says it.
    /// </summary>
    static string Build_ForPromotion(IParkedCloseRequest request)
    {
        return
            $"⚙️ Turn '{request.OrchId}' into a full crew?\n\n"
            + $"Asked by: {request.Requester}\n"
            + $"Reason: {request.Reason}\n\n"
            + "This spends a supervisor AND an implementer from now on, instead of the one session you have. "
            + "That session ends — but this conversation, this topic and everything already said carry over to the supervisor untouched. "
            + "Treat it as one-way: there is no going back to a single session without closing the orchestration. Nothing happens unless you tap.";
    }

    static string Build_ForImplementer(IParkedCloseRequest request)
    {
        return
            $"⚠️ Close member '{request.MemberId}' in '{request.OrchId}'?\n\n"
            + $"Asked by: {request.Requester}\n"
            + $"Reason: {request.Reason}\n\n"
            + "This kills that one session's terminal and loses whatever it had not written down. The orchestration and every other member keep running, and its channel stays on disk as audit trail. Nothing happens unless you tap.";
    }
}

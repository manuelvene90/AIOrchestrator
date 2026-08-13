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
    /// THE TWO WORDS THE OWNER ACTUALLY TAPS. They were hard-coded "✅ Close it" / "✋ Keep it open"
    /// while the prompt above them was already kind-aware, so a promotion asked "Turn 'X' into a full
    /// crew?" over a button reading CLOSE IT.
    ///
    /// That is worse than a cosmetic mismatch and worse than the wrong archive label this same class
    /// already guards against. Reading those buttons, the safe-looking tap is "Keep it open" — which
    /// records a DECLINED promotion, tells the solo the owner refused and not to ask again, and
    /// leaves nobody aware that the question asked was never the question answered.
    ///
    /// They live here, beside the prompt they belong to, for the reason this whole class exists: the
    /// engine is unreachable from the suite, and the sentence the owner reads at the one moment they
    /// decide is not something to leave untestable.
    /// </summary>
    public static (string Confirm, string Decline) Build_ButtonLabels(ParkedCloseKinds kind)
    {
        return kind switch
        {
            ParkedCloseKinds.Promotion => ("✅ Make it a crew", "✋ Keep one session"),
            ParkedCloseKinds.Orchestration => ("✅ Close it", "✋ Keep it open"),
            ParkedCloseKinds.Implementer => ("✅ Close it", "✋ Keep it open"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), $"unhandled close kind '{kind}'"),
        };
    }

    /// <summary>
    /// What the prompt is rewritten to say once they have tapped, so the topic carries the decision
    /// rather than the question. It was unconditionally "Close 'X'? … Closed — you confirmed", which
    /// left a promoted orchestration's topic permanently reading that it had been closed.
    ///
    /// An UNREADABLE request falls back to neutral wording rather than to the close wording. Guessing
    /// "closed" is how the record comes to say the opposite of what happened, and this method exists
    /// because that already happened once at the archive label.
    /// </summary>
    public static string Build_DecidedText(ParkedCloseKinds? kind, string orchId, bool confirmed)
    {
        return kind switch
        {
            ParkedCloseKinds.Promotion => confirmed
                ? $"⚙️ Turn '{orchId}' into a full crew?\n\n✅ Promoted — you confirmed. The supervisor is taking over this conversation."
                : $"⚙️ Turn '{orchId}' into a full crew?\n\n✋ Left as one session — you declined. It keeps working exactly as it was.",

            ParkedCloseKinds.Orchestration or ParkedCloseKinds.Implementer => confirmed
                ? $"⚠️ Close '{orchId}'?\n\n✅ Closed — you confirmed."
                : $"⚠️ Close '{orchId}'?\n\n✋ Kept open — you declined. Its sessions keep running.",

            _ => confirmed
                ? $"'{orchId}' — ✅ you confirmed."
                : $"'{orchId}' — ✋ you declined. Nothing was changed.",
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

    /// <summary>
    /// NO LEDGER LINE HERE, and that is a decision. The ledger belongs to the orchestration, so its
    /// unresolved count says nothing about whether THIS member is safe to retire — showing it beside
    /// a one-member close would read as "these lines die with it", which is false and would push the
    /// owner toward keeping sessions alive for a reason that does not apply.
    ///
    /// What the owner needs instead is the scope: the rest keeps running.
    /// </summary>
    static string Build_ForImplementer(IParkedCloseRequest request)
    {
        return
            $"⚠️ Close member '{request.MemberId}' in '{request.OrchId}'?\n\n"
            + $"Asked by: {request.Requester}\n"
            + $"Reason: {request.Reason}\n\n"
            + "This kills that one session's terminal and loses whatever it had not written down. The orchestration and every other member keep running, and its channel stays on disk as audit trail. Nothing happens unless you tap.";
    }
}

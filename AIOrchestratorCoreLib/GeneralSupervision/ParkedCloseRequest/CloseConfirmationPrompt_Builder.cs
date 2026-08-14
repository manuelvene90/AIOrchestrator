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
            _ => throw new ArgumentOutOfRangeException(nameof(request), $"unhandled close kind '{request.Kind}'"),
        };
    }

    /// <summary>
    /// What the prompt is REPLACED with once the tap has been acted on — the last thing the owner is
    /// told about a close, and for a while the only thing that was untrue.
    ///
    /// It used to be written before the close was attempted, so "✅ Closed — you confirmed" appeared
    /// whether or not anything closed. Two paths made it a lie: an unreadable request (nothing is
    /// touched) and a throw partway through (marked closed, sessions alive). The first heals — the
    /// request stays parked and the owner is asked again — so the sentence has to tell them the tap
    /// did not take, or the fresh prompt arrives contradicting a success still on their screen.
    ///
    /// THE SECOND DOES NOT HEAL, and it is why this maps outcomes rather than a boolean. Nothing will
    /// re-offer that request, so whatever this says is final. It must claim NEITHER success nor
    /// failure and point at the one place that can answer, because "we do not know" rendered as either
    /// is how the owner ends up believing live sessions are dead.
    /// </summary>
    /// <param name="request">
    /// What the tap was about, or null when it could not be read — the ONE case where the wording
    /// cannot name a member, because nobody can say whether there was one.
    /// </param>
    public static string Describe_Decision(string orchId, IParkedCloseRequest? request, CloseTapOutcomes outcome)
    {
        var isMember = request?.Kind == ParkedCloseKinds.Implementer;

        // THE HEADER MUST NAME WHAT THE PROMPT NAMED. Both sentences used to open "Close '{orchId}'?"
        // whatever had been tapped, so retiring one member reported itself under the orchestration's
        // name — the exact failure this class's own header calls the worst version of this feature.
        var header = isMember
            ? $"⚠️ Close member '{request!.MemberId}' in '{orchId}'?"
            : $"⚠️ Close '{orchId}'?";

        // WHAT MIGHT STILL BE RUNNING is the member, or the whole orchestration. Saying "sessions" for
        // a one-member close tells the owner something far worse than what happened.
        var survivors = isMember ? $"'{request!.MemberId}' may still be running" : "its sessions may still be running";

        var line = outcome switch
        {
            CloseTapOutcomes.Declined => isMember
                ? "✋ Kept open — you declined. That session keeps running."
                : "✋ Kept open — you declined. Its sessions keep running.",

            CloseTapOutcomes.Closed => "✅ Closed — you confirmed.",

            // NO PROMISE THAT MAY NOT BE KEPT. This said "you will be asked again shortly", which is
            // true only while the file stays readable — a persistently unreadable one is archived and
            // reported to the REQUESTER, and the owner is never asked and never told. The condition is
            // now stated rather than dropped.
            CloseTapOutcomes.NotAttempted =>
                "⚠️ NOT closed — the request could not be read, so nothing was changed. You will be asked again if it can be read on the next sweep.",

            // THE OUTCOME THIS WHOLE CHANGE EXISTS FOR, and it has to be ACTIONABLE as well as honest.
            // It said "check the app" and sent the owner to a card that reads closed and dimmed, with
            // the close button disabled and "Show session" hidden — the one control that would reach a
            // session still running. It now says what is unusual about this outcome instead: the close
            // is recorded, nothing will ask again, and the error is where errors actually land.
            CloseTapOutcomes.Uncertain =>
                $"⚠️ Close did not complete. It is recorded as closed, {survivors}, and you will NOT be asked again. The error is in the General topic.",

            _ => throw new ArgumentOutOfRangeException(nameof(outcome), $"unhandled close outcome '{outcome}'"),
        };

        return $"{header}\n\n{line}";
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
    static string Build_ForImplementer(IParkedCloseRequest request)
    {
        return
            $"⚠️ Close member '{request.MemberId}' in '{request.OrchId}'?\n\n"
            + $"Asked by: {request.Requester}\n"
            + $"Reason: {request.Reason}\n\n"
            + "This kills that one session's terminal and loses whatever it had not written down. The orchestration and every other member keep running, and its channel stays on disk as audit trail. Nothing happens unless you tap.";
    }
}

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHAT THE OWNER IS TOLD WHEN THEIR MESSAGE WENT UNANSWERED PAST THE GRACE WINDOW AND THE SESSION WAS
/// NUDGED — and when they are told nothing new at all.
///
/// <para>
/// "An answer is coming" is a PROMISE, and it is only true of a session that can take a turn. OBSERVED
/// 2026-09-11, fincanva-6: the supervisor could not log in — every attempt between 08:16 and 08:27 UTC
/// answered "Not logged in · Please run /login" — and the owner's phone read
/// "✓✓ · 🔴 Sup: turn ended without a reply — nudged, an answer is coming" at 08:19:24 and again at
/// 08:22:31, each time some twenty seconds after "turn stalled sup turn 35 — error × 3" had said the
/// opposite. The owner resent "Dimmi che smoke devo fare per finire" twice, into a session that could
/// not answer anything. The second promise was not a second nudge of the same message: the tracker is
/// per orchestration and is replaced on every owner delivery (with <c>Nudged</c> false), so each resend
/// earned its own.
/// </para>
/// <para>
/// SO THE PROMISE IS WITHHELD, NOT REWORDED, while the turn carrying the owner's message has failed and
/// not completed. The truth is on the phone or on its way: after three failures the
/// <c>turn stalled</c> alert (owner-facing for every role that talks to the owner — ONCE per turn, see
/// <see cref="Running.StallAlert_Decider"/>, so a resend into an already-stalled turn leaves the first
/// alert standing as the true line rather than adding a copy), and before that either a retry that
/// answers — which is then the reply — or the stall that follows.
/// A second, reworded line ("nudged — no answer yet: its last turn failed") would have been one more
/// message about the same state, and on the reaction receipt (brief D) it is a fresh send rather than
/// an edit, i.e. a notification — decision 14's waterfall with a different sentence. The session is
/// still nudged in its channel and the typing bubble still goes down at the nudge, both unchanged:
/// neither promises anything.
/// </para>
/// </summary>
public static class OwnerNudgeReceipt_Decider
{
    /// <summary>
    /// The receipt text, or null when nothing new is to be said on the phone.
    /// </summary>
    /// <param name="speaker">The resolved voice (🔴 Sup, 🟠 Solo, 🟡 Gen-Sup) — resolved, never spelled.</param>
    /// <param name="failedAttemptsOfCurrentTurn">
    /// From <see cref="OwnerFacingTurn_Reader.Read_CurrentTurnFailures"/>: any value above zero means
    /// the turn that would answer has already failed at least once.
    /// </param>
    public static string? Build_Receipt_OrNull(string speaker, int failedAttemptsOfCurrentTurn)
    {
        if (failedAttemptsOfCurrentTurn > 0)
            return null;

        return $"✓✓  ·  {speaker}: turn ended without a reply — nudged, an answer is coming";
    }

    /// <summary>The log line for a withheld receipt — the decision is never silent.</summary>
    public static string Describe_Withheld(int failedAttemptsOfCurrentTurn)
    {
        return $"Owner NOT told \"an answer is coming\": the turn carrying their message has failed {failedAttemptsOfCurrentTurn} time(s) and has not completed — a retry that answers, or the stall alert, is what reaches them";
    }

    /// <summary>The log line when the session's turn state could not be read and the receipt goes out as before.</summary>
    public static string Describe_Unreadable(string readFailure)
    {
        return $"Could not tell whether the owner-facing session's last turn failed ({readFailure}) — the nudge receipt is sent as before";
    }
}

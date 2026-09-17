namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// THE DIALS AND THE WORDS OF A RE-REVIEW CONTRACT, in one place so no sweep spells them.
/// </summary>
public static class RerouteContract_Policy
{
    /// <summary>
    /// HOW LONG A ROUTED CONTRACT HOLDS THE SUPERVISOR OUT OF THE LOOP. Past this the hold is
    /// released, the fix report wakes the supervisor on the ordinary digest, and one entry says the
    /// re-review never came back.
    ///
    /// <para>
    /// NINETY MINUTES IS A JUDGEMENT AND NOT A MEASUREMENT. A re-review is `quick` by construction —
    /// the delta is small, and the reviewer's own skill costs it at $1–4 — so this is generous by a
    /// wide margin. What matters is the DIRECTION of being wrong: expiring early costs one supervisor
    /// wake-up and restores exactly today's behaviour, while never expiring would leave a round
    /// silently unowned, which is the one failure this feature can produce that nobody would see.
    /// </para>
    /// <para>
    /// A CONSTANT AND NOT A SETTINGS-CATALOGUE ROW. Both of the owner's own dials are data
    /// (CLAUDE.md's model/effort ruling) and this one arguably should be too; nothing in the request
    /// asked for it, so it is PARKED rather than built (decision 22).
    /// </para>
    /// </summary>
    public static readonly TimeSpan REVIEW_CAP = TimeSpan.FromMinutes(90);

    /// <summary>
    /// THE ABSOLUTE CEILING ON A HOLD, and it exists because the cap alone is not one.
    ///
    /// <para>
    /// Expiring is meant to be one-shot in the sense <c>BudgetAlert_Planner</c> teaches: the contract
    /// is removed only once the entry saying "the re-review never came back" is actually on disk, so
    /// an alert refused by a meeting comes back on the tick after it ends. But that choke point also
    /// refuses PERMANENTLY when the owner is at the terminal
    /// (<c>OwnerPresence_Policy.Suppresses_SupervisorAttention</c>), and a refusal that never lifts
    /// would keep the contract, and therefore the hold, for ever — the supervisor kept out of a round
    /// nobody was ever told about, which is the ONE silent failure this whole feature can produce.
    /// </para>
    /// <para>
    /// So the retry is bounded rather than unbounded: past this the hold is released with a log line
    /// and no entry. Losing the alert costs the supervisor nothing it can act on — it is handed the
    /// fix report on its next digest, which is exactly what it got before this feature existed —
    /// whereas losing the release costs it the round.
    /// </para>
    /// </summary>
    public static readonly TimeSpan HOLD_CEILING = REVIEW_CAP + REVIEW_CAP;

    /// <summary>The single word that retracts a declaration: <c>REROUTE: cancel</c>.</summary>
    public const string CANCEL_WORD = "cancel";
}

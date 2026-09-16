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

    /// <summary>The single word that retracts a declaration: <c>REROUTE: cancel</c>.</summary>
    public const string CANCEL_WORD = "cancel";
}

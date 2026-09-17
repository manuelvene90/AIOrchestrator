namespace AIOrchestratorCoreLib.Configuration.ReviewingSettings;

/// <summary>
/// THE ROUTED RE-REVIEW SWEEP'S THREE DIALS, AS DATA — the <c>reviewing</c> block of config.json,
/// resolved through the settings catalogue and the preset beneath it.
///
/// <para>
/// THEY USED TO BE COMPILED CONSTANTS (moved 2026-09-17), exactly as the effort dial was on
/// 2026-09-12 and for the same reason: <c>RerouteContract_Policy.REVIEW_CAP</c>,
/// <c>RerouteContract_Policy.HOLD_CEILING</c> and <c>RerouteContract_Store.HANDLED_MEMORY</c> decide
/// how long a supervisor is held out of its own round, and changing any of them needed a rebuilt app
/// actually running (CLAUDE.md decision 23). Those three fields stay exactly where they are and keep
/// their names: they are now the SHIPPED DEFAULT, which is what the catalogue rows read and what an
/// unconfigured machine still gets, unchanged to the minute.
/// </para>
/// <para>
/// A ROW THAT GOVERNS NOTHING IS WORSE THAN NO ROW, which is the reason this interface exists rather
/// than the resolvers being left unwired. A registered setting invites the owner to state a value;
/// one that is read by nobody accepts it, reports it in the settings surface, and changes nothing —
/// the silent-defeat shape that put a stale model in the owner's own config.json on 2026-09-12.
/// </para>
/// <para>
/// NO NULLS HERE, unlike <c>IEffortSettings</c>. All three rows are non-nullable with a shipped
/// default, because "no cap at all" is not a state this sweep can be in: without a cap a supervisor
/// held by a reviewer that never reports is held for ever, which is the one failure the cap exists
/// to prevent.
/// </para>
/// </summary>
public interface IReviewingSettings
{
    /// <summary>
    /// How long a routed contract holds the supervisor out of the loop. Past it the hold is released,
    /// the fix report reaches the supervisor on the ordinary digest, and one entry says the re-review
    /// never came back.
    /// </summary>
    TimeSpan ReviewCap { get; }

    /// <summary>
    /// The absolute ceiling on a hold, past which it is released even when the release entry itself
    /// keeps being refused — the backstop against a supervisor held out of a round for ever.
    /// </summary>
    TimeSpan HoldCeiling { get; }

    /// <summary>
    /// How many finished re-review declarations an orchestration remembers, so a stale
    /// <c>REROUTE:</c> directive still sitting in a channel cannot re-open a round that has closed.
    /// </summary>
    int HandledMemory { get; }
}

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>What a parked request awaiting the owner's tap should have done to it on this tick.</summary>
public enum ParkedConfirmationActions
{
    /// <summary>Leave it alone. The common case, and every guard below resolves to it.</summary>
    None,

    /// <summary>It has sat unanswered too long: lapse it and tell the requester.</summary>
    Expire,

    /// <summary>Nobody has asked the owner yet: post the prompt with its buttons.</summary>
    Ask,
}

/// <summary>
/// Which kind of decision a parked request is asking the owner to make. The parked file carries its
/// own `action`, so the kind is read from the request rather than remembered beside it.
/// </summary>
public enum ConfirmationKinds
{
    /// <summary>An action this build does not know. NOTHING may be executed on it — see the resolver.</summary>
    Unknown,

    CloseOrchestration,
    CloseImplementer,
    PromoteOrchestration,
}

/// <summary>
/// THE DECISIONS behind the owner-confirmation machinery, pulled out of the tick so they can be
/// tested at all. The wiring around them — the registrations, the Telegram prompt, the archive —
/// stays in `BridgeEngineModel`, which is `internal sealed` and unreachable from the suite. If the
/// registration or the expiry is wrong, this is where it will be wrong, so this is what must be
/// observable.
///
/// FOUR PRODUCTION FAILURES ARE ENCODED IN THE ORDER OF THESE GUARDS, and every one of them was a
/// live incident rather than a hypothetical:
///
///   - a request the tap handler was part-way through resolving got a SECOND prompt with fresh
///     buttons for a decision the owner had just made — its registrations were already gone but its
///     file was not archived until two awaited Telegram calls later;
///   - those duplicate registrations then pointed at an archived path that only the file scan could
///     clear, so nothing ever cleared them, and a tap on that immortal button closed an orchestration
///     the owner had explicitly REFUSED to close;
///   - splitting expiry into its own pass dropped a `continue`, so a file that had lapsed but failed
///     to archive got a fresh prompt every two seconds — a waterfall, item 14;
///   - and a DND gate on the prompt froze the only thing that disarms a live button, so a request
///     asked at 21:00 and muted was still tappable, and still closing, thirteen hours later.
///
/// The order is therefore load-bearing: BEING RESOLVED beats everything, EXPIRED beats ASK, and
/// ALREADY ASKED is the last word. Written as one decision with one table rather than three
/// `continue`s in two loops, because a dropped `continue` is exactly how the third one happened.
/// </summary>
public static class ParkedConfirmation_Planner
{
    /// <summary>
    /// ONE table, read by both passes: the expiry sweep acts on <see cref="ParkedConfirmationActions.Expire"/>
    /// and the ask sweep on <see cref="ParkedConfirmationActions.Ask"/>. Two loops sharing one decision
    /// cannot disagree about which requests are live, which is what they did when each carried its own
    /// guard chain.
    /// </summary>
    public static ParkedConfirmationActions Decide(bool isBeingResolved, bool isExpired, bool alreadyAsked)
    {
        // FIRST, ALWAYS. A request the owner has just answered is not unasked and is not stale — its
        // registrations are dropped the instant they tap, while its file survives two awaited Telegram
        // calls longer. Anything that reads the file alone sees an unanswered request that is neither.
        if (isBeingResolved)
            return ParkedConfirmationActions.None;

        // BEFORE the ask, so a lapsed request is never re-asked even if lapsing it failed. This is the
        // `continue` whose absence produced the waterfall: the file stayed parked with nothing
        // registered, and the ask pass posted fresh live buttons every two seconds.
        if (isExpired)
            return ParkedConfirmationActions.Expire;

        return alreadyAsked ? ParkedConfirmationActions.None : ParkedConfirmationActions.Ask;
    }

    /// <summary>
    /// The kind of decision a parked request carries, from its own `action` string.
    ///
    /// UNKNOWN IS A REFUSAL, not a default. A parked file's actions are destructive or expensive —
    /// closing an orchestration, retiring a member, spending a crew — so an action this build does not
    /// recognise must execute NOTHING. Guessing the closest match is how a file written by a newer
    /// build, or corrupted, gets executed as something it never said.
    ///
    /// This is decision 21's shape with the opposite conclusion, deliberately: a hook that cannot
    /// evaluate its predicate ALLOWS, because it only ever advises. Here the effect is irreversible,
    /// so the same uncertainty must stop instead.
    /// </summary>
    public static ConfirmationKinds Resolve_Kind(string? action)
    {
        return action switch
        {
            OrchestrationRequests_Reader.CLOSE_ORCHESTRATION_ACTION => ConfirmationKinds.CloseOrchestration,
            OrchestrationRequests_Reader.CLOSE_IMPLEMENTER_ACTION => ConfirmationKinds.CloseImplementer,
            OrchestrationRequests_Reader.PROMOTE_ORCHESTRATION_ACTION => ConfirmationKinds.PromoteOrchestration,
            _ => ConfirmationKinds.Unknown,
        };
    }
}

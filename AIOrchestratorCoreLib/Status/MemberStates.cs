namespace AIOrchestratorCoreLib.Status;

/// <summary>Development state of one implementer spoke, derived from its channel entries.</summary>
public enum MemberStates
{
    NewNoTraffic,
    ImplementerWorking,
    AwaitingSupervisorReview,
    WritingWindowOpen,
    BlockedOnOwner,

    /// <summary>
    /// The member has DECLARED itself idle: nothing owed, nothing running, waiting to be spoken to.
    /// It is the state that had no name, and its absence made the two nudges exhaustive — a channel
    /// ends either in a member's entry or in someone else's, so one of the two always fired and an
    /// idle member was woken every 8 minutes forever. "Standing by" was indistinguishable from a
    /// filed report awaiting a verdict, so the supervisor was nudged about it too.
    ///
    /// It is declared with a marker rather than inferred from prose, for the same reason
    /// <see cref="MemberState_Resolver.BLOCKED_ON_OWNER_MARKER"/> is: the state exists in the
    /// member's head and nowhere else, and a predicate that guesses at it from wording is only as
    /// good as the writer's phrasing that day.
    /// </summary>
    StandingBy,
}

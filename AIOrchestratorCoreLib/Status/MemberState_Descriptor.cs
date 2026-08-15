namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// The one wording, and the one brush key, for a member's declared state. Both were in the WPF
/// project — and the wording existed there TWICE, once in the card builder and once in the bridge
/// engine — which CLAUDE.md item 12 forbids and which this repo has already paid for once, when a
/// second copy of a duration formatter lacked the guard the first one had.
///
/// The move here is not tidiness, it is EVIDENCE. `dotnet test` never compiles the WPF project, so a
/// switch living there is unreachable by the suite: adding <see cref="MemberStates.StandingBy"/>
/// left three of these throwing on the happy path and 484 tests stayed green. Anything that must
/// enumerate this enum belongs where the suite can see it, and
/// MemberStateDescriptorTests walks every value so the next member added cannot pass unhandled.
///
/// No WPF types cross this boundary: the brush KEY is a string the view resolves itself.
/// </summary>
public static class MemberState_Descriptor
{
    public static string Describe(MemberStates state)
    {
        return state switch
        {
            MemberStates.NewNoTraffic => "new — no traffic",
            MemberStates.ImplementerWorking => "briefed — not started yet",
            MemberStates.AwaitingSupervisorReview => "awaiting review",
            MemberStates.WritingWindowOpen => "idle — writing window left open",
            MemberStates.BlockedOnOwner => "BLOCKED ON OWNER",
            MemberStates.StandingBy => "standing by — nothing owed",
            _ => throw new Exception($"Unhandled MemberStates: {state}"),
        };
    }

    /// <summary>The one wording for a session that has spoken and is waiting on the owner.</summary>
    public const string WAITING_ON_OWNER = "waiting on you";

    /// <summary>
    /// WHAT THE OWNER ACTUALLY WANTS TO KNOW, in three states rather than two.
    ///
    /// "idle" used to cover everything that was not mid-turn, and the owner called it out on
    /// 2026-08-15 while we were mid-conversation: *"I know you're not idle because we're talking …
    /// especially because you had just asked me a question, and I had answered you, so you were
    /// definitely reasoning."* The label was literally true — no turn inside the two-minute window —
    /// and it reads as ABSENT when the truth is WAITING. In a conversation neither side is mid-turn
    /// most of the time, so "idle" was the state the owner saw almost whenever they looked.
    ///
    /// The third state is not a new judgement: it is <see cref="OwnerOwesReply_Decider"/>, the same
    /// question the stall alert asks, so the card and the alert cannot disagree about whose move it
    /// is.
    ///
    /// It applies ONLY to the session that talks to the owner. An implementer waiting on its
    /// supervisor is not waiting on THEM, and telling the owner otherwise would hand them a queue
    /// that is not theirs to clear.
    /// </summary>
    public static string Describe_ForOwner(MemberStates state, bool isWorkingNow, bool ownerOwesReply)
    {
        if (isWorkingNow)
        {
            return state switch
            {
                MemberStates.WritingWindowOpen => "working now (writing window open)",
                MemberStates.BlockedOnOwner => "working now (was blocked on you)",
                MemberStates.AwaitingSupervisorReview => "working now (report filed)",
                _ => "working now",
            };
        }

        // BEFORE the declared state, deliberately: "new — no traffic" and "standing by" are both
        // states a waiting session can be in, and either one shown to the owner while their answer
        // is the only thing missing is the same misreading in different words.
        if (ownerOwesReply)
            return WAITING_ON_OWNER;

        return Describe(state);
    }

    public static string Brush_Key(MemberStates state)
    {
        return state switch
        {
            MemberStates.NewNoTraffic => "StateNew",
            MemberStates.ImplementerWorking => "StateWorking",
            MemberStates.AwaitingSupervisorReview => "StateAwaitingReview",
            MemberStates.WritingWindowOpen => "StateWindowOpen",
            MemberStates.BlockedOnOwner => "StateBlocked",

            // Deliberately the same brush as "awaiting review": both mean quiet-and-fine, and the
            // owner's card should not sprout a new colour for a state that means nothing is wrong.
            MemberStates.StandingBy => "StateAwaitingReview",

            _ => throw new Exception($"Unhandled MemberStates: {state}"),
        };
    }
}

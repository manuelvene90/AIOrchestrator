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

using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Status.IdleMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The flag's dedup key must be the MEMBER SET and nothing that moves on its own.
///
/// This pins a defect that ran for six hours on 2026-08-13: the key was the rendered entry text,
/// "imp-2 (idle 1 h 56 min)", so it changed every minute and the comparison it feeds could never
/// match. 151 identical flags were written between 12:04 and 18:24, one per minute, and 137 of them
/// had to be archived — six hours of noise in the one channel the supervisor reads to decide what to
/// do. A key that changes whenever a clock ticks is not a key.
///
/// The two tests below redden for DIFFERENT reasons, which is what makes them controls rather than
/// siblings: the first fails if the duration is back in the key, the second fails if the key stops
/// tracking membership. A key hard-coded to a constant would pass the first alone.
/// </summary>
public class IdleFlagKeyTests
{
    /// <summary>
    /// The defect itself. Same members, a clock that has moved — one flag, not two.
    ///
    /// The body assertion is the other half and is deliberately not a negative: it pins that the
    /// duration is still SHOWN. Dropping it from the body would also make this key stable, and that
    /// would be a fix by amputation — the supervisor needs to know how long, it just must not be
    /// told again every minute.
    /// </summary>
    [Fact]
    public void ATickingClockDoesNotChangeTheKey()
    {
        var earlier = Idle(("imp-2", "1 h 56 min"));
        var later = Idle(("imp-2", "1 h 57 min"));

        Assert.Equal(Retirement_Advisor.Build_FlagKey(earlier), Retirement_Advisor.Build_FlagKey(later));

        Assert.NotEqual(Retirement_Advisor.Build_FlagBody(earlier), Retirement_Advisor.Build_FlagBody(later));
        Assert.Contains("1 h 56 min", Retirement_Advisor.Build_FlagBody(earlier));
        Assert.Contains("1 h 57 min", Retirement_Advisor.Build_FlagBody(later));
    }

    /// <summary>
    /// The other direction, and the reason the fix is not "never flag twice": a member joining or
    /// leaving the idle set is news, and it must still get through.
    /// </summary>
    [Fact]
    public void AChangedMemberSetChangesTheKey()
    {
        var one = Idle(("imp-2", "1 h 56 min"));
        var two = Idle(("imp-2", "1 h 56 min"), ("rev-1", "45 min"));

        Assert.NotEqual(Retirement_Advisor.Build_FlagKey(one), Retirement_Advisor.Build_FlagKey(two));
    }

    /// <summary>
    /// The same members reported in a different order are the same set. Member order comes from the
    /// session roster, and a reordering there is not something to wake the supervisor about.
    /// </summary>
    [Fact]
    public void TheKeyIsAnOrderIndependentSet()
    {
        var forwards = Idle(("imp-2", "1 h 56 min"), ("rev-1", "45 min"));
        var backwards = Idle(("rev-1", "45 min"), ("imp-2", "1 h 56 min"));

        Assert.Equal(Retirement_Advisor.Build_FlagKey(forwards), Retirement_Advisor.Build_FlagKey(backwards));
    }

    /// <summary>
    /// The body still says the things that make the flag actionable and non-coercive: who, how long,
    /// and that it is a reminder rather than an instruction. Asserted positively — a body that merely
    /// lacked the wrong words could be empty and still pass.
    /// </summary>
    [Fact]
    public void TheBodyNamesTheMembersAndStaysAReminder()
    {
        var body = Retirement_Advisor.Build_FlagBody(Idle(("imp-2", "1 h 56 min"), ("rev-1", "45 min")));

        Assert.Contains("imp-2", body);
        Assert.Contains("rev-1", body);
        Assert.Contains("REMINDER, not an instruction", body);
    }

    static IReadOnlyList<IIdleMember> Idle(params (string MemberId, string IdleFor)[] members)
    {
        List<IIdleMember> built = [];

        foreach (var (memberId, idleFor) in members)
            built.Add(IdleMember_Factory.Create(memberId, idleFor));

        return built;
    }
}

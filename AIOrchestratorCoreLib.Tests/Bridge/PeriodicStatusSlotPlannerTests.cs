using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The half-hourly status used to fire on ELAPSED TIME since each orchestration's own last push, so
/// every topic carried its own phase and the owner got a trickle instead of one batch: "when I have
/// many orchestration sessions open I get continuously spammed because they are all out of sync".
///
/// These pin PROPERTIES, not one fixture. The load-bearing one is convergence: two orchestrations
/// first seen at unrelated moments must push on THE SAME TICKS, forever, with no per-orchestration
/// offset surviving anywhere. A single-fixture test would pass on an implementation that merely
/// rounded each topic's own cadence.
///
/// They run on an explicit tick grid rather than on the clock, because the interesting cases are all
/// about WHICH tick observes a boundary — the app's real tick is 2 s (MIRROR_TICK_MILLISECONDS) and
/// an outage across a boundary is a gap in that grid, which is exactly how the outage case is built.
/// </summary>
public class PeriodicStatusSlotPlannerTests
{
    static readonly DateTime NOON = new(2026, 8, 13, 12, 0, 0, DateTimeKind.Local);

    const int TICK_SECONDS = 2;

    /// <summary>The away digest's cadence — every property below was written against it and still holds for it.</summary>
    const int HALF_HOUR = PeriodicStatusSlot_Planner.SLOT_MINUTES;

    // ---------------------------------------------------------------------------------------
    // Slot_Start — the flooring itself
    // ---------------------------------------------------------------------------------------

    /// <summary>Every moment in a half hour belongs to the same slot, and it starts on :00 or :30.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(29, 0)]
    [InlineData(29, 59)]
    [InlineData(30, 0)]
    [InlineData(31, 0)]
    [InlineData(59, 59)]
    public void AMomentBelongsToTheHalfHourItFallsIn(int minute, int second)
    {
        var slot = PeriodicStatusSlot_Planner.Slot_Start(NOON.AddMinutes(minute).AddSeconds(second), HALF_HOUR);

        Assert.Equal(minute < 30 ? NOON : NOON.AddMinutes(30), slot);
        Assert.Equal(0, slot.Second);
        Assert.Equal(0, slot.Millisecond);
    }

    /// <summary>The slot keeps the clock it was given — a slot floored from a local moment is local.</summary>
    [Fact]
    public void ASlotKeepsTheKindOfTheClockItWasFlooredFrom()
    {
        Assert.Equal(DateTimeKind.Local, PeriodicStatusSlot_Planner.Slot_Start(NOON.AddMinutes(7), HALF_HOUR).Kind);
    }

    // ---------------------------------------------------------------------------------------
    // THE PROPERTY THIS EXISTS FOR — convergence
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// TWO ORCHESTRATIONS FIRST SEEN AT UNRELATED MOMENTS PUSH ON THE SAME TICKS. This is the owner's
    /// complaint, stated as an assertion: no per-orchestration phase may survive anywhere.
    ///
    /// The comparison starts once BOTH exist: the later one cannot push at a boundary it did not
    /// exist for, and asserting that it does would be asserting something untrue rather than the
    /// property. From its first sight on, every boundary must be shared.
    ///
    /// The counts are asserted too. Two empty lists are equal, and a planner that never pushed at all
    /// would satisfy the convergence claim perfectly — that is the state with two routes to it.
    /// </summary>
    [Fact]
    public void TwoOrchestrationsSeenAtUnrelatedMomentsPushOnTheSameTicks()
    {
        var grid = Ticks(NOON, NOON.AddHours(4));
        var lateFirstSeen = NOON.AddMinutes(52).AddSeconds(47);

        var early = Push_Instants(grid, firstSeen: NOON.AddMinutes(7).AddSeconds(13));
        var late = Push_Instants(grid, firstSeen: lateFirstSeen);

        Assert.Equal([.. early.Where(push => push > lateFirstSeen)], late);
        Assert.True(late.Count >= 5, $"expected several shared pushes over four hours, got {late.Count}");
        Assert.Contains(NOON.AddMinutes(30), early);
    }

    /// <summary>
    /// And they land ON the boundary, not merely together — three topics agreeing on a wrong moment
    /// would pass the convergence test above.
    /// </summary>
    [Fact]
    public void EveryPushLandsOnAHalfHourBoundary()
    {
        var pushes = Push_Instants(Ticks(NOON, NOON.AddHours(4)), firstSeen: NOON.AddMinutes(7));

        Assert.NotEmpty(pushes);

        foreach (var push in pushes)
        {
            Assert.Equal(0, push.Minute % 30);
            Assert.True(push.Second < 10, $"{push:HH:mm:ss} is not on the boundary tick");
        }
    }

    /// <summary>Nothing before the next boundary, and no drift after hours of them.</summary>
    [Fact]
    public void ThePushesAreExactlyTheBoundariesInOrder()
    {
        var pushes = Push_Instants(Ticks(NOON, NOON.AddHours(2)), firstSeen: NOON.AddMinutes(5));

        Assert.Equal(
            [NOON.AddMinutes(30), NOON.AddMinutes(60), NOON.AddMinutes(90), NOON.AddMinutes(120)],
            [.. pushes.Select(push => PeriodicStatusSlot_Planner.Slot_Start(push, HALF_HOUR))]);
    }

    // ---------------------------------------------------------------------------------------
    // A slot fires once
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE SAME SLOT NEVER FIRES TWICE — 900 ticks pass inside one half hour and exactly one of them
    /// pushes.
    /// </summary>
    [Fact]
    public void ASlotAlreadyPushedNeverFiresAgain()
    {
        var pushes = Push_Instants(Ticks(NOON, NOON.AddMinutes(59)), firstSeen: NOON.AddMinutes(5));

        Assert.Single(pushes);
    }

    /// <summary>Stated directly, so it cannot pass only through the simulation.</summary>
    [Fact]
    public void AlreadyPushedThisSlotIsSkipped()
    {
        var plan = PeriodicStatusSlot_Planner.Decide(NOON.AddMinutes(12), NOON, HALF_HOUR);

        Assert.Equal(PeriodicStatusSlotActions.Skip, plan.Action);
    }

    // ---------------------------------------------------------------------------------------
    // A missed boundary does not fire late
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE APP WAS BUSY ACROSS :30 — no tick observed it. The next push is the NEXT boundary, never
    /// the moment the app came back.
    ///
    /// NOT because a late push would drift: the recorded slot is the BOUNDARY, so pushing at 12:41
    /// still records 12:30 and 13:00 still fires on time either way. It is because a digest that
    /// arrives at 12:41 is off-schedule and already stale, and the owner's complaint was as much
    /// about WHEN these land as about their landing together.
    /// </summary>
    [Fact]
    public void ABoundaryMissedDuringAnOutageDoesNotFireOnReturn()
    {
        // Ticks stop at 12:29 and resume at 12:41 — 12:30 passed unobserved.
        var grid = Ticks(NOON, NOON.AddMinutes(29)).Concat(Ticks(NOON.AddMinutes(41), NOON.AddMinutes(59))).ToList();

        var pushes = Push_Instants(grid, firstSeen: NOON);

        Assert.Empty(pushes);
    }

    /// <summary>And it does resume — the outage defers the cadence, it does not end it.</summary>
    [Fact]
    public void TheCadenceResumesAtTheNextBoundaryAfterAnOutage()
    {
        var grid = Ticks(NOON, NOON.AddMinutes(29)).Concat(Ticks(NOON.AddMinutes(41), NOON.AddMinutes(61))).ToList();

        var pushes = Push_Instants(grid, firstSeen: NOON);

        Assert.Equal([NOON.AddMinutes(60)], [.. pushes.Select(push => PeriodicStatusSlot_Planner.Slot_Start(push, HALF_HOUR))]);
    }

    /// <summary>
    /// The grace window is what separates "this tick observed the boundary" from "we are late". Both
    /// sides are asserted so neither can pass for the other's reason.
    /// </summary>
    [Fact]
    public void ATickInsideTheGraceObservesTheBoundaryAndOneOutsideItDoesNot()
    {
        var boundary = NOON.AddMinutes(30);
        var previousSlot = NOON;

        var inside = PeriodicStatusSlot_Planner.Decide(boundary.AddSeconds(PeriodicStatusSlot_Planner.BOUNDARY_GRACE_SECONDS), previousSlot, HALF_HOUR);
        var outside = PeriodicStatusSlot_Planner.Decide(boundary.AddSeconds(PeriodicStatusSlot_Planner.BOUNDARY_GRACE_SECONDS + 1), previousSlot, HALF_HOUR);

        Assert.Equal(PeriodicStatusSlotActions.Push, inside.Action);
        Assert.Equal(PeriodicStatusSlotActions.Skip, outside.Action);
    }

    // ---------------------------------------------------------------------------------------
    // First sight
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// AN ORCHESTRATION IS NEVER PUSHED AT ON THE TICK IT IS FIRST SEEN. It adopts the current slot
    /// silently, so the first status is the first boundary it was actually present for. Because the
    /// store is in-memory, "first seen" includes every orchestration after an app restart — without
    /// this, restarting at 12:07 pushes every topic at 12:07.
    /// </summary>
    [Fact]
    public void AnOrchestrationFirstSeenAdoptsItsSlotWithoutPushing()
    {
        var plan = PeriodicStatusSlot_Planner.Decide(NOON.AddMinutes(7), lastPushedSlotStart: null, HALF_HOUR);

        Assert.Equal(PeriodicStatusSlotActions.Adopt, plan.Action);
        Assert.Equal(NOON, plan.SlotStart);
    }

    /// <summary>
    /// AND NOT SECONDS LATER EITHER, when it is first seen just before a boundary. Adopting the slot
    /// at `now` would leave that to luck: seen at 11:59:59 it adopts 11:30, and one tick later 12:00
    /// is a fresh slot — a push two seconds after spawn, which is the case the owner named. Adoption
    /// counts the grace window in, so both sides of the boundary answer the same.
    /// </summary>
    [Fact]
    public void AnOrchestrationFirstSeenJustBeforeABoundaryDoesNotPushAtIt()
    {
        var pushes = Push_Instants(Ticks(NOON.AddSeconds(-4), NOON.AddMinutes(20)), firstSeen: NOON.AddSeconds(-4));

        Assert.Empty(pushes);
    }

    /// <summary>The same instant, stated as the decision rather than through the grid.</summary>
    [Fact]
    public void AdoptionCountsTheGraceWindowIn()
    {
        var plan = PeriodicStatusSlot_Planner.Decide(NOON.AddSeconds(-1), lastPushedSlotStart: null, HALF_HOUR);

        Assert.Equal(PeriodicStatusSlotActions.Adopt, plan.Action);
        Assert.Equal(NOON, plan.SlotStart);
    }

    /// <summary>
    /// A restart does not resynchronise anything — every orchestration re-adopts on the same tick and
    /// they were already agreeing. Asserted against a topic that was never interrupted.
    /// </summary>
    [Fact]
    public void ARestartedOrchestrationRejoinsTheSameBoundaries()
    {
        var grid = Ticks(NOON, NOON.AddHours(3));

        var uninterrupted = Push_Instants(grid, firstSeen: NOON);
        var restartedMidSlot = Push_Instants(grid, firstSeen: NOON.AddMinutes(37).AddSeconds(19));

        Assert.NotEmpty(restartedMidSlot);
        Assert.Equal([.. uninterrupted.Where(p => p > NOON.AddMinutes(37))], restartedMidSlot);
    }

    // ---------------------------------------------------------------------------------------
    // The clock's residual
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// LOCAL TIME IS NOT MONOTONIC. At the autumn step-back the slot goes BACKWARDS, and the decision
    /// is a change (`!=`), never an ordering (`>`): the cost is at most one extra push at 2 a.m. once
    /// a year. Ordering would hold the line off for a whole hour instead, silently, which is the bad
    /// direction — and this is the test that tells the two implementations apart.
    /// </summary>
    [Fact]
    public void AClockSteppingBackwardsPushesRatherThanGoingSilent()
    {
        var steppedBackTo = NOON.AddMinutes(30);
        var slotAlreadyPushedBeforeTheStep = NOON.AddMinutes(90);

        var plan = PeriodicStatusSlot_Planner.Decide(steppedBackTo.AddSeconds(3), slotAlreadyPushedBeforeTheStep, HALF_HOUR);

        Assert.Equal(PeriodicStatusSlotActions.Push, plan.Action);
    }

    // ---------------------------------------------------------------------------------------
    // The slot length is a parameter (plan 03 Task 8, answer D9)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A LENGTH THAT DOES NOT DIVIDE AN HOUR STILL GETS AN EVEN GRID. Flooring the minute of the hour
    /// would give 45 a slot at :00 and :45 and then a fifteen-minute one back to :00; counted from
    /// midnight it fires every 45 minutes, and every orchestration fires on the same ones.
    /// </summary>
    [Theory]
    [InlineData(45, 11, 20, 11, 15)]
    [InlineData(45, 13, 0, 12, 45)]
    [InlineData(45, 13, 29, 12, 45)]
    [InlineData(45, 13, 30, 13, 30)]
    [InlineData(20, 12, 59, 12, 40)]
    [InlineData(120, 13, 15, 12, 0)]
    public void ASlotOfAnyLength_IsCountedFromMidnight(int slotMinutes, int hour, int minute, int expectedHour, int expectedMinute)
    {
        var moment = new DateTime(2026, 9, 23, hour, minute, 17, DateTimeKind.Local);

        Assert.Equal(
            new DateTime(2026, 9, 23, expectedHour, expectedMinute, 0, DateTimeKind.Local),
            PeriodicStatusSlot_Planner.Slot_Start(moment, slotMinutes));
    }

    /// <summary>
    /// THE AWAY DIGEST DID NOT MOVE when the flooring changed: for every length that divides an hour —
    /// its 30 included — counting from midnight lands on exactly the slot the hour-based floor did.
    /// Walked over a whole day minute by minute, so no hour of it is taken on trust.
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(60)]
    public void ForALengthThatDividesAnHour_TheSlotIsTheHourBasedOne(int slotMinutes)
    {
        var midnight = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Local);

        for (var moment = midnight; moment < midnight.AddDays(1); moment = moment.AddMinutes(1))
        {
            var hourBased = new DateTime(moment.Year, moment.Month, moment.Day, moment.Hour, moment.Minute - (moment.Minute % slotMinutes), 0, moment.Kind);

            Assert.Equal(hourBased, PeriodicStatusSlot_Planner.Slot_Start(moment, slotMinutes));
        }
    }

    /// <summary>A length of zero or less is a bug upstream, and the planner names it rather than dividing by it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void ANonPositiveLength_IsRefused(int slotMinutes)
    {
        var refusal = Assert.ThrowsAny<Exception>(() => PeriodicStatusSlot_Planner.Slot_Start(NOON, slotMinutes));

        Assert.Contains(slotMinutes.ToString(), refusal.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------

    /// <summary>The app's real tick, as a grid of instants, so an outage can be expressed as a gap.</summary>
    static List<DateTime> Ticks(DateTime from, DateTime to)
    {
        var ticks = new List<DateTime>();

        for (var now = from; now <= to; now = now.AddSeconds(TICK_SECONDS))
            ticks.Add(now);

        return ticks;
    }

    /// <summary>
    /// One orchestration walked through the bridge's tick loop, returning the instants it pushed on.
    /// Ticks before `firstSeen` are the app not knowing it exists yet; the store is per-orchestration
    /// and in-memory, exactly as the engine holds it.
    /// </summary>
    static List<DateTime> Push_Instants(IReadOnlyList<DateTime> ticks, DateTime firstSeen)
    {
        DateTime? lastPushedSlotStart = null;
        var pushes = new List<DateTime>();

        foreach (var now in ticks)
        {
            if (now < firstSeen)
                continue;

            var plan = PeriodicStatusSlot_Planner.Decide(now, lastPushedSlotStart, HALF_HOUR);

            if (plan.Action == PeriodicStatusSlotActions.Skip)
                continue;

            lastPushedSlotStart = plan.SlotStart;

            if (plan.Action == PeriodicStatusSlotActions.Push)
                pushes.Add(now);
        }

        return pushes;
    }
}

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>What the engine should do with one orchestration's periodic status this tick.</summary>
public enum PeriodicStatusSlotActions
{
    /// <summary>Not this tick. Record nothing.</summary>
    Skip,

    /// <summary>First sight: take the slot as already spent, send nothing.</summary>
    Adopt,

    /// <summary>A boundary this orchestration was present for, and has not pushed yet.</summary>
    Push,
}

/// <summary>
/// WHEN the half-hourly status fires, as a pure function of the clock — so every topic fires on the
/// SAME tick and the owner gets one batch instead of a trickle.
///
/// WHY: the gate this replaces was elapsed-time since each orchestration's OWN last push
/// (`UtcNow - lastUtc &lt; 1800`). Each topic's phase was therefore set by whenever it first pushed,
/// so N orchestrations settled into N offsets. The owner: "when I have many orchestration sessions
/// open I get continuously spammed because they are all out of sync". Nothing here stores a
/// per-orchestration offset any more — the slot is a property of the clock, identical for everyone,
/// so there is no phase left to drift.
///
/// WHY IT IS OUT HERE, and it is not tidiness: `BridgeEngineModel` is `internal sealed` with no
/// `InternalsVisibleTo` and the test project references CoreLib alone, so a rule decided inside the
/// engine is unreachable from the suite. Three guards were once deleted from it at once with 630
/// tests staying green. See `TopicStatusLine_Planner` for the same move and the same reason.
///
/// THE CLOCK IS LOCAL, DELIBERATELY. The owner named 13:00 and 13:30 — the clock they read. A slot
/// floored in UTC lands on a round LOCAL half-hour only where the offset is a whole or half hour:
/// true in Europe/Rome, false in a `:45` zone, and true by accident in both. Both sides of every
/// comparison here come from that one clock, which is the lesson `Is_AttemptDue` in
/// `TopicStatusLine_Planner` was written to record — it was once handed a UTC stamp against a local
/// `now`, and a 30-second backoff cleared instantly at every value it could be given, with the whole
/// suite green. The engine's field is named for the SLOT it holds, not for a clock, so there is no
/// `…Utc` left to be quietly wrong.
///
/// THE RESIDUAL THAT CHOICE CARRIES: local time is not monotonic. At the autumn step-back the slot
/// moves BACKWARDS, which is why the comparison below is a CHANGE (`!=`) and never an ordering
/// (`&gt;`). The cost is at most one extra push at 2 a.m. once a year; an ordering would instead hold
/// the status off for a whole hour, silently. Spring skips a slot and is harmless. As
/// `Is_AttemptDue` already notes for its own interval, the proper answer for a DURATION is a
/// monotonic source — but a slot is not a duration, it is a position on the owner's wall clock, and
/// that is exactly the thing a monotonic source cannot tell you.
/// </summary>
public static class PeriodicStatusSlot_Planner
{
    /// <summary>The cadence itself: the wall clock is cut into half hours starting on the hour.</summary>
    public const int SLOT_MINUTES = 30;

    /// <summary>
    /// How late a tick may be and still count as having OBSERVED the boundary. The bridge ticks every
    /// 2 s, but a tick does real work (API calls, file polling) and may run long, so the window is
    /// generous enough that a slow tick still counts and small enough that a status the owner reads
    /// as "13:00" was actually sent within a minute of it.
    ///
    /// IT IS NOT PROTECTING THE CADENCE, and an earlier version of this comment claimed it was. What
    /// gets recorded is `SlotStart` — the BOUNDARY — never the moment of the send, so a late push
    /// cannot move the phase: delete this window, let an outage from 12:29 to 12:41 push on return,
    /// and 12:30 is still what gets recorded and 13:00 still fires exactly on time. There is no drift
    /// to put back. The next person to tune this constant would have been tuning it against a reason
    /// that was not true.
    ///
    /// WHAT IT ACTUALLY BUYS is the arrival time, which is the half of the complaint the slot does not
    /// cover. The owner asked for messages AT 13:00 and 13:30; a digest delivered at 12:41 is
    /// off-schedule and eleven minutes stale on arrival. So a status either lands on its boundary or
    /// is skipped, and skipping is the cheap side: the next boundary is at most 30 minutes away and
    /// this is a rolling summary, not an event that can be missed.
    ///
    /// The same constant decides adoption for the same reason — a boundary less than a grace away is
    /// treated as already ours. Both uses mean "close enough to a boundary to count as being at it".
    /// </summary>
    public const int BOUNDARY_GRACE_SECONDS = 60;

    /// <summary>The action, and the slot the engine must record when it is not Skip.</summary>
    public readonly record struct PeriodicStatusSlotPlan(PeriodicStatusSlotActions Action, DateTime SlotStart);

    /// <summary>
    /// The half hour a moment falls in — :00 or :30, seconds cleared, on the clock it was given.
    /// </summary>
    public static DateTime Slot_Start(DateTime moment)
    {
        var slotMinute = moment.Minute - (moment.Minute % SLOT_MINUTES);

        return new DateTime(moment.Year, moment.Month, moment.Day, moment.Hour, slotMinute, 0, moment.Kind);
    }

    /// <summary>
    /// Push at a boundary this orchestration was present for and has not already pushed.
    ///
    /// FIRST SIGHT ADOPTS AND SENDS NOTHING. The engine's store is in-memory and never persisted, so
    /// "first sight" is not only a new orchestration — it is EVERY orchestration after an app
    /// restart. Without this, restarting at 13:07 with four of them running pushes all four at 13:07:
    /// the owner's own complaint arriving by a second route. Alignment a restart can knock out of
    /// phase is not alignment.
    ///
    /// Adoption counts the grace window IN, and that is not a detail: adopting the slot at `now`
    /// leaves the near-boundary case to luck. Seen at 12:59:59 it would adopt 12:30, and one tick
    /// later 13:00 is a fresh slot — a push two seconds after spawn, the case the owner named — while
    /// being seen two seconds later would defer correctly. With the grace included both sides answer
    /// the same.
    ///
    /// AWAY MODE IS NOT CONSULTED HERE, deliberately. Mode decides WHAT is sent; this decides WHEN,
    /// and a mode that could push on adoption would be a second source of phase — the exact thing
    /// this function exists to delete. An away owner is by definition not reading, so a digest is
    /// worth what it says when they look, not how fast it arrived; away mode's own contract already
    /// promises an update every 30 min rather than one on restart. The alternative costs most in the
    /// case it is meant to protect: a watchdog respawn loop would emit an unaligned burst per
    /// respawn, at 04:00, to someone who cannot stop it — and "the app keeps dying" is what the
    /// crash-loop alert is for.
    /// </summary>
    public static PeriodicStatusSlotPlan Decide(DateTime now, DateTime? lastPushedSlotStart)
    {
        if (lastPushedSlotStart == null)
            return new PeriodicStatusSlotPlan(PeriodicStatusSlotActions.Adopt, Slot_Start(now.AddSeconds(BOUNDARY_GRACE_SECONDS)));

        var current = Slot_Start(now);

        // A CHANGE, never an ordering — see the residual in the class comment.
        if (lastPushedSlotStart.Value == current)
            return new PeriodicStatusSlotPlan(PeriodicStatusSlotActions.Skip, current);

        // Late to this boundary: wait for the next one rather than firing now.
        if (now - current > TimeSpan.FromSeconds(BOUNDARY_GRACE_SECONDS))
            return new PeriodicStatusSlotPlan(PeriodicStatusSlotActions.Skip, current);

        return new PeriodicStatusSlotPlan(PeriodicStatusSlotActions.Push, current);
    }
}

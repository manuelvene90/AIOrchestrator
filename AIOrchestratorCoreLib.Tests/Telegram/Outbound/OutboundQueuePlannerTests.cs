using Xunit;
using AIOrchestratorCoreLib.Telegram.Outbound;

namespace AIOrchestratorCoreLib.Tests.Telegram.Outbound;

/// <summary>
/// WHAT GOES NEXT. The owner's priority ruling of 2026-09-10 lives here, and so does the one
/// explicit bet of the design: a band above the one that was refused may still knock on a shut door.
/// </summary>
public class OutboundQueuePlannerTests
{
    static readonly DateTime NOW = new(2026, 9, 11, 9, 27, 0, DateTimeKind.Utc);

    static OutboundIntent Intent(OutboundBands band, string target, DateTime at) =>
        OutboundIntent.Slot(band, "chat:1", $"slot:{band}:{target}", target, (_, _) => Task.FromResult<long?>(null), null, at);

    static bool NothingHeld(string _) => false;

    [Fact]
    public void THE_MOST_IMPORTANT_GOES_FIRST()
    {
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:1", NOW), Intent(OutboundBands.Tap, "msg:2", NOW.AddSeconds(5))],
            NothingHeld,
            bandThatCausedTheHold: null,
            NOW.AddSeconds(6));

        Assert.Equal(OutboundBands.Tap, chosen!.Band);
    }

    [Fact]
    public void WITHIN_A_BAND_THE_OLDEST_GOES_FIRST()
    {
        // Without this a band can starve itself: a surface that republishes constantly would always
        // present a fresher intent than the one that has been waiting.
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:2", NOW.AddSeconds(5)), Intent(OutboundBands.Cosmetic, "msg:1", NOW)],
            NothingHeld,
            bandThatCausedTheHold: null,
            NOW.AddSeconds(6));

        Assert.Equal("msg:1", chosen!.CooldownTarget);
    }

    [Fact]
    public void A_HELD_TARGET_IS_SKIPPED_AND_THE_NEXT_ONE_GOES()
    {
        // The pump is a SCHEDULER, not a queue: a shut door is stepped over, never waited on.
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:held", NOW), Intent(OutboundBands.Cosmetic, "msg:free", NOW.AddSeconds(1))],
            target => target == "msg:held",
            bandThatCausedTheHold: OutboundBands.Cosmetic,
            NOW.AddSeconds(2));

        Assert.Equal("msg:free", chosen!.CooldownTarget);
    }

    [Fact]
    public void NOTHING_ELIGIBLE_MEANS_NOTHING_GOES()
    {
        Assert.Null(OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:held", NOW)],
            _ => true,
            bandThatCausedTheHold: OutboundBands.Cosmetic,
            NOW));
    }

    [Fact]
    public void A_HIGHER_BAND_MAY_STILL_KNOCK_ON_A_HELD_DOOR()
    {
        // THE OWNER'S TAP GETS THROUGH while a status line is in punishment — the inversion this
        // whole change exists to remove. One attempt, no retries: it is [unconfirmed] whether
        // knocking during a flood wait extends it, and one high-value call is a risk worth taking
        // where thirty low-value ones a minute are not.
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Tap, "msg:held", NOW)],
            _ => true,
            bandThatCausedTheHold: OutboundBands.Cosmetic,
            NOW);

        Assert.Equal(OutboundBands.Tap, chosen!.Band);
    }

    [Fact]
    public void A_LOWER_OR_EQUAL_BAND_MAY_NOT()
    {
        Assert.Null(OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:held", NOW)],
            _ => true,
            bandThatCausedTheHold: OutboundBands.Conversation,
            NOW));

        Assert.Null(OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Conversation, "msg:held", NOW)],
            _ => true,
            bandThatCausedTheHold: OutboundBands.Conversation,
            NOW));
    }

    [Fact]
    public void WITH_NOTHING_HELD_THE_EXCEPTION_IS_NOT_CONSULTED_AT_ALL()
    {
        // A door that is open needs no permission to be knocked on. Pinned because an implementation
        // that checked the band FIRST would hold a free target whenever nothing had been refused.
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:free", NOW)],
            NothingHeld,
            bandThatCausedTheHold: null,
            NOW);

        Assert.NotNull(chosen);
    }
}

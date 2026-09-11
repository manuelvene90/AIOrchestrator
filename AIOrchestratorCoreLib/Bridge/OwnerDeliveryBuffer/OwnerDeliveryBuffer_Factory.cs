namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

public static class OwnerDeliveryBuffer_Factory
{
    /// <summary>
    /// HOW LONG A MESSAGE THAT READS AS FINISHED WAITS, instead of the full aggregation window.
    ///
    /// <para>
    /// It waited for NOTHING for one evening (owner decision, 2026-09-09: 11–12 s median from their
    /// Telegram message to the supervisor's turn, and they asked for the wait to go). Measured against
    /// the real flush loop the next day, that cost more than it saved: <c>Flush_OwnerDeliveries_Async</c>
    /// runs on every mirror tick, so a message released on the first pass leaves the buffer before the
    /// next one is typed — two messages two seconds apart bought TWO supervisor turns with full stops
    /// and ONE without, at roughly a million input tokens the turn. A trailing dot was buying a turn.
    /// </para>
    /// <para>
    /// TWO SECONDS IS WHAT THE TWO COSTS BALANCE AT. It is long enough that consecutive typing still
    /// lands in one delivery (the second message resets the count, and the pair then serves the full
    /// window from the later one), long enough that the ⏸ button under the receipt can still reach a
    /// finished message — which the whole aggregation number is sized around — and still a third off
    /// the window for a message that really is alone, which is what the owner asked for.
    /// </para>
    /// <para>
    /// WHAT IT DOES NOT COVER, said plainly rather than implied: a pair typed further apart than this
    /// still buys two turns. That is not a gap in the rule, it is the rule — the aggregation window is
    /// the number that says how far apart two messages may be and still be one thought, and this one is
    /// the discount a finished message gets against it. Both are the owner's to move.
    /// </para>
    /// <para>
    /// NEVER LONGER THAN THE WINDOW: <c>OwnerDeliveryBufferModel</c> clamps it, so a deployment that
    /// shortens the aggregation below two seconds cannot end up making a finished message the SLOW one.
    /// </para>
    /// </summary>
    public const int FINISHED_MESSAGE_QUIET_SECONDS = 2;

    /// <summary>
    /// THERE IS NO HOLD CAP ANY MORE. It took a second argument until 2026-08-20 — sixty seconds,
    /// after which a hold ended by itself — and the owner removed it once they saw what it actually
    /// did: it ended in silence, so the receipt reverted to delivered and every following message
    /// went through as though they had never pressed anything.
    ///
    /// The parameter is GONE rather than defaulted, so no caller can quietly reintroduce a lapse.
    /// </summary>
    public static IOwnerDeliveryBuffer Create(int aggregationSeconds)
    {
        if (aggregationSeconds < 1)
            throw new ArgumentException($"aggregationSeconds must be >= 1, got {aggregationSeconds}");

        return new OwnerDeliveryBufferModel(aggregationSeconds);
    }
}

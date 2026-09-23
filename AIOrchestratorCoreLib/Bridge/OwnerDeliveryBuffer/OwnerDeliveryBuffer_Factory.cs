namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

public static class OwnerDeliveryBuffer_Factory
{
    /// <summary>
    /// THE SHIPPED AGGREGATION WINDOW — the catalogue's default for <c>phone.aggregationSeconds</c>, read
    /// from here so the number and its history have one home. It lived on
    /// <c>BridgeEngineTiming_Factory.OWNER_AGGREGATION_SECONDS</c> as the production value until
    /// 2026-09-23 (plan 03 task 13), when the window became a per-user setting and the timing stopped
    /// carrying one in production. The history moved with it.
    ///
    /// <para>
    /// The owner often texts several messages in a row — quiet time before delivery as ONE entry, so a
    /// burst arrives on the session as one turn instead of one turn each.
    /// </para>
    /// <para>
    /// FOUR SECONDS WAS TOO SHORT TO BE HELD: WAIT can only stop a message still in the buffer, and
    /// four seconds is less than it takes to realise you have more to say and type a word — measured on
    /// the owner's machine, a WAIT five seconds behind its message arrived after the take and stopped
    /// nothing. It went to eight. SIX, because the ⏸ button changed what the window has to be long
    /// enough FOR: with a tap sitting under the receipt the owner set it back down themselves
    /// (2026-08-15) — "with the button we can reduce the window".
    /// </para>
    /// <para>
    /// THREE on 2026-09-09 (bb91051a, the fork author), because the window is no longer served in full by
    /// everybody. Measured on the VPS that day: 11–12 s median from the owner's text to the entry landing
    /// in the supervisor's channel, six of them here. A message that reads as plainly over serves the
    /// shorter <see cref="FINISHED_MESSAGE_QUIET_SECONDS"/> instead, asked below the ⏸ check so a hold
    /// still stops everything, which leaves this number covering what it was always for: a burst of
    /// typing that has not finished yet.
    /// </para>
    /// <para>
    /// AND SIX AGAIN FOR THE OWNER, 2026-09-23 (ai-orchestrator-29 entry [9]): <i>"the buffer time … now is
    /// only very short, but it should be a buffer of 6 seconds, giving me the time to press wait if I
    /// need."</i> With the 2-second discount a finished single message left the buffer before they could
    /// reach ⏸ Wait. So the shipped value stays the fork's 3 and <c>kit/presets/classic.json</c> states 6
    /// with no discount — both machines keep their own phone.
    /// </para>
    /// </summary>
    public const int DEFAULT_AGGREGATION_SECONDS = 3;

    /// <summary>
    /// HOW LONG A MESSAGE THAT READS AS FINISHED WAITS, instead of the full aggregation window — the
    /// catalogue's shipped default for <c>phone.finishedMessageSeconds</c>, read from here so the number
    /// and its argument have one home.
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
    /// the discount a finished message gets against it. Both are the owner's to move, and since
    /// 2026-09-23 they are settings: classic states no discount at all (see
    /// <see cref="DEFAULT_AGGREGATION_SECONDS"/>).
    /// </para>
    /// <para>
    /// NEVER LONGER THAN THE WINDOW: <c>OwnerDeliveryBufferModel</c> clamps it at the point of use, so a
    /// deployment that shortens the aggregation below this cannot end up making a finished message the
    /// SLOW one.
    /// </para>
    /// </summary>
    public const int FINISHED_MESSAGE_QUIET_SECONDS = 2;

    /// <summary>
    /// A FIXED WINDOW, with the shipped discount unless one is named — the shape the buffer's own tests
    /// use, where the window is the subject and nothing resolves a setting. Production never builds this
    /// one; it reads the window live through <see cref="Create_ReadingWindow"/>.
    ///
    /// <para>
    /// THERE IS NO HOLD CAP ANY MORE. It took a hold-cap argument until 2026-08-20 — sixty seconds,
    /// after which a hold ended by itself — and the owner removed it once they saw what it actually
    /// did: it ended in silence, so the receipt reverted to delivered and every following message
    /// went through as though they had never pressed anything. The parameter is GONE rather than
    /// defaulted, so no caller can quietly reintroduce a lapse.
    /// </para>
    /// </summary>
    public static IOwnerDeliveryBuffer Create(int aggregationSeconds, int finishedMessageSeconds = FINISHED_MESSAGE_QUIET_SECONDS)
    {
        Validate_Window(aggregationSeconds, finishedMessageSeconds);

        return new OwnerDeliveryBufferModel(() => (aggregationSeconds, finishedMessageSeconds));
    }

    /// <summary>
    /// THE WINDOW IS ASKED FOR ON EVERY TAKE — the production shape since 2026-09-23 (plan 03 task 13).
    /// <paramref name="readWindow"/> is called once per <c>Take_ReadyDeliveries</c>, which the engine's
    /// flush makes on every mirror tick, so a config.json edited mid-run is obeyed on the next flush
    /// with no engine restart. The engine hands in <c>OwnerAggregationWindow_Resolver.Resolve</c> over
    /// <c>_configProvider.Get_Current().Phone</c>: the value is resolved at the point of effect and
    /// never kept, which is the hot-reload rule every seam in plan 03 follows.
    /// </summary>
    public static IOwnerDeliveryBuffer Create_ReadingWindow(Func<(int AggregationSeconds, int FinishedMessageSeconds)> readWindow)
    {
        return new OwnerDeliveryBufferModel(readWindow);
    }

    /// <summary>
    /// The buffer's floors, for both shapes. An INVARIANT, so it throws naming the value: the catalogue
    /// rows refuse anything below these before a value reaches here, and so does the timing seam.
    /// </summary>
    internal static void Validate_Window(int aggregationSeconds, int finishedMessageSeconds)
    {
        if (aggregationSeconds < 1)
            throw new ArgumentException($"aggregationSeconds must be >= 1, got {aggregationSeconds}");

        if (finishedMessageSeconds < 0)
            throw new ArgumentException($"finishedMessageSeconds must be >= 0, got {finishedMessageSeconds}");
    }
}

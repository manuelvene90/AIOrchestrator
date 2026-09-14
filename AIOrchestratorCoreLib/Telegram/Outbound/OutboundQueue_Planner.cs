namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHAT GOES NEXT — pure, so the whole priority rule can be tested without a clock, a queue or a
/// network. That is not a stylistic preference: the engine this serves is <c>internal sealed</c>
/// with no test seam, and every decision in this subsystem that survived was one that had been
/// lifted out into a pure function first (<see cref="TokenBucket_Gate"/>,
/// <see cref="TelegramAttempt_Gate"/>, <see cref="OutboundCooldown_Gate"/>).
/// </summary>
public static class OutboundQueue_Planner
{
    /// <summary>
    /// The most important intent whose door is open; ties to the oldest, so a band that republishes
    /// constantly cannot starve its own backlog.
    ///
    /// <para>
    /// A SHUT DOOR IS STEPPED OVER, NEVER WAITED ON. That is what makes the pump a scheduler rather
    /// than a queue, and it is the difference between a held status line delaying itself and a held
    /// status line delaying the owner's conversation.
    /// </para>
    /// <para>
    /// THE BAND EXCEPTION. An intent whose target is held is skipped, UNLESS its band is strictly
    /// above <paramref name="bandThatCausedTheHold"/> — so the owner's tap goes out while a status
    /// line's punishment runs. This is the design's one explicit bet: it is [unconfirmed] whether
    /// knocking during a flood wait extends it, since Telegram documents nothing about it either
    /// way. One high-value call is a risk worth taking; thirty low-value ones a minute, which is
    /// what was measured, is not. If a window is ever seen to lengthen under this, revisit here.
    /// </para>
    /// </summary>
    public static OutboundIntent? Decide_Next(
        IReadOnlyList<OutboundIntent> eligible,
        Func<string, bool> isHeld,
        OutboundBands? bandThatCausedTheHold,
        DateTime nowUtc)
    {
        OutboundIntent? best = null;

        foreach (var intent in eligible)
        {
            if (isHeld(intent.CooldownTarget) && !May_KnockAnyway(intent.Band, bandThatCausedTheHold))
                continue;

            if (best == null
                || intent.Band < best.Band
                || (intent.Band == best.Band && intent.EnqueuedUtc < best.EnqueuedUtc))
            {
                best = intent;
            }
        }

        return best;
    }

    /// <summary>
    /// Strictly above, and only while something is actually held: a band never earns a pass on a
    /// door that was never shut.
    /// </summary>
    static bool May_KnockAnyway(OutboundBands band, OutboundBands? bandThatCausedTheHold)
    {
        return bandThatCausedTheHold != null && band < bandThatCausedTheHold.Value;
    }
}

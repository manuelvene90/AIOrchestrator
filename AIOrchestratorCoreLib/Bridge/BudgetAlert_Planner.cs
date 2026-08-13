using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// The runaway-cost alert fires ONCE per orchestration, and that once must be spent on an alert that
/// actually went out.
///
/// <para>
/// IT WAS SPENT ON ONE THAT DID NOT. The engine took the token first and consulted the delivery mode
/// second, so an alert first coming due during a meeting — or under app-wide DND — was marked as
/// sent and then dropped. There is no release anywhere for that token, so the alert could never fire
/// again for the rest of the process: not deferred, not delayed, lost, on the one alert whose whole
/// purpose is telling the owner money is being burnt (rev-7 P1, 2026-08-13).
/// </para>
/// <para>
/// SILENCED IS LICENSED TO DROP, AND THE LICENCE DOES NOT REACH THIS. That licence rests on the owner
/// reading the same content live in a terminal — but this figure is computed by the APP from
/// <c>.usage.json</c> and appears in no terminal, so there is nothing live to read instead. And every
/// other Silenced drop resumes when the silence lifts, while a one-shot token with no reset outlives
/// the meeting that justified it.
/// </para>
/// <para>
/// AND IT DECIDES ONLY WHETHER TO SEND. It used to return a "remember this" half as well, which the
/// engine committed BEFORE the send — so a thrown send (Telegram briefly unreachable, a rate limit)
/// spent the token on an alert that never went out, and with no release anywhere that alert was dead
/// for the life of the process. That half is gone rather than reordered: the memo is now written at
/// exactly one place, after a confirmed send, so there is no longer a value the engine could commit
/// early (rev-6, 2026-08-13). It is the same rule as "the owner's answer survives a failed Telegram
/// send", which this repo fixed once already and did not carry across.
/// </para>
/// <para>
/// No release is needed and none exists: usage only grows, so an orchestration past its ceiling stays
/// past it, and the alert is genuinely once per orchestration per run — once per run that was SENT.
/// </para>
/// </summary>
public static class BudgetAlert_Planner
{
    public static bool Should_Send(
        long tokensUsed,
        long budgetTokens,
        bool alreadyAlerted,
        TelegramDeliveryModes effectiveMode)
    {
        if (tokensUsed < budgetTokens)
            return false;

        if (alreadyAlerted)
            return false;

        // DEFERRED, NOT DROPPED: nothing is recorded on this path, so the alert comes back on the
        // first tick that can deliver it.
        return effectiveMode == TelegramDeliveryModes.Normal;
    }
}

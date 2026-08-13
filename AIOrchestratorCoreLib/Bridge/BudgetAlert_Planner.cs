using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>Whether to send the runaway-cost alert, and whether to remember having sent it.</summary>
public readonly record struct BudgetAlertOutcome(bool ShouldSend, bool RemembersAlerted);

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
/// No release is needed once the order is right: usage only grows, so an orchestration that has
/// passed its ceiling stays past it, and the alert is genuinely once per orchestration per run.
/// </para>
/// </summary>
public static class BudgetAlert_Planner
{
    public static BudgetAlertOutcome Decide(
        long tokensUsed,
        long budgetTokens,
        bool alreadyAlerted,
        TelegramDeliveryModes effectiveMode)
    {
        if (tokensUsed < budgetTokens)
            return new BudgetAlertOutcome(ShouldSend: false, RemembersAlerted: alreadyAlerted);

        // DEFERRED, NOT DROPPED — and the memo is returned UNCHANGED, which is the whole fix.
        if (effectiveMode != TelegramDeliveryModes.Normal)
            return new BudgetAlertOutcome(ShouldSend: false, RemembersAlerted: alreadyAlerted);

        return new BudgetAlertOutcome(ShouldSend: !alreadyAlerted, RemembersAlerted: true);
    }
}

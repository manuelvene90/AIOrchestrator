using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class BudgetAlertPlannerTests
{
    const long Budget = 1_000_000;

    /// <summary>
    /// THE FINDING. The alert first comes due during a meeting: it must be HELD, and the once-per-run
    /// token must not be spent on an alert nobody received. There is no release for that token, so
    /// spending it here loses the alert for the rest of the process.
    /// </summary>
    [Fact]
    public void ComingDueDuringAMeeting_HoldsTheAlert_AndDoesNotSpendTheToken()
    {
        var outcome = BudgetAlert_Planner.Decide(Budget + 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Silenced);

        Assert.False(outcome.ShouldSend);
        Assert.False(outcome.RemembersAlerted);
    }

    /// <summary>And the held alert arrives once delivery is possible again.</summary>
    [Fact]
    public void AfterTheMeeting_TheHeldAlertIsSent()
    {
        var duringMeeting = BudgetAlert_Planner.Decide(Budget + 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Silenced);
        var afterwards = BudgetAlert_Planner.Decide(Budget + 1, Budget, duringMeeting.RemembersAlerted, TelegramDeliveryModes.Normal);

        Assert.True(afterwards.ShouldSend);
        Assert.True(afterwards.RemembersAlerted);
    }

    /// <summary>
    /// DND makes the same promise in writing — traffic is held and replayed, never dropped — and this
    /// alert is sent directly rather than mirrored, so holding the token is the only thing that makes
    /// the promise true for it.
    /// </summary>
    [Fact]
    public void UnderAppWideDeferral_TheAlertIsAlsoHeldRatherThanSpent()
    {
        var outcome = BudgetAlert_Planner.Decide(Budget + 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Deferred);

        Assert.False(outcome.ShouldSend);
        Assert.False(outcome.RemembersAlerted);
    }

    [Fact]
    public void UnderBudget_SaysNothingAndRemembersNothing()
    {
        var outcome = BudgetAlert_Planner.Decide(Budget - 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Normal);

        Assert.False(outcome.ShouldSend);
        Assert.False(outcome.RemembersAlerted);
    }

    /// <summary>Once per run, not once per tick — the rule the token carries when it is spent honestly.</summary>
    [Fact]
    public void AlreadySent_DoesNotRepeatOnEveryTick()
    {
        var outcome = BudgetAlert_Planner.Decide(Budget + 1, Budget, alreadyAlerted: true, TelegramDeliveryModes.Normal);

        Assert.False(outcome.ShouldSend);
        Assert.True(outcome.RemembersAlerted);
    }

    /// <summary>
    /// Passing the ceiling is not forgotten by dropping back under it — usage only grows, so this is
    /// the impossible case, and the planner must not invent a re-arm that the engine has no release for.
    /// </summary>
    [Fact]
    public void AnAlertAlreadySent_IsNotReArmedByAnUnderBudgetReading()
    {
        var outcome = BudgetAlert_Planner.Decide(Budget - 1, Budget, alreadyAlerted: true, TelegramDeliveryModes.Normal);

        Assert.False(outcome.ShouldSend);
        Assert.True(outcome.RemembersAlerted);
    }
}

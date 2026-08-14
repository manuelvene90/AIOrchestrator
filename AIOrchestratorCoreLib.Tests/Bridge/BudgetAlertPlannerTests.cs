using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The planner decides ONLY whether to send. It used to return a "remember this" half as well, and
/// the engine committed that half before the send — so a thrown send spent the once-per-run token on
/// an alert that never went out, with no release anywhere to undo it.
/// <para>
/// So the "a deferred alert does not spend its token" property is no longer asserted here, and that
/// is not a loss of coverage: there is now exactly ONE write to the memo, inside the try block after
/// a confirmed send, so a path that does not send has nothing it could record. The invariant moved
/// from something the engine had to honour into something it cannot express.
/// </para>
/// </summary>
public class BudgetAlertPlannerTests
{
    const long Budget = 1_000_000;

    [Fact]
    public void PastTheCeiling_AndDeliverable_Sends()
    {
        Assert.True(BudgetAlert_Planner.Should_Send(Budget + 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Normal));
    }

    /// <summary>
    /// THE FINDING THIS UNIT WAS BORN FOR. An alert first coming due during a meeting is HELD: the
    /// owner is in a terminal, and this figure — computed by the app from .usage.json — appears in no
    /// terminal, so there is nothing live for them to read instead.
    /// </summary>
    [Fact]
    public void ComingDueDuringAMeeting_DoesNotSend()
    {
        Assert.False(BudgetAlert_Planner.Should_Send(Budget + 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Silenced));
    }

    /// <summary>
    /// DND promises in writing that traffic is held and replayed. This alert is sent directly rather
    /// than mirrored, so not sending is the only way to keep that promise for it.
    /// </summary>
    [Fact]
    public void UnderAppWideDeferral_DoesNotSend()
    {
        Assert.False(BudgetAlert_Planner.Should_Send(Budget + 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Deferred));
    }

    /// <summary>And the held alert goes out on the first tick that can deliver it — it is not lost.</summary>
    [Fact]
    public void AfterTheMeeting_TheHeldAlertSends()
    {
        Assert.False(BudgetAlert_Planner.Should_Send(Budget + 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Silenced));
        Assert.True(BudgetAlert_Planner.Should_Send(Budget + 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Normal));
    }

    [Fact]
    public void UnderBudget_SaysNothing()
    {
        Assert.False(BudgetAlert_Planner.Should_Send(Budget - 1, Budget, alreadyAlerted: false, TelegramDeliveryModes.Normal));
    }

    /// <summary>Once per run, not once per tick — and "already alerted" now means already SENT.</summary>
    [Fact]
    public void AlreadySent_DoesNotRepeatOnEveryTick()
    {
        Assert.False(BudgetAlert_Planner.Should_Send(Budget + 1, Budget, alreadyAlerted: true, TelegramDeliveryModes.Normal));
    }
}

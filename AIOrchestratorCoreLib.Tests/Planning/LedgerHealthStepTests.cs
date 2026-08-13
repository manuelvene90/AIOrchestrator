using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

public class LedgerHealthStepTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public LedgerHealthStepTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-ledger-step-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder("arb-fix"));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    bool Block_IsRaised()
    {
        return File.Exists(LedgerHealth_Tracker.Build_FlagFilePath(_paths, "arb-fix"));
    }

    /// <summary>
    /// THE COMPOSITION, and it is what this step exists for. In a meeting the owner directs the
    /// supervisor to update the ledger — directed work continues — so the debt gets paid mid-meeting.
    /// If the meeting also defers the RECONCILIATION, the block stays up and the turn-end hook keeps
    /// refusing to let the supervisor finish, demanding the file it just wrote.
    /// <para>
    /// It asserts the FLAG FILE, not the position of any guard, so an early exit added above the
    /// reconciliation for any future reason reddens this wherever it is placed.
    /// </para>
    /// </summary>
    [Fact]
    public void DebtPaidDuringAMeeting_LiftsTheBlock_RatherThanHoldingItUntilTheMeetingEnds()
    {
        LedgerHealth_Tracker.Sync_Flag(_paths, "arb-fix", isBehind: true);
        Assert.True(Block_IsRaised());

        LedgerHealth_Step.Reconcile(_paths, "arb-fix", isBehind: false, alreadyReported: true, suppressed: true);

        Assert.False(Block_IsRaised());
    }

    /// <summary>
    /// The debt is still REAL during a meeting, so the flag still goes up: deferring the alert is not
    /// deferring the obligation, and a supervisor that ends its turn owing a ledger update is the
    /// thing the flag was added for.
    /// </summary>
    [Fact]
    public void DebtIncurredDuringAMeeting_StillRaisesTheBlock()
    {
        LedgerHealth_Step.Reconcile(_paths, "arb-fix", isBehind: true, alreadyReported: false, suppressed: true);

        Assert.True(Block_IsRaised());
    }

    /// <summary>
    /// DEFERRED, NOT DROPPED. The once-per-spell token must not be spent on an entry that goes
    /// nowhere — that is the difference between an alert held and an alert destroyed.
    /// </summary>
    [Fact]
    public void InAMeeting_TheAlertIsNotSent_AndTheTokenIsNotSpent()
    {
        var outcome = LedgerHealth_Step.Reconcile(_paths, "arb-fix", isBehind: true, alreadyReported: false, suppressed: true);

        Assert.False(outcome.ShouldAppendAlert);
        Assert.False(outcome.RemembersReported);
    }

    /// <summary>And the deferred alert actually arrives once the owner leaves.</summary>
    [Fact]
    public void AfterTheMeeting_TheDeferredAlertIsDelivered()
    {
        var inMeeting = LedgerHealth_Step.Reconcile(_paths, "arb-fix", isBehind: true, alreadyReported: false, suppressed: true);
        var afterwards = LedgerHealth_Step.Reconcile(_paths, "arb-fix", isBehind: true, alreadyReported: inMeeting.RemembersReported, suppressed: false);

        Assert.True(afterwards.ShouldAppendAlert);
        Assert.True(afterwards.RemembersReported);
    }

    /// <summary>Once per spell, not once per tick — the rule the token carries when it is spent honestly.</summary>
    [Fact]
    public void AnAlertAlreadySent_DoesNotRepeatWhileTheDebtStands()
    {
        var outcome = LedgerHealth_Step.Reconcile(_paths, "arb-fix", isBehind: true, alreadyReported: true, suppressed: false);

        Assert.False(outcome.ShouldAppendAlert);
        Assert.True(outcome.RemembersReported);
    }

    /// <summary>Debt paid outside a meeting clears both the block and the memo, so the next one can fire.</summary>
    [Fact]
    public void DebtPaid_ClearsTheBlockAndForgetsThatItWasReported()
    {
        LedgerHealth_Tracker.Sync_Flag(_paths, "arb-fix", isBehind: true);

        var outcome = LedgerHealth_Step.Reconcile(_paths, "arb-fix", isBehind: false, alreadyReported: true, suppressed: false);

        Assert.False(Block_IsRaised());
        Assert.False(outcome.RemembersReported);
        Assert.False(outcome.ShouldAppendAlert);
    }
}

using AIOrchestratorCoreLib.Bridge.SuppressedEntries;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.SuppressedEntries;

/// <summary>
/// THE ENTRIES <c>phone.push = filtered</c> HOLDS BACK FOR THE TURN-END DIGEST — moved out of
/// <c>BridgeEngineModel</c> into its own store (plan 03 Task 2).
///
/// <para>
/// IT IS A LIST, NOT A SLOT, and that is the whole point. Decision 25 records that the suppressed memo
/// was one slot on 2026-09-10: the answer was filed there, the "WAITING ON …" status line written a
/// minute later overwrote it, and the turn-ended receipt delivered the status line. The owner re-typed
/// their question three times that morning.
/// </para>
/// </summary>
public class SuppressedEntriesTests
{
    const string ORCH_ID = "strategy-lab-6";

    [Fact]
    public void Drain_AfterTwoFilings_ReturnsBothInTheOrderTheyWereSaid()
    {
        var store = SuppressedEntries_Factory.Create(new RecordingLog_Fake());

        store.File(ORCH_ID, "defaults per root", "🔴 Sup: Agreed, three rules.");
        store.File(ORCH_ID, "WAITING ON the re-review", "🔴 Sup: the re-review is still running.");

        var drained = store.Drain(ORCH_ID);

        Assert.Equal(2, drained.Count);
        Assert.Equal(("defaults per root", "🔴 Sup: Agreed, three rules."), drained[0]);
        Assert.Equal(("WAITING ON the re-review", "🔴 Sup: the re-review is still running."), drained[1]);
    }

    /// <summary>A digest delivered once is spent: the next turn end must not hand the owner the same words again.</summary>
    [Fact]
    public void Drain_EmptiesTheOrchestration_SoASecondDrainReturnsNothing()
    {
        var store = SuppressedEntries_Factory.Create(new RecordingLog_Fake());

        store.File(ORCH_ID, "s", "🔴 Sup: progress");
        store.Drain(ORCH_ID);

        Assert.Empty(store.Drain(ORCH_ID));
    }

    [Fact]
    public void Drain_OfAnOrchestrationNothingWasFiledFor_ReturnsNothing()
    {
        var store = SuppressedEntries_Factory.Create(new RecordingLog_Fake());

        Assert.Empty(store.Drain(ORCH_ID));
    }

    /// <summary>Two orchestrations' narration never meet: a digest carries only its own topic's words.</summary>
    [Fact]
    public void Drain_TakesOnlyItsOwnOrchestration()
    {
        var store = SuppressedEntries_Factory.Create(new RecordingLog_Fake());

        store.File(ORCH_ID, "s", "🔴 Sup: mine");
        store.File("crm-2", "s", "🔴 Sup: theirs");

        Assert.Equal("🔴 Sup: mine", Assert.Single(store.Drain(ORCH_ID)).Text);
        Assert.Equal("🔴 Sup: theirs", Assert.Single(store.Drain("crm-2")).Text);
    }

    /// <summary>A closed orchestration, or a fresh owner message, must not leave words behind to surface later out of context.</summary>
    [Fact]
    public void Forget_DropsEverythingFiled_ForThatOrchestrationOnly()
    {
        var store = SuppressedEntries_Factory.Create(new RecordingLog_Fake());

        store.File(ORCH_ID, "s", "🔴 Sup: stale");
        store.File("crm-2", "s", "🔴 Sup: still owed");

        store.Forget(ORCH_ID);

        Assert.Empty(store.Drain(ORCH_ID));
        Assert.Single(store.Drain("crm-2"));
    }

    /// <summary>
    /// CAPPED, OLDEST FIRST, AND SAID. An orchestration that runs for a day with the owner away must not
    /// grow a digest nobody can read; when something has to go, the newest words are the ones worth
    /// keeping — and the drop goes to the log (decision 15: the owner cannot act on it), naming the
    /// entry, which is still in the channel file.
    /// </summary>
    [Fact]
    public void File_PastTheCap_DropsTheOldest_AndLogsWhatItDropped()
    {
        var log = new RecordingLog_Fake();
        var store = SuppressedEntries_Factory.Create(log);

        for (var i = 1; i <= ISuppressedEntries.MAX_ENTRIES_PER_ORCHESTRATION + 1; i++)
            store.File(ORCH_ID, $"entry {i}", $"🔴 Sup: progress {i}");

        var drained = store.Drain(ORCH_ID);

        Assert.Equal(ISuppressedEntries.MAX_ENTRIES_PER_ORCHESTRATION, drained.Count);
        Assert.Equal("entry 2", drained[0].Subject);
        Assert.Equal($"entry {ISuppressedEntries.MAX_ENTRIES_PER_ORCHESTRATION + 1}", drained[^1].Subject);

        // Quoted, so "entry 1" cannot be satisfied by "entry 10".
        Assert.True(log.Has_Line_Containing("'entry 1'"),$"the dropped entry was not named in the log.{Environment.NewLine}{log.Dump()}");
    }
}

using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// WHAT THE TICK'S ENDEAVOUR WORK MAY NOT DO, AND WHERE IT RUNS (Task 10b, from the Task 10 review).
///
/// <para>
/// A SCAN, AND THE WEAKER CLAIM, said as such (the <c>PauseGatesEveryWakerScanTests</c> honesty). Both
/// properties are invisible to a behavioural test:
/// <list type="bullet">
/// <item>"no git on the tick" — the branch <c>GitHead_Reader</c> reads from <c>.git/HEAD</c> is the branch
/// <c>git</c> would print, so swapping in <c>GitSnapshot_Reader</c> keeps every digest test green: two routes
/// to one state, which pins neither (decision 20). The cost is N siblings × every 2 s × several git forks.</item>
/// <item>"compaction after the owner's delivery" — observing it needs a stale sibling lock AND a contended
/// owner channel inside one 1.5 s allowance, measured against a wall clock the engine offers no seam for.
/// The order of statements in the tick is the property, so the order is what is read.</item>
/// </list>
/// </para>
/// </summary>
public class EndeavourArtefactsTickScanTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";

    /// <summary>
    /// Every file the 2-second tick runs for the endeavour artefacts. <c>SiblingWorld_Reader</c> is not here
    /// on purpose: it lists worktrees with git, and runs only at arrival, prompt time and tap.
    /// </summary>
    public static TheoryData<string> TheTickFiles => new()
    {
        "EndeavourArtefacts_Step.cs",
        "EndeavourDigest_Reader.cs",
        "EndeavourDigest_Builder.cs",
        "EndeavourMarkers_Sync.cs",
        "DerivedFile_Writer.cs",
        "GitHead_Reader.cs",
    };

    [Theory]
    [MemberData(nameof(TheTickFiles))]
    public void TheTickPath_StartsNoProcess_AndNeverAsksGit(string fileName)
    {
        var code = BranchSource.Read_Code(fileName);

        Assert.True(code.Length > 200, $"{fileName} read as {code.Length} characters — this scan is not reading the file it names");

        Assert.DoesNotContain("GitSnapshot_Reader", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SiblingWorld_Reader", code, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(code, @"\bProcess(StartInfo)?\b"), $"{fileName} names Process — the tick would fork");
    }

    /// <summary>The digest's branch still comes from the files — the positive half, so the scan above is reading live code.</summary>
    [Fact]
    public void TheDigestReader_TakesTheBranchFromHeadFiles()
    {
        Assert.Contains("GitHead_Reader.Read_Branch_OrNull(", BranchSource.Read_Code("EndeavourDigest_Reader.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEngineEndeavourCalls_NeverAskGit()
    {
        var engine = BranchSource.Read_Code(ENGINE_FILE);

        foreach (var method in new[] { "void Sync_EndeavourArtefacts()", "void Compact_SiblingOutboxes()" })
        {
            var body = BranchSource.Extract_Method(engine, method);

            Assert.Contains("EndeavourArtefacts_Step.", body, StringComparison.Ordinal);
            Assert.DoesNotContain("GitSnapshot_Reader", body, StringComparison.Ordinal);
            Assert.DoesNotContain("SiblingWorld_Reader", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A NEW CHILD'S FILES ON THE TICK THAT BORE IT (final review M4, 2026-09-24). The tick's roster is read
    /// at its top and the birth runs inside it, so the reconcile further down saw neither the child nor its
    /// newly linked parent until the NEXT tick — and a print child's first turn could run with no
    /// <c>ENDEAVOUR.md</c> in its state pack. The birth loop re-reads the roster once, only when it ran a
    /// birth. A scan, the weaker claim: which of two adjacent ticks wrote a file is a race a behavioural test
    /// could only lose or win by luck.
    /// </summary>
    [Fact]
    public void ATickThatRunsABirth_RereadsTheRoster_BeforeTheReconcile()
    {
        var engine = BranchSource.Read_Code(ENGINE_FILE);
        var births = BranchSource.Extract_Method(engine, "async Task Run_ApprovedSiblingBirths_Async");

        Assert.Contains("_sessionsThisTick = _store.Load_All()", births, StringComparison.Ordinal);

        var tick = BranchSource.Extract_Method(engine, "async Task Execute_MirrorTick_Inside_Snapshot_Async");
        var birth = tick.IndexOf("Run_ApprovedSiblingBirths_Async(", StringComparison.Ordinal);
        var reconcile = tick.IndexOf("Sync_EndeavourArtefacts()", StringComparison.Ordinal);

        Assert.True(birth >= 0 && reconcile > birth, "the reconcile no longer runs after the births in the tick, so re-reading the roster there buys nothing");
    }

    /// <summary>
    /// THE OWNER'S DELIVERY GOES FIRST IN THE SHARED LOCK ALLOWANCE — the engine's own rule, stated where
    /// the announcements drain. Outbox compaction takes the channel lock with a 1 s budget, so run before
    /// <c>Flush_OwnerDeliveries_Async</c> a stale <c>sibling-outbox.md.lock</c> (broken only after 60 s) left
    /// the owner's message 0.5 s for about thirty ticks. It runs after the poll, beside the tailed channels'
    /// own compaction, and the reconcile of the derived files stays where the paused marker needs it.
    /// </summary>
    [Fact]
    public void OutboxCompaction_RunsAfterThePoll_NotBeforeTheOwnersDelivery()
    {
        var tick = BranchSource.Extract_Method(BranchSource.Read_Code(ENGINE_FILE), "async Task Execute_MirrorTick_Inside_Snapshot_Async");

        var reconcile = tick.IndexOf("Sync_EndeavourArtefacts()", StringComparison.Ordinal);
        var deliver = tick.IndexOf("Flush_OwnerDeliveries_Async", StringComparison.Ordinal);
        var poll = tick.IndexOf("_tailer.Poll(", StringComparison.Ordinal);
        var compactOutboxes = tick.IndexOf("Compact_SiblingOutboxes()", StringComparison.Ordinal);
        var compactChannels = tick.IndexOf("Compact_LongChannels()", StringComparison.Ordinal);

        Assert.True(deliver >= 0 && poll >= 0 && compactChannels >= 0, "the tick no longer reads as this scan expects — it cannot judge an order it cannot find");
        Assert.True(reconcile >= 0 && reconcile < deliver, "the derived-file reconcile left its place before the owner's delivery");
        Assert.True(compactOutboxes >= 0, "nothing compacts the sibling outboxes on the tick");
        Assert.True(compactOutboxes > deliver && compactOutboxes > poll, "outbox compaction runs before the owner's delivery or the poll, and can spend the owner's lock allowance");

        var reconcileBody = BranchSource.Extract_Method(BranchSource.Read_Code("EndeavourArtefacts_Step.cs"), "public static IReadOnlyList<string> Reconcile(");
        Assert.DoesNotContain("Compact", reconcileBody, StringComparison.Ordinal);
    }
}

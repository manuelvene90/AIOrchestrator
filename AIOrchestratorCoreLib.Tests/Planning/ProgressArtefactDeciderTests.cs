using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// Whether to publish the supervisor's progress artefact on this tick. Lifted out of the engine
/// because <c>BridgeEngineModel</c> is `internal sealed` with no `InternalsVisibleTo`: a rule decided
/// in there is unreachable rather than untested, and this one runs thirty times a minute.
///
/// WHAT THESE CASES DO NOT COVER, said here rather than left to be assumed: the call's POSITION above
/// the DND gate. That is a property of statement order in the engine, not of this decision, and no
/// case below would redden if the call moved.
/// </summary>
public class ProgressArtefactDeciderTests
{
    static readonly DateTime NOW = new(2026, 8, 14, 12, 0, 0);

    const string CURRENT = """{"text":"2/5 done (40%)"}""";

    [Fact]
    public void Unchanged_AndWithinTheHeartbeat_WritesNothing()
    {
        // The common case: thirty ticks a minute must not be thirty disk writes.
        Assert.Equal(
            ProgressArtefactActions.None,
            ProgressArtefact_Decider.Decide(CURRENT, CURRENT, NOW.AddSeconds(-5), NOW));
    }

    [Fact]
    public void ChangedText_Writes()
    {
        Assert.Equal(
            ProgressArtefactActions.Write,
            ProgressArtefact_Decider.Decide(CURRENT, """{"text":"1/5 done (20%)"}""", NOW.AddSeconds(-5), NOW));
    }

    /// <summary>
    /// The heartbeat, and the boundary is asserted on BOTH sides — a case that only checks "much
    /// older" passes just as well against a rule that never fires at all.
    /// </summary>
    [Fact]
    public void Unchanged_ButOlderThanTheHeartbeat_Writes()
    {
        Assert.Equal(
            ProgressArtefactActions.None,
            ProgressArtefact_Decider.Decide(CURRENT, CURRENT, NOW.AddSeconds(-59), NOW));

        Assert.Equal(
            ProgressArtefactActions.Write,
            ProgressArtefact_Decider.Decide(CURRENT, CURRENT, NOW.AddSeconds(-ProgressArtefact_Decider.HEARTBEAT_SECONDS), NOW));

        Assert.Equal(
            ProgressArtefactActions.Write,
            ProgressArtefact_Decider.Decide(CURRENT, CURRENT, NOW.AddSeconds(-3600), NOW));
    }

    /// <summary>
    /// WHAT IS ON DISK IS A DIFFERENT QUESTION FROM WHAT THIS PROCESS REMEMBERS. The memory is empty
    /// after a restart and the file is not, and — the direction that actually loses data — the memory
    /// can hold a write whose file has since been deleted underneath us.
    /// </summary>
    [Fact]
    public void AFileThatIsNotOnDisk_IsWritten_WhateverTheMemorySays()
    {
        Assert.Equal(
            ProgressArtefactActions.Write,
            ProgressArtefact_Decider.Decide(CURRENT, CURRENT, null, NOW));

        Assert.Equal(
            ProgressArtefactActions.Write,
            ProgressArtefact_Decider.Decide(CURRENT, null, null, NOW));
    }

    [Fact]
    public void AfterARestart_TheFirstTickRewritesOnce()
    {
        // Remembered text is null, the file is present and current: one write, then quiet.
        Assert.Equal(
            ProgressArtefactActions.Write,
            ProgressArtefact_Decider.Decide(CURRENT, null, NOW.AddSeconds(-5), NOW));

        Assert.Equal(
            ProgressArtefactActions.None,
            ProgressArtefact_Decider.Decide(CURRENT, CURRENT, NOW.AddSeconds(-5), NOW));
    }

    /// <summary>
    /// A FUTURE STAMP IS DUE, NOT FRESH. The subtraction gives a negative age, which reads as "not old
    /// enough" and would suppress the heartbeat for as long as the clock stayed behind the stamp — on
    /// the one file the owner's terminal reads. This repo has already paid for a future stamp once.
    /// </summary>
    [Fact]
    public void AFutureTimestamp_IsWritten_RatherThanTrustedAsRecent()
    {
        Assert.Equal(
            ProgressArtefactActions.Write,
            ProgressArtefact_Decider.Decide(CURRENT, CURRENT, NOW.AddHours(6), NOW));
    }

    /// <summary>
    /// An unparseable ledger takes the artefact AWAY. A number nobody can derive from the file on
    /// disk any more is worse than no number, and the renderer falls back cleanly to the line it drew
    /// before this feature existed.
    /// </summary>
    [Fact]
    public void AnUnparseableLedger_DeletesTheArtefact()
    {
        Assert.Equal(
            ProgressArtefactActions.Delete,
            ProgressArtefact_Decider.Decide(null, CURRENT, NOW.AddSeconds(-5), NOW));
    }

    [Fact]
    public void AnUnparseableLedger_WithNothingOnDisk_DoesNothing()
    {
        // Nothing to remove is not a deletion: an orchestration that never had a ledger would
        // otherwise ask the file system to delete a file that has never existed, every two seconds.
        Assert.Equal(
            ProgressArtefactActions.None,
            ProgressArtefact_Decider.Decide(null, null, null, NOW));
    }
}

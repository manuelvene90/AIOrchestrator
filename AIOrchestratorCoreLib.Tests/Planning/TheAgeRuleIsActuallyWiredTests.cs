using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// PINS THE CALL, NOT THE CALLEE. <c>Find_UnmovedInProgressLines</c> is covered directly by
/// <c>AnInProgressLineNobodyIsWorkingOnTests</c>, and that proves only that the function works —
/// not that the engine ever asks it anything.
///
/// This repo has paid for that distinction: replacing a planner's call with a plain null left 634
/// tests green, because every assertion was on the callee. The engine is `internal sealed` with no
/// InternalsVisibleTo, so the suite cannot reach `Report_StaleInProgress` at all; a source scan is
/// the only oracle available, and a weak oracle that exists beats a strong one that cannot run.
/// </summary>
public class TheAgeRuleIsActuallyWiredTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";

    [Fact]
    public void TheEngineAsksTheAgeRuleAndTracksTheLineAges()
    {
        var source = Read_EngineSource();

        // The harness proves it found the right file before asserting a presence in it.
        Assert.Contains("Report_StaleInProgress", source);

        Assert.Contains("StaleInProgress_Detector.Find_UnmovedInProgressLines", source);
        Assert.Contains("Note_InProgressLines", source);
        Assert.Contains("StaleInProgress_Detector.Describe_Unmoved", source);
    }

    /// <summary>
    /// THE BLIND SPOT THE OWNER HIT, pinned as an absence that must NOT come back: the old code
    /// returned outright whenever any session was working, so the busiest orchestration — the one
    /// whose ledger drifts furthest — was never checked at all.
    /// </summary>
    [Fact]
    public void ABusySessionNoLongerEndsTheCheckOutright()
    {
        var source = Read_EngineSource();

        Assert.DoesNotContain(
            "        if (working)\n        {\n            _quietSinceUtc.Remove(session.OrchId);\n            _reportedStaleInProgress.Remove(session.OrchId);\n            return;\n        }",
            source.Replace("\r\n", "\n"));
    }

    static string Read_EngineSource()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "AIOrchestratorCoreLib", "Bridge", "BridgeEngine", ENGINE_FILE);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        // A harness that cannot find its subject fails loudly rather than certifying an absence.
        Assert.Fail($"{ENGINE_FILE} was not found walking up from {AppContext.BaseDirectory}");

        return "";
    }
}

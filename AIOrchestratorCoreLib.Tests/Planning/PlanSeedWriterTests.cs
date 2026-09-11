using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// THE SEED LINE MUST BE BLOCKED ON THE OWNER, not "in progress".
///
/// The seed used to read `- [>] agree the direction with the owner, then replace this line with the
/// real tasks`. `[>]` is an OPEN line to the run-to-the-end Stop hook, and a fresh orchestration that
/// has not yet received the owner's first message has nothing else to point at — no `- [?]` line, no
/// QUESTION in the channel, nothing WAITING ON. So the hook refused the very first turn end of every
/// new session (observed 2026-09-11 at boot), for a session that was, correctly, blocked on the
/// owner from the moment it was created. `[?]` says so honestly, and the app's status line then reads
/// "needs you", which is true.
/// </summary>
public class PlanSeedWriterTests : IDisposable
{
    readonly string _tempRoot;

    public PlanSeedWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-planseed-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public void TheSeedLine_IsBlockedOnTheOwner_SoTheStopHookLetsTheFirstTurnEnd()
    {
        var paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(paths.Get_OrchestrationFolder("crm-4"));

        PlanSeed_Writer.Ensure_Exists(paths, "crm-4", "crm");

        var seed = File.ReadAllText(paths.Get_PlanFile("crm-4"));

        Assert.Contains("- [?] agree the direction with the owner", seed);
        Assert.Contains("blocked on: the owner's first message", seed);

        // And not the marker that broke boot: an in-progress line the hook reads as still-open work.
        Assert.DoesNotContain("- [>] agree the direction", seed);
    }
}

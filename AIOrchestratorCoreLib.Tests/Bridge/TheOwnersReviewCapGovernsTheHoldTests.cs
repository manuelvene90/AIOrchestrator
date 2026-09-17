using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Reviewing;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE ROW THE OWNER WRITES IS THE ONE THE ENGINE OBEYS. <c>reviewing.reviewCapMinutes</c> became a
/// settings-catalogue row on 2026-09-17, and a registered setting that nothing reads is worse than no
/// setting at all: it accepts a value, shows it in the settings surface, and changes nothing — the
/// silent-defeat shape that left a stale model pinned in the owner's own config.json on 2026-09-12.
/// So this drives the WHOLE ENGINE against a config.json that states a ten-minute cap, over a
/// contract routed twenty minutes ago.
///
/// <para>
/// IT IS THE MUTATION OF <see cref="RoutedReportCapTests"/>, NOT A COPY OF IT: twenty minutes is
/// comfortably INSIDE the shipped ninety-minute cap, so this case can only pass if the engine read
/// the owner's number. Restore the engine's <c>RerouteContract_Policy.REVIEW_CAP</c> and the hold is
/// never released here, whatever config.json says. The control beside it is the same twenty-minute
/// contract under a config.json that states nothing.
/// </para>
/// <para>
/// BACKDATED THROUGH THE CONTRACT FILE, never by sleeping — the reason RoutedReportCapTests gives.
/// </para>
/// </summary>
public class TheOwnersReviewCapGovernsTheHoldTests : IDisposable
{
    const string BASE_COMMIT = "abc1234";
    const string HEAD_COMMIT = "def5678";

    /// <summary>Well inside the shipped 90-minute cap, and well past the 10 the owner states below.</summary>
    static readonly TimeSpan ROUTED_AGO = TimeSpan.FromMinutes(20);

    readonly List<string> _tempRoots = [];

    public void Dispose()
    {
        foreach (var root in _tempRoots)
            TempTree.Delete_BestEffort(root);

        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOwnerStatedReviewCap_ReleasesTheHoldTheShippedDefaultWouldStillBeHolding()
    {
        var world = Build_World("""{"repos":[],"reviewing":{"reviewCapMinutes":10}}""");

        var orchId = world.Launcher.Start_Orchestration("Repo", world.RepoPath).OrchId;

        Store_RoutedContract(world.Paths, orchId, DateTime.UtcNow - ROUTED_AGO);

        // READ WHILE IT IS STILL THERE: an empty list has two routes to it (decision 20).
        Assert.Single(RerouteContract_Store.Read_Open(world.Paths, orchId));

        var released = await Run_Until_Async(
            world.Engine,
            () => RerouteContract_Store.Read_Open(world.Paths, orchId).Count == 0,
            BridgeTestTiming.Window_ForTicks(12));

        Assert.True(released, "the engine held the supervisor past the cap the owner configured — the reviewing.* rows are registered and unread");
    }

    /// <summary>
    /// THE CONTROL, and it is what makes the case above mean anything: the SAME contract, the same
    /// age, under a config.json that states nothing must still be held — otherwise the case above
    /// would pass with the clock ignored and every routed contract expired on sight.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AndWithNothingConfigured_TheSameContractIsStillHeld()
    {
        var world = Build_World("""{"repos":[]}""");

        var orchId = world.Launcher.Start_Orchestration("Repo", world.RepoPath).OrchId;

        Store_RoutedContract(world.Paths, orchId, DateTime.UtcNow - ROUTED_AGO);

        var released = await Run_Until_Async(
            world.Engine,
            () => RerouteContract_Store.Read_Open(world.Paths, orchId).Count == 0,
            BridgeTestTiming.Window_ForTicks(12));

        Assert.False(released, $"a contract routed {ROUTED_AGO.TotalMinutes} minutes ago was released under the shipped {RerouteContract_Policy.REVIEW_CAP.TotalMinutes}-minute cap");
    }

    sealed record World(string RepoPath, ISupervisionPaths Paths, IOrchestrationLauncher Launcher, IBridgeEngine Engine);

    World Build_World(string configJson)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-owner-cap-{Guid.NewGuid():N}");
        var tempRepo = Path.Combine(tempRoot, "repo");

        _tempRoots.Add(tempRoot);
        Directory.CreateDirectory(tempRepo);

        var paths = SupervisionPaths_Factory.Create(tempRoot);
        Directory.CreateDirectory(paths.RequestsFolder);
        File.WriteAllText(paths.ConfigFile, configJson);

        var store = OrchestrationSessionStore_Factory.Create(paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(paths);
        var log = OrchestrationLog_Factory.Create(paths);

        var launcher = OrchestrationLauncher_Factory.Create(paths, configProvider, store, new RecordingSpawner_Fake(), log);

        var engine = BridgeEngine_Factory.Create_WithTelegramClient(
            paths, configProvider, store, launcher, log, telegramClient: null, BridgeTestTiming.Fast());

        return new World(tempRepo, paths, launcher, engine);
    }

    static void Store_RoutedContract(ISupervisionPaths paths, string orchId, DateTime routedUtc)
    {
        var declared = RerouteContract_Factory.Create_Declared(
            id: "contract-imp-1-rev-1",
            orchId: orchId,
            implementerId: "imp-1",
            reviewerId: "rev-1",
            baseCommit: BASE_COMMIT,
            brief: "F1 and F3 only.",
            declaredUtc: routedUtc);

        RerouteContract_Store.Write_Open(
            paths,
            orchId,
            [RerouteContract_Factory.CreateFrom_Routed(declared, reportIdentity: "9f2a1c0000000000", headCommit: HEAD_COMMIT, routedUtc: routedUtc)]);
    }

    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 50)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(50);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}

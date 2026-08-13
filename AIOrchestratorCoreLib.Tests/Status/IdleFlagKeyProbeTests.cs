using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// THE ENGINE USES THE KEY, asserted through the real engine — the half that unit tests structurally
/// cannot see.
///
/// `Build_FlagKey` is pinned four ways in IdleFlagKeyTests and every one of them passes while the
/// engine calls `Build_FlagBody` for its dedup key instead. That mutant COMPILES and RUNS — both take
/// the same argument and return a string — and it restores the shipped defect verbatim: the rendered
/// duration back inside the key, one flag per minute, 151 of them in six hours on 2026-08-13.
///
/// The defect never lived in the helper. It lived in which value the CALL SITE fed to the comparison,
/// and a fix that moves logic into a tested helper leaves that layer exactly as unpinned as it found
/// it. Same argument as GuardReportProbeTests in this branch — pin the CALL, not just the callee —
/// applied to the case that motivated the harness in the first place.
///
/// The clock is moved by REWRITING the declaration's stamp between ticks rather than by waiting: the
/// idle duration is computed from that stamp, so an older stamp renders a different duration with no
/// sleep and no flake.
/// </summary>
public class IdleFlagKeyProbeTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public IdleFlagKeyProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-idleflagprobe-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create(_paths, configProvider, store, _launcher, log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE LIVE DEFECT, through the engine. The same member stays idle while only the clock moves —
    /// which is every minute of every idle spell — and that must not be a second flag.
    ///
    /// Reddens the moment the engine's key carries the rendered duration again, whatever the helper
    /// is called and however well the helper is tested.
    /// </summary>
    [Fact]
    public async Task AMemberIdleForLongerIsNotFlaggedTwice()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId;
        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);

        Declare_Idle_Since(session.OrchId, memberId, DateTime.Now.AddMinutes(-31));
        await Tick_Once_Async();

        Assert.True(Wait_Until(() => Count_IdleFlags(ownerChannel) >= 1), "an idle member was never flagged at all");

        // Only the clock moves: same member, same declaration, a duration that now renders differently.
        Declare_Idle_Since(session.OrchId, memberId, DateTime.Now.AddMinutes(-95));
        await Tick_Once_Async();

        Assert.Equal(1, Count_IdleFlags(ownerChannel));
    }

    /// <summary>
    /// The disjoint half, and the reason the fix is not "flag once and never again": a member JOINING
    /// the idle set is news, and the engine must still say so. A key hard-coded to a constant, or an
    /// engine that stopped flagging after the first, passes the test above and fails this one.
    /// </summary>
    [Fact]
    public async Task AMemberJoiningTheIdleSetIsFlaggedAgain()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);

        var first = session.Members[0].MemberId;

        Declare_Idle_Since(session.OrchId, first, DateTime.Now.AddMinutes(-31));
        await Tick_Once_Async();

        Assert.True(Wait_Until(() => Count_IdleFlags(ownerChannel) >= 1), "an idle member was never flagged at all");

        var withSecond = _launcher.Add_Implementer(session.OrchId);
        var second = withSecond.Members.Last(member => member.MemberId != first).MemberId;

        Declare_Idle_Since(session.OrchId, second, DateTime.Now.AddMinutes(-31));
        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Count_IdleFlags(ownerChannel) >= 2),
            "a second member became idle and the supervisor was never told — the dedup swallowed a change in the set");
    }

    /// <summary>
    /// A briefed member that has DECLARED, long enough ago to be an accumulation rather than a pause.
    /// Written as a whole file each time so the stamp can move backwards; the app never rewrites a
    /// channel, but a fixture may.
    /// </summary>
    void Declare_Idle_Since(string orchId, string memberId, DateTime declaredAt)
    {
        var stamp = declaredAt.ToString("yyyy-MM-dd HH:mm");
        var briefStamp = declaredAt.AddMinutes(-5).ToString("yyyy-MM-dd HH:mm");

        File.WriteAllText(
            _paths.Get_ImplementerChannelFile(orchId, memberId),
            $"## [1] FROM supervisor — {briefStamp} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {stamp} — {MemberState_Resolver.STANDING_BY_MARKER} — waiting for the next brief\nnothing owed, nothing running\n");
    }

    int Count_IdleFlags(string ownerChannel)
    {
        if (!File.Exists(ownerChannel))
            return 0;

        return ChannelEntry_Parser
            .Parse_All(File.ReadAllText(ownerChannel))
            .Count(entry =>
                entry.Author == ChannelAuthors.App
                && entry.Subject.Contains(Retirement_Advisor.FLAG_SUBJECT, StringComparison.OrdinalIgnoreCase));
    }

    async Task Tick_Once_Async()
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way this loop ends.
        }
    }

    static bool Wait_Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
                return true;

            Thread.Sleep(50);
        }

        return false;
    }
}

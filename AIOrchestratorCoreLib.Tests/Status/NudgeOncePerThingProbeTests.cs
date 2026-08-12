using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// ONE NUDGE PER UNANSWERED THING, driven through the real engine — because the gate is engine state
/// and a pure function cannot be asked about it.
///
/// The repetition was self-feeding and needed nobody: the app's nudge woke the member (its watcher
/// fingerprints the whole file), the waking proved it alive, that proof cleared the escalation map,
/// and the quiet clock its own write had restarted elapsed. A member that obeyed "silence is
/// acknowledgment" was nudged for obeying it, every 8 minutes, indefinitely.
///
/// ONLY ONE OF THE FOUR CHECKS LIVES HERE, AND A CONTROL RUN IS WHY. The other two engine-level ones
/// were written, run, and DELETED because the control refuted them:
///
///   "no second nudge while nothing new is said"  PASSED with the gate removed  -> vacuous
///   "a new entry earns its own nudge"            FAILED with the gate removed  -> not the gate
///
/// Both are governed by something older: after a nudge, `_nudgedMemberUtc` holds the member and the
/// escalation branch returns for ORPHAN_CONFIRM_MINUTES, so within that window NOTHING can nudge
/// again — with or without this fix. The gate's effect only becomes observable after the alive probe
/// clears that map six minutes later, which no fast test can reach.
///
/// So the DECISION is pinned pure, in NudgeDeciderTests, and the wiring is not. The fourth check — a
/// dead member still escalating — is verified at the diff and reported as such: the escalation
/// branch, the probe and `_nudgedMemberUtc` are untouched, and the first nudge still records its
/// timestamp. A stated limit beats a test that passes for another reason, which is what two of these
/// were doing before the control was run.
/// </summary>
public class NudgeOncePerThingProbeTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public NudgeOncePerThingProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-nudgeonce-tests-{Guid.NewGuid():N}");
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
    /// CHECK 1 — the alarm still fires. Everything else here is about NOT firing, and a gate that
    /// silenced the nudge entirely would satisfy all of them.
    /// </summary>
    [Fact]
    public async Task ADormantMemberIsNudgedOnce()
    {
        var channelFile = Start_WithDormantMember();

        await Tick_Once_Async();

        Assert.True(Wait_Until(() => Count_AppEntries(channelFile) == 1), "the dormant member was never nudged");
    }

    /// <summary>
    /// A briefed member that announced a window and went silent twenty minutes ago — dormant, past
    /// the nudge threshold, and not mid-turn.
    /// </summary>
    string Start_WithDormantMember()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);
        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.WriteAllText(
            channelFile,
            $"## [1] FROM supervisor — {stamp} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {stamp} — WRITING WINDOW OPEN — Parser.cs\nstarting now\n");

        File.SetLastWriteTime(channelFile, DateTime.Now.AddMinutes(-20));

        return channelFile;
    }

    int Count_AppEntries(string channelFile)
    {
        return ChannelEntry_Parser
            .Parse_All(File.ReadAllText(channelFile))
            .Count(entry => entry.Author == ChannelAuthors.App);
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

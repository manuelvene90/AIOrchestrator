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
using AIOrchestratorCoreLib.Bridge.EngineState;
using Xunit;
using Xunit.Abstractions;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE BRANCH THAT ACTED ON A GUESS, DRIVEN END TO END.
///
/// <para>
/// The orphan escalation is the one path in this system that reached into a member's process, and
/// until now it was the only branch with no automated coverage at all — Nudge_Decider records why in
/// its own comment: the two windows were `const int` read against a bare clock, "with no seam, so a
/// test would have to wait six real minutes against a suite that runs in about eighty seconds".
/// </para>
/// <para>
/// Both ends now read the injected clock, so the window can be crossed by advancing it. What this
/// pins is the whole wiring, not the decision: OrphanEscalationDeciderTests proves what the rule
/// says; this proves the loop asks it, and that a member the app cannot read is nudged and then LEFT
/// ALONE. On 2026-09-07 that member would have been declared ORPHANED and respawned, nineteen times
/// in three hours, every one of them a false positive.
/// </para>
/// </summary>
// SERIALISED WITH THE CHANNEL-LOCK CLASSES, because this one starts a real engine and
// BridgeEngineModel.Run_Async rewires the PROCESS-WIDE ChannelLock_Diagnostics sink on every
// start. A class that does that while ChannelLockDiagnosticsTests is mid-assertion steals its
// captured lines, and the theft is reported as that class's failure. 31 other classes start an
// engine outside this collection and have the same effect; enrolling them all was MEASURED at
// 6m26s against a 1m35s baseline — a 4x cost on every run, refused. This one is enrolled
// because it is the thief this session added, and removing it costs nothing.
[Collection(AIOrchestratorCoreLib.Tests.Channels.CHANNEL_LOCK_COLLECTION.NAME)]
public class ANudgedMemberIsNeverRespawnedOnSilenceTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestrationLog _log;
    readonly FixedClock_Fake _clock;
    readonly IBridgeEngine _engine;
    readonly ITestOutputHelper _out;

    public ANudgedMemberIsNeverRespawnedOnSilenceTests(ITestOutputHelper output)
    {
        _out = output;

        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-noRespawn-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _log = OrchestrationLog_Factory.Create(_paths);
        _clock = new FixedClock_Fake(DateTime.UtcNow);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, configProvider, _store, _launcher, _log, null,
            EngineStateStore_Factory.Create_InMemory(), _clock,
            BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }
    }

    /// <summary>
    /// THE REGRESSION THAT MATTERS. The member is unreadable — no usage file, no session state, which
    /// is every member on a headless host — so the app knows nothing about it. It gets nudged, stays
    /// silent through the whole confirmation window, and must still be left alone: not knowing is not
    /// death, and the cost of being wrong here is a member's entire working context.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnUnreadableMemberThatStaysSilent_IsNudgedButNeverRespawned()
    {
        var (orchId, memberId, channelFile) = Start_WithADormantMember();

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Was_Nudged(channelFile)),
            $"the member was never nudged, so this test never reached the escalation it is about.\n{Read(channelFile)}");

        // PAST THE CONFIRMATION WINDOW, in one jump. This is the line the old shape made impossible.
        _clock.Advance(TimeSpan.FromMinutes(10));

        await Tick_Once_Async();
        await Tick_Once_Async();

        _out.WriteLine(Read(channelFile));

        Assert.False(
            Was_Respawned(channelFile),
            "THE MEMBER WAS RESPAWNED ON SILENCE: the app could not read it, concluded it was dead, and threw "
            + $"away its context — the 2026-09-07 defect, nineteen times over.\n{Read(channelFile)}");

        // AND THE ESCALATION WAS ACTUALLY REACHED. Without this the assertion above passes for a
        // member the loop never weighed at all — a green test proving the code was never run, which
        // is the shape this whole audit kept finding. The decider logs every outcome by name, so its
        // own words are the evidence that it decided rather than that nothing happened.
        Assert.True(
            Wait_Until(() => Log_Contains("not knowing is not death")),
            "the loop never reached the escalation, so passing above proves nothing about it.\n"
            + Read_Log());
    }

    string Read_Log()
    {
        var file = Path.Combine(_tempRoot, "orchestrator-global.log.jsonl");
        List<string> found = [];

        foreach (var candidate in Directory.EnumerateFiles(_tempRoot, "*.log.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                found.Add(File.ReadAllText(candidate));
            }
            catch (IOException)
            {
                // A log being written while we read it is not a test result.
            }
        }

        return found.Count == 0 ? $"(no log files under {_tempRoot}, expected one at {file})" : string.Join("\n", found);
    }

    bool Log_Contains(string fragment)
    {
        return Read_Log().Contains(fragment, StringComparison.Ordinal);
    }

    /// <summary>
    /// A member the app CAN read and that has recently worked is doubly protected — the escalation
    /// must not even be reached. Pinned separately so a future change that breaks the first guard
    /// cannot hide behind the second.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMemberThatIsWorking_IsNeverRespawned()
    {
        var (orchId, memberId, channelFile) = Start_WithADormantMember();

        await Tick_Once_Async();
        _clock.Advance(TimeSpan.FromMinutes(10));
        await Tick_Once_Async();
        await Tick_Once_Async();

        Assert.False(Was_Respawned(channelFile), $"a working member was respawned.\n{Read(channelFile)}");
    }

    /// <summary>
    /// AND THE OTHER DIRECTION: nudging still happens. A guard that spared everything by never
    /// running would pass the assertions above and leave a genuinely dormant member unwoken, which is
    /// the alarm the whole system rests on.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheNudgeItself_StillFires()
    {
        var (_, _, channelFile) = Start_WithADormantMember();

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Was_Nudged(channelFile)),
            $"a member dormant for 20 minutes was never nudged.\n{Read(channelFile)}");
    }

    (string OrchId, string MemberId, string ChannelFile) Start_WithADormantMember()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId;
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, memberId);

        // LOCAL TIME, because the quiet clock reads the channel's own stamps and those are local —
        // Nudge_Decider pins that, and handing it UTC on a machine east of Greenwich makes every
        // duration negative and silently stops the whole nudge system.
        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.WriteAllText(
            channelFile,
            $"## [1] FROM supervisor — {stamp} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {stamp} — WRITING WINDOW OPEN — parser\nstarting now\n");

        File.SetLastWriteTime(channelFile, DateTime.Now.AddMinutes(-20));

        return (session.OrchId, memberId, channelFile);
    }

    string Read(string channelFile)
    {
        return File.Exists(channelFile) ? File.ReadAllText(channelFile) : "(no channel file)";
    }

    bool Was_Nudged(string channelFile)
    {
        return ChannelEntry_Parser
            .Parse_All(Read(channelFile))
            .Any(entry => entry.Author == ChannelAuthors.App);
    }

    /// <summary>
    /// The respawn announces itself with a constant, which is exactly why it is safe to assert on:
    /// Nudge_Wording.RESPAWN_SUBJECT is the one string the recovery ever wrote into a member channel.
    /// </summary>
    bool Was_Respawned(string channelFile)
    {
        return ChannelEntry_Parser
            .Parse_All(Read(channelFile))
            .Any(entry => entry.Subject.Contains(Nudge_Wording.RESPAWN_SUBJECT, StringComparison.OrdinalIgnoreCase));
    }

    async Task Tick_Once_Async()
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await Task.Delay(600);
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
        for (var waited = 0; waited < 8_000; waited += 100)
        {
            if (condition())
                return true;

            Thread.Sleep(100);
        }

        return condition();
    }
}

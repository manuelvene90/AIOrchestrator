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
/// TWO CLOCKS READ A MEMBER'S CHANNEL AND ONLY ONE WAS CONVERTED. The member nudge moved onto the
/// conversation stamp; the SUPERVISOR nudge — the one that reports which members are waiting on a
/// verdict — kept reading the file's write stamp, so every app write to a member's channel silenced
/// it for the full threshold. That is the same defect
/// <see cref="NudgeAppWriteDelayProbeTests"/> pins one path over, and it survived because the fix was
/// described by the clock it changed rather than by the clocks that existed.
///
/// COMPACTION IS THE REASON THIS IS NOT MERELY A DELAY WORTH SHRUGGING AT. It rewrites a live channel
/// through a rename-over, so the file's stamp advances with NOBODY HAVING SPOKEN — no entry added, no
/// conversation moved. Under a file-stamp clock the supervisor is then told nothing is waiting, on a
/// channel where a report has been sitting unanswered for twenty minutes. `/resume` does the same
/// thing on purpose and unboundedly: it has no dedupe, so each broadcast re-silenced this path.
///
/// THE FILE STAMP IS SET TO NOW ON PURPOSE, and it is what makes the first case specific: under the
/// old behaviour that stamp reads "quiet for ~0" and no nudge can fire, so a nudge here can only have
/// come from reading the last conversation entry.
///
/// SCHEDULED AND GATED, NOT A COVERAGE GAP — and the distinction is the point. The mtime FALLBACK
/// inside <see cref="AIOrchestratorCoreLib.Status.Nudge_Decider.Measure_QuietFor"/> still fails
/// SILENT when a stamp cannot be trusted, against a docstring promising it fails noisy. It is
/// reachable and it is testable, by this very harness; it is not done here because it MUST NOT LAND
/// before the app-only sentinel is on master. Until then the fallback's 8-minute reset is the only
/// thing throttling a channel that has no conversation identity, so removing it first releases the
/// loop it is masking.
///
/// Phrased this way on purpose. An earlier draft filed the same sentence under "what this does not
/// pin", which reads as *nobody can test this* rather than *this is next, behind a gate* — and a
/// declared gap resting on an unexamined reason is what teaches the next reader to stop looking.
/// That exact confusion cost another session six commits on this repo the same evening.
/// </summary>
public class SupervisorNudgeClockProbeTests : IDisposable
{
    const string SUPERVISOR_NUDGE_SUBJECT = "unread reports waiting on you";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public SupervisorNudgeClockProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-supnudgeclock-tests-{Guid.NewGuid():N}");
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
    /// A member filed a report twenty minutes ago and nobody has answered it. Something then touched
    /// the file — a compaction, a `/resume` — leaving its write stamp at now. The supervisor is still
    /// owed that verdict and must still be told.
    /// </summary>
    [Fact]
    public async Task AFileTouchDoesNotHideAReportTheSupervisorHasOwedForTwentyMinutes()
    {
        var ownerChannel = Start_WithUnansweredReport_ThenAFileTouch();

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Has_SupervisorNudge(ownerChannel)),
            "the supervisor was never told about a report owed for twenty minutes — a write to the file, with nobody speaking, held its clock down");
    }

    /// <summary>
    /// THE OTHER DIRECTION, asserted apart and for its own reason. A clock that ignored the file
    /// entirely would satisfy the case above and also wake the supervisor for reports filed seconds
    /// ago: here the member filed one minute back, so nothing is late and nothing may fire.
    /// </summary>
    [Fact]
    public async Task AReportFiledAMinuteAgoDoesNotWakeTheSupervisor()
    {
        var ownerChannel = Start_WithReportFiledAMinuteAgo();

        await Tick_Once_Async();

        Assert.False(
            Wait_Until(() => Has_SupervisorNudge(ownerChannel)),
            "the supervisor was woken about a report filed a minute ago");
    }

    /// <summary>
    /// Twenty minutes of unanswered report, with the file stamped now as a compaction or an app write
    /// would leave it. The member spoke LAST, so the member-nudge path has nothing to do here and the
    /// only thing that can move is the supervisor's own channel.
    /// </summary>
    string Start_WithUnansweredReport_ThenAFileTouch()
    {
        var filed = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        return Start_WithMemberChannel(
            $"## [1] FROM supervisor — {filed} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {filed} — TASK 1 landed abc1234\nparser done, suite green\n");
    }

    string Start_WithReportFiledAMinuteAgo()
    {
        var briefed = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");
        var justNow = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm");

        return Start_WithMemberChannel(
            $"## [1] FROM supervisor — {briefed} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {justNow} — TASK 1 landed abc1234\nparser done, suite green\n");
    }

    /// <summary>Returns the OWNER channel — the supervisor's own, which is where its nudge lands.</summary>
    string Start_WithMemberChannel(string text)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);

        File.WriteAllText(channelFile, text);

        // The touch under test. A compaction's rename-over leaves exactly this: a file stamped now,
        // with the conversation inside it untouched and hours old.
        File.SetLastWriteTime(channelFile, DateTime.Now);

        return _paths.Get_OwnerChannelFile(session.OrchId);
    }

    /// <summary>
    /// Matched by SUBJECT rather than by counting app entries: the owner channel is a busy file —
    /// periodic status, ledger complaints and request confirmations all land there — so a count would
    /// pass on somebody else's write and pin nothing.
    /// </summary>
    static bool Has_SupervisorNudge(string ownerChannel)
    {
        if (!File.Exists(ownerChannel))
            return false;

        return ChannelEntry_Parser
            .Parse_All(File.ReadAllText(ownerChannel))
            .Any(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(SUPERVISOR_NUDGE_SUBJECT));
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

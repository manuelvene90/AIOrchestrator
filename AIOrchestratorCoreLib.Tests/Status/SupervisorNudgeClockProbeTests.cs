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
    /// THE SIGN TRAP, ISOLATED — and it exists because the two controls this file shipped with had
    /// the SAME red set (rev-7's F2). Reverting to the file stamp and handing the reader `UtcNow` both
    /// reddened the compaction case, so the second failure was only ever caught by breaking what the
    /// first breaks, and a "correct this to UtcNow" edit would have been indistinguishable from a
    /// revert. They are not the same failure: the file-stamp reading still fires after eight minutes
    /// of genuine quiet, while `UtcNow` on a machine east of UTC fires NEVER.
    ///
    /// Here the conversation and the file stamp AGREE that the channel is twenty minutes old, so the
    /// old file-stamp reading is right too and stays green — leaving `UtcNow`, whose subtraction goes
    /// negative, as the only mutation that can redden this.
    ///
    /// The author's own rule, applied to the author: a case that reddens with its siblings pins
    /// nothing of its own.
    /// </summary>
    [Fact]
    public async Task AStalledReportIsNudgedEvenWhenTheFileStampAgreesItIsOld()
    {
        var ownerChannel = Start_WithStalledReport_AndAnAgreeingFileStamp();

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Has_SupervisorNudge(ownerChannel)),
            "a plainly stalled report was not reported to the supervisor — the clock is not measuring in the direction it thinks it is");
    }

    /// <summary>
    /// AN UNDATEABLE CHANNEL IS PAST THE THRESHOLD, NOT AT ZERO — 3(a), and the case that changes.
    ///
    /// The report is stamped SIX HOURS IN THE FUTURE, which item 12 records happening twice in this
    /// orchestration's own channels in a day, so neither stamp-reading step will touch it. **And the
    /// file is stamped NOW**, as an app write or a compaction's rename-over leaves it.
    ///
    /// Before fail-noisy the clock answered "quiet for ~0" from that file and the supervisor was told
    /// nothing was waiting — a member that could not be dated read as a member hard at work, which is
    /// precisely what the rule in `Measure_QuietFor`'s docstring forbade while the line beneath it did
    /// the opposite. Now the clock returns null, null is past the threshold, and the report is
    /// reported.
    ///
    /// It is the fresh file stamp that makes this case discriminating: with an OLD one the nudge fires
    /// under both behaviours and the change is invisible.
    /// </summary>
    [Fact]
    public async Task AnUndateableChannelIsReportedRatherThanReadingAsBusy()
    {
        var ownerChannel = Start_WithReportStampedInTheFuture_AndAFreshFileStamp();

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Has_SupervisorNudge(ownerChannel)),
            "a report the clock could not date was hidden from the supervisor — the file stamp answered for it and said 'busy'");
    }

    /// <summary>
    /// THE SAME UNDATEABLE CHANNEL WITH AN OLD FILE STAMP — kept because it pins the OTHER direction:
    /// that fail-noisy did not simply make everything nudge. It passed before 3(a) and passes after,
    /// for different reasons — the file answered then, null answers now — and saying so is the point,
    /// since a test whose reason changes silently is the trap this branch has been fixing all evening.
    /// </summary>
    [Fact]
    public async Task AnUntrustworthyStampDoesNotHideTheReportWhenTheFileIsOldEither()
    {
        var ownerChannel = Start_WithReportStampedInTheFuture_AndAnOldFileStamp();

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Has_SupervisorNudge(ownerChannel)),
            "a report the clock could not date was hidden from the supervisor instead of falling back to the file");
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
            + $"## [2] FROM implementer — {filed} — TASK 1 landed abc1234\nparser done, suite green\n",
            DateTime.Now);
    }

    string Start_WithReportFiledAMinuteAgo()
    {
        var briefed = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");
        var justNow = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm");

        return Start_WithMemberChannel(
            $"## [1] FROM supervisor — {briefed} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {justNow} — TASK 1 landed abc1234\nparser done, suite green\n",
            DateTime.Now);
    }

    /// <summary>
    /// Everything old and AGREEING — the conversation twenty minutes back and the file stamp with it.
    /// Nothing has touched this channel; it is simply stalled. The point is what it does NOT
    /// discriminate: the old file-stamp reading is right here too, so this case cannot redden for the
    /// reason the other one does.
    /// </summary>
    string Start_WithStalledReport_AndAnAgreeingFileStamp()
    {
        var filed = DateTime.Now.AddMinutes(-20);

        return Start_WithMemberChannel(
            $"## [1] FROM supervisor — {filed:yyyy-MM-dd HH:mm} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {filed:yyyy-MM-dd HH:mm} — TASK 1 landed abc1234\nparser done, suite green\n",
            filed);
    }

    /// <summary>
    /// A stamp no reader will trust and a file stamped NOW — so nothing in the channel can date it and
    /// the only thing that could have answered is the one source that says "a moment ago" for reasons
    /// that have nothing to do with the member.
    /// </summary>
    string Start_WithReportStampedInTheFuture_AndAFreshFileStamp()
    {
        var briefed = DateTime.Now.AddMinutes(-20);
        var impossible = DateTime.Now.AddHours(6);

        return Start_WithMemberChannel(
            $"## [1] FROM supervisor — {briefed:yyyy-MM-dd HH:mm} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {impossible:yyyy-MM-dd HH:mm} — TASK 1 landed abc1234\nparser done, suite green\n",
            DateTime.Now);
    }

    /// <summary>
    /// A stamp no reader will trust, and a file old enough to answer instead. Six hours ahead is well
    /// past the two-minute skew tolerance, so this is refused rather than merely odd.
    /// </summary>
    string Start_WithReportStampedInTheFuture_AndAnOldFileStamp()
    {
        var briefed = DateTime.Now.AddMinutes(-20);
        var impossible = DateTime.Now.AddHours(6);

        return Start_WithMemberChannel(
            $"## [1] FROM supervisor — {briefed:yyyy-MM-dd HH:mm} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {impossible:yyyy-MM-dd HH:mm} — TASK 1 landed abc1234\nparser done, suite green\n",
            briefed);
    }

    /// <summary>Returns the OWNER channel — the supervisor's own, which is where its nudge lands.</summary>
    string Start_WithMemberChannel(string text, DateTime fileStamp)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);

        File.WriteAllText(channelFile, text);

        // The file stamp is a PARAMETER because it is the axis the cases differ on: set to now it
        // reproduces a compaction's rename-over (a stamp moved with nobody speaking), set to the
        // conversation's own age it reproduces an ordinary stall.
        File.SetLastWriteTime(channelFile, fileStamp);

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

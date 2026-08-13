using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A meeting must DEFER the app's attention traffic, never destroy it — driven through the REAL
/// engine, because the thing that goes wrong is engine state and no pure unit can be asked about it.
///
/// <para>
/// This harness is the answer to a claim I made twice in commit messages and which was wrong:
/// "nothing in the suite constructs BridgeEngineModel, so a test would have to start the loop, sleep
/// past a tick and assert through the filesystem". <c>BridgeEngine_Factory.Create</c> is PUBLIC —
/// the type being internal blocks NAMING it, not building it — and starting the loop with a token
/// cancelled immediately runs exactly one tick with no sleep, because the tick body runs to
/// completion before the loop's delay observes the cancellation. Two probe classes were already
/// doing this at the time I wrote that sentence (rev-7 P3, 2026-08-13).
/// </para>
/// </summary>
public class MeetingDefersAlertsProbeTests : IDisposable
{
    const string NudgeSubject = "unread reports waiting on you";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public MeetingDefersAlertsProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-meeting-defer-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create(_paths, configProvider, _store, _launcher, log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE STUCK TOKEN. The nudge fires once per quiet spell and remembers that it did. Entering a
    /// meeting used to bail ABOVE the line that RELEASES that memory, so a spell which genuinely
    /// ended during the meeting was never cleared — and the next spell, with a real unanswered
    /// report in it, was silently un-nudgeable.
    /// <para>
    /// It asserts the NUDGE, not where any guard sits, so it reddens for any placement that skips
    /// the release.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ASpellThatENDEDDuringAMeeting_DoesNotSilenceTheNextOne()
    {
        var (orchId, memberChannel) = Start_WithDormantMember();
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        await Tick_Once_Async();
        Assert.True(Wait_Until(() => Count_Nudges(ownerChannel) == 1), "the supervisor was never nudged for the first spell");

        // The owner walks over. Directed work continues in a meeting, so the supervisor answers the
        // outstanding report while they are talking — the spell is genuinely over.
        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Terminal);
        Answer_TheReport(memberChannel);

        await Tick_Once_Async();
        Assert.Equal(1, Count_Nudges(ownerChannel));

        // The owner leaves, and a member files something new: a NEW spell, with a real unanswered
        // report in it.
        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Remote);
        File_AFreshReport(memberChannel);

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Count_Nudges(ownerChannel) == 2),
            "the new spell was never nudged — the token from the spell that ended during the meeting was still held");
    }

    /// <summary>
    /// The other half, so the fix above cannot be "nudge regardless": while the owner IS in the
    /// meeting, the nudge stays away. Suppressing and deferring are both required.
    /// </summary>
    [Fact]
    public async Task DuringAMeeting_TheSupervisorIsNotNudgedAtAll()
    {
        var (orchId, _) = Start_WithDormantMember();
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Terminal);

        await Tick_Once_Async();

        Assert.Equal(0, Count_Nudges(ownerChannel));
    }

    /// <summary>
    /// And the deferred nudge ARRIVES afterwards rather than being lost — the owner's rule, driven
    /// end to end: suppressed while they are there, delivered once they leave.
    /// </summary>
    [Fact]
    public async Task AfterTheMeeting_TheDeferredNudgeIsDelivered()
    {
        var (orchId, _) = Start_WithDormantMember();
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Terminal);
        await Tick_Once_Async();
        Assert.Equal(0, Count_Nudges(ownerChannel));

        _store.Set_OwnerPresence(orchId, OwnerPresenceModes.Remote);
        await Tick_Once_Async();

        Assert.True(Wait_Until(() => Count_Nudges(ownerChannel) == 1), "the nudge held during the meeting never arrived");
    }

    /// <summary>A briefed member that went silent twenty minutes ago, with a report nobody answered.</summary>
    (string OrchId, string MemberChannel) Start_WithDormantMember()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberChannel = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);
        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.WriteAllText(
            memberChannel,
            $"## [1] FROM supervisor — {stamp} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {stamp} — TASK 1 done\nfiled, awaiting your verdict\n");

        File.SetLastWriteTime(memberChannel, DateTime.Now.AddMinutes(-20));

        return (session.OrchId, memberChannel);
    }

    static void Answer_TheReport(string memberChannel)
    {
        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(memberChannel, $"\n## [3] FROM supervisor — {stamp} — accepted\nverdict given\n");
        File.SetLastWriteTime(memberChannel, DateTime.Now.AddMinutes(-20));
    }

    static void File_AFreshReport(string memberChannel)
    {
        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(memberChannel, $"\n## [4] FROM implementer — {stamp} — TASK 2 done\nfiled, awaiting your verdict\n");
        File.SetLastWriteTime(memberChannel, DateTime.Now.AddMinutes(-20));
    }

    int Count_Nudges(string ownerChannel)
    {
        if (!File.Exists(ownerChannel))
            return 0;

        return ChannelEntry_Parser
            .Parse_All(File.ReadAllText(ownerChannel))
            .Count(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(NudgeSubject));
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

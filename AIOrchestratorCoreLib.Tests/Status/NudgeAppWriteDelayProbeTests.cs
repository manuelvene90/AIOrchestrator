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
/// AN APP WRITE MUST NOT DELAY THE FIRST NUDGE — the one defect the quiet clock still fixes on
/// current master, driven through the real engine because the delay is a property of the CALL.
///
/// WHY THIS ONE AND NOT THE LOOP. The self-feeding repetition this clock was originally written for
/// is already dead: <see cref="AIOrchestratorCoreLib.Status.Nudge_Decider.Identify_LastConversationEntry_OrNull"/>
/// remembers WHICH entry a member was nudged about, that memory is never cleared, and the app's own
/// writes never change the last conversation entry — so no second nudge can be issued for the same
/// unanswered thing whatever the clock says. Asserting the loop here would therefore pass with the
/// clock reverted, which is precisely how two engine-level tests in
/// <see cref="NudgeOncePerThingProbeTests"/> were written, run, refuted by their control, and deleted.
///
/// What survives that control is the FIRST nudge. Scheduled from the file's write stamp, any app
/// write landing after the unanswered entry restarts the clock — so a member that has been genuinely
/// stalled for twenty minutes is not woken because the app itself touched the file one minute ago.
///
/// THE FILE STAMP IS SET TO NOW ON PURPOSE, and it is what stops this passing for another reason: the
/// old behaviour reads "quiet for ~0" from that stamp and cannot nudge, so a nudge here can only have
/// come from reading the last CONVERSATION entry.
/// </summary>
public class NudgeAppWriteDelayProbeTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public NudgeAppWriteDelayProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-nudgeappwrite-tests-{Guid.NewGuid():N}");
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
    /// The member has owed an answer for twenty minutes. The app wrote to the channel one minute ago
    /// — a `/resume` broadcast, a malformed-header report — and that write is the most recent thing
    /// in the file. The nudge is still owed and must still fire.
    /// </summary>
    [Fact]
    public async Task AnAppWriteDoesNotHoldOffTheNudgeAMemberHasAlreadyEarned()
    {
        var channelFile = Start_WithStalledMember_ThenAnAppWrite();

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Count_AppEntries(channelFile) == 2),
            "the stalled member was never nudged — the app's own write held its clock down");
    }

    /// <summary>
    /// THE OTHER DIRECTION, asserted apart. A clock that ignored the file entirely would satisfy the
    /// case above and nudge members that genuinely have nothing owed: here the member SPOKE one
    /// minute ago, so nothing is owed and nothing may fire.
    /// </summary>
    [Fact]
    public async Task AMemberThatJustSpokeIsNotNudged()
    {
        var channelFile = Start_WithMemberWhoJustSpoke();

        await Tick_Once_Async();

        Assert.False(
            Wait_Until(() => Count_AppEntries(channelFile) > 0),
            "a member that spoke a minute ago was nudged anyway");
    }

    /// <summary>
    /// Twenty minutes of owed answer, then a FROM app entry a minute ago — and the file stamp set to
    /// now, as the app's write would leave it.
    /// </summary>
    string Start_WithStalledMember_ThenAnAppWrite()
    {
        var stalled = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");
        var justNow = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm");

        return Start_WithChannel(
            $"## [1] FROM supervisor — {stalled} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {stalled} — WRITING WINDOW OPEN — Parser.cs\nstarting now\n\n"
            + $"## [3] FROM app — {justNow} — GO AHEAD — resume\npick up where you left off\n");
    }

    string Start_WithMemberWhoJustSpoke()
    {
        var stalled = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");
        var justNow = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm");

        return Start_WithChannel(
            $"## [1] FROM supervisor — {stalled} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {justNow} — WRITING WINDOW OPEN — Parser.cs\nstarting now\n");
    }

    string Start_WithChannel(string text)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);

        File.WriteAllText(channelFile, text);

        // The app's write leaves the file stamped NOW. Under the old file-stamp clock that reads as
        // "quiet for ~0" and no nudge can fire, which is what makes the assertion above specific.
        File.SetLastWriteTime(channelFile, DateTime.Now);

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

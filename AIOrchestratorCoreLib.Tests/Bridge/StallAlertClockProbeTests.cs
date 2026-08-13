using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE OWNER'S STALL ALERT, AND THE WRITE THAT USED TO SILENCE IT WAS THE APP'S OWN (rev-7's F1).
///
/// `Send_StallAlerts_Async` asks how long an orchestration has been quiet. That question used to be
/// answered by the newest FILE stamp across the owner channel and every member channel, so anything
/// that touched a file marked the whole orchestration alive without a word being said — a compaction's
/// rename-over, a request confirmation, a `/resume`. The sharpest case is self-referential: **the
/// supervisor nudge the engine fires itself** lands in the owner channel, and that write told the
/// stall detector everything was fine. A liveness signal its own alarm resets is not a liveness
/// signal.
///
/// The fixture is exactly that shape: a member's report unanswered for forty minutes, and an app entry
/// in the owner channel stamped NOW with the owner channel's file stamped to match. Under the file
/// reading the orchestration looks like it spoke a moment ago; under the conversation reading it has
/// been silent for forty minutes and the owner is owed an alert.
///
/// It needs a Telegram client because `Send_StallAlerts_Async` returns immediately without one — so
/// this test is only possible at all through `Create_WithTelegramClient`, the seam that does not exist
/// on the branches based before it.
/// </summary>
public class StallAlertClockProbeTests : IDisposable
{
    const int OLDER_THAN_THE_STALL_THRESHOLD_MINUTES = 40;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;

    public StallAlertClockProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-stallclock-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _telegram = new FailableTelegram_Fake();
        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, store, _launcher, log, _telegram);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task TheAppsOwnWriteDoesNotConvinceTheStallDetectorTheOrchestrationIsAlive()
    {
        Start_StalledOrchestration_WithAFreshAppEntryInTheOwnerChannel();

        var alerted = await Run_UntilAsync(() => _telegram.Count_Attempts_Containing("quiet for") > 0);

        Assert.True(
            alerted,
            "no stall alert reached the owner — the app's own entry in the owner channel was read as the orchestration being alive");
    }

    void Start_StalledOrchestration_WithAFreshAppEntryInTheOwnerChannel()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        var silentSince = DateTime.Now.AddMinutes(-OLDER_THAN_THE_STALL_THRESHOLD_MINUTES);

        // The orchestration must be older than its own silence, or the "cannot be quieter than it is
        // old" floor answers the question before any channel is read.
        Age_Session(session.OrchId, silentSince);

        // EVERY member channel has to be aged, not just the one under test. An orchestration starts
        // with imp-1 AND rev-1, both seeded this instant, and the quiet span is the SHORTEST across
        // channels — so a single freshly-seeded channel with no entries in it falls to its own file
        // stamp and answers "active now" for the whole orchestration. Found by reading the engine's
        // log after this test failed with the fix already in place.
        foreach (var member in session.Members)
            File.SetLastWriteTime(_paths.Get_ImplementerChannelFile(session.OrchId, member.MemberId), silentSince);

        var memberChannel = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);

        Write_Channel(
            memberChannel,
            $"## [1] FROM supervisor — {silentSince:yyyy-MM-dd HH:mm} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {silentSince:yyyy-MM-dd HH:mm} — TASK 1 landed abc1234\nparser done\n",
            silentSince);

        // The owner channel: a real conversation forty minutes old, then the app's own entry stamped
        // NOW — the supervisor nudge — with the file stamp it would leave behind.
        Write_Channel(
            _paths.Get_OwnerChannelFile(session.OrchId),
            $"## [1] FROM supervisor — {silentSince:yyyy-MM-dd HH:mm} — starting\nbriefing imp-1\n\n"
            + $"## [2] FROM app — {DateTime.Now:yyyy-MM-dd HH:mm} — unread reports waiting on you — imp-1\nimp-1 filed entries you have not answered\n",
            DateTime.Now);
    }

    static void Write_Channel(string channelFile, string text, DateTime fileStamp)
    {
        File.WriteAllText(channelFile, text);
        File.SetLastWriteTime(channelFile, fileStamp);
    }

    /// <summary>
    /// Rewrites `createdUtc` in the session file. The store has no seam for it and the value is
    /// round-tripped as an ISO string, so this edits what the store will read back.
    /// </summary>
    void Age_Session(string orchId, DateTime createdLocal)
    {
        var sessionFile = _paths.Get_SessionFile(orchId);
        var json = File.ReadAllText(sessionFile);

        var aged = System.Text.RegularExpressions.Regex.Replace(
            json,
            "\"createdUtc\": \"[^\"]+\"",
            $"\"createdUtc\": \"{createdLocal.ToUniversalTime():O}\"");

        Assert.NotEqual(json, aged);

        File.WriteAllText(sessionFile, aged);
    }

    /// <summary>
    /// Runs the engine until the condition holds, THEN cancels — rather than cancelling immediately
    /// and inspecting afterwards. The cancel-first shape works for probes that assert on files,
    /// because the writes happen early in the tick, but the Telegram sends sit behind several awaits
    /// that observe the token: with the token already cancelled the tick unwinds before reaching them
    /// and NOTHING is ever attempted. Measured — the first draft of this test read zero attempts of
    /// any kind, which is indistinguishable from the defect it was written to catch.
    /// </summary>
    async Task<bool> Run_UntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        var satisfied = Wait_Until(condition, 15_000);

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way this loop ends.
        }

        return satisfied;
    }

    static bool Wait_Until(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 50)
        {
            if (condition())
                return true;

            Thread.Sleep(50);
        }

        return false;
    }
}

using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The rule this pins, stated once: A MEMO THAT RECORDS WORK AS DONE MUST NOT OUTLIVE A FAILED
/// ATTEMPT TO DO IT.
/// <para>
/// While a failed append THREW, several sites in the bridge were protected by a try/catch they never
/// had to think about. Making the appender return false instead removed that protection everywhere
/// at once, and the compiler, the tests and the diff were all silent about it: the catch that stopped
/// guarding is twenty lines away and UNCHANGED.
/// </para>
/// <para>
/// THIS TEST DRIVES THE REAL ENGINE. Its first draft did not — it restated the caller's logic
/// (<c>if (reported) File.Delete(marker)</c>) inside the test body and asserted on that, which is a
/// tautology: deleting the production guard left all three cases GREEN, measured. A test that
/// contains the code it is testing pins nothing, and it is the same defect as an assertion satisfied
/// by a comment. The harness here is the one <see cref="GeneralSupervision.CloseImplementerGuardProbeTests"/>
/// established — a temp root, fakes, no network, no spawning — after the same "unreachable" assumption
/// had been made about that guard and turned out to be false.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class AppendFailureDoesNotMarkWorkDoneTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public AppendFailureDoesNotMarkWorkDoneTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-memo-tests-{Guid.NewGuid():N}");
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
    /// The contract <c>Report_GuardsNotInForce</c> states in as many words — "DELETED once recorded,
    /// so the next inability is a new fact rather than a stale one. If the record cannot be written
    /// the marker STAYS, and the next tick tries again."
    /// <para>
    /// It held for free while the append threw into the surrounding catch. It has to be checked now.
    /// </para>
    /// <para>
    /// THE TWO HALVES FAIL FOR DISJOINT REASONS, which is what stops this passing for the wrong one.
    /// A marker that survives a locked channel could mean the guard works OR that the tick never ran
    /// the code at all — indistinguishable, and opposite conclusions. So the lock is then RELEASED and
    /// the marker must disappear: that can only happen if the loop was reaching this code the whole
    /// time, and it demonstrates the retried report the comment promises rather than assuming it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AReportThatCouldNotBeWritten_LeavesTheMarker_AndTheNextTickRetriesIt()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        var markerFile = Path.Combine(_paths.Get_OrchestrationFolder(session.OrchId), GuardNotInForce_Marker.FILE_NAME);
        File.WriteAllText(markerFile, "pre-write-check.sh\nwhether the target is a channel file\nfork failed");

        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);
        var lockDirectory = Hold_Locked(ownerChannel);

        using var cancellation = new CancellationTokenSource();
        var loop = _engine.Run_Async(cancellation.Token);

        try
        {
            // Long enough for several ticks. Each blocked append waits out the lock budget, so this
            // is deliberately not tight.
            await Task.Delay(TimeSpan.FromSeconds(6));

            Assert.True(
                File.Exists(markerFile),
                "the marker was deleted for a report that was never written — the record that would have retried it is gone");

            Assert.DoesNotContain(GuardNotInForce_Marker.ENTRY_SUBJECT, File.ReadAllText(ownerChannel));

            // Now prove the loop was reaching the code, and that the retry is real rather than
            // promised: with the channel free, the very next tick must record it and drop the marker.
            Directory.Delete(lockDirectory, recursive: true);

            Assert.True(
                await Wait_Until_Async(() => !File.Exists(markerFile)),
                "the marker was never cleared once the channel was free — the tick was not reaching Report_GuardsNotInForce, so the assertion above pinned nothing");

            Assert.Contains(GuardNotInForce_Marker.ENTRY_SUBJECT, File.ReadAllText(ownerChannel));
        }
        finally
        {
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
    }

    /// <summary>
    /// Holds the channel's lock the way a foreign writer would, so appends against it genuinely fail
    /// for the whole budget. Returns the directory so the test can release it.
    /// </summary>
    string Hold_Locked(string channelFile)
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session", "another-writer"));

        return lockDirectory;
    }

    static async Task<bool> Wait_Until_Async(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (condition())
                return true;

            await Task.Delay(200);
        }

        return false;
    }
}

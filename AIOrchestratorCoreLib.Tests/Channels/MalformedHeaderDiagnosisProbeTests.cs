using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The malformed-header diagnosis, driven through the REAL engine — because the thing being pinned
/// is the WIRING, and a pure test of the formatter cannot see whether the field reaches the log.
///
/// <para>
/// The bytes, the two verdicts and now the file's movement across the read are three separate
/// answers to one question that has been re-argued from scratch twice: a well-formed header was
/// reported as malformed, and every hypothesis died because the only evidence was the line's TEXT.
/// Each occurrence has to carry its own proof; a formatter that produces the proof and a call site
/// that drops it would look identical in every unit test of the formatter.
/// </para>
/// <para>
/// WHY TWO TICKS. The first sight of a file is baselined SILENTLY, on purpose — a malformed header
/// from days ago cannot be usefully re-appended, and announcing history at every startup trains the
/// owner to ignore the warning that matters. So the first tick swallows the header that is already
/// there, and only a header that appears AFTER the app has seen the file is reported.
/// </para>
/// </summary>
public class MalformedHeaderDiagnosisProbeTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public MalformedHeaderDiagnosisProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-malformed-diagnosis-{Guid.NewGuid():N}");
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
    /// The whole diagnosis has to reach the log line: the bytes, BOTH regex verdicts, and whether any
    /// writer moved the file across the read. The last one is the only field that can implicate — or
    /// clear — a concurrent writer, which is the hypothesis that outlived the other three.
    /// </summary>
    [Fact]
    public async Task AMalformedHeader_IsLoggedWithItsBYTES_ANDWhetherTheFileMovedAcrossTheRead()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        // Present at first sight, so it is baselined silently and cannot be the line under test.
        File.WriteAllText(channelFile, $"## [1] FROM supervisor — {stamp} — brief\nbody\n\n## [2b] FROM supervisor — {stamp} — history\nbody\n");

        await Tick_Once_Async();

        // Written while the app is watching: this is the one the owner is told about.
        File.AppendAllText(channelFile, $"\n## [supervisor] FROM supervisor — {stamp} — the invisible one\nbody\n");

        await Tick_Once_Async();

        var warning = Wait_For_MalformedWarning(session.OrchId);

        Assert.Contains("attempted=True", warning);
        Assert.Contains("parses=False", warning);
        Assert.Contains("len=", warning);

        // Nothing wrote to the file between the app's own read and its report, so the field must say
        // so — and say it with a real verdict, not merely be present.
        Assert.Contains("file=UNCHANGED-ACROSS-READ", warning);
    }

    string Wait_For_MalformedWarning(string orchId)
    {
        var logFile = _paths.Get_OrchestrationLogFile(orchId);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (File.Exists(logFile))
            {
                var line = File.ReadAllLines(logFile).FirstOrDefault(l => l.Contains("Malformed header"));

                if (line != null)
                    return line;
            }

            Thread.Sleep(50);
        }

        throw new Exception($"no 'Malformed header' line was ever written to {logFile}");
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
}

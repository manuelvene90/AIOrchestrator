using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// WHICH LINES THE ENGINE REPORTS, asserted through the real engine.
///
/// `Build_ReportBody` is well covered as a function, and every one of those tests passes while the
/// engine hands it `malformed` — every offending line in the file — instead of `unreported`, the ones
/// it has not already reported. That mutant compiles, runs, and was GREEN across the full suite: the
/// supervisor would be re-told about every historical malformed line on every new one, which is the
/// waterfall this repo keeps removing.
///
/// Same class as rev-7's I1 and I4: a call site choosing WHICH value to feed a tested helper, with the
/// helper's own tests structurally unable to see the choice.
///
/// The first-sight rule is load-bearing for the fixture and easy to get wrong: a channel is baselined
/// the first time it is seen WITH malformed lines (the count check precedes the baseline add), so the
/// first offender is absorbed silently as history and only the SECOND produces a report. The sequence
/// below is written around that rather than against it.
/// </summary>
public class MalformedHeaderReportProbeTests : IDisposable
{
    const string FIRST_OFFENDER = "## [8b] FROM supervisor — 2026-08-13 12:00 — absorbed as history";
    const string SECOND_OFFENDER = "## [9b] FROM supervisor — 2026-08-13 12:05 — the one worth reporting";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public MalformedHeaderReportProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-shapeprobe-tests-{Guid.NewGuid():N}");
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
    /// The report names the NEW offender and not the one already absorbed. Reddens the moment the
    /// engine passes the full set instead of the unreported one.
    ///
    /// Asserted on the report ENTRY's body rather than on the file: both offending lines are present
    /// in the file as raw text by construction, so a whole-file assertion would be satisfied by the
    /// fixture itself and pin nothing.
    /// </summary>
    [Fact]
    public async Task TheReportNamesOnlyLinesItHasNotAlreadyReported()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId;
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, memberId);

        File.WriteAllText(
            channelFile,
            $"## [1] FROM supervisor — 2026-08-13 11:00 — brief\nwork on it\n\n{FIRST_OFFENDER}\nbody of the absorbed one\n");

        await Tick_Once_Async();

        Assert.Empty(Report_Bodies(channelFile));

        File.AppendAllText(channelFile, $"\n{SECOND_OFFENDER}\nbody of the reported one\n");

        await Tick_Once_Async();

        Assert.True(Wait_Until(() => Report_Bodies(channelFile).Count == 1), "the new malformed header was never reported");

        var body = Report_Bodies(channelFile).Single();

        Assert.Contains("[9b]", body);
        Assert.DoesNotContain("[8b]", body);
    }

    IReadOnlyList<string> Report_Bodies(string channelFile)
    {
        if (!File.Exists(channelFile))
            return [];

        return ChannelEntry_Parser
            .Parse_All(File.ReadAllText(channelFile))
            .Where(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains("INVISIBLE", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Body)
            .ToList();
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

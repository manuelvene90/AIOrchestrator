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
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// WHAT THE GUARD REPORT ACTUALLY SAYS, asserted through the real engine — because the defect was not
/// in the sentence, it was in what the engine bolted onto the sentence afterwards.
///
/// `Describe_OrNull` is unit-tested and correct. The engine then appended a fixed tail claiming the
/// cause was "almost always the machine rather than the code — hooks shell out, and a machine that
/// cannot fork cannot run them", followed by "Nothing is wrong with your work". That tail was a
/// literal, derived from no reason code, printed directly beside the REAL reason it contradicted. On
/// 2026-08-13 the real reason was a scanner that could not parse a redirect target, and the alert
/// blamed the machine — so the correct diagnosis existed in the same process and was discarded at the
/// last step.
///
/// The unit tests could not see this: the wrong half was added after the unit under test returned.
/// This is "pin the CALL, not just the callee", the same argument as NudgeClockProbeTests.
/// </summary>
public class GuardReportProbeTests : IDisposable
{
    const string HOOK = "reviewer-readonly-check.sh";
    const string PREDICATE = "whether this command mutates anything";
    const string REASON = "a redirect target this scanner cannot resolve";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public GuardReportProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-guardreport-tests-{Guid.NewGuid():N}");
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
    /// The report is the marker's own sentence and nothing else.
    ///
    /// Both halves are needed and neither would do alone: the equality says what the body MUST be,
    /// and the two negatives name the specific invented claims that were there. A negative assertion
    /// on its own admits every other string in the language — an empty body would pass it.
    /// </summary>
    [Fact]
    public async Task TheReportSaysWhatTheMarkerSaidAndInventsNoCause()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);

        Write_Marker(session.OrchId, HOOK, PREDICATE, REASON);

        await Tick_Once_Async();

        Assert.True(Wait_Until(() => Find_GuardEntry_OrNull(ownerChannel) != null), "the guard marker was never reported");

        var body = Find_GuardEntry_OrNull(ownerChannel)!.Body.Trim();

        Assert.Equal(
            GuardNotInForce_Marker.Describe_OrNull($"{HOOK}\n{PREDICATE}\n{REASON}\n")!.Trim(),
            body);

        Assert.DoesNotContain("cannot fork", body);
        Assert.DoesNotContain("Nothing is wrong with your work", body);
    }

    /// <summary>
    /// The reason the equality above is not merely cosmetic: the reason the hook gave has to survive
    /// into what the reader sees, since it is the only field that says WHICH inability happened.
    /// </summary>
    [Fact]
    public async Task TheReasonTheHookGaveReachesTheReader()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);

        Write_Marker(session.OrchId, HOOK, PREDICATE, REASON);

        await Tick_Once_Async();

        Assert.True(Wait_Until(() => Find_GuardEntry_OrNull(ownerChannel) != null), "the guard marker was never reported");

        var body = Find_GuardEntry_OrNull(ownerChannel)!.Body;

        Assert.Contains(REASON, body);
        Assert.Contains(HOOK, body);
    }

    void Write_Marker(string orchId, string hook, string predicate, string reason)
    {
        File.WriteAllText(
            Path.Combine(_paths.Get_OrchestrationFolder(orchId), GuardNotInForce_Marker.FILE_NAME),
            $"{hook}\n{predicate}\n{reason}\n");
    }

    IChannelEntry? Find_GuardEntry_OrNull(string ownerChannel)
    {
        if (!File.Exists(ownerChannel))
            return null;

        return ChannelEntry_Parser
            .Parse_All(File.ReadAllText(ownerChannel))
            .FirstOrDefault(entry =>
                entry.Author == ChannelAuthors.App
                && entry.Subject.Contains(GuardNotInForce_Marker.ENTRY_SUBJECT, StringComparison.OrdinalIgnoreCase));
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

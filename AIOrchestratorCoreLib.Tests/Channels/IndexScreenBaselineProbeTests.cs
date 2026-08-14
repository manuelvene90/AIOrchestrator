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
/// THE BASELINE IS TAKEN ON SIGHT OF THE FILE, NOT ON THE FIRST OFFENCE — driven through the real
/// engine, because the defect is a statement ORDER in engine state and no pure test of the screen can
/// see it. rev-8 F1/F4: all twelve of the screen's own tests exercise the pure class, and the
/// integration — the baseline discipline, the dedupe key composed with the file path, the log call —
/// was unexercised, which is why a statement-order error survived to a reader.
///
/// <para>
/// The defect, in both sweeps: the early `continue` sat ABOVE the baseline registration, so a channel
/// was registered on the first sweep in which it HAD something to report rather than on first sight.
/// A channel clean when the app starts was therefore never registered, and its first real offence
/// arrived on a sweep that still counted as "first sight" — suppressed as history while being
/// recorded in a memo that has no release, so it could never be reported on any later sweep either.
/// For a new orchestration that is 100% of first offences, a fresh channel being clean at creation.
/// </para>
/// <para>
/// WHICH CASES DISCRIMINATE, measured rather than argued (the controls are in the commit message):
/// the two named <c>…WasCleanAtFirstSight…</c> FAIL with the registration moved back below the
/// `continue`. <c>AnOffenceALREADYInTheFileIsAbsorbedAsHistory</c> PASSES on both sides — it is the
/// invariant the fix must not break, not a guard for it, and it is named that way so the next reader
/// does not count three guards where there are two.
/// </para>
/// </summary>
public class IndexScreenBaselineProbeTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public IndexScreenBaselineProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-index-baseline-{Guid.NewGuid():N}");
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
    /// GUARD — the case the screen exists for: a quoted header appearing in a channel the app has
    /// already seen clean. This is the 2026-08-13 incident, and before the fix the screen was silent
    /// for it permanently.
    /// </summary>
    [Fact]
    public async Task ACrossingInAChannelThatWasCleanAtFirstSight_IsLogged()
    {
        var (orchId, channelFile) = Start_WithACleanChannel();

        await Tick_Once_Async();

        // The quotation: a header that PARSES, sitting inside the body of entry [3].
        File.AppendAllText(channelFile, "\n## [1] FROM supervisor — 2026-08-13 09:00 — quoted inside the entry above\n\n## [4] FROM supervisor — 2026-08-13 09:20 — next\n");

        await Tick_Once_Async();

        // NOT `Contains(fragment, Wait_For_LogLine(fragment))`, which asserts a string is present in
        // a line that was found by searching for it. The real assertion is the helper's throw; this
        // one adds something the search did not already guarantee — that the line names THE quotation
        // this test planted (rev-6 F4).
        //
        // It names the LINE and not the index pair on purpose: the engine appends its own idle-nudge
        // entry to this channel during the ticks, so the number the quotation collides with is the
        // app's, not the fixture's. Asserting the pair pinned that accident; asserting the planted
        // line pins the thing the test is actually about.
        Assert.Contains("quoted inside the entry above", Wait_For_LogLine(orchId, "index runs backwards"));
    }

    /// <summary>
    /// GUARD, the twin — the same statement order in the malformed-header sweep this screen was
    /// copied from. rev-8 checked the obvious defence ("it copies the sweep above, which has the same
    /// shape deliberately") and ruled that a latent bug in a neighbour is not a design.
    /// </summary>
    [Fact]
    public async Task AMalformedHeaderInAChannelThatWasCleanAtFirstSight_IsReported()
    {
        var (orchId, channelFile) = Start_WithACleanChannel();

        await Tick_Once_Async();

        File.AppendAllText(channelFile, "\n## [2b] FROM supervisor — 2026-08-13 09:20 — a non-numeric index\nbody\n");

        await Tick_Once_Async();

        // Same non-tautology: the search proves the line exists, this proves it names the channel the
        // owner has to go and look at AND how many entries went invisible — the two parts of that
        // message that are computed rather than constant.
        Assert.Contains($"{Path.GetFileName(channelFile)}: 1 malformed", Wait_For_LogLine(orchId, "malformed entry header"));
    }

    /// <summary>
    /// INVARIANT, not a guard: a crossing ALREADY in the file when the app first sees it stays silent.
    /// It passes on both sides of the fix and is here because something must hold it — a fix that
    /// registered nothing at all would satisfy both guards above and turn this log into the waterfall
    /// the dedupe discipline exists to prevent.
    /// </summary>
    [Fact]
    public async Task AnOffenceALREADYInTheFileIsAbsorbedAsHistory()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);

        File.WriteAllText(
            channelFile,
            "## [7] FROM supervisor — 2026-08-13 09:00 — real\n\n## [6] FROM supervisor — 2026-08-13 08:55 — quoted, already here\n\n## [8] FROM supervisor — 2026-08-13 09:10 — real\n");

        await Tick_Once_Async();
        await Tick_Once_Async();

        Assert_ATickActuallyRan();
        Assert.DoesNotContain(Read_LogLines(session.OrchId), line => line.Contains("index runs backwards"));
    }

    /// <summary>
    /// THE SAME INVARIANT FOR THE TWIN, and it is the site with the larger blast radius: the malformed
    /// sweep APPENDS A CHANNEL ENTRY and alerts the owner, where the index sweep only writes a log
    /// line. Without this, a mutation hardcoding `isFirstSight = false` at the malformed site passed
    /// the whole suite (rev-6 F3) — and what it would produce is every historical malformed header
    /// re-announced on the first sweep after every app start, with an owner alert each time. That is
    /// the owner-retraining waterfall the sweep's own comment gives as its reason for existing.
    ///
    /// The author's stated reason for keeping the index invariant — "a fix that registered nothing
    /// would satisfy both guards" — applies verbatim here, and the twin had none.
    /// </summary>
    [Fact]
    public async Task AMalformedHeaderALREADYInTheFileIsAbsorbedAsHistory()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);

        File.WriteAllText(
            channelFile,
            "## [1] FROM supervisor — 2026-08-13 09:00 — brief\nbody\n\n## [2b] FROM supervisor — 2026-08-13 09:05 — already here when the app started\nbody\n");

        await Tick_Once_Async();
        await Tick_Once_Async();

        Assert_ATickActuallyRan();
        Assert.DoesNotContain(Read_LogLines(session.OrchId), line => line.Contains("malformed entry header"));

        // And nothing was appended to the channel either — that is the half the owner would see.
        Assert.DoesNotContain("INVISIBLE", File.ReadAllText(channelFile));
    }

    /// <summary>
    /// A POSITIVE MARKER FOR THE INVARIANTS, because "no warning was logged" has two routes to green
    /// and the second one is "no tick ever ran" (rev-6 F4, decision 20). `Persist_BridgeState` is the
    /// LAST statement of the tick body and both sweeps run before it, so the state file existing is
    /// proof the pass reached its end rather than dying on an early await.
    ///
    /// The guards do not need this — their helper throws when the line never arrives — but that made
    /// the guards the only thing establishing the invariants' precondition, which is a coupling worth
    /// removing rather than documenting.
    /// </summary>
    void Assert_ATickActuallyRan()
    {
        Assert.True(
            Wait_Until(() => File.Exists(_paths.BridgeStateFile)),
            "no tick ever completed, so 'nothing was reported' proves nothing about the sweep");
    }

    static bool Wait_Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (condition())
                return true;

            Thread.Sleep(50);
        }

        return false;
    }

    (string OrchId, string ChannelFile) Start_WithACleanChannel()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);

        File.WriteAllText(
            channelFile,
            "## [1] FROM supervisor — 2026-08-13 09:00 — brief\nbody\n\n## [2] FROM implementer — 2026-08-13 09:05 — report\nbody\n\n## [3] FROM supervisor — 2026-08-13 09:10 — accepted\nbody\n");

        return (session.OrchId, channelFile);
    }

    string[] Read_LogLines(string orchId)
    {
        var logFile = _paths.Get_OrchestrationLogFile(orchId);

        return File.Exists(logFile) ? File.ReadAllLines(logFile) : [];
    }

    string Wait_For_LogLine(string orchId, string fragment)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var line = Read_LogLines(orchId).FirstOrDefault(l => l.Contains(fragment));

            if (line != null)
                return line;

            Thread.Sleep(50);
        }

        throw new Exception($"no log line containing '{fragment}' was ever written for {orchId}");
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

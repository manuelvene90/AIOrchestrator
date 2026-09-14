using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A CLOSING MEMBER'S LAST REPORT DOES NOT DISAPPEAR IN SILENCE. Closing a spoke stops it being a
/// source and keeps its cursor, so an entry the supervisor was never handed reaches nobody, ever —
/// and the member digest (2026-09-09) widened the window in which one can be sitting there from a
/// tick to five minutes. The close is not stopped and nothing is drained: what changed is that the
/// log says which entries are being left behind.
/// </summary>
public class UndeliveredSpokeTrafficReporterTests : IDisposable
{
    /// <summary>No turn is running, which is what every case here assumed before 2026-09-10.</summary>
    static readonly IReadOnlySet<string> NOTHING_IN_FLIGHT = new HashSet<string>(StringComparer.Ordinal);

    readonly string _root;
    readonly ISupervisionPaths _paths;

    public UndeliveredSpokeTrafficReporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-undelivered-spoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _paths = SupervisionPaths_Factory.Create(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void AnEntryTheSupervisorWasNeverHanded_IsNamedWithItsIndexAndSubject()
    {
        Write_Channel(("supervisor", 1, "BRIEF — port the ledger"), ("implementer", 2, "REPORT — done, pushed at abc1234"));
        Write_SupervisorCursor(highWater: 1, delivered: [Identity_Of(1)]);

        var described = UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1", NOTHING_IN_FLIGHT);

        Assert.NotNull(described);
        Assert.True(described.Value.AnythingDropped);
        Assert.Contains("1 entry", described.Value.Line);
        Assert.Contains("[2] REPORT — done, pushed at abc1234", described.Value.Line);
    }

    /// <summary>
    /// THE PRODUCTION FALSE ALARM OF 2026-09-10, WHICH IS THIS LINE'S FIRST EVER FIRING. On
    /// <c>fincanva-5</c>, <c>imp-3</c> filed entry [72] ("Done. F1 closed, origin/dev merged, all gates
    /// re-run") at 14:06:13; the digest released it at 14:11:15 and the supervisor turn started carrying
    /// it; the reporter said at 14:12:43 that the supervisor "was never handed" [72]; that turn ended in
    /// success at 14:12:58. The cursor advances only when a turn completes, so the pending set alone
    /// cannot tell a dropped entry from one being delivered — and calling this a WARNING is what the
    /// owner was about to promote into a refusal to close the member at all.
    /// </summary>
    [Fact]
    public void AnEntryTheInFlightTurnIsCarrying_IsNotCalledDropped_AndIsNotAWarning()
    {
        Write_Channel(("supervisor", 71, "REVIEW — F1"), ("implementer", 72, "Done. F1 closed, `origin/dev` merged, all gates re-run"));
        Write_SupervisorCursor(highWater: 71, delivered: [Identity_Of(71)]);

        var described = UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1", new HashSet<string>([Identity_Of(72)], StringComparer.Ordinal));

        Assert.NotNull(described);
        Assert.False(described.Value.AnythingDropped);
        Assert.Contains("carrying 1 entry", described.Value.Line);
        Assert.Contains("[72] Done. F1 closed", described.Value.Line);
        Assert.DoesNotContain("never handed", described.Value.Line);
    }

    /// <summary>
    /// AND THE IN-FLIGHT CASE IS NOT A SUPPRESSION. A turn that fails leaves its entries pending with
    /// the member already gone, so the line still names them — it names the CONDITION instead of
    /// asserting a loss that has not happened.
    /// </summary>
    [Fact]
    public void TheInFlightLine_SaysWhatHappensIfThatTurnFails()
    {
        Write_Channel(("implementer", 5, "REPORT — suite green"));
        Write_SupervisorCursor(highWater: 0, delivered: []);

        var described = UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1", new HashSet<string>([Identity_Of(5)], StringComparer.Ordinal));

        Assert.NotNull(described);
        Assert.Contains("if it fails", described.Value.Line);
    }

    /// <summary>
    /// One entry in flight and one nothing is carrying: the second is a real loss and decides the
    /// level, the first is reported beside it rather than folded into the same claim.
    /// </summary>
    [Fact]
    public void WithOneEntryInFlightAndOneNobodyIsCarrying_TheDroppedOneIsStillTheWarning()
    {
        Write_Channel(("implementer", 8, "REPORT — done"), ("implementer", 9, "QUESTION: which branch?"));
        Write_SupervisorCursor(highWater: 0, delivered: []);

        var described = UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1", new HashSet<string>([Identity_Of(8)], StringComparer.Ordinal));

        Assert.NotNull(described);
        Assert.True(described.Value.AnythingDropped);
        Assert.Contains("never handed", described.Value.Line);
        Assert.Contains("[9] QUESTION: which branch?", described.Value.Line);
        Assert.Contains("carrying 1 entry more", described.Value.Line);
        Assert.Contains("[8] REPORT — done", described.Value.Line);
    }

    [Fact]
    public void WhenTheSupervisorHasBeenHandedEverything_ThereIsNothingToSay()
    {
        Write_Channel(("supervisor", 1, "BRIEF — port the ledger"));
        Write_SupervisorCursor(highWater: 1, delivered: [Identity_Of(1)]);

        Assert.Null(UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1", NOTHING_IN_FLIGHT));
    }

    [Fact]
    public void WithNoBridgeDrivenSupervisor_ItSaysNothing_BecauseNobodyWasGoingToBeHandedAnything()
    {
        Write_Channel(("implementer", 1, "REPORT — done"));

        Assert.Null(UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1", NOTHING_IN_FLIGHT));
    }

    [Fact]
    public void WithNoChannelFileAtAll_ItSaysNothing_RatherThanThrowingInsideAClose()
    {
        Write_SupervisorCursor(highWater: 0, delivered: []);

        Assert.Null(UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1", NOTHING_IN_FLIGHT));
    }

    /// <summary>
    /// THE LEVEL IS THE WHOLE POINT OF THE SPLIT, AND NOTHING TESTED IT. Review finding 2026-09-10:
    /// every other case here calls <c>Describe_Pending_OrNull</c> directly, so inverting the <c>if</c>
    /// in <c>Log_BeforeClosing</c> — sending a real loss to INFO and a routine in-flight close to
    /// WARNING — left all of them green. That is decision 20's rule: a test that cannot fail pins
    /// nothing.
    /// </summary>
    [Fact]
    public void ADroppedEntryIsAWarning_AndAnEntryInFlightIsNot()
    {
        Write_Channel(("implementer", 3, "REPORT — done"));
        Write_SupervisorCursor(highWater: 0, delivered: []);

        var whenNothingIsCarryingIt = new LevelRecordingLog_Fake();
        UndeliveredSpokeTraffic_Reporter.Log_BeforeClosing(_paths, whenNothingIsCarryingIt, "repo-1", "imp-1", NOTHING_IN_FLIGHT);

        Assert.Contains("WARNING", whenNothingIsCarryingIt.Lines.Single());
        Assert.Contains("never handed", whenNothingIsCarryingIt.Lines.Single());

        var whenATurnIsCarryingIt = new LevelRecordingLog_Fake();
        UndeliveredSpokeTraffic_Reporter.Log_BeforeClosing(_paths, whenATurnIsCarryingIt, "repo-1", "imp-1", new HashSet<string>([Identity_Of(3)], StringComparer.Ordinal));

        Assert.Contains("INFO", whenATurnIsCarryingIt.Lines.Single());
        Assert.DoesNotContain("never handed", whenATurnIsCarryingIt.Lines.Single());
    }

    /// <summary>Nothing is pending, so the close says nothing at all — at any level.</summary>
    [Fact]
    public void WithNothingPending_TheCloseIsSilent()
    {
        Write_Channel(("supervisor", 1, "BRIEF — port the ledger"));
        Write_SupervisorCursor(highWater: 1, delivered: [Identity_Of(1)]);

        var log = new LevelRecordingLog_Fake();
        UndeliveredSpokeTraffic_Reporter.Log_BeforeClosing(_paths, log, "repo-1", "imp-1", NOTHING_IN_FLIGHT);

        Assert.Empty(log.Lines);
    }

    void Write_Channel(params (string Author, int Index, string Subject)[] entries)
    {
        var file = _paths.Get_ImplementerChannelFile("repo-1", "imp-1");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Join("\n\n", entries.Select(entry =>
            $"## [{entry.Index}] FROM {entry.Author} — 2026-09-10 11:0{entry.Index % 10} — {entry.Subject}\n\nbody {entry.Index}")) + "\n");
    }

    void Write_SupervisorCursor(int highWater, IReadOnlyList<string> delivered)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        var cursor = TurnCursor_Factory.Create("imp-1", _paths.Get_ImplementerChannelFile("repo-1", "imp-1"), highWater, new HashSet<string>(delivered, StringComparer.Ordinal));
        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID, _root, null, _paths.Get_OwnerChannelFile("repo-1"), [cursor]);

        PrintSessionState_Store.Write(stateFile, state);
    }

    /// <summary>Records the LEVEL as well as the text, because the level is what the split decides.</summary>
    sealed class LevelRecordingLog_Fake : IOrchestrationLog
    {
        public List<string> Lines { get; } = [];

        public void Log_Info(string orchId, string message) => Lines.Add($"INFO {message}");
        public void Log_Warning(string orchId, string message) => Lines.Add($"WARNING {message}");
        public void Log_Error(string orchId, string message, Exception? exception) => Lines.Add($"ERROR {message}");

        public event Action<IOrchestrationLogEntry>? EntryLogged;
    }

    /// <summary>The identity the cursor stores for an entry — the digest, never the agent-written index (decision 12).</summary>
    string Identity_Of(int index)
    {
        var file = _paths.Get_ImplementerChannelFile("repo-1", "imp-1");
        var entry = ChannelEntry_Parser.Parse_All(File.ReadAllText(file)).First(candidate => candidate.Index == index);

        return ChannelEntry_Digest.Compute(entry);
    }
}

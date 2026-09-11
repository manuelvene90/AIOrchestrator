using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
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

        var line = UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1");

        Assert.NotNull(line);
        Assert.Contains("1 entry", line);
        Assert.Contains("[2] REPORT — done, pushed at abc1234", line);
    }

    [Fact]
    public void WhenTheSupervisorHasBeenHandedEverything_ThereIsNothingToSay()
    {
        Write_Channel(("supervisor", 1, "BRIEF — port the ledger"));
        Write_SupervisorCursor(highWater: 1, delivered: [Identity_Of(1)]);

        Assert.Null(UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1"));
    }

    [Fact]
    public void WithNoBridgeDrivenSupervisor_ItSaysNothing_BecauseNobodyWasGoingToBeHandedAnything()
    {
        Write_Channel(("implementer", 1, "REPORT — done"));

        Assert.Null(UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1"));
    }

    [Fact]
    public void WithNoChannelFileAtAll_ItSaysNothing_RatherThanThrowingInsideAClose()
    {
        Write_SupervisorCursor(highWater: 0, delivered: []);

        Assert.Null(UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(_paths, "repo-1", "imp-1"));
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

    /// <summary>The identity the cursor stores for an entry — the digest, never the agent-written index (decision 12).</summary>
    string Identity_Of(int index)
    {
        var file = _paths.Get_ImplementerChannelFile("repo-1", "imp-1");
        var entry = ChannelEntry_Parser.Parse_All(File.ReadAllText(file)).First(candidate => candidate.Index == index);

        return ChannelEntry_Digest.Compute(entry);
    }
}

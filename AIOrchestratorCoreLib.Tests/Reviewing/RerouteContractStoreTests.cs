using AIOrchestratorCoreLib.Reviewing;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE CONTRACT OUTLIVES THE PROCESS, because the round does: the supervisor declares it in one turn
/// and the implementer files its fix minutes or hours later, across app restarts. And ABSENT IS THE
/// COMMON CASE — an orchestration that never declares one must pay nothing for this feature, which
/// is why every read of a missing file is an empty list and never an error.
/// </summary>
public class RerouteContractStoreTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"aiorch-reroute-{Guid.NewGuid():N}");
    readonly ISupervisionPaths _paths;

    public RerouteContractStoreTests()
    {
        Directory.CreateDirectory(_root);
        _paths = SupervisionPaths_Factory.Create(_root);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_root);
        GC.SuppressFinalize(this);
    }

    static IRerouteContract Declared(string id = "c1")
    {
        return RerouteContract_Factory.Create_Declared(
            id: id, orchId: "repo-1", implementerId: "imp-1", reviewerId: "rev-1",
            baseCommit: "abc1234", brief: "F1 and F3 only. The delta is the fix, not the branch.",
            declaredUtc: new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AMissingFileReadsAsNoContracts()
    {
        Assert.Empty(RerouteContract_Store.Read_Open(_paths, "repo-1"));
    }

    [Fact]
    public void ACorruptFileReadsAsNoContractsRatherThanThrowing()
    {
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder("repo-1"));
        File.WriteAllText(RerouteContract_Store.Get_File(_paths, "repo-1"), "{not json");

        Assert.Empty(RerouteContract_Store.Read_Open(_paths, "repo-1"));
    }

    /// <summary>
    /// A CONTRACT THAT IS MISSING A FIELD IS NOT A CONTRACT. It is read out of a file the app itself
    /// wrote, but an older shape of that file — or one a human edited — must not become a contract
    /// with an empty reviewer id, which would route a relay into a channel path built from "".
    /// The row is dropped; the ones beside it survive.
    /// </summary>
    [Fact]
    public void ARowMissingAFieldIsDroppedAndTheOthersSurvive()
    {
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder("repo-1"));
        File.WriteAllText(
            RerouteContract_Store.Get_File(_paths, "repo-1"),
            """
            {"contracts":[
              {"id":"broken","orchId":"repo-1","implementerId":"imp-1","baseCommit":"abc1234","brief":"x","declaredUtc":"2026-09-15T10:00:00Z","state":"Declared"},
              {"id":"c2","orchId":"repo-1","implementerId":"imp-2","reviewerId":"rev-1","baseCommit":"abc1234","brief":"x","declaredUtc":"2026-09-15T10:00:00Z","state":"Declared"}
            ]}
            """);

        Assert.Equal("c2", Assert.Single(RerouteContract_Store.Read_Open(_paths, "repo-1")).Id);
    }

    [Fact]
    public void ADeclaredContractRoundTripsWithTheSupervisorsWordsIntact()
    {
        RerouteContract_Store.Write_Open(_paths, "repo-1", [Declared()]);

        var read = Assert.Single(RerouteContract_Store.Read_Open(_paths, "repo-1"));

        Assert.Equal("imp-1", read.ImplementerId);
        Assert.Equal("rev-1", read.ReviewerId);
        Assert.Equal("abc1234", read.BaseCommit);
        Assert.Equal("F1 and F3 only. The delta is the fix, not the branch.", read.Brief);
        Assert.Equal(RerouteStates.Declared, read.State);
        Assert.Null(read.ReportIdentity);
        Assert.Null(read.HeadCommit);
        Assert.Null(read.RoutedUtc);
        Assert.Equal(new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), read.DeclaredUtc);
        Assert.Equal(DateTimeKind.Utc, read.DeclaredUtc.Kind);
    }

    [Fact]
    public void ARoutedContractCarriesTheReportItWasSatisfiedBy()
    {
        var routed = RerouteContract_Factory.CreateFrom_Routed(
            Declared(), reportIdentity: "9f2a1c", headCommit: "def5678",
            routedUtc: new DateTime(2026, 9, 15, 10, 30, 0, DateTimeKind.Utc));

        RerouteContract_Store.Write_Open(_paths, "repo-1", [routed]);
        var read = Assert.Single(RerouteContract_Store.Read_Open(_paths, "repo-1"));

        Assert.Equal(RerouteStates.Routed, read.State);
        Assert.Equal("9f2a1c", read.ReportIdentity);
        Assert.Equal("def5678", read.HeadCommit);
        Assert.Equal("abc1234", read.BaseCommit);
        Assert.Equal(new DateTime(2026, 9, 15, 10, 30, 0, DateTimeKind.Utc), read.RoutedUtc);
        // THE SUPERVISOR'S WORDS SURVIVE THE TRANSITION. CreateFrom_Routed is the only way a contract
        // reaches Routed precisely so the brief and the base cannot be dropped on the way — the relay
        // written in Task 6 is made of them.
        Assert.Equal("F1 and F3 only. The delta is the fix, not the branch.", read.Brief);
        Assert.Equal("rev-1", read.ReviewerId);
        Assert.Equal(new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), read.DeclaredUtc);
    }

    /// <summary>
    /// CLOSING IS REMOVING. A contract left in the file is a hold left armed, and the hold is what
    /// keeps a supervisor from being woken — the one failure mode of this feature that is silent.
    /// </summary>
    [Fact]
    public void WritingAShorterListRemovesTheRest()
    {
        RerouteContract_Store.Write_Open(_paths, "repo-1", [Declared("c1"), Declared("c2")]);
        RerouteContract_Store.Write_Open(_paths, "repo-1", [Declared("c2")]);

        Assert.Equal("c2", Assert.Single(RerouteContract_Store.Read_Open(_paths, "repo-1")).Id);
    }

    /// <summary>
    /// AN EMPTY LIST IS WRITTEN, NOT SKIPPED — the last contract closing must actually disarm the
    /// hold on disk, not leave the previous file standing.
    /// </summary>
    [Fact]
    public void WritingAnEmptyListClosesTheLastContract()
    {
        RerouteContract_Store.Write_Open(_paths, "repo-1", [Declared("c1")]);
        RerouteContract_Store.Write_Open(_paths, "repo-1", []);

        Assert.Empty(RerouteContract_Store.Read_Open(_paths, "repo-1"));
    }

    /// <summary>
    /// TWO ORCHESTRATIONS DO NOT SHARE A WORKING SET. The file sits in the orchestration folder, so a
    /// relay armed in one repo can never be satisfied by a report filed in another.
    /// </summary>
    [Fact]
    public void ContractsAreKeptPerOrchestration()
    {
        RerouteContract_Store.Write_Open(_paths, "repo-1", [Declared("c1")]);

        Assert.Empty(RerouteContract_Store.Read_Open(_paths, "repo-2"));
        Assert.NotEqual(
            RerouteContract_Store.Get_File(_paths, "repo-1"),
            RerouteContract_Store.Get_File(_paths, "repo-2"));
    }

    /// <summary>
    /// A CONTRACT NEEDS EVERY FIELD IT ROUTES ON. The factory is the gate: an empty reviewer id or an
    /// empty base commit reaching the file would arm a hold that can never be satisfied and a relay
    /// addressed to nobody.
    /// </summary>
    [Theory]
    [InlineData("", "imp-1", "rev-1", "abc1234")]
    [InlineData("c1", "", "rev-1", "abc1234")]
    [InlineData("c1", "imp-1", "", "abc1234")]
    [InlineData("c1", "imp-1", "rev-1", "")]
    public void AContractMissingAnIdentifierIsRefusedAtTheFactory(string id, string implementerId, string reviewerId, string baseCommit)
    {
        Assert.Throws<ArgumentException>(() => RerouteContract_Factory.Create_Declared(
            id: id, orchId: "repo-1", implementerId: implementerId, reviewerId: reviewerId,
            baseCommit: baseCommit, brief: "x", declaredUtc: DateTime.UtcNow));
    }
}

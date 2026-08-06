using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

public class OrchestrationSessionStoreTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;

    public OrchestrationSessionStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-store-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Create_Orchestration_SeedsOwnerChannelAndPersists()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");

        Assert.True(File.Exists(_paths.Get_OwnerChannelFile("arb-fix")));

        var loaded = _store.Get_Session("arb-fix");
        Assert.Equal("Arb Studio", loaded.RepoName);
        Assert.Equal(@"C:\repos\arb", loaded.RepoPath);
        Assert.Empty(loaded.Members);
        Assert.Null(loaded.ClosedUtc);
    }

    [Fact]
    public void Create_Orchestration_DuplicateId_Throws()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");

        Assert.Throws<Exception>(() => _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb"));
    }

    [Fact]
    public void Add_Implementer_AllocatesSequentialIdsAndSeedsChannels()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");

        var afterFirst = _store.Add_Implementer("arb-fix");
        var afterSecond = _store.Add_Implementer("arb-fix");

        Assert.Equal("imp-1", afterFirst.Members[0].MemberId);
        Assert.Equal(2, afterSecond.Members.Count);
        Assert.Equal("imp-2", afterSecond.Members[1].MemberId);
        Assert.True(File.Exists(_paths.Get_ImplementerChannelFile("arb-fix", "imp-1")));
        Assert.True(File.Exists(_paths.Get_ImplementerChannelFile("arb-fix", "imp-2")));
    }

    [Fact]
    public void Set_TelegramTopicId_RoundTripsAndIsFindable()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");

        _store.Set_TelegramTopicId("arb-fix", 77);

        Assert.Equal(77, _store.Get_Session("arb-fix").TelegramTopicId);
        var found = _store.Find_ByTelegramTopicId_OrNull(77)
            ?? throw new Exception("Session with topic 77 should be findable");
        Assert.Equal("arb-fix", found.OrchId);
        Assert.Null(_store.Find_ByTelegramTopicId_OrNull(999));
    }

    [Fact]
    public void Close_Member_MarksOnlyThatMemberClosed()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");
        _store.Add_Implementer("arb-fix");
        _store.Add_Implementer("arb-fix");

        _store.Close_Member("arb-fix", "imp-1");

        var session = _store.Get_Session("arb-fix");
        Assert.NotNull(session.Members[0].ClosedUtc);
        Assert.Null(session.Members[1].ClosedUtc);
    }

    [Fact]
    public void Set_DisplayName_RoundTrips()
    {
        _store.Create_Orchestration("crm-2", "CRM", @"C:\repos\crm");

        _store.Set_DisplayName("crm-2", "CRM invoice crash");

        Assert.Equal("CRM invoice crash", _store.Get_Session("crm-2").DisplayName);
    }

    [Fact]
    public void Set_SupervisorPid_StampsSpawnTime_TheWatchdogGraceSource()
    {
        _store.Create_Orchestration("crm-2", "CRM", @"C:\repos\crm");
        Assert.Null(_store.Get_Session("crm-2").SupervisorSpawnedUtc);

        _store.Set_SupervisorPid("crm-2", 1234);

        var stamped = _store.Get_Session("crm-2").SupervisorSpawnedUtc
            ?? throw new Exception("SupervisorSpawnedUtc should be stamped by Set_SupervisorPid");
        Assert.True((DateTime.UtcNow - stamped).TotalSeconds < 30);
    }

    [Fact]
    public void Close_Orchestration_SetsClosedUtc()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");

        _store.Close_Orchestration("arb-fix");

        Assert.NotNull(_store.Get_Session("arb-fix").ClosedUtc);
    }

    [Fact]
    public void Load_All_ListsEverySessionFolder()
    {
        _store.Create_Orchestration("one", "Repo One", @"C:\repos\one");
        _store.Create_Orchestration("two", "Repo Two", @"C:\repos\two");

        var all = _store.Load_All();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.OrchId == "one");
        Assert.Contains(all, s => s.OrchId == "two");
    }

    [Fact]
    public void Set_MemberPid_UnknownMember_ThrowsNamingTheRoster()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");
        _store.Add_Implementer("arb-fix");

        var ex = Assert.Throws<Exception>(() => _store.Set_MemberPid("arb-fix", "imp-9", 123));

        Assert.Contains("imp-9", ex.Message);
        Assert.Contains("imp-1", ex.Message);
    }
}

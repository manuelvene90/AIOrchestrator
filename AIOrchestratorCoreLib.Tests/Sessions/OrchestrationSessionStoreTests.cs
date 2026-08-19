using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
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

    /// <summary>
    /// A SECOND store over the same folder — the app after a restart. Asserting on `_store` alone
    /// would pass on an in-memory copy and prove nothing about what reached session.json, which is
    /// the only thing that survives the restart these fields exist for.
    /// </summary>
    IOrchestrationSessionStore Reload()
    {
        return OrchestrationSessionStore_Factory.Create(SupervisionPaths_Factory.Create(_tempRoot));
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
    public void Set_TelegramMode_RoundTripsEveryMode_AndLeavesEverythingElseIntact()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");
        _store.Add_Implementer("arb-fix");
        _store.Set_DisplayName("arb-fix", "drift guard");
        _store.Set_SupervisorModelOverride("arb-fix", "fable");

        Assert.Equal(TelegramDeliveryModes.Normal, _store.Get_Session("arb-fix").TelegramMode);

        _store.Set_TelegramMode("arb-fix", TelegramDeliveryModes.Silenced);

        var silenced = _store.Get_Session("arb-fix");
        Assert.Equal(TelegramDeliveryModes.Silenced, silenced.TelegramMode);

        // The copy-with-overrides path must not drop neighbouring fields.
        Assert.Equal("drift guard", silenced.DisplayName);
        Assert.Equal("fable", silenced.SupervisorModelOverride);
        Assert.Single(silenced.Members);

        _store.Set_TelegramMode("arb-fix", TelegramDeliveryModes.Deferred);
        Assert.Equal(TelegramDeliveryModes.Deferred, _store.Get_Session("arb-fix").TelegramMode);

        _store.Set_TelegramMode("arb-fix", TelegramDeliveryModes.Normal);
        Assert.Equal(TelegramDeliveryModes.Normal, _store.Get_Session("arb-fix").TelegramMode);
    }

    [Fact]
    public void Set_OwnerPresence_SurvivesAReload_AndDoesNotDisturbTheDeliveryMode()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");
        _store.Set_DisplayName("arb-fix", "drift guard");
        _store.Set_TelegramMode("arb-fix", TelegramDeliveryModes.Deferred);

        Assert.Equal(OwnerPresenceModes.Remote, _store.Get_Session("arb-fix").OwnerPresence);

        _store.Set_OwnerPresence("arb-fix", OwnerPresenceModes.Terminal);

        // Persisted, because an app restart does not move the owner out of their chair — and a
        // presence lost on restart re-wedges the supervisor at the worst possible moment.
        var reloaded = OrchestrationSessionStore_Factory.Create(_paths).Get_Session("arb-fix");
        Assert.Equal(OwnerPresenceModes.Terminal, reloaded.OwnerPresence);

        // Orthogonal: the delivery mode the owner chose is still exactly what they chose.
        Assert.Equal(TelegramDeliveryModes.Deferred, reloaded.TelegramMode);
        Assert.Equal("drift guard", reloaded.DisplayName);

        _store.Set_OwnerPresence("arb-fix", OwnerPresenceModes.Remote);
        Assert.Equal(OwnerPresenceModes.Remote, _store.Get_Session("arb-fix").OwnerPresence);
    }

    [Fact]
    public void Get_Session_SessionWrittenBeforePresenceExisted_IsRemote()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");

        var sessionFile = _paths.Get_SessionFile("arb-fix");

        // Asserted before removing it: without this the test would pass just as well if the field
        // were never written at all, and would then be pinning nothing.
        Assert.Contains("\"ownerPresence\"", File.ReadAllText(sessionFile), StringComparison.Ordinal);

        var withoutPresence = File.ReadAllLines(sessionFile)
            .Where(line => !line.Contains("\"ownerPresence\"", StringComparison.Ordinal));

        File.WriteAllLines(sessionFile, withoutPresence);

        // A missing key must never read as "the owner is at the terminal": that would suppress the
        // awaiting-answer flag for every orchestration written before this field existed.
        Assert.Equal(OwnerPresenceModes.Remote, _store.Get_Session("arb-fix").OwnerPresence);
    }

    [Fact]
    public void Get_Session_LegacySilencedBoolean_IsReadAsSilenced()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");

        // A session.json written before the three-state mode existed.
        var sessionFile = _paths.Get_SessionFile("arb-fix");
        File.WriteAllText(sessionFile, File.ReadAllText(sessionFile).Replace("\"telegramMode\": \"Normal\"", "\"telegramSilenced\": true"));

        Assert.Equal(TelegramDeliveryModes.Silenced, _store.Get_Session("arb-fix").TelegramMode);
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

    /// <summary>
    /// The status message id had NO test at all — a grep across the test project returned zero files
    /// — while being the one thing that must survive a restart. If the serializer stopped writing
    /// that line the suite stayed green while the feature's whole premise was dead: every restart
    /// would post a second status message into every topic, which is the defect it exists to prevent.
    /// </summary>
    [Fact]
    public void Set_StatusLineMessageId_SurvivesAReload()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:
eposrb");

        _store.Set_StatusLineMessageId("arb-fix", 4242);

        Assert.Equal(4242, _store.Get_Session("arb-fix").StatusLineMessageId);
        Assert.Equal(4242, Reload().Get_Session("arb-fix").StatusLineMessageId);
    }

    /// <summary>
    /// And it must be CLEARABLE, which `?? existing` silently prevented: /clear tears the topic down
    /// and recreates it, so a stale id points at a message that no longer exists and the
    /// orchestration would never get a status line again. Null cannot mean both "unchanged" and
    /// "cleared" — the factory's own docstring says so, and the compiler accepted the wrong one.
    /// </summary>
    [Fact]
    public void Clear_StatusLineMessageId_ActuallyClearsIt()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:
eposrb");
        _store.Set_StatusLineMessageId("arb-fix", 4242);

        _store.Clear_StatusLineMessageId("arb-fix");

        Assert.Null(_store.Get_Session("arb-fix").StatusLineMessageId);
        Assert.Null(Reload().Get_Session("arb-fix").StatusLineMessageId);
    }

    /// <summary>
    /// Setting an UNRELATED field must not disturb it. The id goes in as the fifteenth positional
    /// argument to a call with an optional-defaulted parameter — the exact shape the copy-with-
    /// overrides docstring was written about, where "a newly added field silently got dropped".
    /// </summary>
    [Fact]
    public void AnUnrelatedMutationDoesNotDropTheStatusLineMessageId()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:
eposrb");
        _store.Set_StatusLineMessageId("arb-fix", 4242);

        _store.Set_TelegramTopicId("arb-fix", 77);
        _store.Add_Implementer("arb-fix");

        Assert.Equal(4242, Reload().Get_Session("arb-fix").StatusLineMessageId);
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

    /// <summary>
    /// THE MARK IS A REMINDER, so it must survive a restart — the owner mutes a finished endeavour
    /// and later needs to know which of the muted ones still owe them a test (2026-08-19). A flag
    /// that lived only in memory would be gone by the time it was needed.
    /// </summary>
    [Fact]
    public void Set_AwaitingTest_SurvivesAReload_AndDoesNotDisturbItsNeighbours()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:eposrb");
        _store.Set_DisplayName("arb-fix", "drift guard");
        _store.Set_TelegramMode("arb-fix", TelegramDeliveryModes.Silenced);

        Assert.False(_store.Get_Session("arb-fix").AwaitingTest);

        _store.Set_AwaitingTest("arb-fix", true);

        var marked = _store.Get_Session("arb-fix");

        Assert.True(marked.AwaitingTest);

        // The copy-with-overrides path must not drop neighbouring fields — the defect that shape
        // exists to prevent, and the reason a plain bool here carries an explicit wasSet flag.
        Assert.Equal("drift guard", marked.DisplayName);
        Assert.Equal(TelegramDeliveryModes.Silenced, marked.TelegramMode);

        _store.Set_AwaitingTest("arb-fix", false);

        Assert.False(_store.Get_Session("arb-fix").AwaitingTest);
        Assert.Equal(TelegramDeliveryModes.Silenced, _store.Get_Session("arb-fix").TelegramMode);
    }

    /// <summary>
    /// Every session written before 2026-08-19 has no such field, and absence must read as false —
    /// an orchestration nobody ever marked is not awaiting a test.
    /// </summary>
    [Fact]
    public void ASessionWrittenBeforeTheFlagExistedIsNotAwaitingATest()
    {
        _store.Create_Orchestration("arb-fix", "Arb Studio", @"C:eposrb");

        Assert.False(_store.Get_Session("arb-fix").AwaitingTest);
    }
}

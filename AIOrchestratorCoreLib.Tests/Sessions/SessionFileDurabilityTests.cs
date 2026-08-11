using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// session.json is the one file in an orchestration that NOTHING regenerates. It holds the repo
/// path, the member roster with their pids, the Telegram topic id, the model overrides and
/// ClosedUtc — so losing it does not degrade an orchestration, it erases one: gone from the card
/// list, invisible to the watchdog that would respawn it, its processes stranded beyond
/// Kill_OrchestrationSessions' reach, its topic id lost.
///
/// It is also written at the worst possible moment. Closing an orchestration saves this file
/// immediately before tree-killing the very processes whose pids it records, on the UI thread.
/// </summary>
public class SessionFileDurabilityTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;

    public SessionFileDurabilityTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-sessiondurability-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// A write that fails must not destroy the file it was updating (commit 3a0f8a2's rule). Under
    /// the old truncate-then-write, this is the instant the orchestration disappeared.
    /// </summary>
    [Fact]
    public void AFailedSave_LeavesTheExistingSessionFileIntact()
    {
        _store.Create_Orchestration("crm-2", "CRM", @"C:\repos\crm");

        var sessionFile = _paths.Get_SessionFile("crm-2");
        var original = File.ReadAllText(sessionFile);

        Assert.Contains("crm-2", original);

        // Readable but not deletable: the write reaches the rename and is refused there — exactly
        // where the old approach had already truncated the original.
        using (new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<Exception>(() => _store.Set_TelegramTopicId("crm-2", 4242));
        }

        Assert.Equal(original, File.ReadAllText(sessionFile));

        // And it is still a session, not a fragment — the store can read it back.
        var reloaded = _store.Get_Session_OrNull("crm-2");

        Assert.NotNull(reloaded);
        Assert.Equal(@"C:\repos\crm", reloaded.RepoPath);
    }

    /// <summary>No temp file is left behind to be mistaken for state, on the failure path either.</summary>
    [Fact]
    public void AFailedSave_LeavesNoTemporaryFileBehind()
    {
        _store.Create_Orchestration("crm-2", "CRM", @"C:\repos\crm");

        var sessionFile = _paths.Get_SessionFile("crm-2");

        using (new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<Exception>(() => _store.Set_TelegramTopicId("crm-2", 4242));
        }

        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(sessionFile) ?? _tempRoot,
            $"*{AIOrchestratorCoreLib.Storage.Atomic_FileWriter.TEMP_FILE_SUFFIX}"));
    }

    /// <summary>The ordinary path still works — durability must not cost correctness.</summary>
    [Fact]
    public void AnOrdinarySave_IsReadBackWhole()
    {
        _store.Create_Orchestration("crm-2", "CRM", @"C:\repos\crm");
        _store.Set_TelegramTopicId("crm-2", 4242);

        var reloaded = _store.Get_Session_OrNull("crm-2");

        Assert.NotNull(reloaded);
        Assert.Equal(4242, reloaded.TelegramTopicId);
        Assert.Equal("CRM", reloaded.RepoName);
    }
}

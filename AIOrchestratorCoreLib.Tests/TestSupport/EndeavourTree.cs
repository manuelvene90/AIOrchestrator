using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// A SUPERVISION TREE WITH A REAL STORE and nothing running — the fixture the Task 10 derived-file tests
/// share (plan 2026-09-23), so the sync, digest and step classes do not each copy a constructor. No engine,
/// no launcher, no git: the derived files are a function of session.json, PLAN.md and two channel files,
/// and that is all this builds.
/// </summary>
internal sealed class EndeavourTree : IDisposable
{
    public const string REPO_NAME = "AIOrchestrator";

    readonly string _root = Path.Combine(Path.GetTempPath(), $"aiorch-endeavour-{Guid.NewGuid():N}");

    public ISupervisionPaths Paths { get; }
    public IOrchestrationSessionStore Store { get; }
    public string RepoPath { get; }

    public EndeavourTree()
    {
        Paths = SupervisionPaths_Factory.Create(Path.Combine(_root, "supervision"));
        RepoPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(RepoPath);
        Store = OrchestrationSessionStore_Factory.Create(Paths);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_root);
    }

    /// <summary>
    /// An open solo orchestration named <paramref name="name"/>, linked to <paramref name="endeavourId"/>
    /// when one is given, working in its own folder under the fixture root.
    /// </summary>
    public IOrchestrationSession Add_Solo(string orchId, string name, string? endeavourId, bool paused = false)
    {
        Store.Create_Orchestration(orchId, REPO_NAME, RepoPath);
        Store.Set_DisplayName(orchId, name);

        if (endeavourId != null)
        {
            var worktree = Path.Combine(_root, "trees", orchId);
            Directory.CreateDirectory(worktree);
            Store.Set_SiblingLink(orchId, endeavourId, endeavourId, $"{endeavourId}#1", worktree);
        }

        if (paused)
            Store.Set_Paused(orchId, true);

        return Store.Get_Session(orchId);
    }

    public IReadOnlyList<IOrchestrationSession> Sessions()
    {
        return Store.Load_All();
    }

    public void Append_Entry(string channelFilePath, ChannelAuthors author, string subject, string body)
    {
        var appended = author switch
        {
            ChannelAuthors.Owner => ChannelAppender.Append_OwnerEntry(channelFilePath, body, DateTime.Now),
            ChannelAuthors.App => ChannelAppender.Append_AppEntry(channelFilePath, AppEntryAudiences.Agent, subject, body, DateTime.Now),
            _ => ChannelAppender.Append_SessionEntry(channelFilePath, author, subject, body, DateTime.Now),
        };

        Assert.True(appended, $"'{channelFilePath}' stayed locked, so the fixture could not write the entry the test is about.");
    }
}

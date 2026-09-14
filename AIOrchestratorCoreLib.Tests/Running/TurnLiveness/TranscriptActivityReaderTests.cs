using AIOrchestratorCoreLib.Running.TurnLiveness;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.TurnLiveness;

/// <summary>
/// The silence brake kills a turn only when it shows no sign of life, and the transcript is the
/// strongest sign it has. These pin the three things a wrong reader would get wrong in the
/// dangerous direction: a busy sub-agent behind a quiet parent must still count, a session found in
/// two project folders reads as its newest copy, and a session id is never a path.
/// </summary>
public class TranscriptActivityReaderTests : IDisposable
{
    const string SESSION_ID = "3f2c9a4e-1b7d-4c55-9e0a-6d8b2f1c7a90";
    const string OTHER_SESSION_ID = "8a1e5c3b-2d4f-4a6b-8c9d-0e1f2a3b4c5d";

    static readonly DateTime OLDER = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
    static readonly DateTime NEWER = new(2026, 9, 11, 10, 5, 0, DateTimeKind.Utc);
    static readonly DateTime NEWEST = new(2026, 9, 11, 10, 9, 0, DateTimeKind.Utc);

    readonly string _claudeHome;
    readonly string _projects;

    public TranscriptActivityReaderTests()
    {
        _claudeHome = Path.Combine(Path.GetTempPath(), $"aiorch-transcript-activity-{Guid.NewGuid():N}");
        _projects = Path.Combine(_claudeHome, TranscriptActivity_Reader.PROJECTS_FOLDER);
        Directory.CreateDirectory(_claudeHome);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_claudeHome);
    }

    [Fact]
    public void Read_NoProjectsFolder_ReturnsNull()
    {
        Assert.Null(TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Fact]
    public void Read_ProjectsFolderWithoutTheSession_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_projects, "-home-orch-repo"));

        Assert.Null(TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Fact]
    public void Read_OnlyTheMainTranscript_ReturnsItsWriteTime()
    {
        Write_File(Path.Combine(_projects, "-home-orch-repo", SESSION_ID + ".jsonl"), OLDER);

        Assert.Equal<DateTime?>(OLDER, TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Fact]
    public void Read_SubagentWrittenAfterTheParent_ReturnsTheSubagentsTime()
    {
        // The case the brake exists to get right: the parent is blocked on a sub-agent and silent,
        // the sub-agent is working.
        var project = Path.Combine(_projects, "-home-orch-repo");
        Write_File(Path.Combine(project, SESSION_ID + ".jsonl"), OLDER);
        Write_File(Path.Combine(project, SESSION_ID, "subagents", "agent-a1.jsonl"), NEWER);

        Assert.Equal<DateTime?>(NEWER, TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Fact]
    public void Read_SubagentNestedDeeperUnderTheSessionFolder_IsStillFound()
    {
        var project = Path.Combine(_projects, "-home-orch-repo");
        Write_File(Path.Combine(project, SESSION_ID + ".jsonl"), OLDER);
        Write_File(Path.Combine(project, SESSION_ID, "subagents", "agent-a1", "nested", "agent-a2.jsonl"), NEWEST);

        Assert.Equal<DateTime?>(NEWEST, TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Fact]
    public void Read_SubagentWithoutAMainTranscript_IsStillFound()
    {
        Write_File(Path.Combine(_projects, "-home-orch-repo", SESSION_ID, "subagents", "agent-a1.jsonl"), NEWER);

        Assert.Equal<DateTime?>(NEWER, TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Fact]
    public void Read_SameSessionInTwoProjectFolders_ReturnsTheNewestOfAll()
    {
        // A resumed session whose working directory changed lands under a second slug.
        Write_File(Path.Combine(_projects, "-home-orch-repo", SESSION_ID + ".jsonl"), NEWEST);
        Write_File(Path.Combine(_projects, "-home-orch-repo-worktree", SESSION_ID + ".jsonl"), OLDER);
        Write_File(Path.Combine(_projects, "-home-orch-repo-worktree", SESSION_ID, "subagents", "agent-a1.jsonl"), NEWER);

        Assert.Equal<DateTime?>(NEWEST, TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Fact]
    public void Read_AnotherSessionsFiles_NeverCount()
    {
        var project = Path.Combine(_projects, "-home-orch-repo");
        Write_File(Path.Combine(project, SESSION_ID + ".jsonl"), OLDER);
        Write_File(Path.Combine(project, OTHER_SESSION_ID + ".jsonl"), NEWEST);
        Write_File(Path.Combine(project, OTHER_SESSION_ID, "subagents", "agent-b1.jsonl"), NEWEST);

        Assert.Equal<DateTime?>(OLDER, TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Fact]
    public void Read_AnotherSessionOnly_ReturnsNull()
    {
        Write_File(Path.Combine(_projects, "-home-orch-repo", OTHER_SESSION_ID + ".jsonl"), NEWEST);

        Assert.Null(TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, SESSION_ID));
    }

    [Theory]
    [InlineData("../" + SESSION_ID)]
    [InlineData("../../" + SESSION_ID)]
    [InlineData("-home-orch-repo/" + SESSION_ID)]
    [InlineData("*")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData(" " + SESSION_ID)]
    [InlineData("{" + SESSION_ID + "}")]
    public void Read_SessionIdThatIsNotAGuid_ReturnsNullWithoutBuildingAPath(string sessionId)
    {
        // Bait for every one of them: a real transcript a traversal or a wildcard WOULD reach if the
        // id were ever combined into a path — in the home beside projects/ (reached by "../../"),
        // directly in projects/ (reached by "../"), and in a project folder (reached by "*").
        Write_File(Path.Combine(_claudeHome, SESSION_ID + ".jsonl"), NEWEST);
        Write_File(Path.Combine(_projects, SESSION_ID + ".jsonl"), NEWEST);
        Write_File(Path.Combine(_projects, "-home-orch-repo", SESSION_ID + ".jsonl"), NEWEST);

        var reading = TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, sessionId);

        Assert.Null(reading);
    }

    [Fact]
    public void Read_NullSessionId_ReturnsNull()
    {
        Write_File(Path.Combine(_projects, "-home-orch-repo", SESSION_ID + ".jsonl"), NEWEST);

        Assert.Null(TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(_claudeHome, null!));
    }

    [Fact]
    public void Read_BlankClaudeHome_ReturnsNull()
    {
        Assert.Null(TranscriptActivity_Reader.Read_LastWriteUtc_OrNull("  ", SESSION_ID));
    }

    [Fact]
    public void Resolve_ClaudeHome_OverrideSet_ReturnsTheOverride()
    {
        var overrides = new Dictionary<string, string> { [TranscriptActivity_Reader.CLAUDE_CONFIG_DIR_ENV] = _claudeHome };

        Assert.Equal(_claudeHome, TranscriptActivity_Reader.Resolve_ClaudeHome(overrides));
    }

    [Fact]
    public void Resolve_ClaudeHome_BlankOverride_FallsBackToTheInheritedValue()
    {
        var overrides = new Dictionary<string, string> { [TranscriptActivity_Reader.CLAUDE_CONFIG_DIR_ENV] = "   " };

        // The process environment is read, not set: mutating a real variable would race every other
        // test in the run. The expected value is therefore whatever this process inherited.
        var inherited = Environment.GetEnvironmentVariable(TranscriptActivity_Reader.CLAUDE_CONFIG_DIR_ENV);
        var expected = string.IsNullOrWhiteSpace(inherited)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : inherited;

        Assert.Equal(expected, TranscriptActivity_Reader.Resolve_ClaudeHome(overrides));
        Assert.Equal(expected, TranscriptActivity_Reader.Resolve_ClaudeHome(null));
    }

    /// <summary>The loop detector reads the main agent's file only — never a sub-agent's, never another session's.</summary>
    [Fact]
    public void Find_MainTranscript_IsTheSessionsOwnFile_TheNewestCopy_AndNeverASubAgentsOrAnotherSessions()
    {
        var older = Path.Combine(_projects, "-repo-a", SESSION_ID + ".jsonl");
        var newer = Path.Combine(_projects, "-repo-b", SESSION_ID + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(older)!);
        Directory.CreateDirectory(Path.Combine(_projects, "-repo-b", SESSION_ID, "subagents"));
        Write_File(older, OLDER);
        Write_File(newer, NEWER);
        Write_File(Path.Combine(_projects, "-repo-b", SESSION_ID, "subagents", "agent-1.jsonl"), NEWEST);
        Write_File(Path.Combine(_projects, "-repo-a", OTHER_SESSION_ID + ".jsonl"), NEWEST);

        Assert.Equal(newer, TranscriptActivity_Reader.Find_MainTranscript_OrNull(_claudeHome, SESSION_ID));
        Assert.Null(TranscriptActivity_Reader.Find_MainTranscript_OrNull(_claudeHome, "../" + SESSION_ID));
        Assert.Null(TranscriptActivity_Reader.Find_MainTranscript_OrNull(_claudeHome, "8b2e6d4c-3e5f-4b7c-9d0e-1f2a3b4c5d6e"));
    }

    static void Write_File(string path, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}\n");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }
}

using System.Diagnostics;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

public class StatePackInputsReaderTests : IDisposable
{
    readonly string _root;
    readonly ISupervisionPaths _paths;

    public StatePackInputsReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-pack-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _paths = SupervisionPaths_Factory.Create(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Read_ForAMember_FindsTheBriefAcrossTheArchive_ItsLastEntry_AndItsLedgerLines()
    {
        var channel = _paths.Get_ImplementerChannelFile("repo-1", "imp-1");
        Directory.CreateDirectory(Path.GetDirectoryName(channel)!);
        File.WriteAllText(Channel_Compactor.Build_ArchiveFilePath(channel), Entries(("supervisor", 1, "BRIEF — port the ledger"), ("implementer", 2, "imp-1 online")));
        File.WriteAllText(channel, Entries(("implementer", 3, "Committed abc1234"), ("supervisor", 4, "Accepted"), ("app", 5, "[agent] turn_ended")));
        File.WriteAllText(_paths.Get_PlanFile("repo-1"), "# PLAN\n- [>] port the ledger (imp-1)\n- [ ] something for imp-2\n\n## PARKED\n- [ ] not mine\n");

        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Implementer, "repo-1", "imp-1", Path.Combine(_root, "not-a-repo"), null, channel, []);
        var inputs = StatePackInputs_Reader.Read(_paths, state, "repo-1/imp-1/6", [], [new Source("imp-1", channel)]);

        Assert.Equal(1, inputs.Brief!.Index);
        Assert.Equal(3, inputs.LastOwnEntry!.Index);
        Assert.Equal(["- [>] port the ledger (imp-1)"], inputs.LedgerLines);
        Assert.Null(inputs.PlanText);
        Assert.Empty(inputs.OwnerTail);
        Assert.Contains(inputs.Unavailable, line => line.StartsWith("git:", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_ForTheSupervisor_TakesTheWholePlanAndTheOwnerTail_NoBrief()
    {
        var owner = _paths.Get_OwnerChannelFile("repo-1");
        Directory.CreateDirectory(Path.GetDirectoryName(owner)!);
        File.WriteAllText(owner, Entries(("owner", 1, "hi"), ("supervisor", 2, "supervisor online"), ("owner", 3, "do X"), ("supervisor", 4, "BRIEF — sent to imp-1"), ("owner", 5, "and Y"), ("supervisor", 6, "ok"), ("owner", 7, "how far?")));
        File.WriteAllText(_paths.Get_PlanFile("repo-1"), "# PLAN\n- [x] a\n- [ ] b\n");

        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Supervisor, "repo-1", "sup", Path.Combine(_root, "not-a-repo"), null, owner, []);
        var inputs = StatePackInputs_Reader.Read(_paths, state, "repo-1/sup/9", [], [new Source("owner", owner)]);

        Assert.Null(inputs.Brief);
        Assert.Equal(6, inputs.LastOwnEntry!.Index);
        Assert.Equal("# PLAN\n- [x] a\n- [ ] b\n", inputs.PlanText);
        Assert.Equal([3, 4, 5, 6, 7], inputs.OwnerTail.Select(entry => entry.Index));
    }

    [Fact]
    public void Read_NamesAMissingPlan_AndNeverThrows_OnAnEmptyRoot()
    {
        var channel = _paths.Get_ImplementerChannelFile("repo-1", "rev-1");
        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Reviewer, "repo-1", "rev-1", Path.Combine(_root, "missing"), null, channel, []);

        var inputs = StatePackInputs_Reader.Read(_paths, state, "repo-1/rev-1/1", [], [new Source("rev-1", channel)]);

        Assert.Null(inputs.Brief);
        Assert.Null(inputs.LastOwnEntry);
        Assert.Contains("PLAN.md: not found", inputs.Unavailable);
        Assert.Contains(inputs.Unavailable, line => line.StartsWith("git:", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_DescribesARealRepository_BranchDirtyAndLastCommits()
    {
        var repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init -q -b main");
        Git(repo, "-c user.email=t@t -c user.name=t commit -q --allow-empty -m first");
        File.WriteAllText(Path.Combine(repo, "dirty.txt"), "x");

        var channel = _paths.Get_ImplementerChannelFile("repo-1", "imp-1");
        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Implementer, "repo-1", "imp-1", repo, null, channel, []);

        var inputs = StatePackInputs_Reader.Read(_paths, state, "repo-1/imp-1/1", [], [new Source("imp-1", channel)]);

        var line = Assert.Single(inputs.GitLines);
        Assert.Contains("@ main:", line);
        Assert.Contains("1 dirty", line);
        Assert.Contains("first", line);
        Assert.DoesNotContain(inputs.Unavailable, l => l.StartsWith("git:", StringComparison.Ordinal));
    }

    [Fact]
    public void Locator_PutsThePackBesideWhatEachRoleReadsAtBoot()
    {
        Assert.Equal(Path.Combine(_root, "repo-1", "imp-2", "pack.md"), StatePack_Locator.Get_File(_paths, SessionRoles.Implementer, "repo-1", "imp-2"));
        Assert.Equal(Path.Combine(_root, "repo-1", "solo-1", "pack.md"), StatePack_Locator.Get_File(_paths, SessionRoles.Solo, "repo-1", "solo-1"));
        Assert.Equal(Path.Combine(_root, "repo-1", ".supervisor.pack.md"), StatePack_Locator.Get_File(_paths, SessionRoles.Supervisor, "repo-1", "sup"));
        Assert.Equal(Path.Combine(_root, "general", "pack.md"), StatePack_Locator.Get_File(_paths, SessionRoles.General, "general", "general"));
    }

    [Fact]
    public void Writer_CreatesTheFolderAndReplacesThePack()
    {
        var file = Path.Combine(_root, "repo-1", "imp-9", "pack.md");

        StatePack_Writer.Write(file, "first");
        StatePack_Writer.Write(file, "second");

        Assert.Equal("second", File.ReadAllText(file));
    }

    static string Entries(params (string Author, int Index, string Subject)[] entries)
    {
        return string.Join("\n\n", entries.Select(entry => $"## [{entry.Index}] FROM {entry.Author} — 2026-09-09 10:0{entry.Index % 10} — {entry.Subject}\n\nbody {entry.Index}")) + "\n";
    }

    static void Git(string repo, string arguments)
    {
        var process = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.WaitForExit(10_000);
        Assert.Equal(0, process.ExitCode);
    }

    sealed class Source(string key, string path) : AIOrchestratorCoreLib.Running.TurnSource.ITurnSource
    {
        public string Key => key;
        public string ChannelFilePath => path;
        public bool IsOwnerChannel => key == "owner";
    }

    [Fact]
    public void Read_ForAMember_PicksUpTheProgressNoteBesideItsPack()
    {
        var channel = _paths.Get_ImplementerChannelFile("repo-1", "imp-1");
        Directory.CreateDirectory(Path.GetDirectoryName(channel)!);
        File.WriteAllText(channel, Entries(("supervisor", 1, "BRIEF — port the ledger")));
        var note = StatePack_Locator.Get_ProgressFile_OrNull(_paths, SessionRoles.Implementer, "repo-1", "imp-1")!;
        File.WriteAllText(note, "- ledger ported (abc1234)\n- next: the parser\n");

        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Implementer, "repo-1", "imp-1", Path.Combine(_root, "not-a-repo"), null, channel, []);
        var inputs = StatePackInputs_Reader.Read(_paths, state, "repo-1/imp-1/6", [], [new Source("imp-1", channel)]);

        Assert.Equal("- ledger ported (abc1234)\n- next: the parser", inputs.ProgressNote);
        Assert.Equal(Path.GetDirectoryName(StatePack_Locator.Get_File(_paths, SessionRoles.Implementer, "repo-1", "imp-1")), Path.GetDirectoryName(note));
    }

    [Fact]
    public void Read_WithNoProgressNote_LeavesItNull_AndTheSupervisorHasNone()
    {
        Assert.Null(StatePack_Locator.Get_ProgressFile_OrNull(_paths, SessionRoles.Supervisor, "repo-1", "sup"));
        Assert.Null(StatePack_Locator.Get_ProgressFile_OrNull(_paths, SessionRoles.General, "general", "general"));
    }

    /// <summary>
    /// A NEW BRIEF STARTS A NEW NOTE (review finding, 2026-09-11): the old task's note is archived, not
    /// handed to a member starting a different task as "resume from here" — and ordinary traffic,
    /// which is not a new task, leaves the note alone.
    /// </summary>
    [Fact]
    public void Archive_ProgressNote_OnANewBrief_MovesTheOldNoteAside_AndOrdinaryTrafficDoesNot()
    {
        var channel = _paths.Get_ImplementerChannelFile("repo-1", "imp-1");
        Directory.CreateDirectory(Path.GetDirectoryName(channel)!);
        var note = StatePack_Locator.Get_ProgressFile_OrNull(_paths, SessionRoles.Implementer, "repo-1", "imp-1")!;
        var archive = Path.Combine(Path.GetDirectoryName(note)!, StatePack_Locator.PROGRESS_ARCHIVE_FILE_NAME);
        File.WriteAllText(note, "- task A: parser done (abc1234)\n");

        File.WriteAllText(channel, Entries(("supervisor", 1, "Accepted — carry on")));
        StatePack_Locator.Archive_ProgressNote_IfNewTask(_paths, SessionRoles.Implementer, "repo-1", "imp-1", ChannelEntry_Parser.Parse_All(File.ReadAllText(channel)));
        Assert.True(File.Exists(note));

        File.WriteAllText(channel, Entries(("supervisor", 2, "BRIEF — task B")));
        StatePack_Locator.Archive_ProgressNote_IfNewTask(_paths, SessionRoles.Implementer, "repo-1", "imp-1", ChannelEntry_Parser.Parse_All(File.ReadAllText(channel)));

        Assert.False(File.Exists(note));
        Assert.Contains("- task A: parser done (abc1234)", File.ReadAllText(archive));
    }
}

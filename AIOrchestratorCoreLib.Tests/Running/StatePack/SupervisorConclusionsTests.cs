using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// THE ONE THING A FRESH SESSION CANNOT RE-DERIVE. Every other section of the state pack is a FACT
/// read from disk — the brief is a channel entry, the ledger is PLAN.md, the code state is git — so
/// throwing a transcript away costs none of them. A CONCLUSION is on disk nowhere: *"we tried that
/// route on the 13th and it cannot work"* was reasoned once, acted on, and never written down. A
/// fresh supervisor re-proposes the dead end, an implementer spends a day on it again, and nothing
/// errors — which is worse than a crash, because a crash is reported.
///
/// <para>
/// And until 2026-09-15 the supervisor had nowhere to put one: <c>Get_ProgressFile_OrNull</c> returns
/// null for <see cref="SessionRoles.Supervisor"/>, which has no member folder at all.
/// </para>
/// </summary>
public class SupervisorConclusionsTests : IDisposable
{
    readonly string _root;
    readonly ISupervisionPaths _paths;

    public SupervisorConclusionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-conclusions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _paths = SupervisionPaths_Factory.Create(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void The_supervisor_conclusions_file_sits_beside_its_pack()
    {
        Assert.Equal(
            Path.Combine(_paths.Get_OrchestrationFolder("repo-1"), StatePack_Locator.SUPERVISOR_CONCLUSIONS_FILE_NAME),
            StatePack_Locator.Get_ConclusionsFile_OrNull(_paths, SessionRoles.Supervisor, "repo-1", "supervisor"));
    }

    /// <summary>
    /// A MEMBER'S IS THE C1.2 FILE, at the path the 2026-09-08 token-efficiency spec names. One
    /// accessor for both halves, so when that plan ships the two meet at one path and one reader
    /// rather than growing a second convention.
    /// </summary>
    [Fact]
    public void A_member_conclusions_file_is_the_C1_2_state_file()
    {
        Assert.Equal(
            Path.Combine(_paths.Get_OrchestrationFolder("repo-1"), "imp-1", StatePack_Locator.CONCLUSIONS_FILE_NAME),
            StatePack_Locator.Get_ConclusionsFile_OrNull(_paths, SessionRoles.Implementer, "repo-1", "imp-1"));
    }

    /// <summary>The general keeps its own CLAUDE.md as memory and is stateless across launches (decision 8).</summary>
    [Fact]
    public void The_general_supervisor_has_none()
    {
        Assert.Null(StatePack_Locator.Get_ConclusionsFile_OrNull(_paths, SessionRoles.General, "general", "general"));
    }

    [Fact]
    public void The_reader_picks_the_conclusions_up_off_disk()
    {
        var file = StatePack_Locator.Get_ConclusionsFile_OrNull(_paths, SessionRoles.Supervisor, "repo-1", "supervisor")!;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "dead ends:\n- the fence route (2026-09-13): the tailer re-anchors first, it cannot work\n");

        var channel = _paths.Get_OwnerChannelFile("repo-1");
        Directory.CreateDirectory(Path.GetDirectoryName(channel)!);
        File.WriteAllText(channel, "## [1] FROM owner — 2026-09-15 10:00 — how is it going\n\n?\n");

        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Supervisor, "repo-1", "supervisor", Path.Combine(_root, "not-a-repo"), null, channel, []);
        var inputs = StatePackInputs_Reader.Read(_paths, state, "repo-1/supervisor/2", [], [new Source("owner", channel)]);

        Assert.Contains("the tailer re-anchors first", inputs.Conclusions);
    }

    /// <summary>An absent file is an ordinary state — the supervisor has not written one yet — not an error.</summary>
    [Fact]
    public void No_file_reads_as_null_and_is_not_named_unavailable()
    {
        var channel = _paths.Get_OwnerChannelFile("repo-1");
        Directory.CreateDirectory(Path.GetDirectoryName(channel)!);
        File.WriteAllText(channel, "## [1] FROM owner — 2026-09-15 10:00 — how is it going\n\n?\n");

        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Supervisor, "repo-1", "supervisor", Path.Combine(_root, "not-a-repo"), null, channel, []);
        var inputs = StatePackInputs_Reader.Read(_paths, state, "repo-1/supervisor/2", [], [new Source("owner", channel)]);

        Assert.Null(inputs.Conclusions);
        Assert.DoesNotContain(inputs.Unavailable, line => line.Contains(StatePack_Locator.CONCLUSIONS_FILE_NAME, StringComparison.Ordinal));
    }

    [Fact]
    public void The_pack_carries_the_conclusions_before_the_pending_entries()
    {
        var pack = Build("dead ends:\n- the fence route (2026-09-13): the tailer re-anchors first, it cannot work");

        Assert.Contains(StatePack_Builder.CONCLUSIONS_HEADING, pack);
        Assert.Contains("the tailer re-anchors first", pack);

        // ORDER IS PINNED, cache-first (token-efficiency spec C2): stable material before the trigger.
        // The conclusions change about once a day; the entries change every turn.
        Assert.True(
            pack.IndexOf(StatePack_Builder.CONCLUSIONS_HEADING, StringComparison.Ordinal)
            < pack.IndexOf(StatePack_Builder.PENDING_HEADING, StringComparison.Ordinal));
    }

    /// <summary>
    /// THE HEAD SURVIVES, NOT THE TAIL — the opposite of the progress note, deliberately. A dead end
    /// is written once and stays true for ever, so recency does not make it more relevant; and the
    /// OLDEST conclusions are the ones nothing else on disk still remembers, while the newest are
    /// still half-visible in the last few channel entries. The overflow is said out loud so the
    /// supervisor can compact its own file — it is the only reader who can judge which dead end is
    /// still live, and the app must not choose for it.
    /// </summary>
    [Fact]
    public void An_outgrown_conclusions_file_keeps_its_oldest_lines_and_says_it_was_cut()
    {
        var pack = Build("OLDEST DEAD END\n" + new string('x', StatePack_Builder.CONCLUSIONS_CAP) + "\nNEWEST DEAD END");

        Assert.Contains("OLDEST DEAD END", pack);
        Assert.DoesNotContain("NEWEST DEAD END", pack);
        Assert.Contains("characters truncated by the bridge", pack);
    }

    [Fact]
    public void Without_conclusions_there_is_no_section()
    {
        Assert.DoesNotContain(StatePack_Builder.CONCLUSIONS_HEADING, Build(null));
    }

    static string Build(string? conclusions)
    {
        var source = new Source("owner", "/x/owner-channel.md");
        var woke = Entry(9, ChannelAuthors.Owner, "how is it going", "?");

        return StatePack_Builder.Build(new StatePackInputs(
            "repo-1", "supervisor", SessionRoles.Supervisor, "repo-1/supervisor/4",
            [new PendingEntry(source, woke)], [source],
            brief: null, lastOwnEntry: null, ledgerLines: [], planText: "# PLAN\n- [ ] open",
            gitLines: [], ownerTail: [], unavailable: [], progressNote: null, conclusions: conclusions));
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string body)
    {
        var word = ChannelAuthor_Words.Get_Word(author);
        return ChannelEntry_Factory.Create(index, author, "2026-09-15 10:00", subject, body,
            $"## [{index}] FROM {word} — 2026-09-15 10:00 — {subject}\n\n{body}");
    }

    sealed class Source(string key, string path) : ITurnSource
    {
        public string Key => key;
        public string ChannelFilePath => path;
        public bool IsOwnerChannel => key == "owner";
    }
}

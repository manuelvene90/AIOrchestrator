using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.Running.TurnSource;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

public class StatePackBuilderTests
{
    [Fact]
    public void Build_ForAMember_PutsStableSectionsFirstAndThePendingEntriesLast_Verbatim()
    {
        var brief = Entry(3, ChannelAuthors.Supervisor, "BRIEF — port the ledger", "do this and that");
        var last = Entry(7, ChannelAuthors.Implementer, "Committed abc1234", "214 tests green");
        var woke = Entry(9, ChannelAuthors.Supervisor, "One more thing", "also fix the date");
        var source = new StubSource("imp-1", "/x/imp-1/channel.md", false);

        var pack = StatePack_Builder.Build(Inputs(brief, last, [woke], [source], ledger: ["- [>] port the ledger (imp-1)"], git: ["dev @ /repo: clean, 3 commits: a, b, c"]));

        Assert.StartsWith(StatePack_Builder.TITLE_PREFIX + "imp-1 of repo-1 — turn repo-1/imp-1/4", pack);
        Assert.Contains(StatePack_Builder.OPENING, pack);
        Assert.Contains(woke.RawText, pack);
        Assert.Contains(brief.RawText, pack);
        Assert.Contains(last.RawText, pack);
        Assert.Contains("- [>] port the ledger (imp-1)", pack);
        Assert.Contains("dev @ /repo: clean", pack);

        var order = new[] { "## Your contract", "## Your brief", "## Your last report", "## Your ledger lines", "## Code state", StatePack_Builder.PENDING_HEADING }
            .Select(heading => pack.IndexOf(heading, StringComparison.Ordinal)).ToList();
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i), order);

        // The single-source contract, word for word the one a resumed turn gets.
        Assert.Contains(PrintTurnPrompt_Builder.Describe_Contract([source]), pack);
        Assert.DoesNotContain(StatePack_Builder.UNAVAILABLE_HEADING, pack);
    }

    [Fact]
    public void Build_TruncatesALongBriefWithAVisibleMarker_ButNeverThePendingEntries()
    {
        var brief = Entry(3, ChannelAuthors.Supervisor, "BRIEF — huge", new string('b', StatePack_Builder.BRIEF_CAP + 500));
        var woke = Entry(9, ChannelAuthors.Supervisor, "big pending", new string('p', 50_000));
        var source = new StubSource("imp-1", "/x/imp-1/channel.md", false);

        var pack = StatePack_Builder.Build(Inputs(brief, null, [woke], [source]));

        Assert.Contains("characters truncated by the bridge — read entry [3] in your channel", pack);
        Assert.Contains(woke.RawText, pack);
    }

    [Fact]
    public void Build_ForTheSupervisor_CarriesTheWholePlanAndTheOwnerTail_AndTheMultiSourceContract()
    {
        var owner = new StubSource("owner", "/x/owner-channel.md", true);
        var imp1 = new StubSource("imp-1", "/x/imp-1/channel.md", false);
        var ownerEntry = Entry(40, ChannelAuthors.Owner, "how is it going", "?");
        var woke = Entry(12, ChannelAuthors.Implementer, "Committed abc1234", "report");

        var pack = StatePack_Builder.Build(Inputs(null, null, [woke], [owner, imp1], plan: "# PLAN\n- [x] done\n- [ ] open", ownerTail: [ownerEntry], role: SessionRoles.Supervisor, memberId: "sup"));

        Assert.Contains("## The ledger — PLAN.md", pack);
        Assert.Contains("- [ ] open", pack);
        Assert.Contains("## Owner channel — the last 1 entries", pack);
        Assert.Contains(ownerEntry.RawText, pack);
        Assert.Contains(TurnReply_Splitter.TO_MARKER, pack);
        Assert.Contains("--- from imp-1 (channel.md) ---", pack);
    }

    [Fact]
    public void Build_NamesEverySectionTheReaderCouldNotFill()
    {
        var source = new StubSource("imp-1", "/x/imp-1/channel.md", false);
        var woke = Entry(1, ChannelAuthors.Supervisor, "BRIEF — first", "start");

        var pack = StatePack_Builder.Build(Inputs(woke, null, [woke], [source], unavailable: ["PLAN.md: file not found", "git: not a repository"]));

        Assert.Contains(StatePack_Builder.UNAVAILABLE_HEADING, pack);
        Assert.Contains("- PLAN.md: file not found", pack);
        Assert.Contains("- git: not a repository", pack);
        Assert.DoesNotContain("## Your ledger lines", pack);
        Assert.DoesNotContain("## Code state", pack);
    }

    static StatePackInputs Inputs(IChannelEntry? brief, IChannelEntry? last, IReadOnlyList<IChannelEntry> woke, IReadOnlyList<ITurnSource> sources,
        IReadOnlyList<string>? ledger = null, string? plan = null, IReadOnlyList<string>? git = null, IReadOnlyList<IChannelEntry>? ownerTail = null,
        IReadOnlyList<string>? unavailable = null, SessionRoles role = SessionRoles.Implementer, string memberId = "imp-1", string? progress = null)
    {
        var pending = woke.Select(entry => new PendingEntry(sources.Last(), entry)).ToList();
        return new StatePackInputs("repo-1", memberId, role, $"repo-1/{memberId}/4", pending, sources, brief, last, ledger ?? [], plan, git ?? [], ownerTail ?? [], unavailable ?? [], progress);
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string body)
    {
        var word = ChannelAuthor_Words.Get_Word(author);
        var raw = $"## [{index}] FROM {word} — 2026-09-09 10:00 — {subject}\n\n{body}";
        return ChannelEntry_Factory.Create(index, author, "2026-09-09 10:00", subject, body, raw);
    }

    sealed class StubSource(string key, string path, bool isOwner) : ITurnSource
    {
        public string Key => key;
        public string ChannelFilePath => path;
        public bool IsOwnerChannel => isOwner;
    }

    /// <summary>
    /// A CUT TURN RESUMES FROM ITS OWN NOTE. The note is the member's record of how far it got while
    /// it worked — the one thing a print turn can leave mid-turn — and it rides the next pack.
    /// </summary>
    [Fact]
    public void Build_WithAProgressNote_HandsItBack_UnderItsHeading()
    {
        var source = new StubSource("imp-1", "/x/imp-1/channel.md", false);
        var woke = Entry(1, ChannelAuthors.Supervisor, "BRIEF — first", "start");

        var pack = StatePack_Builder.Build(Inputs(woke, null, [woke], [source], progress: "- parser done (abc1234); next: Parse_All"));

        Assert.Contains(StatePack_Builder.PROGRESS_HEADING, pack);
        Assert.Contains("- parser done (abc1234); next: Parse_All", pack);
    }

    /// <summary>An outgrown note keeps its END — the last lines are where the member got to.</summary>
    [Fact]
    public void Build_WithAProgressNoteOverItsCap_KeepsTheNewestLines()
    {
        var source = new StubSource("imp-1", "/x/imp-1/channel.md", false);
        var woke = Entry(1, ChannelAuthors.Supervisor, "BRIEF — first", "start");
        var note = "OLDEST STEP\n" + new string('x', StatePack_Builder.PROGRESS_CAP) + "\nNEWEST STEP";

        var pack = StatePack_Builder.Build(Inputs(woke, null, [woke], [source], progress: note));

        Assert.Contains("NEWEST STEP", pack);
        Assert.DoesNotContain("OLDEST STEP", pack);
        Assert.Contains("characters truncated by the bridge", pack);
    }

    [Fact]
    public void Build_WithoutAProgressNote_HasNoProgressSection()
    {
        var source = new StubSource("imp-1", "/x/imp-1/channel.md", false);
        var woke = Entry(1, ChannelAuthors.Supervisor, "BRIEF — first", "start");

        Assert.DoesNotContain(StatePack_Builder.PROGRESS_HEADING, StatePack_Builder.Build(Inputs(woke, null, [woke], [source])));
    }
}

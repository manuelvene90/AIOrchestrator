using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.StatePack;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// The brief is the one entry a fresh member cannot do without, and the one the 4a fallback ("the
/// last three entries") lost first. These pin the two rules: a task marker wins over everything
/// later, and without markers the longest recent supervisor entry stands in.
/// </summary>
public class BriefFinderTests
{
    [Fact]
    public void Find_TakesTheLatestSupervisorEntryWithATaskMarker_EvenWhenLaterEntriesExist()
    {
        var history = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "BRIEF — port the ledger", "long body of the first brief ....."),
            Entry(2, ChannelAuthors.Implementer, "imp-1 online", "ready"),
            Entry(3, ChannelAuthors.Supervisor, "REVIEW FIN-D-1 @ abc1234", "scope + depth"),
            Entry(4, ChannelAuthors.Implementer, "Committed abc1234", "report"),
            Entry(5, ChannelAuthors.Supervisor, "Accepted — good work", "a short acknowledgement"),
            Entry(6, ChannelAuthors.App, "[agent] turn_ended imp-1 turn 4 — success", "request_id: ..."),
        };

        var brief = Brief_Finder.Find_OrNull(history);

        Assert.NotNull(brief);
        Assert.Equal(3, brief!.Index);
    }

    [Fact]
    public void Find_WithoutMarkers_TakesTheLongestRecentSupervisorEntry_LaterWinsATie()
    {
        var history = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "Here is what I need", new string('x', 400)),
            Entry(2, ChannelAuthors.Implementer, "imp-1 online", "ready"),
            Entry(3, ChannelAuthors.Supervisor, "ok", "short"),
            Entry(4, ChannelAuthors.Owner, "hello", new string('o', 2000)),
        };

        var brief = Brief_Finder.Find_OrNull(history);

        Assert.NotNull(brief);
        Assert.Equal(1, brief!.Index);
    }

    [Fact]
    public void Find_FallbackLooksOnlyAtTheRecentWindow()
    {
        var entries = new List<IChannelEntry> { Entry(1, ChannelAuthors.Supervisor, "very old and very long", new string('x', 5000)) };

        for (var i = 2; i <= 2 + Brief_Finder.FALLBACK_WINDOW; i++)
            entries.Add(Entry(i, i % 2 == 0 ? ChannelAuthors.Implementer : ChannelAuthors.Supervisor, $"entry {i}", "short"));

        var brief = Brief_Finder.Find_OrNull(entries);

        Assert.NotNull(brief);
        Assert.NotEqual(1, brief!.Index);
        Assert.Equal(ChannelAuthors.Supervisor, brief.Author);
    }

    [Fact]
    public void Find_WithNoSupervisorEntry_IsNull()
    {
        var history = new[]
        {
            Entry(1, ChannelAuthors.Owner, "BRIEF — the owner uses the word too", "not a supervisor"),
            Entry(2, ChannelAuthors.Implementer, "imp-1 online", "ready"),
        };

        Assert.Null(Brief_Finder.Find_OrNull(history));
    }

    [Theory]
    [InlineData("BRIEF — integrate the moved dev", true)]
    [InlineData("  final review — FIN-D-282a", true)]
    [InlineData("FINDINGS — ten from rev-8", true)]
    [InlineData("GO AHEAD — resume", true)]
    [InlineData("Accepted at c4b8edce2", false)]
    [InlineData("STANDING BY — waiting", false)]
    [InlineData("A briefing on the review policy", false)]
    public void HasTaskMarker_MatchesTheSubjectOpening_CaseInsensitive(string subject, bool expected)
    {
        Assert.Equal(expected, Brief_Finder.Has_TaskMarker(subject));
    }

    /// <summary>
    /// THE DEFECT E3 REQUIREMENT 3 SAYS DISAPPEARS, pinned: *"a brief that is merely QUOTED becomes
    /// the brief"*.
    ///
    /// <para>
    /// The subject rule cannot tell a brief from a quotation of one, because a reviewer quoting a
    /// brief back writes the subject they are quoting — `BRIEF — the parser fix`. So the LATER entry
    /// won, and the state pack handed the next turn a review as if it were its instructions. The
    /// declared type is what the writer passed a flag for, so a quotation carries the type of what it
    /// IS, and the type is read first.
    /// </para>
    /// <para>
    /// THE QUOTING ENTRY IS TYPED `report`, and getting that right took a failed run: my first
    /// fixture typed it `review`, and a supervisor entry typed `review` genuinely IS new
    /// instructions — a reviewer's brief is a review — so returning it was correct and the test was
    /// wrong. The defect needs an entry that is NOT a brief of any kind and merely mentions one:
    /// a progress note whose subject quotes the brief it is about.
    /// </para>
    /// </summary>
    [Fact]
    public void AReportThatQuotesABriefsSubjectIsNotTheBrief()
    {
        IReadOnlyList<IChannelEntry> history =
        [
            Typed(1, ChannelAuthors.Supervisor, "BRIEF — the parser fix", "do the thing", "brief"),
            Typed(2, ChannelAuthors.Supervisor, "BRIEF — the parser fix", "one correction to what I asked", "report"),
        ];

        var found = Brief_Finder.Find_OrNull(history);

        Assert.NotNull(found);
        Assert.Equal(1, found.Index);
        Assert.Equal("brief", found.Type);
    }

    /// <summary>
    /// THE SECOND HALF ON ITS OWN: with NO typed brief anywhere, an entry that declared a non-brief
    /// type is still refused even though its subject carries the marker — and the older untyped
    /// entry that really is the brief wins. Without this rule the report would win by subject, which
    /// is the defect surviving in a channel mid-transition.
    /// </summary>
    [Fact]
    public void ATypedReportNeverWinsBySubjectMarker()
    {
        IReadOnlyList<IChannelEntry> history =
        [
            Entry(1, ChannelAuthors.Supervisor, "BRIEF — the parser fix", "do the thing"),
            Typed(2, ChannelAuthors.Supervisor, "BRIEF — the parser fix", "one correction", "report"),
        ];

        Assert.Equal(1, Brief_Finder.Find_OrNull(history)!.Index);
    }

    /// <summary>
    /// A TYPED REVIEW IS STILL THE WORK IN HAND WHEN THERE IS NO BRIEF — `review` is one of the
    /// brief types, because a reviewer's instructions are the review it was asked for. This stops the
    /// case above from passing for the wrong reason (a rule that simply ignored every review).
    /// </summary>
    [Fact]
    public void ATypedReviewIsTheWorkInHandWhenNoBriefWasDeclared()
    {
        IReadOnlyList<IChannelEntry> history =
        [
            Typed(1, ChannelAuthors.Supervisor, "progress", "nothing yet", "report"),
            Typed(2, ChannelAuthors.Supervisor, "REVIEW — the parser fix", "review this", "review"),
        ];

        Assert.Equal(2, Brief_Finder.Find_OrNull(history)!.Index);
    }

    /// <summary>
    /// THE TRANSITION: an entirely UNTYPED history behaves exactly as it did — the subject markers,
    /// then the longest-entry fallback. Every channel in existence is untyped today, and a session on
    /// the old skill goes on writing untyped entries, so this is the case that must not move.
    /// </summary>
    [Fact]
    public void AnUntypedHistoryFallsThroughToTheBehaviourItHad()
    {
        IReadOnlyList<IChannelEntry> history =
        [
            Entry(1, ChannelAuthors.Supervisor, "BRIEF — the parser fix", "do the thing"),
            Entry(2, ChannelAuthors.Supervisor, "progress", "still going"),
        ];

        Assert.Equal(1, Brief_Finder.Find_OrNull(history)!.Index);
    }

    /// <summary>
    /// A TYPED ENTRY AMONG UNTYPED ONES does not disturb them: the type rule only ever ADDS a way to
    /// be sure, so a channel mid-transition — half old prose, half tool-written — still resolves.
    /// </summary>
    [Fact]
    public void AMixedHistoryResolvesToTheTypedBrief()
    {
        IReadOnlyList<IChannelEntry> history =
        [
            Entry(1, ChannelAuthors.Supervisor, "BRIEF — the old one", "written before the tool existed"),
            Typed(2, ChannelAuthors.Supervisor, "the new one", "written by the tool", "brief"),
        ];

        Assert.Equal(2, Brief_Finder.Find_OrNull(history)!.Index);
    }

    static IChannelEntry Typed(int index, ChannelAuthors author, string subject, string body, string type)
    {
        var raw = $"## [{index}] FROM {author} — 2026-09-09 10:00 — {subject}\ntype: {type}\n\n{body}";

        return ChannelEntry_Factory.Create(index, author, "2026-09-09 10:00", subject, body, raw, type);
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string body)
    {
        var word = ChannelAuthor_Words.Get_Word(author);
        var raw = $"## [{index}] FROM {word} — 2026-09-09 10:00 — {subject}\n\n{body}";
        return ChannelEntry_Factory.Create(index, author, "2026-09-09 10:00", subject, body, raw);
    }
}

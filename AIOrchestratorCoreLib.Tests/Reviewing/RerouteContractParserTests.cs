using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Reviewing;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE APP READS TWO FIELDS AND COPIES THE REST. A contract's argument — which reviewer, from which
/// commit — is machine-readable on purpose; everything under it is the SUPERVISOR's brief and is
/// carried verbatim, because a re-review's scope is the supervisor's decision and the app has no
/// opinion it is entitled to.
/// </summary>
public class RerouteContractParserTests
{
    static IChannelEntry Entry(string subject, string body)
    {
        return ChannelEntry_Parser.Parse_All($"## [7] FROM supervisor — 2026-09-15 10:00 — {subject}\n{body}\n")[0];
    }

    [Fact]
    public void ADeclarationNamesTheReviewerAndTheBaseCommit()
    {
        var read = RerouteContract_Parser.Read(Entry(
            "VERDICT — F1 and F3 must be fixed",
            "Fix F1 and F3 on the branch.\n\nREROUTE: rev-1 from abc1234\nCheck F1 and F3 only. F2 was accepted."));

        Assert.Equal(RerouteDeclarations.Contract, read.Kind);
        Assert.Equal("rev-1", read.ReviewerId);
        Assert.Equal("abc1234", read.BaseCommit);
    }

    /// <summary>
    /// THE BRIEF IS EVERYTHING BELOW THE LINE, VERBATIM — the rule that keeps the app out of the
    /// business of deciding what a re-review covers.
    /// </summary>
    [Fact]
    public void TheBriefIsEverythingUnderTheDeclaration()
    {
        var read = RerouteContract_Parser.Read(Entry(
            "VERDICT",
            "Fix F1.\n\nREROUTE: rev-1 from abc1234\nCheck F1 only.\nF2 stands as a stated limitation."));

        Assert.Equal("Check F1 only.\nF2 stands as a stated limitation.", read.Brief);
    }

    /// <summary>
    /// AND "VERBATIM" INCLUDES THE SHAPE. A brief with a blank line, a bullet list and an indented
    /// block reaches the reviewer as the supervisor wrote it; only the blank lines at the two ends
    /// are removed, because they are an artefact of where the declaration sits.
    /// </summary>
    [Fact]
    public void TheBriefKeepsItsOwnBlankLinesAndIndentation()
    {
        var read = RerouteContract_Parser.Read(Entry(
            "VERDICT",
            "REROUTE: rev-1 from abc1234\n\n- F1 only.\n\n    the delta is the fix\n\n"));

        Assert.Equal("- F1 only.\n\n    the delta is the fix", read.Brief);
    }

    [Fact]
    public void AnEntryWithNoDeclarationIsNone()
    {
        Assert.Equal(RerouteDeclarations.None, RerouteContract_Parser.Read(Entry("VERDICT", "Fix F1 and F3.")).Kind);
    }

    [Fact]
    public void TheSupervisorCanRetractOne()
    {
        Assert.Equal(RerouteDeclarations.Cancel, RerouteContract_Parser.Read(Entry("VERDICT", "REROUTE: cancel")).Kind);
    }

    /// <summary>
    /// A MALFORMED ARGUMENT IS NOT A CONTRACT. Half a declaration read as a whole one would route a
    /// round to a reviewer that does not exist, or diff from a commit nobody named; the honest answer
    /// is None, and the caller says which predicate failed.
    /// </summary>
    [Theory]
    [InlineData("REROUTE: rev-1")]
    [InlineData("REROUTE: from abc1234")]
    [InlineData("REROUTE: rev-1 from HEAD")]
    [InlineData("REROUTE: rev-1 from zzzz")]
    [InlineData("REROUTE:")]
    [InlineData("REROUTE: rev-1 from abc123")]
    [InlineData("REROUTE: rev-1 to rev-2 from abc1234")]
    public void AMalformedDeclarationIsNoDeclaration(string line)
    {
        Assert.Equal(RerouteDeclarations.None, RerouteContract_Parser.Read(Entry("VERDICT", line)).Kind);
    }

    /// <summary>
    /// AND A MALFORMED ONE CARRIES NO BRIEF EITHER. A refusal that still handed the caller the
    /// supervisor's words would invite a caller to route on half a declaration.
    /// </summary>
    [Fact]
    public void ARefusedDeclarationCarriesNothingAtAll()
    {
        var read = RerouteContract_Parser.Read(Entry("VERDICT", "REROUTE: rev-1\nCheck F1 only."));

        Assert.Equal(RerouteDeclarations.None, read.Kind);
        Assert.Equal(string.Empty, read.ReviewerId);
        Assert.Equal(string.Empty, read.BaseCommit);
        Assert.Equal(string.Empty, read.Brief);
    }

    /// <summary>
    /// AND A QUOTED ONE DECLARES NOTHING. A supervisor explaining the mechanism in a brief — or an
    /// implementer's report quoting the brief back — writes these words constantly. The matcher this
    /// parser delegates to has excluded quotation since 2026-09-09, and this is that rule's third
    /// consumer rather than a fourth spelling of it (CLAUDE.md decision 12).
    /// </summary>
    [Theory]
    [InlineData("> REROUTE: rev-1 from abc1234")]
    [InlineData("\" REROUTE: rev-1 from abc1234")]
    [InlineData("' REROUTE: rev-1 from abc1234")]
    public void AQuotedDeclarationIsNotADeclaration(string line)
    {
        Assert.Equal(RerouteDeclarations.None, RerouteContract_Parser.Read(Entry("VERDICT", line)).Kind);
    }

    /// <summary>
    /// NOR IS ONE MENTIONED MID-SENTENCE. The supervisor teaching the protocol — "end with REROUTE:
    /// rev-1 from &lt;sha&gt;" — is discussion, and the matcher's line-start rule in the body is what
    /// tells the two apart.
    /// </summary>
    [Fact]
    public void AMarkerInsideASentenceDeclaresNothing()
    {
        Assert.Equal(
            RerouteDeclarations.None,
            RerouteContract_Parser.Read(Entry("VERDICT", "When the fix lands write REROUTE: rev-1 from abc1234 in your verdict.")).Kind);
    }

    /// <summary>
    /// A DECORATED DECLARATION IS STILL A DECLARATION — the same rule the matcher applies to the
    /// front of the line, applied to what it leaves behind.
    /// </summary>
    [Fact]
    public void MarkdownAroundTheLineDoesNotHideIt()
    {
        var read = RerouteContract_Parser.Read(Entry("VERDICT", "**REROUTE: rev-1 from abc1234**\nCheck F1 only."));

        Assert.Equal(RerouteDeclarations.Contract, read.Kind);
        Assert.Equal("rev-1", read.ReviewerId);
    }

    /// <summary>
    /// A SHA IS A SHA WHICHEVER CASE IT WAS TYPED IN, and the contract compares it later — so it is
    /// lower-cased on the way in rather than at each comparison.
    /// </summary>
    [Fact]
    public void TheCommitIsLowerCased()
    {
        Assert.Equal("abc1234", RerouteContract_Parser.Read(Entry("VERDICT", "REROUTE: rev-1 from ABC1234")).BaseCommit);
    }

    /// <summary>
    /// THE FIRST DECLARATION IN AN ENTRY WINS, and the brief starts under it. An entry with two is a
    /// supervisor that wrote the line twice; taking the first is the one a human reads at the top,
    /// and the same rule the question directives use for a repeated marker.
    /// </summary>
    [Fact]
    public void TheFirstDeclarationInOneEntryWins()
    {
        var read = RerouteContract_Parser.Read(Entry(
            "VERDICT", "REROUTE: rev-1 from abc1234\nCheck F1.\nREROUTE: rev-2 from def5678"));

        Assert.Equal("rev-1", read.ReviewerId);
        Assert.Equal("abc1234", read.BaseCommit);
        Assert.Equal("Check F1.\nREROUTE: rev-2 from def5678", read.Brief);
    }

    /// <summary>
    /// A DECLARATION IN THE SUBJECT DECLARES NOTHING, and saying so is the point: the brief is what
    /// sits UNDER the line, and a subject has nothing under it. Fail-open, like every other refusal
    /// here — the verdict stays ordinary traffic and the supervisor keeps the round.
    /// </summary>
    [Fact]
    public void ADeclarationInTheSubjectAloneIsNotOne()
    {
        Assert.Equal(
            RerouteDeclarations.None,
            RerouteContract_Parser.Read(Entry("VERDICT — REROUTE: rev-1 from abc1234", "Fix F1.")).Kind);
    }

    /// <summary>
    /// THE PARSER IS A TEXT READER AND SCREENS NO AUTHORS. Who may declare a contract is the CALLER's
    /// question — it screens for the supervisor before it ever asks this — and putting the rule in
    /// both places would be two rules to keep in step.
    /// </summary>
    [Fact]
    public void TheParserRefusesNothingAboutAuthors()
    {
        var fromAnImplementer = ChannelEntry_Parser.Parse_All(
            "## [7] FROM implementer — 2026-09-15 10:00 — done\nREROUTE: rev-1 from abc1234\n")[0];

        Assert.Equal(RerouteDeclarations.Contract, RerouteContract_Parser.Read(fromAnImplementer).Kind);
    }

    /// <summary>
    /// THE MARKER COMES FROM THE GRAMMAR, never from a literal in this test. If the word ever changes
    /// in <c>kit/grammar/channel-grammar.json</c>, the fixtures above are what goes red — and this
    /// case says which one word they are all built on.
    /// </summary>
    [Fact]
    public void TheWordIsTheGrammarsWord()
    {
        Assert.Equal("REROUTE:", ChannelGrammar.REROUTE);
        Assert.Equal(RerouteDeclarations.Contract, RerouteContract_Parser.Read(Entry("VERDICT", $"{ChannelGrammar.REROUTE} rev-1 from abc1234")).Kind);
    }
}

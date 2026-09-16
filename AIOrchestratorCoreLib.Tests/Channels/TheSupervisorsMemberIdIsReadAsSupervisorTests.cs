using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.StatePack;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// `FROM sup` is the supervisor, because for four days the append tool signed with the session's
/// MEMBER id instead of its role word: between 7d6949f (2026-09-10) and 2848172 (2026-09-14)
/// derive_author preferred AIORCH_MEMBER, and the supervisor's member id is "sup". Measured on the
/// VPS 2026-09-15: 93 such headers in fincanva-8 and fincanva-12, written THROUGH channel-append.sh
/// (both `.self-write.sup` and `.self-write.supervisor` records are present, and only that script
/// writes them).
///
/// <para>
/// The cost of reading them as Unknown is not cosmetic: an Unknown author is invisible to
/// <see cref="Brief_Finder"/>, so the state pack a terminal session is handed carries NO BRIEF for
/// those orchestrations — silently, and on the mechanism by which a terminal session learns what it
/// was asked to do.
/// </para>
/// </summary>
public class TheSupervisorsMemberIdIsReadAsSupervisorTests
{
    const string MEMBER_CHANNEL_WITH_A_SUP_BRIEF =
        "## [1] FROM sup — 2026-09-14 12:49 — BRIEF — FIN-D-307a — the run door decides a refusal from a boolean that means three things\n" +
        "\n" +
        "Read the run door and say what the boolean means at each call site.\n" +
        "\n" +
        "## [2] FROM implementer — 2026-09-14 13:10 — starting\n" +
        "\n" +
        "On it.\n";

    [Fact]
    public void ABriefSignedWithTheMemberId_IsFoundByBriefFinder()
    {
        var history = ChannelEntry_Parser.Parse_All(MEMBER_CHANNEL_WITH_A_SUP_BRIEF);
        var brief = Brief_Finder.Find_OrNull(history);

        Assert.NotNull(brief);
        Assert.StartsWith("BRIEF — FIN-D-307a", brief!.Subject);
    }

    [Fact]
    public void AGenuinelyUnknownAuthorWord_IsStillUnknown()
    {
        // THE BOUNDARY, and it is the half that keeps the case above honest. Without it the suite
        // would pass just as well if Parse_Author had been widened to resolve anything it did not
        // recognise to Supervisor — which is the one outcome the repair must not have.
        var entries = ChannelEntry_Parser.Parse_All("## [1] FROM auditor — 2026-09-14 12:49 — BRIEF — a stranger\nbody\n");

        Assert.Equal(ChannelAuthors.Unknown, entries[0].Author);
        Assert.Null(Brief_Finder.Find_OrNull(entries));
    }

    [Theory]
    [InlineData("imp-2")]
    [InlineData("rev-1")]
    [InlineData("solo-1")]
    [InlineData("com")]
    [InlineData("general")]
    public void NoOtherMemberIdIsRead_BecauseNoneOfThemEverReachedAHeader(string memberId)
    {
        // THE REPAIR IS DATED AND CLOSED, not an alias vocabulary. Only the supervisor's own appends
        // went through the tool on the affected hosts, so only "sup" exists in the record; a second
        // id added here would leave the author words with no boundary at all.
        var entries = ChannelEntry_Parser.Parse_All($"## [1] FROM {memberId} — 2026-09-14 12:49 — BRIEF — a member id\nbody\n");

        Assert.Equal(ChannelAuthors.Unknown, entries[0].Author);
    }

    [Fact]
    public void AMemberSeesASupEntryAsInbound_AndTheSupervisorDoesNotSeeItsOwnOnTheOwnerChannel()
    {
        // THE DOWNSTREAM CONSEQUENCE, verified rather than assumed. Making `sup` a Supervisor makes
        // a `FROM sup` entry INBOUND for an implementer or a reviewer — correct, it really was the
        // supervisor writing to them.
        var author = ChannelEntry_Parser.Parse_All(MEMBER_CHANNEL_WITH_A_SUP_BRIEF)[0].Author;

        Assert.True(PrintTurn_Trigger.Is_Inbound(SessionRoles.Implementer, author));
        Assert.True(PrintTurn_Trigger.Is_Inbound(SessionRoles.Reviewer, author));

        // AND THE ONE THAT WOULD HAVE BEEN A DEFECT: `sup`-signed entries also sit on the owner
        // channel (fincanva-12 carries owner-channel.md.self-write.sup), so if a supervisor counted
        // its own entries as inbound it would wake itself on its own words, for ever. It does not —
        // Is_Inbound admits only the owner and MEMBERS for that role, and a supervisor is not a
        // member. Pinned here because the parser change is what would have exposed it.
        Assert.False(PrintTurn_Trigger.Is_Inbound(SessionRoles.Supervisor, author));
        Assert.False(ChannelAuthor_Kinds.Is_Member(author));
    }
}

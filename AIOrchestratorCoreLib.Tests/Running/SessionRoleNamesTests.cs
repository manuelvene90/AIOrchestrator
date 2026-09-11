using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

public class SessionRoleNamesTests
{
    [Theory]
    [InlineData(SessionRoles.Supervisor, "/supervisor orch-1")]
    [InlineData(SessionRoles.Implementer, "/implementer orch-1/imp-2")]
    [InlineData(SessionRoles.Reviewer, "/reviewer orch-1/imp-2")]
    [InlineData(SessionRoles.Solo, "/solo orch-1")]
    [InlineData(SessionRoles.General, "/general-supervisor")]
    [InlineData(SessionRoles.Communicator, "/communicator orch-1")]
    public void RoleCommand_IsTheOneTheTerminalSpawnAlwaysPassed(SessionRoles role, string expected)
    {
        Assert.Equal(expected, SessionRole_Names.Build_RoleCommand(role, "orch-1", "imp-2"));
    }

    [Fact]
    public void ConfigKeys_RoundTrip_AndUnknownIsNull()
    {
        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(role, SessionRole_Names.Parse_OrNull(SessionRole_Names.Get_ConfigKey(role)));

        Assert.Null(SessionRole_Names.Parse_OrNull("janitor"));
        Assert.Equal(SessionRoles.Reviewer, SessionRole_Names.From_MemberKind(MemberKinds.Reviewer));
    }

    /// <summary>The word the print runner signs with must parse back to the same author — or the entry becomes Unknown and invisible to the mirror.</summary>
    [Theory]
    [InlineData(ChannelAuthors.Supervisor)]
    [InlineData(ChannelAuthors.Implementer)]
    [InlineData(ChannelAuthors.Reviewer)]
    [InlineData(ChannelAuthors.Solo)]
    [InlineData(ChannelAuthors.Communicator)]
    [InlineData(ChannelAuthors.Owner)]
    [InlineData(ChannelAuthors.App)]
    public void AuthorWord_ParsesBackToTheSameAuthor(ChannelAuthors author)
    {
        var header = $"## [3] FROM {ChannelAuthor_Words.Get_Word(author)} — 2026-09-05 10:00 — subject\n\nbody\n";

        Assert.Equal(author, ChannelEntry_Parser.Parse_All(header)[0].Author);
    }

    [Fact]
    public void UnknownAuthor_HasNoWord()
    {
        Assert.Throws<Exception>(() => ChannelAuthor_Words.Get_Word(ChannelAuthors.Unknown));
    }

    [Fact]
    public void RunnerAndResumeWords_RoundTrip()
    {
        Assert.Equal(SessionRunners.Print, SessionRunner_Names.Parse_OrNull(" PRINT "));
        Assert.Equal(SessionRunners.Stream, SessionRunner_Names.Parse_OrNull("stream"));

        // 'bg' IS a word now, and this line used to assert it was not. It parses because the
        // fallback ladder needs a name for the rung between stream and print even while that rung
        // is unimplemented — what refuses it is Runner_Support (no role) and BgSettings_Rule (never
        // with Remote Control on), not the spelling.
        Assert.Equal(SessionRunners.Bg, SessionRunner_Names.Parse_OrNull("bg"));
        Assert.Null(SessionRunner_Names.Parse_OrNull("headless"));
        Assert.Equal(ResumeModes.Fresh, ResumeMode_Names.Parse_OrNull("fresh"));
        Assert.Null(ResumeMode_Names.Parse_OrNull(null));
    }
}

using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// This is what replaced the communicator session, so it has to be as CONCRETE as the communicator
/// was told to be ("Sup is editing the launcher and running the test suite", not "Sup is working").
/// It reads the same transcript the communicator read; the difference is that it costs nothing.
/// </summary>
public class SupervisorActivityDescriberTests
{
    static string Line(string toolName, string inputJson)
    {
        return $$$"""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"{{{toolName}}}","input":{{{inputJson}}}}]}}""";
    }

    [Fact]
    public void Editing_NamesTheFile_NotTheWholePath()
    {
        var tail = Line("Edit", """{"file_path":"C:\\repo\\AIOrchestratorCoreLib\\Bridge\\BridgeEngineModel.cs"}""");

        Assert.Equal("editing BridgeEngineModel.cs", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(tail));
    }

    [Fact]
    public void Reading_And_Searching_AreDistinctFromEditing()
    {
        Assert.Equal("reading supervisor.md", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(
            Line("Read", """{"file_path":"/kit/commands/supervisor.md"}""")));

        Assert.Equal("searching for TODO", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(
            Line("Grep", """{"pattern":"TODO"}""")));
    }

    [Fact]
    public void RecognisableCommands_AreShortenedToTheirFirstWords()
    {
        var tail = Line("Bash", """{"command":"dotnet test AIOrchestratorCoreLib.Tests --nologo -v q 2>&1 | tail"}""");

        Assert.Equal("running dotnet test AIOrchestratorCoreLib.Tests", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(tail));
    }

    /// <summary>
    /// Shell plumbing told the owner NOTHING and looked broken on their phone. This is verbatim
    /// from a real conversation: "🔴 Sup: mid-task — running sup="$HOME/.claude/supervision/
    /// strategy-lab-2"; sed -n '208,". Truncating a one-liner to N words cannot produce sense.
    /// </summary>
    [Theory]
    [InlineData("""{"command":"sup=\"$HOME/.claude/supervision/strategy-lab-2\"; sed -n '208,240p' \"$sup/PLAN.md\""}""")]
    [InlineData("""{"command":"cd \"C:/Users/Gianpiero/Documents/Visual Studio 2022/Projects\" && ls"}""")]
    [InlineData("""{"command":"cat >> \"$sup/owner-channel.md\" <<'EOF'"}""")]
    public void ShellPlumbing_CollapsesToSomethingHonest_RatherThanGarbage(string input)
    {
        Assert.Equal("running a command", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(Line("Bash", input)));
    }

    [Fact]
    public void TheUsefulOnes_StillComeThrough()
    {
        Assert.Equal("running git status --short", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(
            Line("Bash", """{"command":"git status --short"}""")));

        Assert.Equal("running dotnet build AIOrchestratorCoreLib", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(
            Line("Bash", """{"command":"dotnet build AIOrchestratorCoreLib -v q --nologo"}""")));
    }

    [Fact]
    public void TheLastToolCallWins_BecauseThatIsWhatItIsDoingNOW()
    {
        var tail = string.Join('\n',
            Line("Read", """{"file_path":"/a/old.cs"}"""),
            Line("Bash", """{"command":"git status"}"""));

        Assert.Equal("running git status", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(tail));
    }

    /// <summary>
    /// The tail is read from a byte offset, so the first line is usually a fragment. It must be
    /// skipped silently rather than poisoning the answer.
    /// </summary>
    [Fact]
    public void TruncatedFirstLine_IsIgnored()
    {
        var tail = "ontent\":[{\"type\":\"text\"...GARBAGE\n" + Line("Edit", """{"file_path":"Foo.cs"}""");

        Assert.Equal("editing Foo.cs", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(tail));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"type":"assistant","message":{"content":[{"type":"text","text":"just thinking"}]}}""")]
    public void NothingUsable_YieldsNull_SoTheCallerCanFallBack(string tail)
    {
        Assert.Null(SupervisorActivity_Describer.Describe_FromTranscript_OrNull(tail));
    }

    [Fact]
    public void MissingInput_DoesNotThrow_AndStaysReadable()
    {
        Assert.Equal("editing a file", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(Line("Write", "{}")));
        Assert.Equal("running a command", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(Line("Bash", "{}")));
    }

    [Fact]
    public void UnknownTool_IsNamedRatherThanHidden()
    {
        Assert.Equal("using SomeNewTool", SupervisorActivity_Describer.Describe_FromTranscript_OrNull(Line("SomeNewTool", "{}")));
    }

    [Fact]
    public void MissingUsageFile_YieldsNull_NeverThrows()
    {
        Assert.Null(SupervisorActivity_Describer.Describe_OrNull(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.json")));
    }
}

using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// What follows "/model" or "/effort" on the owner's phone. The lexer hands the app the whole
/// remainder of the message, lowercased ("model fable 5.1"); this splits an optional ROLE word off
/// the front and hands back the argument, so the caller can decide between "apply it" and
/// "show the buttons".
/// </summary>
public class ModelEffortCommandParserTests
{
    [Fact]
    public void TheBareCommand_HasNoRoleAndAnEmptyArgument()
    {
        Assert.Equal((null, ""), ModelEffortCommand_Parser.Parse_Argument("model", "model"));
    }

    [Fact]
    public void AnArgument_IsHandedBackWhole()
    {
        Assert.Equal((null, "fable 5.1"), ModelEffortCommand_Parser.Parse_Argument("model fable 5.1", "model"));
    }

    [Theory]
    [InlineData("model sup opus", "sup", "opus")]
    [InlineData("model supervisor opus", "sup", "opus")]
    [InlineData("model imp sonnet", "imp", "sonnet")]
    [InlineData("model implementer sonnet", "imp", "sonnet")]
    [InlineData("model implementers sonnet", "imp", "sonnet")]
    [InlineData("model solo haiku 4.5", "imp", "haiku 4.5")]
    [InlineData("effort sup xhigh", "sup", "xhigh")]
    public void ALeadingRoleWord_IsSplitOff(string commandText, string expectedRole, string expectedArgument)
    {
        Assert.Equal((expectedRole, expectedArgument), ModelEffortCommand_Parser.Parse_Argument(commandText, commandText.Split(' ')[0]));
    }

    /// <summary>A role word with nothing after it is a role and an empty argument — the buttons follow, for that role.</summary>
    [Fact]
    public void ARoleWordAlone_IsARoleWithAnEmptyArgument()
    {
        Assert.Equal(("sup", ""), ModelEffortCommand_Parser.Parse_Argument("effort sup", "effort"));
    }

    [Fact]
    public void Padding_IsIgnored()
    {
        Assert.Equal((null, "xhigh"), ModelEffortCommand_Parser.Parse_Argument("effort    xhigh  ", "effort"));
    }

    /// <summary>"/models" is not "/model" — the verb must end where the word ends.</summary>
    [Theory]
    [InlineData("models fable")]
    [InlineData("modelling")]
    [InlineData("effortless")]
    public void AVerbThatOnlyBeginsWithTheCommand_IsNotTheCommand(string commandText)
    {
        Assert.False(ModelEffortCommand_Parser.Is_Command(commandText, "model"));
        Assert.False(ModelEffortCommand_Parser.Is_Command(commandText, "effort"));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("model fable")]
    [InlineData("model   ")]
    public void TheCommandItself_IsRecognised(string commandText)
    {
        Assert.True(ModelEffortCommand_Parser.Is_Command(commandText, "model"));
    }
}

using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// ASCII mockups are how the owner picks between layout options from their phone. Two things must
/// hold: the drawing keeps its exact characters and alignment, and the translator never touches it.
/// </summary>
public class MonospaceBlocksFormatterTests
{
    const string MOCKUP_MESSAGE = """
        🔴 Sup: two layouts for the settings window — which one?

        ```
        +----------+-------------+
        | rail     | content     |
        |  general |  [x] dark   |
        |  models  |  [ ] italian|
        +----------+-------------+
        ```
        OPTION: rail on the left
        """;

    [Fact]
    public void Extract_LiftsTheDrawingOut_SoProseCanBeTranslatedWithoutIt()
    {
        var (withoutBlocks, blocks) = MonospaceBlocks_Formatter.Extract_Blocks(MOCKUP_MESSAGE);

        Assert.Single(blocks);
        Assert.Contains("rail", blocks[0]);
        Assert.DoesNotContain("+----------+", withoutBlocks);
        Assert.Contains("which one?", withoutBlocks);
    }

    [Fact]
    public void ExtractThenRestore_ReturnsTheDrawingCharacterForCharacter()
    {
        var (withoutBlocks, blocks) = MonospaceBlocks_Formatter.Extract_Blocks(MOCKUP_MESSAGE);

        // Stands in for what the translator does to the prose around the block.
        var translated = withoutBlocks.Replace("which one?", "quale dei due?");
        var restored = MonospaceBlocks_Formatter.Restore_Blocks(translated, blocks);

        Assert.Contains("quale dei due?", restored);
        Assert.Contains("|  general |  [x] dark   |", restored);
    }

    [Fact]
    public void Build_Html_WrapsTheDrawingInPre_AndEscapesEverything()
    {
        var html = MonospaceBlocks_Formatter.Build_Html("before\n```\n<b>a & b</b>\n```\nafter");

        Assert.Contains("<pre>", html);
        Assert.Contains("&lt;b&gt;a &amp; b&lt;/b&gt;", html);
        Assert.Contains("before", html);
        Assert.Contains("after", html);
    }

    [Fact]
    public void Has_Blocks_TellsPlainProseApartFromAMockup()
    {
        Assert.True(MonospaceBlocks_Formatter.Has_Blocks(MOCKUP_MESSAGE));
        Assert.False(MonospaceBlocks_Formatter.Has_Blocks("🔴 Sup: done — branch ready to merge"));
    }

    [Fact]
    public void Build_Html_PlainProse_IsJustEscapedText()
    {
        Assert.Equal("a &lt; b &amp; c", MonospaceBlocks_Formatter.Build_Html("a < b & c"));
    }
}

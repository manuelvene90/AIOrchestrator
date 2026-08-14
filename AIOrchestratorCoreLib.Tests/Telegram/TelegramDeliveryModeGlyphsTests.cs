using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// Topic names get re-decorated on every mode change, so glyphs must never accumulate — a name
/// that grew "🔕 🌙 🔕 crm bug" would be permanent litter in the owner's topic list.
/// </summary>
public class TelegramDeliveryModeGlyphsTests
{
    [Fact]
    public void Decorate_AddsTheGlyphOfTheMode()
    {
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Decorate_TopicName("crm bug", TelegramDeliveryModes.Normal));
        Assert.Equal("🔕 crm bug", TelegramDeliveryMode_Glyphs.Decorate_TopicName("crm bug", TelegramDeliveryModes.Silenced));
        Assert.Equal("🌙 crm bug", TelegramDeliveryMode_Glyphs.Decorate_TopicName("crm bug", TelegramDeliveryModes.Deferred));
    }

    [Theory]
    [InlineData("crm bug")]
    [InlineData("🔕 crm bug")]
    [InlineData("🌙 crm bug")]
    public void Strip_ThenDecorate_NeverStacksGlyphs(string currentName)
    {
        var baseName = TelegramDeliveryMode_Glyphs.Strip_Glyph(currentName);

        Assert.Equal("crm bug", baseName);
        Assert.Equal("🔕 crm bug", TelegramDeliveryMode_Glyphs.Decorate_TopicName(baseName, TelegramDeliveryModes.Silenced));
    }

    [Fact]
    public void Strip_LeavesANameThatMerelyCONTAINSAnEmojiAlone()
    {
        Assert.Equal("release 🔔 candidate", TelegramDeliveryMode_Glyphs.Strip_Glyph("release 🔔 candidate"));
    }

    [Fact]
    public void Terminal_ReplacesTheModeGlyphInsteadOfStackingWithIt()
    {
        // Terminal already silences the topic, so 💻 🔕 would say one thing twice — the
        // presence/delivery conflation this mode removes, drawn on the title bar.
        var decorated = TelegramDeliveryMode_Glyphs.Decorate_TopicName(
            "crm bug",
            TelegramDeliveryModes.Silenced,
            isAway: false,
            isQuiet: false,
            OwnerPresenceModes.Terminal);

        Assert.Equal("💻 crm bug", decorated);
    }

    [Fact]
    public void Terminal_StillShowsAway_BecauseTheyAreDifferentFacts()
    {
        // Away is app-wide and about the owner's phone; terminal is about where they are sitting
        // for THIS orchestration. One does not imply the other.
        var decorated = TelegramDeliveryMode_Glyphs.Decorate_TopicName(
            "crm bug",
            TelegramDeliveryModes.Normal,
            isAway: true,
            isQuiet: false,
            OwnerPresenceModes.Terminal);

        Assert.Equal("✈ 💻 crm bug", decorated);
    }

    [Fact]
    public void Strip_RemovesTheTerminalGlyph_SoARenameDoesNotAccumulate()
    {
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph("💻 crm bug"));
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph("✈ 💻 crm bug"));
    }
}

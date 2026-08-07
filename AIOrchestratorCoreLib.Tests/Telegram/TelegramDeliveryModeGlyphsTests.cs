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
}

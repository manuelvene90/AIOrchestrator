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

    /// <summary>
    /// 🧪 REPLACES the mode glyph, because /test IS mute underneath — the delivery mode really is
    /// Silenced, so drawing 🔕 🧪 together would state one fact twice.
    /// </summary>
    [Fact]
    public void AwaitingTest_ReplacesTheModeGlyph()
    {
        Assert.Equal(
            "🧪 crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Silenced, isAway: false, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: true));
    }

    /// <summary>
    /// AND IT OUTRANKS TERMINAL PRESENCE, which otherwise replaces every mode glyph. The others say
    /// how messages are being delivered; this one says DO NOT CLOSE THIS YET — losing it because the
    /// owner is sitting in the terminal would hide the reminder exactly when they might act on it.
    /// </summary>
    [Fact]
    public void AwaitingTest_SurvivesTerminalPresence()
    {
        Assert.Equal(
            "🧪 crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Silenced, isAway: false, isQuiet: false,
                OwnerPresenceModes.Terminal, isAwaitingTest: true));

        // Away is app-wide and about their phone, so it still shows alongside.
        Assert.Equal(
            "✈ 🧪 crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Silenced, isAway: true, isQuiet: false,
                OwnerPresenceModes.Terminal, isAwaitingTest: true));
    }

    /// <summary>Without the strip, every rename would stack another 🧪 onto the name.</summary>
    [Fact]
    public void Strip_RemovesTheAwaitingTestGlyph()
    {
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph("🧪 crm bug"));
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph("✈ 🧪 crm bug"));
    }

    /// <summary>
    /// THE OWNER ASKED TO SEE IT FROM THE TOPIC LIST (2026-08-19): "from the topic name I should be
    /// able to immediately understand if some topic needs me for a response, whether blocking or
    /// not". Two glyphs, because one would answer half the question.
    /// </summary>
    [Fact]
    public void AWaitingTopicSaysSo_AndSaysWhetherItIsStopped()
    {
        Assert.Equal(
            "❓ crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Normal, isAway: false, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: false, ownerReply: OwnerReplyStates.Wanted));

        Assert.Equal(
            "⛔ crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Normal, isAway: false, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: false, ownerReply: OwnerReplyStates.Blocking));
    }

    /// <summary>
    /// It CONCATENATES rather than replacing — their word. It leads, because it is the only glyph
    /// here that asks something OF them; the rest describe what the app is doing.
    /// </summary>
    [Fact]
    public void TheReplyGlyphLeadsAndKeepsTheOthers()
    {
        Assert.Equal(
            "❓ 🧪 crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Silenced, isAway: false, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: true, ownerReply: OwnerReplyStates.Wanted));

        Assert.Equal(
            "⛔ ✈ 🔕 crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Silenced, isAway: true, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: false, ownerReply: OwnerReplyStates.Blocking));
    }

    /// <summary>Without the strip, every rename would stack another ❓ onto the name.</summary>
    [Fact]
    public void Strip_RemovesTheReplyGlyphs()
    {
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph("❓ crm bug"));
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph("⛔ ✈ 🔕 crm bug"));
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph("❓ 🧪 crm bug"));
    }

    /// <summary>Nothing waiting draws nothing — the default must stay silent.</summary>
    [Fact]
    public void NoReplyWantedDrawsNoGlyph()
    {
        Assert.Equal(
            "crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Normal, isAway: false, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: false, ownerReply: OwnerReplyStates.None));
    }

    /// <summary>
    /// THE OWNER'S OWN CASE, 2026-08-20: they muted a topic and no bell appeared. That one turned out
    /// to be Telegram's own mute, which the app cannot see — but the reply glyphs had just been put
    /// IN FRONT of the mode glyph, and a prefix that swallowed the bell would produce the identical
    /// symptom the moment it went live. This pins that it does not.
    /// </summary>
    [Fact]
    public void AMutedTopicKeepsItsBell_EvenWhenSomeoneIsWaitingOnTheOwner()
    {
        Assert.Equal(
            "❓ 🔕 crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Silenced, isAway: false, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: false, ownerReply: OwnerReplyStates.Wanted));

        // And with nobody waiting, the bell is the whole decoration — the plain /mute case.
        Assert.Equal(
            "🔕 crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Silenced, isAway: false, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: false, ownerReply: OwnerReplyStates.None));

        // Do-Not-Disturb keeps its moon on the same terms.
        Assert.Equal(
            "❓ 🌙 crm bug",
            TelegramDeliveryMode_Glyphs.Decorate_TopicName(
                "crm bug", TelegramDeliveryModes.Deferred, isAway: false, isQuiet: false,
                OwnerPresenceModes.Remote, isAwaitingTest: false, ownerReply: OwnerReplyStates.Wanted));
    }
}

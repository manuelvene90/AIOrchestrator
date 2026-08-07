using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The owner landed from a flight to a wall of messages, several of them multi-select questions,
/// with no way to tell which were still relevant. Away mode exists to stop that backlog forming.
///
/// The three-state shape is the owner's own correction: a single 15-minute timer would let a
/// hundred questions arrive and only THEN react. QUIET fires at the third unanswered message,
/// immediately; AWAY is the conclusion drawn 15 minutes later.
/// </summary>
public class AwayModePolicyTests
{
    static readonly DateTime T0 = new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(9, true)]
    public void Quiet_TripsOnTheThirdUnansweredMessage_WithNoWaiting(int unanswered, bool expected)
    {
        Assert.Equal(expected, AwayMode_Policy.Should_GoQuiet(unanswered));
    }

    [Fact]
    public void Away_NeedsBothSomeoneWaitingAndFifteenMinutesOfSilence()
    {
        Assert.True(AwayMode_Policy.Should_EnterAway(true, T0, T0.AddMinutes(AwayMode_Policy.AWAY_AFTER_MINUTES)));
    }

    /// <summary>
    /// Silence with nobody waiting is not absence — it usually means there was nothing to say.
    /// Tripping away mode then would announce a state change for no reason.
    /// </summary>
    [Fact]
    public void Silence_WithNoOrchestrationWaiting_IsNotAway()
    {
        Assert.False(AwayMode_Policy.Should_EnterAway(false, T0, T0.AddHours(6)));
    }

    /// <summary>
    /// The chatty-supervisor case: three questions in one minute means the SUPERVISOR is noisy, not
    /// that the owner left. Quiet still fires (harmless, unannounced), away must not.
    /// </summary>
    [Fact]
    public void ABurstOfQuestions_GoesQuietButDoesNotGoAway()
    {
        Assert.True(AwayMode_Policy.Should_GoQuiet(3));
        Assert.False(AwayMode_Policy.Should_EnterAway(true, T0, T0.AddMinutes(1)));
        Assert.False(AwayMode_Policy.Should_EnterAway(true, T0, T0.AddMinutes(14)));
    }

    /// <summary>
    /// The clock runs on the owner's last message ANYWHERE: chatting in one topic proves they are
    /// present for all of them, so a quiet orchestration must not drag everything into away mode.
    /// </summary>
    [Fact]
    public void PresenceInAnyTopic_KeepsEverythingOutOfAway()
    {
        var chattedRecently = T0.AddMinutes(14);

        Assert.False(AwayMode_Policy.Should_EnterAway(true, chattedRecently, T0.AddMinutes(20)));
    }

    [Fact]
    public void TheOwnerNotice_SaysTheBacklogIsParkedAndOneMessageClearsItEverywhere()
    {
        Assert.Contains("PARKED", AwayMode_Policy.AWAY_ON_NOTICE);
        Assert.Contains("do not scroll back", AwayMode_Policy.AWAY_ON_NOTICE);
        Assert.Contains("everywhere", AwayMode_Policy.AWAY_ON_NOTICE);
        Assert.Contains("30 min", AwayMode_Policy.AWAY_ON_NOTICE);

        Assert.Contains("away mode off", AwayMode_Policy.AWAY_OFF_NOTICE, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parked", AwayMode_Policy.PARKED_SUFFIX, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Away is app-wide and the per-topic delivery mode is not, so a topic can be in both states at
/// once and the title has to survive being re-decorated on every tick.
/// </summary>
public class AwayTopicGlyphTests
{
    [Fact]
    public void AwayGlyph_IsNotTheDeferredMoon()
    {
        Assert.NotEqual(TelegramDeliveryMode_Glyphs.DEFERRED, TelegramDeliveryMode_Glyphs.AWAY);
        Assert.Equal(TelegramDeliveryMode_Glyphs.AWAY, AwayMode_Policy.AWAY_GLYPH);
    }

    [Fact]
    public void AwayAndMode_ShowTogether()
    {
        Assert.Equal("✈ crm bug", TelegramDeliveryMode_Glyphs.Decorate_TopicName("crm bug", TelegramDeliveryModes.Normal, isAway: true));
        Assert.Equal("✈ 🔕 crm bug", TelegramDeliveryMode_Glyphs.Decorate_TopicName("crm bug", TelegramDeliveryModes.Silenced, isAway: true));
        Assert.Equal("🌙 crm bug", TelegramDeliveryMode_Glyphs.Decorate_TopicName("crm bug", TelegramDeliveryModes.Deferred, isAway: false));
    }

    /// <summary>
    /// Titles are re-decorated every tick, so glyphs must never accumulate — "✈ ✈ 🔕 crm bug" would
    /// be permanent litter in the topic list.
    /// </summary>
    [Theory]
    [InlineData("crm bug")]
    [InlineData("✈ crm bug")]
    [InlineData("✈ 🔕 crm bug")]
    [InlineData("🌙 crm bug")]
    [InlineData("✈ 🌙 crm bug")]
    public void Strip_RemovesEveryLeadingGlyph(string decorated)
    {
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph(decorated));
    }

    [Fact]
    public void Strip_LeavesANameThatMerelyContainsAnEmojiAlone()
    {
        Assert.Equal("release 🔔 candidate", TelegramDeliveryMode_Glyphs.Strip_Glyph("release 🔔 candidate"));
    }
}

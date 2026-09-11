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

    /// <summary>AT_THE_PC / AT_THE_PHONE name the ownerAtAPc argument at every call below.</summary>
    const bool AT_THE_PC = true;

    const bool AT_THE_PHONE = false;

    [Fact]
    public void Away_NeedsBothSomeoneWaitingAndFifteenMinutesOfSilence()
    {
        Assert.True(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, T0, T0.AddMinutes(AwayMode_Policy.AWAY_AFTER_MINUTES)));
    }

    /// <summary>
    /// Silence with nobody waiting is not absence — it usually means there was nothing to say.
    /// Tripping away mode then would announce a state change for no reason.
    /// </summary>
    [Fact]
    public void Silence_WithNoOrchestrationWaiting_IsNotAway()
    {
        Assert.False(AwayMode_Policy.Should_EnterAway(false, AT_THE_PHONE, T0, T0.AddHours(6)));
    }

    /// <summary>
    /// The chatty-supervisor case: three questions in one minute means the SUPERVISOR is noisy, not
    /// that the owner left. Quiet still fires (harmless, unannounced), away must not.
    /// </summary>
    [Fact]
    public void ABurstOfQuestions_GoesQuietButDoesNotGoAway()
    {
        Assert.True(AwayMode_Policy.Should_GoQuiet(3));
        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, T0, T0.AddMinutes(1)));
        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, T0, T0.AddMinutes(14)));
    }

    /// <summary>
    /// The clock runs on the owner's last message ANYWHERE: chatting in one topic proves they are
    /// present for all of them, so a quiet orchestration must not drag everything into away mode.
    /// </summary>
    [Fact]
    public void PresenceInAnyTopic_KeepsEverythingOutOfAway()
    {
        var chattedRecently = T0.AddMinutes(14);

        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, chattedRecently, T0.AddMinutes(20)));
    }

    // -----------------------------------------------------------------------------------
    // At the pc — owner's ruling, 2026-09-07
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// THE OWNER'S REPORT, 2026-09-07: *"if I'm at the pc, and use the pc command to tell the
    /// session I'm working from there and obviously not answering from telegram anymore, at some
    /// point it goes in automatic away mode... when I'm at the pc... automatic away mode should
    /// never happen"*.
    ///
    /// Their log had it exactly: da-vinci-fintech-suite-26 went Terminal at 09:53, away mode fired
    /// app-wide at 10:23:50 calling them unresponsive, and they left that terminal at 10:30:56.
    ///
    /// The inputs here are the ones that USED to be sufficient — someone waiting, and silence well
    /// past the threshold — so this fails against the old two-argument rule for the right reason.
    /// </summary>
    [Fact]
    public void AtThePc_NeverGoesAway_HoweverLongTheTelegramSilence()
    {
        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PC, T0, T0.AddMinutes(AwayMode_Policy.AWAY_AFTER_MINUTES)));
        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PC, T0, T0.AddHours(9)));
    }

    /// <summary>
    /// Presence is a FACT the owner stated; "nobody is waiting" and "they have been silent" are
    /// inferences about the same thing. The fact has to win, so being at the pc is checked before
    /// either — and this pins that it is not merely one more term that a strong enough silence
    /// could outvote.
    /// </summary>
    [Fact]
    public void AtThePc_OutranksEveryOtherInput()
    {
        foreach (var anyQuiet in new[] { true, false })
        {
            foreach (var minutes in new[] { 0, 14, 15, 600 })
                Assert.False(AwayMode_Policy.Should_EnterAway(anyQuiet, AT_THE_PC, T0, T0.AddMinutes(minutes)));
        }
    }

    /// <summary>
    /// The state, not just the transition. A spell that began while the owner was out and was still
    /// running when they sat down must END — guarding only the entry would leave it standing until
    /// they next typed into Telegram, which is the chore /pc exists to spare them.
    /// </summary>
    [Fact]
    public void AwayThatWasAlreadyOn_EndsWhenTheOwnerReachesAPc()
    {
        Assert.True(AwayMode_Policy.Should_LeaveAway(awayActive: true, ownerAtAPc: AT_THE_PC));
    }

    /// <summary>
    /// And it says nothing about the other direction: away that is off stays off, and an owner on
    /// their phone is not a reason to end a spell — only their speaking is, which is a different
    /// path entirely (Note_OwnerSpoke_AndWasAway).
    /// </summary>
    [Theory]
    [InlineData(false, AT_THE_PC)]
    [InlineData(true, AT_THE_PHONE)]
    [InlineData(false, AT_THE_PHONE)]
    public void LeavingAway_IsOnlyForAnActiveSpellAndAnOwnerAtAPc(bool awayActive, bool ownerAtAPc)
    {
        Assert.False(AwayMode_Policy.Should_LeaveAway(awayActive, ownerAtAPc));
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
///
/// NEITHER STATE TOUCHES A TOPIC NAME ANY MORE (owner, 2026-09-10): ✈ and 🤐 moved to PULSE's header
/// line, because both are app-wide and a name carrying them renamed every open topic the moment the
/// owner toggled one — and every rename writes a service message into the thread it renames. Three
/// tests here asserted them in a name and are replaced below by one asserting they are absent; the
/// claims themselves — away beside the mode glyph, quiet on its own topic, away superseding quiet —
/// are now pinned on the header in TopicStatusLineBuilderTests. What stays here is what did not
/// move: the glyph constants, the quiet notice, and the strip that keeps old names clean.
/// </summary>
public class AwayTopicGlyphTests
{
    [Fact]
    public void EveryStateGlyph_IsDistinct()
    {
        string[] glyphs =
        [
            TelegramDeliveryMode_Glyphs.DEFERRED,
            TelegramDeliveryMode_Glyphs.SILENCED,
            TelegramDeliveryMode_Glyphs.AWAY,
            TelegramDeliveryMode_Glyphs.QUIET,
        ];

        Assert.Equal(glyphs.Length, glyphs.Distinct().Count());
        Assert.Equal(TelegramDeliveryMode_Glyphs.AWAY, AwayMode_Policy.AWAY_GLYPH);
        Assert.Equal(TelegramDeliveryMode_Glyphs.QUIET, AwayMode_Policy.QUIET_GLYPH);
    }

    /// <summary>
    /// REPLACES AwayAndMode_ShowTogether ("✈ crm bug", "✈ 🔕 crm bug", "🌙 crm bug"),
    /// Quiet_ShowsOnItsOwnTopic ("🤐 crm bug", "🤐 🌙 crm bug") and Away_SupersedesQuiet ("✈ crm bug"
    /// with both set). All three were right until 2026-09-10 and all three claims survive — on
    /// PULSE's header line, where TopicStatusLineBuilderTests now pins them. What they can no longer
    /// claim is that a topic NAME says any of it.
    ///
    /// THE COST WAS THE POINT: away and quiet are app-wide, so a single toggle renamed every open
    /// topic at once, and Telegram writes a service message into each thread it renames — one state
    /// change the owner had just made themselves, announced back to them once per orchestration. On
    /// the header the same fact costs one silent edit of a message that was being edited anyway.
    /// </summary>
    [Fact]
    public void AwayAndQuietNoLongerReachATopicName()
    {
        var name = TelegramDeliveryMode_Glyphs.Compose_TopicName(
            "crm bug",
            new TelegramDeliveryMode_Glyphs.TopicNameFlags(OwnerReply: OwnerReplyStates.Blocking, IsAwaitingTest: true));

        Assert.DoesNotContain(TelegramDeliveryMode_Glyphs.AWAY, name);
        Assert.DoesNotContain(TelegramDeliveryMode_Glyphs.QUIET, name);

        // And the name still says everything it IS responsible for, so the absence above is a
        // narrowed surface rather than a broken one.
        Assert.Equal("❓ 🧪 crm bug", name);
    }

    [Fact]
    public void TheQuietNotice_MarksWhereInTheConversationItStopped()
    {
        Assert.Contains("going quiet", AwayMode_Policy.QUIET_ON_NOTICE);
        Assert.Contains("stops asking", AwayMode_Policy.QUIET_ON_NOTICE);
        Assert.Contains("Reply", AwayMode_Policy.QUIET_ON_NOTICE);
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
    [InlineData("🤐 crm bug")]
    [InlineData("🤐 🌙 crm bug")]
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

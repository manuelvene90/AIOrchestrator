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
/// immediately; AWAY is the conclusion drawn after the away delay (`away.afterMinutes`: 15 shipped,
/// 60 under classic, 0 = never by itself).
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

    /// <summary>
    /// Today's delay, the shipped default of <c>away.afterMinutes</c> — every case below that is not about
    /// the delay itself runs on it. A literal, so the cases pin a number and not whatever the constant says.
    /// </summary>
    const int FIFTEEN = 15;

    /// <summary>AT_THE_PC / AT_THE_PHONE name the ownerAtAPc argument at every call below.</summary>
    const bool AT_THE_PC = true;

    const bool AT_THE_PHONE = false;

    [Fact]
    public void Away_NeedsBothSomeoneWaitingAndFifteenMinutesOfSilence()
    {
        Assert.True(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, T0, T0.AddMinutes(FIFTEEN), FIFTEEN));
    }

    /// <summary>
    /// Silence with nobody waiting is not absence — it usually means there was nothing to say.
    /// Tripping away mode then would announce a state change for no reason.
    /// </summary>
    [Fact]
    public void Silence_WithNoOrchestrationWaiting_IsNotAway()
    {
        Assert.False(AwayMode_Policy.Should_EnterAway(false, AT_THE_PHONE, T0, T0.AddHours(6), FIFTEEN));
    }

    /// <summary>
    /// The chatty-supervisor case: three questions in one minute means the SUPERVISOR is noisy, not
    /// that the owner left. Quiet still fires (harmless, unannounced), away must not.
    /// </summary>
    [Fact]
    public void ABurstOfQuestions_GoesQuietButDoesNotGoAway()
    {
        Assert.True(AwayMode_Policy.Should_GoQuiet(3));
        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, T0, T0.AddMinutes(1), FIFTEEN));
        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, T0, T0.AddMinutes(14), FIFTEEN));
    }

    /// <summary>
    /// The clock runs on the owner's last message ANYWHERE: chatting in one topic proves they are
    /// present for all of them, so a quiet orchestration must not drag everything into away mode.
    /// </summary>
    [Fact]
    public void PresenceInAnyTopic_KeepsEverythingOutOfAway()
    {
        var chattedRecently = T0.AddMinutes(14);

        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, chattedRecently, T0.AddMinutes(20), FIFTEEN));
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
        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PC, T0, T0.AddMinutes(FIFTEEN), FIFTEEN));
        Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PC, T0, T0.AddHours(9), FIFTEEN));
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
                Assert.False(AwayMode_Policy.Should_EnterAway(anyQuiet, AT_THE_PC, T0, T0.AddMinutes(minutes), FIFTEEN));
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

    // -----------------------------------------------------------------------------------
    // The delay is a setting — owner, 2026-09-23 (entry [95]), plan 03 task 18
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// THE OWNER'S REPORT: <i>"the away mode is triggered too soon all the time. That also should be a
    /// setting."</i> The delay is handed in, and the policy obeys whatever it is given: today's 15, the
    /// owner's 60 (classic), one minute short of either holds and the minute itself starts away.
    /// </summary>
    [Theory]
    [InlineData(15, 14, false)]
    [InlineData(15, 15, true)]
    [InlineData(60, 15, false)]
    [InlineData(60, 20, false)]
    [InlineData(60, 59, false)]
    [InlineData(60, 60, true)]
    [InlineData(60, 600, true)]
    public void Away_StartsAfterTheConfiguredSilence(int awayAfterMinutes, int silentMinutes, bool expected)
    {
        Assert.Equal(expected, AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, T0, T0.AddMinutes(silentMinutes), awayAfterMinutes));
    }

    /// <summary>
    /// ZERO IS "NEVER BY ITSELF", NOT "AT ONCE". Read literally, a zero-minute delay would put the owner in
    /// away mode on the first tick after the third unanswered message — the opposite of what someone
    /// setting it to zero is asking for. No silence, however long, starts it.
    /// </summary>
    [Fact]
    public void AZeroDelay_NeverStartsAway_HoweverLongTheSilence()
    {
        foreach (var minutes in new[] { 0, 1, 15, 60, 1440, 14 * 1440 })
            Assert.False(AwayMode_Policy.Should_EnterAway(true, AT_THE_PHONE, T0, T0.AddMinutes(minutes), awayAfterMinutes: 0));
    }

    /// <summary>
    /// THE SHIPPED DEFAULT IS TODAY'S BEHAVIOUR (ruling R14): fifteen minutes, read by the catalogue row
    /// from the one constant — and the row is the only reader, so the constant is not the rule.
    /// </summary>
    [Fact]
    public void TheShippedDelay_IsTodaysFifteenMinutes()
    {
        Assert.Equal(FIFTEEN, AwayMode_Policy.DEFAULT_AWAY_AFTER_MINUTES);
        Assert.Equal(
            "15",
            AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog.Find_OrNull("away.afterMinutes")!.Default_OrNull!.ToJsonString());
    }

    /// <summary>
    /// THE HOLD ENTRY QUOTES THE DELAY IN FORCE. The session is told how long the owner has before away
    /// mode starts, and a number that is not the configured one is a promise the app then breaks.
    /// </summary>
    [Fact]
    public void TheHoldNotice_QuotesTheConfiguredDelay()
    {
        var notice = AwayMode_Policy.Build_HoldNotice(60);

        Assert.Contains("If they stay silent for 60 minutes you will get an AWAY MODE ON entry", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("15 minutes", notice, StringComparison.Ordinal);
        Assert.StartsWith("3 of your messages are unanswered.", notice, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND AT ZERO IT SAYS NONE WILL COME, rather than promising an AWAY MODE ON entry "after 0 minutes"
    /// that the app will never write.
    /// </summary>
    [Fact]
    public void TheHoldNotice_AtZero_SaysAwayModeWillNotStartByItself()
    {
        var notice = AwayMode_Policy.Build_HoldNotice(0);

        Assert.DoesNotContain("0 minutes", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("you will get an AWAY MODE ON entry", notice, StringComparison.Ordinal);
        Assert.Contains("away mode does not start by itself", notice, StringComparison.Ordinal);
        Assert.Contains("if they reply, everything returns to normal on its own", notice, StringComparison.Ordinal);
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
    ///
    /// UNDER THE SHIPPED PLACEMENT (plan 03 Task 7). Classic's <c>topic.modeGlyphs = name</c> puts
    /// them back, as master drew them — TelegramDeliveryModeGlyphsTests pins that half. And away and
    /// quiet are SET here now: the test used to pass flags that could not carry them at all, so it
    /// could not have failed.
    /// </summary>
    [Fact]
    public void AwayAndQuietDoNotReachATopicName_UnderTheShippedPlacement()
    {
        var name = TelegramDeliveryMode_Glyphs.Compose_TopicName(
            "crm bug",
            new TelegramDeliveryMode_Glyphs.TopicNameFlags(OwnerReply: OwnerReplyStates.Blocking, IsAwaitingTest: true, IsAway: true, IsQuiet: true),
            ModeGlyphPlacements.PulseHeader);

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

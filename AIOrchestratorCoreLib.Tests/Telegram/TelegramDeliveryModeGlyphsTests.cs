using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE TOPIC NAME'S WHOLE VOCABULARY, as the owner ruled it on 2026-09-10: ❓ when something waits on
/// them, then EXACTLY ONE state glyph — 🏁 closed, ✅ done, 🧪 awaiting test, ⏸ paused for a usage
/// limit — and nothing else. Names are recomposed on every rename, so nothing may ever accumulate.
///
/// REWRITTEN 2026-09-10, when `Compose_TopicName` replaced `Decorate_TopicName`. Two rules changed
/// underneath these tests and both are now asserted rather than assumed:
///
/// THE FIVE DELIVERY GLYPHS LEFT THE NAME — 🌙 deferred, 🔕 silenced, ✈ away, 🤐 quiet, 💻 terminal —
/// for PULSE's header line. They say how the app is DELIVERING, which is not the question a topic
/// list is read to answer, and two of them are app-wide: on a name, one toggle renamed every open
/// topic at once, and every rename writes a service message into the thread it renames. The tests
/// that pinned them here were right when written and are REPLACED below by tests pinning their
/// absence; their claims now live on PULSE's header in TopicStatusLineBuilderTests.
///
/// ⛔ WAS FOLDED INTO ❓. It was split off on 2026-08-19 so the owner could tell "waiting on you"
/// from "waiting on you AND stopped"; they retired the distinction on 2026-09-10 because both mean
/// they have to do something. The enum keeps the two states — the app acts on them — so the fold-in
/// is asserted as an EQUALITY between the two composed names, and re-splitting them fails a test.
///
/// `Strip_Glyph` STILL KNOWS THE DEPARTED GLYPHS, and that is deliberate migration support rather
/// than dead code: every topic in the owner's list was named by the previous build, so the first
/// rename after this change has to be able to take a moon off a name nothing will ever put a moon on
/// again.
/// </summary>
public class TelegramDeliveryModeGlyphsTests
{
    /// <summary>
    /// The five that moved to PULSE's header on 2026-09-10, as a list to sweep a name against. A
    /// sweep rather than five asserts because the failure this guards is a REGRESSION of the move —
    /// somebody wiring a delivery fact back into the name — and that would arrive through whichever
    /// glyph they picked.
    /// </summary>
    static readonly string[] DEPARTED_FROM_THE_NAME =
    [
        TelegramDeliveryMode_Glyphs.DEFERRED,
        TelegramDeliveryMode_Glyphs.SILENCED,
        TelegramDeliveryMode_Glyphs.AWAY,
        TelegramDeliveryMode_Glyphs.QUIET,
        TelegramDeliveryMode_Glyphs.TERMINAL,
    ];

    /// <summary>Nothing waiting and nothing to report draws nothing — the default must stay silent.</summary>
    [Fact]
    public void NoFlagsAtAllLeavesTheBareName()
    {
        Assert.Equal("crm bug", Name(new()));
    }

    /// <summary>
    /// THE OWNER ASKED TO SEE IT FROM THE TOPIC LIST (2026-08-19): "from the topic name I should be
    /// able to immediately understand if some topic needs me for a response". It is the only glyph
    /// here that asks something OF them; every other one describes where the work stands.
    /// </summary>
    [Fact]
    public void ATopicWaitingOnTheOwnerSaysSo()
    {
        Assert.Equal("❓ crm bug", Name(new(OwnerReply: OwnerReplyStates.Wanted)));
    }

    /// <summary>
    /// REPLACES AWaitingTopicSaysSo_AndSaysWhetherItIsStopped, which asserted "❓ crm bug" for Wanted
    /// and "⛔ crm bug" for Blocking. That was right from 2026-08-19, when the owner asked to see
    /// "whether blocking or not" from the list; they retired the distinction on 2026-09-10 — *"⛔ is
    /// folded into ❓ (same meaning for the owner)"* — because both states mean they have to do
    /// something, and which one it is does not change what they do next.
    ///
    /// ASSERTED AS AN EQUALITY BETWEEN THE TWO NAMES, not as two expectations of the same literal.
    /// <see cref="OwnerReplyStates"/> still carries the distinction, because the app acts on it, so
    /// the day someone gives the blocking state its own character back this is the test that says so.
    /// </summary>
    [Fact]
    public void ABlockingWaitDrawsTheSameQuestionMarkAsAWantedOne()
    {
        var wanted = Name(new(OwnerReply: OwnerReplyStates.Wanted));
        var blocking = Name(new(OwnerReply: OwnerReplyStates.Blocking));

        Assert.Equal(wanted, blocking);
        Assert.Equal("❓ crm bug", blocking);
    }

    /// <summary>
    /// CLOSED — the orchestration is over. Its topic is normally DELETED on close, so this is what
    /// the owner sees in the window before a delete that has not happened yet, or will never happen:
    /// Telegram refuses to delete some topics, and a closed endeavour still wearing its working name
    /// is one they cannot tell from a live one.
    /// </summary>
    [Fact]
    public void AClosedTopicWearsTheChequeredFlag()
    {
        Assert.Equal("🏁 crm bug", Name(new(IsClosed: true)));
    }

    /// <summary>
    /// /done — the owner's own "I have checked it, leave the topic open", asked for on 2026-08-21:
    /// *"a different icon that lets me remember the topic is finished, but I still don't want to
    /// close the topic in case I have something else to do later."*
    /// </summary>
    [Fact]
    public void ADoneTopicWearsTheOwnersOwnTick()
    {
        Assert.Equal("✅ crm bug", Name(new(IsDone: true)));
    }

    /// <summary>
    /// /test — finished, but NOT yet tested by the owner, so do not close it. Their own workflow made
    /// visible: they were muting completed endeavours and then remembering, unaided, which of the
    /// muted ones still needed testing (2026-08-19).
    /// </summary>
    [Fact]
    public void AnAwaitingTestTopicWearsTheFlask()
    {
        Assert.Equal("🧪 crm bug", Name(new(IsAwaitingTest: true)));
    }

    /// <summary>
    /// PAUSED FOR A USAGE LIMIT — waiting for a window to reset, not stalled. It is on the NAME
    /// rather than only in PULSE because it is the state most likely to be mistaken for a stall: a
    /// topic that has gone quiet reads as broken, and the false alerts of 2026-09-09 were exactly
    /// this state being reported as "waiting on your reply".
    /// </summary>
    [Fact]
    public void ATopicPausedForAUsageLimitWearsThePauseSign()
    {
        Assert.Equal("⏸ crm bug", Name(new(IsPausedForUsageLimit: true)));
    }

    /// <summary>
    /// EXACTLY ONE STATE GLYPH, MOST-FINAL-FIRST: 🏁 then ✅ then 🧪 then ⏸. A closed endeavour is not
    /// also awaiting a test, and a topic the owner has finished with does not need to say why the
    /// machine stopped. Each glyph REPLACES the ones below it — stating one fact twice is what made
    /// this list too long to read.
    ///
    /// The three adjacent pairs get a test each rather than one test of the whole chain: a chain
    /// asserted end-to-end passes on more than one ordering, and the point is that no single step can
    /// flip unnoticed.
    /// </summary>
    [Fact]
    public void AClosedTopicDoesNotAlsoSayItIsDone()
    {
        Assert.Equal("🏁 crm bug", Name(new(IsClosed: true, IsDone: true)));
    }

    /// <summary>
    /// DONE OUTRANKS AWAITING-TEST — the owner's own rule of 2026-08-21, kept verbatim through the
    /// 2026-09-10 rewrite. 🧪 asks them to go and test something; ✅ records that the asking is over.
    /// Showing the request on a topic they have already signed off would tell them to do a job they
    /// have done.
    /// </summary>
    [Fact]
    public void ADoneTopicDoesNotAlsoAskToBeTested()
    {
        Assert.Equal("✅ crm bug", Name(new(IsAwaitingTest: true, IsDone: true)));
    }

    /// <summary>
    /// AWAITING-TEST OUTRANKS A USAGE PAUSE. 🧪 is a job for the owner; ⏸ is a fact about the machine
    /// they can do nothing about (decision 15's test, applied inside one field). A reminder they can
    /// act on must not be displaced by one they cannot.
    /// </summary>
    [Fact]
    public void AnAwaitingTestTopicDoesNotSayWhyTheMachineStopped()
    {
        Assert.Equal("🧪 crm bug", Name(new(IsPausedForUsageLimit: true, IsAwaitingTest: true)));
    }

    /// <summary>
    /// The non-adjacent pairs, so the chain cannot be satisfied by a partial ordering — 🏁 beating ✅
    /// and ✅ beating 🧪 does not by itself prove 🏁 beats 🧪 in an implementation that tests the flags
    /// in some other sequence.
    /// </summary>
    [Fact]
    public void TheMostFinalStateWinsAcrossTheNonAdjacentPairsToo()
    {
        Assert.Equal("🏁 crm bug", Name(new(IsClosed: true, IsAwaitingTest: true)));
        Assert.Equal("🏁 crm bug", Name(new(IsClosed: true, IsPausedForUsageLimit: true)));
        Assert.Equal("✅ crm bug", Name(new(IsPausedForUsageLimit: true, IsDone: true)));
    }

    /// <summary>And with every state flag set at once, still one glyph, still the most final one.</summary>
    [Fact]
    public void EveryStateFlagAtOnceStillDrawsOneGlyph()
    {
        Assert.Equal(
            "🏁 crm bug",
            Name(new(IsPausedForUsageLimit: true, IsClosed: true, IsAwaitingTest: true, IsDone: true)));
    }

    /// <summary>
    /// ❓ IS OUTERMOST — the owner asked for it "at the beginning of the topic name, to concatenate
    /// with other possible icons". It CONCATENATES rather than replacing, and it is not swallowed by
    /// a state glyph: a finished topic must not hide a question that is somehow still outstanding on
    /// it, since that glyph is the only one asking something OF the owner.
    ///
    /// Each state glyph gets its own assertion. A prefix that swallowed exactly one of them would
    /// pass a test that only ever tried a single pairing.
    /// </summary>
    [Fact]
    public void TheReplyGlyphLeadsTheStateGlyph()
    {
        Assert.Equal("❓ 🏁 crm bug", Name(new(OwnerReply: OwnerReplyStates.Wanted, IsClosed: true)));
        Assert.Equal("❓ ✅ crm bug", Name(new(OwnerReply: OwnerReplyStates.Wanted, IsDone: true)));
        Assert.Equal("❓ 🧪 crm bug", Name(new(OwnerReply: OwnerReplyStates.Wanted, IsAwaitingTest: true)));
        Assert.Equal("❓ ⏸ crm bug", Name(new(OwnerReply: OwnerReplyStates.Wanted, IsPausedForUsageLimit: true)));
    }

    /// <summary>
    /// THE HEART OF THE 2026-09-10 CHANGE, asserted directly: no combination of flags can put a
    /// DELIVERY glyph in a topic name any more. 🌙 🔕 ✈ 🤐 💻 describe how the app is delivering, and
    /// away and quiet are app-wide — on a name, one toggle renamed every open topic and wrote a
    /// service message into each of the owner's threads to tell them something they had just done
    /// themselves.
    ///
    /// SWEPT OVER EVERY COMBINATION OF THE CURRENT FLAGS rather than spot-checked, because a
    /// regression would arrive as a new BRANCH — a corner of the existing flags that no literal
    /// example happens to visit — and mutation-testing confirmed exactly that: a glyph emitted only
    /// for Blocking-plus-all-four-state-flags is caught by this test and by nothing else.
    ///
    /// WHAT IT DOES NOT COVER, said plainly because the first version of this summary claimed it did:
    /// a NEW flag. `All_FlagCombinations` is a hand-written loop over the five members
    /// `TopicNameFlags` has today, so adding a sixth silently leaves half the space unswept. There is
    /// no reflection over the record here on purpose — it would be a cleverer test that fails for
    /// reasons unrelated to the rule — so this is a fact about the guard, not a hole to be hidden.
    /// </summary>
    [Fact]
    public void TheFiveDepartedGlyphsNeverAppearInAName_WhateverTheFlags()
    {
        List<string> offenders = [];

        foreach (var flags in All_FlagCombinations())
        {
            var name = Name(flags);

            foreach (var glyph in DEPARTED_FROM_THE_NAME)
                if (name.Contains(glyph, StringComparison.Ordinal))
                    offenders.Add($"{glyph} in \"{name}\" for {flags}");
        }

        Assert.True(offenders.Count == 0, $"delivery glyphs are back on the name:\n{string.Join("\n", offenders)}");
    }

    /// <summary>
    /// The same sweep for ⛔, which is retired rather than moved: it has no second home, and the
    /// constant survives only so <c>Strip_Glyph</c> can take it off a name an older build wrote. A
    /// name that still drew it would be a name no rename could ever clean.
    /// </summary>
    [Fact]
    public void TheRetiredBlockingGlyphNeverAppearsInAName_WhateverTheFlags()
    {
        List<string> offenders = [];

        foreach (var flags in All_FlagCombinations())
        {
            var name = Name(flags);

            if (name.Contains(TelegramDeliveryMode_Glyphs.REPLY_BLOCKING, StringComparison.Ordinal))
                offenders.Add($"\"{name}\" for {flags}");
        }

        Assert.True(offenders.Count == 0, $"⛔ is back on the name:\n{string.Join("\n", offenders)}");
    }

    /// <summary>
    /// REPLACES AMutedTopicKeepsItsBell_EvenWhenSomeoneIsWaitingOnTheOwner, which asserted
    /// "❓ 🔕 crm bug" and "❓ 🌙 crm bug". Its incident was real — on 2026-08-20 the owner muted a
    /// topic and no bell appeared, and although that turned out to be Telegram's own mute, the reply
    /// glyphs had just been put IN FRONT of the mode glyph, so a prefix that swallowed the bell would
    /// have produced the identical symptom.
    ///
    /// THE BELL IS NOT ON THE NAME ANY MORE (owner, 2026-09-10), so the claim inverts: a muted topic's
    /// name says nothing about mute, and the mute state is read from PULSE's header instead. The
    /// swallowing hazard the old test guarded is now covered by
    /// <see cref="TheReplyGlyphLeadsTheStateGlyph"/>, where ❓ has real glyphs to lead.
    /// </summary>
    [Fact]
    public void AMutedTopicNoLongerCarriesABell_TheBellMovedToPulsesHeader()
    {
        var waiting = Name(new(OwnerReply: OwnerReplyStates.Wanted));

        Assert.Equal("❓ crm bug", waiting);
        Assert.DoesNotContain(TelegramDeliveryMode_Glyphs.SILENCED, waiting);
        Assert.DoesNotContain(TelegramDeliveryMode_Glyphs.DEFERRED, waiting);
    }

    /// <summary>
    /// 🏁 MEANS CLOSED ON A TOPIC NAME AND NOTHING ELSE, which is why the ledger recap gave the
    /// character up on 2026-09-10 (owner's ruling: the recap takes another glyph, 🏁 stays for
    /// closed). Two different finished-somethings sharing one symbol is the collision ✅'s own
    /// reasoning refused when it was chosen over 🏁 for /done; the recap was the same clash from the
    /// other side.
    ///
    /// It reads both constants rather than asserting the literal 🎯, so the day someone moves the
    /// recap back onto 🏁 this fails wherever they do it.
    /// </summary>
    [Fact]
    public void TheClosedGlyphAndTheLedgerRecapGlyphAreDifferentCharacters()
    {
        Assert.NotEqual(TelegramDeliveryMode_Glyphs.CLOSED, LedgerTransition_Wording.RECAP_GLYPH);
    }

    /// <summary>
    /// The five the name CAN draw must be five different characters — two states sharing a symbol in
    /// the topic list is worse than no symbol, and the list is read at a glance with no legend.
    /// </summary>
    [Fact]
    public void EveryGlyphTheNameCanDrawIsDistinct()
    {
        string[] glyphs =
        [
            TelegramDeliveryMode_Glyphs.REPLY_WANTED,
            TelegramDeliveryMode_Glyphs.CLOSED,
            TelegramDeliveryMode_Glyphs.DONE,
            TelegramDeliveryMode_Glyphs.AWAITING_TEST,
            TelegramDeliveryMode_Glyphs.PAUSED_FOR_LIMIT,
        ];

        Assert.Equal(glyphs.Length, glyphs.Distinct().Count());
    }

    /// <summary>
    /// REPLACES Strip_ThenDecorate_NeverStacksGlyphs, same claim through the new entry point: a name
    /// is stripped before it is recomposed on every rename, so "🔕 🌙 🔕 crm bug" can never become
    /// permanent litter in the owner's topic list.
    ///
    /// The inline cases now include names the PREVIOUS build wrote, because those are the ones the
    /// first rename after this change actually meets.
    /// </summary>
    [Theory]
    [InlineData("crm bug")]
    [InlineData("✅ crm bug")]
    [InlineData("🏁 crm bug")]
    [InlineData("❓ ⏸ crm bug")]
    [InlineData("⛔ ✈ 🔕 crm bug")]
    [InlineData("❓ 💻 🧪 crm bug")]
    public void Strip_ThenCompose_NeverStacksGlyphs(string currentName)
    {
        var baseName = TelegramDeliveryMode_Glyphs.Strip_Glyph(currentName);

        Assert.Equal("crm bug", baseName);
        Assert.Equal("✅ crm bug", TelegramDeliveryMode_Glyphs.Compose_TopicName(baseName, new(IsDone: true)));
    }

    /// <summary>
    /// THE MIGRATION, ASSERTED. Strip has to keep removing the glyphs the name no longer draws —
    /// 🌙 🔕 ✈ 🤐 💻 and the retired ⛔ — because every topic the owner has open was named by the
    /// build before this one. Narrowing this list to what is currently drawn would strand the old
    /// glyph on the name for as long as the topic lives.
    ///
    /// Each glyph is measured, not assumed: ✈ is one UTF-16 unit and the emoji are two, and the
    /// fallback in Leading_GlyphLength silently removes two units of whatever it is handed.
    /// </summary>
    [Theory]
    [InlineData("🌙 crm bug")]
    [InlineData("🔕 crm bug")]
    [InlineData("✈ crm bug")]
    [InlineData("🤐 crm bug")]
    [InlineData("💻 crm bug")]
    [InlineData("⛔ crm bug")]
    [InlineData("✈ 💻 crm bug")]
    [InlineData("🤐 🌙 crm bug")]
    [InlineData("⛔ ✈ 🔕 crm bug")]
    public void Strip_StillRemovesTheGlyphsNoNameWillEverCarryAgain(string decorated)
    {
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph(decorated));
    }

    /// <summary>
    /// And the ones the name still draws, including the two that had no strip coverage before this
    /// rewrite — ⏸ and 🏁. Without it every rename stacks another glyph onto the name.
    /// </summary>
    [Theory]
    [InlineData("❓ crm bug")]
    [InlineData("🏁 crm bug")]
    [InlineData("✅ crm bug")]
    [InlineData("🧪 crm bug")]
    [InlineData("⏸ crm bug")]
    [InlineData("❓ 🏁 crm bug")]
    [InlineData("❓ ✅ crm bug")]
    [InlineData("❓ 🧪 crm bug")]
    [InlineData("❓ ⏸ crm bug")]
    public void Strip_RemovesEveryGlyphTheNameStillDraws(string decorated)
    {
        Assert.Equal("crm bug", TelegramDeliveryMode_Glyphs.Strip_Glyph(decorated));
    }

    /// <summary>
    /// A LEADING glyph is decoration; an emoji INSIDE the owner's own name is theirs. Stripping by
    /// "contains an emoji" would quietly rewrite the names they typed.
    /// </summary>
    [Fact]
    public void Strip_LeavesANameThatMerelyCONTAINSAnEmojiAlone()
    {
        Assert.Equal("release 🔔 candidate", TelegramDeliveryMode_Glyphs.Strip_Glyph("release 🔔 candidate"));
    }

    /// <summary>One base name for every case, so an expectation reads as its glyphs and nothing else.</summary>
    static string Name(TelegramDeliveryMode_Glyphs.TopicNameFlags flags)
    {
        return TelegramDeliveryMode_Glyphs.Compose_TopicName("crm bug", flags);
    }

    /// <summary>
    /// Every value the flags can take — 3 reply states × 4 booleans. Small enough to sweep whole,
    /// which is what makes the "departed glyph" tests claims about the FUNCTION rather than about
    /// six examples of it.
    /// </summary>
    static IEnumerable<TelegramDeliveryMode_Glyphs.TopicNameFlags> All_FlagCombinations()
    {
        bool[] bothWays = [false, true];

        foreach (var reply in Enum.GetValues<OwnerReplyStates>())
            foreach (var paused in bothWays)
                foreach (var closed in bothWays)
                    foreach (var awaitingTest in bothWays)
                        foreach (var done in bothWays)
                            yield return new TelegramDeliveryMode_Glyphs.TopicNameFlags(
                                OwnerReply: reply,
                                IsPausedForUsageLimit: paused,
                                IsClosed: closed,
                                IsAwaitingTest: awaitingTest,
                                IsDone: done);
    }
}

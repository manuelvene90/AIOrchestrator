using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// WHICH QUESTION THE OWNER STILL OWES AN ANSWER TO — and, far more often, none.
///
/// <para>
/// THE FIRST RULE (2026-08-15) was "alert only if I owe you a reply", after the ⚠️ fired on plain
/// silence: *"quiet for 28 min and no session is working — text it to wake it up"* on two topics,
/// answered *"Makes no sense."* The channel was quiet because THEY had stopped texting.
/// </para>
/// <para>
/// THE SECOND RULE (2026-09-09) is this file's subject, and it came from the same alert getting it
/// wrong the other way. "The session spoke last" is not a debt: on 2026-09-09 the owner was told
/// *"has been waiting on your reply for 25 min"* at 17:30 — thirty minutes after the supervisor
/// wrote **"Nothing more needed from you"** — then again at 18:38, while the supervisor was paused
/// for a usage limit, and again at 20:25; three more on a second topic. Six false alerts in an
/// evening, each indistinguishable from a real one, which is what makes them expensive.
/// </para>
/// <para>
/// So the decider now answers WHICH question, by entry index: only a declared question counts, and
/// the index is what lets the caller alert once per question instead of once per stretch of silence.
/// </para>
/// </summary>
public class OwnerOwesReplyDeciderTests
{
    const string QUESTION_BODY = "QUESTION: merge wf-perf now?\nOPTION: Merge it\nOPTION: Hold";
    const string BLOCKED_BODY = "BLOCKED ON OWNER — I need the staging token before anything else moves.";
    const string PLAIN_BODY = "Two fixes landed and the suite is green. Nothing more needed from you.";

    static IChannelEntry Entry(int index, ChannelAuthors author, string body = PLAIN_BODY)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-09-09 17:00", "subject", body, $"## [{index}] FROM {author}\n{body}");
    }

    /// <summary>
    /// THE KEY IS WHAT THE QUESTION SAYS, NOT WHERE IT SITS — the owner's correction of 2026-09-10,
    /// on the grounds of decision 12: the `[n]` in a channel header is agent-written and is a guess
    /// unless the writer re-read the file.
    ///
    /// <para>
    /// BOTH FAILURES THE INDEX PRODUCED ARE ASSERTED HERE, because each is silent on its own and
    /// each would look like the other's fix. On a DUPLICATE index — `option-lab-2` carried two `[80]`
    /// and two `[81]` in one evening — two genuinely different questions shared one key, and the ⚠️
    /// fires once per key, so the second question was never alerted about at all. Across a
    /// COMPACTION that renumbers entries, the same question got a new key and bought a second alert
    /// about a debt the owner already knew of.
    /// </para>
    /// <para>
    /// The third case is the author: the same words from a supervisor and from a solo are two debts
    /// on two channels, so they must not collapse into one key.
    /// </para>
    /// </summary>
    [Fact]
    public void TwoQuestionsSharingAnIndexAreTwoQuestions_AndOneQuestionRenumberedIsStillOne()
    {
        var asked = Entry(80, ChannelAuthors.Supervisor, QUESTION_BODY);
        var anotherAskedUnderTheSameIndex = ChannelEntry_Factory.Create(
            80, ChannelAuthors.Supervisor, "2026-09-09 17:00", "a different subject", QUESTION_BODY,
            "## [80] FROM supervisor\n" + QUESTION_BODY);
        var theSameQuestionRenumbered = ChannelEntry_Factory.Create(
            3, ChannelAuthors.Supervisor, "2026-09-09 19:42", "subject", QUESTION_BODY,
            "## [3] FROM supervisor\n" + QUESTION_BODY);
        var theSameWordsFromASolo = Entry(80, ChannelAuthors.Solo, QUESTION_BODY);

        Assert.NotEqual(
            OwnerOwesReply_Decider.Identify_Question(asked),
            OwnerOwesReply_Decider.Identify_Question(anotherAskedUnderTheSameIndex));

        // The renumbered one also carries a LATER stamp, so this pins that neither the index nor the
        // clock is in the key — a re-stamped entry is not a new question either.
        Assert.Equal(
            OwnerOwesReply_Decider.Identify_Question(asked),
            OwnerOwesReply_Decider.Identify_Question(theSameQuestionRenumbered));

        Assert.NotEqual(
            OwnerOwesReply_Decider.Identify_Question(asked),
            OwnerOwesReply_Decider.Identify_Question(theSameWordsFromASolo));
    }

    /// <summary>
    /// THE BODY IS IN THE KEY, and nothing pinned that until a review traced the three assertions
    /// above against a key with the body deleted: they differ by SUBJECT, by INDEX and by AUTHOR
    /// respectively, so all three still passed. A one-line change dropping the body was invisible to
    /// the suite — and it reintroduces the exact collision those assertions claim to prevent, because
    /// role commands teach fixed subjects and agents reuse them: two different questions from one
    /// supervisor under one reused subject would share a key, and the second would be silent.
    ///
    /// This is the case where EVERYTHING ELSE IS EQUAL — same author, same subject, same index, same
    /// stamp — so it can only pass for the body.
    /// </summary>
    [Fact]
    public void TwoDifferentQuestionsUnderOneReusedSubjectAreStillTwoQuestions()
    {
        var askedAboutTheMigration = ChannelEntry_Factory.Create(
            7, ChannelAuthors.Supervisor, "2026-09-09 17:00", "a question for you",
            "QUESTION: proceed with the migration?", "## [7] FROM supervisor\nQUESTION: proceed with the migration?");

        var askedAboutTheRollback = ChannelEntry_Factory.Create(
            7, ChannelAuthors.Supervisor, "2026-09-09 17:00", "a question for you",
            "QUESTION: roll back stage 3 instead?", "## [7] FROM supervisor\nQUESTION: roll back stage 3 instead?");

        Assert.NotEqual(
            OwnerOwesReply_Decider.Identify_Question(askedAboutTheMigration),
            OwnerOwesReply_Decider.Identify_Question(askedAboutTheRollback));
    }

    [Fact]
    public void ADeclaredQuestionFromTheSupervisor_IsWhatTheOwnerOwes()
    {
        Assert.Equal(2, OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
            [Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Supervisor, QUESTION_BODY)])?.Index);
    }

    [Fact]
    public void ABlockedOnOwnerEntry_CountsAsAQuestion()
    {
        Assert.Equal(2, OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
            [Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Solo, BLOCKED_BODY)])?.Index);
    }

    /// <summary>
    /// THE DEFECT THIS FILE EXISTS FOR: the supervisor's last word was a report, and the old rule
    /// read it as a debt. The owner's example is in the class summary — an alert thirty minutes
    /// after "Nothing more needed from you".
    /// </summary>
    [Fact]
    public void APlainReportFromTheSupervisor_IsNotADebt()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
            [Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Supervisor)]));
    }

    [Fact]
    public void TheOwnerSpokeLast_TheyOweNothing()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
            [Entry(1, ChannelAuthors.Supervisor, QUESTION_BODY), Entry(2, ChannelAuthors.Owner)]));
    }

    /// <summary>
    /// A question the supervisor has since moved past is not revived. They asked, then reported
    /// something else — the owner is answering the conversation, not an archive.
    /// </summary>
    [Fact]
    public void AQuestionBehindALaterPlainEntry_IsNotOwedAnyMore()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
        [
            Entry(1, ChannelAuthors.Owner),
            Entry(2, ChannelAuthors.Supervisor, QUESTION_BODY),
            Entry(3, ChannelAuthors.Supervisor),
        ]));
    }

    /// <summary>
    /// APP ENTRIES ARE SKIPPED, unchanged from the first rule: the app writes precisely when
    /// somebody has been waiting, so counting one as the last word let its own status push silence
    /// the alert — a feature quietly disabling itself.
    /// </summary>
    [Fact]
    public void AnAppEntryAfterTheQuestion_DoesNotCancelTheDebt()
    {
        Assert.Equal(2, OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
        [
            Entry(1, ChannelAuthors.Owner),
            Entry(2, ChannelAuthors.Supervisor, QUESTION_BODY),
            Entry(3, ChannelAuthors.App),
        ])?.Index);
    }

    [Fact]
    public void AnAppEntryAfterTheOwner_DoesNotCreateADebt()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
            [Entry(1, ChannelAuthors.Supervisor, QUESTION_BODY), Entry(2, ChannelAuthors.Owner), Entry(3, ChannelAuthors.App)]));
    }

    [Fact]
    public void NobodyHasSpokenYet_NothingIsOwed()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull([]));
    }

    /// <summary>
    /// PROSE ENDING IN '?' IS DELIBERATELY NOT A QUESTION HERE, though the push filter treats it as
    /// one: pushing an ask-shaped line costs one message the owner can ignore, alerting on it costs
    /// a ⚠️ every 25 minutes about a rhetorical question. Their own ruling on the topic glyph
    /// (2026-08-25) drew the same line: *"it's not that it should be interpreted indirectly based on
    /// the presence of a ? here and there that could mean anything."*
    /// </summary>
    [Fact]
    public void ARhetoricalQuestionInProse_IsNotADeclaredQuestion()
    {
        Assert.Null(OwnerOwesReply_Decider.Find_UnansweredQuestion_OrNull(
            [Entry(1, ChannelAuthors.Owner), Entry(2, ChannelAuthors.Supervisor, "Why did that fail? Because the token was stale. Fixed.")]));
    }

    [Fact]
    public void Is_RealQuestion_ReadsTheTwoDeclaredShapesAndNothingElse()
    {
        Assert.True(OwnerOwesReply_Decider.Is_RealQuestion(QUESTION_BODY));
        Assert.True(OwnerOwesReply_Decider.Is_RealQuestion(BLOCKED_BODY));
        Assert.True(OwnerOwesReply_Decider.Is_RealQuestion("blocked on owner — lowercase still counts"));
        Assert.False(OwnerOwesReply_Decider.Is_RealQuestion(PLAIN_BODY));
        Assert.False(OwnerOwesReply_Decider.Is_RealQuestion(""));
    }
}

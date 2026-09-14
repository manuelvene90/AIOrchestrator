namespace AIOrchestratorCoreLib.Bridge;

/// <summary>What an inbound owner message is allowed to close.</summary>
public enum AnswerBindings
{
    /// <summary>Nothing was open. The message is an instruction, a comment, or small talk.</summary>
    NothingWasOpen,

    /// <summary>Exactly one question was open and the message reads as an answer to it.</summary>
    TheOnlyOpenQuestion,

    /// <summary>The message is itself ask-shaped, so it settles nothing.</summary>
    NothingItIsAQuestion,

    /// <summary>Two or more were open, so a message carrying no question id cannot say which.</summary>
    NothingItIsAmbiguous,

    /// <summary>
    /// The owner used Telegram's Reply on an open question: the one message that DOES carry a
    /// question id. It answers that question, however many others are open.
    /// </summary>
    TheQuestionItRepliesTo,

    /// <summary>The owner replied to some other message — they are pointing at that, not at a question.</summary>
    NothingItRepliesToSomethingElse,
}

/// <summary>What the owner's message was a Telegram reply to, as far as the question registry can tell.</summary>
public enum OwnerReplyTargets
{
    /// <summary>Not a reply (or a reply to the topic root, which Telegram attaches to everything).</summary>
    None,

    /// <summary>A reply to a question that is still open in this orchestration.</summary>
    AnOpenQuestion,

    /// <summary>A reply to anything else — a report, a closed question, their own message.</summary>
    AnotherMessage,
}

/// <summary>
/// WHICH QUESTION AN OWNER MESSAGE ANSWERS — and the answer is often "none of them".
///
/// <para>
/// The rule this replaces was stated in the engine's own comment: <i>any owner message answers
/// whatever was pending</i>. Every inbound message swept the whole orchestration's registry and
/// stamped the owner's words under every question it removed. Two failures the owner reported come
/// straight out of that one sentence:
/// </para>
/// <para>
/// <b>THEIR OWN QUESTION WAS RECORDED AS AN ANSWER.</b> Asked "where are we at?" while a merge
/// question was open, the topic showed <c>✅ answered: A che punto siamo?</c> — twice, because the
/// sweep stamps once per question it closes. They had asked something; the app filed it as a vote.
/// </para>
/// <para>
/// <b>ONE REPLY CLOSED FOUR QUESTIONS.</b> With a merge question and three others open, any single
/// word closed all four and wrote the same text under each. Three of those four were never answered
/// by anybody, and nothing anywhere recorded that.
/// </para>
/// <para>
/// <b>THE CODEBASE ALREADY BELIEVES THIS RULE — for taps.</b> A tap resolves by an unguessable
/// nonce precisely so a stale button cannot answer a question it was not offered for
/// (<see cref="Telegram.CallbackToken"/>). A typed reply carries no such token, so the binding has
/// to be inferred; where it cannot be inferred safely, the honest outcome is to bind nothing and
/// leave the question visibly open, not to guess.
/// </para>
/// <para>
/// <b>THE FAILURE DIRECTION IS CHOSEN.</b> An answer that ends in a question mark — "Stripe?" as a
/// reply — is read here as a question and closes nothing. The owner then sees a question still
/// carrying its buttons and taps it, which costs one gesture. The opposite mistake costs a decision
/// recorded that nobody took. A question left open is also not left alone: it keeps its deadline,
/// its half-window reminder and its place in <c>/pending</c>.
/// </para>
/// <para>
/// <b>NOTHING HERE UNBLOCKS OR BLOCKS THE SESSION.</b> The awaiting-answer flag is cleared by the
/// caller on every owner message regardless of what this decides — the owner spoke, so the
/// conversation moves. This decides only what may be recorded as answered and removed from the
/// registry.
/// </para>
/// </summary>
public static class AnswerBinding_Decider
{
    /// <param name="openQuestionCount">How many questions are open in THIS orchestration.</param>
    /// <param name="ownerMessageText">The owner's words, exactly as they arrived.</param>
    public static AnswerBindings Decide(int openQuestionCount, string ownerMessageText)
    {
        return Decide(openQuestionCount, ownerMessageText, OwnerReplyTargets.None);
    }

    /// <summary>
    /// THE REPLY IS THE LINK THE OWNER ASKED FOR. 2026-09-11, after two exported chats in which
    /// answers and questions had come apart: <i>"non c'è modo di linkare domande e risposte? Tipo con
    /// un rispondi? Avere più domande non lo vedo come un problema."</i> Telegram's Reply names the
    /// message it answers, so it is the one typed answer that needs no inference: it binds to that
    /// question even with several open — which is what makes several open questions fine. And a
    /// reply pointing at a report or at their own message is about THAT: it binds to nothing, even
    /// with a single question open (fincanva-6, 12:58, an instruction was stamped "✅ answered" under
    /// the only open question).
    /// </summary>
    /// <param name="ownerMessageText">The owner's own words — WITHOUT the quote a reply carries: the quoted question ends in "?" and would make every reply read as a question.</param>
    /// <param name="replyTarget">What the message was a Telegram reply to.</param>
    public static AnswerBindings Decide(int openQuestionCount, string ownerMessageText, OwnerReplyTargets replyTarget)
    {
        if (openQuestionCount <= 0)
            return AnswerBindings.NothingWasOpen;

        // ASK-SHAPED FIRST, so it reads the same whether one question is open or five. A message
        // that asks something is not an answer to anything, and the count cannot make it one — nor
        // can a Reply: "e se lo facessimo domani?" on a question is a question about it.
        if (OwnerPush_Policy.Asks_InProse(ownerMessageText))
            return AnswerBindings.NothingItIsAQuestion;

        if (replyTarget == OwnerReplyTargets.AnOpenQuestion)
            return AnswerBindings.TheQuestionItRepliesTo;

        if (replyTarget == OwnerReplyTargets.AnotherMessage)
            return AnswerBindings.NothingItRepliesToSomethingElse;

        if (openQuestionCount == 1)
            return AnswerBindings.TheOnlyOpenQuestion;

        return AnswerBindings.NothingItIsAmbiguous;
    }

    /// <summary>
    /// Whether the caller may close and stamp. Kept beside <see cref="Decide"/> so a call site never
    /// has to enumerate the negative cases and miss one as they grow.
    /// </summary>
    public static bool Binds(AnswerBindings binding)
    {
        return binding is AnswerBindings.TheOnlyOpenQuestion or AnswerBindings.TheQuestionItRepliesTo;
    }

    /// <summary>
    /// What the log should say. Every non-binding outcome is written down: a question that stays
    /// open because of a rule is a fact the owner may later ask about, and silence here would make
    /// the new behaviour indistinguishable from the old defect.
    /// </summary>
    public static string Describe(AnswerBindings binding, int openQuestionCount)
    {
        return binding switch
        {
            AnswerBindings.TheOnlyOpenQuestion =>
                "the owner's message closed the one question that was open",
            AnswerBindings.NothingItIsAQuestion =>
                $"the owner's message is itself a question — {openQuestionCount} question(s) stay open",
            AnswerBindings.TheQuestionItRepliesTo =>
                "the owner replied to one open question — it closed that one",
            AnswerBindings.NothingItRepliesToSomethingElse =>
                $"the owner replied to a message that is not an open question — {openQuestionCount} question(s) stay open",
            AnswerBindings.NothingItIsAmbiguous =>
                $"{openQuestionCount} questions were open and the reply names none of them — all stay open, "
                + "the owner can tap the one they meant",
            _ => "no question was open",
        };
    }
}

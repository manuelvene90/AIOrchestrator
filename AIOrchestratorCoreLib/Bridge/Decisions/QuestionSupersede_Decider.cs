using AIOrchestratorCoreLib.Bridge.EngineState;

namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// WHEN A NEW QUESTION MAY CLOSE AN OLD ONE, AND WHEN IT MAY NOT GO OUT AT ALL.
///
/// <para>
/// Stage 16 (2026-09-10) taught a newer question to close the older ones the owner had already
/// answered in words, and it read "the owner spoke" as "the owner answered". On 2026-09-11 in
/// fincanva-6 that asked the owner the same thing twice: they tapped «Sì» on a high-risk merge
/// question at 10:42 (local), the app edited it to show the read-back code and held the answer, and
/// before typing the code they asked "devo fare solo smoke?". The supervisor, told nothing of the
/// tap, answered and asked the merge question again word for word; the app counted the owner's
/// QUESTION as a reply in words, closed the tapped one as superseded — discarding the pending code —
/// and posted a second copy with live buttons. Three rules below, one per mistake in that chain.
/// </para>
/// </summary>
public static class QuestionSupersede_Decider
{
    /// <summary>
    /// WHETHER AN OWNER MESSAGE COUNTS AS A REPLY for the supersede rule. A message that is itself a
    /// question asks something; it answers nothing, so it cannot make an older question obsolete —
    /// the same reading <see cref="AnswerBinding_Decider"/> gives it for binding.
    /// </summary>
    public static bool Counts_AsAReply(string ownerMessageText)
    {
        return !OwnerPush_Policy.Asks_InProse(ownerMessageText);
    }

    /// <summary>
    /// The open questions of ONE orchestration that a newer question closes: those asked before the
    /// owner last replied in words — EXCEPT a question the owner has already answered by a tap and
    /// whose high-risk read-back is still pending. That question is answered; only the code is
    /// outstanding, and superseding it throws the owner's answer away.
    /// </summary>
    /// <param name="openInOrchestration">The open questions of the orchestration the new one belongs to.</param>
    /// <param name="ownerRepliedUtc">When the owner last replied in words there, or null if never.</param>
    /// <param name="awaitingReadBack">Message ids of questions tapped and waiting for their code.</param>
    public static IReadOnlyList<OpenQuestionRecord> Select_ToSupersede(
        IEnumerable<OpenQuestionRecord> openInOrchestration,
        DateTime? ownerRepliedUtc,
        IReadOnlyCollection<long> awaitingReadBack)
    {
        if (ownerRepliedUtc == null)
            return [];

        return [.. openInOrchestration.Where(question =>
            question.AskedUtc < ownerRepliedUtc.Value
            && !awaitingReadBack.Contains(question.MessageId))];
    }

    /// <summary>
    /// The open question the new one repeats, or null. A repeat is the same question line — compared
    /// without case and with runs of whitespace collapsed, since a session re-typing its question
    /// reflows it. Only the question line: the options, the deadline and the recommendation are
    /// terms of the same question, and the owner answers the question.
    ///
    /// <para>
    /// Null for a record saved before its question line was remembered (<see cref="OpenQuestionRecord.Prompt"/>
    /// absent): nothing to compare is not a match, and the cost is the old behaviour, one duplicate.
    /// </para>
    /// </summary>
    public static OpenQuestionRecord? Find_Repeat_OrNull(IEnumerable<OpenQuestionRecord> openInOrchestration, string newPrompt)
    {
        var wanted = Normalise(newPrompt);

        if (wanted.Length == 0)
            return null;

        return openInOrchestration.FirstOrDefault(question => question.Prompt != null && Normalise(question.Prompt) == wanted);
    }

    /// <summary>
    /// How long a question the owner has ALREADY resolved keeps the same words off their phone.
    ///
    /// <para>
    /// It is a window rather than for ever because a verbatim re-ask is legitimate once the world has
    /// moved: "merge stage 13 now?" asked again tomorrow is a new question wearing the same sentence.
    /// Two hours is about one conversation, which is where the fault lives.
    /// </para>
    /// </summary>
    public static readonly TimeSpan CLOSED_REPEAT_WINDOW = TimeSpan.FromHours(2);

    /// <summary>
    /// The recently RESOLVED question the new one repeats word for word, or null.
    ///
    /// <para>
    /// The sibling above guards the questions still open; this one guards the ones the owner has just
    /// dealt with — tapped an option, typed an answer, or tapped "let's talk", which closes the
    /// question without deciding anything and then asks the session to put it again. That last path is
    /// the one the owner met on 2026-09-12, and by the open-questions rule both copies were correct.
    /// </para>
    /// <para>
    /// SAME MATCHING AS THE OPEN CASE — the question line alone, case-folded, whitespace collapsed —
    /// so the two guards cannot disagree about what "the same question" means. The newest match wins:
    /// it is the one whose answer the session is being told about.
    /// </para>
    /// </summary>
    public static ClosedQuestionRecord? Find_ClosedRepeat_OrNull(
        IEnumerable<ClosedQuestionRecord> closedQuestions,
        string orchId,
        string newPrompt,
        DateTime nowUtc)
    {
        var wanted = Normalise(newPrompt);

        if (wanted.Length == 0)
            return null;

        return closedQuestions
            .Where(closed => closed.OrchId == orchId)
            .Where(closed => nowUtc - closed.ClosedUtc <= CLOSED_REPEAT_WINDOW)
            .Where(closed => Normalise(closed.Prompt) == wanted)
            .OrderByDescending(closed => closed.ClosedUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether a question that repeats a DECIDED one is withheld: only the first time.
    ///
    /// <para>
    /// A WARNING, NEVER A WALL. The first copy is withheld because the owner has just dealt with this
    /// question and a second identical one reads as being asked twice. A session that has been told
    /// what they decided and asks the same thing REGARDLESS is no longer repeating itself by accident
    /// — it is insisting, and a decision that can never be put to the owner is worse than one
    /// duplicate. So the second attempt goes out.
    /// </para>
    /// </summary>
    public static bool Should_Withhold_Reask(ClosedQuestionRecord? decidedAlready, bool withheldOnceAlready)
    {
        return decidedAlready != null && !withheldOnceAlready;
    }

    static string Normalise(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }
}

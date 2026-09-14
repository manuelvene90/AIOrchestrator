namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// A question that HAS been resolved, kept only so the same words are not put on the owner's phone
/// again a minute later.
///
/// <para>
/// WHY IT EXISTS SEPARATELY FROM <c>OpenQuestionRecord</c>. The repeat guard of 2026-09-11 compares a
/// new question against the OPEN ones, which is exactly right while the buttons are live and blind
/// the moment they are not. The owner, 2026-09-12: *"per esempio in sta chat mi hai fatto la stessa
/// domanda 2 volte"* — and both copies were legitimate by that rule, because a tap on "💬 Let's talk"
/// CLOSES the question and the protocol then tells the session to ask it again. It re-asked verbatim,
/// and from the phone that is being asked twice.
/// </para>
/// <para>
/// IT CARRIES THE ANSWER WHEN THERE WAS ONE, because that is what makes the refusal useful rather
/// than merely correct: a session re-asking something the owner has already decided is told WHAT they
/// decided and when, which is the thing it was missing.
/// </para>
/// <para>
/// IN MEMORY ONLY, deliberately. It is a short window (see
/// <c>QuestionSupersede_Decider.CLOSED_REPEAT_WINDOW</c>) against a mistake made inside one
/// conversation, and a restart that forgets it costs one duplicate — the behaviour before this
/// existed. Persisting it would put a question's text into the engine state file to buy that.
/// </para>
/// </summary>
public sealed record ClosedQuestionRecord
{
    public required string OrchId { get; init; }

    /// <summary>The question line alone, as the owner read it — what a repeat is recognised by.</summary>
    public required string Prompt { get; init; }

    public DateTime ClosedUtc { get; init; }

    /// <summary>How it ended, in <c>QuestionClosure_Wording</c>'s words — shown to the session.</summary>
    public required string Closure { get; init; }

    /// <summary>The option the owner chose, when they chose one. Null for a talk request or a lapse.</summary>
    public string? AnswerLabel { get; init; }
}

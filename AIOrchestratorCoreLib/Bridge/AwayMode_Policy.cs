namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Decides when the owner has stopped reading, so the orchestrations can stop talking AT them.
///
/// WHY: the owner spent a day on a plane and landed to a wall of messages, several of them
/// multi-select questions, with no way to tell which were still relevant. Unanswerable spam is
/// worse than silence — it costs them work before they can do any.
///
/// THREE STATES, because two were not enough (owner's catch): "what if I got sent 100 questions in
/// 15 minutes?" A single 15-minute timer lets the flood happen and only THEN reacts.
///
///   NORMAL → QUIET   at the 3rd unanswered message, IMMEDIATELY. That orchestration stops asking.
///                    It is a suspicion, not a conclusion: the owner may be 30 seconds from
///                    replying, so nothing is announced and nothing is parked yet.
///   QUIET  → AWAY    once the owner has been silent EVERYWHERE for 15 minutes. Now it is a
///                    conclusion: announced, questions parked, short updates begin.
///   → NORMAL         the instant the owner says anything anywhere.
///
/// AWAY IS APP-WIDE, never per orchestration. The owner is either at their phone or not; they are
/// not "present for crm-2 and absent for arb-1". The clock therefore runs on their LAST MESSAGE
/// ANYWHERE — chatting in one topic proves presence for all of them — and the app coordinates every
/// orchestration directly rather than having supervisors relay it to each other (owner's call).
/// </summary>
public static class AwayMode_Policy
{
    /// <summary>Consecutive unanswered messages that make ONE orchestration stop asking.</summary>
    public const int QUIET_THRESHOLD = 3;

    /// <summary>Silence from the owner ANYWHERE before quiet hardens into away.</summary>
    public const int AWAY_AFTER_MINUTES = 15;

    /// <summary>The short update cadence while away — enough to stay informed, not enough to be spam.</summary>
    public const int AWAY_UPDATE_MINUTES = 30;

    /// <summary>An orchestration that has talked into the void enough times holds its tongue.</summary>
    public static bool Should_GoQuiet(int unansweredCount)
    {
        return unansweredCount >= QUIET_THRESHOLD;
    }

    /// <summary>
    /// Away needs BOTH: somebody actually waiting on the owner (otherwise silence just means there
    /// was nothing to say), and 15 minutes of silence from them across every topic.
    /// </summary>
    public static bool Should_EnterAway(bool anyOrchestrationQuiet, DateTime lastOwnerMessageUtc, DateTime nowUtc)
    {
        if (!anyOrchestrationQuiet)
            return false;

        return (nowUtc - lastOwnerMessageUtc).TotalMinutes >= AWAY_AFTER_MINUTES;
    }

    /// <summary>Marks an away topic in the owner's topic LIST, so the state is visible without opening it.</summary>
    public const string AWAY_GLYPH = "✈";

    /// <summary>
    /// What the owner sees when it flips on. It must answer the question they will actually have on
    /// landing: "do I need to scroll up and answer all that?" — no.
    /// </summary>
    public const string AWAY_ON_NOTICE =
        "✈ AWAY MODE ON — you have not replied in a while, so I am assuming you are busy.\n\n"
        + "Everything already asked is PARKED: ignore it, do not scroll back. Every supervisor keeps the work "
        + "moving on its own and will not ask you anything else.\n\n"
        + "You get a 3-line update every 30 min per orchestration. Send any message when you are back — one is "
        + "enough, it clears away mode everywhere — and each supervisor re-asks only what is still relevant.";

    public const string AWAY_OFF_NOTICE =
        "☀ Welcome back — away mode off everywhere. Parked questions are being re-checked; anything still relevant "
        + "comes back updated, the rest is dropped.";

    /// <summary>Appended to a question message that was left unanswered when away mode began.</summary>
    public const string PARKED_SUFFIX = "\n\n⌛ parked — you were away. Ignore this; it will be re-asked if still relevant.";
}

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Decides when the owner has stopped reading, so the orchestration can stop talking AT them.
///
/// WHY: the owner spent a day on a plane and landed to a wall of messages, several of them
/// multi-select questions, with no way to tell which were still relevant and which had been
/// overtaken by events. Unanswerable spam is worse than silence — it costs them the work of
/// reconstructing what is still live before they can do anything.
///
/// Both conditions must hold, and they guard different mistakes:
///   - COUNT: one unanswered message means nothing (they may simply be mid-task).
///   - TIME: three questions fired in the same minute means the supervisor is chatty, not that the
///     owner has left. Without the clock, a burst would trip away mode while they are right there.
/// </summary>
public static class AwayMode_Policy
{
    /// <summary>Consecutive messages to the owner with no reply.</summary>
    public const int UNANSWERED_THRESHOLD = 3;

    /// <summary>How long the OLDEST unanswered message must have been sitting there.</summary>
    public const int AWAY_AFTER_MINUTES = 15;

    /// <summary>The short update cadence while away — enough to stay informed, not enough to be spam.</summary>
    public const int AWAY_UPDATE_MINUTES = 30;

    public static bool Should_EnterAway(int unansweredCount, DateTime firstUnansweredUtc, DateTime nowUtc)
    {
        if (unansweredCount < UNANSWERED_THRESHOLD)
            return false;

        return (nowUtc - firstUnansweredUtc).TotalMinutes >= AWAY_AFTER_MINUTES;
    }

    /// <summary>
    /// What the owner sees when it flips on. It must answer the question they will actually have on
    /// landing: "do I need to scroll up and answer all that?" — no.
    /// </summary>
    public const string AWAY_ON_NOTICE =
        "🌙 AWAY MODE ON — you have not replied in a while, so I am assuming you are busy.\n\n"
        + "Everything already asked is PARKED: ignore it, do not scroll back. The supervisor will keep the work "
        + "moving on its own and will not ask you anything else.\n\n"
        + "You get a 3-line update every 30 min. Send any message when you are back and it re-asks only what is "
        + "still relevant.";

    public const string AWAY_OFF_NOTICE =
        "☀ Welcome back — away mode off. Parked questions are being re-checked; anything still relevant comes back "
        + "updated, the rest is dropped.";

    /// <summary>Appended to a question message that was left unanswered when away mode began.</summary>
    public const string PARKED_SUFFIX = "\n\n⌛ parked — you were away. Ignore this; it will be re-asked if still relevant.";
}

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
    ///
    /// AND IT NEVER APPLIES WHILE THE OWNER IS AT A PC (owner's ruling, 2026-09-07: *"when I'm at
    /// the pc, which mean when at least one session is in pc mode, automatic away mode should never
    /// happen"*). `/pc` says they are sitting at a terminal and deliberately not answering Telegram,
    /// so the silence measured here is that mode WORKING rather than evidence of absence. Their log
    /// carries the failure it caused: da-vinci-fintech-suite-26 went Terminal at 09:53, away mode
    /// declared them "unresponsive" app-wide at 10:23:50, and they did not leave that terminal until
    /// 10:30:56 - so the app parked their questions and told every supervisor they were not reading
    /// while they sat in front of one of them.
    ///
    /// It is asked FIRST rather than added as one more term in the conjunction, because it is of a
    /// different kind: presence is a FACT the owner stated, while the other two are inferences drawn
    /// from silence. An inference must not outrank the thing it is a guess about.
    /// </summary>
    public static bool Should_EnterAway(bool anyOrchestrationQuiet, bool ownerAtAPc, DateTime lastOwnerMessageUtc, DateTime nowUtc)
    {
        if (ownerAtAPc)
            return false;

        if (!anyOrchestrationQuiet)
            return false;

        return (nowUtc - lastOwnerMessageUtc).TotalMinutes >= AWAY_AFTER_MINUTES;
    }

    /// <summary>
    /// The same rule read as an INVARIANT rather than as an entry gate: away mode must not merely
    /// fail to start while the owner is at a pc, it must not STAND.
    ///
    /// The owner asked for the state and not the transition - "away mode should never happen" - and
    /// the two differ in the case that actually bites: a spell that began while they were out, and
    /// was still on when they sat down. Guarding only the entry would leave that spell running until
    /// they next typed into Telegram, which is the one thing /pc exists to stop them having to do.
    ///
    /// Leaving away this way is deliberately NOT "the owner spoke": it clears the away flag and
    /// nothing else. Stamping the silence clock or zeroing the quiet counters here would assert a
    /// message that never arrived, and would stop any other orchestration from ever going quiet for
    /// as long as the owner sat at one terminal.
    /// </summary>
    public static bool Should_LeaveAway(bool awayActive, bool ownerAtAPc)
    {
        return awayActive && ownerAtAPc;
    }

    /// <summary>
    /// Away's glyph. NOT IN THE TOPIC LIST ANY MORE — it moved to PULSE's header on 2026-09-10, with
    /// the other four mode glyphs, because away is APP-WIDE: on a name it renamed every open topic
    /// the moment the owner went away, and each rename writes a service message into the thread it
    /// renames.
    ///
    /// This constant has no reader outside the test that asserts it still equals
    /// <c>TelegramDeliveryMode_Glyphs.AWAY</c>, which is the character everything actually draws.
    /// It is a SECOND SPELLING and that test is the only thing keeping the two in step; the pair is
    /// noted for removal rather than removed here, because deleting a constant is not one of the six
    /// points this branch was asked for (decision 22).
    /// </summary>
    public const string AWAY_GLYPH = "✈";

    /// <summary>Per-topic quiet marker — same move, same duplication, same note.</summary>
    public const string QUIET_GLYPH = "🤐";

    /// <summary>
    /// Posted in the topic the moment it goes quiet. The glyph alone says WHAT is happening but not
    /// WHERE in the conversation it started — and that boundary is the useful part when scrolling
    /// back: everything above it was asked, nothing below it will be until you reply.
    /// </summary>
    public const string QUIET_ON_NOTICE =
        "🤐 going quiet here — 3 messages unanswered, so this orchestration stops asking. It keeps working and parks "
        + "anything it needs from you. Reply whenever; that resumes it immediately.";

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

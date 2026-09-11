namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>What a question that nobody has answered needs, right now.</summary>
public enum QuestionDeadlineActions
{
    /// <summary>Nothing is due. The overwhelmingly common answer, and the cheap one.</summary>
    None,

    /// <summary>Half the window has gone: EDIT the existing message with a reminder.</summary>
    Remind,

    /// <summary>The window closed and a default was declared: take it, and say so in the channel.</summary>
    ApplyDefault,

    /// <summary>The window closed with no default: the answer is NO, with reason timeout.</summary>
    ExpireAsDeny,
}

/// <summary>
/// The rule for a question the owner has not answered.
///
/// <para>
/// A REMINDER IS AN EDIT, NEVER A SECOND MESSAGE (decision 14). A repeat that arrives as a
/// notification is a waterfall, which is the thing this system exists to prevent — so the reminder
/// changes the message that is already there, and happens exactly once, which is why
/// <c>reminderAlreadySent</c> is an input rather than something inferred from the clock.
/// </para>
/// <para>
/// HIGH RISK NEVER DEFAULTS. Not "has no default configured" — CANNOT have one. A push approved
/// because a phone was in a pocket is precisely the outcome the second gesture exists to prevent,
/// and a default would walk straight around it. It expires as a DENY, the session is told why, and
/// the question stays listed in /pending so the owner sees what lapsed rather than discovering it
/// from the consequences.
/// </para>
/// <para>
/// AT THE INSTANT, NOT AFTER IT. Both comparisons are <c>&gt;=</c>, for the reason
/// <see cref="Telegram.TelegramAttempt_Gate.Is_AttemptDue"/> states: a deadline that is never due at
/// exactly its own moment is a deadline silently longer than it says.
/// </para>
/// </summary>
public static class QuestionDeadline_Planner
{
    public static QuestionDeadlineActions Decide(
        DateTime askedUtc,
        DateTime? deadlineUtc,
        bool reminderAlreadySent,
        bool isHighRisk,
        int? defaultOptionIndex,
        DateTime nowUtc)
    {
        // NO DEADLINE IS NOT AN OVERSIGHT, it is the old behaviour and the default one: a question
        // waits until it is answered. Nothing here may invent one.
        if (deadlineUtc == null)
            return QuestionDeadlineActions.None;

        if (nowUtc >= deadlineUtc.Value)
        {
            // The high-risk check comes FIRST and does not consult the index. A record that somehow
            // carries both flags must deny, and reading the default before the risk would be a
            // second place where that combination could resolve the other way.
            if (isHighRisk || defaultOptionIndex == null)
                return QuestionDeadlineActions.ExpireAsDeny;

            return QuestionDeadlineActions.ApplyDefault;
        }

        if (reminderAlreadySent)
            return QuestionDeadlineActions.None;

        return nowUtc >= Compute_ReminderAtUtc(askedUtc, deadlineUtc.Value)
            ? QuestionDeadlineActions.Remind
            : QuestionDeadlineActions.None;
    }

    /// <summary>
    /// Halfway between the asking and the deadline. Separate from <see cref="Decide"/> so a test can
    /// set a stamp and walk the clock across it, rather than assert on an arithmetic it performed
    /// itself — the same split <see cref="Telegram.TelegramAttempt_Gate"/> makes.
    /// </summary>
    public static DateTime Compute_ReminderAtUtc(DateTime askedUtc, DateTime deadlineUtc)
    {
        // A window that is already inverted (a deadline before the asking) reminds immediately
        // rather than in the past-tense middle of nowhere — and Decide will expire it on the same
        // tick anyway, so this is a total function, not a live path.
        if (deadlineUtc <= askedUtc)
            return askedUtc;

        return askedUtc + TimeSpan.FromTicks((deadlineUtc - askedUtc).Ticks / 2);
    }
}

using AIOrchestratorCoreLib.Formatting;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// What the app says when it wakes a member. Extracted so it can be TESTED: this text has now been
/// wrong twice, and both times the defect was in what it asserted rather than in when it fired.
///
/// It claimed "nothing was going to wake you" to a reviewer whose monitor was alive and had fired on
/// every write for the previous half hour — the app cannot see a session's monitor. Then it offered
/// three remedies, none of which could work: the mid-task branch is reached if and only if the
/// member has an OPEN WINDOW, and <see cref="MemberState_Resolver"/> tests the window before the
/// blocked and standing-by markers, so declaring either cannot change the state. The one escape,
/// closing the window, was the one it never mentioned.
///
/// The rule these tests enforce: EVERY REMEDY THIS TEXT NAMES MUST BE ONE THAT CAN ACTUALLY CHANGE
/// THE STATE. Telling a stuck session to do three things that leave it stuck is worse than silence,
/// because it reads as instructions that were followed.
/// </summary>
public static class Nudge_Wording
{
    /// <summary>
    /// How long the channel has been quiet, in words — or that it could not be worked out.
    ///
    /// <see cref="Nudge_Decider.Measure_QuietFor"/> returns null when nothing in the channel can be
    /// dated, and this text goes into the entry the member reads. Printing a number there would be
    /// the same defect the clock just stopped committing: a confident figure standing in for an
    /// answer nobody has. Item 12 settled this shape once already —
    /// <see cref="SessionDuration_Formatter.Describe_SinceStamp_OrNull"/> returns null for a stamp it
    /// cannot trust rather than a wrong duration — and this is that rule reaching the wording.
    ///
    /// It DELEGATES to <see cref="SessionDuration_Formatter.Describe"/> for the ordinary case rather
    /// than formatting its own, because a second copy of a duration formatter is exactly what item 12
    /// forbids: the UI once grew one and rendered a future stamp as "under a minute".
    /// </summary>
    public static string Describe_QuietFor(TimeSpan? quietFor)
    {
        return quietFor == null
            ? "an unknown time — nothing in this channel carries a stamp I can read"
            : SessionDuration_Formatter.Describe(quietFor.Value);
    }

    public static string Subject_For(bool dormantMidWork)
    {
        if (dormantMidWork)
            return "your writing window is still open — close it or resume";

        return "unread traffic — you have not answered";
    }

    /// <summary>
    /// The member stopped with a window open. The ONLY state-changing remedy is closing it, and the
    /// text says so — including that the other two markers will not help, because a member that has
    /// just been told to "say what you are waiting for" will otherwise try them.
    /// </summary>
    public static string Body_ForOpenWindow(int lastEntryIndex, string quietForText)
    {
        return $"Your own entry [{lastEntryIndex}] is the last thing on this channel and nothing has moved for {quietForText}, with a writing window you announced and never closed. A monitor only fires when someone ELSE writes, so if nobody owes you a reply this cannot continue on its own — this entry is the app waking you in case that is what happened. Either resume the batch, or append an entry containing {MemberState_Resolver.WRITING_WINDOW_CLOSED_MARKER} (or {MemberState_Resolver.MUTATION_WINDOW_CLOSED_MARKER}) with the results. Closing it is the ONLY thing that changes this state: while a window is open, declaring {MemberState_Resolver.BLOCKED_ON_OWNER_MARKER} or {MemberState_Resolver.STANDING_BY_MARKER} will not stop these, because an open window outranks both. Once it is closed, file your report or declare, and this stops.";
    }

    /// <summary>
    /// Somebody wrote and the member has not replied. Here the declaration IS the escape — the
    /// traffic may have asked for nothing, which is the case that used to nudge forever.
    /// </summary>
    public static string Body_ForUnansweredTraffic(int lastEntryIndex, string authorText, string quietForText)
    {
        return $"Entry [{lastEntryIndex}] FROM {authorText} has been waiting {quietForText} with no reply from you. Read this channel from your last entry down and act on it. If it asked you for nothing — a hold, an acknowledgement — reply {MemberState_Resolver.STANDING_BY_MARKER} once and these stop. If your monitor is no longer running, arm a fresh one.";
    }
}

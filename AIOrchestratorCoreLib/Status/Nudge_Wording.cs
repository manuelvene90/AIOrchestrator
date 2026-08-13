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
    /// The subject of the entry the app appends when it respawns an orphaned member. It lives here,
    /// beside the nudge subjects, because <see cref="Is_WakeSubject"/> has to recognise it and a
    /// predicate that RE-TYPES the text it recognises is a drift waiting to happen — the writer moves,
    /// the recogniser does not, and nothing says so.
    /// </summary>
    public const string RESPAWN_SUBJECT = "session was orphaned and has been respawned";

    public static string Subject_For(bool dormantMidWork)
    {
        if (dormantMidWork)
            return "your writing window is still open — close it or resume";

        return "unread traffic — you have not answered";
    }

    /// <summary>
    /// Is this an entry the app wrote while WAKING a member, rather than something the member is being
    /// woken ABOUT? The distinction only matters on a channel with no conversation in it at all, where
    /// there is nothing else to key the "already nudged" memo on.
    ///
    /// Everything the app appends to a member channel to wake it belongs here: both nudge subjects and
    /// the respawn notice. A `/resume` broadcast deliberately does NOT — that is the owner speaking
    /// through the app, it is the one thing such a member is genuinely supposed to act on, and treating
    /// it as the app's own noise is what let one nudge per PROCESS look like one nudge per thing.
    /// </summary>
    public static bool Is_WakeSubject(string subject)
    {
        return subject == Subject_For(true)
            || subject == Subject_For(false)
            || subject == RESPAWN_SUBJECT;
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

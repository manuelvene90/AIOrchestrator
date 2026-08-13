using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The one message per Telegram topic. It is EDITED in place while it is the last thing in the
/// topic, so it never notifies; when later traffic buries it and the topic then goes quiet for two
/// minutes it is deleted and written again at the bottom, because Telegram cannot move a message and
/// the owner wants the current state to be what they see on entering the chat.
///
/// NOT PINNED, and the owner's reason for refusing that is the reason this file exists at all.
/// "Working now" elsewhere in the app is FILE MTIME, which stays true for ~2 minutes after a turn
/// ends; pinned, that wrong state would sit in front of them permanently. So this line contains NO
/// MTIME ANYWHERE. Every word of it comes from parsed channel state and the PLAN.md ledger — things
/// an agent actually wrote — and a member with nothing to report reads "idle" rather than being
/// described by how recently its file was touched.
///
/// Being an EDIT rather than a message is what makes it safe to keep CURRENT: a repeat that is a
/// notification is a waterfall, which is the thing this system exists to prevent. The repost is the
/// one exception and it is bounded by the quiet window — every message resets it, so at most one
/// notification arrives per quiet period, and never in the middle of the owner's own conversation.
/// </summary>
public static class TopicStatusLine_Builder
{
    /// <summary>
    /// Task words kept per member row — a topic line is glanceable or it is nothing.
    ///
    /// FOUR, down from five on the owner's 2026-08-13 directive, and the number is a WRAP budget
    /// rather than a taste: the row it sits in is `• imp-1 · <task> · 12 min`, so ten characters go
    /// to the member and eight to the duration before the task gets any. At five words the owner's
    /// own screenshot wrapped `rev-1 · audit R2–R8 against current master · 2 min` onto three visual
    /// lines. Four costs the trailing word of a long brief and buys the row back.
    /// </summary>
    public const int MEMBER_TASK_WORDS = 4;

    /// <summary>
    /// Opens every member row and is the only thing dividing them, which is why the 28-dash divider
    /// could go. It also survives WRAPPING, which no amount of column padding did: a bullet row that
    /// spills onto a second visual line still reads as one row, because the `•` marks where it began.
    /// </summary>
    const string MEMBER_BULLET = "• ";

    /// <summary>
    /// Between every pair of fields, in place of the runs of spaces that used to fake columns.
    /// Telegram renders the message body in a PROPORTIONAL font, so those columns could never align
    /// — the padding bought nothing and spent the width that made the rows wrap.
    /// </summary>
    const string FIELD_SEPARATOR = " · ";

    /// <summary>
    /// <paramref name="aMessageIsAlreadyPosted"/> decides what NOTHING TO SAY means. With no
    /// message up it means silence; with one up it means the BARE TITLE, because saying nothing
    /// would leave the last row standing — a running duration for a member that has been closed.
    ///
    /// REQUIRED, with no default, because `false` is the dangerous value: a caller who omitted it
    /// would silently get the pre-fix behaviour — an emptied line returning empty while a message
    /// is posted, which is the frozen-row regression this parameter exists to prevent. A default
    /// that is the wrong answer for the very case the parameter was added for is a trap.
    ///
    /// The fallback lives HERE, not at the call site, because the decider must see the text that
    /// is actually sent. Substituting it after the decision made the decider compare an empty
    /// string against a cached title forever: same title, same message, a rejected edit every two
    /// seconds for as long as the app ran. A decider that does not see what goes out cannot be
    /// trusted about anything.
    /// </summary>
    public static string Build(
        string title,
        IPlanProgress? progress,
        IReadOnlyList<ITopicStatusMember> members,
        string? lastSubject,
        DateTime now,
        bool aMessageIsAlreadyPosted)
    {
        List<string> lines = [];

        foreach (var member in members)
        {
            if (member.IsClosed)
                continue;

            lines.Add(Build_MemberLine(member, now));
        }

        // NOTHING TO SAY MEANS SAY NOTHING. With no ledger, no live member and no history, the line
        // would have been the topic's own title repeated back at the owner — a message whose entire
        // content is what they are already looking at. Item 15: if they cannot act on it and it tells
        // them nothing, it does not get written.
        // Total > 0 rather than progress != null: a PLAN.md of nothing but struck-out `- [-]`
        // lines parses to a NON-NULL progress with Total 0, and printing "0/0" beside a topic
        // title is the say-nothing message again.
        var hasSubstance = lines.Count > 0 || (progress != null && progress.Total > 0) || !string.IsNullOrWhiteSpace(lastSubject);

        if (!hasSubstance)
            return aMessageIsAlreadyPosted ? title : "";

        lines.Insert(0, Build_TitleLine(title, progress));

        // NO BULLET on this one, deliberately: it is not a member, and a `• last · …` row reads like
        // one more session called "last". Not having the bullet is what separates it now that the
        // divider is gone. It affords two more task words than a member row because it carries no
        // duration — a longer field, in a shorter row.
        if (!string.IsNullOrWhiteSpace(lastSubject))
            lines.Add($"last{FIELD_SEPARATOR}{TextSummary_Formatter.Summarize_Task(lastSubject, MEMBER_TASK_WORDS + 2)}");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The percentage comes from <see cref="PlanProgress_Formatter.Percent"/> rather than being
    /// recomputed here. The owner reads this line and `/progress` for the same ledger; two surfaces
    /// quoting different numbers is the failure item 10 exists to prevent, and it starts as two
    /// copies of one division.
    ///
    /// It TRUNCATES, so 72/113 reads 63% and not 64%. That is deliberate everywhere in this repo —
    /// 75 of 76 must never read as 100% — and it is worth more than matching a mock.
    /// </summary>
    static string Build_TitleLine(string title, IPlanProgress? progress)
    {
        if (progress == null || progress.Total <= 0)
            return title;

        return $"{title}{FIELD_SEPARATOR}{progress.Done}/{progress.Total}{FIELD_SEPARATOR}{PlanProgress_Formatter.Percent(progress)}%";
    }

    /// <summary>
    /// A member row is "who · what · how long", and every part of it is agent-written. The duration
    /// comes from the SUPERVISOR's last brief stamp through the one formatter this repo has
    /// (item 12), which returns null for a stamp in the future rather than a confident wrong number.
    /// </summary>
    static string Build_MemberLine(ITopicStatusMember member, DateTime now)
    {
        var state = MemberState_Resolver.Resolve(member.Entries);

        if (Is_Idle(state))
            return Build_Row(member.MemberId, "idle");

        var brief = MemberState_Resolver.Find_LastBrief_OrNull(member.Entries);

        if (brief == null)
            return Build_Row(member.MemberId, "idle");

        var task = TextSummary_Formatter.Summarize_Task(brief.Subject, MEMBER_TASK_WORDS);
        var onTaskFor = SessionDuration_Formatter.Describe_SinceStamp_OrNull(brief.DateText, now);

        // A missing duration DROPS ITS FIELD rather than leaving the separator standing: a row ending
        // in a dangling `· ` reads as a value that failed to load, when the truth is that the stamp
        // was not trustworthy enough to date.
        return onTaskFor == null
            ? Build_Row(member.MemberId, task)
            : Build_Row(member.MemberId, task, onTaskFor);
    }

    /// <summary>
    /// ONE place that joins a member row, so the bullet and the separator cannot drift apart between
    /// the idle row, the dated row and the undated one — three copies of a format string is how the
    /// old shape ended up with a four-space gap in one branch and a six-space gap in another.
    /// </summary>
    static string Build_Row(string memberId, params string[] fields)
    {
        return MEMBER_BULLET + memberId + FIELD_SEPARATOR + string.Join(FIELD_SEPARATOR, fields);
    }

    /// <summary>
    /// IDLE means nothing is owed and nothing is running — a member that has DECLARED it, and one
    /// that has never been briefed. Both read the same to the owner, which is correct: there is
    /// nothing for them to look at either way.
    ///
    /// Note what is NOT idle: awaiting a verdict. That member is waiting on the SUPERVISOR, and the
    /// owner seeing it as idle would hide the one queue they can actually unblock.
    /// </summary>
    static bool Is_Idle(MemberStates state)
    {
        return state == MemberStates.StandingBy || state == MemberStates.NewNoTraffic;
    }

}

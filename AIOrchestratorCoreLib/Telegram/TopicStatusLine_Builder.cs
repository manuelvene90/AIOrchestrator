using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The one message per Telegram topic that is posted once and EDITED forever after — so it never
/// notifies and never scrolls away.
///
/// NOT PINNED, and the owner's reason for refusing that is the reason this file exists at all.
/// "Working now" elsewhere in the app is FILE MTIME, which stays true for ~2 minutes after a turn
/// ends; pinned, that wrong state would sit in front of them permanently. So this line contains NO
/// MTIME ANYWHERE. Every word of it comes from parsed channel state and the PLAN.md ledger — things
/// an agent actually wrote — and a member with nothing to report reads "idle" rather than being
/// described by how recently its file was touched.
///
/// Being an EDIT rather than a message is also what makes it safe to keep current: a repeat that is
/// a notification is a waterfall, which is the thing this system exists to prevent.
/// </summary>
public static class TopicStatusLine_Builder
{
    /// <summary>Task words kept per member row — a topic line is glanceable or it is nothing.</summary>
    public const int MEMBER_TASK_WORDS = 5;

    const string SEPARATOR = "────────────────────────────";

    public static string Build(
        string title,
        IPlanProgress? progress,
        IReadOnlyList<ITopicStatusMember> members,
        string? lastSubject,
        DateTime now)
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
            return "";

        lines.Insert(0, Build_TitleLine(title, progress));

        if (!string.IsNullOrWhiteSpace(lastSubject))
        {
            lines.Add(SEPARATOR);
            lines.Add($"last    {TextSummary_Formatter.Summarize_Task(lastSubject, MEMBER_TASK_WORDS + 3)}");
        }

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

        return $"{title}          {progress.Done}/{progress.Total} · {PlanProgress_Formatter.Percent(progress)}%";
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
            return $"{member.MemberId}   idle";

        var brief = MemberState_Resolver.Find_LastBrief_OrNull(member.Entries);

        if (brief == null)
            return $"{member.MemberId}   idle";

        var task = TextSummary_Formatter.Summarize_Task(brief.Subject, MEMBER_TASK_WORDS);
        var onTaskFor = SessionDuration_Formatter.Describe_SinceStamp_OrNull(brief.DateText, now);

        if (onTaskFor == null)
            return $"{member.MemberId}   {task}";

        return $"{member.MemberId}   {task}      {onTaskFor}";
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

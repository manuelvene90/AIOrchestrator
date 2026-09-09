using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using AIOrchestratorCoreLib.Status.SessionModelReading;
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
/// an agent actually wrote — and a member with nothing to report reads "standing by" rather than being
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
    /// What this line calls itself. The owner chose the word on 2026-08-24 from three offered.
    ///
    /// IT REPLACES THE TOPIC NAME, which used to open the line and which they asked to have removed:
    /// *"the name of the topic is not needed, I already know where I am, and if I don't I have it at
    /// the top of the screen."* What the opening field is FOR is telling the two recurring messages
    /// apart at a glance — this one, edited constantly and never notifying, against the half-hourly
    /// digest that opens with STATUS. Their constraint, verbatim: *"it cannot be STATUS, because that
    /// is the 30m message, it must be something else."*
    /// </summary>
    const string LEAD_WORD = "PULSE";

    /// <summary>
    /// <paramref name="aMessageIsAlreadyPosted"/> decides what NOTHING TO SAY means. With no
    /// message up it means silence; with one up it means the BARE LEAD WORD, because saying nothing
    /// would leave the last row standing — a running duration for a member that has been closed.
    ///
    /// REQUIRED, with no default, because `false` is the dangerous value: a caller who omitted it
    /// would silently get the pre-fix behaviour — an emptied line returning empty while a message
    /// is posted, which is the frozen-row regression this parameter exists to prevent. A default
    /// that is the wrong answer for the very case the parameter was added for is a trap.
    ///
    /// The fallback lives HERE, not at the call site, because the decider must see the text that
    /// is actually sent. Substituting it after the decision made the decider compare an empty
    /// string against a cached lead line forever: same text, same message, a rejected edit every two
    /// seconds for as long as the app ran. A decider that does not see what goes out cannot be
    /// trusted about anything.
    /// </summary>
    public static string Build(
        IPlanProgress? progress,
        IReadOnlyList<ITopicStatusMember> members,
        string? lastSubject,
        DateTime now,
        bool aMessageIsAlreadyPosted,
        TimeSpan? figuresUnchangedFor = null,
        ISessionContextUsage? supervisorContext = null,
        ISessionModelReading? supervisorModel = null)
    {
        List<string> lines = [];

        foreach (var member in members)
        {
            if (member.IsClosed)
                continue;

            lines.Add(Build_MemberLine(member, now));
        }

        // NOTHING TO SAY MEANS SAY NOTHING. With no ledger, no live member and no history, the line
        // would have been the lead word and nothing else — a message whose entire content is that a
        // status line exists. Item 15: if they cannot act on it and it tells them nothing, it does not
        // get written.
        // Total > 0 rather than progress != null: a PLAN.md of nothing but struck-out `- [-]`
        // lines parses to a NON-NULL progress with Total 0, and printing "0/0" beside the lead word
        // is the say-nothing message again.
        var hasSubstance = lines.Count > 0 || (progress != null && progress.Total > 0) || !string.IsNullOrWhiteSpace(lastSubject);

        if (!hasSubstance)
            return aMessageIsAlreadyPosted ? LEAD_WORD : "";

        lines.Insert(0, Build_LeadLine(progress, figuresUnchangedFor, supervisorContext, supervisorModel));

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
    /// <summary>
    /// <paramref name="figuresUnchangedFor"/> is this line's answer to the half-hourly message's
    /// delta. The owner asked for it here INSTEAD of a difference, because this surface refreshes
    /// constantly: *"the difference doesn't make sense because at best, after updating every
    /// 10 seconds it would go back to 0."*
    ///
    /// THE SUPERVISOR'S CONTEXT RIDES ON THE LEAD LINE, not on a row of its own, because it has no row
    /// here — this line lists MEMBERS, and a crew's supervisor is not one. Giving it a row would
    /// invent a session called "supervisor" that stands beside imp-1 with no task and no duration.
    /// A basic orchestration has no supervisor at all, so nothing is added there and its solo
    /// carries the figure on its own member row instead.
    ///
    /// THE SUPERVISOR'S MODEL RIDES THERE TOO, between the ledger reading and its context: the facts
    /// about the session first, the alarm last, in the same order a member row keeps them. Unlike
    /// the context figure it has no visibility policy — a context percentage is worth a glance only
    /// past a threshold, whereas the model is simply known or not, and null drops the field.
    /// </summary>
    static string Build_LeadLine(IPlanProgress? progress, TimeSpan? figuresUnchangedFor, ISessionContextUsage? supervisorContext, ISessionModelReading? supervisorModel)
    {
        // THE LEAD WORD STILL COMES BACK BARE WHEN THERE IS NO LEDGER, and the supervisor fields go
        // with it. This return is the "nothing to say" shape the whole class is built around, and
        // hanging a figure off it would be the say-nothing message the class doc refuses.
        if (progress == null || progress.Total <= 0)
            return LEAD_WORD;

        // ONE PATH, BUILDING A FIELD LIST — the member row's shape, for the member row's reason.
        // With three optional fields this had become three copies of one `null ? "" : separator +
        // field` spelling, which is exactly how a separator ends up different in one of them.
        List<string> fields = [$"{progress.Done}/{progress.Total}", $"{PlanProgress_Formatter.Percent(progress)}%"];

        var unchanged = figuresUnchangedFor == null
            ? null
            : Formatting.UnchangedFor_Formatter.Describe_OrNull(figuresUnchangedFor.Value);

        if (unchanged != null)
            fields.Add(unchanged);

        var supervisorModelField = ModelReading_Formatter.Describe_OrNull(supervisorModel);

        if (supervisorModelField != null)
            fields.Add($"sup {supervisorModelField}");

        // Describe_OrNull rather than the bang operator: the policy and the formatter each answer
        // the null question for themselves, so neither this line nor the reader has to assert what
        // the other already checked.
        var supervisorContextField = ContextVisibility_Policy.Show_Supervisor(supervisorContext)
            ? ContextUsage_Formatter.Describe_OrNull(supervisorContext)
            : null;

        if (supervisorContextField != null)
            fields.Add($"sup {supervisorContextField}");

        return LEAD_WORD + FIELD_SEPARATOR + string.Join(FIELD_SEPARATOR, fields);
    }

    /// <summary>
    /// "IDLE" WAS THE WORD, AND IT READ AS A CONTRADICTION. The owner, 2026-08-21, after a topic
    /// showed them "Solo: busy — running a command" and "solo-1 · idle" within seconds of each
    /// other: one session, two opposite states.
    ///
    /// Both were true of what they measure. The receipt reads SessionActivity_Probe (live transcript,
    /// so it sees a command start instantly); this line reads MemberState_Resolver (what an agent has
    /// DECLARED in the channel), and it excludes the mtime signal on purpose — see
    /// ITopicStatusMember, where the owner refused a pinned line that would show a stale
    /// "working now" for the two minutes after a turn ends. Reconciling them would reverse that
    /// ruling, so the WORD changed instead, which is what they chose when asked.
    ///
    /// It is also the second time this exact word has been wrong at them. On 2026-08-15, of the card:
    /// *"I know you're not idle because we're talking"* — "idle" reads as ABSENT when the truth is
    /// merely that nothing has been declared since. MemberState_Descriptor carries that history.
    /// </summary>
    const string NOTHING_DECLARED = "standing by";

    /// <summary>
    /// A member row is "who · what · how long", and every part of it is agent-written. The duration
    /// comes from the SUPERVISOR's last brief stamp through the one formatter this repo has
    /// (item 12), which returns null for a stamp in the future rather than a confident wrong number.
    /// Context % is included for solo/supervisor always, and for other members only if > 90%.
    /// </summary>
    static string Build_MemberLine(ITopicStatusMember member, DateTime now)
    {
        var state = MemberState_Resolver.Resolve(member.Entries);

        // ONE PATH, BUILDING A FIELD LIST, rather than a branch per combination of optional fields.
        // With a task, a duration and now a context reading all able to be absent independently,
        // the ternary shape needed eight spellings of the same row — and the doc on Build_Row below
        // records what happens when one row is spelled more than once: the copies drift, and a
        // separator ends up different in one branch from another.
        var brief = Is_Idle(state) ? null : Find_CurrentTask_OrNull(member, state);

        List<string> fields = [];

        if (brief == null)
        {
            fields.Add(NOTHING_DECLARED);
        }
        else
        {
            fields.Add(TextSummary_Formatter.Summarize_Task(brief.Subject, MEMBER_TASK_WORDS));

            var onTaskFor = SessionDuration_Formatter.Describe_SinceStamp_OrNull(brief.DateText, now);

            // A missing duration DROPS ITS FIELD rather than leaving the separator standing: a row
            // ending in a dangling `· ` reads as a value that failed to load, when the truth is that
            // the stamp was not trustworthy enough to date.
            if (onTaskFor != null)
                fields.Add(onTaskFor);
        }

        // The model sits between the duration and the context figure: what the session IS before
        // how full it is, so the alarm stays last where a glance lands. No policy gates it — a
        // reading is either known or not, and null drops the field like every other one here.
        var modelField = ModelReading_Formatter.Describe_OrNull(member.Model);

        if (modelField != null)
            fields.Add(modelField);

        var contextField = Build_ContextField_OrNull(member);

        if (contextField != null)
            fields.Add(contextField);

        return Build_Row(member.MemberId, [.. fields]);
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
    /// <summary>
    /// What this member is working on: its supervisor's last brief — except for a SOLO, which is
    /// never briefed and never will be, so its own last entry is the answer.
    ///
    /// A SOLO'S ROW READ "standing by" 100% OF THE TIME before this existed, and it was structural
    /// rather than a stale read: Find_LastBrief_OrNull looks for a `FROM supervisor` entry, a basic
    /// orchestration HAS no supervisor, and a solo's member channel is the owner channel, which
    /// carries only `FROM solo`, `FROM owner` and `FROM app`. The row therefore said "standing by"
    /// while mid-turn, inside a Bash call, and while blocked on the owner alike. The owner caught it
    /// against the app's own busy line — "still at it — running a command" printed directly above
    /// "solo-1 · standing by" — and told this session to resolve it (2026-08-24).
    ///
    /// THE FALLBACK IS SOLO-ONLY, and that is the whole care in it. For an implementer or a reviewer
    /// "no brief yet" is a true and useful statement — they are waiting to be told what to do, and
    /// ANeverBriefedMemberReadsStandingBy pins exactly that. Widening this to every member would
    /// trade a wrong answer for a different wrong answer.
    /// </summary>
    static IChannelEntry? Find_CurrentTask_OrNull(ITopicStatusMember member, MemberStates state)
    {
        var brief = MemberState_Resolver.Find_LastBrief_OrNull(member.Entries);

        if (brief != null || MemberKind_Ids.Resolve_Kind(member.MemberId) != MemberKinds.Solo)
            return brief;

        return MemberState_Resolver.Find_LastEntryBy_OrNull(member.Entries, ChannelAuthors.Solo);
    }

    static bool Is_Idle(MemberStates state)
    {
        return state == MemberStates.StandingBy || state == MemberStates.NewNoTraffic;
    }

    /// <summary>
    /// The member's context field, or null when it is not this member's turn to show one. WHO and
    /// WHEN is <see cref="ContextVisibility_Policy"/>'s and the wording is
    /// <see cref="ContextUsage_Formatter"/>'s — this method only joins them, so the status line
    /// cannot drift from the digest about either.
    /// </summary>
    static string? Build_ContextField_OrNull(ITopicStatusMember member)
    {
        if (!ContextVisibility_Policy.Show_Member_OnStatusLine(member.MemberId, member.ContextUsage))
            return null;

        return ContextUsage_Formatter.Describe_OrNull(member.ContextUsage);
    }
}

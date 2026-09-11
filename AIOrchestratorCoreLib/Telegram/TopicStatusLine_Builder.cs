using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The one message per Telegram topic — and since 2026-09-09 the ONLY status surface a topic has.
/// It is EDITED in place while it is the last thing in the topic, so it never notifies; when later
/// traffic buries it and the topic then goes quiet for two minutes it is deleted and written again at
/// the bottom, because Telegram cannot move a message and the owner wants the current state to be
/// what they see on entering the chat.
///
/// THE PERIODIC STATUS MESSAGE IS GONE (owner decision 2026-09-09) and that is why this class grew
/// four fields. Two recurring surfaces meant the owner read the same orchestration twice in two
/// vocabularies, half an hour apart; the half-hourly one also NOTIFIED, which is the waterfall this
/// system exists to prevent. Everything the digest was for now has to be answerable here, in SIX
/// fields the owner specified in order: what is waiting on them, what the supervisor says it is
/// doing, who is live and on what, the last real event, how much is merged, and when this line was
/// last drawn.
///
/// NOT PINNED, and the owner's reason for refusing that is the reason this file exists at all.
/// "Working now" elsewhere in the app is FILE MTIME, which stays true for ~2 minutes after a turn
/// ends; pinned, that wrong state would sit in front of them permanently. So this line contains NO
/// MTIME ANYWHERE. Every word of it comes from parsed channel state, the PLAN.md ledger, and facts
/// the app knows for certain (a usage-limit pause) — things an agent actually wrote or the app
/// measured — and a member with nothing to report reads "standing by" rather than being described by
/// how recently its file was touched.
///
/// Being an EDIT rather than a message is what makes it safe to keep CURRENT: a repeat that is a
/// notification is a waterfall. The repost is the one exception and it is bounded by the quiet
/// window — every message resets it, so at most one notification arrives per quiet period, and never
/// in the middle of the owner's own conversation.
/// </summary>
public static class TopicStatusLine_Builder
{
    /// <summary>
    /// Task words kept per member row — a topic line is glanceable or it is nothing.
    ///
    /// FOUR, down from five on the owner's 2026-08-13 directive, and the number is a WRAP budget
    /// rather than a taste: the row it sits in is `• imp-1 · <task> · working · 12 min`, so ten
    /// characters go to the member, a state word and eight more to the duration before the task gets
    /// any. At five words the owner's own screenshot wrapped
    /// `rev-1 · audit R2–R8 against current master · 2 min` onto three visual lines. Four costs the
    /// trailing word of a long brief and buys the row back.
    /// </summary>
    public const int MEMBER_TASK_WORDS = 4;

    /// <summary>
    /// Words kept per item in the `waiting on you` field. WIDER than a member row's task, and that is
    /// the field's importance made structural: it is the one field the owner is meant to ACT on, it
    /// carries no duration column, and a question cut to four words ("should I bump the") stops being
    /// answerable.
    /// </summary>
    public const int OWNER_ASK_WORDS = 8;

    /// <summary>
    /// Live member rows before the roster collapses to a single summary line, as the owner set it:
    /// one row each *"up to 4 live members; from 5 live, one line"*.
    ///
    /// The point is a bounded MESSAGE, not a bounded roster: eight rows of who-is-on-what pushes the
    /// three fields underneath them off a phone screen, and those three (the last event, the merged
    /// count, the heartbeat) are the ones that say whether the endeavour is moving at all.
    /// </summary>
    public const int MAX_MEMBER_ROWS = 4;

    /// <summary>
    /// Items shown in the `waiting on you` field before the rest become a count.
    ///
    /// THREE, and the reason is the same one that bounds the roster: this field leads the message, so
    /// an unbounded list of asks pushes everything else off the screen. The overflow is COUNTED
    /// rather than dropped — "+4 more" is actionable (open the topic, ask /pending), a silently
    /// shorter list is not.
    /// </summary>
    public const int MAX_OWNER_ASKS = 3;

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
    /// the top of the screen."*
    ///
    /// IT SURVIVES THE DEATH OF THE MESSAGE IT WAS CHOSEN TO BE DISTINGUISHED FROM. The word was
    /// picked because *"it cannot be STATUS, because that is the 30m message"* — and the 30m message
    /// is now gone (2026-09-09). It stays anyway, for a second reason that was always true: it is
    /// this line's NAME, the thing the owner scrolls for, and it is the text the message falls back to
    /// when there is nothing to report. Renaming a surface the owner has learned costs more than the
    /// obsolete half of its justification is worth.
    /// </summary>
    const string LEAD_WORD = "PULSE";

    /// <summary>Field 1's glyph, the owner's own: `⏳ waiting on you · …`.</summary>
    const string WAITING_GLYPH = "⏳";

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
    ///
    /// ONE SPELLING, in <see cref="TopicStatusWording"/>, because the collapsed roster line names the
    /// same state as a group word and two spellings would read as two states.
    /// </summary>
    const string NOTHING_DECLARED = TopicStatusWording.STANDING_BY;

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
    /// <param name="fields">
    /// The inputs PULSE cannot derive from a ledger and a set of member channels — the topic's
    /// delivery mode, the supervisor's declared state, a usage-limit pause, the `last` event's clock,
    /// and the open questions that live on the OWNER channel this builder is never handed. Defaulted
    /// so an un-wired caller degrades to the four derivable fields rather than failing to compile:
    /// every one of them is optional by contract, and each absent one omits its own field instead of
    /// inventing a value. That is the opposite of the
    /// <paramref name="aMessageIsAlreadyPosted"/> reasoning above and deliberately so — there the
    /// default was the dangerous answer, here `default` means "nothing is known", which is exactly
    /// what an unwired caller knows.
    /// </param>
    public static string Build(
        IPlanProgress? progress,
        IReadOnlyList<ITopicStatusMember> members,
        TopicLastEvent? lastEvent,
        DateTime now,
        bool aMessageIsAlreadyPosted,
        TimeSpan? figuresUnchangedFor = null,
        ISessionContextUsage? supervisorContext = null,
        TopicStatusFields fields = default)
    {
        // Resolved ONCE per member and carried, rather than asked again per field. The state decides
        // the reading order, the row's state word, the collapsed line's grouping AND whether the
        // member belongs in "waiting on you" — four readers, and MemberState_Resolver.Resolve scans
        // the whole channel history each time it is called.
        var live = Order_LiveMembers(members);

        var closedCount = Count_Closed(members);

        var asks = Collect_OwnerAsks(fields.OwnerAsks, live, progress, now);

        // NOTHING TO SAY MEANS SAY NOTHING. With no ledger, no live member, no history and nothing
        // waiting, the line would have been the lead word and nothing else — a message whose entire
        // content is that a status line exists. Item 15: if they cannot act on it and it tells them
        // nothing, it does not get written.
        //
        // Total > 0 rather than progress != null: a PLAN.md of nothing but struck-out `- [-]`
        // lines parses to a NON-NULL progress with Total 0, and printing "0/0 merged" beside the lead
        // word is the say-nothing message again.
        //
        // A CLOSED MEMBER IS NOT SUBSTANCE, which is why the count below is not in this test: "7
        // closed" on an otherwise empty line is a message about an orchestration that has finished
        // talking. The owner asked for the closed count as CONTEXT for the live rows, not as a reason
        // to write.
        //
        // AN ASK IS SUBSTANCE, and it is the most actionable substance there is: a supervisor's
        // question on a topic whose ledger has not been written yet must still reach the owner.
        var hasSubstance = live.Count > 0
            || (progress != null && progress.Total > 0)
            || !string.IsNullOrWhiteSpace(lastEvent?.Subject)
            || asks.Count > 0;

        if (!hasSubstance)
            return aMessageIsAlreadyPosted ? LEAD_WORD : "";

        // THE SIX FIELDS IN THE OWNER'S OWN ORDER (2026-09-09), under a header line that carries the
        // lead word and every MODE glyph — 🌙 🔕 ✈ 🤐 💻, all five of which left the topic name on
        // 2026-09-10. Every field omits itself when it has nothing to say; none of them substitutes a
        // placeholder.
        List<string> lines = [Build_HeaderLine(fields)];

        if (asks.Count > 0)
            lines.Add(Build_WaitingOnYouLine(asks));

        var supervisorRow = Build_SupervisorRow_OrNull(fields, supervisorContext, now);

        if (supervisorRow != null)
            lines.Add(supervisorRow);

        lines.AddRange(Build_MemberLines(live, now));

        if (closedCount > 0)
            lines.Add(Build_ClosedCountLine(closedCount));

        // NO BULLET on this one, deliberately: it is not a member, and a `• last · …` row reads like
        // one more session called "last". Not having the bullet is what separates it now that the
        // divider is gone. It affords two more task words than a member row because it carries no
        // duration — a longer field, in a shorter row.
        if (lastEvent != null && !string.IsNullOrWhiteSpace(lastEvent.Value.Subject))
            lines.Add(Build_LastLine(lastEvent.Value, now));

        if (progress != null && progress.Total > 0)
            lines.Add(Build_MergedLine(progress, figuresUnchangedFor));

        // THE HEARTBEAT, last and unconditional once there is anything to say. It is what tells the
        // owner the difference between a quiet orchestration and a dead app — the failure this line
        // cannot otherwise report, because a frozen status line looks exactly like a correct one.
        lines.Add(Build_UpdatedLine(now));

        return string.Join('\n', lines);
    }

    /// <summary>
    /// PULSE'S HEADER — the lead word, and every MODE glyph the topic name used to carry: `✈ 💻 PULSE`.
    ///
    /// <para>
    /// ALL FIVE LIVE HERE NOW (owner, 2026-09-10): 🌙 deferred, 🔕 silenced, ✈ away, 🤐 quiet,
    /// 💻 terminal. They describe how the app is DELIVERING, which is not what a topic list is read
    /// to answer — and two of them are app-wide, so on a name they renamed every open topic at once
    /// and wrote a service message into each one. Here the same fact costs one silent edit of a
    /// message that was being edited anyway.
    /// </para>
    /// <para>
    /// THE PRECEDENCE IS THE TOPIC NAME'S, MOVED VERBATIM, because it was right and because changing
    /// it in the same commit as the move would make a behaviour change look like a relocation. AWAY
    /// SUPERSEDES QUIET: away already means every orchestration has stopped asking, so both together
    /// state one fact twice. TERMINAL REPLACES THE DELIVERY GLYPH: sitting in the terminal is what
    /// silences the topic, so 💻 🔕 says the same thing in two characters. Away still shows beside
    /// terminal — it is about the owner's PHONE, which is a different fact from where they are
    /// sitting for this one endeavour.
    /// </para>
    /// </summary>
    static string Build_HeaderLine(TopicStatusFields fields)
    {
        var presenceOrMode = fields.Presence == OwnerPresenceModes.Terminal
            ? $"{TelegramDeliveryMode_Glyphs.TERMINAL} "
            : fields.Mode switch
            {
                TelegramDeliveryModes.Normal => "",
                TelegramDeliveryModes.Deferred => $"{TelegramDeliveryMode_Glyphs.DEFERRED} ",
                TelegramDeliveryModes.Silenced => $"{TelegramDeliveryMode_Glyphs.SILENCED} ",
                _ => throw new Exception($"Unhandled TelegramDeliveryModes: {fields.Mode}"),
            };

        var ownerAttention = fields switch
        {
            { IsAway: true } => $"{TelegramDeliveryMode_Glyphs.AWAY} ",
            { IsQuiet: true } => $"{TelegramDeliveryMode_Glyphs.QUIET} ",
            _ => "",
        };

        return $"{ownerAttention}{presenceOrMode}{LEAD_WORD}";
    }

    /// <summary>
    /// FIELD 1, and the owner's reason for putting it first: this is the only field that asks
    /// something OF them. Everything below it describes what the app and its sessions are doing.
    ///
    /// <para>
    /// IT IS OMITTED ENTIRELY WHEN NOTHING WAITS — no "nothing waiting" row. A standing row that
    /// usually says "nothing" teaches the owner to skip the line it is on, which costs exactly the
    /// glance this field was created to catch.
    /// </para>
    /// <para>
    /// The overflow is a COUNT, not a truncation: "+4 more" says a list exists and can be read with
    /// /pending, where a silently shortened list says there were three.
    /// </para>
    /// </summary>
    static string Build_WaitingOnYouLine(IReadOnlyList<string> asks)
    {
        var shown = asks.Count <= MAX_OWNER_ASKS ? asks : [.. asks.Take(MAX_OWNER_ASKS)];

        var overflow = asks.Count - shown.Count;

        var overflowPart = overflow > 0 ? $"{FIELD_SEPARATOR}+{overflow} more" : "";

        return $"{WAITING_GLYPH} {TopicStatusWording.WAITING_ON_YOU}{FIELD_SEPARATOR}{string.Join(FIELD_SEPARATOR, shown)}{overflowPart}";
    }

    /// <summary>
    /// Everything the owner is being waited on for, worded, in the order they can act: the app's own
    /// open questions first, then members that have declared BLOCKED ON OWNER, then `[?]` ledger
    /// lines.
    ///
    /// <para>
    /// THREE SOURCES BECAUSE THE ANSWER GENUINELY LIVES IN THREE PLACES, and two of them this builder
    /// can read for itself. The open questions cannot be: they are on the OWNER channel and this
    /// builder is handed per-member channels, so they arrive through
    /// <see cref="TopicStatusFields.OwnerAsks"/>. Deriving the other two here rather than asking the
    /// caller for them is what keeps a half-wired caller honest — a blocked member shows up in this
    /// field on an app that has not been taught to pass anything.
    /// </para>
    /// <para>
    /// ORDER IS BY ANSWERABILITY. A question with live buttons on the owner's phone is one tap; a
    /// blocked session needs a sentence; a `[?]` ledger line is a job. The owner reads left to right
    /// and the cheapest thing to clear is first.
    /// </para>
    /// <para>
    /// DEDUPLICATED ON THE WORDED ITEM, because two of the sources really do describe one fact: a
    /// supervisor that asks a question and declares BLOCKED ON OWNER in the same entry is one thing
    /// waiting, and saying it twice on the line the owner is meant to act on makes the queue look
    /// longer than it is.
    /// </para>
    /// </summary>
    static IReadOnlyList<string> Collect_OwnerAsks(
        IReadOnlyList<TopicOwnerAsk>? handedIn,
        IReadOnlyList<(ITopicStatusMember Member, MemberStates State)> live,
        IPlanProgress? progress,
        DateTime now)
    {
        List<string> asks = [];

        foreach (var ask in handedIn ?? [])
            Add_Once(asks, Describe_Ask(ask.Source, ask.What, TopicStatusWording.Clock_OrNull(ask.Since, now)));

        foreach (var (member, state) in live)
        {
            if (state != MemberStates.BlockedOnOwner)
                continue;

            var lastEntry = MemberState_Resolver.Find_LastSessionEntry_OrNull(member.Entries);

            // The stamp goes through the TRUSTED reader, not a raw parse: a member's own header is
            // agent-written (decision 12), and a future stamp must produce no "(since …)" rather than
            // a confident wrong clock.
            var since = lastEntry != null
                && SessionDuration_Formatter.Try_ReadTrustedStamp(lastEntry.DateText, now, out var stamp)
                ? TopicStatusWording.Clock(stamp)
                : null;

            Add_Once(asks, Describe_Ask(member.MemberId, lastEntry?.Subject ?? "", since));
        }

        // `[?]` IS THE LEDGER'S "WAITING ON THE OWNER" MARKER, and it is a subset of `[!]` blocked —
        // see IPlanProgress.BlockedOnOwner, added because `[!]` was carrying both kinds and the owner
        // read a reviewer-blocked line as their own. Only `[?]` belongs in this field.
        foreach (var line in progress?.Lines ?? [])
        {
            if (line.Marker != LEDGER_BLOCKED_ON_OWNER_MARKER)
                continue;

            Add_Once(asks, Describe_Ask(LEDGER_SOURCE, line.Text, null));
        }

        return asks;
    }

    /// <summary>
    /// The ledger's marker for a line waiting on the OWNER — `- [?] …`. Normalised at the parse, so
    /// one character is the whole test.
    /// </summary>
    const string LEDGER_BLOCKED_ON_OWNER_MARKER = "?";

    /// <summary>
    /// What a ledger-derived ask names as its source. A ledger line has no author and no session: the
    /// honest source is the document, and the owner's own example of this field
    /// ("your browser pass on FIN-D-277") is a ledger line.
    /// </summary>
    const string LEDGER_SOURCE = "ledger";

    /// <summary>
    /// One ask, worded: `sup: bump the framework? (since 18:18)`. An empty `what` yields the source
    /// alone rather than a dangling colon — a source with nothing behind it is still a door to knock
    /// on, and a trailing `: ` reads as a value that failed to load.
    /// </summary>
    static string Describe_Ask(string source, string what, string? sinceClock)
    {
        var summary = TextSummary_Formatter.Summarize_Task(what, OWNER_ASK_WORDS);

        var head = string.IsNullOrWhiteSpace(summary) ? source : $"{source}: {summary}";

        return sinceClock == null ? head : $"{head} (since {sinceClock})";
    }

    /// <summary>
    /// Appends unless the identical wording is already there. Ordinal: these are strings this class
    /// built one line earlier, so a case-insensitive comparison could only ever collapse two items
    /// that differ in a way the owner can see.
    /// </summary>
    static void Add_Once(List<string> asks, string ask)
    {
        if (!asks.Contains(ask, StringComparer.Ordinal))
            asks.Add(ask);
    }

    /// <summary>
    /// FIELD 2 — the supervisor's own one-line state, IN ITS OWN WORDS, with the single thing the app
    /// knows for certain added: a usage-limit pause and when it lifts.
    ///
    /// <para>
    /// NULL DEGRADES, IT DOES NOT INVENT. The app does not know what a supervisor is doing — that is
    /// the entire reason this field is a declaration rather than a derivation — so with nothing
    /// declared the row is OMITTED rather than filled with a guess or a placeholder. A permanent
    /// "sup · no state declared" on every topic would be a row that says the app is missing data,
    /// which is not something the owner can act on (decision 15).
    /// </para>
    /// <para>
    /// THE ROW STILL APPEARS FOR THE TWO FACTS THE APP OWNS. A usage-limit pause is the one the owner
    /// asked for by name, and it is what the false stall alerts of 2026-09-09 were actually looking at
    /// — *"the supervisor was not waiting for anything but PAUSED for a usage limit"*. The context
    /// reading rides here too, which is where it belongs now that the supervisor HAS a row: it used to
    /// hang off the lead line for want of one, and its own docstring said so.
    /// </para>
    /// <para>
    /// THE RESUME TIME IS NOT PUT THROUGH THE FUTURE-STAMP GUARD, and it is the one clock on this line
    /// that must not be. Every other one describes something that has already happened, so a stamp
    /// ahead of `now` is a lie; this one is a time in the future BY DEFINITION, and refusing it would
    /// blank the only half of the field the owner can plan around.
    /// </para>
    /// </summary>
    static string? Build_SupervisorRow_OrNull(TopicStatusFields fields, ISessionContextUsage? supervisorContext, DateTime now)
    {
        List<string> parts = [];

        if (!string.IsNullOrWhiteSpace(fields.SupervisorDeclaredState))
        {
            parts.Add(fields.SupervisorDeclaredState.Trim());

            var declaredAt = TopicStatusWording.Clock_OrNull(fields.SupervisorDeclaredAt, now);

            if (declaredAt != null)
                parts.Add($"declared {declaredAt}");
        }

        if (fields.UsageLimitResumeAt != null)
            parts.Add($"paused for usage limit until {TopicStatusWording.Clock(fields.UsageLimitResumeAt.Value)}");

        // Describe_OrNull rather than the bang operator: the policy and the formatter each answer the
        // null question for themselves, so neither this line nor the reader has to assert what the
        // other already checked.
        var contextField = ContextVisibility_Policy.Show_Supervisor(supervisorContext)
            ? ContextUsage_Formatter.Describe_OrNull(supervisorContext)
            : null;

        if (contextField != null)
            parts.Add(contextField);

        if (parts.Count == 0)
            return null;

        return $"{SUPERVISOR_ROW_ID}{FIELD_SEPARATOR}{string.Join(FIELD_SEPARATOR, parts)}";
    }

    /// <summary>
    /// What field 2 calls the supervisor. `sup`, the owner's own abbreviation in the brief and the
    /// one already used by the channel tags they read (`[sup → imp-2]`), deliberately NOT a
    /// `• `-bulleted row: the bullets are the member roster, and a crew's supervisor is not a member
    /// of it.
    /// </summary>
    const string SUPERVISOR_ROW_ID = "sup";

    /// <summary>
    /// FIELD 3 — who is live, on what, in which state, for how long. One row each up to
    /// <see cref="MAX_MEMBER_ROWS"/>; beyond that a single grouped line, which is the owner's own
    /// shape: *"imp-1, imp-2, imp-3 working · rev-1 waiting"*.
    ///
    /// <para>
    /// The collapse keeps the STATES and drops the tasks, which is the right half to lose: with five
    /// sessions live the question the owner is asking is "is anybody stuck", and five truncated task
    /// labels answer it worse than five ids grouped by state.
    /// </para>
    /// </summary>
    static IReadOnlyList<string> Build_MemberLines(IReadOnlyList<(ITopicStatusMember Member, MemberStates State)> live, DateTime now)
    {
        if (live.Count == 0)
            return [];

        if (live.Count > MAX_MEMBER_ROWS)
            return [Build_CollapsedRoster(live)];

        return [.. live.Select(entry => Build_MemberLine(entry.Member, entry.State, now))];
    }

    /// <summary>
    /// The roster as one line, grouped by state word in reading order: `imp-1, imp-2 working ·
    /// rev-1 waiting on you`.
    ///
    /// <para>
    /// The groups come out in the order the members were already sorted into, because
    /// <c>GroupBy</c> yields groups in first-appearance order — so this method holds no second copy
    /// of the reading order. A second copy is how the collapsed line comes to disagree with the rows
    /// it replaces about which state matters most.
    /// </para>
    /// </summary>
    static string Build_CollapsedRoster(IReadOnlyList<(ITopicStatusMember Member, MemberStates State)> live)
    {
        var groups = live
            .GroupBy(entry => TopicStatusWording.Describe_State(entry.State), StringComparer.Ordinal)
            .Select(group => $"{string.Join(", ", group.Select(entry => entry.Member.MemberId))} {group.Key}");

        return string.Join(FIELD_SEPARATOR, groups);
    }

    /// <summary>
    /// The closed roster as a COUNT and nothing more — the owner's 2026-09-09 wording, "7 closed".
    ///
    /// <para>
    /// Closed members used to fall off this line entirely, and before that they lingered as stale rows
    /// with running durations. Neither was what the owner wanted: the roster of finished sessions is
    /// noise ("Out: … the closed roster"), but their NUMBER is the context that makes two live rows
    /// mean something — two of nine is an endeavour winding down, two of two is one just started.
    /// </para>
    /// <para>
    /// No bullet, for the same reason `last` has none: it is not a member.
    /// </para>
    /// </summary>
    static string Build_ClosedCountLine(int closedCount)
    {
        return $"{closedCount} closed";
    }

    /// <summary>
    /// FIELD 4 — the last relevant event, with its clock: `last · 18:00 · FIN-D-293 merged to
    /// staging`.
    ///
    /// <para>
    /// The clock is omitted rather than filled from <paramref name="now"/> when it is unknown or in
    /// the future. Dating the last event to the moment the line was DRAWN would make every event look
    /// like it just happened, which is the "on task under a minute" failure of decision 12 in a
    /// different field.
    /// </para>
    /// <para>
    /// WHICH event this is remains <c>TopicStatusLine_Planner.Pick_LastEvent_OrNull</c>'s to decide
    /// — the last session entry across the live members, by trusted stamp, WITH the stamp of that
    /// same entry. This method words it, and can no longer be handed a mismatched pair.
    /// </para>
    /// </summary>
    static string Build_LastLine(TopicLastEvent lastEvent, DateTime now)
    {
        // ONE ARGUMENT, so the clock and the subject cannot come from different events. They used to
        // be two parameters filled from two files — see TopicLastEvent for what that rendered.
        var clock = TopicStatusWording.Clock_OrNull(lastEvent.At, now);

        var clockPart = clock == null ? "" : $"{clock}{FIELD_SEPARATOR}";

        return $"last{FIELD_SEPARATOR}{clockPart}{TextSummary_Formatter.Summarize_Task(lastEvent.Subject, MEMBER_TASK_WORDS + 2)}";
    }

    /// <summary>
    /// FIELD 5 — `72/113 merged · 63 %`.
    ///
    /// <para>
    /// "MERGED" IS A RENAME OF THE LABEL, NOT OF THE NUMBERS, and saying so plainly is the point of
    /// this comment. There is no merged marker in the ledger vocabulary; <see cref="IPlanProgress"/>
    /// has Done and Total. In practice a ledger line is only marked done once it is merged, which is
    /// why the owner asked for the honest word — but the arithmetic is unchanged, and anyone adding a
    /// real merge signal later should change the numbers HERE and not add a second pair beside them.
    /// </para>
    /// <para>
    /// The percentage comes from <see cref="PlanProgress_Formatter.Percent"/> rather than being
    /// recomputed. The owner reads this line and `/progress` for the same ledger; two surfaces quoting
    /// different numbers is the failure item 10 exists to prevent, and it starts as two copies of one
    /// division. It TRUNCATES, so 72/113 reads 63% and not 64% — deliberate everywhere in this repo,
    /// because 75 of 76 must never read as 100%.
    /// </para>
    /// <para>
    /// NO "N RUNNING" AND NO "NOT DOING" (owner, 2026-09-09). Both are ledger bookkeeping the owner
    /// cannot act on from a phone, and `/progress` still carries them for when they want them.
    /// </para>
    /// <para>
    /// <paramref name="figuresUnchangedFor"/> stays, and it stays HERE, beside the figures it is about.
    /// The owner asked for it INSTEAD of a delta because this surface refreshes constantly: *"the
    /// difference doesn't make sense because at best, after updating every 10 seconds it would go back
    /// to 0."*
    /// </para>
    /// </summary>
    static string Build_MergedLine(IPlanProgress progress, TimeSpan? figuresUnchangedFor)
    {
        var unchanged = figuresUnchangedFor == null
            ? null
            : UnchangedFor_Formatter.Describe_OrNull(figuresUnchangedFor.Value);

        var unchangedPart = unchanged == null ? "" : $"{FIELD_SEPARATOR}{unchanged}";

        return $"{progress.Done}/{progress.Total} merged{FIELD_SEPARATOR}{PlanProgress_Formatter.Percent(progress)} %{unchangedPart}";
    }

    /// <summary>
    /// FIELD 6 — the heartbeat. A status line that has stopped being redrawn looks exactly like one
    /// describing a quiet orchestration, and this is the only field that can tell them apart.
    /// </summary>
    /// <summary>
    /// FIELD 6, STEPPED TO FIVE MINUTES — the same step as a member's duration, so PULSE's text
    /// changes at most every five minutes when nothing else moves.
    ///
    /// <para>
    /// MEASURED IN PRODUCTION, 2026-09-10 20:57-20:59, the first three minutes after the deploy: a
    /// per-minute clock made `editMessageText` fire once a minute PER LIVE TOPIC, and Telegram
    /// answered 429 with `retry_after` 20, then 22, then 32 seconds — once a minute, per topic. It
    /// throttles edits of the SAME message far harder than the twenty-a-minute group ceiling, which
    /// is the number the control bucket was sized from.
    /// </para>
    /// <para>
    /// MY OWN REASONING WAS WRONG, and this is the correction. Stage 8d excluded the heartbeat from
    /// the REPOST decision and left the edit firing every minute, on the grounds that "an edit
    /// notifies nobody" — true of the owner's phone and false of the API. Silence is not the only
    /// cost of a write.
    /// </para>
    /// <para>
    /// THE HEARTBEAT STILL EARNS ITS PLACE at five minutes: it exists so a frozen line can be told
    /// from a quiet orchestration, and five minutes is well inside the patience of somebody asking
    /// "is this thing still alive". A stepped clock reads as a clock; what it stops being is a
    /// per-minute write.
    /// </para>
    /// </summary>
    static string Build_UpdatedLine(DateTime now)
    {
        return $"{HEARTBEAT_PREFIX}{TopicStatusWording.Clock(Step_Down(now, UnchangedFor_Formatter.STEP_MINUTES))}";
    }

    /// <summary>
    /// A moment floored to a whole number of minutes. Seconds go too: a clock rendered `HH:mm` never
    /// showed them, and leaving them in would make two moments inside one step compare unequal for a
    /// difference the owner cannot see.
    /// </summary>
    static DateTime Step_Down(DateTime moment, int stepMinutes)
    {
        return new DateTime(
            moment.Year, moment.Month, moment.Day, moment.Hour,
            moment.Minute / stepMinutes * stepMinutes,
            0,
            moment.Kind);
    }

    /// <summary>How the heartbeat line opens, so a reader can recognise it without re-spelling it.</summary>
    const string HEARTBEAT_PREFIX = "updated ";

    /// <summary>
    /// THE LINE WITHOUT ITS HEARTBEAT — for the one caller that must ask "has anything actually
    /// CHANGED?" rather than "is this text different?".
    ///
    /// <para>
    /// The two questions came apart on 2026-09-10, when the owner ruled that PULSE is re-posted only
    /// when it is buried AND its content changed. The heartbeat is <see cref="Build_UpdatedLine"/> and
    /// it carries a wall clock, so PULSE's TEXT differs from the previous one at every minute
    /// boundary whatever the orchestration is doing — and a repost gated on the raw text would have
    /// degraded the owner's rule to "buried, then within sixty seconds". The delete-plus-post
    /// carrying no news would still happen; it would merely be late, which is the worse failure
    /// because it looks fixed.
    /// </para>
    /// <para>
    /// It strips only the LAST line and only when that line is the heartbeat, rather than filtering
    /// every line that starts with the prefix: the heartbeat is emitted last and unconditionally, and
    /// a member whose task began with the word "updated" is not a heartbeat.
    /// </para>
    /// </summary>
    public static string? Strip_Heartbeat(string? statusText)
    {
        if (statusText == null)
            return null;

        var lastBreak = statusText.LastIndexOf('\n');

        if (lastBreak < 0)
            return statusText.StartsWith(HEARTBEAT_PREFIX, StringComparison.Ordinal) ? "" : statusText;

        return statusText.AsSpan(lastBreak + 1).StartsWith(HEARTBEAT_PREFIX, StringComparison.Ordinal)
            ? statusText[..lastBreak]
            : statusText;
    }

    /// <summary>
    /// The live members, in the owner's reading order — waiting on them, then working, then idle —
    /// each carrying its resolved state so nothing downstream resolves it again.
    ///
    /// <para>
    /// STABLE inside a bucket: <c>OrderBy</c> is a stable sort, so members that share a state keep the
    /// roster's order. A line whose rows reshuffle between refreshes has to be re-read every time.
    /// </para>
    /// </summary>
    static IReadOnlyList<(ITopicStatusMember Member, MemberStates State)> Order_LiveMembers(IReadOnlyList<ITopicStatusMember> members)
    {
        return
        [
            .. members
                .Where(member => !member.IsClosed)
                .Select(member => (Member: member, State: MemberState_Resolver.Resolve(member.Entries)))
                .OrderBy(entry => TopicStatusWording.Reading_Order(entry.State))
        ];
    }

    static int Count_Closed(IReadOnlyList<ITopicStatusMember> members)
    {
        return members.Count(member => member.IsClosed);
    }

    /// <summary>
    /// A member row is "who · what · which state · how long", and every part of it is agent-written
    /// except the state word, which is resolved from what the agent declared. The duration comes from
    /// the SUPERVISOR's last brief stamp through the one formatter this repo has (item 12), which
    /// returns null for a stamp in the future rather than a confident wrong number. Context % is
    /// included for solo/supervisor always, and for other members only if > 90%.
    ///
    /// <para>
    /// THE STATE FIELD IS NEW (owner, 2026-09-09) and it is the answer to a row that showed a task
    /// and left the owner guessing whether anyone was on it. It sits between the task and the
    /// duration, in the owner's own field order, so the row reads as a sentence: imp-1, on the marker
    /// fix, working, for four minutes.
    /// </para>
    /// <para>
    /// AN IDLE ROW CARRIES THE STATE AND NOTHING ELSE. There is no task to show — showing the last
    /// one would date a stale brief with a live duration — and "standing by" is both the state word
    /// and the whole content of the row.
    /// </para>
    /// </summary>
    static string Build_MemberLine(ITopicStatusMember member, MemberStates state, DateTime now)
    {
        // ONE PATH, BUILDING A FIELD LIST, rather than a branch per combination of optional fields.
        // With a task, a state, a duration and a context reading all able to be absent independently,
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

            fields.Add(TopicStatusWording.Describe_State(state));

            // STEPPED, NOT EXACT — the owner's ruling of 2026-09-10. A per-minute duration made
            // PULSE's text differ on every tick, so a buried line was re-posted for the minute hand
            // rather than for news: the same defect the heartbeat had, in the one field that reads a
            // live clock. The step comes from UnchangedFor_Formatter rather than a number of its own,
            // so this line's two ticking fields move in lockstep and cannot be tuned apart by
            // accident.
            var onTaskFor = SessionDuration_Formatter.Describe_SinceStamp_Stepped_OrNull(
                brief.DateText, now, UnchangedFor_Formatter.STEP_MINUTES);

            // A missing duration DROPS ITS FIELD rather than leaving the separator standing: a row
            // ending in a dangling `· ` reads as a value that failed to load, when the truth is that
            // the stamp was not trustworthy enough to date.
            if (onTaskFor != null)
                fields.Add(onTaskFor);
        }

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
    static bool Is_Idle(MemberStates state)
    {
        return state == MemberStates.StandingBy || state == MemberStates.NewNoTraffic;
    }

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
    ///
    /// IT SURVIVES THE 2026-09-09 RULE "never the truncated subject of the last entry", and the
    /// reading is deliberate: that rule is about the `last` EVENT being reused as a member's task,
    /// which is the field directly below the roster. A solo has no other source for what it is doing
    /// — there is no supervisor and never will be — so applying the rule here would restore the
    /// 100%-"standing by" defect the owner sent back by name.
    /// </summary>
    static IChannelEntry? Find_CurrentTask_OrNull(ITopicStatusMember member, MemberStates state)
    {
        var brief = MemberState_Resolver.Find_LastBrief_OrNull(member.Entries);

        if (brief != null || MemberKind_Ids.Resolve_Kind(member.MemberId) != MemberKinds.Solo)
            return brief;

        return MemberState_Resolver.Find_LastEntryBy_OrNull(member.Entries, ChannelAuthors.Solo);
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

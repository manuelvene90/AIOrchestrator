using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Whether a session is dormant in a way that needs waking. The member's nudge and the supervisor's
/// nudge live HERE, together, because they were written apart and were exhaustive without either
/// author noticing: a channel ends either in a member's entry or in someone else's, so one of the
/// two always fired. Every idle member in every open orchestration was woken every 8 minutes for as
/// long as it stayed correctly idle — including a nudge about a nudge — and "append one line and go
/// quiet" only chose which of the two it got.
///
/// The state that had no name is the fix: a member that has DECLARED itself idle
/// (<see cref="MemberStates.StandingBy"/>) owes nobody a reply and is owed none, so neither nudge
/// fires. Any inbound entry makes the declaration no longer the last entry, and both go live again.
///
/// What is deliberately NOT weakened: a member that stops with work still announced and says
/// nothing is woken exactly as before. That nudge is load-bearing — a session cannot give itself the
/// next turn — and it is the false positives around it that were not.
/// </summary>
public static class Nudge_Decider
{
    /// <summary>
    /// The member stopped with work in flight and nobody about to speak to it. Its monitor fires
    /// only when someone ELSE writes, so this state cannot resolve itself.
    /// </summary>
    public static bool Is_DormantMidWork(IReadOnlyList<IChannelEntry> entries, bool hasBeenBriefed)
    {
        if (entries.Count == 0)
            return false;

        if (!ChannelAuthor_Kinds.Is_Member(entries[entries.Count - 1].Author))
            return false;

        // Never briefed is not dormant — it is a freshly spawned member waiting for work, which is
        // the correct state for the imp-1 and rev-1 that every orchestration starts with. Nudging
        // them for saying "online" respawned them on a loop and cost them their context.
        if (!hasBeenBriefed)
            return false;

        return !Is_LegitimatelyQuiet(entries);
    }

    /// <summary>
    /// Has a supervisor EVER written here — across the live file AND its archive.
    ///
    /// CLAUDE.md item 13: <see cref="Channel_Compactor"/> moves older entries into a sibling archive,
    /// so a live-file scan is not monotonic and "no supervisor entry" stops meaning "never briefed"
    /// the moment a long-running channel compacts. The failure is silent and one-directional: a
    /// briefed member reverts to looking freshly spawned, and the stalled-mid-task nudge — the
    /// load-bearing one — switches off for exactly the members that have been running longest.
    ///
    /// Counted through the one reader that spans both, never by re-scanning here.
    /// </summary>
    public static bool Has_BeenBriefed(string channelFilePath)
    {
        return ChannelHistory_Counter.Count_Entries_ByAuthor(channelFilePath, ChannelAuthors.Supervisor) > 0;
    }

    /// <summary>
    /// WHICH unanswered thing a member is being nudged about — so it can be nudged ONCE for it.
    ///
    /// The nudge used to repeat every 8 minutes for as long as a member behaved correctly, and the
    /// app's own entry was the engine: it made <see cref="Has_UnansweredInboundTraffic"/> true, it
    /// woke the member (whose watcher fires on any write), the waking proved the member alive, that
    /// proof cleared the nudged map, and the quiet clock — restarted by that same write — elapsed.
    /// Nothing outside the app was needed for any turn of it. Measured on two channels; one member
    /// took three in a row while saying nothing, which is what the protocol tells it to do.
    ///
    /// This is the identity the app remembers, and comparing it is the whole gate: the app's own
    /// writes never change the last CONVERSATION entry, so nothing the app says can ever qualify a
    /// member for another nudge. One nudge per unanswered thing.
    ///
    /// THAT GUARANTEE HOLDS ONLY WHILE AN IDENTITY CAN BE FOUND, and stating it as an absolute is how
    /// it went wrong: a null here is not "nothing to compare", it is NO MEMORY — the caller skips the
    /// gate and records nothing, so the loop is back. There were TWO routes to that null and both are
    /// now closed: compaction, below, and a channel holding only app entries with no conversation
    /// anywhere — reachable with no compaction at all, when the app writes to a member channel before
    /// its first brief (a `/resume` broadcast will do it), leaving the member eligible through
    /// <see cref="Has_UnansweredInboundTraffic"/> with nothing to key on. That second one is answered
    /// by <see cref="NO_CONVERSATION_YET"/> and <see cref="Identify_NudgeSubject"/>, which is what the
    /// engine actually calls — this function may still return null, and its caller is why that is safe.
    ///
    /// NOTHING BELOW THIS LINE MAY SAY THE SECOND ROUTE IS OPEN. It was described as open here, and in
    /// HANDOFF.md, for two commits after `5f3dc1f` closed it — including through a docs-only commit
    /// whose whole job was tidying this docstring and which moved the stale paragraph instead of
    /// deleting it. A docs-only commit is the one nobody re-reads for truth.
    ///
    /// IT IS THE RAW TEXT AND IT MUST NEVER BE THE INDEX OR THE TIMESTAMP. Both are agent-written and
    /// neither is unique: `option-lab-2` carried two `[80]`s and two `[81]`s on 2026-08-10, and one
    /// evening's traffic produced two duplicate indices in a single channel. A genuinely NEW entry
    /// that repeats an index — or that lands in the same MINUTE as the one before it, which is the
    /// resolution of a header stamp — would compare equal to what is remembered here and lose the
    /// nudge it earned. That failure is silent, and it is the exact defect this gate replaces wearing
    /// the other mask. The next person to touch this will reach for the index because it is smaller;
    /// this paragraph is why they should not.
    ///
    /// IT SPANS THE ARCHIVE, AND READING ONLY THE LIVE FILE WAS THE WHOLE LOOP COMING BACK — CLAUDE.md
    /// item 13, thirty lines from <see cref="Has_BeenBriefed"/>, which already gets this right.
    /// <see cref="Channel_Compactor"/> moves older entries into a sibling archive, so once the last
    /// conversation entry is compacted out, a live-only read returns null. Null means "no memory": the
    /// caller skips the gate AND records nothing, so it nudges — and that nudge becomes the next
    /// round's unanswered thing. Every 8 minutes, forever, needing nobody, on exactly the channels that
    /// have been running longest.
    ///
    /// Measured, not feared: `ai-orchestrator-3/imp-1` ends at entry [395] whose body reads *"Entry
    /// [394] FROM app has been waiting 8 min with no reply from you"* — the app nudging a member about
    /// its own previous nudge. Two `da-vinci-fintech-suite-5` channels show the same shape.
    ///
    /// The previous docstring called the null case harmless and named only "a channel holding nothing
    /// but app entries". That case is real and still returns null — but it was never the only route to
    /// one, and the other route restarted the defect this gate exists to end.
    ///
    /// LIVE WINS OVER ARCHIVE, because the compactor only ever moves from the front: anything archived
    /// is older than anything live. Preferring the archive would pin every member to an ancient entry
    /// and stop a genuinely new brief from earning its own nudge — the mute switch, from the far side.
    /// </summary>
    public static string? Identify_LastConversationEntry_OrNull(IReadOnlyList<IChannelEntry> entries, string channelFilePath)
    {
        var live = MemberState_Resolver.Find_LastConversationEntry_OrNull(entries);

        if (live != null)
            return live.RawText;

        return MemberState_Resolver
            .Find_LastConversationEntry_OrNull(ChannelHistory_Counter.Read_ArchivedEntries(channelFilePath))
            ?.RawText;
    }

    /// <summary>
    /// What a channel with NO conversation entry anywhere is keyed on, so it can be nudged once
    /// instead of forever.
    ///
    /// AT MOST ONCE, NOT NEVER (owner-facing ruling 2026-08-13). "Never nudge an app-only channel"
    /// was the tempting rule and it drops the one wake that matters: a `/resume` broadcast is an app
    /// entry a respawned member is genuinely supposed to act on, and it may be the only thing telling
    /// it to start. So the channel gets its one nudge, remembered, and never a second.
    ///
    /// IT IS A SENTINEL RATHER THAN AN ENTRY'S TEXT, AND THAT IS FORCED — the brief asked for the raw
    /// text the gate already uses, and for app-only channels there is none that holds still. Keying on
    /// the LAST entry is the obvious reading and it rebuilds the exact loop being fixed: the nudge is
    /// itself an app entry, so the last entry changes the moment the nudge lands, the next round reads
    /// a different key, and it nudges again forever. Any key derived from "the newest app entry" has
    /// that property. The state being remembered is not an entry, it is "this channel had nothing to
    /// be nudged about when I nudged it", so the sentinel says exactly that and stops moving.
    ///
    /// It cannot collide with a real identity: those are whole entries and always begin `## [`.
    /// </summary>
    public const string NO_CONVERSATION_YET = "<no conversation entry in this channel>";

    /// <summary>
    /// What a member is being nudged ABOUT, always answerable — the last conversation entry above; on a
    /// channel that has none, the last entry the app did not write while WAKING this member; and
    /// <see cref="NO_CONVERSATION_YET"/> only when there is nothing of either kind.
    ///
    /// This is what the engine compares and records. It never returns null, and that is the point: a
    /// null identity used to skip the gate AND skip the record together, which was the loop.
    ///
    /// THE CONSTANT SENTINEL WAS ONE NUDGE PER PROCESS, NOT ONE PER THING (rev-5's R1). Being constant,
    /// it matched for ever once recorded — so a second `/resume` could not earn the wake the sentinel's
    /// own docstring promises it, and after the single orphan recovery the member stayed silent for the
    /// life of the app run. The owner's unstick command was the one thing that could not unstick such a
    /// member: the same `/resume` path as the delayed-nudge finding, one layer down.
    ///
    /// WHY NOT A CLEAR ON `/resume`, which is the obvious fix: that adds a RELEASE site — a second
    /// place that must fire at the right moment, on a memo that today has none — and a value the engine
    /// must remember to commit at the right moment is a value it can commit at the wrong one. This
    /// keeps the single write site and lets the subject stop matching because REALITY MOVED.
    ///
    /// WHAT COUNTS AS THE APP'S OWN WAKE is <see cref="Nudge_Wording.Is_WakeSubject"/>, sharing its
    /// constants with the code that writes them. A `/resume` is deliberately not one: it is the owner
    /// speaking through the app and is exactly what such a member is supposed to act on.
    ///
    /// SKIPPING RATHER THAN TAKING THE NEWEST ENTRY is what keeps the loop closed. Any key derived from
    /// the newest app entry moves the instant the nudge lands — the nudge IS an app entry — so the next
    /// round reads a different key and nudges again, rebuilding the exact defect. Measured, not argued:
    /// <c>AnAppOnlyChannelKeepsOneSubjectAsFurtherAppEntriesArrive</c> is the case that reddens.
    /// </summary>
    public static string Identify_NudgeSubject(IReadOnlyList<IChannelEntry> entries, string channelFilePath)
    {
        var conversation = Identify_LastConversationEntry_OrNull(entries, channelFilePath);

        if (conversation != null)
            return conversation;

        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (Nudge_Wording.Is_WakeSubject(entries[index].Subject))
                continue;

            return entries[index].RawText;
        }

        return NO_CONVERSATION_YET;
    }

    /// <summary>
    /// Somebody else wrote last and the member has not replied. Note this counts the APP's own
    /// entries: that is deliberate, because the escalation to orphan-recovery is what proves a
    /// member's monitor is dead, and it can only run on a member that has already been nudged.
    ///
    /// UNCHANGED ON PURPOSE. Two fixes that reinterpreted this predicate were tried and withdrawn:
    /// skipping app entries here makes it and <see cref="Is_DormantMidWork"/> false at the same
    /// instant, the settled-reset fires, and a genuinely dead session can never escalate. The
    /// repetition was never in this predicate — it was in what the app remembered about the nudge it
    /// had already sent, which was nothing.
    /// </summary>
    public static bool Has_UnansweredInboundTraffic(IReadOnlyList<IChannelEntry> entries)
    {
        if (entries.Count == 0)
            return false;

        return !ChannelAuthor_Kinds.Is_Member(entries[entries.Count - 1].Author);
    }

    /// <summary>
    /// The member filed something the supervisor has not answered. A declaration is not a filing:
    /// <see cref="MemberStates.StandingBy"/> asks for no verdict, and treating it as one moved the
    /// loop rather than ending it — the member went quiet and the supervisor was woken every 8
    /// minutes instead, told it had failed to answer an entry that had asked it for nothing.
    /// </summary>
    public static bool Owes_MemberAVerdict(IReadOnlyList<IChannelEntry> entries)
    {
        if (entries.Count == 0)
            return false;

        if (!ChannelAuthor_Kinds.Is_Member(entries[entries.Count - 1].Author))
            return false;

        // ASKS WHETHER WORK WAS FILED, not whether the member looks idle. Those are different
        // questions and this line collapsed them: it returned false for any member resolving to
        // StandingBy, so a filed report that ENDED with the declaration — the shape the role commands
        // tell members to write — left the supervisor with no reminder that it owed a verdict.
        //
        // The docstring three lines above named both states while the code recognised one. A spurious
        // nudge costs a wake; a missing one costs filed work sitting unread with nothing anywhere
        // saying so.
        return MemberState_Resolver.Is_AwaitingVerdict(entries);
    }

    /// <summary>
    /// Quiet for a reason, by either of the two shapes it takes: somebody owes the member a reply
    /// (a filed report, a question with the owner), or the member owes nothing and has SAID SO.
    /// </summary>
    static bool Is_LegitimatelyQuiet(IReadOnlyList<IChannelEntry> entries)
    {
        var state = MemberState_Resolver.Resolve(entries);

        return state == MemberStates.AwaitingSupervisorReview
            || state == MemberStates.BlockedOnOwner
            || state == MemberStates.StandingBy;
    }

}

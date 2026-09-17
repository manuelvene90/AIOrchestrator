using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.StatusNotes;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.WakeDecision;

/// <summary>
/// WHETHER THIS SESSION TAKES A TURN NOW, AND WITH WHAT — in ONE place, so it can be asked about a
/// session this process will never run. Every rule here used to live inside
/// <c>PrintTurnDispatcherModel.Consider_Session</c>, where the only way to ask the question was to
/// take the turn; a terminal session's monitor therefore answered it again, in bash, without a
/// cursor, without the digest and without the notion of an entry that should wake nobody.
///
/// <para>
/// WHAT IS DELIBERATELY NOT HERE: the per-PROCESS gates. The coalesce window, the stall, the
/// usage-limit appointment, the retry backoff and the digest's own hold clock all read the
/// dispatcher's in-memory tracker — they are about what THIS process has already done, not about
/// what the channels say — so they stay with it and the hold stamp arrives as
/// <c>digestHeldSince</c>. What is here is exactly the part a second machine has to agree with.
/// </para>
/// </summary>
public static class WakeDecision_Resolver
{
    static readonly StringComparer SOURCE_KEYS = StringComparer.OrdinalIgnoreCase;

    /// <summary>The reason a boot turn carries — see <see cref="Needs_BootTurn"/>.</summary>
    public const string BOOT_TURN_REASON = "the session has not taken a turn yet";

    /// <summary>
    /// Told about each source AS IT IS READ, while the cursor and the file's contents are still the
    /// ones the pending set was selected against. It exists for the dispatcher's cursor bookkeeping —
    /// baselining a source it has never seen, persisting that, and the archive-gap warning — which
    /// needs both and which <see cref="SourceRead"/> deliberately refuses to carry away from here.
    /// </summary>
    /// <param name="cursorIsNew">
    /// Whether the cursor was MADE on this read rather than found in the state file — the caller's
    /// signal that the session's cursor set changed and has to be written.
    /// </param>
    public delegate void SourceReadObserver(ITurnSource source, ITurnCursor cursor, IReadOnlyList<IChannelEntry> entries, bool cursorIsNew);

    /// <summary>
    /// THE WHOLE QUESTION, ASKED FROM OUTSIDE ANY TURN: resolve the session's channels, read them,
    /// and decide. Null is "not yet" — nothing pending, or a member's ordinary report still inside
    /// its digest window.
    ///
    /// <para>
    /// IT WRITES NOTHING. A cursor made here for a source the state file has never seen lives for the
    /// length of the call; whoever ACTS on the decision is what persists cursors, so asking the
    /// question can never be the thing that loses an entry. The dispatcher, which does act, reads
    /// through <see cref="Read_Pending"/> with an observer and does its own bookkeeping.
    /// </para>
    /// </summary>
    public static IWakeDecision? Resolve_OrNull(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IPrintSessionState state,
        IRunnerConfigs configs,
        DateTime? digestHeldSince,
        DateTime nowLocal)
    {
        var sources = TurnSources_Resolver.Resolve(paths, store, state.Role, state.OrchId, state.MemberId);
        var reads = Read_Pending(state, state.Role, sources);

        if (reads.Count == 0)
            return null;

        var ordered = PendingTraffic_Orderer.Order([.. reads.Select(read => (read.Source, read.Pending))]);

        return Decide_OrNull(
            paths,
            state,
            sources,
            ordered,
            Describe_FirstContactSources(reads),
            Reviewing.RoutedHold_Policy.Resolve_RidingOnly(paths, state),
            digestHeldSince,
            nowLocal,
            configs.MemberDigestWindow);
    }

    /// <summary>
    /// THE CHANNELS THIS SESSION HAS NEVER BEEN HANDED ANYTHING FROM — the digest's first-entry rule
    /// (<see cref="SourceRead.NothingEverDelivered"/>). Built with the cursor set's own comparer,
    /// because a source key is a word an agent typed and the two sets have to agree on what "the same
    /// channel" means.
    /// </summary>
    public static IReadOnlyCollection<string> Describe_FirstContactSources(IReadOnlyList<SourceRead> reads)
    {
        return new HashSet<string>(reads.Where(read => read.NothingEverDelivered).Select(read => read.Source.Key), SOURCE_KEYS);
    }

    /// <summary>
    /// Every source read, and what of each is pending — the read half of the question, with no
    /// side effects of any kind. <paramref name="observe"/> is how a caller that must also PERSIST
    /// what this read implies gets at the cursor and the entries while they are fresh.
    /// </summary>
    public static IReadOnlyList<SourceRead> Read_Pending(
        IPrintSessionState state,
        SessionRoles role,
        IReadOnlyList<ITurnSource> sources,
        SourceReadObserver? observe = null)
    {
        var known = state.Cursors.ToDictionary(cursor => cursor.SourceKey, SOURCE_KEYS);

        // NO CURSORS AT ALL means this session has never been through here — a state file written before
        // sources existed, or one whose `sources` array could not be read. Its channel holds a whole life
        // of traffic and none of it has been handed over by anything that recorded the fact, so it is
        // HISTORY, absorbed exactly as registration absorbs it. Treating it as "nothing delivered" would
        // replay up to a full live channel into one turn, which is the very thing the store's own note
        // says must not happen. A session that HAS cursors and meets a NEW key is the opposite case, and
        // is handled below.
        var firstSightOfThisSession = state.Cursors.Count == 0;

        List<SourceRead> reads = [];

        foreach (var source in sources)
        {
            var entries = ChannelHistory_Cache.Read_Entries(source.ChannelFilePath);
            var cursorIsNew = !known.TryGetValue(source.Key, out var cursor);

            if (cursorIsNew)
            {
                // A SOURCE THAT APPEARS WHILE THE SESSION IS RUNNING STARTS EMPTY, so everything in it
                // is traffic and none of it is absorbed. A channel that turns up now belongs to a member
                // that was created now, and its very first entry — the member's boot greeting, landing
                // between its spawn and the next tick — is exactly what a supervisor is here to answer.
                // Absorbing it would swallow the one entry this rule can ever see.
                cursor = firstSightOfThisSession
                    ? TurnCursor_Factory.Create_Baseline(source, role, entries)
                    : TurnCursor_Factory.Create_Empty(source);
            }

            observe?.Invoke(source, cursor!, entries, cursorIsNew);

            reads.Add(new SourceRead(source, PrintTurn_Trigger.Select_Pending(role, entries, cursor!), Nothing_EverDelivered(cursor!)));
        }

        return reads;
    }

    /// <summary>
    /// THE DECISION ITSELF, from traffic somebody else has already read. The dispatcher enters here
    /// because its own gates need the ordered set before it can ask;
    /// <see cref="Resolve_OrNull"/> enters here after reading for itself. One body either way.
    /// </summary>
    /// <param name="ridingOnlyIdentities">
    /// The pending entries that must be HANDED to a turn without being allowed to START one —
    /// <see cref="Reviewing.RoutedHold_Policy.Resolve_RidingOnly"/>, which today means a fix report the
    /// app has already relayed to a reviewer. Empty for every orchestration that has declared no
    /// contract, which is almost all of them, and the set is then not even walked.
    ///
    /// <para>
    /// IT HAS NO DEFAULT VALUE ON PURPOSE. Three callers decide whether a session takes a turn, and a
    /// default is how one of them silently skips a rule the other two apply — the lesson plan 01 took
    /// from <c>CreateFrom_Existing_*</c>. A caller that has nothing to hold passes an empty set and
    /// says so.
    /// </para>
    /// </param>
    public static IWakeDecision? Decide_OrNull(
        ISupervisionPaths paths,
        IPrintSessionState state,
        IReadOnlyList<ITurnSource> sources,
        IReadOnlyList<PendingEntry> ordered,
        IReadOnlyCollection<string> firstContactSources,
        IReadOnlyCollection<string> ridingOnlyIdentities,
        DateTime? digestHeldSince,
        DateTime nowLocal,
        TimeSpan memberDigestWindow)
    {
        // NOTHING PENDING IS NOTHING TO DO, except for the boot turn. Asked here as well as at the
        // dispatcher's own early return, because WakeUp_Policy answers an EMPTY pending set with
        // "boot turn" unconditionally — it was written when only the boot turn could reach it with
        // one, and a caller that skipped this test would be told every idle session is booting.
        if (ordered.Count == 0 && !Needs_BootTurn(state))
            return null;

        // THE ENTRIES THAT RIDE RATHER THAN WAKE (Reviewing.RoutedHold_Policy). The wake-up rules are
        // asked about everything EXCEPT them; the turn, if one starts, is still handed the WHOLE set a
        // few lines below. That asymmetry is the feature and not an oversight: a fix report the app has
        // already relayed to a reviewer is something the supervisor should READ on its next turn and
        // never a reason to buy one, exactly as the app's own notes are (With_AgentNotes).
        var wakers = ridingOnlyIdentities.Count == 0
            ? ordered
            : (IReadOnlyList<PendingEntry>)[.. ordered.Where(item => !ridingOnlyIdentities.Contains(ChannelEntry_Digest.Compute(item.Entry)))];

        // AND AN EMPTY WAKER SET IS "NOT YET", NEVER A BOOT TURN. WakeUp_Policy answers an empty
        // pending set with "boot turn" unconditionally — the trap the guard above already documents —
        // so a set whose every entry is riding must return here rather than be handed to it.
        if (wakers.Count == 0 && !Needs_BootTurn(state))
            return null;

        // THE SUPERVISOR IS WOKEN TO DECIDE, NOT TO TAKE NOTE (spec §C4, measured 6–9 Sep 2026: 247 of
        // its ~400 wake-ups were member traffic, at ~1 M input tokens each). The owner and a member
        // that says it is blocked start a turn now, exactly as before; a member's ordinary report is
        // held for MemberDigestWindow so several of them ride ONE turn. Nothing is lost by being held
        // — a turn takes every pending entry, so held reports ride whatever starts the next one,
        // including the owner's own next message.
        //
        // A SESSION THAT HAS NEVER TAKEN A TURN IS NEVER HELD. The boot turn reaches the policy as an
        // empty pending set and is released by it — but only while the set IS empty, and a supervisor
        // whose very first traffic is a member's report has a non-empty one. Its greeting is what
        // creates the orchestration's Telegram topic (Needs_BootTurn above), so holding that report for
        // the digest would hold the owner's own way in behind it.
        var wakeReason = Needs_BootTurn(state)
            ? BOOT_TURN_REASON
            : WakeUp_Policy.Resolve_WakeReason_OrNull(wakers, firstContactSources, digestHeldSince, nowLocal, memberDigestWindow);

        if (wakeReason == null)
            return null;

        // `ordered` AND NOT `wakers`: the riding entries were withheld from the RULES and are handed
        // over here with everything else. Nothing was consumed by being held — no cursor moved — so the
        // turn this creates carries the relayed fix report beside the re-review that released it.
        return WakeDecision_Factory.Create(wakeReason, With_AgentNotes(paths, state, sources, ordered, nowLocal), sources);
    }

    /// <summary>
    /// THE APP'S NOTES TO THIS SESSION RIDE THE TURN THAT IS STARTING — first, as context, before the
    /// traffic that started it — and never start one (see <see cref="PrintTurn_Trigger.Select_AgentNotes"/>
    /// for what they are and why nothing carried them before). Added AFTER every wake-up rule has
    /// decided, so a note can neither start a turn nor change which rule released one. A boot turn
    /// carries none: its empty pending set is what tells the executor it is a boot.
    ///
    /// <para>
    /// TWO FILES SINCE 2026-09-15, ONE RULE, ONE CAP. Plan 02 routes the bookkeeping kinds to
    /// <see cref="Channels.StatusLog.StatusLog_Store"/> when a role's sink says so, and NOTHING
    /// MIGRATES what is already in the channel — so for the whole of the transition a session's notes
    /// live in both files and both are read here, ONCE, into one call of the selector. Asking each
    /// source for its own newest five and concatenating would put ten notes at the head of a prompt,
    /// which is the growing boot this series exists to shrink (CLAUDE.md decision 12: the rule grows a
    /// source, never a second implementation).
    /// </para>
    /// <para>
    /// THE LOG IS READ WHATEVER THE SINK SAYS. It is not asked whether this role's bookkeeping is
    /// routed today: a machine that sets the key and then unsets it would otherwise strand every note
    /// written in between, unread, for ever. An absent log reads as no entries and costs one
    /// <c>File.Exists</c>.
    /// </para>
    /// <para>
    /// THE CHANNEL HALF STILL NEEDS ITS CURSOR AND THE LOG HALF DELIBERATELY DOES NOT, which is the one
    /// asymmetry here. A session with no cursor for its own channel has never been read through
    /// <c>Read_Pending</c>'s baseline, and that baseline is what declares a live channel's whole
    /// history to be history; handing its notes over on the strength of "no cursor says otherwise"
    /// would replay a channel's every note into one turn. The log has no baseline BY DESIGN
    /// (<see cref="StatusNotes_Bookkeeper.Is_Delivered"/> says why): a missing <c>.status</c> cursor
    /// means a session that existed before the log did, and the notes written to it since are exactly
    /// what the first turn after the upgrade should carry.
    /// </para>
    /// </summary>
    static IReadOnlyList<PendingEntry> With_AgentNotes(ISupervisionPaths paths, IPrintSessionState state, IReadOnlyList<ITurnSource> sources, IReadOnlyList<PendingEntry> ordered, DateTime nowLocal)
    {
        if (ordered.Count == 0)
            return ordered;

        var own = sources.FirstOrDefault(source => string.Equals(source.ChannelFilePath, state.ChannelFilePath, StringComparison.OrdinalIgnoreCase));

        if (own == null)
            return ordered;

        var ownCursor = state.Cursors.FirstOrDefault(candidate => SOURCE_KEYS.Equals(candidate.SourceKey, own.Key));

        IReadOnlyList<IChannelEntry> channelEntries = ownCursor == null ? [] : ChannelHistory_Cache.Read_Entries(own.ChannelFilePath);
        var logEntries = StatusNotes_Bookkeeper.Read_Entries(paths, state);

        var notes = PrintTurn_Trigger.Select_AgentNotes(
            [.. channelEntries, .. logEntries],
            entry => ownCursor?.Delivered.Contains(ChannelEntry_Digest.Compute(entry)) == true
                || StatusNotes_Bookkeeper.Is_Delivered(state, entry),
            nowLocal);

        if (notes.Count == 0)
            return ordered;

        // The notes are labelled under the session's OWN channel, as they always were: the prompt
        // groups traffic by source and the session has never been told the status log exists.
        return [.. notes.Select(note => new PendingEntry(own, note)), .. ordered];
    }

    /// <summary>
    /// Whether this session still owes the owner the greeting nothing else can produce.
    ///
    /// <para>
    /// TWO ROLES, because exactly two write an orchestration's owner channel: the SUPERVISOR of a crew
    /// and the SOLO of a basic orchestration (<see cref="TurnSources_Resolver.Resolve_Own"/>
    /// maps both onto <c>owner-channel.md</c>). A member's greeting reaches nobody but its supervisor,
    /// and booting every member on registration would buy two model turns each — one to greet, one for
    /// the supervisor woken by the greeting — for something no owner is waiting on. The GENERAL
    /// supervisor is excluded for a different reason: its topic is the supergroup's General, pinned by
    /// the owner, so it is reachable before it has ever run.
    /// </para>
    /// <para>
    /// ONCE, and "never completed a turn" is the whole test. An app restart re-registers every session
    /// and finds <see cref="IPrintSessionState.ExecutedTurns"/> non-empty, so nothing is dispatched and
    /// no second greeting is filed. A boot turn that FAILED leaves the list empty and is retried — under
    /// <c>PrintTurnDispatcherModel.MAX_ATTEMPTS</c> and the retry backoff like any other turn, because a
    /// supervisor whose one and only turn died is exactly the case where giving up is silent.
    /// </para>
    /// <para>
    /// AND NEVER FOR A SESSION THIS APP DOES NOT DRIVE (<see cref="IPrintSessionState.DrivesTurns"/>).
    /// A terminal session HAS a process from the moment it is spawned — the window carries its role
    /// command and the greeting is the first thing it does — so the no-process deadlock this rule
    /// exists to break cannot happen to it. Two things go wrong without the clause, both found while
    /// wiring the wake-ticket sweep (one-wake-model Task 8): nothing ever records an executed turn for
    /// a terminal session, so "has not taken a turn yet" is permanently TRUE — it would be handed a
    /// wake ticket on every tick for ever with nothing pending, and every ticket it did earn would be
    /// stamped with this reason instead of the one that actually woke it. The dispatcher is unaffected:
    /// <c>Consider_Session</c> returns above this on the same flag.
    /// </para>
    /// </summary>
    public static bool Needs_BootTurn(IPrintSessionState state)
    {
        return state.DrivesTurns && state.ExecutedTurns.Count == 0 && state.Role is SessionRoles.Supervisor or SessionRoles.Solo;
    }

    /// <summary>
    /// HAS THIS SESSION EVER BEEN HANDED ANYTHING FROM THIS CHANNEL — asked of the cursor, and asked
    /// in a way that CANNOT COME BACK TRUE. Two readers depend on it: the digest's first-entry
    /// exemption (<see cref="SourceRead.NothingEverDelivered"/>) and the archive-gap warning
    /// (<c>PrintTurnDispatcherModel.Warn_IfEntriesWereArchivedUndelivered</c>), and one implementation
    /// is the whole point — they were two copies of the same wrong test.
    ///
    /// <para>
    /// REVIEW FINDING, 2026-09-10, and it is CLAUDE.md decision 13's exact shape: a stored count
    /// re-derived from a live read. <see cref="TurnCursor_Factory.CreateFrom_Delivered"/> prunes
    /// <see cref="ITurnCursor.Delivered"/> to the identities still in the LIVE file, so after
    /// compaction, on a turn where that source had nothing pending, the set EMPTIES — and
    /// <c>Delivered.Count == 0</c> then said "first contact" about a member of many hours' standing.
    /// Its every report was exempt from the digest from then on, and the archive-gap warning returned
    /// early on exactly the channels compaction had touched, which are the only ones it exists for.
    /// </para>
    /// <para>
    /// <see cref="ITurnCursor.HighWaterIndex"/> is what makes the answer stick: it is only ever raised
    /// (<c>Math.Max</c>), it survives the prune, and zero is not an index a channel hands out — they
    /// are numbered from one. The delivered set stays in the test as the second half, so a channel
    /// whose only delivered entry carried an agent-typed <c>[0]</c> is still not called first contact.
    /// This is not the index deciding DELIVERY, which that field forbids and which
    /// <see cref="PrintTurn_Trigger.Select_Pending"/> still answers from identities alone; it is one
    /// boolean about whether anything ever happened here.
    /// </para>
    /// </summary>
    public static bool Nothing_EverDelivered(ITurnCursor cursor)
    {
        return cursor.HighWaterIndex == 0 && cursor.Delivered.Count == 0;
    }
}

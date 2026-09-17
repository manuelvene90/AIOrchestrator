using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.StatusNotes;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Running.WakeDecision;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.SessionCursors;

/// <summary>
/// WHAT A SESSION HAS ALREADY BEEN HANDED, kept on its state file — the read that persists a cursor
/// the session has never had, and the advance that records a set of entries as delivered.
///
/// <para>
/// BOTH HALVES WERE PRIVATE TO <c>PrintTurnDispatcherModel</c> (<c>Read_Sources</c> and
/// <c>Advance_Cursors</c>), which was right while a state file only ever belonged to a session the
/// dispatcher ran. It does not any more: after the one-wake-model change (2026-09-15) a TERMINAL
/// session keeps the same file for its cursors, and the engine's wake-ticket sweep does the same two
/// pieces of bookkeeping for it. CLAUDE.md decision 12 — never a second copy — so the dispatcher
/// calls this too and there is one implementation of each.
/// </para>
/// <para>
/// THE PERSIST IS NOT OPTIONAL FOR A SESSION WITH NO CURSORS AT ALL, and that is the whole reason
/// this is shared rather than left where it was. <see cref="WakeDecision_Resolver.Read_Pending"/>
/// BASELINES a session it has never seen — everything now in the channel is history — and writes
/// nothing. A caller that merely asks and discards would baseline again on the next tick, over a
/// file that has meanwhile grown, and absorb as history the very entry it was supposed to wake on:
/// permanently deaf, silently.
/// </para>
/// </summary>
public static class SessionCursors_Bookkeeper
{
    static readonly StringComparer SOURCE_KEYS = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Reads every source through <see cref="WakeDecision_Resolver.Read_Pending"/> and PERSISTS the
    /// cursor set when the read made one — see the type header for why that write is load-bearing.
    ///
    /// <para>
    /// THE OBSERVER IS THE CALLER'S EXTRA WORK, handed the cursor and the file DURING the read, while
    /// they are still the pair the pending set was selected against. The dispatcher uses it to log a
    /// baselined channel and to warn about an archive gap; the sweep passes none.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SourceRead> Read_AndPersist(
        string stateFile,
        ref IPrintSessionState state,
        SessionRoles role,
        IReadOnlyList<ITurnSource> sources,
        WakeDecision_Resolver.SourceReadObserver? observe = null)
    {
        List<ITurnCursor> cursors = [];
        var changed = false;

        var reads = WakeDecision_Resolver.Read_Pending(state, role, sources, (source, cursor, entries, cursorIsNew) =>
        {
            if (cursorIsNew)
                changed = true;

            cursors.Add(cursor);
            observe?.Invoke(source, cursor, entries, cursorIsNew);
        });

        // A CURSOR IS NEVER DROPPED FOR A SOURCE THAT MERELY DID NOT RESOLVE THIS TICK. It used to be,
        // to keep the file tidy — and the roster read that decides is best-effort: an absent session.json
        // resolves to the owner channel alone, and rewriting the durable record from that transient
        // answer loses every spoke cursor irreversibly. The next successful read then meets them all as
        // unknown keys and re-delivers whole channels. Keeping a stale key costs one line in a JSON file
        // nobody counts; dropping it costs a supervisor re-answering every member it has.
        foreach (var cursor in state.Cursors)
        {
            // THE STATUS LOG'S CURSOR IS NOT A STALE SOURCE KEY, it is a key that is not a source at
            // all and never will be (StatusNotes_Bookkeeper's header says why it must not become one).
            // Falling through the test above would be harmless today and would become a DUPLICATE the
            // moment Advance re-adds it — two `.status` rows in the cursors array, one of them frozen
            // at whatever it said when the session last had a source-less read, and the resolver's
            // FirstOrDefault picking whichever the writer happened to put first. Kept once, below.
            if (SOURCE_KEYS.Equals(cursor.SourceKey, StatusLog_Store.CURSOR_KEY))
                continue;

            if (!sources.Any(source => SOURCE_KEYS.Equals(source.Key, cursor.SourceKey)))
                cursors.Add(cursor);
        }

        // AND THEN RE-ADDED UNCHANGED, because this read is what PERSISTS the cursor set: skipping it
        // above and not putting it back would drop the log's delivery record on the first read that
        // meets a new source, and every note the session had already been shown would ride again.
        var statusCursor = StatusNotes_Bookkeeper.Find_Cursor_OrNull(state);

        if (statusCursor != null)
            cursors.Add(statusCursor);

        if (changed)
        {
            state = PrintSessionState_Factory.CreateFrom_Existing_Cursors(state, cursors);
            PrintSessionState_Store.Write(stateFile, state);
        }

        return reads;
    }

    /// <summary>
    /// The cursor set that records <paramref name="pending"/> as handed over. Returns it rather than
    /// writing it: the dispatcher folds it into the record of a completed turn, and the wake-ticket
    /// sweep writes it beside the ticket.
    /// </summary>
    public static IReadOnlyList<ITurnCursor> Advance(
        ISupervisionPaths paths,
        IPrintSessionState state,
        IReadOnlyList<ITurnSource> sources,
        IReadOnlyList<PendingEntry> pending,
        IReadOnlySet<string>? answeredSourceKeys = null)
    {
        var byKey = state.Cursors.ToDictionary(cursor => cursor.SourceKey, SOURCE_KEYS);

        List<ITurnCursor> advanced = [];

        foreach (var source in sources)
        {
            if (!byKey.TryGetValue(source.Key, out var cursor))
                continue;

            // THE CLOSING TURN'S EXCEPTION, and only its (see PrintTurnDispatcherModel's
            // Close_Down_KilledTurn_Async). Nothing is dropped here: the cursor is carried over
            // untouched, so the source keeps its history and its entries are pending again on the
            // next tick.
            if (answeredSourceKeys != null && !answeredSourceKeys.Contains(source.Key))
            {
                advanced.Add(cursor);
                continue;
            }

            var delivered = pending.Where(item => SOURCE_KEYS.Equals(item.Source.Key, source.Key)).Select(item => item.Entry).ToList();
            var entries = ChannelHistory_Cache.Read_Entries(source.ChannelFilePath);

            advanced.Add(TurnCursor_Factory.CreateFrom_Delivered(cursor, state.Role, entries, delivered));
        }

        // THE LOG'S CURSOR, ADVANCED WITH THE SAME SET AND IN THE SAME WRITE. A note that rode this
        // turn is delivered exactly as a channel entry is; recorded in a second write it could survive
        // a crash that lost the turn, or the reverse.
        //
        // WHICH OF THE PENDING ENTRIES CAME FROM THE LOG IS ASKED OF THE LOG, not remembered: the
        // notes are labelled under the session's own source (WakeDecision_Resolver.With_AgentNotes) so
        // that the prompt reads right, and re-deriving here is one read of a small file against
        // carrying a flag through five signatures.
        //
        // ONCE THERE IS SOMETHING TO REMEMBER, AND THEN FOR EVER AFTER. Advance's answer REPLACES the
        // session's cursor set, so a tick that omitted an EXISTING `.status` cursor would erase the
        // record of every note already delivered and hand them all over again on the next turn — hence
        // the `!= null` half. The other half is why this is not simply unconditional, which is what the
        // plan asked for: a session whose role has never had its bookkeeping routed (the default, and
        // every session on both of the owner's machines until somebody sets the key) would otherwise
        // grow an EMPTY `.status` row in its state file that records nothing, carries nothing and
        // changes what every cursor-counting reader sees. The row appears when the first note rides.
        var logDigests = StatusNotes_Bookkeeper.Read_Entries(paths, state).Select(ChannelEntry_Digest.Compute).ToHashSet();
        var handedFromLog = pending.Where(item => logDigests.Contains(ChannelEntry_Digest.Compute(item.Entry))).Select(item => item.Entry).ToList();

        if (handedFromLog.Count > 0 || StatusNotes_Bookkeeper.Find_Cursor_OrNull(state) != null)
            advanced.Add(StatusNotes_Bookkeeper.Advance(paths, state, handedFromLog));

        return advanced;
    }
}

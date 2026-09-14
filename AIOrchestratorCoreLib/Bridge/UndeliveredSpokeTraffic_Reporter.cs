using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHAT A CLOSING MEMBER'S SPOKE STILL HAD PENDING, SAID OUT LOUD BEFORE IT STOPS BEING A SOURCE.
///
/// <para>
/// Closing a member is <c>Close_Member</c> plus a kill: no drain, no read of the spoke. From that
/// moment <c>TurnSources_Resolver</c> skips the channel, so an entry the supervisor was never handed
/// is never handed over — and because the cursor is deliberately kept, it is not re-delivered either.
/// Nothing was lost from the FILE, and nothing is re-run; what is lost is that anybody knows.
/// </para>
/// <para>
/// THE EXPOSURE WAS ONE TICK AND IS NOW A DIGEST WINDOW. Before the member digest (2026-09-09) an
/// ordinary report was handed over on the next tick, so the gap between "filed" and "delivered" was
/// seconds; a held report now waits up to <c>printRunner.memberDigestMinutes</c>. The re-review of
/// that change measured the widening and called it unacceptable as shipped, for the right reason: the
/// entry most likely to be in flight when a supervisor closes a member is the member's LAST one —
/// "done, pushed, here is the diff and the suite output" — and the supervisor may then close a ledger
/// line whose evidence it never read.
/// </para>
/// <para>
/// A LINE, NOT A DRAIN. Draining the spoke would mean running one more supervisor turn inside a close,
/// which is the close deciding to spend a turn nobody asked for; and the honest half of the problem is
/// not the delivery, it is the silence. So this says what is being dropped, to the log the app tails —
/// never to Telegram, which decision 15 reserves for what the owner can act on, and this is the
/// supervisor's own decision taking effect. When it cannot answer, it says which read failed rather
/// than inventing a verdict (decision 21).
/// </para>
/// <para>
/// "PENDING" IS NOT A MEASUREMENT WHILE A TURN IS IN FLIGHT — measured in production 2026-09-10, the
/// first time this line ever fired, and it was FALSE. <c>imp-3</c> filed entry [72] ("Done. F1 closed,
/// origin/dev merged, all gates re-run") at 14:06:13; the digest released it at 14:11:15 and the
/// supervisor turn started carrying it; this said at 14:12:43 that the supervisor "was never handed"
/// [72]; that turn ended in success at 14:12:58. The cursor advances beside
/// <c>Advance_Cursors</c> — i.e. when a turn COMPLETES — so for the whole of a turn's run the entries
/// it is delivering still read as pending. So the pending set alone cannot answer this, and the
/// caller passes what the dispatcher alone knows: the identities the in-flight turn is carrying.
/// </para>
/// <para>
/// AND THE ANSWER IS A CONDITIONAL, NOT A SUPPRESSION. An in-flight turn that FAILS leaves its
/// entries pending, and the member is gone by then — so hiding the line whenever a turn is in flight
/// would trade a false alarm for a silent drop. Entries nothing is carrying are a WARNING (they are
/// being left behind, full stop); entries a turn is carrying are an INFO naming the condition, which
/// is what the caller distinguishes with <c>AnythingDropped</c>.
/// </para>
/// <para>
/// BUT NOTHING RESOLVES THAT CONDITIONAL, AND THE COMMIT THAT ADDED IT OVERSOLD THIS (review finding,
/// 2026-09-10). Once <c>Close_Member</c> has run, <c>TurnSources_Resolver</c> skips the member for
/// good: the entries can never be pending again, the archived-undelivered warning only iterates
/// RESOLVED sources, and no later close re-examines them. So if that turn does fail, the INFO line
/// above is the only record that will ever exist — better than a WARNING with a false claim, and not
/// the same thing as the drop being caught. Closing it properly means one warning on the turn's
/// failure path, which already holds the pending set and can ask the store which members are closed;
/// that is left as an OPEN item rather than added untested at the end of a long session, and it is
/// written down in <c>docs/superpowers/specs/2026-09-10-brief-for-a-fresh-agent.md</c>.
/// </para>
/// <para>
/// It lives here rather than in <c>BridgeEngineModel</c> because that file takes no new lines by
/// inertia (`.claude/rules/code-conventions.md`): the engine keeps the one call.
/// </para>
/// </summary>
public static class UndeliveredSpokeTraffic_Reporter
{
    /// <param name="deliveringIdentities">
    /// The entry identities the supervisor's IN-FLIGHT turn is carrying right now, from
    /// <c>IPrintTurnDispatcher.Get_DeliveringIdentities</c> — empty when no turn is running. Passed in
    /// rather than read here so this stays a pure function over the files plus one fact, and so a
    /// caller has to state what it knows: a caller that passed nothing would resurrect the false
    /// alarm of 2026-09-10 silently, which is why there is no default.
    /// </param>
    public static void Log_BeforeClosing(ISupervisionPaths paths, IOrchestrationLog log, string orchId, string memberId, IReadOnlySet<string> deliveringIdentities)
    {
        try
        {
            var describedPending = Describe_Pending_OrNull(paths, orchId, memberId, deliveringIdentities);

            if (describedPending == null)
                return;

            if (describedPending.Value.AnythingDropped)
                log.Log_Warning(orchId, describedPending.Value.Line);
            else
                log.Log_Info(orchId, describedPending.Value.Line);
        }
        catch (Exception ex)
        {
            // The predicate could not be evaluated, so it says so and says nothing else: a close is
            // not stopped by this, and a silence here would be the very thing it exists to remove.
            log.Log_Error(orchId, $"'{memberId}' is being closed and what its spoke still had pending could not be read, so this cannot say whether anything is being dropped", ex);
        }
    }

    /// <summary>
    /// The line, or null when there is nothing to say — no bridge-driven supervisor for this
    /// orchestration (nobody was going to be handed anything), no cursor for this spoke, or an empty
    /// pending set. Split out so a test can read the sentence without a log sink.
    /// </summary>
    public static (string Line, bool AnythingDropped)? Describe_Pending_OrNull(ISupervisionPaths paths, string orchId, string memberId, IReadOnlySet<string> deliveringIdentities)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
        var state = PrintSessionState_Store.Read_OrNull(stateFile);

        var cursor = state?.Cursors.FirstOrDefault(known => string.Equals(known.SourceKey, memberId, StringComparison.OrdinalIgnoreCase));

        if (cursor == null)
            return null;

        var channelFile = paths.Get_ImplementerChannelFile(orchId, memberId);

        if (!File.Exists(channelFile))
            return null;

        var pending = PrintTurn_Trigger.Select_Pending(SessionRoles.Supervisor, ChannelHistory_Cache.Read_Entries(channelFile), cursor);

        if (pending.Count == 0)
            return null;

        // The same identity the cursor is keyed on (ChannelEntry_Digest), never the agent-written
        // [n] — decision 12. The dispatcher computed these from the very entries it launched with.
        var inFlight = pending.Where(entry => deliveringIdentities.Contains(ChannelEntry_Digest.Compute(entry))).ToList();
        var dropped = pending.Where(entry => !deliveringIdentities.Contains(ChannelEntry_Digest.Compute(entry))).ToList();

        if (dropped.Count == 0)
            return ($"'{memberId}' is being closed while a supervisor turn in flight is already carrying {Describe_Count(inFlight.Count)} of its channel — nothing is dropped if that turn completes, and if it fails these stay in the channel file and reach nobody: "
                + Describe_Entries(inFlight), false);

        var line = $"'{memberId}' is being closed with {Describe_Count(dropped.Count)} its supervisor was never handed — the spoke stops being a source, so these stay in its channel file and reach nobody: "
            + Describe_Entries(dropped);

        if (inFlight.Count > 0)
            line += $" — and a supervisor turn in flight is carrying {Describe_Count(inFlight.Count)} more, which arrive only if that turn completes: " + Describe_Entries(inFlight);

        return (line, true);
    }

    static string Describe_Count(int count)
    {
        return $"{count} entr{(count == 1 ? "y" : "ies")}";
    }

    static string Describe_Entries(IEnumerable<IChannelEntry> entries)
    {
        return string.Join("; ", entries.Select(entry => $"[{entry.Index}] {entry.Subject}"));
    }
}

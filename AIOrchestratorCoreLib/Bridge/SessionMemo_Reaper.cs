namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHICH IN-MEMORY MEMOS DESCRIBE A SESSION THAT NO LONGER EXISTS.
///
/// <para>
/// WHY IT EXISTS. The engine keeps per-session memos in plain dictionaries — "what ticket was this
/// session last handed", "have we already reported this" — and a memo that is written on first sight
/// and never removed outlives the thing it is about. It is not a runaway: the key space is the
/// sessions this PROCESS has seen, so a desktop run bounded by an evening never notices. The daemon
/// is the reader that does (<c>deploy/</c>, systemd/launchd): it runs for weeks across many closed
/// orchestrations, and every one of them leaves its memo behind for the life of the process.
/// </para>
/// <para>
/// IT ANSWERS FROM THE LIVE SET, NOT FROM A CLOSE EVENT, and that is the whole design. A memo could
/// instead be dropped where an orchestration is closed — but a close is one door among several ways
/// a session stops existing (a member closed on its own, a state file deleted, a close made by an
/// earlier process and read back from disk at startup), so a reaper hung on that door is correct
/// only for the door it hangs on. The sweeps that own these memos already enumerate every live
/// session on every tick; comparing against that enumeration costs one hash set over a list the
/// caller is holding anyway, is self-healing, and cannot disagree with whatever the store now says
/// is open.
/// </para>
/// <para>
/// NOT EVERY MEMO IN THAT FILE WANTS THIS, and the next caller has to check rather than assume. As
/// of 2026-09-17 <c>BridgeEngineModel</c> holds some thirty collections that are written and never
/// emptied, and they are not one defect thirty times: <c>_topicDeletesTakenOn</c>, for instance,
/// exists precisely to remember something about an orchestration that has ALREADY closed, so
/// reaping it against the live set would delete the memo at the moment it starts mattering. This
/// component answers "which keys name nothing live"; whether that is the right question for a given
/// map is the map's own to answer.
/// </para>
/// <para>
/// IT IS A PURE FUNCTION, deliberately: <c>BridgeEngineModel</c> is <c>internal sealed</c> with no
/// <c>InternalsVisibleTo</c>, so a rule decided inside it is not merely untested but unreachable —
/// and the reaping of a memo has no observable behaviour at all, only a footprint. Decided here, it
/// is checkable.
/// </para>
/// </summary>
public static class SessionMemo_Reaper
{
    /// <summary>
    /// The memo keys that name nothing in <paramref name="liveKeys"/>, as a materialised list so the
    /// caller can remove from the dictionary it took them out of.
    ///
    /// <para>
    /// ORDINAL-IGNORE-CASE, because these keys are FILE PATHS and the engine's own host is Windows:
    /// two spellings of one path are one session, and a reaper that thought otherwise would keep the
    /// memo it was asked to drop — failing silently, in the direction it exists to prevent.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Find_Orphans(IEnumerable<string> memoKeys, IEnumerable<string> liveKeys)
    {
        var live = new HashSet<string>(liveKeys, StringComparer.OrdinalIgnoreCase);

        List<string> orphans = [];

        foreach (var key in memoKeys)
        {
            if (!live.Contains(key))
                orphans.Add(key);
        }

        return orphans;
    }
}

using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Running.PendingTraffic;

/// <summary>
/// HOW LONG THIS PENDING SET WAITS FOR THE REST OF ITSELF BEFORE ITS TURN STARTS.
///
/// <para>
/// The dispatcher normally waits <c>IRunnerConfigs.CoalesceWindow</c> — three seconds — for the
/// pending set to stop changing, so entries still landing ride the same turn instead of buying a
/// second one. For member → supervisor traffic that is worth every second: an extra supervisor wake
/// costs ~1 M input tokens, measured, and a crew filing reports within a second of each other is the
/// normal case, not the edge one.
/// </para>
/// <para>
/// FOR THE OWNER IT IS THREE SECONDS OF A PERSON WAITING (owner decision, 2026-09-09). Measured on
/// the VPS that day: 11–12 s median from their Telegram message to the supervisor's turn starting —
/// aggregation window, then a mirror tick, then this. The owner's ruling was to keep the coalescence
/// for member traffic and take it off theirs.
/// </para>
/// <para>
/// IT WAS A WAIVER FOR ONE EVENING, AND THE WAIVER COST A TURN. "Skips the WAIT, never narrows the
/// turn" was the claim, and the second half was false: a turn that has already STARTED cannot take
/// anything else, so a member report landing a breath after the owner's message finds
/// <c>_inFlight</c>, waits out that turn and buys its own. Driven through the real dispatcher: owner
/// entry then a member entry one second later gave TWO turns, where member-then-member a second apart
/// gave one. The exposure was the whole coalesce window after every owner message — an extra
/// supervisor wake is ~1 M input tokens, measured, against three seconds of a person waiting.
/// </para>
/// <para>
/// SO IT IS A SHORTER WINDOW RATHER THAN NO WINDOW. <see cref="OWNER_BREATH_MILLISECONDS"/> is long
/// enough that what was already on its way rides along, and short enough that it disappears into the
/// path the owner was complaining about. It is CAPPED by the configured window, so a deployment that
/// coalesces faster than a breath is never slowed down by the rule that exists to speed it up.
/// </para>
/// <para>
/// BOTH HALVES ARE REQUIRED — the OWNER wrote it, and they wrote it on an OWNER CHANNEL — and neither
/// is redundant. A solo writes its own entries into <c>owner-channel.md</c>
/// (<see cref="TurnSource.TurnSources_Resolver.Resolve_Own"/>), so the channel alone is not evidence
/// that a person is waiting; and the owner can type straight into a member's spoke, which is not the
/// path the 11–12 s was measured on — nothing was mirrored and no aggregation window was served.
/// Requiring the channel is also what confines the waiver to the roles that HAVE one (supervisor,
/// solo, general): a member's sources are spokes, so its traffic keeps the window with no role test
/// anywhere.
/// </para>
/// <para>
/// A POLICY RATHER THAN AN <c>if</c> because the dispatcher's decision has one line per reason and
/// this reason is the owner's, argued from a measurement — it belongs where it can be read and tested
/// without a state file, a clock or a session.
/// </para>
/// </summary>
public static class CoalesceWindow_Policy
{
    /// <summary>
    /// What a pending set containing the owner's own message waits for the rest of itself, instead of
    /// the configured window.
    ///
    /// <para>
    /// A SECOND, because that is roughly how long it takes the dispatcher to SEE something written
    /// beside the owner's message: it ticks inside the mirror loop, which runs on a 2 s ceiling and
    /// several times a second while a burst is arriving. Anything shorter is the waiver again under
    /// another name; anything longer starts giving the owner back the wait this whole change removed.
    /// </para>
    /// <para>
    /// It does NOT make the pair-that-arrives-slowly free: two entries further apart than this still
    /// buy two turns, exactly as the configured window says they may. What it removes is the case the
    /// waiver created, where they bought two turns however close together they landed.
    /// </para>
    /// </summary>
    public const int OWNER_BREATH_MILLISECONDS = 1000;

    /// <summary>
    /// The window this pending set must be unchanged for. Never longer than
    /// <paramref name="configuredWindow"/> — this rule only ever shortens.
    /// </summary>
    public static TimeSpan Resolve_Window(IReadOnlyList<PendingEntry> pending, TimeSpan configuredWindow)
    {
        if (!Contains_OwnerTraffic(pending))
            return configuredWindow;

        var breath = TimeSpan.FromMilliseconds(OWNER_BREATH_MILLISECONDS);

        return breath < configuredWindow ? breath : configuredWindow;
    }

    static bool Contains_OwnerTraffic(IReadOnlyList<PendingEntry> pending)
    {
        foreach (var item in pending)
        {
            if (item.Source.IsOwnerChannel && item.Entry.Author == ChannelAuthors.Owner)
                return true;
        }

        return false;
    }
}

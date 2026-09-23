using AIOrchestratorCoreLib.Configuration.PhoneSettings;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Bridge.PeriodicStatus;

/// <summary>
/// THE TWO UNPROMPTED STATUS POSTS, EACH ON ITS OWN SLOT AND EACH CHANGE-GATED.
///
/// <para>
/// HISTORY, because the method this replaces was named for a feature that no longer existed. Master's
/// <c>Push_PeriodicStatus_Async</c> posted a fifteen-line STATUS every half hour and, while the owner
/// was away, a three-line digest instead. The fork deleted the status on 2026-09-09 (ten messages in
/// five and a half hours in one topic, three IDENTICAL at 19:00, 19:30 and 20:00 with every member
/// closed), leaving <c>Push_AwayDigests_Async</c> with a bare <c>continue</c> where it had been. The
/// owner, 2026-09-23: *"he removed the status message, but I liked it, so we should be able to opt
/// in."* Plan 03 Task 8 brings it back under <c>phone.status.periodic</c> (answer D1 (a)), and moves the
/// sweep out of <c>BridgeEngineModel</c> because it grew its second branch again.
/// </para>
/// <para>
/// TWO SLOTS, NOT ONE (answer D9). The periodic status runs on <c>phone.status.intervalMinutes</c>; the
/// away digest stays on <see cref="PeriodicStatusSlot_Planner.SLOT_MINUTES"/>. They shared one slot
/// store when both were 30 minutes, and <see cref="AwayDigest_Decider"/> records a limit cycle locked
/// to exactly that number — so the away cadence is not something a phone preference may move. Each
/// has its own record of the last slot it spent, so changing one can never shift the other's phase.
/// </para>
/// <para>
/// WHY THE STATUS DOES NOT FEED ITS OWN WAKES — the hazard this task was warned about. Both posts go
/// through <see cref="IPeriodicStatusHost.Post_StatusEntry"/>: an APP entry appended to the
/// orchestration's OWNER channel (subject STATUS, audience Owner), which the mirror then delivers —
/// never straight to Telegram, so Normal mirrors it, Deferred keeps only the newest, Silenced drops it.
/// A bridge-driven session (print or stream runner) is never woken by it at all:
/// <c>PrintTurn_Trigger.Is_Inbound</c> excludes <c>ChannelAuthors.App</c> for every role. A TERMINAL
/// session is: its watcher fires on any foreign change to <c>owner-channel.md</c>. That is exactly
/// the 2026-08-18/19 away-digest loop (append → wake → STANDING BY → next slot → append …), and what
/// breaks it here is the same thing that broke it there — a post goes out ONLY WHEN IT SAYS SOMETHING
/// THE LAST ONE DID NOT (ruling R6). The status is compared WITHOUT the readings the wake itself moves
/// — the context figure AND the working state ("working now — editing X" against "idle — waiting")
/// of the supervisor and of a solo, the two sessions that read the owner channel — and without the
/// ones time moves ("last wrote N min ago"); see <see cref="IPeriodicStatusHost.Build_MemberStatus"/>.
/// The working state was left in by the first cut and a review caught it (2026-09-23): a wake whose
/// turn outlasts the interval — easy at the 5-minute minimum — made the next status differ, post, and
/// wake again.
/// </para>
/// <para>
/// WHAT THAT GUARANTEES, and no more: nothing the WAKE does can make the next status post. A wake
/// that leads to real work — a ledger line ticked, a member briefed, a question asked — changes the
/// status, and the next one reports it; that is the status doing its job, one wake per real change,
/// and zero on an orchestration where nothing moves. Members are never reached: the status is written
/// to the owner channel only, and a member's watcher reads its own channel.
/// </para>
/// </summary>
internal sealed class PeriodicStatusSweepModel : IPeriodicStatusSweep
{
    /// <summary>
    /// The sweep runs on the mirror tick; <see cref="Forget_AwayDigests"/> arrives from the inbound
    /// loop when the owner speaks. One lock for every store, held only around a read or a write.
    /// </summary>
    readonly object _lock = new();

    /// <summary>The last AWAY slot each orchestration spent — LOCAL, like everything the planner handles.</summary>
    readonly Dictionary<string, DateTime> _lastAwaySlotByOrchId = [];

    /// <summary>The last STATUS slot each orchestration spent, on the interval in force when it was spent.</summary>
    readonly Dictionary<string, DateTime> _lastStatusSlotByOrchId = [];

    /// <summary>The away digest last WRITTEN, so an identical one is never sent again. Dropped when a spell ends.</summary>
    readonly Dictionary<string, string> _lastAwayDigestByOrchId = [];

    /// <summary>The comparison form of the status last WRITTEN — never the posted text, which moves on every read.</summary>
    readonly Dictionary<string, string> _lastStatusKeyByOrchId = [];

    /// <summary>
    /// The ledger figures the owner was last TOLD, for the "17(+1)/30(+3)" deltas. A snapshot of a
    /// message, not of the file — see <see cref="PlanProgressSnapshot"/>.
    /// </summary>
    readonly Dictionary<string, PlanProgressSnapshot> _lastPostedProgressByOrchId = [];

    public async Task Push_Async(
        DateTime now,
        bool isAway,
        IPhoneSettings phone,
        IReadOnlyList<IOrchestrationSession> sessions,
        IPeriodicStatusHost host,
        CancellationToken cancellationToken)
    {
        foreach (var session in sessions)
        {
            if (session.ClosedUtc != null || session.TelegramTopicId == null)
                continue;

            // PAUSED: skipped before either stamp for the same reason as a meeting — stamping here
            // would restart the clock on every tick of a pause that may last days, so the first post
            // after they lift it would be a whole period late. CLAUDE.md's PAUSE bullet names this
            // sweep as a waker that must be gated here, not only at the choke point.
            if (session.Paused)
                continue;

            // MEETING: skipped BEFORE the stamps, deliberately. Stamping here would restart the clock
            // on every tick of the meeting, so a boundary the owner comes back to inside its grace
            // would already be spent.
            if (OwnerPresence_Policy.Suppresses_SupervisorAttention(session.OwnerPresence))
                continue;

            // THE AWAY SLOT IS KEPT ON EVERY TICK, away or not — as it always was when one store
            // served both — so the first digest of a spell lands on the next half hour rather than a
            // full period after the owner left.
            var awayPlan = PeriodicStatusSlot_Planner.Decide(
                now, Find_Slot_OrNull(_lastAwaySlotByOrchId, session.OrchId), PeriodicStatusSlot_Planner.SLOT_MINUTES);

            if (awayPlan.Action != PeriodicStatusSlotActions.Skip)
                Record(_lastAwaySlotByOrchId, session.OrchId, awayPlan.SlotStart);

            // Away mode: the owner cannot reply, so the digest is their ONLY window into the
            // orchestration — it replaces the status, never rides beside it. It goes out whether or
            // not work is in flight, because "imp-1 is blocked waiting for you" is exactly what they
            // need to know. An Adopt sends nothing: first sight, including every orchestration after
            // a restart, must not push every topic at once, off-boundary.
            if (isAway)
            {
                if (awayPlan.Action == PeriodicStatusSlotActions.Push)
                    await Push_AwayDigest_Async(session, host, cancellationToken);

                continue;
            }

            // THE SETTING, READ ON THIS TICK. Off means the status slot is not even consulted, so a
            // machine under quiet spends no channel read on a status it will never send.
            if (!phone.PeriodicStatus)
                continue;

            var statusPlan = PeriodicStatusSlot_Planner.Decide(
                now, Find_Slot_OrNull(_lastStatusSlotByOrchId, session.OrchId), phone.PeriodicStatusIntervalMinutes);

            if (statusPlan.Action == PeriodicStatusSlotActions.Skip)
                continue;

            // Spent whatever happens below: of the paths after this, only one writes, and recording
            // once here is why none of them can fire twice in a slot.
            Record(_lastStatusSlotByOrchId, session.OrchId, statusPlan.SlotStart);

            // FIRST SIGHT SENDS NOTHING AND REMEMBERS WHAT IT WOULD HAVE SAID. First sight is every
            // orchestration after an app restart (these stores live in process), and with an empty key
            // store the first boundary after a restart always read as changed — one status per topic
            // per restart, saying what the owner was last told (review of 8d548f0, 2026-09-23). Seeding
            // the key here makes the restart invisible: the next boundary posts only if something
            // moved. The BASELINE is not seeded — the deltas are "since the owner was last told", and
            // after a restart that is unknown, so the first status shows none rather than invent one.
            if (statusPlan.Action == PeriodicStatusSlotActions.Adopt)
            {
                Record(_lastStatusKeyByOrchId, session.OrchId, Build_StatusKey(session, host, host.Read_PlanProgress_OrNull(session.OrchId)?.CurrentTaskText));
                continue;
            }

            await Push_Status_Async(session, host, phone.PeriodicStatusIntervalMinutes, cancellationToken);
        }
    }

    public void Forget_AwayDigests()
    {
        lock (_lock)
            _lastAwayDigestByOrchId.Clear();
    }

    /// <summary>
    /// ONLY WHEN SOMETHING CHANGED (owner's call, 2026-08-19) — see <see cref="AwayDigest_Decider"/>
    /// for the limit cycle an unchanged digest drove all night.
    /// </summary>
    async Task Push_AwayDigest_Async(IOrchestrationSession session, IPeriodicStatusHost host, CancellationToken cancellationToken)
    {
        var digest = host.Build_AwayDigest(session);

        if (!AwayDigest_Decider.Should_Send(Find_Text_OrNull(_lastAwayDigestByOrchId, session.OrchId), digest))
            return;

        // REMEMBERED ONLY ON A CONFIRMED WRITE. Recording it first would let a channel that stayed
        // locked for the whole budget count as a delivery, and because an unchanged digest is never
        // re-sent, that away spell would go silent entirely.
        //
        // THE PICTURE IS APPENDED AFTER THE DECISION AND IS NOT REMEMBERED WITH IT. A fresh timestamped
        // IMAGE: path differs on every single pass, so folding it into either side would make every
        // digest look changed and restart the exact limit cycle the rule above exists to break.
        var text = digest + await host.Build_ScreenshotMarker_OrEmpty_Async(session, cancellationToken);

        if (host.Post_StatusEntry(session.OrchId, text, session.OwnerPresence))
            Record(_lastAwayDigestByOrchId, session.OrchId, digest);
    }

    /// <summary>
    /// MASTER'S STATUS, with one thing master did not have: it is not posted when it would say what
    /// the last one said (ruling R6). The cadence that got it deleted was never its content — it was
    /// that it posted unconditionally, so a topic nobody was working in got the same fifteen lines
    /// every half hour, and every one of them was an append a terminal supervisor's watcher woke on.
    /// </summary>
    async Task Push_Status_Async(IOrchestrationSession session, IPeriodicStatusHost host, int intervalMinutes, CancellationToken cancellationToken)
    {
        var progress = host.Read_PlanProgress_OrNull(session.OrchId);

        // Nothing running: the rule was to stop the cadence, not to report "no change" forever. The
        // slot is spent already, so work starting mid-slot waits for the next boundary like everyone
        // else rather than firing on its own schedule.
        if (!Has_WorkInFlight(session, progress, host, intervalMinutes))
            return;

        var currentTask = progress?.CurrentTaskText;

        var key = Build_StatusKey(session, host, currentTask);

        if (!AwayDigest_Decider.Should_Send(Find_Text_OrNull(_lastStatusKeyByOrchId, session.OrchId), key))
            return;

        var previous = Find_Progress_OrNull(session.OrchId);

        // THE PICTURE RIDES THE ENTRY, as an IMAGE: line, exactly as it did in master: the mirror turns
        // it into a photo and strips the line. Outside the key, for the digest's reason.
        var text = PeriodicStatus_Builder.Build(host.Build_MemberStatus(session, previous, withVolatileReadings: true), currentTask)
            + await host.Build_ScreenshotMarker_OrEmpty_Async(session, cancellationToken);

        if (!host.Post_StatusEntry(session.OrchId, text, session.OwnerPresence))
            return;

        Record(_lastStatusKeyByOrchId, session.OrchId, key);

        // RE-READ rather than reusing the read above: the baseline must be what the message that just
        // went out said, and the roster read the ledger itself. A figure nobody was shown would make
        // the next delta a lie. Master's Remember_PostedProgress, verbatim in intent.
        var posted = host.Read_PlanProgress_OrNull(session.OrchId);

        if (posted != null)
            Record(_lastPostedProgressByOrchId, session.OrchId, new PlanProgressSnapshot(posted.Done, posted.Total));
    }

    /// <summary>
    /// THE COMPARISON FORM: no deltas (a "(+2)" against the last message would make the very next
    /// unchanged status look different from the one that carried it) and none of the readings that move
    /// by themselves. Built by the same builder as the posted text — a second formatter for the key
    /// would drift from the first (CLAUDE.md decision 12), and the away digest's decider compares as
    /// text for the same reason. One method, so the seed on first sight and the check at a boundary
    /// cannot build two different keys.
    /// </summary>
    static string Build_StatusKey(IOrchestrationSession session, IPeriodicStatusHost host, string? currentTask)
    {
        return PeriodicStatus_Builder.Build(host.Build_MemberStatus(session, previous: null, withVolatileReadings: false), currentTask);
    }

    /// <summary>
    /// "Work in flight" without asking anyone: the ledger has a <c>[&gt;]</c> line, or any session of the
    /// orchestration worked within the interval. Master's <c>Has_WorkInFlight</c>, with the window
    /// taken from the setting rather than the half hour it used to be.
    ///
    /// <para>
    /// IT DOES NOT DEPEND ON THE LEDGER BEING MAINTAINED, and that was a real silence. On 2026-08-20
    /// <c>Tear-off tabs</c> went five hours without a status while its solo worked the whole time: its
    /// ledger read 8 done, 3 open and nothing <c>[&gt;]</c>. "Has anyone worked LATELY" is the question
    /// that catches it, and the window is the cadence itself: worked at any point since the last slot
    /// IS work in flight for this one.
    /// </para>
    /// </summary>
    static bool Has_WorkInFlight(IOrchestrationSession session, IPlanProgress? progress, IPeriodicStatusHost host, int intervalMinutes)
    {
        if (progress != null && progress.InProgress > 0)
            return true;

        return host.Has_AnySessionWorkedWithin(session, intervalMinutes);
    }

    DateTime? Find_Slot_OrNull(Dictionary<string, DateTime> store, string orchId)
    {
        lock (_lock)
            return store.TryGetValue(orchId, out var slot) ? slot : null;
    }

    string? Find_Text_OrNull(Dictionary<string, string> store, string orchId)
    {
        lock (_lock)
            return store.TryGetValue(orchId, out var text) ? text : null;
    }

    PlanProgressSnapshot? Find_Progress_OrNull(string orchId)
    {
        lock (_lock)
            return _lastPostedProgressByOrchId.TryGetValue(orchId, out var snapshot) ? snapshot : null;
    }

    void Record<TValue>(Dictionary<string, TValue> store, string orchId, TValue value)
    {
        lock (_lock)
            store[orchId] = value;
    }
}

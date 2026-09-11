using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.Running.PendingTraffic;

/// <summary>
/// WHETHER THIS PENDING SET IS WORTH A TURN AT ALL — the question <see cref="CoalesceWindow_Policy"/>
/// does not ask. That one decides how long a set waits for the rest of itself; this one decides
/// whether the set starts a turn now or is held for the digest.
///
/// <para>
/// MEASURED ON THE VPS, 6–9 Sep 2026: an orchestration supervisor was woken ~400 times in the window,
/// 247 of them by MEMBER traffic, at 2.5 model calls and a mean 398 k of context per call — one
/// wake-up is on the order of 1 M input tokens, and 15 % of those turns produced less than 600 output
/// tokens (an acknowledgement). The supervisor is the most expensive role in the system and the last
/// one still resuming a transcript, so cutting the NUMBER of its wake-ups is the one large saving
/// available without touching its memory. Spec §C4 of
/// <c>docs/superpowers/specs/2026-09-08-token-efficiency-design.md</c>.
/// </para>
/// <para>
/// THE RULE IN THREE LINES. The owner wakes it now. A member's ordinary report waits for the digest —
/// at most <c>IRunnerConfigs.MemberDigestWindow</c>, so several members' reports ride ONE turn instead
/// of buying one each. The app's own bookkeeping never wakes it and cannot reach this at all
/// (<see cref="PrintTurn_Trigger.Is_Inbound"/> excludes <see cref="ChannelAuthors.App"/>, so a
/// <c>turn_ended</c>, a <c>STATUS</c>, a ledger advisory or an orphan notice is never pending for
/// anybody) — measured: zero turns in the whole window were triggered by app entries alone.
/// </para>
/// <para>
/// NOTHING IS LOST BY BEING HELD, and that is what makes the digest cheap rather than risky. A turn
/// takes EVERY pending entry of every channel, so a held member report rides the very next turn
/// whatever starts it — the owner's next message included. The owner therefore never waits for a
/// digest, and a member's answer to the owner cannot be delayed by one: the entry that would carry it
/// is on the same turn as the owner traffic that asked for it.
/// </para>
/// <para>
/// A MEMBER THAT IS BLOCKED IS NOT HELD. <see cref="ESCALATION_MARKERS"/> is the vocabulary a member
/// has for "I cannot go on without you", and an entry carrying one starts a turn at once. The markers
/// were read out of the kit rather than invented, and two of the three the brief proposed do not
/// exist in this direction — see that field.
/// </para>
/// <para>
/// AND A MEMBER'S FIRST ENTRY IS NOT HELD EITHER — a review finding of 2026-09-09 rather than part of
/// the original design. That entry is the boot greeting of a member created seconds ago, so holding it
/// made every <c>add-implementer</c> cost up to one window of dead time before the new member could be
/// briefed; a member that cannot be briefed is a member doing nothing, which is the opposite of a
/// saving. See <see cref="Is_Digestable"/>.
/// </para>
/// <para>
/// A POLICY AND NOT AN <c>if</c> IN THE DISPATCHER, for the reason <see cref="CoalesceWindow_Policy"/>
/// gives: the decision has one line per reason, the reasons are arguable from measurements, and they
/// have to be testable without a clock, a state file or a session.
/// </para>
/// </summary>
public static class WakeUp_Policy
{
    /// <summary>
    /// WHAT A MEMBER WRITES WHEN IT CANNOT GO ON, and therefore what is never digested. Both entries
    /// are CONSTANTS OWNED ELSEWHERE rather than spellings of their own (CLAUDE.md decision 12) —
    /// verified in the kit and in the repo on 2026-09-09 rather than assumed:
    ///
    /// <para>
    /// <see cref="MemberState_Resolver.BLOCKED_ON_OWNER_MARKER"/> is real and is a MEMBER's marker —
    /// <c>kit/skills/implementer/SKILL.md</c> ("Blocked on the owner? … phrase it as
    /// <c>BLOCKED ON OWNER</c> plus the question and options"), and that field is where the word lives.
    /// A member writing it has stopped and a PERSON is at the end of the chain, so five minutes of
    /// digest would be five minutes of two sessions waiting.
    /// </para>
    /// <para>
    /// <see cref="MemberState_Resolver.QUESTION_MARKER"/> is the SUPERVISOR's vocabulary for asking the
    /// OWNER (<c>kit/skills/supervisor/SKILL.md</c>, and that field is the repo's copy of it); the
    /// member skills never teach it, because a member's only interlocutor is its supervisor. It is
    /// honoured anyway — if a member does write it, the entry is a question and waking is both the
    /// cheap and the safe direction.
    /// </para>
    /// <para>
    /// REVIEW FINDING, 2026-09-09: this was a FOURTH hard-coded spelling of that word, and it was the
    /// bare <c>QUESTION</c> without the colon the other three carry. The subject half of
    /// <see cref="MemberState_Resolver.Contains_Marker"/> matches a whole token ANYWHERE and case
    /// insensitively, so <c>REPORT — the open question about the parser is settled</c> defeated the
    /// hold, and so did a body bullet mentioning "the question of the retry order". Members discuss
    /// this vocabulary constantly, so the bare word put the digest's cost back at one wake per report —
    /// silently, and in the direction that looks like it is working. The colon is what makes the marker
    /// a DECLARATION rather than a word, and taking the constant is what stops the next copy.
    /// </para>
    /// <para>
    /// BOTH CONSTANTS COME FROM <see cref="MemberState_Resolver"/>, which is where the channel
    /// vocabulary lives and which both this layer and <c>Bridge</c> already depend on. An earlier
    /// attempt took the word from <c>Bridge.OwnerPush_Policy</c> instead — a reference pointing back
    /// up the way it came, since <c>BridgeEngineModel</c> is constructed with an
    /// <see cref="PrintTurnDispatcher.IPrintTurnDispatcher"/> — and this paragraph then said the move
    /// to its right home was "one line in a file outside this stage's set, so it is reported rather
    /// than done". It WAS done, in the same commit; the paragraph outlived the fact by a day and sent
    /// the next reader to <c>Bridge</c> for a constant that is not there (review finding, 2026-09-10).
    /// </para>
    /// <para>
    /// TWO MARKERS THE BRIEF NAMED ARE NOT HERE, and their absence is a finding rather than an
    /// omission. The <c>turn stalled</c> subject is written by the APP
    /// (<see cref="PrintTurn_Words.TURN_STALLED_SUBJECT"/>, author <see cref="ChannelAuthors.App"/>),
    /// so it is not inbound for any role and can never reach this method. And <c>WAITING ON</c> is not
    /// protocol vocabulary at all: the real marker in that area is
    /// <see cref="MemberState_Resolver.STANDING_BY_MARKER"/>, which declares the OPPOSITE — nothing
    /// owed, nothing running — and is the best digest candidate there is.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ESCALATION_MARKERS =
    [
        MemberState_Resolver.BLOCKED_ON_OWNER_MARKER,
        MemberState_Resolver.QUESTION_MARKER,
    ];

    /// <summary>
    /// Whether anything in this set is a member entry the digest may hold. The dispatcher asks it to
    /// decide when to START its hold clock, which is why it is a separate question from
    /// <see cref="Resolve_WakeReason_OrNull"/>: the clock must be set on the tick the FIRST such entry
    /// appears and left alone afterwards, or a crew filing a report every minute would push its own
    /// deadline out for ever.
    /// </summary>
    /// <param name="sourcesNeverDeliveredFrom">See the same parameter on <see cref="Resolve_WakeReason_OrNull"/>.</param>
    public static bool Contains_DigestableTraffic(IReadOnlyList<PendingEntry> pending, IReadOnlyCollection<string> sourcesNeverDeliveredFrom)
    {
        foreach (var item in pending)
        {
            if (Is_Digestable(item, sourcesNeverDeliveredFrom))
                return true;
        }

        return false;
    }

    /// <summary>
    /// WHY THIS SET IS WORTH A TURN NOW, or null to hold it for the digest. A reason rather than a
    /// bool because it is logged: a supervisor turn that did not happen is invisible, and the one
    /// line saying which rule held it is what makes the digest auditable instead of a shrug.
    /// </summary>
    /// <param name="sourcesNeverDeliveredFrom">
    /// The source keys this session has never been handed a single entry from — the first-entry rule
    /// of <see cref="Is_Digestable"/>. The caller passes a case-insensitive set, the comparer its
    /// cursor set uses, because a session addresses a channel by a word an agent typed.
    /// </param>
    /// <param name="digestHeldSince">
    /// When member traffic first became pending for this session — the dispatcher's own stamp, not a
    /// header's (CLAUDE.md decision 12: a channel header's date is agent-written and untrusted).
    /// NULL means nothing recorded a hold, and this then DELIVERS.
    ///
    /// <para>
    /// THAT NULL IS THE RESTART CASE, AND DELIVERING ON IT IS THE DECISION. The stamp lives in the
    /// dispatcher's tracker, which is per-process, so traffic already sitting in the channels when a
    /// dispatcher starts has no hold on record — it has already waited an unknown time, and the honest
    /// answer is to hand it over. The dispatcher therefore writes NO stamp until a turn of that
    /// session's has COMPLETED under it
    /// (<c>PrintTurnDispatcherModel.SessionTracker.HasDeliveredTraffic</c> — it was
    /// <c>HasStartedATurn</c> for one commit, and having a merely STARTED turn arm the hold is the
    /// regression of 2026-09-10: a failed turn armed it while consuming nothing). REVIEW
    /// FINDING, 2026-09-09: the first tick after a restart used to stamp <c>nowLocal</c> instead, so a
    /// restart RESTARTED the window on traffic that had already waited — probed, a report filed at T+1
    /// on a five-minute window went out at T+11, two full windows, once per restart, with 21 daemon
    /// restarts measured in 44 hours of VPS uptime. A restart now costs one EARLY delivery, which is
    /// the safe direction and what this comment always claimed it cost.
    /// </para>
    /// </param>
    public static string? Resolve_WakeReason_OrNull(
        IReadOnlyList<PendingEntry> pending,
        IReadOnlyCollection<string> sourcesNeverDeliveredFrom,
        DateTime? digestHeldSince,
        DateTime nowLocal,
        TimeSpan digestWindow)
    {
        // NOTHING PENDING REACHES HERE ONLY AS THE BOOT TURN — the dispatcher returns before this for
        // every other empty set (PrintTurnDispatcherModel.Needs_BootTurn). The greeting it produces is
        // what creates the orchestration's Telegram topic, so holding it would hold the owner's own
        // way in.
        if (pending.Count == 0)
            return "boot turn";

        foreach (var item in pending)
        {
            if (item.Entry.Author == ChannelAuthors.Owner)
                return $"the owner wrote in '{item.Source.Key}'";
        }

        foreach (var item in pending)
        {
            var marker = Find_EscalationMarker_OrNull(item);

            if (marker != null)
                return $"'{item.Source.Key}' wrote {marker}";
        }

        // ANYTHING THAT IS NOT A MEMBER'S ORDINARY ENTRY WAKES AS IT ALWAYS DID. This is what confines
        // the digest to the supervisor without a role test anywhere: a member's inbound authors are its
        // supervisor and the owner (PrintTurn_Trigger.Is_Inbound), so a brief or a verdict is never
        // digestable and every other role's timing is untouched. A member's FIRST entry is in this
        // class too, for the reason Is_Digestable gives.
        //
        // AND ONE SUCH ENTRY RELEASES THE WHOLE SET — a TURN TAKES EVERYTHING PENDING, so the reports
        // being held beside it go early. Measured and disclosed on 2026-09-10 rather than fixed: it is
        // not the exemption leaking, it is the queue. This session's queue IS the channels, one turn
        // takes every pending inbound entry of every source, and that property is what makes holding
        // safe in the first place ("nothing is lost by being held", above). Releasing one entry and
        // not its neighbours would mean starting a turn on a SUBSET — two turns where the design
        // promises one, and one request id covering entries the next turn must take again. What it
        // costs is the size of the saving when an add-implementer lands inside a window, and the
        // direction is early rather than late.
        foreach (var item in pending)
        {
            if (!Is_Digestable(item, sourcesNeverDeliveredFrom))
                return $"'{item.Source.Key}' carries traffic the digest does not hold";
        }

        if (digestWindow <= TimeSpan.Zero)
            return "the member digest is off";

        if (digestHeldSince == null)
            return "no hold was recorded for this traffic";

        var held = nowLocal - digestHeldSince.Value;

        // A HOLD THAT READS AS NEGATIVE IS A CLOCK THAT MOVED, NEVER A REASON TO WAIT LONGER. Same
        // ruling as Describe_SinceStamp_OrNull's (CLAUDE.md decision 12): a duration that cannot be
        // believed must not turn into a confident wrong wait, and here the honest direction is to
        // deliver rather than to hold traffic until the clock catches up.
        if (held < TimeSpan.Zero)
            return "the hold stamp is in the future, so the digest cannot be timed";

        // THE REPO'S ONE DURATION FORMATTER, NOT A SECOND ONE. This file cites decision 12 twice and
        // then carried its own `Describe_Window` anyway, which a review said plainly on 2026-09-09 —
        // the same class of drift as the duplicated duration wording that once printed "on task under
        // a minute" for a member that had been working for hours. What the private copy bought was
        // sub-minute precision, and it is worth nothing here: the window is configured in MINUTES and
        // refused above five (RunnerConfigs.RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW), so "under
        // a minute" can only ever describe a test's window or a hold that has barely started — and the
        // shared formatter's negative guard is the behaviour this needs anyway.
        return held >= digestWindow
            ? $"the {SessionDuration_Formatter.Describe(digestWindow)} member digest elapsed ({SessionDuration_Formatter.Describe(held)} held)"
            : null;
    }

    /// <summary>
    /// An entry the digest may hold: written by a MEMBER — <see cref="ChannelAuthor_Kinds.Is_Member"/>,
    /// so reviewers and a solo count exactly as implementers do — carrying none of
    /// <see cref="ESCALATION_MARKERS"/>, and NOT arriving from a channel this session has never been
    /// handed anything from.
    ///
    /// <para>
    /// THE FIRST ENTRY OF A CHANNEL IS A NEW MEMBER SAYING HELLO. A spoke appears when a member is
    /// created, and <c>PrintTurnDispatcherModel.Read_Sources</c> deliberately starts its cursor EMPTY
    /// instead of baselining it, with a comment saying why: the boot greeting lands between the spawn
    /// and the next tick and is the one entry that rule can ever see. Holding it made
    /// <c>add-implementer</c> cost up to a whole window before the new member could be briefed — dead
    /// time for a session that exists to work, bought to save a wake-up the supervisor is about to
    /// spend anyway, since it has to brief the member it just asked for. REVIEW FINDING, 2026-09-09;
    /// probed red on 2026-09-10.
    /// </para>
    /// <para>
    /// ASKED OF THE CURSOR AND NEVER OF THE ENTRY'S <c>[n]</c>. Decision 12 is explicit that a header's
    /// index is agent-written and untrusted — <c>option-lab-2</c> carried two <c>[80]</c> entries on
    /// 2026-08-10 — so "is this entry number one" is not a question the header can answer. "Has this
    /// session ever been handed anything from this channel" is answered by the durable cursor, which is
    /// the same record that decides delivery.
    /// </para>
    /// <para>
    /// AND ONLY THE FIRST. The exemption ends the moment the channel has handed something over, so a
    /// member's second entry is an ordinary report and is held like anybody's — otherwise "a young
    /// channel" would quietly grow into "every channel".
    /// </para>
    /// </summary>
    static bool Is_Digestable(PendingEntry item, IReadOnlyCollection<string> sourcesNeverDeliveredFrom)
    {
        if (sourcesNeverDeliveredFrom.Contains(item.Source.Key))
            return false;

        return ChannelAuthor_Kinds.Is_Member(item.Entry.Author) && Find_EscalationMarker_OrNull(item) == null;
    }

    /// <summary>
    /// The escalation marker this entry declares, or null.
    ///
    /// <para>
    /// THE MATCH IS <see cref="MemberState_Resolver.Contains_Marker"/>'S AND IS NOT REWRITTEN HERE.
    /// Every rule in it — the whole-token test, decoration stripped by category so "🚩 BLOCKED ON
    /// OWNER" counts, quotation excluded so a brief QUOTING the marker does not declare it, subject
    /// anywhere but body only at the start of a line — was paid for by a live failure, and that method
    /// says in as many words that a second matcher written for a new marker starts out missing all of
    /// them. This is its third consumer.
    /// </para>
    /// </summary>
    static string? Find_EscalationMarker_OrNull(PendingEntry item)
    {
        foreach (var marker in ESCALATION_MARKERS)
        {
            if (MemberState_Resolver.Contains_Marker(item.Entry, marker))
                return marker;
        }

        return null;
    }
}

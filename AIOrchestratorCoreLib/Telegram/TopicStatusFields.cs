using System.Globalization;
using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// One thing the owner is waiting on, WITH ITS SOURCE — the owner's own wording for PULSE's first
/// field, 2026-09-09: *"⏳ waiting on you · &lt;what, with its source&gt;"*.
///
/// <para>
/// THE SOURCE IS NOT DECORATION. `arb portfolio UX` showed a blocked count the owner read as
/// something waiting on THEM when it was waiting on a reviewer (2026-08-19), and the ruling that
/// came out of it was that whoever knows says which. An item that cannot name where it came from
/// cannot be acted on: "your browser pass" is a job, "sup: your browser pass" is a job with a
/// door to knock on.
/// </para>
/// <para>
/// <paramref name="Since"/> is nullable because half the sources genuinely do not have one — a
/// ledger line carries no timestamp anywhere in this codebase. A null one prints no "(since …)"
/// rather than a plausible number, which is CLAUDE.md decision 12 applied to a second field: a
/// stamp is only ever printed when it was read from something an agent actually wrote.
/// </para>
/// </summary>
public readonly record struct TopicOwnerAsk(string Source, string What, DateTime? Since = null);

/// <summary>
/// PULSE's field 4 — the last relevant event, AS ONE VALUE: `last · 18:00 · FIN-D-293 merged to
/// staging`.
///
/// <para>
/// THE PAIR IS THE POINT. The clock and the subject used to arrive separately and from DIFFERENT
/// FILES: the subject was the newest session entry across the live member spokes, and the clock was
/// the stamp on the supervisor's last entry in `owner-channel.md` — the same value that fills
/// "declared HH:MM" one field above. So a topic where the supervisor had spoken at 18:00 and an
/// implementer had reported at 18:47 rendered `last · 18:00 · &lt;the implementer's subject&gt;`: two true
/// facts, one false sentence, and the owner reads that line to know when something last moved.
/// </para>
/// <para>
/// Nothing was wrong with either half, which is why nothing caught it — no test ever set the clock
/// (grep found not one `LastEventAt` in the suite), because the two halves were never asserted
/// together. A record makes the pairing structural: there is no longer a way to hand the builder a
/// time from one event and a subject from another, because there is only one argument.
/// </para>
/// <para>
/// <paramref name="At"/> stays nullable, and null prints no clock rather than borrowing `now` —
/// which would date every event to the moment the line was drawn. An agent-written stamp that cannot
/// be trusted (unparseable, or in the future) arrives here as null by the same rule that already
/// keeps it from winning the field at all.
/// </para>
/// </summary>
public readonly record struct TopicLastEvent(string Subject, DateTime? At);

/// <summary>
/// The inputs PULSE needs that are not derivable from the ledger and the member channels — passed
/// as ONE named value rather than a growing tail of optional parameters.
///
/// <para>
/// WHY A RECORD AND NOT FOUR MORE ARGUMENTS. `TopicStatusLine_Planner.TopicNewestMessage` records
/// the lesson this follows: two adjacent parameters of the same nullable type can be swapped at the
/// one call site with everything still compiling, and the engine that calls it is `internal sealed`,
/// so the suite cannot see the swap. Three of the five fields below are `DateTime?` and two of them
/// are timestamps of different things — declared-at against resume-at — which is exactly that trap.
/// Named members make the compiler refuse it.
/// </para>
/// <para>
/// EVERY FIELD IS DATA, NEVER A PRE-FORMATTED STRING, apart from the supervisor's own declared
/// state, which IS prose the supervisor wrote. The clock renderings ("declared 18:12", "since
/// 18:18", "updated 18:20") all happen inside the builder, so there is one spelling of a time on
/// this surface and a caller cannot invent a second.
/// </para>
/// </summary>
/// <param name="Mode">
/// The topic's delivery mode, which decorates PULSE's HEADER LINE. It moved off the topic NAME on
/// the owner's 2026-09-09 directive; the glyph characters themselves stay in
/// <see cref="TelegramDeliveryMode_Glyphs"/>, so the two surfaces cannot come to disagree about
/// what a muted topic looks like.
/// </param>
/// <param name="IsAway">
/// Away mode — app-wide, and that is why it belongs here rather than on the name. On the name it
/// renamed EVERY open topic the moment the owner went away, and each rename writes a service
/// message into the thread it renames: one state change, one line of noise per topic, telling them
/// something they had just done. Here it is one character in a message already being edited.
/// </param>
/// <param name="IsQuiet">
/// This one orchestration has stopped asking after three unanswered messages. Per topic, unlike
/// away — and away SUPERSEDES it, because away already means every orchestration has stopped asking,
/// so drawing both would state one fact twice (the rule kept verbatim from the topic name).
/// </param>
/// <param name="Presence">
/// Where the owner is sitting for THIS orchestration. Terminal presence REPLACES the mode glyph
/// rather than joining it: sitting in the terminal already silences the topic, so 💻 🔕 together
/// would say one thing twice — the presence/delivery conflation that mode exists to remove.
/// </param>
/// <param name="SupervisorDeclaredState">
/// The supervisor's own one-line state, AS THE SUPERVISOR DECLARED IT at turn end. Null until it
/// declares one — and null must read as "nothing declared", never as a state: the app does not know
/// what a supervisor is doing and the whole point of this field is that the supervisor says.
/// </param>
/// <param name="SupervisorDeclaredAt">When that declaration was read. Null prints no clock.</param>
/// <param name="UsageLimitResumeAt">
/// When a usage-limit pause ends. This is the ONE thing the app adds to the supervisor's own words,
/// because it is the one thing it knows for certain — and it is what the false stall alerts of
/// 2026-09-09 were actually looking at: a supervisor that was not waiting for the owner but paused.
/// </param>
/// <param name="OwnerAsks">
/// What the APP knows the owner is being waited on for — the open questions on the owner channel,
/// which PULSE cannot see for itself: the builder is handed per-member channels, and a supervisor's
/// question to the owner lives in `owner-channel.md`. Blocked members and `[?]` ledger lines are
/// derived by the builder from data it already has and do not belong here.
/// </param>
public readonly record struct TopicStatusFields(
    TelegramDeliveryModes Mode = TelegramDeliveryModes.Normal,
    bool IsAway = false,
    bool IsQuiet = false,
    OwnerPresenceModes Presence = OwnerPresenceModes.Remote,
    string? SupervisorDeclaredState = null,
    DateTime? SupervisorDeclaredAt = null,
    DateTime? UsageLimitResumeAt = null,

    // NO `LastEventAt` HERE ANY MORE. It was the second half of field 4, filled by the engine from
    // the owner channel while the first half came from the member spokes — see TopicLastEvent for
    // what that rendered. The pair now travels together as one argument to the builder, and the only
    // way to keep this field would be to keep the two-source bug available.
    IReadOnlyList<TopicOwnerAsk>? OwnerAsks = null);

/// <summary>
/// PULSE's own vocabulary: the one-word state a member row shows, and the reading order of the
/// rows.
///
/// <para>
/// IT IS NOT A SECOND COPY OF <see cref="MemberState_Descriptor"/>, and the distinction matters
/// because CLAUDE.md decision 12 forbids the thing it superficially resembles. That class answers
/// "describe this state on the app's CARD", where a row has a whole line to itself:
/// "briefed — not started yet", "idle — writing window left open". This answers "name this state in
/// ONE FIELD of a phone row that also carries an id, a task and a duration". Both read the same
/// <see cref="MemberStates"/> value from the same <see cref="MemberState_Resolver"/>, so they can
/// never disagree about WHICH state a member is in — only about how many words to spend saying it,
/// which is what the two surfaces were asked for separately.
/// </para>
/// <para>
/// "waiting on you" is taken from <see cref="MemberState_Descriptor.WAITING_ON_OWNER"/> rather than
/// spelled again: that phrase is the one the owner is taught to recognise as their own queue, and
/// two spellings of it would be two queues.
/// </para>
/// <para>
/// "writing window" IS DELIBERATELY UNSAYABLE HERE. The owner struck it from this surface on
/// 2026-09-09 ("Out: writing window"), so a member mid-write reads <see cref="WORKING"/> — which is
/// what it is doing. The state itself is unchanged and the card still names it; only this line stops
/// spending a field on a protocol detail the owner cannot act on (decision 15).
/// </para>
/// </summary>
public static class TopicStatusWording
{
    /// <summary>The owner's own queue, in the one wording the rest of the app already uses for it.</summary>
    public const string WAITING_ON_YOU = MemberState_Descriptor.WAITING_ON_OWNER;

    public const string WORKING = "working";

    /// <summary>
    /// Waiting on the SUPERVISOR, which is not idle and not the owner's queue. Kept as its own word
    /// rather than folded into <see cref="WORKING"/>: a review sitting unread is the one queue the
    /// owner can unblock by nudging, and calling it "working" would hide it.
    /// </summary>
    public const string AWAITING_REVIEW = "awaiting review";

    /// <summary>
    /// Nothing owed and nothing running. NOT "idle": the owner has sent that word back twice
    /// (2026-08-15, 2026-08-21) because it reads as ABSENT when the truth is merely that nothing has
    /// been declared since.
    /// </summary>
    public const string STANDING_BY = "standing by";

    public static string Describe_State(MemberStates state)
    {
        return state switch
        {
            MemberStates.BlockedOnOwner => WAITING_ON_YOU,
            MemberStates.AwaitingSupervisorReview => AWAITING_REVIEW,
            MemberStates.ImplementerWorking => WORKING,
            MemberStates.WritingWindowOpen => WORKING,
            MemberStates.StandingBy => STANDING_BY,
            MemberStates.NewNoTraffic => STANDING_BY,
            _ => throw new Exception($"Unhandled MemberStates: {state}"),
        };
    }

    /// <summary>
    /// Reading order, as the owner set it: *"waiting on the owner → working → idle"*. Lower sorts
    /// first.
    ///
    /// <para>
    /// It is a SORT KEY rather than a comparison so the sort stays STABLE — members inside one bucket
    /// keep the roster's order, which is the order the owner added them in. A comparison that ranked
    /// pairs would be free to shuffle equals, and a status line whose rows move between refreshes for
    /// no reason is one the owner has to re-read every time.
    /// </para>
    /// </summary>
    public static int Reading_Order(MemberStates state)
    {
        return state switch
        {
            MemberStates.BlockedOnOwner => 0,
            MemberStates.ImplementerWorking => 1,
            MemberStates.WritingWindowOpen => 1,
            MemberStates.AwaitingSupervisorReview => 1,
            MemberStates.StandingBy => 2,
            MemberStates.NewNoTraffic => 2,
            _ => throw new Exception($"Unhandled MemberStates: {state}"),
        };
    }

    /// <summary>
    /// The ONE spelling of a wall clock on this surface — "18:12". Invariant, because a topic line is
    /// read by one person on one phone and a culture-dependent 12-hour rendering would make "06:12"
    /// ambiguous between morning and evening on a line whose entire job is telling the owner when
    /// something happened.
    ///
    /// <para>
    /// Every time on PULSE comes through here: declared-at, since, the resume time, the `last`
    /// event and the heartbeat. Five copies of a format string is how one of them ends up rendering
    /// seconds.
    /// </para>
    /// </summary>
    public static string Clock(DateTime moment)
    {
        return moment.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The clock reading for a moment, or null when it is in the FUTURE relative to
    /// <paramref name="now"/>.
    ///
    /// <para>
    /// CLAUDE.md decision 12, applied to every clock on this line. A supervisor really did stamp an
    /// entry ten hours ahead, and the duration formatter already refuses such a stamp — printing
    /// "declared 01:34" for something written at 15:20 the previous day would put the refusal in one
    /// field and the lie in the next one, inside the same message.
    /// </para>
    /// </summary>
    public static string? Clock_OrNull(DateTime? moment, DateTime now)
    {
        if (moment == null || moment.Value > now)
            return null;

        return Clock(moment.Value);
    }
}

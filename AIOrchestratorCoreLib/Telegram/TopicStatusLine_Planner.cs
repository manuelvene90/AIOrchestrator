using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// EVERY decision the status line makes, in one pure function — what to write, whether to write it,
/// and whether now is the moment. The engine is left with the execution and nothing else.
///
/// WHY THIS EXISTS, and it is not tidiness. `BridgeEngineModel` is `internal sealed` with no
/// `InternalsVisibleTo`, and the test project references CoreLib alone — so anything decided inside
/// the engine is unreachable from the suite. That is not a theory: a reviewer deleted the trusted
/// stamp reader, the per-topic delivery gate and the backoff gate ALL AT ONCE and the suite stayed
/// green, then proved the build was real by injecting a syntax error into the same file and watching
/// it fail. The green was necessary, not observed.
///
/// The seam was available as `InternalsVisibleTo` and was refused deliberately: it would make the
/// engine testable without making it tested. Moving the two error predicates out is what made them
/// pinned, so the same move is applied to the gates and to the wiring that activates them.
///
/// The wiring is the subtle half. Passing a derived `bool` let a mutation hand the builder `false`
/// with nothing reddening — the fix rested on an argument no test could see. The planner takes the
/// MESSAGE ID itself and derives the flag here, so there is no boolean at the call site to get wrong.
/// </summary>
public static class TopicStatusLine_Planner
{
    /// <summary>What the engine should do this tick, and the exact text to send if anything.</summary>
    public readonly record struct TopicStatusPlan(TopicStatusActions Action, string Text);

    /// <summary>
    /// The newest message the app knows of in a topic, and when it learned of it.
    ///
    /// A RECORD RATHER THAN TWO LOOSE PARAMETERS, and that is the M-G3 lesson applied preventively:
    /// `existingMessageId` and this id are both `long?` and mean opposite things, so as adjacent
    /// arguments they could be swapped at the one call site with everything still compiling — and the
    /// swap is invisible to the suite, because the engine is `internal sealed`. Swapped, the status
    /// line would be compared against itself and never move again. As a named type the compiler
    /// refuses it.
    /// </summary>
    public readonly record struct TopicNewestMessage(long MessageId, DateTime ArrivedAt);

    /// <summary>
    /// How long a topic must be quiet before a buried status line is rewritten at the bottom, as the
    /// owner stated the rule on 2026-08-13. It is what bounds the repost to at most one notification
    /// per quiet period: every message resets it, so a conversation in progress is never interrupted,
    /// and the line moves once the exchange is over.
    /// </summary>
    public const int REPOST_AFTER_QUIET_SECONDS = 120;

    public static TopicStatusPlan Plan(
        string title,
        IPlanProgress? progress,
        IReadOnlyList<ITopicStatusMember> members,
        DateTime now,
        long? existingMessageId,
        string? lastWrittenText,
        TelegramDeliveryModes mode,
        DateTime? lastFailedAttemptAt,
        int backoffSeconds,
        TopicNewestMessage? newestTopicMessage,
        bool repostIsImpossible,
        TimeSpan? figuresUnchangedFor = null)
    {
        // The id decides what "nothing to say" means, and it is passed rather than a flag derived at
        // the call site — that derivation was mutable to `false` with nothing reddening.
        // The `last` line is chosen HERE, not handed in. Gate C — the trusted reading of an
        // agent-written stamp — was the one gate that never left the engine, so it could be reverted
        // to a raw parse with 630 tests staying green.
        var text = TopicStatusLine_Builder.Build(title, progress, members, Pick_LastSubject_OrNull(members, now), now, existingMessageId != null, figuresUnchangedFor);

        var decided = TopicStatusLine_Decider.Decide(text, lastWrittenText, existingMessageId);

        // THE REPOST OVERRIDES THE DECIDER, and it has to. A buried line is USUALLY unchanged text —
        // a quiet orchestration says the same thing minute after minute — and the identical-text rule
        // answers None to exactly that. Behind that rule the repost would fire only for a topic that
        // happened to change something in the same tick, which is never the quiet topic it was asked
        // for. Emptiness is still refused: the builder emits the bare title whenever a message is up,
        // so blank text here would mean sending nothing at all.
        // THE LATCH COMES FIRST, and it is a fallback rather than a failure. Telegram REFUSES some
        // deletes permanently — a message past its 48-hour window, or a bot without
        // `can_delete_messages` — and a refusal is not a gone message, so nothing clears the id and
        // the delete throws ahead of the send on every tick. Because this promotion overrides the
        // decider, the Edit was starved too: the line ended up buried AND stale, which is worse than
        // the behaviour the repost replaced, where it was merely buried.
        //
        // Latched, the topic stops trying to MOVE its line and goes on updating it in place. That is
        // master's behaviour, which is the right floor to degrade to.
        var action = !repostIsImpossible
                     && !string.IsNullOrWhiteSpace(text)
                     && Is_RepostDue(existingMessageId, newestTopicMessage, now, REPOST_AFTER_QUIET_SECONDS)
            ? TopicStatusActions.Repost
            : decided;

        // THE DELIVERY GATE IS ON THE MESSAGES THAT NOTIFY — the POST, and now the REPOST, which is a
        // delete followed by a send and so pushes to the phone exactly as a first post does. An edit
        // notifies nobody, so silencing it buys nothing and costs a line frozen at pre-DND content for
        // the whole period; a topic the owner silenced must not be the thing that wakes them.
        //
        // A BLOCKED REPOST FALLS BACK TO WHAT THE DECIDER SAID rather than to silence. The content
        // still updates in place — Deferred's contract is that nothing is lost — and only the MOVE
        // waits for the unmute. Falling back to a blanket Edit instead would rewrite identical text
        // every tick for the whole DND period, which is the wasted-call spin the identical-text rule
        // exists to stop.
        if (action == TopicStatusActions.Repost && mode != TelegramDeliveryModes.Normal)
            action = decided;

        if (action == TopicStatusActions.None)
            return new TopicStatusPlan(TopicStatusActions.None, text);

        if (action == TopicStatusActions.Post && mode != TelegramDeliveryModes.Normal)
            return new TopicStatusPlan(TopicStatusActions.None, text);

        // THE BACKOFF, last: a 429 answered at the tick rate inverts the cadence from once a minute
        // to thirty times a minute per topic and sustains the throttling that caused it.
        if (!Is_AttemptDue(lastFailedAttemptAt, now, backoffSeconds))
            return new TopicStatusPlan(TopicStatusActions.None, text);

        return new TopicStatusPlan(action, text);
    }


    /// <summary>
    /// The most recent real entry across the LIVE members, by the stamp the agent wrote. A closed
    /// member does not feed this: one message must not disagree with itself about whether a member
    /// exists.
    ///
    /// App entries are not conversation — without that, /resume appends to every member channel in
    /// every orchestration and every topic simultaneously reads `last GO AHEAD`.
    /// </summary>
    public static string? Pick_LastSubject_OrNull(IReadOnlyList<ITopicStatusMember> members, DateTime now)
    {
        IChannelEntry? latest = null;

        foreach (var member in members)
        {
            if (member.IsClosed)
                continue;

            // SESSIONS ONLY. A solo's "member channel" IS the owner channel, so this scan used to
            // reach the owner's own messages — and the bridge stamps every inbound one with the
            // subject "via Telegram", which is what the owner then read back as their orchestration's
            // last word (their call, 2026-08-19).
            var candidate = MemberState_Resolver.Find_LastSessionEntry_OrNull(member.Entries);

            if (candidate != null && (latest == null || Is_LaterStamp(candidate, latest, now)))
                latest = candidate;
        }

        return latest?.Subject;
    }

    /// <summary>
    /// GATE C. Which of two entries happened later, by the stamp the AGENT wrote — read through the
    /// one trusted-stamp reader rather than a raw parse, so an entry dated in the future cannot win
    /// `last` and hold it until real time catches up.
    ///
    /// It lived in the engine, which is unreachable from the suite, so it could be reverted to
    /// DateTime.TryParse without a single test noticing. An unparseable or future stamp LOSES rather
    /// than winning by accident.
    /// </summary>
    public static bool Is_LaterStamp(IChannelEntry candidate, IChannelEntry incumbent, DateTime now)
    {
        if (!SessionDuration_Formatter.Try_ReadTrustedStamp(candidate.DateText, now, out var candidateStamp))
            return false;

        if (!SessionDuration_Formatter.Try_ReadTrustedStamp(incumbent.DateText, now, out var incumbentStamp))
            return true;

        return candidateStamp > incumbentStamp;
    }

    /// <summary>
    /// Has the backoff elapsed since the last FAILED attempt? No recorded failure means due — the
    /// common case, and it must not cost a wait.
    ///
    /// BOTH ARGUMENTS MUST COME FROM THE SAME CLOCK, and the caller now has only one to give. This
    /// was passed a UTC stamp against a LOCAL `now`: on a UTC+2 machine that made one second after a
    /// failure compute as two hours elapsed, so a 30-second backoff cleared instantly at every value
    /// it could ever be given — the 429 protection was absent while every test passed, because the
    /// tests build both sides from one constant and cannot observe two clocks disagreeing.
    ///
    /// The seam moved the DECISION and left the CLOCK at the call site. That is the same shape as the
    /// derived bool removed for M-G3, one parameter to the left.
    ///
    /// TWO JUSTIFICATIONS NOW RIDE ON ONE `now`, AND ONLY ONE IS LOAD-BEARING. The durations need
    /// LOCAL because they compare against agent-written local stamps in channel headers. The
    /// backoff has no such requirement — it inherited local purely by being handed the same clock.
    /// Anyone "fixing" this back to UTC for correctness will reintroduce the inert backoff, because
    /// the stored stamp would then disagree with the durations' clock again.
    ///
    /// LOCAL TIME IS NOT MONOTONIC, which is the residual this choice carries. At the autumn
    /// transition the clock steps back an hour, so a failure stamped at 03:00 yields a NEGATIVE
    /// elapsed and this holds the line off for up to an hour — once a year, silently. Spring clears
    /// a backoff early and is harmless. The proper answer for an INTERVAL is a monotonic source
    /// rather than either wall clock; it is on the ledger and is not a tonight problem.
    /// </summary>
    /// <summary>
    /// Has the status line been BURIED, and has the topic gone quiet since? Both halves are required
    /// and each answers a different failure.
    ///
    /// Buried is decided by comparing message IDS, not by a count or a flag: Telegram ids increase
    /// within a chat, so an id above the status line's is a message that came after it. The engine
    /// remembers every id it sends or receives per topic, which is the same set /clear deletes from.
    ///
    /// Quiet is what keeps this from being a waterfall. A repost NOTIFIES — Telegram cannot move a
    /// message, so the only way to put the line at the bottom is to delete and send — and firing it
    /// the instant a message lands would ping the owner in the middle of their own sentence. Every
    /// message resets the window, so at most one notification arrives per quiet period.
    ///
    /// UNKNOWN IS NOT BURIED. The newest id lives in memory, so after a restart there is none for any
    /// topic until traffic repopulates it, and a repost is a notification: "I do not know where the
    /// line is" must not be answered by pushing to a phone. It edits in place, as it always did.
    ///
    /// The stamps are both LOCAL, from the one clock this file uses — read the Is_AttemptDue comment
    /// before touching either. A message stamped in the FUTURE (a clock step, nothing else can do it)
    /// yields a negative elapsed and holds the repost, which is the safe direction.
    /// </summary>
    public static bool Is_RepostDue(long? existingMessageId, TopicNewestMessage? newestTopicMessage, DateTime now, int quietSeconds)
    {
        if (existingMessageId == null || newestTopicMessage == null)
            return false;

        // EQUAL is not buried: the newest message the app knows of IS the status line, so nothing came
        // after it. Only strictly-later ids bury it.
        if (newestTopicMessage.Value.MessageId <= existingMessageId.Value)
            return false;

        return now - newestTopicMessage.Value.ArrivedAt >= TimeSpan.FromSeconds(quietSeconds);
    }

    public static bool Is_AttemptDue(DateTime? lastFailedAttemptAt, DateTime now, int backoffSeconds)
    {
        if (lastFailedAttemptAt == null)
            return true;

        return now - lastFailedAttemptAt.Value >= TimeSpan.FromSeconds(backoffSeconds);
    }
}

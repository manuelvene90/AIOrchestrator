using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using AIOrchestratorCoreLib.Status.SessionContextUsage;

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
    ///
    /// TEN SECONDS, NOT TWO MINUTES (owner directive 2026-08-24): *"the topic status message should
    /// arrive immediately, not after 2 minutes, but more like after 10 seconds"*. At 120 the line was
    /// correct and invisible — the owner opened the topic, found their own last message above it and
    /// had to scroll for the state, which is the exact defect the repost was built to remove. Ten is
    /// long enough to still be a PAUSE: the owner's own multi-message burst, and an agent's mirrored
    /// entries arriving in chunks, both keep resetting it.
    ///
    /// WHAT KEEPS A SHORT WINDOW FROM BEING A WATERFALL IS NOT THIS NUMBER. The status line's own
    /// message is never recorded as topic traffic (`_newestTopicMessageByThread` in
    /// `BridgeEngineModel` is written only by `Remember_TopicMessage`, which the status-line refresh
    /// deliberately does not call), so a fresh post carries a HIGHER id than the newest message the
    /// app knows of and `Is_RepostDue` reads the line as un-buried from the very next tick. That
    /// bounds it at ONE repost per burst of real traffic at any window value — shortening the window
    /// changes WHEN the move happens, never how often it can repeat. The cost of 10 over 120 is
    /// therefore paid only in a topic that keeps talking with pauses in between: a delete plus a send
    /// where an edit would have done, once per pause.
    /// </summary>
    public const int REPOST_AFTER_QUIET_SECONDS = 10;

    public static TopicStatusPlan Plan(
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
        TimeSpan? figuresUnchangedFor = null,
        ISessionContextUsage? supervisorContext = null,

        // WHAT ONLY THE ENGINE CAN KNOW — the supervisor's declared state, what the owner is being
        // waited on for, a usage-limit pause, when the last event happened. The builder is handed
        // per-member channels and cannot read owner-channel.md or a member's state file, so these
        // arrive as data rather than being fetched. See TopicStatusFields.
        TopicStatusFields fields = default)
    {
        // The id decides what "nothing to say" means, and it is passed rather than a flag derived at
        // the call site — that derivation was mutable to `false` with nothing reddening.
        // The `last` line is chosen HERE, not handed in. Gate C — the trusted reading of an
        // agent-written stamp — was the one gate that never left the engine, so it could be reverted
        // to a raw parse with 630 tests staying green.
        // THE MODE IS FILLED IN HERE rather than by the caller: the planner already takes it for its
        // own repost gate, and asking the engine to pass the same value twice is how two surfaces
        // come to disagree about whether a topic is muted.
        var text = TopicStatusLine_Builder.Build(
            progress, members, Pick_LastEvent_OrNull(members, now), now, existingMessageId != null,
            figuresUnchangedFor, supervisorContext, fields with { Mode = mode });

        var decided = TopicStatusLine_Decider.Decide(text, lastWrittenText, existingMessageId);

        // THE REPOST RIDES ON THE DECIDER — it no longer overrides it. Owner, 2026-09-09: PULSE is
        // "deleted and re-posted (silently) only when it is buried by later traffic AND its content
        // changed". Burial alone used to be enough, and the cost was the surface's own promise: a
        // quiet orchestration says the same thing minute after minute, so every pause in a talkative
        // topic bought a delete plus a post that carried no news — the waterfall decision 14 exists to
        // prevent, arriving one message at a time instead of all at once.
        //
        // "SOMETHING NEW TO SAY" IS THE DECIDER'S ANSWER, NOT A SECOND COMPARISON. `Decide` already
        // answers None for both cases that must not move the line — text identical to what is up, and
        // text that is blank — so asking it is the whole predicate; writing `lastWrittenText != text`
        // here would be a second spelling of the same rule, free to disagree with the first.
        //
        // AFTER A RESTART the remembered text is null (it lives in memory) and `Decide` reads that as
        // Edit, which counts as news here. That does NOT produce a restart repost: the newest-message
        // map is in memory too, so `Find_NewestTopicMessage_OrNull` answers null until the app observes
        // real traffic, and `Is_RepostDue` refuses a topic it knows nothing about. The two blind spots
        // cover each other, and the test at the bottom of this file pins the pair.
        //
        // AND THE HEARTBEAT IS NOT NEWS. Field 6 is `updated HH:MM`, emitted unconditionally, so
        // PULSE's raw text differs from the previous one at every minute boundary however still the
        // orchestration is — which would have degraded the owner's rule to "buried, then within sixty
        // seconds". The repost asks the substance question through `Strip_Heartbeat`; the EDIT still
        // compares the raw text, because keeping the clock ticking in place is the heartbeat's whole
        // job and an edit notifies nobody.
        var somethingNewToSay = decided != TopicStatusActions.None
            && TopicStatusLine_Builder.Strip_Heartbeat(text) != TopicStatusLine_Builder.Strip_Heartbeat(lastWrittenText);

        // THE LATCH COMES FIRST, and it is a fallback rather than a failure. Telegram REFUSES some
        // deletes permanently — a message past its 48-hour window, or a bot without
        // `can_delete_messages` — and a refusal is not a gone message, so nothing clears the id and
        // the delete throws ahead of the send on every tick. Because this promotion overrides the
        // decider, the Edit was starved too: the line ended up buried AND stale, which is worse than
        // the behaviour the repost replaced, where it was merely buried.
        //
        // Latched, the topic stops trying to MOVE its line and goes on updating it in place. That is
        // master's behaviour, which is the right floor to degrade to.
        var action = somethingNewToSay
                     && !repostIsImpossible
                     && Is_RepostDue(existingMessageId, newestTopicMessage, now, REPOST_AFTER_QUIET_SECONDS)
            ? TopicStatusActions.Repost
            : decided;

        // THE DELIVERY GATE IS ON SILENCED ONLY — it used to be on everything but Normal, and the
        // reason it could be is gone. Every one of these writes is `TelegramSendSounds.Silent` now
        // (brief C), so a post and a repost wake nobody; the gate was reasoning about a post that
        // notified, and it outlived that post.
        //
        // DEFERRED (🌙) AND SILENCED (🔕) PART COMPANY HERE, on the owner's ruling of 2026-09-09:
        // "DND holds only what rings; PULSE and the dashboard keep updating silently". The two modes
        // mean opposite things about content — Deferred KEEPS everything and replays it, because the
        // owner is away and will come back to it; Silenced DROPS it, because they are reading the
        // same thing live in the terminal and do not want it twice.
        //
        // WHAT "OUT OF THE WAY UNDER SILENCED" MEANS EXACTLY, because the looser wording contradicted
        // the code two lines below it: 🔕 refuses to PUT A MESSAGE THERE and refuses to MOVE one. It
        // does not stop the EDIT — an edit notifies nobody and appears nowhere new, and a line left
        // frozen for the length of a terminal session would be wrong on the owner's next glance. So
        // Silenced adds no message to the topic and Deferred keeps its line current AND at the bottom.
        //
        // A BLOCKED REPOST FALLS BACK TO WHAT THE DECIDER SAID rather than to silence: the content
        // still updates in place and only the MOVE waits. Falling back to a blanket Edit instead
        // would rewrite identical text every tick, which is the wasted-call spin the identical-text
        // rule exists to stop.
        if (action == TopicStatusActions.Repost && mode == TelegramDeliveryModes.Silenced)
            action = decided;

        if (action == TopicStatusActions.None)
            return new TopicStatusPlan(TopicStatusActions.None, text);

        if (action == TopicStatusActions.Post && mode == TelegramDeliveryModes.Silenced)
            return new TopicStatusPlan(TopicStatusActions.None, text);

        // THE BACKOFF, last: a 429 answered at the tick rate inverts the cadence from once a minute
        // to thirty times a minute per topic and sustains the throttling that caused it.
        if (!Is_AttemptDue(lastFailedAttemptAt, now, backoffSeconds))
            return new TopicStatusPlan(TopicStatusActions.None, text);

        return new TopicStatusPlan(action, text);
    }


    /// <summary>
    /// The most recent real entry across the LIVE members, by the stamp the agent wrote — AND ITS
    /// STAMP, which is the change of 2026-09-10. A closed member does not feed this: one message
    /// must not disagree with itself about whether a member exists.
    ///
    /// App entries are not conversation — without that, /resume appends to every member channel in
    /// every orchestration and every topic simultaneously reads `last GO AHEAD`.
    ///
    /// <para>
    /// IT USED TO RETURN THE SUBJECT ALONE and throw the winning entry away, so the clock beside it
    /// had to come from somewhere else — and it came from another file. The entry it already holds
    /// carries both, so returning both is not new work; it is stopping the discard. The stamp goes
    /// through the same trusted reader that decides which entry WINS, so a future or unparseable
    /// stamp reads as null here exactly as it loses there — one rule, not two.
    /// </para>
    /// </summary>
    public static TopicLastEvent? Pick_LastEvent_OrNull(IReadOnlyList<ITopicStatusMember> members, DateTime now)
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

        if (latest == null)
            return null;

        return new TopicLastEvent(
            latest.Subject,
            SessionDuration_Formatter.Try_ReadTrustedStamp(latest.DateText, now, out var stamp) ? stamp : null);
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

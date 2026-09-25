using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using AIOrchestratorCoreLib.Status.SessionModelReading;

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
    /// <summary>
    /// What the engine should do this tick, the exact text to send if anything, and the whole rendering
    /// (<see cref="TopicStatusLine_RenderKey"/>) that text makes with the bar it was planned against — the
    /// value the engine remembers once the write succeeds, so the key is built in one place.
    ///
    /// <para>
    /// <see cref="SeenAtBottomKey"/> is the third thing the engine remembers after a successful write
    /// (ruling R27): the rendering, heartbeat stripped, that the owner saw when the line was last at the
    /// bottom. The planner decides it — the line's burial state is known here and nowhere the suite can
    /// reach in the engine — so the engine stores it and never computes it. Null only when nothing is
    /// known (a restart before the first write), which the next plan reads as "compare with the last
    /// write".
    /// </para>
    /// </summary>
    public readonly record struct TopicStatusPlan(TopicStatusActions Action, string Text, string RenderKey, string? SeenAtBottomKey);

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
        TopicStatusFields fields = default,

        // THE SUPERVISOR'S MODEL AND EFFORT, and the two `pulse.*` values — all RESOLVED BY THE ENGINE
        // and handed through untouched, because this function is pure: it is never given a config
        // provider, and a value it read for itself would be a second reading of a setting the owner can
        // change between two ticks. Null keeps the builder's shipped defaults, as it does there.
        ISessionModelReading? supervisorModel = null,
        IReadOnlyList<string>? pulseFields = null,
        int? stepMinutes = null,

        // `topic.modeGlyphs`, resolved by the engine for the same reason: whether the header or the
        // topic name carries the five mode glyphs. Null is the builder's shipped default.
        ModeGlyphPlacements? modeGlyphs = null,

        // THE BAR, AND THE WHOLE RENDERING LAST WRITTEN — see "HOW A CHANGE TO THE BAR ALONE IS SEEN"
        // below. Both null is "no bar is known", which decides on the text alone: every caller that
        // predates brief D, and the tests that are about the text.
        IReadOnlyList<IReadOnlyList<(string Data, string Label)>>? commandButtonRows = null,
        string? lastWrittenRenderKey = null,

        // WHAT THE OWNER LAST SAW AT THE BOTTOM — the previous plan's SeenAtBottomKey, handed back
        // (ruling R27). Null falls back to the rendering last written: the rule as it was before R27.
        string? lastSeenAtBottomKey = null,

        // `pulse.unchangedFor`, resolved by the engine like the other `pulse.*` values (task 19). Null is
        // the builder's shipped default.
        bool? unchangedFor = null)
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
            figuresUnchangedFor, supervisorContext, fields with { Mode = mode },
            supervisorModel, pulseFields, stepMinutes, modeGlyphs, unchangedFor);

        IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows = commandButtonRows ?? [];
        var renderKey = TopicStatusLine_RenderKey.Build(text, buttonRows);

        // TEXT AGAINST TEXT. `lastWrittenText` is the TEXT last written, never the render key: the key
        // opens with "<length>:", so a key compared with raw text never matches, and from the fork's
        // `2143db8` until plan 03 Task 17 (2026-09-23) that is what the engine handed in — every tick
        // answered Edit (one "not modified" edit per topic every 30 s in production), and the repost
        // gate below reduced to "buried and quiet", so PULSE was deleted and re-sent ten seconds after
        // every exchange whether or not anything in it had changed (plan 03 report §5.3).
        var decided = TopicStatusLine_Decider.Decide(text, lastWrittenText, existingMessageId);

        // HOW A CHANGE TO THE BAR ALONE IS SEEN. The bar is not part of the text — the hold toggle's label
        // carries the held count, and `pulse.buttons` can be changed from config.json — so a text
        // comparison is blind to it, and a quiet orchestration's text does not move for hours (brief D).
        // The planner is handed the bar itself and the whole rendering last written, and asks: "would the
        // text last written, under TODAY'S bar, render as what was written?" If not, the bar changed.
        //
        // WHY THE ROWS AND THE KEY, AND NOT A `barChanged` FLAG: the flag would be derived at the call
        // site inside the engine, where nothing can see it, and a derived bool is exactly what this file
        // was built to stop taking (M-G3, the id-for-a-bool change above). WHY THE PLANNER AND NOT THE
        // ENGINE: this promotion used to be an engine branch after the plan, with its own copy of the
        // back-off check (a 2026-09-10 incident, 357 retries against a 429, came from the copy it once
        // lacked); here it runs under the one back-off at the bottom of this method, and it is reachable
        // by the suite.
        //
        // No rows or no key is "unknown", which is "unchanged": a restart forgets the key along with the
        // text, and `Decide` already answers Edit for that.
        var barChanged = commandButtonRows != null
            && lastWrittenText != null
            && lastWrittenRenderKey != null
            && TopicStatusLine_RenderKey.Build(lastWrittenText, buttonRows) != lastWrittenRenderKey;

        // A BAR-ONLY CHANGE IS AN EDIT (or a first post, if the id is gone), never a reason to write a
        // blank line — `Decide`'s nothing-to-say rule still holds on the text.
        if (decided == TopicStatusActions.None && barChanged && !string.IsNullOrWhiteSpace(text))
            decided = existingMessageId == null ? TopicStatusActions.Post : TopicStatusActions.Edit;

        // THE REPOST RIDES ON THE DECIDER — it no longer overrides it. Owner, 2026-09-09: PULSE is
        // "deleted and re-posted (silently) only when it is buried by later traffic AND its content
        // changed". Burial alone used to be enough, and the cost was the surface's own promise: a
        // quiet orchestration says the same thing minute after minute, so every pause in a talkative
        // topic bought a delete plus a post that carried no news — the waterfall decision 14 exists to
        // prevent, arriving one message at a time instead of all at once.
        //
        // "SOMETHING NEW TO SAY" USED TO BE THE DECIDER'S ANSWER — "different from the last write" — and
        // ruling R27 (2026-09-24) replaced it; see "NEW SINCE THE OWNER LAST SAW IT" below. The two
        // cases that must not move the line are still refused: blank text, and a rendering that is what
        // the owner saw at the bottom.
        //
        // AFTER A RESTART nothing is remembered (it lives in memory), which counts as news here. That
        // does NOT produce a restart repost: the newest-message
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
        //
        // A CHANGED BAR IS NEWS (plan 03 Task 17): the owner reads the labels — the held count on the
        // toggle is the reason the render key exists — so a bar that changed under a buried line moves
        // it exactly as a changed row would.
        //
        // NEW SINCE THE OWNER LAST SAW IT AT THE BOTTOM, NOT SINCE THE LAST WRITE (ruling R27, 2026-09-24).
        // Asking the decider answered "new since the last write", and the last write of a buried line is
        // usually the in-place EDIT made during the quiet window — so the common case, an answer that both
        // buries PULSE and changes its STATE, was edited above the answer and then left there for good:
        // by the time the topic was quiet it had nothing new to say against itself. The question is now
        // asked of the SUBSTANCE (heartbeat stripped, bar included) against what the line showed when it
        // was last unburied, which an edit made while buried does not move. With nothing remembered (a
        // restart, or a caller that predates R27) the last write stands in for it — the rule as it was.
        var substanceKey = Build_SubstanceKey(text, buttonRows);
        var seenAtBottomBefore = lastSeenAtBottomKey
            ?? (lastWrittenText == null ? null : Build_SubstanceKey(lastWrittenText, buttonRows));

        var somethingNewToSay = !string.IsNullOrWhiteSpace(text)
            && (seenAtBottomBefore == null
                || substanceKey != seenAtBottomBefore

                // A bar change is invisible to the fallback, which rebuilds the last write under TODAY'S
                // bar; a remembered key carries the bar it was seen with, so it needs no help.
                || (lastSeenAtBottomKey == null && barChanged));

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

        // WHAT THE OWNER WILL HAVE SEEN AT THE BOTTOM once this write lands (ruling R27). A repost puts the
        // line there, and a write to a line that is not buried is written where they are looking; an
        // edit to a BURIED line is not seen, so the memory stays where it was — that is the whole fix.
        // A plan that writes nothing hands back what it was given; the engine stores only on a write.
        var buried = Is_Buried(existingMessageId, newestTopicMessage);

        TopicStatusPlan Planned(TopicStatusActions planned)
        {
            var seenAtBottomAfter = planned == TopicStatusActions.Repost || (planned != TopicStatusActions.None && !buried)
                ? substanceKey
                : seenAtBottomBefore;

            return new TopicStatusPlan(planned, text, renderKey, seenAtBottomAfter);
        }

        if (action == TopicStatusActions.None)
            return Planned(TopicStatusActions.None);

        if (action == TopicStatusActions.Post && mode == TelegramDeliveryModes.Silenced)
            return Planned(TopicStatusActions.None);

        // THE BACKOFF, last: a 429 answered at the tick rate inverts the cadence from once a minute
        // to thirty times a minute per topic and sustains the throttling that caused it.
        if (!Is_AttemptDue(lastFailedAttemptAt, now, backoffSeconds))
            return Planned(TopicStatusActions.None);

        return Planned(action);
    }

    /// <summary>
    /// The rendering as the repost question reads it: the text with its heartbeat stripped (the clock
    /// is not news — see Strip_Heartbeat) and the bar's labels, in the render key's own encoding.
    /// </summary>
    static string Build_SubstanceKey(string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows)
    {
        return TopicStatusLine_RenderKey.Build(TopicStatusLine_Builder.Strip_Heartbeat(text) ?? "", buttonRows);
    }

    /// <summary>
    /// Has later traffic landed below the status line? Ids, not counts: Telegram ids rise within a chat.
    /// UNKNOWN IS NOT BURIED — no line, or no traffic seen since a restart — for the reason
    /// <see cref="Is_RepostDue"/> gives. Equal is not buried: the newest message IS the line.
    /// </summary>
    public static bool Is_Buried(long? existingMessageId, TopicNewestMessage? newestTopicMessage)
    {
        return existingMessageId != null
            && newestTopicMessage != null
            && newestTopicMessage.Value.MessageId > existingMessageId.Value;
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
        // EQUAL is not buried: the newest message the app knows of IS the status line, so nothing came
        // after it. Only strictly-later ids bury it — see Is_Buried, the one spelling of that rule.
        if (!Is_Buried(existingMessageId, newestTopicMessage))
            return false;

        return now - newestTopicMessage!.Value.ArrivedAt >= TimeSpan.FromSeconds(quietSeconds);
    }

    public static bool Is_AttemptDue(DateTime? lastFailedAttemptAt, DateTime now, int backoffSeconds)
    {
        if (lastFailedAttemptAt == null)
            return true;

        return now - lastFailedAttemptAt.Value >= TimeSpan.FromSeconds(backoffSeconds);
    }
}

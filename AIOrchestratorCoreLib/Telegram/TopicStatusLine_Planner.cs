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
    /// A FOURTH VALUE, <c>SeenAtBottomKey</c>, lived here from ruling R27 (2026-09-24) until 2026-09-30: the
    /// rendering the owner last saw at the bottom, which the "buried AND changed" repost gate compared
    /// against. The owner's 2026-09-30 rule moves a buried line whether or not it changed, so nothing
    /// reads that memory any more and it went with the gate.
    /// </para>
    /// </summary>
    public readonly record struct TopicStatusPlan(TopicStatusActions Action, string Text, string RenderKey);

    /// <summary>
    /// The newest message the app knows of in a topic — the one fact burial is decided on.
    ///
    /// A RECORD RATHER THAN A LOOSE <c>long?</c>, and that is the M-G3 lesson applied preventively:
    /// `existingMessageId` and this id are both ids and mean opposite things, so as adjacent arguments
    /// they could be swapped at the one call site with everything still compiling — and the swap is
    /// invisible to the suite, because the engine is `internal sealed`. Swapped, the status line would
    /// be compared against itself and never move again. As a named type the compiler refuses it.
    ///
    /// <para>
    /// IT CARRIED AN <c>ArrivedAt</c> UNTIL 2026-09-30, the clock of the ten-second quiet window that
    /// every message reset. The owner's rule of that day times the move off the SESSION's last message
    /// alone (<see cref="TopicSessionSilence"/>), so an arrival time that anybody's message moved would
    /// only be a second, wrong clock for the same decision.
    /// </para>
    /// </summary>
    public readonly record struct TopicNewestMessage(long MessageId);

    /// <summary>
    /// When the SESSION last put a message in the topic, and "now" read off the SAME clock — the input
    /// the owner's rule of 2026-09-30 is decided on. <c>LastSessionMessageAtUtc</c> is null when the
    /// session has put nothing there since the app started counting.
    ///
    /// <para>
    /// BOTH STAMPS IN ONE VALUE, because a stamp from one clock compared against a reading of another
    /// is how the status line's back-off once went inert for every value it could be given (see
    /// <see cref="Is_AttemptDue"/>). The engine fills both from its injected <c>IClock</c> — which is
    /// also what lets an engine test move the minute instead of waiting it out. The planner's own
    /// <c>now</c> stays LOCAL for the durations it renders against agent-written stamps; this pair never
    /// meets it.
    /// </para>
    /// </summary>
    public readonly record struct TopicSessionSilence(DateTime? LastSessionMessageAtUtc, DateTime NowUtc);

    /// <summary>
    /// How old the SESSION's last message in the topic must be before a buried status line is moved
    /// back to the bottom — the owner's rule of 2026-09-30, in their words: *"it should be updated often
    /// so that is basically always the last message in the conversation. it should not get in the way
    /// of me speaking with the session, so it should be updated once the last session's message is at
    /// least 1 minute old. (not my last message, because to the session it quite often take a lot of
    /// time to reply)"*.
    ///
    /// <para>
    /// IT SUPERSEDES TWO EARLIER RULINGS, both of which left the line buried in the case the owner
    /// reported (PULSE twenty messages up, its buttons with it, so pressing one meant scrolling):
    /// </para>
    /// <list type="bullet">
    /// <item>the TEN-SECOND QUIET WINDOW (owner, 2026-08-24), which EVERY message reset — the owner's
    /// included, and the app's receipts and alerts — so a busy topic was never quiet for long enough;</item>
    /// <item>"BURIED AND CHANGED" (owner, 2026-09-09; ruling R27, 2026-09-24), under which a line whose
    /// content had not changed since the owner last saw it at the bottom stayed buried indefinitely.</item>
    /// </list>
    /// <para>
    /// WHAT KEEPS IT FROM BEING A WATERFALL is unchanged: every write here is silent, and a fresh post
    /// carries a higher id than the traffic that buried the old one, so the next tick reads it as the
    /// bottom and plans an edit (<see cref="Is_Buried"/>, strictly later ids only). One move per burial,
    /// at most.
    /// </para>
    /// </summary>
    public const int REPOST_AFTER_SESSION_QUIET_SECONDS = 60;

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

        // `pulse.unchangedFor`, resolved by the engine like the other `pulse.*` values (task 19). Null is
        // the builder's shipped default.
        bool? unchangedFor = null,

        // WHEN THE SESSION LAST SPOKE IN THE TOPIC (owner, 2026-09-30) — the one clock the move waits on.
        // Null means the same as a record with no message in it: the session has said nothing since the
        // app started counting, so nothing holds the move. The engine always passes it; a dropped
        // argument there would move the line in the middle of the session's reply, which the engine
        // facts in `PresetPhoneProbeTests` ("PULSE's cadence") are there to catch.
        TopicSessionSilence? sessionSilence = null)
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
        // answered Edit (one "not modified" edit per topic every 30 s in production).
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

        // THE MOVE — the owner's rule of 2026-09-30 (see REPOST_AFTER_SESSION_QUIET_SECONDS for their
        // words): a line BURIED by anything is moved back to the bottom once the SESSION's last message is
        // a minute old. It OVERRIDES the decider, as the repost did before 2026-09-09, because the point
        // is where the line is, not what it says: an unchanged PULSE twenty messages up is exactly the
        // complaint, its buttons out of reach.
        //
        // WHAT THIS REPLACED, said once so nobody re-derives it from older comments: from 2026-09-09
        // (owner: re-posted "only when it is buried by later traffic AND its content changed") to
        // 2026-09-30 the move also required the content to differ from what the owner last saw at the
        // bottom (ruling R27, heartbeat stripped, bar included), and until 2026-09-30 the topic had to
        // have been quiet for ten seconds by ANY message. Both are superseded; neither is checked here.
        //
        // WHAT STILL HOLDS IT: nothing to show (a blank line is never sent — the decider's emptiness rule,
        // re-checked because this overrides it), the latch (below), Silenced (below) and the back-off (at
        // the bottom).
        //
        // THE LATCH, and it is a fallback rather than a failure. Telegram REFUSES some deletes
        // permanently — a message past its 48-hour window, or a bot without `can_delete_messages` — and
        // a refusal is not a gone message, so nothing clears the id and the delete throws ahead of the
        // send on every tick. Latched, the topic stops trying to MOVE its line and goes on updating it
        // in place — master's behaviour, which is the right floor to degrade to.
        var action = !string.IsNullOrWhiteSpace(text)
                     && !repostIsImpossible
                     && Is_RepostDue(existingMessageId, newestTopicMessage, sessionSilence)
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
        // WHAT "OUT OF THE WAY UNDER SILENCED" MEANS EXACTLY: 🔕 refuses to PUT A MESSAGE THERE and
        // refuses to MOVE one. It does not stop the EDIT — an edit notifies nobody and appears nowhere
        // new, and a line left frozen for the length of a terminal session would be wrong on the owner's
        // next glance. So Silenced adds no message to the topic and Deferred keeps its line current AND
        // at the bottom.
        //
        // A BLOCKED REPOST FALLS BACK TO WHAT THE DECIDER SAID rather than to silence: the content
        // still updates in place and only the MOVE waits. Falling back to a blanket Edit instead
        // would rewrite identical text every tick, which is the wasted-call spin the identical-text
        // rule exists to stop.
        if (action == TopicStatusActions.Repost && mode == TelegramDeliveryModes.Silenced)
            action = decided;

        if (action == TopicStatusActions.None)
            return new TopicStatusPlan(TopicStatusActions.None, text, renderKey);

        if (action == TopicStatusActions.Post && mode == TelegramDeliveryModes.Silenced)
            return new TopicStatusPlan(TopicStatusActions.None, text, renderKey);

        // THE BACKOFF, last: a 429 answered at the tick rate inverts the cadence from once a minute
        // to thirty times a minute per topic and sustains the throttling that caused it.
        if (!Is_AttemptDue(lastFailedAttemptAt, now, backoffSeconds))
            return new TopicStatusPlan(TopicStatusActions.None, text, renderKey);

        return new TopicStatusPlan(action, text, renderKey);
    }

    /// <summary>
    /// Has later traffic landed below the status line? Ids, not counts: Telegram ids rise within a chat.
    /// UNKNOWN IS NOT BURIED — no line, or no traffic known — and the planner does not guess: after a
    /// restart it is the engine's traffic record that decides to assume burial once
    /// (<c>ITopicTraffic.Find_Newest_OrAssumeBuried</c>), where the missing knowledge is.
    ///
    /// <para>
    /// EQUAL IS NOT BURIED, and since 2026-09-30 that is load-bearing: every message the app sends is
    /// recorded as topic traffic, the status line's own post included, so right after a post or a
    /// repost the newest message the app knows of IS the line. Only a strictly later id buries it —
    /// a `&gt;=` here would read every fresh line as buried by itself and move it on every tick.
    /// </para>
    /// </summary>
    public static bool Is_Buried(long? existingMessageId, TopicNewestMessage? newestTopicMessage)
    {
        return existingMessageId != null
            && newestTopicMessage != null
            && newestTopicMessage.Value.MessageId > existingMessageId.Value;
    }

    /// <summary>
    /// Has the SESSION been quiet in the topic for the owner's minute (2026-09-30)? No record, or a record
    /// with no message in it, is a session that has said nothing since the app started counting — quiet.
    ///
    /// A message stamped in the FUTURE (a clock step, nothing else can do it) yields a negative elapsed
    /// and holds the move, which is the safe direction: the line stays where it is and is edited.
    /// </summary>
    public static bool Is_SessionQuietLongEnough(TopicSessionSilence? sessionSilence)
    {
        if (sessionSilence?.LastSessionMessageAtUtc == null)
            return true;

        var silence = sessionSilence.Value;

        return silence.NowUtc - silence.LastSessionMessageAtUtc!.Value >= TimeSpan.FromSeconds(REPOST_AFTER_SESSION_QUIET_SECONDS);
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
    public static bool Is_AttemptDue(DateTime? lastFailedAttemptAt, DateTime now, int backoffSeconds)
    {
        if (lastFailedAttemptAt == null)
            return true;

        return now - lastFailedAttemptAt.Value >= TimeSpan.FromSeconds(backoffSeconds);
    }

    /// <summary>
    /// Is the status line due to be MOVED back to the bottom — the owner's rule of 2026-09-30? Buried by
    /// ANY later message (the owner's, the app's, the session's: every one of them pushes the line and
    /// its buttons up), AND the SESSION's last message at least <see cref="REPOST_AFTER_SESSION_QUIET_SECONDS"/>
    /// old, so the move never lands between two messages of a reply still being written.
    ///
    /// <para>
    /// IT ASKED "HAS THE TOPIC GONE QUIET" UNTIL 2026-09-30 — ten seconds since the newest message of
    /// any author. The owner's words on why that was the wrong clock: *"not my last message, because to
    /// the session it quite often take a lot of time to reply"*. Their own message and the app's
    /// receipts, alerts and narration now BURY the line without holding it.
    /// </para>
    /// </summary>
    public static bool Is_RepostDue(long? existingMessageId, TopicNewestMessage? newestTopicMessage, TopicSessionSilence? sessionSilence)
    {
        // EQUAL is not buried: the newest message the app knows of IS the status line, so nothing came
        // after it. Only strictly-later ids bury it — see Is_Buried, the one spelling of that rule.
        return Is_Buried(existingMessageId, newestTopicMessage) && Is_SessionQuietLongEnough(sessionSilence);
    }
}

using AIOrchestratorCoreLib.Planning.PlanProgress;
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

    public static TopicStatusPlan Plan(
        string title,
        IPlanProgress? progress,
        IReadOnlyList<ITopicStatusMember> members,
        string? lastSubject,
        DateTime now,
        long? existingMessageId,
        string? lastWrittenText,
        TelegramDeliveryModes mode,
        DateTime? lastFailedAttemptAt,
        int backoffSeconds)
    {
        // The id decides what "nothing to say" means, and it is passed rather than a flag derived at
        // the call site — that derivation was mutable to `false` with nothing reddening.
        var text = TopicStatusLine_Builder.Build(title, progress, members, lastSubject, now, existingMessageId != null);

        var action = TopicStatusLine_Decider.Decide(text, lastWrittenText, existingMessageId);

        if (action == TopicStatusActions.None)
            return new TopicStatusPlan(TopicStatusActions.None, text);

        // THE DELIVERY GATE IS ON THE POST ONLY. An edit notifies nobody, so silencing it buys
        // nothing and costs a line frozen at pre-DND content for the whole period; a POST does
        // notify, and a topic the owner silenced must not be the thing that pushes to their phone.
        if (action == TopicStatusActions.Post && mode != TelegramDeliveryModes.Normal)
            return new TopicStatusPlan(TopicStatusActions.None, text);

        // THE BACKOFF, last: a 429 answered at the tick rate inverts the cadence from once a minute
        // to thirty times a minute per topic and sustains the throttling that caused it.
        if (!Is_AttemptDue(lastFailedAttemptAt, now, backoffSeconds))
            return new TopicStatusPlan(TopicStatusActions.None, text);

        return new TopicStatusPlan(action, text);
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
    /// </summary>
    public static bool Is_AttemptDue(DateTime? lastFailedAttemptAt, DateTime now, int backoffSeconds)
    {
        if (lastFailedAttemptAt == null)
            return true;

        return now - lastFailedAttemptAt.Value >= TimeSpan.FromSeconds(backoffSeconds);
    }
}

using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Bridge.TopicDeletion;

/// <summary>
/// WHAT A DELETE ATTEMPT TOLD US, AND WHEN TO TRY AGAIN — the two decisions the topic delete makes,
/// as pure functions.
///
/// <para>
/// WHY IT EXISTS (audit 2026-09-09, brief E1). Closing an orchestration asks Telegram to DELETE its
/// topic, and the ask was <c>Delete_TelegramTopic_FireAndForget</c>: one call, no retry, a failure
/// swallowed into a log line, and nothing written down anywhere. A delete that failed therefore left
/// an orphan topic on the owner's phone with no record that it should not be there — and the owner's
/// decision, recorded in the same brief, is that topics ARE deleted (they will have thousands, and a
/// list full of finished ones is noise). A delete nobody retries is that decision not being kept.
/// </para>
/// <para>
/// HERE RATHER THAN IN THE ENGINE, for the reason <see cref="TelegramAttempt_Gate"/> gives in full:
/// <c>BridgeEngineModel</c> is <c>internal sealed</c> with no <c>InternalsVisibleTo</c>, so a rule
/// written inside it cannot be asserted by the suite. The decision moves out; the I/O stays where it
/// is. What remains unpinned is the one-line call from the engine, which the engine-level probe in
/// <c>ClosingATopicReallyDeletesItTests</c> covers end to end.
/// </para>
/// </summary>
public static class TopicDelete_Decider
{
    /// <summary>
    /// How many attempts one process makes before leaving the delete PENDING for the next start.
    ///
    /// <para>
    /// Bounded, and the bound is the point. An unbounded retry on a detached task is the shape of
    /// every runaway this file's neighbours have already paid for; and it is unnecessary here,
    /// because the reconciliation sweep gives the attempt somewhere to go. Four attempts spread over
    /// the delays below cover a rate limit and a short outage; anything longer is not a burst, and a
    /// restart will pick it up.
    /// </para>
    /// </summary>
    public const int MAXIMUM_ATTEMPTS = 4;

    /// <summary>
    /// The gap before attempt <c>n</c> (1-based, so <c>Build_BackoffDelay(1)</c> is the wait before
    /// the SECOND attempt). Exponential from two seconds, capped — Telegram's own
    /// <c>retry_after</c> beats it whenever the answer carried one.
    /// </summary>
    public static readonly TimeSpan FIRST_BACKOFF = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest this waits between attempts. The whole retry runs on a detached task after an
    /// orchestration is already closed, so nothing is waiting on it — but it must still end, and the
    /// four attempts must fit comfortably inside the time a person leaves the app running after
    /// pressing close.
    /// </summary>
    public static readonly TimeSpan MAXIMUM_BACKOFF = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Success is the absence of an exception; everything else is classified from the TYPE and the
    /// STATUS, never from the sentence — with the one documented exception this codebase already
    /// makes for Telegram's machine-readable descriptions (see <see cref="TelegramApiException"/>).
    ///
    /// <para>
    /// A NON-TELEGRAM EXCEPTION IS "UNKNOWN", NOT "REFUSED", and the asymmetry is deliberate. Reading
    /// a bug or a vanished client as permanent leaves an orphan topic for ever; reading a genuine
    /// refusal as unknown costs one API call per app start. The cheap mistake is the one to make.
    /// A real permission failure is always a <see cref="TelegramApiException"/>, so it still lands on
    /// <see cref="TopicDeleteOutcomes.Refused"/> and still tells the owner.
    /// </para>
    /// </summary>
    public static TopicDeleteOutcomes Classify(Exception? failure)
    {
        if (failure == null)
            return TopicDeleteOutcomes.Deleted;

        if (failure is TelegramApiException answered)
        {
            // ASKED BEFORE THE STATUS, because Telegram answers this 400 — the same status it uses
            // for a refusal — and the two mean opposite things. "The thread is not there" is the
            // state we were trying to reach.
            if (Says_TopicAlreadyGone(answered.Message))
                return TopicDeleteOutcomes.AlreadyGone;

            if (answered.Is_Retryable)
                return TopicDeleteOutcomes.OutcomeUnknown;

            return TopicDeleteOutcomes.Refused;
        }

        // Telegram never answered: an HttpClient timeout arrives as a TaskCanceledException, and a
        // dropped connection, DNS failure or TLS error as an HttpRequestException. Neither says
        // anything about whether the delete took effect.
        return TopicDeleteOutcomes.OutcomeUnknown;
    }

    /// <summary>
    /// Telegram's wordings for "there is no such topic". All of them are 400s, so the status alone
    /// cannot separate them from a refusal — this is the same narrow, documented exception to the
    /// no-string-parsing rule that <see cref="TelegramAttempt_Gate"/> makes for <c>TOPIC_NOT_MODIFIED</c>
    /// and <see cref="TopicStatusLine_Decider.Is_MessageGone"/> makes for a deleted message.
    /// </summary>
    static bool Says_TopicAlreadyGone(string errorMessage)
    {
        return errorMessage.Contains("TOPIC_DELETED", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("TOPIC_ID_INVALID", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("message thread not found", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("topic to delete not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the topic is gone, by either route — the only two outcomes that end the retry happily.</summary>
    public static bool Is_Settled(TopicDeleteOutcomes outcome)
    {
        return outcome is TopicDeleteOutcomes.Deleted or TopicDeleteOutcomes.AlreadyGone;
    }

    /// <summary>
    /// Whether another attempt is worth making NOW, inside this process.
    ///
    /// <para>
    /// Only an unknown outcome earns one. A refusal will not change while the process runs, so
    /// retrying it here is pure cost — the recovery for a refusal is the next start, after whoever
    /// owns the bot's rights has changed them.
    /// </para>
    /// </summary>
    public static bool Should_RetryNow(TopicDeleteOutcomes outcome, int attemptsMade, int maximumAttempts = MAXIMUM_ATTEMPTS)
    {
        return outcome == TopicDeleteOutcomes.OutcomeUnknown && attemptsMade < maximumAttempts;
    }

    /// <summary>
    /// How long to wait before attempt number <paramref name="attemptsMade"/> + 1.
    ///
    /// <para>
    /// TELEGRAM'S NUMBER BEATS OURS whenever it gave one, exactly as everywhere else in this app —
    /// clamped by <see cref="TokenBucket_Gate.Read_RetryAfter"/> so a malformed or hostile
    /// <c>retry_after</c> cannot park a detached task for a day. Our own schedule is the fallback:
    /// 2 s, 4 s, 8 s, … capped at <see cref="MAXIMUM_BACKOFF"/>.
    /// </para>
    /// </summary>
    public static TimeSpan Build_BackoffDelay(int attemptsMade, int? retryAfterSeconds = null)
    {
        var advised = TokenBucket_Gate.Read_RetryAfter(retryAfterSeconds);

        if (advised > TimeSpan.Zero)
            return advised < MAXIMUM_BACKOFF ? advised : MAXIMUM_BACKOFF;

        // Guarded rather than trusted: a negative or absurd attempt count must not shift a doubling
        // into an overflow, and the caller is a loop counter that has been wrong before elsewhere.
        var doublings = Math.Clamp(attemptsMade - 1, 0, 16);
        var seconds = FIRST_BACKOFF.TotalSeconds * Math.Pow(2, doublings);

        return seconds >= MAXIMUM_BACKOFF.TotalSeconds ? MAXIMUM_BACKOFF : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Whether to TELL THE OWNER, in General, that a topic will not go away.
    ///
    /// <para>
    /// ONCE PER ORCHESTRATION, EVER — <paramref name="alreadyReported"/> is persisted in session.json
    /// precisely so a restart cannot repeat it. The sweep runs at every start and a refusal survives
    /// restarts by definition, so without the flag this alert would be a waterfall: the exact shape
    /// CLAUDE.md decision 14 and 15 exist to prevent, on an item the owner can act on exactly once
    /// (give the bot back its rights).
    /// </para>
    /// <para>
    /// AND ONLY FOR A REFUSAL. An unknown outcome is not a failure the owner can do anything about;
    /// it goes to the log and to the next start's sweep.
    /// </para>
    /// </summary>
    public static bool Should_ReportToOwner(TopicDeleteOutcomes outcome, bool alreadyReported)
    {
        return outcome == TopicDeleteOutcomes.Refused && !alreadyReported;
    }
}

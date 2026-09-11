using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE EXPIRY IS THE BEHAVIOUR THE WHOLE CHANGE BUYS, so it is asserted in both directions: an unknown
/// outcome must suppress retries FOR A WHILE and then RETRY. Either half alone is satisfied by a wrong
/// answer — "never retry" satisfies the first, "always retry" satisfies the second — which is why
/// neither is asserted on its own here.
///
/// These pin the DECISIONS. The one-line call from `BridgeEngineModel` into them is NOT pinned and
/// cannot be: the engine is `internal sealed` with no `InternalsVisibleTo`. A green run here says the
/// rules are right, not that they are wired up.
/// </summary>
public class TelegramAttemptGateTests
{
    static readonly DateTime NOW = new(2026, 8, 14, 16, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A TIMEOUT SAYS NOTHING ABOUT THE NAME. `HttpClient.Timeout` throws `TaskCanceledException`, which
    /// IS an `OperationCanceledException` — the same conflation that made a timeout read as a shutdown
    /// one layer up.
    /// </summary>
    [Fact]
    public void ATimeoutIsAnUnknownOutcome()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.OutcomeUnknown,
            TelegramAttempt_Gate.Classify_Failure(new TaskCanceledException("simulated HttpClient timeout")));
    }

    /// <summary>
    /// THE CASE THAT MOTIVATED WIDENING THE PREDICATE. A dropped connection reads as "we do not know",
    /// not as "Telegram refused" — the two-bucket version got this wrong and recorded a Wi-Fi drop as a
    /// successfully applied name.
    /// </summary>
    [Fact]
    public void ADroppedConnectionIsAnUnknownOutcome()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.OutcomeUnknown,
            TelegramAttempt_Gate.Classify_Failure(new HttpRequestException("connection refused")));
    }

    /// <summary>
    /// AND THE OTHER SIDE, asserted apart so the classifier cannot pass by calling everything unknown —
    /// which would reinstate the spin the done-flag write exists to stop.
    /// </summary>
    [Fact]
    public void ARefusalFromTelegramIsRejectedRatherThanUnknown()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.Rejected,
            TelegramAttempt_Gate.Classify_Failure(new Exception("Telegram 'editForumTopic' failed with HTTP 400: bad request")));
    }

    /// <summary>
    /// A RATE LIMIT IS "WE DO NOT KNOW", and it is the case that made the typed exception worth taking:
    /// the app edits topic names every tick, so a 429 is ordinary. Before the status code survived to
    /// here, one of them recorded a name as applied for the life of the process.
    /// </summary>
    [Fact]
    public void ARateLimitIsAnUnknownOutcome()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.OutcomeUnknown,
            TelegramAttempt_Gate.Classify_Failure(new TelegramApiException(429, "Too Many Requests")));
    }

    /// <summary>Telegram failing on its own side says nothing about whether the edit took effect.</summary>
    [Fact]
    public void AServerErrorIsAnUnknownOutcome()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.OutcomeUnknown,
            TelegramAttempt_Gate.Classify_Failure(new TelegramApiException(503, "Service Unavailable")));
    }

    /// <summary>
    /// AND THE OTHER SIDE OF THE STATUS TEST, asserted apart. A 400 is a refusal on the merits: it will
    /// not become valid by waiting, so it must NOT be stamped and retried. Without this case the
    /// classifier could pass by calling every answered failure unknown, which would turn a permanent
    /// refusal into a retry every 30 seconds for ever.
    /// </summary>
    [Fact]
    public void ABadRequestIsRejectedRatherThanUnknown()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.Rejected,
            TelegramAttempt_Gate.Classify_Failure(new TelegramApiException(400, "Bad Request: TOPIC_NAME_INVALID")));
    }

    /// <summary>
    /// THE CASE THE ENUM DOCUMENTED AND THE CLASSIFIER NEVER RETURNED. `Applied` described
    /// TOPIC_NOT_MODIFIED from the day it was written, while the only code that recognised it was a
    /// private filter inside the engine wired to ONE of the two topic-name syncs. The other one — the
    /// General topic — logged "Could not rename the General topic" on every tick from app start,
    /// because the memo that stops the retry was only ever written on the success path.
    ///
    /// The message is the one Telegram actually sent, copied from the owner's activity log, so this
    /// pins the classification against the real response body rather than against a tidied version.
    /// </summary>
    [Fact]
    public void TopicNotModifiedIsAppliedRatherThanAFailure()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.Applied,
            TelegramAttempt_Gate.Classify_Failure(new TelegramApiException(
                400,
                "Telegram 'editGeneralForumTopic' failed with HTTP 400: {\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: TOPIC_NOT_MODIFIED\"}")));
    }

    /// <summary>
    /// AND THROUGH THE UNTYPED PATH TOO. Not every failure reaching this classifier is a
    /// <see cref="TelegramApiException"/> — the engine hands it whatever the client threw, and a plain
    /// <see cref="Exception"/> carrying the same slug must classify the same way. Asserted apart from
    /// the typed case so a fix applied to only one branch cannot pass.
    /// </summary>
    [Fact]
    public void TopicNotModifiedIsAppliedEvenWithoutTheStatusCode()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.Applied,
            TelegramAttempt_Gate.Classify_Failure(new Exception("Bad Request: TOPIC_NOT_MODIFIED")));
    }

    /// <summary>
    /// THE GUARD ON THE NEW BRANCH, and the one a regression would take. `Applied` is the outcome that
    /// suppresses retries FOR EVER, so it must never be reachable from a failure where Telegram did not
    /// answer at all: a transport failure says the round trip did not complete, never that the name
    /// already holds. Without this the slug test could be hoisted above the transport test — which reads
    /// like a harmless simplification and would turn a dropped connection into a permanently stale topic
    /// name, the exact defect `OutcomeUnknown` was introduced to end.
    /// </summary>
    [Fact]
    public void ATransportFailureIsNeverReadAsAlreadyNamed()
    {
        Assert.Equal(
            TopicNameAttemptOutcomes.OutcomeUnknown,
            TelegramAttempt_Gate.Classify_Failure(new HttpRequestException("connection reset — TOPIC_NOT_MODIFIED")));
    }

    /// <summary>Nothing holding it back is the ordinary case and must not need a stamp to proceed.</summary>
    [Fact]
    public void WithNoStampAnAttemptIsAlwaysDue()
    {
        Assert.True(TelegramAttempt_Gate.Is_AttemptDue(null, NOW));
    }

    /// <summary>
    /// THE SUPPRESSION HALF. Inside the window the attempt is not due — without this the unknown outcome
    /// retries at tick rate, which is the 28-errors-in-minutes spin.
    /// </summary>
    [Fact]
    public void InsideTheWindowAnAttemptIsNotDue()
    {
        var retryAfter = TelegramAttempt_Gate.Build_RetryAfterUtc(NOW, 30);

        Assert.False(TelegramAttempt_Gate.Is_AttemptDue(retryAfter, NOW.AddSeconds(29)));
    }

    /// <summary>
    /// THE RETRY HALF, AND IT IS THE ONE A REGRESSION WOULD TAKE. Suppressing for ever is the failure
    /// mode that looks identical to working — the topic simply never updates again — so the clock is
    /// walked PAST the stamp and the attempt must come back.
    /// </summary>
    [Fact]
    public void OnceTheWindowHasPassedTheAttemptIsDueAgain()
    {
        var retryAfter = TelegramAttempt_Gate.Build_RetryAfterUtc(NOW, 30);

        Assert.True(TelegramAttempt_Gate.Is_AttemptDue(retryAfter, NOW.AddSeconds(31)));
    }

    /// <summary>
    /// EXACTLY AT THE DEADLINE IT IS DUE. A gate that is not due at its own instant has a duration
    /// silently longer than the one it advertises, and the next reader would be measuring a window that
    /// is really 30 seconds plus one tick.
    /// </summary>
    [Fact]
    public void AtTheDeadlineItselfTheAttemptIsDue()
    {
        var retryAfter = TelegramAttempt_Gate.Build_RetryAfterUtc(NOW, 30);

        Assert.True(TelegramAttempt_Gate.Is_AttemptDue(retryAfter, NOW.AddSeconds(30)));
    }
}

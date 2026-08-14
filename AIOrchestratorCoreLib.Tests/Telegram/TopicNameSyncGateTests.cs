using AIOrchestratorCoreLib.Telegram;
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
public class TopicNameSyncGateTests
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
            TopicNameSync_Gate.Classify_Failure(new TaskCanceledException("simulated HttpClient timeout")));
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
            TopicNameSync_Gate.Classify_Failure(new HttpRequestException("connection refused")));
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
            TopicNameSync_Gate.Classify_Failure(new Exception("Telegram 'editForumTopic' failed with HTTP 400: bad request")));
    }

    /// <summary>Nothing holding it back is the ordinary case and must not need a stamp to proceed.</summary>
    [Fact]
    public void WithNoStampAnAttemptIsAlwaysDue()
    {
        Assert.True(TopicNameSync_Gate.Is_AttemptDue(null, NOW));
    }

    /// <summary>
    /// THE SUPPRESSION HALF. Inside the window the attempt is not due — without this the unknown outcome
    /// retries at tick rate, which is the 28-errors-in-minutes spin.
    /// </summary>
    [Fact]
    public void InsideTheWindowAnAttemptIsNotDue()
    {
        var retryAfter = TopicNameSync_Gate.Build_RetryAfterUtc(NOW, 30);

        Assert.False(TopicNameSync_Gate.Is_AttemptDue(retryAfter, NOW.AddSeconds(29)));
    }

    /// <summary>
    /// THE RETRY HALF, AND IT IS THE ONE A REGRESSION WOULD TAKE. Suppressing for ever is the failure
    /// mode that looks identical to working — the topic simply never updates again — so the clock is
    /// walked PAST the stamp and the attempt must come back.
    /// </summary>
    [Fact]
    public void OnceTheWindowHasPassedTheAttemptIsDueAgain()
    {
        var retryAfter = TopicNameSync_Gate.Build_RetryAfterUtc(NOW, 30);

        Assert.True(TopicNameSync_Gate.Is_AttemptDue(retryAfter, NOW.AddSeconds(31)));
    }

    /// <summary>
    /// EXACTLY AT THE DEADLINE IT IS DUE. A gate that is not due at its own instant has a duration
    /// silently longer than the one it advertises, and the next reader would be measuring a window that
    /// is really 30 seconds plus one tick.
    /// </summary>
    [Fact]
    public void AtTheDeadlineItselfTheAttemptIsDue()
    {
        var retryAfter = TopicNameSync_Gate.Build_RetryAfterUtc(NOW, 30);

        Assert.True(TopicNameSync_Gate.Is_AttemptDue(retryAfter, NOW.AddSeconds(30)));
    }
}

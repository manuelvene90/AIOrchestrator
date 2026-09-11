using AIOrchestratorCoreLib.Bridge.TopicDeletion;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The four answers a delete attempt can give, and what each one buys — brief E1's decision half.
///
/// The case that matters most is the pair that share a status: Telegram answers BOTH "you may not
/// delete this" and "there is no such thread" with a 400, and reading the second as the first would
/// retry a topic that is already gone at every app start for ever.
/// </summary>
public class TopicDeleteDeciderTests
{
    [Fact]
    public void Classify_WithNoFailure_IsDeleted()
    {
        Assert.Equal(TopicDeleteOutcomes.Deleted, TopicDelete_Decider.Classify(null));
    }

    [Theory]
    [InlineData("Telegram 'deleteForumTopic' failed with HTTP 400: {\"description\":\"Bad Request: TOPIC_DELETED\"}")]
    [InlineData("Telegram 'deleteForumTopic' failed with HTTP 400: {\"description\":\"Bad Request: message thread not found\"}")]
    [InlineData("Telegram 'deleteForumTopic' failed with HTTP 400: {\"description\":\"Bad Request: topic to delete not found\"}")]
    [InlineData("Telegram 'deleteForumTopic' failed with HTTP 400: {\"description\":\"Bad Request: TOPIC_ID_INVALID\"}")]
    public void Classify_WhenTheTopicIsNotThere_IsAlreadyGone_AndSettles(string message)
    {
        var outcome = TopicDelete_Decider.Classify(new TelegramApiException(400, message));

        Assert.Equal(TopicDeleteOutcomes.AlreadyGone, outcome);
        Assert.True(TopicDelete_Decider.Is_Settled(outcome), "a topic that is not there is the state the delete was aiming at");
    }

    /// <summary>
    /// The permission failure the owner has to be told about — and the ONLY outcome that tells them.
    /// Same status as the case above, which is why the description is read before the status.
    /// </summary>
    [Theory]
    [InlineData(400, "Telegram 'deleteForumTopic' failed with HTTP 400: {\"description\":\"Bad Request: not enough rights to manage topics\"}")]
    [InlineData(403, "Telegram 'deleteForumTopic' failed with HTTP 403: {\"description\":\"Forbidden: bot is not a member of the supergroup chat\"}")]
    public void Classify_WhenTelegramRefusesOnTheMerits_IsRefused(int statusCode, string message)
    {
        var outcome = TopicDelete_Decider.Classify(new TelegramApiException(statusCode, message));

        Assert.Equal(TopicDeleteOutcomes.Refused, outcome);
        Assert.False(TopicDelete_Decider.Is_Settled(outcome));
        Assert.False(TopicDelete_Decider.Should_RetryNow(outcome, attemptsMade: 1), "a refusal will not change while this process runs");
        Assert.True(TopicDelete_Decider.Should_ReportToOwner(outcome, alreadyReported: false));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    public void Classify_WithARetryableStatus_IsOutcomeUnknown(int statusCode)
    {
        Assert.Equal(
            TopicDeleteOutcomes.OutcomeUnknown,
            TopicDelete_Decider.Classify(new TelegramApiException(statusCode, $"HTTP {statusCode}")));
    }

    /// <summary>
    /// Telegram never answered, so it cannot have refused. Both of these arrive from HttpClient — a
    /// timeout as a TaskCanceledException, a dropped connection as an HttpRequestException.
    /// </summary>
    [Fact]
    public void Classify_WhenTelegramNeverAnswered_IsOutcomeUnknown()
    {
        Assert.Equal(TopicDeleteOutcomes.OutcomeUnknown, TopicDelete_Decider.Classify(new TaskCanceledException("timed out")));
        Assert.Equal(TopicDeleteOutcomes.OutcomeUnknown, TopicDelete_Decider.Classify(new HttpRequestException("connection reset")));
    }

    /// <summary>
    /// THE ASYMMETRY, ASSERTED. A bug or a vanished client read as "refused" strands the topic for
    /// ever; read as "unknown" it costs one API call at the next start. The cheap mistake is the one
    /// this must make — and the expensive one is exactly what a `_ => Refused` default would do.
    /// </summary>
    [Fact]
    public void Classify_WithAnExceptionThatIsNotTelegrams_IsOutcomeUnknown_NotRefused()
    {
        var outcome = TopicDelete_Decider.Classify(new Exception("Telegram client vanished while deleting topic 42"));

        Assert.Equal(TopicDeleteOutcomes.OutcomeUnknown, outcome);
        Assert.False(TopicDelete_Decider.Should_ReportToOwner(outcome, alreadyReported: false), "an unknown outcome is not something the owner can act on");
    }

    [Fact]
    public void Should_RetryNow_StopsAtTheAttemptCap()
    {
        Assert.True(TopicDelete_Decider.Should_RetryNow(TopicDeleteOutcomes.OutcomeUnknown, attemptsMade: TopicDelete_Decider.MAXIMUM_ATTEMPTS - 1));
        Assert.False(TopicDelete_Decider.Should_RetryNow(TopicDeleteOutcomes.OutcomeUnknown, attemptsMade: TopicDelete_Decider.MAXIMUM_ATTEMPTS));
    }

    [Fact]
    public void Should_ReportToOwner_OnlyOnce()
    {
        Assert.True(TopicDelete_Decider.Should_ReportToOwner(TopicDeleteOutcomes.Refused, alreadyReported: false));
        Assert.False(TopicDelete_Decider.Should_ReportToOwner(TopicDeleteOutcomes.Refused, alreadyReported: true));
    }

    [Fact]
    public void Build_BackoffDelay_DoublesFromTheFirstGap_AndIsCapped()
    {
        Assert.Equal(TopicDelete_Decider.FIRST_BACKOFF, TopicDelete_Decider.Build_BackoffDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(TopicDelete_Decider.FIRST_BACKOFF.TotalSeconds * 2), TopicDelete_Decider.Build_BackoffDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(TopicDelete_Decider.FIRST_BACKOFF.TotalSeconds * 4), TopicDelete_Decider.Build_BackoffDelay(3));
        Assert.Equal(TopicDelete_Decider.MAXIMUM_BACKOFF, TopicDelete_Decider.Build_BackoffDelay(99));
    }

    /// <summary>Telegram's own number beats ours — the rule every other retry in this app follows.</summary>
    [Fact]
    public void Build_BackoffDelay_HonoursRetryAfter_ClampedToTheCeiling()
    {
        Assert.Equal(TimeSpan.FromSeconds(7), TopicDelete_Decider.Build_BackoffDelay(1, retryAfterSeconds: 7));

        // Above our own ceiling it is clamped rather than obeyed: a detached task must still end.
        Assert.Equal(TopicDelete_Decider.MAXIMUM_BACKOFF, TopicDelete_Decider.Build_BackoffDelay(1, retryAfterSeconds: 600));

        // And a hostile value cannot outrun the shared clamp either.
        Assert.True(TopicDelete_Decider.Build_BackoffDelay(1, retryAfterSeconds: int.MaxValue) <= TopicDelete_Decider.MAXIMUM_BACKOFF);
        Assert.True(TokenBucket_Gate.MAXIMUM_HONOURED_RETRY_AFTER_SECONDS > 0);
    }

    /// <summary>A nonsensical attempt count must not shift a doubling into an overflow.</summary>
    [Fact]
    public void Build_BackoffDelay_WithANonsenseAttemptCount_StaysInsideTheBounds()
    {
        foreach (var attempts in new[] { int.MinValue, -1, 0, int.MaxValue })
        {
            var delay = TopicDelete_Decider.Build_BackoffDelay(attempts);

            Assert.True(delay >= TimeSpan.Zero, $"attempt {attempts} produced a negative delay");
            Assert.True(delay <= TopicDelete_Decider.MAXIMUM_BACKOFF, $"attempt {attempts} produced {delay}");
        }
    }
}

using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// Every case here drives <see cref="TokenBucket_Gate.Take"/> with explicit DateTime values and
/// advances a virtual clock by whatever Wait the call itself returns — never Task.Delay, never
/// DateTime.UtcNow. The rule is pure specifically so it can be tested at any speed; a test that used
/// a real clock would have thrown that property away.
/// </summary>
public class TokenBucketGateTests
{
    static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AFullBucket_HandsOutATokenImmediately_AndDecrements()
    {
        var (tokens, lastRefillUtc, wait) = TokenBucket_Gate.Take(
            tokens: TokenBucket_Gate.DEFAULT_CAPACITY,
            lastRefillUtc: T0,
            nowUtc: T0);

        Assert.Equal(TimeSpan.Zero, wait);
        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY - 1, tokens);
        Assert.Equal(T0, lastRefillUtc);
    }

    /// <summary>
    /// THE ACCEPTANCE PROPERTY. A naive limiter that just drops a request when the bucket is empty
    /// would pass a "never exceeds capacity" check trivially — the interesting half is that nothing
    /// is LOST: every one of 25 requests, more than the 18-token capacity, must eventually get a
    /// token by waiting, and at no point does any 60-second span hand out more than capacity's worth.
    /// This threads (tokens, lastRefillUtc) through repeated calls exactly as the real caller would,
    /// advancing the virtual clock by each returned Wait.
    /// </summary>
    [Fact]
    public void TwentyFiveRequests_AllEventuallyGetAToken_AndNoSixtySecondWindowExceedsCapacity()
    {
        var tokens = TokenBucket_Gate.DEFAULT_CAPACITY;
        var lastRefillUtc = T0;
        var now = T0;
        var grantTimes = new List<DateTime>();

        for (var i = 0; i < 25; i++)
        {
            while (true)
            {
                var result = TokenBucket_Gate.Take(tokens, lastRefillUtc, now);
                tokens = result.Tokens;
                lastRefillUtc = result.LastRefillUtc;

                if (result.Wait == TimeSpan.Zero)
                {
                    grantTimes.Add(now);
                    break;
                }

                now += result.Wait;
            }
        }

        // None lost: the loop above only ever leaves via a grant, so 25 calls in produces 25 grants
        // out — but state it as an explicit assertion so a future change that could spin forever
        // still gets caught by a count, not by a hang.
        Assert.Equal(25, grantTimes.Count);

        var window = TimeSpan.FromSeconds(TokenBucket_Gate.DEFAULT_REFILL_SECONDS);

        foreach (var windowStart in grantTimes)
        {
            var windowEnd = windowStart + window;
            var grantedInWindow = grantTimes.Count(t => t >= windowStart && t < windowEnd);

            // AGAINST THE CEILING, NOT AGAINST THE CAPACITY, and the difference is the property.
            // A bucket's worst case in a rolling window is capacity PLUS whatever refilled during
            // it, so asserting "never more than capacity" would be asserting something no token
            // bucket has ever done. What has to hold is Telegram's own limit — which is why the
            // capacity is derived from it rather than chosen, and why this assertion caught a
            // capacity that allowed 35 messages a minute against a ceiling of 20.
            Assert.True(
                grantedInWindow <= TokenBucket_Gate.TELEGRAM_GROUP_MESSAGES_PER_MINUTE,
                $"{grantedInWindow} tokens granted within 60s of {windowStart:o} — Telegram's ceiling is {TokenBucket_Gate.TELEGRAM_GROUP_MESSAGES_PER_MINUTE}");
        }
    }

    /// <summary>
    /// The returned Wait must be honest: if it under-promised, a caller sleeping exactly that long
    /// would ask again and still be refused, which is the busy-loop this whole design exists to
    /// avoid. Capacity/refill are chosen here (1 token per 60s, rather than the 18/60 default) so
    /// the arithmetic lands on a whole number of seconds — TimeSpan.FromSeconds rounds to the
    /// nearest millisecond, and a fractional-second answer would make this assertion flaky for a
    /// reason that has nothing to do with the property being pinned.
    /// </summary>
    [Fact]
    public void AnEmptyBucket_ReturnsAPositiveWait_AndATokenIsAvailableAfterWaitingThatLong()
    {
        var first = TokenBucket_Gate.Take(tokens: 0, lastRefillUtc: T0, nowUtc: T0, capacity: 1, refillSeconds: 60);

        Assert.True(first.Wait > TimeSpan.Zero);

        var later = T0 + first.Wait;
        var second = TokenBucket_Gate.Take(first.Tokens, first.LastRefillUtc, later, capacity: 1, refillSeconds: 60);

        Assert.Equal(TimeSpan.Zero, second.Wait);
    }

    /// <summary>
    /// An NTP correction or a laptop waking can move the clock backwards. That must not manufacture
    /// tokens out of a negative elapsed time, and must not wedge the bucket forever either — the next
    /// reading re-bases cleanly and refills normally from there.
    /// </summary>
    [Fact]
    public void AClockGoingBackwards_MintsNoTokens_AndDoesNotFreezeTheBucket()
    {
        var later = T0.AddMinutes(10);

        // A fractional balance makes the "no mint" claim unambiguous: if the backward jump added
        // anything at all, refilled would no longer equal the original 0.5 exactly.
        var backwards = TokenBucket_Gate.Take(tokens: 0.5, lastRefillUtc: later, nowUtc: T0);

        Assert.Equal(0.5, backwards.Tokens);
        Assert.True(backwards.Wait > TimeSpan.Zero);
        Assert.Equal(T0, backwards.LastRefillUtc);

        // Re-based, not frozen: thirty seconds after the (earlier) reading it just used, it refills
        // normally instead of staying pinned at 0.5 or treating the skew as still outstanding.
        var afterRebase = TokenBucket_Gate.Take(backwards.Tokens, backwards.LastRefillUtc, T0.AddSeconds(30));

        // DERIVED FROM THE CAPACITY, never a number typed in: thirty seconds is half a refill
        // window, so half a bucket arrives, minus the one token this call takes. A literal here
        // reddened the moment the capacity was corrected — which is a test asserting the constant
        // rather than the behaviour.
        var refilledInHalfAWindow = TokenBucket_Gate.DEFAULT_CAPACITY / 2;

        Assert.Equal(0.5 + refilledInHalfAWindow - 1, afterRebase.Tokens);
    }

    /// <summary>
    /// However long the bucket idles, it must cap at capacity rather than accumulate an unbounded
    /// credit that would let a very long silence pay for an unlimited burst.
    /// </summary>
    [Fact]
    public void TheBucketNeverExceedsCapacity_HoweverLongItIdles()
    {
        var farFuture = T0.AddDays(10000);

        var result = TokenBucket_Gate.Take(tokens: 2, lastRefillUtc: T0, nowUtc: farFuture);

        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY - 1, result.Tokens);
    }

    [Fact]
    public void ReadRetryAfter_IsZero_ForNull()
    {
        Assert.Equal(TimeSpan.Zero, TokenBucket_Gate.Read_RetryAfter(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ReadRetryAfter_IsZero_ForZeroOrNegative(int retryAfterSeconds)
    {
        Assert.Equal(TimeSpan.Zero, TokenBucket_Gate.Read_RetryAfter(retryAfterSeconds));
    }

    [Fact]
    public void ReadRetryAfter_PassesThrough_ANormalValue()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), TokenBucket_Gate.Read_RetryAfter(10));
    }

    /// <summary>
    /// A malformed or hostile retry_after must not be able to park the bridge for a day — it is
    /// clamped to the documented ceiling rather than honoured verbatim.
    /// </summary>
    [Fact]
    public void ReadRetryAfter_ClampsAHugeValue_ToTheMaximumHonouredCeiling()
    {
        var result = TokenBucket_Gate.Read_RetryAfter(1_000_000);

        Assert.Equal(TimeSpan.FromSeconds(TokenBucket_Gate.MAXIMUM_HONOURED_RETRY_AFTER_SECONDS), result);
    }
}

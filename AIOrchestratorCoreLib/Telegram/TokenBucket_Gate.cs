namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// How long a request must wait before Telegram will accept it — the rate limit, in ONE place.
///
/// <para>
/// WHY ONE PLACE. Telegram allows roughly twenty messages a minute to a group, and this app edits
/// topic names every tick, posts a status line per orchestration, mirrors channel appends and
/// answers commands. Handling the limit per feature is how it was handled: <see cref="TelegramAttempt_Gate"/>
/// classifies a 429 into an unknown outcome and backs that ONE caller off, the busy-supervisor
/// narration reads the same 429 and does the opposite thing, and every other send has no opinion at
/// all. The result is that a burst from any one of them spends the allowance of all the others,
/// invisibly, and the feature that gets the 429 is whichever happened to go last.
/// </para>
/// <para>
/// A BUCKET, NOT A SLEEP BETWEEN SENDS. A fixed delay would cap the steady rate and still let a cold
/// start fire twenty messages in one second; a bucket lets a quiet minute pay for a burst, which is
/// exactly the traffic shape here — silence, then a catch-up burst when the owner unmutes.
/// </para>
/// <para>
/// PURE, so the rule can be tested at any speed without a network or a clock. The state is two
/// numbers the caller carries; the caller is <see cref="TelegramApiClient.TelegramApiClientModel"/>,
/// which is the single choke point every Telegram method already goes through.
/// </para>
/// </summary>
public static class TokenBucket_Gate
{
    /// <summary>Telegram's documented ceiling for messages sent to one group.</summary>
    public const double TELEGRAM_GROUP_MESSAGES_PER_MINUTE = 20;

    /// <summary>Refill over one minute, the window the ceiling is stated in.</summary>
    public const double DEFAULT_REFILL_SECONDS = 60;

    /// <summary>
    /// The burst a quiet minute pays for — and it is HALF the ceiling on purpose, which is the
    /// arithmetic a first version of this got wrong.
    ///
    /// <para>
    /// A bucket's worst case over a rolling window is not its capacity: it is the capacity PLUS
    /// whatever refilled during that window. Starting full at 18, this granted the 18 immediately
    /// and then one every 3.3 s, which is 35 messages inside the first minute — against a ceiling of
    /// 20. A limiter that can nearly double the limit it exists to respect is not a limiter, and the
    /// failure would have shown up exactly where it is least welcome: the catch-up burst after an
    /// unmute, which is the biggest burst this app ever produces.
    /// </para>
    /// <para>
    /// So capacity + refill-in-one-window must fit under the ceiling: 10 + 10 = 20. That is the
    /// whole derivation, and it is why this number is not tunable by feel.
    /// </para>
    /// </summary>
    public const double DEFAULT_CAPACITY = TELEGRAM_GROUP_MESSAGES_PER_MINUTE / 2;

    /// <summary>
    /// THE SECOND BUCKET: edits, deletes and <c>answerCallbackQuery</c> (brief F5).
    ///
    /// <para>
    /// They were metered by NOTHING. The opt-in above is deliberately narrow — it covers the calls
    /// that CREATE A MESSAGE, which is what Telegram's twenty-a-minute ceiling counts — and the
    /// reasoning for keeping edits out of it is still right: spending the message allowance on an
    /// edit is spending it on something the ceiling does not charge for, and queueing an
    /// <c>answerCallbackQuery</c> behind a mirror backlog hangs a button spinner on the owner's
    /// phone. But "not on the message bucket" was implemented as "on no bucket at all", and this app
    /// edits a topic's status line, its name and its command bar on a tick — the edits are its
    /// HIGHEST-VOLUME traffic, and the only thing that has ever slowed them down is a 429 arriving.
    /// </para>
    /// <para>
    /// LARGER, AND THE NUMBER IS A GUESS — stated as one. Telegram documents no per-chat edit limit,
    /// so this cannot be derived the way <see cref="DEFAULT_CAPACITY"/> is derived from a published
    /// ceiling. Sixty a minute is several times the app's own steady rate and well under anything
    /// observed to draw a 429, and the same capacity-plus-refill arithmetic is applied to it: 30 + 30
    /// = 60. It is a brake against a runaway, not a model of a limit — the 429 handling remains what
    /// actually respects the limit.
    /// </para>
    /// </summary>
    public const double CONTROL_CALLS_PER_MINUTE = 60;

    /// <summary>Half the ceiling, for the reason <see cref="DEFAULT_CAPACITY"/> gives: capacity + one window's refill must fit.</summary>
    public const double CONTROL_CAPACITY = CONTROL_CALLS_PER_MINUTE / 2;

    /// <summary>
    /// THE MINIMUM GAP BETWEEN TWO EDITS OF THE SAME MESSAGE — thirty seconds, and this number was
    /// measured rather than chosen.
    ///
    /// <para>
    /// PRODUCTION, 2026-09-10 20:57-20:59, the first three minutes after the deploy: PULSE edited one
    /// message per topic once a minute, and Telegram answered 429 with `retry_after` 20, then 22,
    /// then 32 seconds — once a minute, per topic. Nothing was lost (the retry lands) but the pattern
    /// is a throttle being hit continuously, and the retry_after it hands back is Telegram telling us
    /// the interval it wants. Thirty seconds is the top of the observed range.
    /// </para>
    /// <para>
    /// WHY IT CANNOT BE THE CONTROL BUCKET. That bucket is sized from the group ceiling — sixty calls
    /// a minute across the whole supergroup — and it was nowhere near empty while these 429s arrived.
    /// Telegram limits edits of ONE message far more tightly than calls to the group, so a global
    /// allowance cannot express the rule however it is tuned: two topics editing once a minute each
    /// is two calls a minute, which no group-level bucket would ever hold back.
    /// </para>
    /// <para>
    /// IT IS A BELT, NOT THE FIX. The cause was a heartbeat that changed every minute, and that is
    /// stepped to five minutes at the source. This stops the next surface that decides to edit a
    /// message in a loop from finding out the same way.
    /// </para>
    /// </summary>
    public static readonly TimeSpan MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest a 429 may hold a CONTROL call — far shorter than the message one, and the
    /// shortness is the whole point.
    ///
    /// <para>
    /// Retrying these used to be forbidden outright, for a measured reason: a 429 carrying
    /// <c>retry_after: 300</c> slept five minutes twice inside a single two-second mirror tick, and
    /// for a callback query the wait was worse than useless because Telegram invalidates one after
    /// about ten seconds. Two seconds covers the burst a 429 on an edit actually is, cannot hold a
    /// tick open, and leaves a callback query most of its life. Past it the failure goes to the
    /// caller, where the per-channel backoff already lives.
    /// </para>
    /// </summary>
    public static readonly TimeSpan MAXIMUM_CONTROL_RETRY_WAIT = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The bucket after time has passed, and how long the caller must wait before spending a token.
    ///
    /// <para>
    /// A ZERO wait means send now, and the returned <paramref name="tokens"/> already has the token
    /// deducted. A POSITIVE wait means the caller sleeps that long and asks again — the recompute
    /// after the sleep is deliberate: several senders race here, and the one that wakes first should
    /// take the token that became available, not the one that was promised to it a second ago.
    /// </para>
    /// </summary>
    public static (double Tokens, DateTime LastRefillUtc, TimeSpan Wait) Take(
        double tokens,
        DateTime lastRefillUtc,
        DateTime nowUtc,
        double capacity = DEFAULT_CAPACITY,
        double refillSeconds = DEFAULT_REFILL_SECONDS)
    {
        // A clock that went BACKWARDS (an NTP correction, a laptop waking) must not mint tokens by
        // producing a negative elapsed time, and must not freeze the bucket for the whole skew
        // either — it simply refills nothing this time and re-bases on the new reading.
        var elapsedSeconds = Math.Max(0, (nowUtc - lastRefillUtc).TotalSeconds);
        var refilled = Math.Min(capacity, tokens + (elapsedSeconds * capacity / refillSeconds));

        if (refilled >= 1)
            return (refilled - 1, nowUtc, TimeSpan.Zero);

        // How long until one whole token exists. Never negative: refilled is below 1 here.
        var secondsToOneToken = (1 - refilled) * refillSeconds / capacity;

        return (refilled, nowUtc, TimeSpan.FromSeconds(secondsToOneToken));
    }

    /// <summary>
    /// How long Telegram itself said to wait, from a 429's <c>retry_after</c>.
    ///
    /// <para>
    /// THEIR NUMBER BEATS OURS, always. The bucket is an estimate of a limit we cannot see; a 429
    /// carries the limit's own answer, and ignoring it in favour of a local guess is how a client
    /// keeps hitting the same wall. Clamped to a sane ceiling so a malformed or hostile payload
    /// cannot park the bridge for a day.
    /// </para>
    /// </summary>
    public const int MAXIMUM_HONOURED_RETRY_AFTER_SECONDS = 300;

    /// <summary>
    /// The longest a 429 may hold a single call INSIDE the caller's turn.
    ///
    /// <para>
    /// The mirror tick runs every two seconds and holds a channel-write allowance while it does, so
    /// a call that sleeps for minutes does not merely delay itself — it stops the mirror, the owner's
    /// deliveries and the deadline sweep for as long as it sleeps. Telegram's advice is honoured up
    /// to this much and no further; beyond it the failure goes to the caller, where the per-channel
    /// backoff already knows what to do with an outcome it could not learn.
    /// </para>
    /// </summary>
    public static readonly TimeSpan MAXIMUM_INLINE_RETRY_WAIT = TimeSpan.FromSeconds(10);

    public static TimeSpan Read_RetryAfter(int? retryAfterSeconds)
    {
        if (retryAfterSeconds == null || retryAfterSeconds.Value <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds.Value, MAXIMUM_HONOURED_RETRY_AFTER_SECONDS));
    }
}

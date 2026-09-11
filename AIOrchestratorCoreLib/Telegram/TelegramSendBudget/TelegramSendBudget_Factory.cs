namespace AIOrchestratorCoreLib.Telegram.TelegramSendBudget;

public static class TelegramSendBudget_Factory
{
    /// <summary>
    /// A FIRST-EVER START: the send bucket is full, which is what it has always been and what the
    /// catch-up-after-unmute design wants — a quiet minute pays for a burst.
    ///
    /// <para>
    /// Used only when there is nothing to restore: no state file, or one that carried no bucket
    /// (an older version, a quarantined file). Anything else uses
    /// <see cref="Create_FromPersisted"/>, so an ordinary restart neither grants a free burst nor
    /// imposes a wait the previous process had not already paid for.
    /// </para>
    /// </summary>
    public static ITelegramSendBudget Create_Fresh()
    {
        return new TelegramSendBudgetModel(TokenBucket_Gate.DEFAULT_CAPACITY, DateTime.UtcNow);
    }

    /// <summary>
    /// RESUMES WHERE THE LAST PROCESS STOPPED. The stamp is a wall-clock time, so the elapsed real
    /// seconds since it refill the bucket exactly as they would have inside a process that never
    /// died — a restart after a quiet hour comes up full, and a restart two seconds into a crash
    /// loop comes up as empty as it was.
    ///
    /// <para>
    /// Both inputs are clamped, because they come off a file that anything can write: a token count
    /// outside [0, capacity] and a stamp in the FUTURE are both expressible in JSON, and a future
    /// stamp is the dangerous one — <see cref="TokenBucket_Gate.Take"/> refuses to refill on a
    /// negative elapsed time, so an absurd stamp would freeze the bucket rather than merely skew it.
    /// CLAUDE.md decision 12 is the same lesson from the channel headers: a stored timestamp is
    /// untrusted input.
    /// </para>
    /// </summary>
    public static ITelegramSendBudget Create_FromPersisted(double tokens, DateTime refilledUtc, DateTime nowUtc)
    {
        var safeTokens = double.IsFinite(tokens)
            ? Math.Clamp(tokens, 0, TokenBucket_Gate.DEFAULT_CAPACITY)
            : TokenBucket_Gate.DEFAULT_CAPACITY;

        var safeStamp = refilledUtc > nowUtc ? nowUtc : refilledUtc;

        return new TelegramSendBudgetModel(safeTokens, safeStamp);
    }
}

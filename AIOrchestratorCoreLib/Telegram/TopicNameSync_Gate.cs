namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The two decisions the topic-name sync makes — WHAT an attempt told us, and WHETHER to attempt now.
///
/// HERE RATHER THAN IN THE ENGINE, AND THAT IS THE WHOLE REASON THIS FILE EXISTS. `BridgeEngineModel` is
/// `internal sealed` with no `InternalsVisibleTo`, so every decision taken inside it is invisible to the
/// suite. <see cref="TopicStatusLine_Planner"/> records what that costs: a reviewer deleted the trusted
/// stamp reader, the per-topic delivery gate and the backoff gate all at once and the suite stayed
/// green. The seam was available as `InternalsVisibleTo` and was refused deliberately, because it "would
/// make the engine testable without making it tested". Moving the DECISION out is what makes it pinned;
/// the I/O stays where it is.
///
/// What remains unpinned is the one-line call from the engine to these methods. That is stated rather
/// than hidden — a green suite here says these rules are right, not that they are wired up.
///
/// UNASSERTED, NOT UNTESTABLE, AND THE DIFFERENCE MATTERS. An earlier version of this said the wiring
/// "cannot be observed by any test", which is wrong and was repeated into a commit message before it was
/// checked. `BridgeEngine_Factory.Create` is PUBLIC and takes interfaces only, and five test files
/// already drive the engine end to end — `OwnerAnswerSurvivesFailedSendTests`,
/// `CloseImplementerGuardProbeTests` and the three nudge probes. What is unreachable is the engine's
/// INTERNAL METHODS; its BEHAVIOUR is reachable, at the cost of a slow probe.
///
/// So the honest statement is that nothing asserts this wiring today and a probe could. "Untestable" is
/// the word that stops anyone trying, which is why it is corrected here rather than left standing.
///
/// SHARED BY TWO CALL SITES WITH OPPOSITE CONSEQUENCES. `Classify_Failure` is also used by the
/// busy-supervisor narration, which asks the same question — did the transport fail — and then does the
/// opposite thing with the answer: it decides whether to DISCARD stored message ids, where this file's
/// caller decides whether to SUPPRESS RETRIES. The classification is single on purpose and the actions
/// are deliberately not.
///
/// The class name is therefore narrower than its contents: the classifier is about Telegram transport
/// failures generally, not about topic names. Naming it for its first caller is a real wart and a rename
/// is available to any reviewer who wants one; it was left because the ruling was to share the rule, not
/// to move it.
/// </summary>
public static class TopicNameSync_Gate
{
    /// <summary>
    /// WHAT A FAILED ATTEMPT TOLD US. A transport failure never says the name is gone or applied — it
    /// says the round trip did not complete, which is a different fact and the one that had nowhere to
    /// be recorded.
    ///
    /// `OperationCanceledException` covers an `HttpClient` timeout, which arrives as a
    /// `TaskCanceledException`; `HttpRequestException` covers a dropped connection, a DNS failure, a
    /// reset and a TLS error. Anything else reached Telegram and came back refused.
    ///
    /// AND A RETRYABLE STATUS IS ALSO "WE DO NOT KNOW" — the limit this used to declare, now closed.
    /// A 429 is Telegram asking us to slow down and every 5xx is Telegram failing on its own side; in
    /// both cases the request may or may not have taken effect and a later attempt is worth making.
    /// They used to land in <see cref="TopicNameAttemptOutcomes.Rejected"/> because the client threw a
    /// plain <see cref="Exception"/> for any non-2xx and the status was formatted into a message string
    /// and lost — NOT unavailable, DISCARDED. <see cref="TelegramApiClient.TelegramApiException"/> now
    /// carries it, and no string is parsed anywhere.
    ///
    /// That mattered here more than anywhere: the app edits topic names every tick, so a rate limit is
    /// an ordinary event rather than an exotic one, and a single 429 used to record a name as applied
    /// for the life of the process.
    /// </summary>
    public static TopicNameAttemptOutcomes Classify_Failure(Exception failure)
    {
        if (failure is TelegramApiClient.TelegramApiException answered)
        {
            return answered.Is_Retryable
                ? TopicNameAttemptOutcomes.OutcomeUnknown
                : TopicNameAttemptOutcomes.Rejected;
        }

        return failure is OperationCanceledException or HttpRequestException
            ? TopicNameAttemptOutcomes.OutcomeUnknown
            : TopicNameAttemptOutcomes.Rejected;
    }

    /// <summary>
    /// WHETHER TO ATTEMPT NOW, given when an unknown outcome said not to before.
    ///
    /// Null means nothing is holding it back — the ordinary case, and the one that must stay cheap. The
    /// comparison is `>=` so the stamp EXPIRES at its instant rather than one tick after it; a gate that
    /// is never due at exactly its own deadline is a gate whose duration is silently longer than it says.
    /// </summary>
    public static bool Is_AttemptDue(DateTime? retryAfterUtc, DateTime nowUtc)
    {
        return retryAfterUtc == null || nowUtc >= retryAfterUtc.Value;
    }

    /// <summary>
    /// When an unknown outcome may be tried again. Separate from <see cref="Is_AttemptDue"/> so the test
    /// can set a stamp and then walk the clock across it, rather than assert on an arithmetic it also
    /// performed itself.
    /// </summary>
    public static DateTime Build_RetryAfterUtc(DateTime nowUtc, int backoffSeconds)
    {
        return nowUtc.AddSeconds(backoffSeconds);
    }
}

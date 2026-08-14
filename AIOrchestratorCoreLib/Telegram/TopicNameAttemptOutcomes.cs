namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// What one attempt to set a Telegram topic's name actually told us — THREE outcomes, because the
/// world has three and collapsing it to two is the defect this enum exists to end.
///
/// The topic-name sync remembered a single fact per orchestration, "this name is applied", and used it
/// for two different purposes: to skip work that is already done, and to stop a failing edit retrying
/// at tick rate. One value, two meanings, and every failure had to be forced into one of them — so a
/// failure that told us NOTHING was recorded as if it had told us the name was applied.
///
/// That is the same shape <see cref="Status.Nudge_Decider"/> records in its own docstring: two earlier
/// fixes failed because "the nudge gate was borrowing a map that already carried two meanings", and the
/// answer there was a second map with exactly one meaning. This is that, for topic names.
/// </summary>
public enum TopicNameAttemptOutcomes
{
    /// <summary>
    /// The name is now what we want. A successful edit, or Telegram's TOPIC_NOT_MODIFIED — which is
    /// success wearing a failure's clothes, and was already handled as success before this enum existed.
    /// </summary>
    Applied,

    /// <summary>
    /// THE TRANSPORT FAILED, SO WE DO NOT KNOW. A timeout or a dropped connection says the round trip
    /// did not complete; it does not say whether Telegram applied the name before the wire broke.
    ///
    /// This is the outcome that had nowhere to go. Recorded as applied it produces a topic frozen on the
    /// OLD mode glyph until the mode changes again or the app restarts — and decision 11 makes that
    /// glyph the owner-visible truth of a passing state. Recorded as nothing it retries every tick,
    /// which is the spin the done-flag write was added to stop: one orchestration logged 28 identical
    /// errors in minutes. Neither is right, because both answer a question we cannot answer.
    ///
    /// It suppresses retries for a WHILE and then retries.
    /// </summary>
    OutcomeUnknown,

    /// <summary>
    /// Telegram answered and refused. The name did not apply, and attempting again immediately will not
    /// change that — so the attempt is remembered and retried on the next name change rather than on the
    /// next tick. This is the bucket the original done-flag write was written for, and its reasoning was
    /// always sound for it.
    /// </summary>
    Rejected,
}

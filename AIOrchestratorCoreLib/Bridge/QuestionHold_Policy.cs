namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// ONE QUESTION AT A TIME, ENFORCED WHERE IT TAKES EFFECT.
///
/// The owner, 2026-09-09: *"I have just received like 10 questions in a row, without the session
/// waiting for my answers to each question before sending the next. This was a mess"*. Nine
/// question-bearing entries left one orchestration between 15:18 and 15:23, six of them written
/// inside the same 20-millisecond batch, with no reply in between.
///
/// <para>
/// The rule already existed twice, and neither copy could hold it. `supervisor.md` states it as a
/// HARD RULE and claims *"the app enforces this by STOPPING YOU"* — but the thing doing the stopping
/// is a PreToolUse hook that (a) applied to the SUPERVISOR role only, so the solo session that
/// flooded the owner was never covered, and (b) says of itself that it is *"advisory, not a
/// boundary ... the enforcement that must actually HOLD lives in the app, at the point of effect"*.
/// The app, meanwhile, computes "a question is outstanding on this topic" on every 2-second tick and
/// spent the answer on a topic-name glyph: <c>AwaitingAnswerFlag_Marker.Is_Raised</c> had no
/// production callers at all. This is what finally reads it.
/// </para>
/// <para>
/// HELD, NEVER DROPPED. The hold works by not confirming the tailer's cursor, so a held entry is
/// re-emitted on the next poll and reaches the owner the moment the flag clears — which happens when
/// they answer (any inbound message clears it) or at the app's own ten-minute cap. Same promise DND
/// makes, for the same reason: a question the owner never sees is worse than a late one. It is
/// deliberately NOT the mirror's failure-retry path, which gives up after a window and drops.
/// </para>
/// </summary>
public static class QuestionHold_Policy
{
    /// <summary>
    /// Whether this entry must wait. <paramref name="questionOutstanding"/> is the awaiting-answer
    /// flag for the orchestration — raised when a question with options is texted to a REMOTE owner,
    /// cleared by any word from them, and never raised at all while they are at the session's own
    /// terminal (there is no phone to flood).
    ///
    /// <para>
    /// MEMBER CHANNELS ARE NEVER HELD. They are agent-to-agent traffic that never reaches the phone,
    /// and holding them would stop an orchestration WORKING because its supervisor asked the owner
    /// something — which is the objection the engine's own comment raises against gating on a pending
    /// question, and a fair one about the work. This gates what is TEXTED and nothing else.
    /// </para>
    /// </summary>
    public static bool Should_Hold(bool isOwnerChannel, bool questionOutstanding)
    {
        return isOwnerChannel && questionOutstanding;
    }
}

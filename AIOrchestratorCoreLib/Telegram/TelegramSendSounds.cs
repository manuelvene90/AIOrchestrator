namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHETHER THIS SEND MAKES THE OWNER'S PHONE RING. Required at every send, so a loud message is a
/// choice somebody made at the call site rather than the default nobody looked at.
///
/// <para>
/// THE OWNER'S RULING, 2026-09-09: *"If the supervisor writes to me, I must know it — that rings.
/// Status, receipts and app bookkeeping do not ring."* Half of what reached their phone was not for
/// them: a fifteen-line STATUS message every thirty minutes, the same receipt sentence under every
/// message they sent, false "waiting on your reply" alerts. None of it was wrong information — it
/// was the wrong CHANNEL for it, and a notification is a channel.
/// </para>
/// <para>
/// A BOOL WOULD HAVE BEEN SHORTER AND WORSE. `disable_notification: true` at thirty-nine call sites
/// reads as a wire detail; <c>TelegramSendSounds.Silent</c> reads as a decision, and the compiler
/// makes the decision unavoidable — which is the point of it being required rather than defaulted.
/// </para>
/// </summary>
public enum TelegramSendSounds
{
    /// <summary>
    /// The phone lights up and makes a sound. For the SUPERVISOR's own words to the owner, and for
    /// alerts the owner can act on: a usage limit, a budget ceiling, a question that has gone
    /// unanswered.
    /// </summary>
    Rings,

    /// <summary>
    /// Delivered without a notification — the message is there when they next look. For everything
    /// the APP writes about itself: the status surface, receipts, confirmations, coaching, "the turn
    /// ended".
    /// </summary>
    Silent,
}

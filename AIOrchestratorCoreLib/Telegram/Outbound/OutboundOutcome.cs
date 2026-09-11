namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHAT HAPPENED TO ONE INTENT, reported by the pump to whoever published it.
///
/// <para>
/// THIS IS THE LOAD-BEARING TYPE FOR CRASH SAFETY. The tailer's offset advances only when a
/// delivery is CONFIRMED, and once the mirror is migrated the confirmation arrives here rather than
/// from the tick's own return value. Publishing is not delivering: a version of this change that
/// settles the tailer on a successful enqueue loses messages on a restart — silently, and only on
/// the day it matters.
/// </para>
/// </summary>
public sealed record OutboundOutcome(
    bool Delivered,
    long? MessageId,
    Exception? Failure,
    int Attempts);

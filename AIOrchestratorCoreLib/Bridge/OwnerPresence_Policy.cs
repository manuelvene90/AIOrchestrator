using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Every decision presence makes, in one pure place — what it does to delivery, whether a session
/// may block on a Telegram answer, and when it ends. The engine is left with the execution.
/// <para>
/// It lives outside <c>BridgeEngineModel</c> for the reason that file's own history documents: the
/// engine's only entry point is <c>Run_Async</c>, so a test cannot ask it for one tick and assert.
/// A decision left in there is verified by a timing-dependent loop test or by nothing at all, and
/// on this system it has repeatedly been nothing at all.
/// </para>
/// </summary>
public static class OwnerPresence_Policy
{
    /// <summary>
    /// What presence does to this topic's delivery, or null when it has no opinion. TERMINAL means
    /// SILENCED rather than DEFERRED, and the difference is the point: the owner is reading this
    /// content live in the terminal, so replaying it on their phone afterwards is precisely what
    /// they do not want. Deferred would hand them a burst of things they already read.
    /// </summary>
    public static TelegramDeliveryModes? Resolve_ModeOverride_OrNull(OwnerPresenceModes presence)
    {
        return presence == OwnerPresenceModes.Terminal
            ? TelegramDeliveryModes.Silenced
            : null;
    }

    /// <summary>
    /// Whether a question to the owner may raise the awaiting-answer flag — the flag whose hook
    /// stops the supervisor until an answer arrives from Telegram.
    /// <para>
    /// THIS IS THE LOAD-BEARING HALF of terminal mode. Without it the mode is cosmetic: the
    /// supervisor still freezes on every Bash call waiting for a tap, while the owner is sitting in
    /// front of it typing the answer into the session. Muting alone never fixed that, because the
    /// block is not about delivery.
    /// </para>
    /// </summary>
    public static bool Should_RaiseAwaitingAnswer(OwnerPresenceModes presence)
    {
        return presence == OwnerPresenceModes.Remote;
    }

    /// <summary>
    /// An owner message that ARRIVED FROM TELEGRAM in this topic proves they are not at its
    /// terminal, so terminal mode ends by itself — the same shape as the auto-unmute, and for the
    /// same reason: a mode the owner has to remember to turn off is one they will be trapped by.
    /// <para>
    /// The presence command itself is excluded. Without that, `/pc` would be flipped back to Remote
    /// by the very message that asked for Terminal — the trap the mode commands already dodge by
    /// deferring themselves out of the inbound loop.
    /// </para>
    /// </summary>
    public static bool Should_FlipToRemote(OwnerPresenceModes presence, bool isPresenceCommandItself)
    {
        return presence == OwnerPresenceModes.Terminal && !isPresenceCommandItself;
    }

    /// <summary>
    /// Whether the app must stop trying to get this orchestration's SUPERVISOR back to work. Terminal
    /// mode is a meeting: the owner is talking to it, and every nudge, ledger complaint, idle flag and
    /// periodic status is an interruption of the conversation they are having.
    /// <para>
    /// It suppresses the app's own attention traffic and NOTHING ELSE. Members keep working, keep
    /// writing to their channels and keep being nudged — they are not in the meeting. The supervisor's
    /// watcher keeps running too: a stopped watcher that never gets re-armed is how an orchestration
    /// goes permanently deaf, so the wake still fires and the supervisor simply does not act on it.
    /// </para>
    /// </summary>
    public static bool Suppresses_SupervisorAttention(OwnerPresenceModes presence)
    {
        return presence == OwnerPresenceModes.Terminal;
    }

    /// <summary>What `/pc` does: a toggle, because the owner asked for one switch and not two commands.</summary>
    public static OwnerPresenceModes Toggle(OwnerPresenceModes current)
    {
        return current == OwnerPresenceModes.Terminal
            ? OwnerPresenceModes.Remote
            : OwnerPresenceModes.Terminal;
    }
}

using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// ONE THING TO SAY TO TELEGRAM, and everything the scheduler needs to decide when to say it.
///
/// <para>
/// THE CALL RIDES AS A DELEGATE, deliberately. Re-encoding fifteen API methods as data would be a
/// second description of the wire that can drift from the first; instead the intent carries the
/// call untouched, and <see cref="OutboundQueue_Planner"/> — which is the part worth testing —
/// decides purely over the descriptive fields and never looks at <see cref="Send"/>.
/// </para>
/// </summary>
/// <param name="Band">Priority. See <see cref="OutboundBands"/>.</param>
/// <param name="Kind">Whether a newer intent makes this one worthless (<see cref="OutboundKinds.Slot"/>) or not.</param>
/// <param name="ChatKey">
/// The serialisation key. One supergroup means one key today; it is carried so a second chat would
/// need no redesign, and so a send has something to be keyed on.
/// </param>
/// <param name="OrderKey">
/// For a job: what it must stay in order behind — the channel file path for a mirrored append. Two
/// appends on one channel must arrive in the order they were written; two appends on DIFFERENT
/// channels must not block each other, which a single global FIFO could not express.
/// </param>
/// <param name="SlotKey">For a slot: the key a newer intent overwrites. Null for a job.</param>
/// <param name="CooldownTarget">
/// The door this call knocks on, from <see cref="OutboundCooldown_Keys"/> — the message for an
/// edit, the chat for a send.
/// </param>
/// <param name="Send">The call itself. Returns the new message id when the call mints one.</param>
/// <param name="OnOutcome">
/// Reported after the pump has finished with this intent, delivered or not. This is where a message
/// id is remembered and where a tailer offset is settled.
/// </param>
/// <param name="EnqueuedUtc">When it was published — for the age diagnostics, and for ties within a band.</param>
public sealed record OutboundIntent(
    OutboundBands Band,
    OutboundKinds Kind,
    string ChatKey,
    string? OrderKey,
    string? SlotKey,
    string CooldownTarget,
    Func<ITelegramApiClient, CancellationToken, Task<long?>> Send,
    Action<OutboundOutcome>? OnOutcome,
    DateTime EnqueuedUtc)
{
    /// <summary>A job: ordered behind everything on the same order key, and never discarded.</summary>
    public static OutboundIntent Job(
        OutboundBands band,
        string chatKey,
        string orderKey,
        string cooldownTarget,
        Func<ITelegramApiClient, CancellationToken, Task<long?>> send,
        Action<OutboundOutcome>? onOutcome,
        DateTime nowUtc)
    {
        return new OutboundIntent(band, OutboundKinds.Job, chatKey, orderKey, SlotKey: null, cooldownTarget, send, onOutcome, nowUtc);
    }

    /// <summary>A slot: the current value of a surface, which a newer value replaces outright.</summary>
    public static OutboundIntent Slot(
        OutboundBands band,
        string chatKey,
        string slotKey,
        string cooldownTarget,
        Func<ITelegramApiClient, CancellationToken, Task<long?>> send,
        Action<OutboundOutcome>? onOutcome,
        DateTime nowUtc)
    {
        return new OutboundIntent(band, OutboundKinds.Slot, chatKey, OrderKey: null, slotKey, cooldownTarget, send, onOutcome, nowUtc);
    }
}

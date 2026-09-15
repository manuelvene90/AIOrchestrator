namespace AIOrchestratorCoreLib.Running.WakeTicket;

/// <summary>
/// THE WHOLE PROTOCOL BETWEEN THE APP AND A TERMINAL SESSION'S MONITOR, after the 2026-09-15
/// one-wake-model change moved "does this session wake now?" out of the bash watcher and into
/// <see cref="WakeDecision.WakeDecision_Resolver"/>. A terminal session in ticket mode
/// (<see cref="WakeModes.Ticket"/>) no longer decides for itself by fingerprinting its channels — the
/// app decides, with the same policy a bridge-driven session gets, and writes this file; the shrunken
/// watcher only polls it and carries the message.
///
/// <para>
/// A ticket is written by <c>WakeTicket_Store.Write</c> and read by both this binary and the bash
/// watcher, so its shape is load-bearing across a language boundary: one line of JSON, snake_case-free
/// (the watcher parses it with plain shell tools), written whole via temp-then-rename so the watcher
/// — which polls on its own clock — can never observe a half-written ticket.
/// </para>
/// </summary>
public interface IWakeTicket
{
    /// <summary>
    /// STRICTLY INCREASING per session, and what the watcher compares on instead of the timestamp: two
    /// wakes inside one clock tick must still be two wakes, and a monitor comparing timestamps could
    /// collapse them into one.
    /// </summary>
    int Number { get; }

    /// <summary>Why the turn is starting — the same words <see cref="WakeDecision.IWakeDecision.Reason"/> carries.</summary>
    string Reason { get; }

    /// <summary>
    /// Null until Task 11 gives the ticket a state pack to hand the terminal session instead of making
    /// it re-read its channels from scratch.
    /// </summary>
    string? StatePackFile { get; }

    DateTime StampedUtc { get; }
}

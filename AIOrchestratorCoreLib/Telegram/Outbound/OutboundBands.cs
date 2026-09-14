namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHO GOES FIRST WHEN THE ALLOWANCE IS SCARCE — the owner's ruling of 2026-09-10, and the reason
/// the door exists at all.
///
/// <para>
/// LOWER IS MORE IMPORTANT, so the scheduler can compare these directly. The order is not a
/// preference: measured on the VPS, the owner's tap — one human event, worth more than anything
/// else on the wire — was handled one-shot and gave up on its first refusal, while a status line
/// repaint worth nothing when the content has not moved retried every 2 s for twelve hours (388
/// HTTP 429 in the hour to 22:38 on 09-10, 378 in the hour to 09:27 on 09-11, ~96% of them two
/// status lines). The surface with no value held the allowance open and the surface the owner was
/// looking at got what was left of it, which during a cooldown window is nothing.
/// </para>
/// </summary>
public enum OutboundBands
{
    /// <summary>The owner just did something and is looking at the screen: a tap's rewrite, its receipt.</summary>
    Tap = 0,

    /// <summary>The conversation itself — agents' messages, alerts the owner must see. Never dropped.</summary>
    Conversation = 1,

    /// <summary>Status lines, topic names, the dashboard. Useful; worthless when superseded.</summary>
    Cosmetic = 2,
}

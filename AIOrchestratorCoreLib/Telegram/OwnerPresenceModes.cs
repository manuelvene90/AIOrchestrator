namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHERE THE OWNER IS for one orchestration — presence, not delivery. The two were conflated and it
/// wedged sessions: 🔕 was documented as "I'm in its terminal" but only ever changed what got sent,
/// so a supervisor being spoken to face-to-face still texted its questions to Telegram and then
/// stopped dead waiting for a tap that was never coming, while the owner typed the answer at it.
/// <para>
/// Delivery settings stay independent and still mean what they meant: REMOTE + muted is "I am on my
/// phone, do not ping me". TERMINAL is a different statement — "I am here" — and the delivery
/// consequence follows from it rather than being it.
/// </para>
/// </summary>
public enum OwnerPresenceModes
{
    /// <summary>The owner is on Telegram: questions are texted and the session waits for an answer.</summary>
    Remote,

    /// <summary>
    /// The owner is in this orchestration's terminal. Nothing is pushed, and nothing BLOCKS on a
    /// Telegram answer — the conversation is happening in the session itself.
    /// </summary>
    Terminal,
}

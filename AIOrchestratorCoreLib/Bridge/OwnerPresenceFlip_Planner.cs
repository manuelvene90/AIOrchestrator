using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>Where the owner is, per orchestration — the only input the flip decision needs.</summary>
public readonly record struct OrchestrationPresence(string OrchId, OwnerPresenceModes Presence);

/// <summary>
/// Which terminals the owner has just proved they are NOT sitting at.
///
/// <para>
/// ONLY `/pc` ENDS TERMINAL MODE (owner's ruling, 2026-08-21). An ordinary message ends nothing.
/// </para>
/// <para>
/// This REVERSES the rule that stood here, and the old reasoning is kept because it was not silly: a
/// message arriving from Telegram does prove the owner is holding a phone, so it looked like proof
/// they were not at any terminal (rev-4 F6, 2026-08-13). What that missed is that holding a phone and
/// sitting at a keyboard are not exclusive. The owner sets `/pc` because they are at the machine,
/// glances at Telegram, and the mode they asked for ten seconds ago is revoked without a word. Their
/// log carries it twice: `da-vinci-fintech-suite-13` set 10:43:02 and revoked 10:45:55 "they texted
/// Telegram"; `da-vinci-fintech-suite-9` set 12:33:18 and revoked 12:33:39 — twenty-one seconds. The
/// second half of the same complaint was the desync it caused: with presence silently back at Remote,
/// their next `/pc` — meant as "off" — turned it ON again, which reads as "the icon will not go away".
/// </para>
/// <para>
/// WHAT THIS GIVES BACK, STATED RATHER THAN GLOSSED: the trap the widening was meant to fix. An owner
/// who walks away without toggling leaves that orchestration silent, with `/pc` as its only exit. That
/// is accepted deliberately. The standing reminder is the 💻 in the topic name, which is visible in the
/// topic list from the phone at all times — the auto-flip never was, which is precisely why it could
/// revoke the mode unnoticed. Do not re-introduce an implicit exit without asking the owner: they have
/// now ruled on this once, against the argument above.
/// </para>
/// <para>
/// A TIMED CAP WAS CONSIDERED AND REJECTED EARLIER, and the reason still holds under the new rule: a
/// cap can fire while the owner is still at the terminal, re-wedging them mid-meeting with no warning.
/// The awaiting-answer cap is safe because a forgotten block is always wrong after ten minutes;
/// terminal presence may be RIGHT for three hours. The two look like the same pattern and are
/// opposites — one expires a mistake, the other would expire a fact.
/// </para>
/// </summary>
public static class OwnerPresenceFlip_Planner
{
    /// <summary>
    /// The orchestrations whose presence must return to Remote because of this message.
    /// <para>
    /// Empty for anything that is not `/pc`. For `/pc` itself: exempt for the ONE orchestration it
    /// targets — otherwise the message asking for terminal mode would immediately undo it — and
    /// exempt for nothing else, because an owner who types `/pc` in one topic while another is still
    /// in terminal mode has just demonstrated they are not at that other terminal.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Resolve_Flips(
        IReadOnlyList<OrchestrationPresence> presences,
        string? textedOrchId,
        bool isPresenceCommand)
    {
        // AN ORDINARY MESSAGE ENDS NOTHING. This is the owner's ruling and it is the whole change:
        // the loop below used to run for every inbound message, so terminal mode was revoked by the
        // owner glancing at their phone. `/pc` is now the only thing that can end it.
        if (!isPresenceCommand)
            return [];

        List<string> flips = [];

        foreach (var candidate in presences)
        {
            if (candidate.Presence != OwnerPresenceModes.Terminal)
                continue;

            // The topic `/pc` was typed in is the one it is turning ON (or off), so it is never
            // flipped by its own delivery. Every OTHER terminal still ends: they cannot sit at two.
            if (candidate.OrchId == textedOrchId)
                continue;

            flips.Add(candidate.OrchId);
        }

        return flips;
    }
}

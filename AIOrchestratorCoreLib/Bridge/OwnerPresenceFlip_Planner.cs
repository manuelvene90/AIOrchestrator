using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>Where the owner is, per orchestration — the only input the flip decision needs.</summary>
public readonly record struct OrchestrationPresence(string OrchId, OwnerPresenceModes Presence);

/// <summary>
/// Which terminals the owner has just proved they are NOT sitting at.
///
/// <para>
/// A message arriving FROM TELEGRAM is EVIDENCE, not a guess: it proves the owner is holding a phone,
/// and therefore is not at ANY terminal — not merely the one whose topic they happened to type in.
/// The auto-flip used to be topic-scoped, so terminal mode had exactly one exit and an owner who
/// walked away without toggling left that orchestration silent indefinitely (rev-4 F6, 2026-08-13).
/// Texting General from a train demonstrates remoteness just as conclusively as texting the topic
/// itself.
/// </para>
/// <para>
/// A TIMED CAP WAS CONSIDERED AND REJECTED, and the reason belongs here so it is not re-proposed: a
/// cap can fire while the owner is still at the terminal, re-wedging them mid-meeting with no
/// warning. The awaiting-answer cap is safe because a forgotten block is always wrong after ten
/// minutes; terminal presence may be RIGHT for three hours. The two look like the same pattern and
/// are opposites — one expires a mistake, the other would expire a fact.
/// </para>
/// </summary>
public static class OwnerPresenceFlip_Planner
{
    /// <summary>
    /// The orchestrations whose presence must return to Remote because of this message.
    /// <para>
    /// The `/pc` command is exempt for the ONE orchestration it targets — otherwise the message
    /// asking for terminal mode would immediately undo it. It is exempt for nothing else: an owner
    /// who types `/pc` in one topic while another is still in terminal mode has just demonstrated
    /// they are not at that other terminal, and they cannot be at two at once.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Resolve_Flips(
        IReadOnlyList<OrchestrationPresence> presences,
        string? textedOrchId,
        bool isPresenceCommand)
    {
        var exemptOrchId = isPresenceCommand ? textedOrchId : null;

        List<string> flips = [];

        foreach (var candidate in presences)
        {
            if (candidate.Presence != OwnerPresenceModes.Terminal)
                continue;

            if (candidate.OrchId == exemptOrchId)
                continue;

            flips.Add(candidate.OrchId);
        }

        return flips;
    }
}

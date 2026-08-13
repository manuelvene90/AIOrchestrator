using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// What actually happens to a topic's outbound traffic, given everything that has an opinion about
/// it: where the owner IS, what this topic was set to, and the two app-wide switches.
/// <para>
/// Extracted from the engine because the ORDER of those opinions is the whole decision, and order is
/// invisible to a reader and to every test while it sits inside a private method of a class whose
/// only entry point is <c>Run_Async</c>. It cost exactly that: the presence check lived INSIDE the
/// "not General" branch, so the General topic — the one the owner sits at most — had the block
/// removed by terminal mode and nothing silenced by it (rev-4, 2026-08-13).
/// </para>
/// </summary>
public static class EffectiveMode_Resolver
{
    /// <summary>
    /// PRESENCE FIRST, AND FOR EVERY TOPIC INCLUDING GENERAL. The owner being in a terminal is not a
    /// delivery setting to be weighed against the others — it is a statement about where they are,
    /// and pushing to a phone they are not holding is the thing it exists to stop.
    /// <para>
    /// Below it the existing precedence is unchanged: a topic's OWN mode wins over the app-wide
    /// setting, so "silence just this one" survives someone flipping the global DND and vice versa.
    /// General has no session and therefore no topic mode of its own, which is the only thing the
    /// General branch was ever for.
    /// </para>
    /// </summary>
    public static TelegramDeliveryModes Resolve(
        OwnerPresenceModes presence,
        bool isGeneral,
        TelegramDeliveryModes topicMode,
        bool appWideDeferred,
        bool appWideSilenced)
    {
        var presenceOverride = OwnerPresence_Policy.Resolve_ModeOverride_OrNull(presence);

        if (presenceOverride != null)
            return presenceOverride.Value;

        if (!isGeneral && topicMode != TelegramDeliveryModes.Normal)
            return topicMode;

        if (appWideDeferred)
            return TelegramDeliveryModes.Deferred;

        if (appWideSilenced)
            return TelegramDeliveryModes.Silenced;

        return TelegramDeliveryModes.Normal;
    }
}

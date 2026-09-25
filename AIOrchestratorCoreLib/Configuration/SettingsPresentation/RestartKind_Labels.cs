using AIOrchestratorCoreLib.Configuration.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// THE WORDS FOR WHEN A CHANGE TAKES EFFECT — one copy for all three renderers (CLAUDE.md decision 12).
///
/// <para>
/// EVERY LABEL SAYS WHEN, AND NONE SAYS "MUST" (spec §6.1: restart kinds are "shown by the renderers,
/// enforced by nobody"). A label reading "requires restart" invites a renderer to grey out Save until the
/// host restarts, and an app that refused a Host-level change until it could restart itself is an app the
/// owner cannot configure from their phone — the thing the catalogue exists to make possible
/// (<see cref="RestartKinds"/>' own doc). So the Host label also says plainly that nothing restarts the
/// host on the owner's behalf: the write lands now, and it waits for them.
/// </para>
/// </summary>
public static class RestartKind_Labels
{
    /// <summary>The provider's write-stamp reload picks the file up on its next tick.</summary>
    public const string NONE = "applies at once";

    /// <summary>The sessions running now keep the old value; the next one spawned gets the new one.</summary>
    public const string NEXT_SPAWN = "applies from the next session spawn — running sessions keep the old value";

    /// <summary>The app or the daemon reads it once, at start.</summary>
    public const string HOST = "applies after the app or daemon restarts — nothing restarts it for you";

    public static string Describe(RestartKinds restart)
    {
        return restart switch
        {
            RestartKinds.None => NONE,
            RestartKinds.NextSpawn => NEXT_SPAWN,
            RestartKinds.Host => HOST,
            _ => throw new InvalidOperationException($"Unhandled RestartKinds: {restart}"),
        };
    }
}

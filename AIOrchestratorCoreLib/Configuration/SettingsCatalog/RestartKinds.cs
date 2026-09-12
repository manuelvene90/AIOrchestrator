namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// How far a change has to travel before it takes effect: <c>None</c> — the provider's write-stamp
/// reload picks it up on the next tick; <c>NextSpawn</c> — the sessions running now keep the old
/// value, the next one spawned gets the new one; <c>Host</c> — the app or the daemon has to be
/// restarted.
///
/// <para>
/// SHOWN BY THE RENDERERS AND ENFORCED BY NOBODY, deliberately (spec §6.1). An app that refused to
/// save a Host-level change until it could restart itself would be an app the owner cannot configure
/// from their phone, which is the thing this catalogue exists to make possible.
/// </para>
/// </summary>
public enum RestartKinds
{
    None,
    NextSpawn,
    Host,
}

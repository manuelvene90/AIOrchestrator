namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

public static class SettingsMenuState_Factory
{
    /// <summary>
    /// The state as the engine-state file last held it (ruling P23: persisted, so a restart leaves no invisible
    /// trap). A step restored past its deadline is kept as it is: the decider answers it <c>Lapsed</c> on the
    /// next message, at its ORIGINAL deadline, rather than this factory quietly deciding for it.
    /// </summary>
    public static ISettingsMenuState Create_Restored(long? liveMenuMessageId, IReadOnlyList<ISettingsReplyStep> steps)
    {
        return new SettingsMenuStateModel(liveMenuMessageId, steps);
    }
}

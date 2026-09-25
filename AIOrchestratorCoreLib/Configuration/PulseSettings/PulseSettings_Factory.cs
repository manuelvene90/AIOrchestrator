namespace AIOrchestratorCoreLib.Configuration.PulseSettings;

public static class PulseSettings_Factory
{
    /// <summary>
    /// THE LISTS ARE COPIED, so the block is immutable in fact and not only in its interface: a caller
    /// that handed in a <c>List&lt;string&gt;</c> and later added to it would otherwise change a config
    /// object every seam reads as a snapshot.
    ///
    /// <para>
    /// No <c>Create_Default</c>, for the reason <c>PhoneSettings_Factory</c> gives: the shipped defaults
    /// live in the settings catalogue, and <see cref="PulseSettings_Json.Parse"/> over a null tree is
    /// already them.
    /// </para>
    /// </summary>
    public static IPulseSettings Create(
        IReadOnlyList<string> fields,
        int stepMinutes,
        IReadOnlyList<string> buttons,
        IReadOnlyList<string> generalButtons,
        bool holdToggle,
        bool unchangedFor)
    {
        return new PulseSettingsModel([.. fields], stepMinutes, [.. buttons], [.. generalButtons], holdToggle, unchangedFor);
    }
}

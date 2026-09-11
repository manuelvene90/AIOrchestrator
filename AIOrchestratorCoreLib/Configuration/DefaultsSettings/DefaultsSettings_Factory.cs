namespace AIOrchestratorCoreLib.Configuration.DefaultsSettings;

public static class DefaultsSettings_Factory
{
    /// <summary>
    /// BASIC, and it is the owner's own choice rather than a programmer's. They set it on 2026-08-13
    /// to stop a crew being spawned for work that needs one session, and nothing about making the key
    /// editable is meant to change what an untouched machine does.
    /// </summary>
    public const bool DEFAULT_ORCHESTRATION_IS_BASIC = true;

    public static IDefaultsSettings Create(bool? orchestrationIsBasic)
    {
        return new DefaultsSettingsModel(orchestrationIsBasic ?? DEFAULT_ORCHESTRATION_IS_BASIC);
    }

    /// <summary>What an absent <c>defaults</c> block means.</summary>
    public static IDefaultsSettings Create_Default()
    {
        return Create(null);
    }
}

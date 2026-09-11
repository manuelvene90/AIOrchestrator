using AIOrchestratorCoreLib.Mirroring;

namespace AIOrchestratorCoreLib.Configuration.TelegramProseSettings;

public static class TelegramProseSettings_Factory
{
    /// <summary>
    /// The defaults are the components' own, NOT restated here. A second copy of 900 in this file is
    /// exactly the drift CLAUDE.md decision 12 forbids: the number would then mean one thing to the
    /// folder and another to a machine whose config.json is silent.
    /// </summary>
    public static ITelegramProseSettings Create(int? foldLongEntriesAbove, int? attachEntriesAbove)
    {
        return new TelegramProseSettingsModel(
            foldLongEntriesAbove ?? OwnerMessage_Folder.DEFAULT_FOLD_THRESHOLD,
            attachEntriesAbove ?? OwnerDocument_Builder.DEFAULT_ATTACH_ABOVE_CHUNKS);
    }

    /// <summary>What an absent <c>telegram</c> block means.</summary>
    public static ITelegramProseSettings Create_Default()
    {
        return Create(null, null);
    }
}

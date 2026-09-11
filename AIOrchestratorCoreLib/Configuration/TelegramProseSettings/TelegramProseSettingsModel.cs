namespace AIOrchestratorCoreLib.Configuration.TelegramProseSettings;

internal sealed class TelegramProseSettingsModel(int foldLongEntriesAbove, int attachEntriesAbove) : ITelegramProseSettings
{
    public int FoldLongEntriesAbove { get; } = foldLongEntriesAbove;
    public int AttachEntriesAbove { get; } = attachEntriesAbove;
}

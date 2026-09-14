using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Configuration.PhoneSettings;

internal sealed class PhoneSettingsModel(
    PhonePushModes push,
    bool periodicStatus,
    int periodicStatusIntervalMinutes,
    bool appMessagesRing,
    ReplyKeyboardModes replyKeyboard,
    ReceiptStyles receipts,
    TopicCloseActions topicOnClose,
    ModeGlyphPlacements topicModeGlyphs) : IPhoneSettings
{
    public PhonePushModes Push { get; } = push;
    public bool PeriodicStatus { get; } = periodicStatus;
    public int PeriodicStatusIntervalMinutes { get; } = periodicStatusIntervalMinutes;
    public bool AppMessagesRing { get; } = appMessagesRing;
    public ReplyKeyboardModes ReplyKeyboard { get; } = replyKeyboard;
    public ReceiptStyles Receipts { get; } = receipts;
    public TopicCloseActions TopicOnClose { get; } = topicOnClose;
    public ModeGlyphPlacements TopicModeGlyphs { get; } = topicModeGlyphs;
}

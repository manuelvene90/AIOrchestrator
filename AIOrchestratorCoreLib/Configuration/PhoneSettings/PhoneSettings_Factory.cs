using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Configuration.PhoneSettings;

public static class PhoneSettings_Factory
{
    /// <summary>
    /// EVERY VALUE IS STATED — there is no <c>Create_Default</c> here, unlike the guardrail and prose
    /// factories. The shipped defaults have ONE home, the settings catalogue, and
    /// <see cref="PhoneSettings_Json.Parse"/> over a null tree already IS those defaults; a second
    /// factory method restating them would be the second copy CLAUDE.md decision 12 forbids.
    /// </summary>
    public static IPhoneSettings Create(
        PhonePushModes push,
        bool periodicStatus,
        int periodicStatusIntervalMinutes,
        bool appMessagesRing,
        ReplyKeyboardModes replyKeyboard,
        ReceiptStyles receipts,
        TopicCloseActions topicOnClose,
        ModeGlyphPlacements topicModeGlyphs)
    {
        return new PhoneSettingsModel(
            push,
            periodicStatus,
            periodicStatusIntervalMinutes,
            appMessagesRing,
            replyKeyboard,
            receipts,
            topicOnClose,
            topicModeGlyphs);
    }
}

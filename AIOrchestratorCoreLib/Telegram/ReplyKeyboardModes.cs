namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHETHER THE BAR OF LITERAL SLASH COMMANDS SITS ABOVE THE INPUT BOX — the catalogue's
/// <c>phone.replyKeyboard</c>.
///
/// <para>
/// OFF FOR BOTH PRESETS (owner's answer to D5, 2026-09-14). <c>classic</c> used to state <c>on</c>;
/// it no longer states the key at all, so both machines read the shipped <c>off</c>. The option stays
/// because a reply keyboard is a legitimate choice for a phone that wants one — at the price
/// <see cref="TopicCommandButtons"/>' own history records: Telegram anchors the bar to the message
/// that delivered it, so it needs one permanent carrier line.
/// </para>
/// </summary>
public enum ReplyKeyboardModes
{
    /// <summary>No reply keyboard. The shipped default, and both presets' answer.</summary>
    Off,

    /// <summary>
    /// A reply keyboard, anchored to one permanent carrier message — ONCE WIRED. Nothing installs it
    /// today: under D5's "off for both" plan 03 task 9 took its implement-nothing branch, so this value
    /// resolves and is read by nobody (the guard in <c>ReplyKeyboardMarkupTests</c> pins that).
    /// </summary>
    On,
}

/// <summary>Reads a <c>phone.replyKeyboard</c> word without a switch at each call site.</summary>
public static class ReplyKeyboard_Modes
{
    /// <summary>The catalogue's words for <c>phone.replyKeyboard</c> — the drift between the two is pinned by <c>PhoneSettingsJsonTests</c>.</summary>
    public const string OFF_TEXT = "off";
    public const string ON_TEXT = "on";

    /// <summary>
    /// Parses the catalogue path <c>phone.replyKeyboard</c>. Ordinal-case-insensitive; an absent,
    /// blank or UNRECOGNISED word reads as <see cref="ReplyKeyboardModes.Off"/>, the catalogue's
    /// shipped default — the direction that posts nothing.
    /// </summary>
    public static ReplyKeyboardModes Parse_OrOff(string? word)
    {
        return string.Equals(word?.Trim(), ON_TEXT, StringComparison.OrdinalIgnoreCase)
            ? ReplyKeyboardModes.On
            : ReplyKeyboardModes.Off;
    }
}

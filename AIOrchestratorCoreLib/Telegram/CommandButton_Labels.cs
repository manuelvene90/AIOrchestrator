namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHAT A COMMAND BUTTON SAYS — verb in, label out, for either bar (plan 03 Task 5).
///
/// <para>
/// THE LABELS USED TO LIVE INSIDE THE TWO BAR ARRAYS, one (verb, label) pair per button, which was fine
/// while the arrays WERE the bars. <c>pulse.buttons</c> and <c>general.buttons</c> made the bars lists of
/// verbs an owner chooses, and a label kept beside a verb in a private array can only be found for the
/// verbs that array happened to name — classic's own bar names six verbs no array ever labelled.
/// </para>
/// <para>
/// A VERB THIS TABLE DOES NOT KNOW RENDERS AS ITS BARE SLASH COMMAND (D3, answered 2026-09-14: the
/// labelled verbs keep their emoji, every other verb renders as <c>/verb</c>). A button reading
/// <c>/screen</c> is usable; a verb with no label that could not be rendered at all would make a preset
/// the validator accepted impossible to draw. The emoji is a nicety, the slash command is the contract.
/// </para>
/// <para>
/// Every label is emoji-then-slash-command: the emoji is what the eye finds on a crowded screen, the
/// slash command is what makes the button's EFFECT unambiguous — a picture alone leaves the owner
/// guessing which of two similar glyphs merges and which closes.
/// </para>
/// </summary>
public static class CommandButton_Labels
{
    /// <summary>
    /// THE ELEVEN LABELS THAT EXISTED, CARRIED VERBATIM — eleven rows across the two old arrays, nine
    /// verbs, because /pending and /limits sat on both bars under the same label.
    ///
    /// <para>
    /// ⏳ for /pending is not a free choice: it is the same glyph PULSE's own "waiting on you" field
    /// uses, so the button and the field it expands read as one thing.
    /// </para>
    /// <para>
    /// 🌙 for /dnd_all is <see cref="TelegramDeliveryMode_Glyphs.DEFERRED"/>'s own character — the glyph
    /// the topics themselves wear while deferred, so the button and the state it puts them all into read
    /// as one thing.
    /// </para>
    /// <para>
    /// "tail sup" IS THE VERB, SPACE INCLUDED: /tail takes a target and a tap arrives with no text to
    /// carry one in, so the target rides inside the verb (see <see cref="TopicCommandButtons"/>).
    /// </para>
    /// </summary>
    static readonly Dictionary<string, string> LABELS = new(StringComparer.Ordinal)
    {
        ["pending"] = "⏳ /pending",
        ["left"] = "📋 /left",
        ["tail sup"] = "👀 /tail sup",
        ["limits"] = "📉 /limits",
        ["merge"] = "🔀 /merge",
        ["close"] = "🏁 /close",
        ["summary"] = "📊 /summary",
        ["resume"] = "▶ /resume",
        ["dnd_all"] = "🌙 /dnd_all",
    };

    /// <summary>The verb's label, or <c>/verb</c> when this table has none. Ordinal, like the parser.</summary>
    public static string For(string verb)
    {
        return LABELS.TryGetValue(verb, out var label) ? label : "/" + verb;
    }
}

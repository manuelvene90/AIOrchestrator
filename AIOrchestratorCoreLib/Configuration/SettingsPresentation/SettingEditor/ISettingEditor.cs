using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;

/// <summary>
/// ONE READING AS AN EDITABLE CONTROL — everything the WPF window would otherwise have to decide for itself
/// (plan 04 Task 9). The window is UI-relaxed and untestable by this suite (its project is
/// <c>net10.0-windows</c>; the tests are <c>net10.0</c> and there is no UI harness), so which control a row
/// gets, what its box holds, which list words may move, and the VALUE an edit produces all live here, and the
/// window binds and forwards. The reading's own members (label, origin, restart, session note) are read off
/// <see cref="Reading"/> unchanged — this adds only what a control needs beyond them.
///
/// <para>
/// EVERY <c>Build_*</c> PRODUCES A VALUE FOR <c>Settings_Writer</c>, NEVER A VERDICT (decision 21). Typed text
/// goes through <c>SettingValue_Parser.Parse</c> (ruling P13) and nothing else; a list edit is made on the
/// JSON elements themselves, so a blank word that READS as <c>SettingValue_Formatter.BLANK_WORD</c> is moved
/// or removed as the blank word it is — never re-parsed from its caption into a literal two-quote word.
/// </para>
/// </summary>
public interface ISettingEditor
{
    ISettingReading Reading { get; }

    SettingEditorKinds Kind { get; }

    /// <summary>
    /// What a Number or Text box holds when drawn: the value's own text ("" when nothing is set — never the
    /// display phrase "not set", which Apply would send back as a word). Empty for every other kind.
    /// </summary>
    string EditText { get; }

    /// <summary>"1 – 120", "at least 1", "at most 5" — the web page's words — for a Number row with a bound; null otherwise. Drawn, never enforced.</summary>
    string? RangeHint_OrNull { get; }

    /// <summary>A Toggle's state: true only when the value is JSON <c>true</c>.</summary>
    bool IsOn { get; }

    /// <summary>The offered caption that is the current value (a Choice's selected item), or null when none is.</summary>
    string? CurrentOffer_OrNull { get; }

    /// <summary>A list's elements in order, each captioned by <c>SettingValue_Formatter.Describe_ListWord</c>, with whether it can move.</summary>
    IReadOnlyList<(string Caption, bool CanMoveUp, bool CanMoveDown)> Items { get; }

    /// <summary>A picker's offers not already in the list — what "Add" may pick. Empty for every other kind.</summary>
    IReadOnlyList<string> RemainingOffers { get; }

    /// <summary>
    /// Whether a Reset has anything to delete: the row is editable and config.json is the layer that answered.
    /// A preset or shipped value has no key here to remove — the web page shows its Reset under the same rule.
    /// </summary>
    bool CanReset { get; }

    /// <summary>Typed text (a box, or a Choice caption) as the value to write — <c>SettingValue_Parser.Parse</c>, verbatim.</summary>
    JsonNode? Build_FromText(string text);

    /// <summary>
    /// A Secret row's typed text as the value to write, or null when the box is blank — which is NO EDIT, never
    /// "clear the secret": the box is always drawn empty (the value is masked), so a blank box says nothing about
    /// what the owner wants. Clearing is the row's Reset alone.
    /// </summary>
    JsonNode? Build_Secret_OrNull(string text);

    /// <summary>
    /// Whether a value is what the row already reads — then an Apply has nothing to write. Without it, Apply on an
    /// untouched box wrote the RESOLVED value back and turned "from preset classic" into "set here" with no change
    /// by the owner (review of 8be367a; the P11 rule that a save never materialises a preset value). JSON-equal,
    /// so 25 typed and 25 in the file are the same value however each was parsed.
    /// </summary>
    bool Is_Unchanged(JsonNode? value);

    /// <summary>A Toggle's new state as the value to write, through the parser's own on/off words.</summary>
    JsonNode? Build_FromToggle(bool isOn);

    /// <summary>The whole list with the element at <paramref name="index"/> moved by <paramref name="delta"/>.</summary>
    JsonNode Build_Moved(int index, int delta);

    /// <summary>The whole list without the element at <paramref name="index"/>.</summary>
    JsonNode Build_Removed(int index);

    /// <summary>
    /// The whole list with the typed (or picked) words appended — the text parsed as the row's own list kind,
    /// so commas separate and a blank word is dropped (P13) — or null when the text holds no word to add.
    /// </summary>
    JsonNode? Build_Added_OrNull(string text);
}

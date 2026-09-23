using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Telegram.SettingsMenu;

/// <summary>
/// THE TELEGRAM /settings MENU AS A PURE FUNCTION (plan 04 Task 4, spec §8.2): a view plus the readings in,
/// the message text and its inline keyboard out. The engine (Task 5) resolves the readings once and sends or
/// edits what this returns; nothing here reads a file, holds state or talks to Telegram, so every word and
/// every payload the phone will show is provable without a bot.
///
/// <para>
/// A DRAWING LAYER ONLY (CLAUDE.md decision 12). Every value reads as its <c>DisplayValue</c>, every origin as
/// its <c>OriginLabel</c>, every restart as its <c>RestartLabel</c>, the section titles and the preset header
/// come from <see cref="SettingsRow_Builder"/>, and which control a row gets comes from
/// <see cref="SettingEditor_Factory"/> — the same decision the WPF window draws from. A value this menu shows
/// that is not a reading (the value a Confirm is about to write) goes through <see cref="SettingValue_Parser"/>
/// and <see cref="SettingValue_Formatter"/>, never through a phrase of its own. It never decides a value is
/// acceptable either (decision 21): a refusal is the definition's, and Task 5 relays it.
/// </para>
/// <para>
/// THE OWNER'S TWO ANSWERS. D2: <see cref="PHONE_FENCED_PATHS"/> are shown and refused with
/// <see cref="PHONE_FENCE_NOTE"/> — they decide how this very menu reaches the owner, and a foot-gun whose
/// recovery path is not the phone is not a confirm dialog's problem — and every other editable Kernel row takes
/// a second tap through the <see cref="SettingsMenuViews.Confirm"/> view (ruling P6). D3: an orchestration
/// topic gets the read-only <see cref="SettingsMenuViews.Orchestration"/> view, never the machine menu.
/// </para>
/// <para>
/// THE OUTPUT IS PLAIN TEXT, AND IT IS ROWS. Labels, descriptions and values carry <c>&lt;</c>, <c>&gt;</c>
/// and <c>&amp;</c> freely (a highRiskPatterns word, a runner description), and none of it is escaped here. The
/// engine (Task 5) must either send it without a parse mode —
/// <c>ITelegramApiClient.Send_MessageWithButtonRows_Async</c> / <c>Edit_MessageTextWithButtonRows_Async</c>
/// set none — or HTML-escape it before any <c>parse_mode = "HTML"</c> path; and it must send the keyboard as
/// the ROWS returned, not through <c>Send_HtmlMessageWithButtons_Async</c>, which takes a flat button list and
/// would stack the page row's Previous / Back / Next and a Values view's word pairs one per line.
/// </para>
/// </summary>
public static class SettingsMenu_Builder
{
    /// <summary>
    /// SETTING BUTTONS PER PAGE — a PHONE SCREEN, not an API limit. Telegram accepts a hundred buttons; a phone
    /// shows about eight full-width rows above the keyboard before the owner has to scroll inside a chat
    /// bubble, which is where menus get lost. Kernel alone is 37+ rows, so a category pages rather than grows,
    /// and every view's keyboard is at most this many rows plus one navigation row however large the catalogue
    /// gets (the owner asked whether Kernel on a phone was even possible — this is the answer).
    /// </summary>
    public const int ROWS_PER_PAGE = 8;

    /// <summary>
    /// Words per row in a Values view. Two, because the longest captions ("✕ pulseHeader", "✕ tail sup") stay
    /// whole side by side on a phone, and three did not — a clipped word is a word the owner cannot tell apart
    /// from its neighbour.
    /// </summary>
    public const int VALUES_PER_ROW = 2;

    /// <summary>Marks the Choice word that is the current value.</summary>
    public const string CURRENT_MARK = "✓ ";

    /// <summary>A picker word not in the list: a tap appends it.</summary>
    public const string ADD_MARK = "+ ";

    /// <summary>A picker word already in the list: a tap takes it out.</summary>
    public const string REMOVE_MARK = "✕ ";

    public const string BACK = "⬅ Back";

    public const string RESET = "↺ Reset";

    const string PREVIOUS = "◀ Previous";

    const string NEXT = "Next ▶";

    const string CONFIRM_YES = "✓ Yes, change it";

    const string CHOOSE = "Choose a value…";

    const string PICK_WORDS = "Add or remove words…";

    const string REPLY = "✎ Reply with a new value";

    const string REPLY_WHOLE_LIST = "✎ Type the whole list";

    /// <summary>
    /// THE THREE KEYS THE PHONE REFUSES (owner answer D2 (b), 2026-09-14). <c>telegramInbound: off</c> stops this
    /// host polling getUpdates — no more inbound from the phone at all; the two ids decide which chat and which
    /// human the bridge answers. Each can cut the owner off from the surface they would be typing the fix on, so
    /// the phone shows them and says where they ARE changed. Paths, checked against the catalogue by a test.
    /// </summary>
    public static readonly IReadOnlyList<string> PHONE_FENCED_PATHS = ["telegramInbound", "telegramSupergroupChatId", "telegramOwnerUserId"];

    /// <summary>What a fenced key says in place of its buttons — and what a tap on one is answered with (Task 5).</summary>
    public const string PHONE_FENCE_NOTE = "Change this at the desktop or in config.json — it is how this menu reaches you.";

    /// <summary>Said wherever a Kernel change starts: the rest of Kernel takes a second tap (D2 (a), ruling P6).</summary>
    public const string KERNEL_CONFIRM_NOTE = "Kernel settings ask you to confirm before anything is saved.";

    const string CATEGORIES_NOTE = "This machine's settings, one section at a time. A change is saved the moment you make it.";

    const string CATEGORY_NOTE = "Tap a setting to see what it does and to change it.";

    const string CHOICE_NOTE = "Tap the value you want.";

    const string CONFIRM_NOTE = "Nothing changes until you tap Yes.";

    /// <summary>A Secret row's Confirm: the typed value is not repeated, not even cut short.</summary>
    const string SECRET_QUESTION = "Save the value you typed? It is a secret, so it is not shown here.";

    const string RESET_QUESTION = "Reset it? Its key is deleted from config.json, and the preset or the shipped default answers again.";

    /// <summary>
    /// D3: the orchestration topic's answer points at what DOES change these rows. The dials are decision 24's
    /// (/model, /effort); the three state rows are the app's, toggled by their own commands (their catalogue
    /// descriptions name them); the machine's settings live in General.
    /// </summary>
    const string ORCHESTRATION_NOTE =
        "Read-only here. Change the model with /model and the effort with /effort; /pause, /dnd, /mute and /pc change " +
        "the three state rows. This machine's settings are under /settings in the General topic.";

    static readonly IReadOnlyDictionary<string, int> INDEX_BY_PATH = Build_IndexByPath();

    /// <summary>
    /// Whether the phone may change this row: the writer's own rule (<c>IsEditable</c>, ruling P4) minus the
    /// three D2 keys. Task 5 asks this before it writes, so a forged or stale payload for a fenced key is
    /// refused by the same rule that drew no button for it.
    /// </summary>
    public static bool Is_EditableOnThePhone(ISettingReading reading)
    {
        return reading.IsEditable && !PHONE_FENCED_PATHS.Contains(reading.Definition.Path);
    }

    /// <summary>Whether a change to this row lands on a Confirm view first (D2 (a)): every Kernel row the phone may edit.</summary>
    public static bool Needs_Confirm(ISettingDefinition definition)
    {
        return definition.Category == SettingCategories.Kernel;
    }

    /// <summary>
    /// One view. <paramref name="category"/> is read by the Category view only; <paramref name="settingIndex"/>
    /// (into <c>SettingsCatalog.ALL</c>) by Setting, Values and Confirm; <paramref name="page"/> by Category and
    /// Values; <paramref name="edit"/> and <paramref name="word"/> by Confirm, which shows the change it asks
    /// about — for <see cref="SettingsMenuEdits.ApplyHeldReply"/> the word is the text the reply step holds. The
    /// caller has already resolved the payload (<see cref="SettingsButton_Data.Resolve_OrNull"/>), so a missing
    /// argument here is a caller's bug and throws.
    /// </summary>
    public static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build(
        SettingsMenuViews view,
        SettingCategories? category,
        int? settingIndex,
        int page,
        SettingsMenuEdits? edit,
        string? word,
        IReadOnlyList<ISettingReading> readings,
        string presetName)
    {
        return view switch
        {
            SettingsMenuViews.Categories => Build_Categories(readings, presetName),
            SettingsMenuViews.Category => Build_Category(category ?? throw new ArgumentException("a Category view needs its category", nameof(category)), page, readings, presetName),
            SettingsMenuViews.Setting => Build_Setting(Find_Reading(settingIndex, readings), readings, presetName),
            SettingsMenuViews.Values => Build_Values(Find_Reading(settingIndex, readings), page, readings, presetName),
            SettingsMenuViews.Confirm => Build_Confirm(Find_Reading(settingIndex, readings), page, edit ?? throw new ArgumentException("a Confirm view needs the change it confirms", nameof(edit)), word, readings, presetName),
            SettingsMenuViews.Orchestration => Build_Orchestration(readings, presetName),
            _ => throw new InvalidOperationException($"Unhandled SettingsMenuViews: {view}"),
        };
    }

    /// <summary>
    /// The "reply with the value" prompt (spec §8.2, D9) — the text only; the step itself is Task 5's state. It
    /// names <c>/cancel</c> and the window because the step can swallow the owner's next message, and a trap the
    /// prompt does not describe is an invisible one (ruling P15: the builder names /cancel, Task 5 consumes it).
    /// A picker's words are listed as CANDIDATES (ruling P39) under the setting's own label, so "progress" is
    /// never ambiguous between the PULSE field and the bar button.
    /// </summary>
    public static string Build_ReplyPrompt(ISettingReading reading, int expiryMinutes)
    {
        var definition = reading.Definition;
        var editor = SettingEditor_Factory.Create_ForReading(reading);
        var text = new StringBuilder();

        text.AppendLine($"Reply with the new value for {definition.Label} ({definition.Path}).");
        text.AppendLine(Describe_Now(reading));

        if (editor.RangeHint_OrNull != null)
            text.AppendLine($"Range: {editor.RangeHint_OrNull}.");

        if (editor.Kind is SettingEditorKinds.WordPicker or SettingEditorKinds.FreeTextList)
            text.AppendLine($"Separate the words with commas — what you send replaces the whole of {definition.Label}, in that order.");

        if (reading.OfferedValues.Count > 0 && editor.Kind == SettingEditorKinds.WordPicker)
            text.AppendLine($"Words {definition.Label} knows: {string.Join(", ", reading.OfferedValues)}.");

        if (editor.Kind == SettingEditorKinds.Secret)
            text.AppendLine("The value is never shown back. Your message stays in this chat — delete it once it is saved.");
        else if (definition.Validate_OrNull(null) == null && editor.Kind is not (SettingEditorKinds.WordPicker or SettingEditorKinds.FreeTextList))
            text.AppendLine($"Send \"{SettingValue_Formatter.NOT_SET}\" to state nothing.");

        if (Needs_Confirm(definition))
            text.AppendLine(KERNEL_CONFIRM_NOTE);

        text.Append($"Send /cancel to stop. Any other /command cancels this too, and it lapses after {Describe_Minutes(expiryMinutes)}.");

        return text.ToString();
    }

    static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_Categories(IReadOnlyList<ISettingReading> readings, string presetName)
    {
        List<IReadOnlyList<(string Data, string Label)>> rows = [];

        // A section with nothing in it is not offered (Kit until plan 05): computed, so the day it fills, it appears.
        foreach (var section in SettingsRow_Builder.Build_Sections(readings).Where(section => section.Rows.Count > 0))
        {
            var caption = $"{section.Title} ({section.Rows.Count.ToString(CultureInfo.InvariantCulture)})";

            rows.Add([(SettingsButton_Data.Build(SettingsMenuViews.Category, section.Category, null, 0, null, null), caption)]);
        }

        return (Join_Lines(Describe_Header(presetName), CATEGORIES_NOTE, KERNEL_CONFIRM_NOTE), rows);
    }

    static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_Category(
        SettingCategories category, int page, IReadOnlyList<ISettingReading> readings, string presetName)
    {
        var section = SettingsRow_Builder.Build_Sections(readings).Single(candidate => candidate.Category == category);
        var backToCategories = SettingsButton_Data.Build(SettingsMenuViews.Categories, null, null, 0, null, null);

        if (section.Rows.Count == 0)
            return (Join_Lines(Describe_Header(presetName), section.Title, SettingsRow_Builder.EMPTY_SECTION_NOTE), [[(backToCategories, BACK)]]);

        var pages = Count_Pages(section.Rows.Count, ROWS_PER_PAGE);
        var shown = Math.Clamp(page, 0, pages - 1);
        var onPage = section.Rows.Skip(shown * ROWS_PER_PAGE).Take(ROWS_PER_PAGE).ToArray();

        List<string> lines = [Describe_Header(presetName), pages > 1 ? $"{section.Title} — {Describe_Page(shown, pages)}" : section.Title, string.Empty];
        List<IReadOnlyList<(string Data, string Label)>> rows = [];

        foreach (var reading in onPage)
        {
            lines.Add($"• {Describe_ValueLine(reading)}");
            rows.Add([(SettingsButton_Data.Build(SettingsMenuViews.Setting, null, Index_Of(reading.Definition), 0, null, null), $"{reading.Definition.Label}: {reading.DisplayValue}")]);
        }

        lines.Add(string.Empty);
        lines.Add(CATEGORY_NOTE);

        if (category == SettingCategories.Kernel)
            lines.Add(KERNEL_CONFIRM_NOTE);

        rows.Add(Build_PageRow(
            shown,
            pages,
            previous: SettingsButton_Data.Build(SettingsMenuViews.Category, category, null, shown - 1 < 0 ? 0 : shown - 1, null, null),
            back: backToCategories,
            next: SettingsButton_Data.Build(SettingsMenuViews.Category, category, null, shown + 1, null, null)));

        return (Join_Lines([.. lines]), rows);
    }

    static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_Setting(
        ISettingReading reading, IReadOnlyList<ISettingReading> readings, string presetName)
    {
        var definition = reading.Definition;
        var index = Index_Of(definition);
        var back = (SettingsButton_Data.Build(SettingsMenuViews.Category, definition.Category, null, Find_PageOf(reading, readings), null, null), BACK);
        List<IReadOnlyList<(string Data, string Label)>> rows = [];
        List<string> lines = [.. Describe_SettingHead(reading, presetName)];

        lines.Add(reading.RestartLabel);
        lines.Add(string.Empty);
        lines.Add(definition.Description);
        lines.Add(string.Empty);

        if (!reading.IsEditable)
        {
            lines.Add(SettingsRow_Builder.READ_ONLY_NOTE);
            rows.Add([back]);

            return (Join_Lines([.. lines]), rows);
        }

        if (!Is_EditableOnThePhone(reading))
        {
            lines.Add(PHONE_FENCE_NOTE);
            rows.Add([back]);

            return (Join_Lines([.. lines]), rows);
        }

        var editor = SettingEditor_Factory.Create_ForReading(reading);
        var landing = Needs_Confirm(definition) ? SettingsMenuViews.Confirm : SettingsMenuViews.Setting;
        var reply = SettingsButton_Data.Build(SettingsMenuViews.Setting, null, index, 0, SettingsMenuEdits.Reply, null);

        switch (editor.Kind)
        {
            case SettingEditorKinds.Toggle:
            {
                var opposite = editor.IsOn ? SettingValue_Formatter.OFF : SettingValue_Formatter.ON;

                rows.Add([(SettingsButton_Data.Build(landing, null, index, 0, SettingsMenuEdits.Set, opposite), $"Turn {opposite}")]);
                break;
            }

            case SettingEditorKinds.Choice:
                rows.Add([(SettingsButton_Data.Build(SettingsMenuViews.Values, null, index, 0, null, null), CHOOSE)]);
                break;

            case SettingEditorKinds.WordPicker:
                rows.Add([(SettingsButton_Data.Build(SettingsMenuViews.Values, null, index, 0, null, null), PICK_WORDS)]);
                rows.Add([(reply, REPLY_WHOLE_LIST)]);
                break;

            case SettingEditorKinds.Number:
            case SettingEditorKinds.Text:
            case SettingEditorKinds.Secret:
            case SettingEditorKinds.FreeTextList:
                rows.Add([(reply, REPLY)]);
                break;

            case SettingEditorKinds.ReadOnly:
                throw new InvalidOperationException($"'{definition.Path}' is editable but its editor is ReadOnly — SettingEditor_Factory and IsEditable disagree");

            default:
                throw new InvalidOperationException($"Unhandled SettingEditorKinds: {editor.Kind}");
        }

        if (editor.CanReset)
            rows.Add([(SettingsButton_Data.Build(landing, null, index, 0, SettingsMenuEdits.Reset, null), RESET)]);

        if (Needs_Confirm(definition))
            lines.Add(KERNEL_CONFIRM_NOTE);

        rows.Add([back]);

        return (Join_Lines([.. lines]), rows);
    }

    /// <summary>
    /// A Choice's words, or a picker list's words, paged. A row with nothing to offer on the phone — fenced,
    /// read-only, free text — draws its Setting view instead: a stale or forged Values payload must still land
    /// on the page that says why there is nothing to pick.
    /// </summary>
    static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_Values(
        ISettingReading reading, int page, IReadOnlyList<ISettingReading> readings, string presetName)
    {
        var definition = reading.Definition;
        var editor = SettingEditor_Factory.Create_ForReading(reading);
        var isChoice = editor.Kind is SettingEditorKinds.Choice or SettingEditorKinds.Toggle;
        var isPicker = editor.Kind == SettingEditorKinds.WordPicker;

        if (!Is_EditableOnThePhone(reading) || reading.OfferedValues.Count == 0 || !(isChoice || isPicker))
            return Build_Setting(reading, readings, presetName);

        var index = Index_Of(definition);
        var perPage = ROWS_PER_PAGE * VALUES_PER_ROW;
        var pages = Count_Pages(reading.OfferedValues.Count, perPage);
        var shown = Math.Clamp(page, 0, pages - 1);
        var present = Read_ListWords(reading);
        List<string> lines = [.. Describe_SettingHead(reading, presetName)];

        lines.Add(string.Empty);
        lines.Add(isPicker
            ? $"Tap a word to add it to the end of {definition.Label}, or a {REMOVE_MARK.Trim()} word to take it out. Each tap changes one word of the current list; to reorder it, type the whole list from the setting's page."
            : CHOICE_NOTE);

        if (Needs_Confirm(definition))
            lines.Add(KERNEL_CONFIRM_NOTE);

        if (pages > 1)
            lines.Add(Describe_Page(shown, pages));

        List<IReadOnlyList<(string Data, string Label)>> rows = [];
        List<(string Data, string Label)> row = [];

        foreach (var word in reading.OfferedValues.Skip(shown * perPage).Take(perPage))
        {
            row.Add(isPicker ? Build_PickerButton(definition, index, shown, word, present.Contains(word)) : Build_ChoiceButton(editor, index, word));

            if (row.Count == VALUES_PER_ROW)
            {
                rows.Add(row);
                row = [];
            }
        }

        if (row.Count > 0)
            rows.Add(row);

        rows.Add(Build_PageRow(
            shown,
            pages,
            previous: SettingsButton_Data.Build(SettingsMenuViews.Values, null, index, shown - 1 < 0 ? 0 : shown - 1, null, null),
            back: SettingsButton_Data.Build(SettingsMenuViews.Setting, null, index, 0, null, null),
            next: SettingsButton_Data.Build(SettingsMenuViews.Values, null, index, shown + 1, null, null)));

        return (Join_Lines([.. lines]), rows);
    }

    /// <summary>
    /// THE SECOND TAP OF A KERNEL CHANGE (D2 (a), ruling P6). It shows what would change and writes nothing;
    /// its Yes carries the same change to the view the change lands on, and that tap writes
    /// (<see cref="SettingsButton_Data.Writes_OnTap"/>). A held reply's Yes carries only the id — the typed
    /// text stays in Task 5's pending step. A row the phone may not change, or a change that is not a write,
    /// draws the Setting view instead.
    /// </summary>
    static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_Confirm(
        ISettingReading reading, int page, SettingsMenuEdits edit, string? word, IReadOnlyList<ISettingReading> readings, string presetName)
    {
        if (!Is_EditableOnThePhone(reading) || edit == SettingsMenuEdits.Reply)
            return Build_Setting(reading, readings, presetName);

        var definition = reading.Definition;
        var index = Index_Of(definition);
        var landing = edit is SettingsMenuEdits.Add or SettingsMenuEdits.Remove ? SettingsMenuViews.Values : SettingsMenuViews.Setting;
        var landingPage = landing == SettingsMenuViews.Values ? page : 0;
        var carriedWord = edit is SettingsMenuEdits.Set or SettingsMenuEdits.Add or SettingsMenuEdits.Remove ? word : null;
        List<string> lines = [.. Describe_SettingHead(reading, presetName)];

        lines.Add(string.Empty);
        lines.Add(Describe_Question(reading, edit, word));
        lines.Add(reading.RestartLabel);
        lines.Add(CONFIRM_NOTE);

        return (Join_Lines([.. lines]),
        [
            [(SettingsButton_Data.Build(landing, null, index, landingPage, edit, carriedWord), CONFIRM_YES)],
            [(SettingsButton_Data.Build(landing, null, index, landingPage, null, null), BACK)],
        ]);
    }

    /// <summary>
    /// D3: the orchestration's own rows — every Orchestration-scoped reading, which is the three session.*
    /// state rows and the supervisor/implementer model and effort dials — read-only, pointing at the commands
    /// that change them. No buttons: nothing here is changed by a tap in this menu.
    /// </summary>
    static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_Orchestration(IReadOnlyList<ISettingReading> readings, string presetName)
    {
        List<string> lines = [$"This orchestration's settings — {SettingsRow_Builder.Describe_PresetHeader(presetName)}", string.Empty];

        foreach (var reading in readings.Where(reading => reading.Definition.Scope == SettingScopes.Orchestration))
        {
            lines.Add($"• {Describe_ValueLine(reading)}");

            if (reading.SessionNote_OrNull != null)
                lines.Add($"  {reading.SessionNote_OrNull}");
        }

        lines.Add(string.Empty);
        lines.Add(ORCHESTRATION_NOTE);

        return (Join_Lines([.. lines]), []);
    }

    static (string Data, string Label) Build_ChoiceButton(ISettingEditor editor, int index, string word)
    {
        var landing = Needs_Confirm(editor.Reading.Definition) ? SettingsMenuViews.Confirm : SettingsMenuViews.Setting;
        var caption = word == editor.CurrentOffer_OrNull ? CURRENT_MARK + word : word;

        return (SettingsButton_Data.Build(landing, null, index, 0, SettingsMenuEdits.Set, word), caption);
    }

    /// <summary>ONE word of the current list, added or taken out (ruling P39) — the tap lands back on this page to pick the next.</summary>
    static (string Data, string Label) Build_PickerButton(ISettingDefinition definition, int index, int page, string word, bool isPresent)
    {
        var landing = Needs_Confirm(definition) ? SettingsMenuViews.Confirm : SettingsMenuViews.Values;
        var edit = isPresent ? SettingsMenuEdits.Remove : SettingsMenuEdits.Add;

        return (SettingsButton_Data.Build(landing, null, index, page, edit, word), (isPresent ? REMOVE_MARK : ADD_MARK) + word);
    }

    /// <summary>Previous and Next only where there is one; Back always, in the middle where the thumb expects it.</summary>
    static IReadOnlyList<(string Data, string Label)> Build_PageRow(int page, int pages, string previous, string back, string next)
    {
        List<(string Data, string Label)> row = [];

        if (page > 0)
            row.Add((previous, PREVIOUS));

        row.Add((back, BACK));

        if (page < pages - 1)
            row.Add((next, NEXT));

        return row;
    }

    /// <summary>
    /// What the Confirm asks. The NEW value is drawn by the one formatter over the one parser — the word a tap
    /// carries, or the text the reply step holds — so it reads exactly as the row will read once written.
    /// </summary>
    static string Describe_Question(ISettingReading reading, SettingsMenuEdits edit, string? word)
    {
        var definition = reading.Definition;
        var editor = SettingEditor_Factory.Create_ForReading(reading);

        // A SECRET'S TYPED VALUE IS NEVER DRAWN (fix round 1 of 7763a6b, ruling P40). "Change it to <token>?"
        // was a second, BOT-sent copy of web.token in the chat — one the owner cannot delete — right after the
        // reply prompt promised the value is never shown back. Keyed on the editor's Secret kind, which
        // SettingEditor_Factory derives from the reader's one masked path, so no second path literal exists here.
        if (editor.Kind == SettingEditorKinds.Secret && edit is SettingsMenuEdits.Set or SettingsMenuEdits.ApplyHeldReply)
            return SECRET_QUESTION;

        return edit switch
        {
            SettingsMenuEdits.Set or SettingsMenuEdits.ApplyHeldReply when word != null =>
                $"Change it to {SettingValue_Formatter.Describe(definition, SettingValue_Parser.Parse(definition, word))}?",
            SettingsMenuEdits.Set or SettingsMenuEdits.ApplyHeldReply => "Change it to the value you typed?",
            SettingsMenuEdits.Add when word != null =>
                $"Add \"{word}\" to it? It becomes: {SettingValue_Formatter.Describe(definition, editor.Build_Added_OrNull(word) ?? reading.Value_OrNull)}",
            SettingsMenuEdits.Remove when word != null =>
                $"Take \"{word}\" out of it? It becomes: {SettingValue_Formatter.Describe(definition, Build_Without(editor, word))}",
            SettingsMenuEdits.Reset => RESET_QUESTION,
            _ => throw new ArgumentException($"a {edit} confirm needs the word it changes", nameof(word)),
        };
    }

    /// <summary>The list without <paramref name="word"/> — through the editor's own removal, so the element itself goes, never a re-parsed caption.</summary>
    static JsonNode? Build_Without(ISettingEditor editor, string word)
    {
        if (editor.Reading.Value_OrNull is not JsonArray array)
            return editor.Reading.Value_OrNull;

        for (var position = 0; position < array.Count; position++)
        {
            if (array[position] is JsonValue element && element.TryGetValue<string>(out var text) && text == word)
                return editor.Build_Removed(position);
        }

        return editor.Reading.Value_OrNull;
    }

    static IReadOnlyList<string> Describe_SettingHead(ISettingReading reading, string presetName)
    {
        var definition = reading.Definition;
        List<string> head =
        [
            Describe_Header(presetName),
            $"{SettingsRow_Builder.Describe_Title(definition.Category)} › {definition.Label}",
            $"key: {definition.Path}",
            Describe_Now(reading),
        ];

        if (reading.SessionNote_OrNull != null)
            head.Add(reading.SessionNote_OrNull);

        return head;
    }

    static string Describe_Header(string presetName)
    {
        return $"Settings — {SettingsRow_Builder.Describe_PresetHeader(presetName)}";
    }

    static string Describe_Now(ISettingReading reading)
    {
        return $"Now: {reading.DisplayValue} — {reading.OriginLabel}";
    }

    static string Describe_ValueLine(ISettingReading reading)
    {
        return $"{reading.Definition.Label}: {reading.DisplayValue} — {reading.OriginLabel}";
    }

    static string Describe_Page(int page, int pages)
    {
        return $"page {(page + 1).ToString(CultureInfo.InvariantCulture)} of {pages.ToString(CultureInfo.InvariantCulture)}";
    }

    static string Describe_Minutes(int minutes)
    {
        return minutes == 1 ? "1 minute" : $"{minutes.ToString(CultureInfo.InvariantCulture)} minutes";
    }

    static HashSet<string> Read_ListWords(ISettingReading reading)
    {
        HashSet<string> words = new(StringComparer.Ordinal);

        if (reading.Value_OrNull is JsonArray array)
        {
            foreach (var element in array)
            {
                if (element is JsonValue value && value.TryGetValue<string>(out var word))
                    words.Add(word);
            }
        }

        return words;
    }

    /// <summary>The Category page a setting sits on, so its Back returns to the page the owner tapped it from.</summary>
    static int Find_PageOf(ISettingReading reading, IReadOnlyList<ISettingReading> readings)
    {
        var section = SettingsRow_Builder.Build_Sections(readings).Single(candidate => candidate.Category == reading.Definition.Category);
        var position = section.Rows.ToList().FindIndex(candidate => candidate.Definition.Path == reading.Definition.Path);

        return position < 0 ? 0 : position / ROWS_PER_PAGE;
    }

    static ISettingReading Find_Reading(int? settingIndex, IReadOnlyList<ISettingReading> readings)
    {
        if (settingIndex is not { } index || index < 0 || index >= Catalog.ALL.Count)
            throw new ArgumentOutOfRangeException(nameof(settingIndex), settingIndex, $"a setting view names a row of SettingsCatalog.ALL (0 – {Catalog.ALL.Count - 1})");

        var path = Catalog.ALL[index].Path;

        return readings.FirstOrDefault(reading => reading.Definition.Path == path)
            ?? throw new ArgumentException($"no reading for '{path}' — the caller resolved a partial snapshot for a machine view", nameof(readings));
    }

    static int Index_Of(ISettingDefinition definition)
    {
        return INDEX_BY_PATH.TryGetValue(definition.Path, out var index)
            ? index
            : throw new ArgumentException($"'{definition.Path}' is not a row of SettingsCatalog.ALL");
    }

    static IReadOnlyDictionary<string, int> Build_IndexByPath()
    {
        Dictionary<string, int> byPath = new(StringComparer.Ordinal);

        for (var index = 0; index < Catalog.ALL.Count; index++)
            byPath[Catalog.ALL[index].Path] = index;

        return byPath;
    }

    static int Count_Pages(int count, int perPage)
    {
        return Math.Max(1, (count + perPage - 1) / perPage);
    }

    /// <summary>Lines joined, with no blank tail — a view whose closing note is absent must not end in an empty line.</summary>
    static string Join_Lines(params string[] lines)
    {
        return string.Join("\n", lines).TrimEnd('\n');
    }
}

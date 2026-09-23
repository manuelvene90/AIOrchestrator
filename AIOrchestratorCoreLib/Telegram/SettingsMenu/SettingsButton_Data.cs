using System.Globalization;
using System.Text;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Telegram.SettingsMenu;

/// <summary>
/// The callback payload behind every /settings button (plan 04 Task 4, D8, rulings P6 and P7).
///
/// <para>
/// STATELESS, LIKE THE DIALS AND UNLIKE THE OPTIONS (CLAUDE.md decision 24). <see cref="ModelEffortButton_Data"/>
/// carries its whole answer so a tap survives an app restart; the <c>opt-</c> registry does not, and a tap on
/// a restarted app answers "expired". A settings menu is a control the owner comes back to, possibly after the
/// app restarted under it, so it is the first kind: every tap names the view it lands on, the setting, the page
/// and the change, and needs no registry to be understood.
/// </para>
/// <para>
/// THE SHAPE (ruling P7): <c>set:&lt;view&gt;:&lt;cat&gt;:&lt;idx&gt;:&lt;chk&gt;:&lt;page&gt;[:&lt;op&gt;&lt;word&gt;]</c>.
/// <c>view</c> is one letter (<see cref="Describe_ViewCode"/>); <c>cat</c> the category's enum NAME, so a
/// reordered enum does not renumber it; <c>idx</c> the setting's index into <c>SettingsCatalog.ALL</c> (D8: one
/// to three characters, no registry); <c>chk</c> four hex digits of FNV-1a over the setting's Path; <c>page</c>
/// the page of the view the tap lands on; and the optional last field one operator character
/// (<see cref="Describe_EditCode"/>) followed by the word it carries. The word is LAST so a word holding the
/// separator still round-trips, and the one-character operator keeps "reset" and "reply" from ever being
/// mistaken for a word some future Enum row might offer.
/// </para>
/// <para>
/// A STALE ID IS ANSWERED, NEVER ACTED ON (D8). The index renumbers whenever a row is inserted before it, and
/// plan 03 T14/T15 insert rows MID-category (<c>topic.repoColours</c>, <c>highRiskConfirmation</c>), so a menu
/// message left open across an upgrade would, by index alone, toggle a different setting than the one whose
/// label the owner read. The category catches a row that moved between sections; the check catches one that
/// moved inside its own section. It is FNV-1a and NOT <c>string.GetHashCode</c>, which .NET randomises per
/// PROCESS — every menu would read as stale after every restart. <see cref="Resolve_OrNull"/> is the one door:
/// null means answer <see cref="STALE_ANSWER"/> and change nothing.
/// </para>
/// <para>
/// ITS OWN PREFIX AND ITS OWN BYTE ASSERT (D8), the pattern <see cref="ModelEffortButton_Data"/> and
/// <see cref="HoldButton_Data"/> set, rather than a reuse of <see cref="CallbackToken"/>, which belongs to the
/// single-use <c>opt-</c> family. "set:" is not a prefix of, nor prefixed by, "cmd:", "hold:", "go:",
/// "close-yes-", "close-no-", "model:", "effort:" or "opt-", so the parse ORDER in the tap handler cannot decide
/// what a payload means.
/// </para>
/// </summary>
public static class SettingsButton_Data
{
    public const string PREFIX = "set:";

    /// <summary>Telegram's hard cap on callback_data; a payload over it is rejected at SEND time, on the phone.</summary>
    public const int TELEGRAM_CALLBACK_DATA_BYTE_LIMIT = 64;

    /// <summary>
    /// What a tap on a menu this build cannot trust is answered with — an index outside the catalogue, a
    /// category or a check that disagrees with the row now at that index, or a "set:" payload this build cannot
    /// read at all. It changes nothing and says how to get a menu that is current.
    /// </summary>
    public const string STALE_ANSWER = "This menu is from an older version of the app — send /settings again.";

    const char FIELD_SEPARATOR = ':';

    /// <summary>view, cat, idx, chk, page — and the optional change, which is the sixth.</summary>
    const int FIELDS_WITHOUT_EDIT = 5;

    const int FIELDS_WITH_EDIT = 6;

    const uint FNV_OFFSET_BASIS = 2166136261;

    const uint FNV_PRIME = 16777619;

    public static string Build(SettingsMenuViews view, SettingCategories? category, int? settingIndex, int page, SettingsMenuEdits? edit, string? word)
    {
        if (page < 0)
            throw new ArgumentOutOfRangeException(nameof(page), page, "a page is zero or more");

        var (categoryWord, indexText, check) = Describe_Target(view, category, settingIndex);
        var data = $"{PREFIX}{Describe_ViewCode(view)}{FIELD_SEPARATOR}{categoryWord}{FIELD_SEPARATOR}{indexText}{FIELD_SEPARATOR}{check}{FIELD_SEPARATOR}{page.ToString(CultureInfo.InvariantCulture)}";

        if (edit != null)
            data += FIELD_SEPARATOR + Describe_Edit(edit.Value, word);
        else if (word != null)
            throw new ArgumentException($"a word ('{word}') rides only with a change — a navigation payload carries none", nameof(word));

        var bytes = Encoding.UTF8.GetByteCount(data);

        if (bytes > TELEGRAM_CALLBACK_DATA_BYTE_LIMIT)
            throw new ArgumentException($"callback data '{data}' is {bytes} bytes; Telegram allows at most {TELEGRAM_CALLBACK_DATA_BYTE_LIMIT}");

        return data;
    }

    /// <summary>
    /// Whether the payload is this family's at all — true for a "set:" payload <see cref="Parse_OrNull"/> cannot
    /// read, so the handler answers it with <see cref="STALE_ANSWER"/> rather than letting it fall through
    /// unanswered to handlers that will never recognise it.
    /// </summary>
    public static bool Is_Ours(string? callbackData)
    {
        return callbackData != null && callbackData.StartsWith(PREFIX, StringComparison.Ordinal);
    }

    /// <summary>
    /// The payload's fields as written, or null for anything that is not ours or that is ours but unreadable
    /// (an unknown view letter or operator, a missing field, a negative page). A readable payload is not yet a
    /// trustworthy one: <see cref="Resolve_OrNull"/> decides that, against THIS build's catalogue.
    /// </summary>
    public static (SettingsMenuViews View, string CategoryWord, int? Index, string Check, int Page, SettingsMenuEdits? Edit, string? Word)? Parse_OrNull(string? callbackData)
    {
        if (!Is_Ours(callbackData))
            return null;

        var fields = callbackData![PREFIX.Length..].Split(FIELD_SEPARATOR, FIELDS_WITH_EDIT);

        if (fields.Length < FIELDS_WITHOUT_EDIT)
            return null;

        var view = Parse_ViewCode_OrNull(fields[0]);

        if (view == null)
            return null;

        int? index = null;

        if (fields[2].Length > 0)
        {
            if (!Try_ParseCount(fields[2], out var parsedIndex))
                return null;

            index = parsedIndex;
        }

        if (!Try_ParseCount(fields[4], out var page))
            return null;

        if (fields.Length == FIELDS_WITHOUT_EDIT)
            return (view.Value, fields[1], index, fields[3], page, null, null);

        var change = Parse_Edit_OrNull(fields[5]);

        if (change == null)
            return null;

        return (view.Value, fields[1], index, fields[3], page, change.Value.Edit, change.Value.Word);
    }

    /// <summary>
    /// The payload against THIS build's catalogue, or null when it is stale (D8) — answer
    /// <see cref="STALE_ANSWER"/> and act on nothing. A view that names a setting must name one whose category
    /// AND Path check both agree with the row now at that index; a Category view must name a category this build
    /// has; a Confirm must carry the change it confirms; and the Orchestration view has no taps at all.
    /// </summary>
    public static (SettingsMenuViews View, SettingCategories? Category, int? Index, ISettingDefinition? Definition_OrNull, int Page, SettingsMenuEdits? Edit, string? Word)? Resolve_OrNull(
        (SettingsMenuViews View, string CategoryWord, int? Index, string Check, int Page, SettingsMenuEdits? Edit, string? Word) parsed)
    {
        switch (parsed.View)
        {
            case SettingsMenuViews.Categories:
                return (parsed.View, null, null, null, parsed.Page, parsed.Edit, parsed.Word);

            case SettingsMenuViews.Category:
            {
                var category = Find_Category_OrNull(parsed.CategoryWord);

                if (category == null)
                    return null;

                return (parsed.View, category, null, null, parsed.Page, parsed.Edit, parsed.Word);
            }

            case SettingsMenuViews.Setting:
            case SettingsMenuViews.Values:
            case SettingsMenuViews.Confirm:
            {
                if (parsed.Index is not { } index || index >= Catalog.ALL.Count)
                    return null;

                var definition = Catalog.ALL[index];

                if (definition.Category.ToString() != parsed.CategoryWord || Compute_Check(definition.Path) != parsed.Check)
                    return null;

                if (parsed.View == SettingsMenuViews.Confirm && parsed.Edit == null)
                    return null;

                return (parsed.View, definition.Category, index, definition, parsed.Page, parsed.Edit, parsed.Word);
            }

            case SettingsMenuViews.Orchestration:
                return null;

            default:
                throw new InvalidOperationException($"Unhandled SettingsMenuViews: {parsed.View}");
        }
    }

    /// <summary>
    /// WHETHER A TAP WRITES — the one rule Task 5 asks before it touches config.json. A Confirm view NEVER
    /// writes: it is the first tap of a Kernel edit, and D2's "two-tap confirm" is empty if the first tap
    /// already landed (ruling P6). The reply step writes nothing either — it asks for the value.
    /// </summary>
    public static bool Writes_OnTap(SettingsMenuViews view, SettingsMenuEdits? edit)
    {
        if (view == SettingsMenuViews.Confirm || edit == null)
            return false;

        return edit.Value switch
        {
            SettingsMenuEdits.Set or SettingsMenuEdits.Add or SettingsMenuEdits.Remove or SettingsMenuEdits.Reset or SettingsMenuEdits.ApplyHeldReply => true,
            SettingsMenuEdits.Reply => false,
            _ => throw new InvalidOperationException($"Unhandled SettingsMenuEdits: {edit}"),
        };
    }

    /// <summary>
    /// FOUR HEX DIGITS OF 32-BIT FNV-1a OVER THE PATH'S UTF-8 BYTES — the low sixteen bits, lowercase. Stable
    /// across processes, builds and machines, which is the whole point (ruling P7). Sixteen bits cannot make a
    /// collision impossible, only rare: two paths landing on the same index AND the same check across one
    /// upgrade — the cost of the five bytes the whole payload can spare.
    /// </summary>
    public static string Compute_Check(string path)
    {
        var hash = FNV_OFFSET_BASIS;

        foreach (var octet in Encoding.UTF8.GetBytes(path))
        {
            hash ^= octet;
            hash *= FNV_PRIME;
        }

        return (hash & 0xFFFF).ToString("x4", CultureInfo.InvariantCulture);
    }

    static (string CategoryWord, string IndexText, string Check) Describe_Target(SettingsMenuViews view, SettingCategories? category, int? settingIndex)
    {
        switch (view)
        {
            case SettingsMenuViews.Categories:
                if (category != null || settingIndex != null)
                    throw new ArgumentException("the Categories view names no category and no setting");

                return (string.Empty, string.Empty, string.Empty);

            case SettingsMenuViews.Category:
                if (category == null || settingIndex != null)
                    throw new ArgumentException("a Category view names its category and no setting");

                return (category.Value.ToString(), string.Empty, string.Empty);

            case SettingsMenuViews.Setting:
            case SettingsMenuViews.Values:
            case SettingsMenuViews.Confirm:
            {
                if (settingIndex is not { } index || index < 0 || index >= Catalog.ALL.Count)
                    throw new ArgumentOutOfRangeException(nameof(settingIndex), settingIndex, $"a {view} view names a row of SettingsCatalog.ALL (0 – {Catalog.ALL.Count - 1})");

                var definition = Catalog.ALL[index];

                if (category != null && category != definition.Category)
                    throw new ArgumentException($"'{definition.Path}' is in {definition.Category}, not {category}");

                return (definition.Category.ToString(), index.ToString(CultureInfo.InvariantCulture), Compute_Check(definition.Path));
            }

            case SettingsMenuViews.Orchestration:
                throw new ArgumentException("the Orchestration view is read-only and has no buttons — nothing may land on it by a tap");

            default:
                throw new InvalidOperationException($"Unhandled SettingsMenuViews: {view}");
        }
    }

    static string Describe_Edit(SettingsMenuEdits edit, string? word)
    {
        var carriesWord = edit is SettingsMenuEdits.Set or SettingsMenuEdits.Add or SettingsMenuEdits.Remove;

        if (carriesWord && string.IsNullOrEmpty(word))
            throw new ArgumentException($"a {edit} carries the word it writes", nameof(word));

        if (!carriesWord && word != null)
            throw new ArgumentException($"a {edit} carries no word, got '{word}'", nameof(word));

        return Describe_EditCode(edit) + (word ?? string.Empty);
    }

    static (SettingsMenuEdits Edit, string? Word)? Parse_Edit_OrNull(string field)
    {
        if (field.Length == 0)
            return null;

        var edit = Parse_EditCode_OrNull(field[0]);

        if (edit == null)
            return null;

        var word = field[1..];
        var carriesWord = edit is SettingsMenuEdits.Set or SettingsMenuEdits.Add or SettingsMenuEdits.Remove;

        if (carriesWord != (word.Length > 0))
            return null;

        return (edit.Value, carriesWord ? word : null);
    }

    /// <summary>One letter per view — a payload is 64 bytes and a view is on every one of them.</summary>
    static string Describe_ViewCode(SettingsMenuViews view)
    {
        return view switch
        {
            SettingsMenuViews.Categories => "h",
            SettingsMenuViews.Category => "c",
            SettingsMenuViews.Setting => "s",
            SettingsMenuViews.Values => "v",
            SettingsMenuViews.Confirm => "y",
            _ => throw new InvalidOperationException($"No payload code for SettingsMenuViews.{view}"),
        };
    }

    static SettingsMenuViews? Parse_ViewCode_OrNull(string code)
    {
        return code switch
        {
            "h" => SettingsMenuViews.Categories,
            "c" => SettingsMenuViews.Category,
            "s" => SettingsMenuViews.Setting,
            "v" => SettingsMenuViews.Values,
            "y" => SettingsMenuViews.Confirm,
            _ => null,
        };
    }

    static char Describe_EditCode(SettingsMenuEdits edit)
    {
        return edit switch
        {
            SettingsMenuEdits.Set => '=',
            SettingsMenuEdits.Add => '+',
            SettingsMenuEdits.Remove => '-',
            SettingsMenuEdits.Reset => '~',
            SettingsMenuEdits.Reply => '?',
            SettingsMenuEdits.ApplyHeldReply => '!',
            _ => throw new InvalidOperationException($"Unhandled SettingsMenuEdits: {edit}"),
        };
    }

    static SettingsMenuEdits? Parse_EditCode_OrNull(char code)
    {
        return code switch
        {
            '=' => SettingsMenuEdits.Set,
            '+' => SettingsMenuEdits.Add,
            '-' => SettingsMenuEdits.Remove,
            '~' => SettingsMenuEdits.Reset,
            '?' => SettingsMenuEdits.Reply,
            '!' => SettingsMenuEdits.ApplyHeldReply,
            _ => null,
        };
    }

    /// <summary>
    /// A category by its enum NAME, ordinal — never by number: <c>Enum.TryParse</c> would accept "3" and hand
    /// back whichever category happens to sit third in this build.
    /// </summary>
    static SettingCategories? Find_Category_OrNull(string word)
    {
        foreach (var category in Enum.GetValues<SettingCategories>())
        {
            if (string.Equals(category.ToString(), word, StringComparison.Ordinal))
                return category;
        }

        return null;
    }

    /// <summary>Digits only, invariant — no sign, no spaces — so a page or an index has one spelling.</summary>
    static bool Try_ParseCount(string text, out int value)
    {
        value = 0;

        return text.Length > 0 && text.All(char.IsAsciiDigit) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}

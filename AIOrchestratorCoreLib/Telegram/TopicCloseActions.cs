using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHAT CLOSING AN ORCHESTRATION DOES TO ITS TELEGRAM TOPIC — the catalogue's <c>topic.onClose</c>.
/// The fork deletes it (a tidy phone); master closed it, keeping the thread as an audit trail.
/// </summary>
public enum TopicCloseActions
{
    /// <summary>The topic is deleted with its history.</summary>
    Delete,

    /// <summary>The topic is closed and kept.</summary>
    Close,
}

/// <summary>Reads a <c>topic.onClose</c> word without a switch at each call site.</summary>
public static class TopicClose_Actions
{
    /// <summary>The catalogue path this class parses.</summary>
    const string PATH = "topic.onClose";

    /// <summary>The catalogue's words for <c>topic.onClose</c> — the drift between the two is pinned by <c>PhoneSettingsJsonTests</c>.</summary>
    public const string DELETE_TEXT = "delete";
    public const string CLOSE_TEXT = "close";

    /// <summary>
    /// Parses the catalogue path <c>topic.onClose</c>. Ordinal-case-insensitive; an absent, blank or
    /// UNRECOGNISED word reads as the catalogue's shipped default.
    ///
    /// <para>
    /// <c>_OrDefault</c> AND NOT <c>_OrClose</c>, unlike its four siblings, because this default is
    /// RULED TO MOVE: the owner answered D2 on 2026-09-14 with <c>delete</c>, and plan 03's Task 10 is
    /// the task that moves the catalogue row. A member named here would be a second copy of that fact,
    /// wrong for as long as the two were out of step (CLAUDE.md decision 12) — so the fallback is READ
    /// from the catalogue at call time, and moving the row is the whole of the change.
    /// </para>
    /// </summary>
    public static TopicCloseActions Parse_OrDefault(string? word)
    {
        return Match_OrNull(word)
            ?? Match_OrNull(Catalog.Find_OrNull(PATH)?.Default_OrNull?.GetValue<string>())
            ?? throw new Exception($"The catalogue's shipped default for '{PATH}' is not a word {nameof(TopicClose_Actions)} knows — the enum and the catalogue row have drifted");
    }

    static TopicCloseActions? Match_OrNull(string? word)
    {
        var trimmed = word?.Trim();

        if (string.Equals(trimmed, DELETE_TEXT, StringComparison.OrdinalIgnoreCase))
            return TopicCloseActions.Delete;

        if (string.Equals(trimmed, CLOSE_TEXT, StringComparison.OrdinalIgnoreCase))
            return TopicCloseActions.Close;

        return null;
    }
}

using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Configuration.TelegramProseSettings;

/// <summary>
/// The <c>telegram</c> block, read:
///
/// <code>
/// "telegram": { "foldLongEntriesAbove": 900, "attachEntriesAbove": 3 }
/// </code>
///
/// <para>
/// READ AND NEVER WRITTEN, for the reason <see cref="DefaultsSettings.DefaultsSettings_Json"/> and the
/// guardrail keys already give: no window has a field for either setting, so the only thing a save
/// could do is materialise THIS BUILD's defaults into the owner's file as if they had chosen them,
/// freezing numbers that are meant to move when the app is updated.
/// </para>
/// <para>
/// TOLERANT, because a config the app refuses to load is a bridge that does not start. An absent
/// block, an absent key, a key holding a string, an object or an array all read as the shipped
/// default for that ONE setting. A negative number is kept rather than corrected: it means the same
/// as 0 to both readers — off — and silently rewriting an owner's value is worse than honouring it.
/// </para>
/// </summary>
public static class TelegramProseSettings_Json
{
    public const string TELEGRAM_KEY = "telegram";
    public const string FOLD_LONG_ENTRIES_ABOVE_KEY = "foldLongEntriesAbove";
    public const string ATTACH_ENTRIES_ABOVE_KEY = "attachEntriesAbove";

    public static ITelegramProseSettings Parse(JsonObject? configRoot)
    {
        var block = configRoot?[TELEGRAM_KEY] as JsonObject;

        return TelegramProseSettings_Factory.Create(
            Read_Int_OrNull(block, FOLD_LONG_ENTRIES_ABOVE_KEY),
            Read_Int_OrNull(block, ATTACH_ENTRIES_ABOVE_KEY));
    }

    /// <summary>
    /// Null for every way the key can fail to be a whole number. <c>GetValue&lt;int&gt;</c> THROWS on
    /// a JSON string, and a config file that throws while being read is the bridge not starting — the
    /// defect the loader's own numeric readers were fixed for.
    /// </summary>
    static int? Read_Int_OrNull(JsonObject? block, string key)
    {
        if (block?[key] is not JsonValue value)
            return null;

        return value.TryGetValue<int>(out var number) ? number : null;
    }
}

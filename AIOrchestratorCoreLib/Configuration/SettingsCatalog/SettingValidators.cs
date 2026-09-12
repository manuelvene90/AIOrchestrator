using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// The named validators a String or StringList definition may point at. A NAME rather than a
/// delegate in the registry so a definition stays plain data that a renderer can serialise — the
/// web page's GET /settings (plan 04) hands the browser the catalogue itself.
/// </summary>
public static class SettingValidators
{
    public const string NONE = "none";

    /// <summary>Letters, digits, '-', '_' and '.', through <see cref="Spawning.SpawnCommand_Builder.First_InvalidModelCharacter_OrNull"/>.</summary>
    public const string MODEL_WORD = "modelWord";

    /// <summary>`host:port`, or the literal `off`. Used by `web.listen`.</summary>
    public const string LISTEN_ADDRESS = "listenAddress";

    /// <summary>Every element must be one of `PulseField_Names.ALL`, with no repeats.</summary>
    public const string PULSE_FIELDS = "pulseFields";

    /// <summary>Every element must be a command in `Telegram.BotCommandMenu.ALL`, with no repeats.</summary>
    public const string BOT_COMMANDS = "botCommands";

    /// <summary>The message, or null when the value is acceptable.</summary>
    public static string? Validate_OrNull(string validatorName, JsonNode? value)
    {
        return validatorName switch
        {
            NONE => null,
            MODEL_WORD => Validate_ModelWord_OrNull(value),
            _ => null,
        };
    }

    static string? Validate_ModelWord_OrNull(JsonNode? value)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var word))
            return "Expected a string";

        var invalid = Spawning.SpawnCommand_Builder.First_InvalidModelCharacter_OrNull(word);

        return invalid == null
            ? null
            : $"'{word}' contains invalid character '{invalid}' — a model name may hold letters, digits, '-', '_' or '.'";
    }
}

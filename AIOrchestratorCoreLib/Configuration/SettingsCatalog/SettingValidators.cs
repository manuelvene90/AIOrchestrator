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

    /// <summary>
    /// WILL check for `host:port`, or the literal `off`, for `web.listen`. NOT YET IMPLEMENTED — see
    /// the fallthrough in <see cref="Validate_OrNull"/>: a definition may point at this name today
    /// and it accepts any value until a later task wires the check in.
    /// </summary>
    public const string LISTEN_ADDRESS = "listenAddress";

    /// <summary>
    /// WILL check that every element is one of `PulseField_Names.ALL`, with no repeats. NOT YET
    /// IMPLEMENTED — see the fallthrough in <see cref="Validate_OrNull"/>: a definition may point at
    /// this name today and it accepts any value until a later task wires the check in.
    /// </summary>
    public const string PULSE_FIELDS = "pulseFields";

    /// <summary>
    /// WILL check the FIRST space-delimited token of each element against
    /// <c>Telegram.BotCommandMenu.ALL</c>, with no repeats of that token. A trailing target after the
    /// space is legal and is not checked against anything — <c>pulse.buttons</c>' shipped default
    /// carries <c>"tail sup"</c>, a verb WITH ITS TARGET (a button tap carries no text, so the target
    /// rides inside the verb), and <c>BotCommandMenu.ALL</c> holds only the bare verb <c>"tail"</c>.
    /// Checking the whole element for exact membership would refuse the catalogue's own default. NOT
    /// YET IMPLEMENTED — see the fallthrough in <see cref="Validate_OrNull"/>: a definition may point
    /// at this name today and it accepts any value until a later task wires the check in.
    /// </summary>
    public const string BOT_COMMANDS = "botCommands";

    /// <summary>The message, or null when the value is acceptable.</summary>
    public static string? Validate_OrNull(string validatorName, JsonNode? value)
    {
        return validatorName switch
        {
            NONE => null,
            MODEL_WORD => Validate_ModelWord_OrNull(value),

            // LISTEN_ADDRESS, PULSE_FIELDS and BOT_COMMANDS are registered names only — a definition
            // may point at one before its check exists (a definition naming a not-yet-implemented
            // validator is legitimate). Until a later task adds a case above, any value is accepted;
            // this is a real gap, not an oversight, and it must stay visible here rather than only in
            // a report nobody reading this switch will see.
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

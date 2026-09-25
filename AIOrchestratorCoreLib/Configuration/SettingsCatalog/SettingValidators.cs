using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Web;

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
    /// `host:port` (an IPv6 literal takes brackets), or the literal `off`, for `web.listen`. The parse
    /// and range rules live in <see cref="Web.ListenAddress"/>, which plan 04's listener (Task 7) reads
    /// directly through <see cref="Web.ListenAddress.Parse_OrNull"/> / <see cref="Web.ListenAddress.Is_Off"/>;
    /// this name only routes to the message-crafting case below.
    /// </summary>
    public const string LISTEN_ADDRESS = "listenAddress";

    /// <summary>
    /// Every element is one of <see cref="PulseField_Names.ALL"/>, with no repeats. The EMPTY list is
    /// accepted: a pulse with no fields is the owner's to choose, not a typo.
    /// </summary>
    public const string PULSE_FIELDS = "pulseFields";

    /// <summary>
    /// The FIRST space-delimited token of each element is a verb of <c>Telegram.BotCommandMenu.ALL</c>,
    /// and no verb appears twice. A trailing target after the space is legal and is not checked against
    /// anything — <c>pulse.buttons</c>' shipped default carries <c>"tail sup"</c>, a verb WITH ITS TARGET
    /// (a button tap carries no text, so the target rides inside the verb), and <c>BotCommandMenu.ALL</c>
    /// holds only the bare verb <c>"tail"</c>. Checking the whole element for exact membership would
    /// refuse the catalogue's own default. The repeat is of the VERB for the same reason: "tail sup" and
    /// "tail 1" are one verb twice on a bar. The EMPTY list is accepted — classic's <c>general.buttons</c>.
    /// </summary>
    public const string BOT_COMMANDS = "botCommands";

    /// <summary>
    /// Every element is one of <see cref="QuestionAppButton_Names.ALL"/>, with no repeats — ordinal, because the
    /// engine matches the word ordinally when it builds a question's keyboard. The EMPTY list is accepted: no app
    /// button under a question is the owner's to choose (entry [123]: "all 2 or just one of the two").
    /// </summary>
    public const string QUESTION_APP_BUTTONS = "questionAppButtons";

    /// <summary>The message, or null when the value is acceptable.</summary>
    public static string? Validate_OrNull(string validatorName, JsonNode? value)
    {
        return validatorName switch
        {
            NONE => null,
            MODEL_WORD => Validate_ModelWord_OrNull(value),
            PULSE_FIELDS => Validate_PulseFields_OrNull(value),
            BOT_COMMANDS => Validate_BotCommands_OrNull(value),
            QUESTION_APP_BUTTONS => Validate_KnownWords_OrNull(value, QuestionAppButton_Names.ALL, "an app button under a question"),
            LISTEN_ADDRESS => Validate_ListenAddress_OrNull(value),

            // Defensive only: every name this switch's own constants can produce has a case above. An
            // unrecognised name here means a definition points at a validator that was never registered
            // — a bug in the catalogue itself, not in the value being checked — so accepting rather than
            // refusing keeps this switch a pure function of the value, not of whether the catalogue is
            // well-formed.
            _ => null,
        };
    }

    static string? Validate_PulseFields_OrNull(JsonNode? value)
    {
        return Validate_KnownWords_OrNull(value, PulseField_Names.ALL, "a pulse field");
    }

    /// <summary>
    /// A list drawn from a fixed vocabulary, each word at most once — <c>pulse.fields</c> and
    /// <c>questions.appButtons</c>. ONE body for both so the two refusals read alike on every renderer; the
    /// pulse-field sentences are unchanged by the extraction (<c>SettingValidatorsTests</c> pins them).
    /// </summary>
    static string? Validate_KnownWords_OrNull(JsonNode? value, IReadOnlyList<string> legalWords, string whatAWordIs)
    {
        if (!Try_ReadWords(value, out var words))
            return "Expected a list of text values";

        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var word in words)
        {
            if (!legalWords.Contains(word, StringComparer.Ordinal))
                return $"'{word}' is not {whatAWordIs} — must be one of: {string.Join(", ", legalWords)}";

            if (!seen.Add(word))
                return $"'{word}' appears more than once — {whatAWordIs} may be listed only once";
        }

        return null;
    }

    /// <summary>
    /// ORDINAL, AND CASE-SENSITIVE, because the tap parser is: <c>TopicCommandButtons.Parse_OrNull</c>
    /// matches a verb ordinally, so a "Tail" this accepted would render as a button that parses to
    /// nothing when tapped — the silent dead button that parser exists to prevent.
    /// </summary>
    static string? Validate_BotCommands_OrNull(JsonNode? value)
    {
        if (!Try_ReadWords(value, out var elements))
            return "Expected a list of text values";

        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var element in elements)
        {
            var verb = element.Split(' ')[0];

            if (!Telegram.BotCommandMenu.ALL.Any(command => string.Equals(command.Command, verb, StringComparison.Ordinal)))
                return $"'{verb}' (in '{element}') is not a bot command — the verb before the first space must be one of the commands in the '/' menu";

            if (!seen.Add(verb))
                return $"'{verb}' appears more than once — a verb may be on a bar only once, whatever follows it";
        }

        return null;
    }

    /// <summary>
    /// Shape-checks first — same defensive re-check the two list validators above make, because this
    /// switch is public and a caller may reach it directly, as the tests do — then delegates the actual
    /// parsing to <see cref="Web.ListenAddress.Problem_OrNull"/> so there is one split routine, not one
    /// per validator and one per listener.
    /// </summary>
    static string? Validate_ListenAddress_OrNull(JsonNode? value)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
            return "Expected a string";

        return ListenAddress.Problem_OrNull(text);
    }

    /// <summary>False for anything that is not an array of strings — a definition checks the shape first, but this switch is public.</summary>
    static bool Try_ReadWords(JsonNode? value, out IReadOnlyList<string> words)
    {
        List<string> read = [];
        words = read;

        if (value is not JsonArray array)
            return false;

        foreach (var element in array)
        {
            if (element is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var word))
                return false;

            read.Add(word);
        }

        return true;
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

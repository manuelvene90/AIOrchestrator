namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// What follows "/model" or "/effort" on the owner's phone.
///
/// The bridge's command lexer hands back the WHOLE remainder of the message, lowercased — "model
/// fable 5.1", not "model" — so a command that takes an argument has to be recognised by its
/// leading word and then split. This does the split: an optional ROLE word off the front, the rest
/// as the argument. Resolving the argument (to a model alias, to an effort level) is the
/// catalogue's job, and deciding between "apply it" and "show the buttons" is the caller's.
/// </summary>
public static class ModelEffortCommand_Parser
{
    /// <summary>
    /// The words an owner might type for a role, folded onto the two override slots. "solo" folds
    /// onto the implementer slot because that is the slot the launcher spawns a solo from.
    /// </summary>
    static readonly IReadOnlyDictionary<string, string> ROLE_WORDS = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sup"] = ModelEffortButton_Data.SUPERVISOR_ROLE,
        ["supervisor"] = ModelEffortButton_Data.SUPERVISOR_ROLE,
        ["imp"] = ModelEffortButton_Data.IMPLEMENTER_ROLE,
        ["implementer"] = ModelEffortButton_Data.IMPLEMENTER_ROLE,
        ["implementers"] = ModelEffortButton_Data.IMPLEMENTER_ROLE,
        ["solo"] = ModelEffortButton_Data.IMPLEMENTER_ROLE,
    };

    /// <summary>
    /// True for the verb alone or the verb followed by a space — "models" and "modelling" are not
    /// "/model", and a StartsWith check on its own would take them.
    /// </summary>
    public static bool Is_Command(string commandText, string verb)
    {
        return commandText == verb || commandText.StartsWith(verb + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// (role, argument): the role is `sup`/`imp` when a role word led the argument, else null; the
    /// argument is the remaining words joined by single spaces, empty for the bare command.
    /// </summary>
    public static (string? Role, string Argument) Parse_Argument(string commandText, string verb)
    {
        if (!Is_Command(commandText, verb))
            throw new ArgumentException($"'{commandText}' is not the /{verb} command", nameof(commandText));

        var words = commandText[verb.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
            return (null, "");

        if (ROLE_WORDS.TryGetValue(words[0], out var role))
            return (role, string.Join(' ', words.Skip(1)));

        return (null, string.Join(' ', words));
    }
}

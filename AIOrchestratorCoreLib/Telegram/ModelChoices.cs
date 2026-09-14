namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The models the owner is offered from the phone, and how a TYPED name resolves to one.
///
/// The owner asked for /model on 2026-09-09 and typed "fabel 5.1" in the very message that asked
/// for it — so the point of this catalogue is not the list, it is the refusal: a name resolves when
/// it is recognisably one of these (or an explicit alias variant / full id Claude Code documents),
/// and resolves to NOTHING otherwise. The caller answers "nothing" with the buttons, never with a
/// guess, because a guessed model respawns a session on the wrong brain.
///
/// Aliases are what `claude --model` accepts for "the latest of that family"; the labels are what
/// a button shows. The catalogue deliberately does not know model IDS — `claude-…` passes through
/// as typed, so a new release needs no change here to be selectable.
/// </summary>
public static class ModelChoices
{
    /// <summary>What a full model id starts with; anything so prefixed is handed to the CLI as typed.</summary>
    const string FULL_ID_PREFIX = "claude-";

    /// <summary>`opus[1m]`-style variants: an alias plus a bracketed context-window tag, no spaces.</summary>
    const char VARIANT_OPEN = '[';
    const char VARIANT_CLOSE = ']';

    /// <summary>THE source of truth, in the order the buttons show them: most capable first.</summary>
    public static readonly IReadOnlyList<(string Alias, string Label)> ALL =
    [
        ("fable", "Fable 5.1"),
        ("opus", "Opus 5"),
        ("sonnet", "Sonnet 5"),
        ("haiku", "Haiku 4.5"),
    ];

    /// <summary>
    /// The `--model` argument for what the owner typed, or null when it is not recognisably one of
    /// ours. "Fable 5.1" → "fable"; "opus[1m]" and "claude-opus-5" → as typed, lowercased.
    /// </summary>
    public static string? Resolve_OrNull(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        var text = typed.Trim().ToLowerInvariant();

        if (text.StartsWith(FULL_ID_PREFIX, StringComparison.Ordinal))
            return text;

        foreach (var (alias, _) in ALL)
            if (text == alias || Is_BracketedVariant(text, alias))
                return text;

        foreach (var (alias, _) in ALL)
            if (text.Contains(alias, StringComparison.Ordinal))
                return alias;

        return null;
    }

    /// <summary>The label for a catalogue alias; an explicit variant or full id describes itself.</summary>
    public static string Describe(string resolvedModel)
    {
        foreach (var (alias, label) in ALL)
            if (resolvedModel == alias)
                return label;

        return resolvedModel;
    }

    static bool Is_BracketedVariant(string text, string alias)
    {
        if (!text.StartsWith(alias + VARIANT_OPEN, StringComparison.Ordinal))
            return false;

        return text.EndsWith(VARIANT_CLOSE) && !text.Contains(' ');
    }
}

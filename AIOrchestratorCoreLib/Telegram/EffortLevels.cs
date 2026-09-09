namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The levels `claude --effort` accepts (Claude Code 2.1.266: low, medium, high, xhigh, max), in
/// ascending order — the order the buttons show them.
///
/// A typed level resolves only on an exact match. "extra high" is not a level and never becomes
/// one here: the buttons exist precisely so that nobody has to remember that it is spelled xhigh.
/// </summary>
public static class EffortLevels
{
    /// <summary>THE source of truth, ascending.</summary>
    public static readonly IReadOnlyList<string> ALL = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>The level for what the owner typed, or null when it is not one — never a guess.</summary>
    public static string? Resolve_OrNull(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        var text = typed.Trim().ToLowerInvariant();

        return ALL.Contains(text) ? text : null;
    }
}

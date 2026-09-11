namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// THE TWO WORDS FOR THE TWO SHAPES, in one place. <c>full</c> is a crew — supervisor, implementer,
/// reviewer; <c>basic</c> is one solo session and no supervisor.
///
/// <para>
/// They were only ever spelled inside the request reader, which was fine while the request was the
/// only thing that could name a shape. It is not any more: <c>config.json</c> now carries the shape a
/// request without a <c>mode</c> gets, and a second spelling of "full" living in the configuration
/// layer is how the two come to disagree — one accepting a word the other rejects, with the owner
/// watching a typo silently buy the expensive shape.
/// </para>
/// <para>
/// PARSING IS ALSO HERE, and it REFUSES rather than defaults. A value nobody recognises must never
/// decide the shape quietly, in either direction: the request protocol has said so since 2026-08-13
/// and the same has to be true of the config key, where a typo would otherwise be permanent.
/// </para>
/// </summary>
public static class OrchestrationModes
{
    /// <summary>A crew: supervisor + imp-1 + rev-1. Has to be asked for by name.</summary>
    public const string FULL = "full";

    /// <summary>One solo session, no supervisor. The owner's default since 2026-08-13, as a cost-saving measure.</summary>
    public const string BASIC = "basic";

    /// <summary>Both words, for a message that has to list what it would have accepted.</summary>
    public static string Describe_Accepted() => $"'{FULL}' or '{BASIC}'";

    /// <summary>
    /// The word as a shape, case- and space-insensitively: true for <c>basic</c>, false for
    /// <c>full</c>, null for anything else INCLUDING null itself. The caller decides what an
    /// unrecognised word costs — the request reader rejects the file, the config loader falls back to
    /// the built-in default and says so — but neither of them guesses which shape was meant.
    /// </summary>
    public static bool? Is_Basic_OrNull(string? mode)
    {
        var word = mode?.Trim().ToLowerInvariant();

        return word switch
        {
            BASIC => true,
            FULL => false,
            _ => null,
        };
    }
}

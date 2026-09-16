namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// WHERE THE APP'S OWN BOOKKEEPING ABOUT A SESSION IS WRITTEN. <see cref="Channel"/>: into the
/// session's channel, where it has always gone and where it is re-read at every boot.
/// <see cref="Log"/>: into <see cref="StatusLog_Store"/>, shown to the session through its state pack
/// and its riding notes.
///
/// <para>
/// IT NEVER GOVERNS AN OWNER-FACING ENTRY. <c>AppEntryAudiences.Owner</c> is what reaches Telegram,
/// and the router refuses to move one whatever this says — the channel is the only route to the
/// mirror. This key decides where AGENT-facing bookkeeping goes and nothing else.
/// </para>
/// <para>
/// Orthogonal to <see cref="Running.SessionRunners"/> and to <see cref="Running.WakeModes"/>, but not
/// independent of them: <see cref="BookkeepingSink_Policy"/> refuses <see cref="Log"/> for a session
/// that is handed no state pack, because a note nothing reads is worse than a noisy channel.
/// </para>
/// </summary>
public enum BookkeepingSinks
{
    Channel,
    Log,
}

public static class BookkeepingSink_Names
{
    public const string CHANNEL = "channel";
    public const string LOG = "log";

    public static string Get_Word(BookkeepingSinks sink)
    {
        return sink switch
        {
            BookkeepingSinks.Channel => CHANNEL,
            BookkeepingSinks.Log => LOG,
            _ => throw new Exception($"Unhandled BookkeepingSinks: {sink}"),
        };
    }

    /// <summary>
    /// Null for anything this build does not know, INCLUDING a typo. The caller falls back to
    /// <see cref="BookkeepingSinks.Channel"/>: a machine whose config names a sink this binary has
    /// never heard of keeps the behaviour it already had.
    /// </summary>
    public static BookkeepingSinks? Parse_OrNull(string? word)
    {
        return word?.Trim().ToLowerInvariant() switch
        {
            CHANNEL => BookkeepingSinks.Channel,
            LOG => BookkeepingSinks.Log,
            _ => null,
        };
    }
}

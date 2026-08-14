namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// The audience tag an app entry carries in its subject, and the only implementation of writing and
/// reading it.
///
/// WHY IT LIVES IN THE SUBJECT. The mirror never sees the call that wrote an entry — it sees an
/// <c>IChannelEntry</c> parsed back out of the FILE, on a later poll, in a different part of the app.
/// So the audience has to survive a round trip through the channel file, which rules out passing it as
/// an argument and nothing else: by the time routing happens that information is gone.
///
/// ONE implementation, referenced by the writer and by the mirror, because two copies of a rule that
/// must agree exactly is the defect this replaces. The tag also lives in <c>Channels</c> rather than in
/// <c>Mirroring</c> so the appender can reach it without a layering inversion.
///
/// The tag is never stripped for display, and does not need to be: a tagged entry is by definition
/// never mirrored, so it cannot reach the formatter that would render it. Agents do see it in their own
/// channels, which is a feature — it says plainly that the line is addressed to them.
/// </summary>
public static class AppEntryAudience_Tag
{
    /// <summary>
    /// Names the AUDIENCE, deliberately not the transport. A tag reading "no-telegram" would be a lie
    /// the day a second transport exists, and channels are append-only, so every historical entry
    /// carrying it could never be corrected.
    /// </summary>
    public const string AGENT_TAG = "[agent]";

    /// <summary>
    /// The subject as it should be written. Owner-facing subjects are returned untouched, so an
    /// owner-facing entry looks exactly as it always has and nothing downstream that reads a subject
    /// has to learn about this at all.
    /// </summary>
    public static string Apply(string subject, AppEntryAudiences audience)
    {
        return audience == AppEntryAudiences.Agent
            ? $"{AGENT_TAG} {subject}"
            : subject;
    }

    /// <summary>
    /// Whether a subject read back from a file is agent-facing. A PREFIX test rather than a Contains
    /// one: the tag means something only where the writer put it, and an entry that merely discusses
    /// the tag — a supervisor quoting this rule into a brief — must not become invisible by mentioning
    /// it. That is the same distinction the marker vocabulary draws between a declaration and a
    /// discussion of one.
    /// </summary>
    public static bool Is_AgentTagged(string subject)
    {
        return subject.TrimStart().StartsWith(AGENT_TAG, StringComparison.OrdinalIgnoreCase);
    }
}

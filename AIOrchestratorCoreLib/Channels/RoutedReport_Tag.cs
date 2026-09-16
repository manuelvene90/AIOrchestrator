namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// THE MARK THAT MAKES ONE APP ENTRY A BRIEF. The app relays an implementer's fix report into a
/// reviewer's channel so that a fix round does not cost the supervisor a wake-up as a postman
/// (2026-09-15 one-wake-model spec, step 4). That entry is written by the app, like every other app
/// entry, and is the only one in the system that a session is expected to ANSWER — so it has to be
/// distinguishable from the app's bookkeeping by something that survives the round trip through the
/// channel file, which is the subject and nothing else (the reasoning is
/// <see cref="AppEntryAudience_Tag"/>'s, one word over).
///
/// <para>
/// IT DOES NOT REPLACE THE AUDIENCE TAG, IT SITS INSIDE IT. A routed report is agent-facing — the
/// owner cannot act on one session's brief to another (CLAUDE.md decision 15) — so the subject
/// carries both, audience outermost: <c>[agent] [routed] re-review — imp-1's fix</c>. That is why
/// <see cref="Is_Routed"/> steps over a leading audience tag before it looks.
/// </para>
/// <para>
/// A PREFIX TEST, NEVER A CONTAINS. The tag means something only where the app put it. A supervisor
/// brief explaining this mechanism, or a reviewer quoting its own brief back, must not become a
/// relay by mentioning the word — the same distinction the marker vocabulary draws between a
/// declaration and a discussion of one.
/// </para>
/// <para>
/// AND THE AUTHOR SCREEN IS STILL FIRST. <c>PrintTurn_Trigger.Is_Inbound</c> asks for
/// <see cref="ChannelAuthors.App"/> before it asks this, so the tag confers nothing on an entry a
/// session wrote. Members never write into each other's channels, and nothing here is a way to.
/// </para>
/// </summary>
public static class RoutedReport_Tag
{
    public const string ROUTED_TAG = "[routed]";

    /// <summary>The subject as the relay writes it. Pass the result to <c>ChannelAppender.Append_AppEntry</c>, which applies the audience tag around it.</summary>
    public static string Apply(string subject)
    {
        return $"{ROUTED_TAG} {subject}";
    }

    /// <summary>
    /// Whether a subject read back from a file is a routed report. The audience tag is stepped over
    /// first because the writer applies it outermost; nothing else is tolerated in front.
    ///
    /// <para>
    /// The step-over is a length skip rather than a search for the tag, because
    /// <see cref="AppEntryAudience_Tag.Is_AgentTagged"/> has just established that the trimmed
    /// subject BEGINS with it — a search would be a second way to find the same character, and the
    /// day the two disagreed it would step over a tag further down a line that never carried one at
    /// the front.
    /// </para>
    /// </summary>
    public static bool Is_Routed(string subject)
    {
        var trimmed = subject.TrimStart();

        if (AppEntryAudience_Tag.Is_AgentTagged(trimmed))
            trimmed = trimmed[AppEntryAudience_Tag.AGENT_TAG.Length..].TrimStart();

        return trimmed.StartsWith(ROUTED_TAG, StringComparison.OrdinalIgnoreCase);
    }
}

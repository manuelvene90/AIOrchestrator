using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// `/endeavour` — WHAT THE SIBLINGS TOLD EACH OTHER, ON DEMAND (spec 2026-09-23 §2.2, §2.4; owner decision
/// O3). Sibling-to-sibling traffic is never texted: the outboxes are not tailed, so nothing of it rings the
/// owner's phone. The owner still gets to see it, and this command is how — asked, never pushed.
/// <para>
/// ONE SPELLING OF THE GROUP (decision 12, pre-flight ruling B): the block at the top is
/// <see cref="ProgressReport_Builder.Build_EndeavourBlock_OrNull"/>, the very text General's `/progress`
/// prints for this endeavour, so the two can never quote a different sum. What this command adds is the
/// part General has no room for: the last <see cref="EndeavourDigest_Builder.MAX_OUTBOX_SUBJECTS"/> subjects
/// of each open member's outbox — the same count each sibling's own digest carries, so the owner sees what
/// the sessions see.
/// </para>
/// <para>
/// OUTBOX HISTORY SPANS THE ARCHIVE (decision 13): the outbox is compacted like any long channel, so it is
/// read through <see cref="ChannelHistory_Counter.Read_Entries"/>, never the live file alone — a sibling that
/// wrote 91 entries would otherwise show a stretch that moved to <c>.archive.md</c> as the "last" three.
/// </para>
/// </summary>
public static class EndeavourReport_Builder
{
    public const string IN_GENERAL = "/endeavour works inside a sibling's topic — /progress here already groups them";

    public const string NOT_LINKED = "this topic is not part of an endeavour — /progress shows its ledger";

    /// <summary>The engine's own wording for a topic no orchestration owns, kept identical so the owner meets one sentence.</summary>
    public const string NO_ORCHESTRATION_IN_TOPIC = "no orchestration is bound to this topic";

    /// <summary>
    /// Every member of the endeavour is closed. Not a case the owner reaches easily — a closed topic is
    /// usually deleted — but a closed topic that was kept can still be written in.
    /// </summary>
    public const string NO_OPEN_MEMBER = "every job of this endeavour is closed — /progress shows this topic's ledger";

    public const string EMPTY_OUTBOX = "nothing written yet";

    /// <summary>
    /// The reply for <paramref name="topicSession"/> (null = General). The group block for THIS endeavour only,
    /// then one outbox section per OPEN member: a closed sibling's outbox stays on disk as its record, but the
    /// owner asked what the jobs in flight are telling each other.
    /// </summary>
    public static string Build(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions, IOrchestrationSession? topicSession)
    {
        if (topicSession == null)
            return IN_GENERAL;

        if (topicSession.EndeavourId == null)
            return NOT_LINKED;

        var block = ProgressReport_Builder.Build_EndeavourBlock_OrNull(paths, sessions, topicSession.EndeavourId);

        if (block == null)
            return NO_OPEN_MEMBER;

        var openMembers = EndeavourMembers_Resolver.Resolve_All(sessions, topicSession.EndeavourId)
            .Where(member => member.ClosedUtc == null);

        List<string> lines = [block, string.Empty];

        foreach (var member in openMembers)
            lines.AddRange(Build_OutboxSection(paths, member));

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The engine's entry point: resolves the topic from <paramref name="sessions"/> exactly as
    /// <c>Find_ByTelegramTopicId_OrNull</c> does (first session carrying the id), so the engine keeps a
    /// one-line call and "a topic nobody owns" is told apart from General — both would be a null session.
    /// </summary>
    public static string Build_ForTopic(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions, long? messageThreadId)
    {
        if (messageThreadId == null)
            return Build(paths, sessions, topicSession: null);

        var topicSession = sessions.FirstOrDefault(session => session.TelegramTopicId == messageThreadId.Value);

        return topicSession == null
            ? NO_ORCHESTRATION_IN_TOPIC
            : Build(paths, sessions, topicSession);
    }

    static IEnumerable<string> Build_OutboxSection(ISupervisionPaths paths, IOrchestrationSession member)
    {
        var name = member.DisplayName ?? member.OrchId;
        var entries = ChannelHistory_Counter.Read_Entries(paths.Get_SiblingOutboxFile(member.OrchId));

        if (entries.Count == 0)
            return [$"{name} — outbox: {EMPTY_OUTBOX}"];

        return [
            $"{name} — outbox:",
            .. entries.TakeLast(EndeavourDigest_Builder.MAX_OUTBOX_SUBJECTS).Select(entry => $"{ProgressReport_Builder.MEMBER_LINE_PREFIX}[{entry.Index}] {entry.Subject}"),
        ];
    }
}

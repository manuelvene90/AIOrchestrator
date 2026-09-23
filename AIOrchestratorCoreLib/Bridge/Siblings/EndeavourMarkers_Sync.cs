using System.Text;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// <c>&lt;orch&gt;/.siblings</c> — THE WATCHER'S LIST OF OPEN SIBLINGS (spec 2026-09-23 §3.4, §5.3). Plain text
/// for a bash loop: one line per open sibling, <c>&lt;orchId&gt;\t&lt;outbox path&gt;\t&lt;paused|live&gt;\t&lt;name&gt;</c>,
/// ending in a newline so <c>while read</c> sees the last line.
///
/// <para>
/// DERIVED, NEVER AUTHORED, and "IT MUST NOT OUTLIVE THE MODE" (<see cref="AIOrchestratorCoreLib.Status.MeetingFlag_Marker"/>): it is
/// removed when the orchestration is closed, unlinked, or has no open sibling — so a crash's leftover goes on
/// the first tick back (§7.4), by this same code rather than a startup special case.
/// </para>
/// <para>
/// THE NAME IS SANITISED HERE AS WELL (Review Focus 2). The request reader already refuses control characters
/// in a name (Task 4); this is the belt to those braces, because a tab would split one sibling into five
/// fields for the watcher and a newline into two siblings — one of them a path that does not exist.
/// </para>
/// </summary>
public static class EndeavourMarkers_Sync
{
    const string PAUSED = "paused";
    const string LIVE = "live";

    public static string Build_Text(IReadOnlyList<(string OrchId, string OutboxPath, bool Paused, string Name)> siblings)
    {
        var text = new StringBuilder();

        foreach (var sibling in siblings)
        {
            text.Append(sibling.OrchId).Append('\t')
                .Append(sibling.OutboxPath).Append('\t')
                .Append(sibling.Paused ? PAUSED : LIVE).Append('\t')
                .Append(Sanitise_Name(sibling.Name)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Makes the file match the world; returns whether it changed. <paramref name="openSiblings"/> may include
    /// <paramref name="self"/> — it is never listed. Throws on an I/O failure, which the step reports.
    /// </summary>
    public static bool Sync(ISupervisionPaths paths, IOrchestrationSession self, IReadOnlyList<IOrchestrationSession> openSiblings)
    {
        var file = paths.Get_SiblingsListFile(self.OrchId);

        List<IOrchestrationSession> listed = self.ClosedUtc != null || self.EndeavourId == null
            ? []
            : [.. openSiblings
                .Where(sibling => sibling.ClosedUtc == null && !string.Equals(sibling.OrchId, self.OrchId, StringComparison.Ordinal))
                .OrderBy(sibling => sibling.OrchId, StringComparer.Ordinal)];

        if (listed.Count == 0)
            return DerivedFile_Writer.Delete_IfPresent(file);

        return DerivedFile_Writer.Write_IfChanged(
            file,
            Build_Text([.. listed.Select(sibling => (sibling.OrchId, paths.Get_SiblingOutboxFile(sibling.OrchId), sibling.Paused, sibling.DisplayName ?? sibling.OrchId))]));
    }

    /// <summary>
    /// A NAME ON ONE LINE, as one field: the ONE sanitiser for a display name written into a derived file.
    /// <see cref="EndeavourDigest_Builder"/> uses it for the <c>##</c> header of <c>ENDEAVOUR.md</c> too — a
    /// newline there would open a forged sibling block in a file the solo trusts as app-written (Task 10 review,
    /// minor 4), and a second copy of this rule would drift from this one (decision 12).
    /// </summary>
    internal static string Sanitise_Name(string name)
    {
        return name.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}

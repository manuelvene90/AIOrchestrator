using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// GENERAL'S PROGRESS BODY — `/progress` in General and the General dashboard
/// (<see cref="GeneralDashboard_Composer"/> wraps it), moved out of <c>BridgeEngineModel.Build_ProgressReportText</c>
/// when the sibling plan (2026-09-23, Task 11) had to touch it: the engine keeps a one-line call.
/// <para>
/// An orchestration with no endeavour renders exactly as it did before siblings existed —
/// <c>&lt;name&gt;: &lt;counts&gt;</c> — and a golden test pins that byte for byte. Linked orchestrations
/// render ONCE, as one block under a summed bar (spec §2.4), at the position of the group's first member:
/// the owner steers the siblings separately, but asked "how far along is the thing" they want one number.
/// </para>
/// </summary>
public static class ProgressReport_Builder
{
    /// <summary>The group header's mark, the same one every other sibling surface leads with.</summary>
    public const string ENDEAVOUR_GLYPH = "🔗";

    /// <summary>Indent of a member's line under the group header.</summary>
    public const string MEMBER_LINE_PREFIX = "   · ";

    public const string NO_OPEN_ORCHESTRATIONS = "no open orchestrations";

    public const string NO_LEDGER = "no task ledger yet";

    /// <summary>Every OPEN orchestration of <paramref name="sessions"/>, in that order; linked ones grouped.</summary>
    public static string Build_OpenOrchestrationsText(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions)
    {
        List<string> blocks = [];
        HashSet<string> renderedEndeavours = new(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            if (session.ClosedUtc != null)
                continue;

            if (session.EndeavourId == null)
            {
                blocks.Add(Build_CountsLine(paths, session.OrchId, Name_Of(session), previous: null));
                continue;
            }

            // Rendered at its FIRST OPEN member's position; later members were already printed inside it.
            if (!renderedEndeavours.Add(session.EndeavourId))
                continue;

            // Never null here — this session is an open member — but a null would lose an orchestration from
            // the owner's view, so the plain line is the fallback rather than nothing.
            blocks.Add(Build_EndeavourBlock_OrNull(paths, sessions, session.EndeavourId)
                       ?? Build_CountsLine(paths, session.OrchId, Name_Of(session), previous: null));
        }

        if (blocks.Count == 0)
            return NO_OPEN_ORCHESTRATIONS;

        return string.Join('\n', blocks);
    }

    /// <summary>
    /// ONE ENDEAVOUR'S BLOCK — the header <c>🔗 &lt;group name&gt; — &lt;summed counts&gt;</c>, then one line per
    /// OPEN member. The single spelling of the group (decision 12, pre-flight ruling B): General's body and
    /// `/endeavour` both print this, so the two can never quote a different sum for the same endeavour.
    /// <para>
    /// A closed member is IN the sum and IN the group name — the bar never goes backwards (§3.5), and the name
    /// stays the name of the whole job — but gets no line of its own: it is finished, and General lists what
    /// is open. Null when no member is open: a finished endeavour is not on the owner's board.
    /// </para>
    /// </summary>
    public static string? Build_EndeavourBlock_OrNull(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions, string endeavourId)
    {
        var members = EndeavourMembers_Resolver.Resolve_All(sessions, endeavourId);
        var openMembers = members.Where(member => member.ClosedUtc == null).ToList();

        if (openMembers.Count == 0)
            return null;

        var sum = EndeavourProgress_Reader.Sum_OrNull(paths, members);
        var counts = sum == null ? NO_LEDGER : PlanProgress_Formatter.Describe_Counts(sum);

        List<string> lines = [$"{ENDEAVOUR_GLYPH} {Build_GroupName(members)} — {counts}"];

        lines.AddRange(openMembers.Select(member =>
            $"{MEMBER_LINE_PREFIX}{Build_CountsLine(paths, member.OrchId, Name_Of(member), previous: null)}"));

        return string.Join('\n', lines);
    }

    /// <summary>
    /// ONE orchestration's counts line, <c>&lt;name&gt;: &lt;counts&gt;</c> or <c>&lt;name&gt;: no task ledger yet</c>
    /// — the engine's <c>Build_OrchestrationCountsLine</c> delegates here, so `/status`, the periodic push and
    /// General cannot print one ledger two ways (decision 12). <paramref name="previous"/> is passed by the
    /// PERIODIC push alone: `/status` answers "where is this now", and a delta against a message the owner may
    /// not have been looking at would be a number with no visible baseline.
    /// </summary>
    public static string Build_CountsLine(ISupervisionPaths paths, string orchId, string displayName, PlanProgressSnapshot? previous)
    {
        var progress = PlanLedger_Parser.Parse_OrNull(Safe_FileReader.Read_AllText_OrEmpty(paths.Get_PlanFile(orchId)));

        if (progress == null)
            return $"{displayName}: {NO_LEDGER}";

        return $"{displayName}: {PlanProgress_Formatter.Describe_Counts(progress, previous)}";
    }

    /// <summary>
    /// <c>AI-Orch · settings + limits</c> when every member shares one platform code — the code once, then each
    /// member's words — and the full names joined with <c> + </c> otherwise: a Strategy Lab endeavour may
    /// legitimately hold an <c>IS</c> sibling (spec §4.1, "A SUB-PRODUCT KEEPS ITS OWN CODE"), and folding that
    /// under the parent's code would misname it. Every member, closed ones included, in session order.
    /// </summary>
    static string Build_GroupName(IReadOnlyList<IOrchestrationSession> members)
    {
        var names = members.Select(Name_Of).ToList();
        var split = names.Select(Split_Code_OrNull).ToList();

        var code = split[0]?.Code;
        var sharesOneCode = code != null && split.All(part => part != null && string.Equals(part.Value.Code, code, StringComparison.Ordinal));

        if (!sharesOneCode)
            return string.Join(" + ", names);

        return $"{code}{SiblingName_Rules.SEPARATOR}{string.Join(" + ", split.Select(part => part!.Value.Words))}";
    }

    static (string Code, string Words)? Split_Code_OrNull(string name)
    {
        var at = name.IndexOf(SiblingName_Rules.SEPARATOR, StringComparison.Ordinal);

        if (at <= 0)
            return null;

        var rest = name[(at + SiblingName_Rules.SEPARATOR.Length)..];

        return rest.Length == 0 ? null : (name[..at], rest);
    }

    static string Name_Of(IOrchestrationSession session)
    {
        return session.DisplayName ?? session.OrchId;
    }
}

using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Git;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Storage;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// GATHERS WHAT <see cref="EndeavourDigest_Builder"/> PRINTS, so the builder stays pure (the
/// <see cref="SiblingWorld_Reader"/> split). Runs on the 2-second mirror tick, so every read here is one the
/// tick can afford:
/// <list type="bullet">
/// <item>the tree is <see cref="WorkingPath_Resolver.Resolve"/> — the one definition the launcher and the
/// validator also read (Task 5);</item>
/// <item>the branch comes from files (<see cref="GitHead_Reader"/>), NEVER from <see cref="GitSnapshot_Reader"/>,
/// which forks git;</item>
/// <item>both channel histories go through <see cref="ChannelHistory_Counter.Read_Entries"/> — live file AND
/// archive (decision 13), and cached per file by (length, stamp) in <see cref="ChannelHistory_Cache"/>, so an
/// unchanged channel is two stats a tick, not a re-parse (pre-flight note #30).</item>
/// </list>
/// It reads only the orchestrations it is handed — the caller's open siblings — and writes nothing.
/// Throws on an unreadable PLAN.md, which the step reports as a failure rather than printing "no task ledger
/// yet" for a ledger that exists.
/// </summary>
public static class EndeavourDigest_Reader
{
    public static IReadOnlyList<SiblingDigestInput> Read_Inputs(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> openSiblings)
    {
        return [.. openSiblings.Select(sibling => Read_Input(paths, sibling))];
    }

    static SiblingDigestInput Read_Input(ISupervisionPaths paths, IOrchestrationSession sibling)
    {
        var workingPath = WorkingPath_Resolver.Resolve(sibling);
        var planFile = paths.Get_PlanFile(sibling.OrchId);

        var progress = File.Exists(planFile)
            ? PlanLedger_Parser.Parse_OrNull(Tolerant_FileReader.Read_AllText(planFile))
            : null;

        var outbox = ChannelHistory_Counter.Read_Entries(paths.Get_SiblingOutboxFile(sibling.OrchId));

        return new SiblingDigestInput(
            sibling.DisplayName ?? sibling.OrchId,
            sibling.OrchId,
            workingPath,
            GitHead_Reader.Read_Branch_OrNull(workingPath),
            sibling.Paused,
            progress,
            ChannelHistory_Counter.Read_Entries(paths.Get_OwnerChannelFile(sibling.OrchId)),
            [.. outbox.TakeLast(EndeavourDigest_Builder.MAX_OUTBOX_SUBJECTS).Select(entry => entry.Subject)]);
    }
}

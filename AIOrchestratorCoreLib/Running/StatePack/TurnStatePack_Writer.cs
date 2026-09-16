using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// THE ONE PLACE A TURN'S PACK IS WRITTEN. It lived as a private method of
/// <c>PrintTurnExecutorModel</c>, which is why the STREAM transport — the supervisor's own, and the
/// role it matters most for — never had one at all (verified 2026-09-15: zero StatePack references
/// in <c>StreamTurnExecutorModel</c>). A supervisor switched to <see cref="ResumeModes.Fresh"/>
/// would have been handed its role command and its pending entries and nothing else.
///
/// <para>
/// MOVED, NOT COPIED. Two executors each writing their own pack would be two answers to "what does a
/// fresh session know", which is precisely the split <see cref="TurnExecutor.ITurnExecutor"/> exists
/// to prevent and CLAUDE.md decision 12 in one sentence.
/// </para>
/// <para>
/// GUARDED AS A WHOLE, on top of the reader's own per-section guards: a pack that cannot be written
/// must never stop a turn — the session then finds no pack and falls back to its boot sequence,
/// which is the behaviour it had before packs existed. NOTE the cost of that mercy once a session is
/// fresh: a silent fallback is a session with no memory and no complaint. Hence the log line here
/// (decision 21: a guard that cannot do its job says so, naming what failed) and hence
/// <see cref="FreshSupervisor_Gate"/>, which declines to make a session fresh in the first place
/// while it has nowhere to have kept its conclusions.
/// </para>
/// </summary>
public static class TurnStatePack_Writer
{
    /// <summary>
    /// The pack's path, or null when none was written — nothing pending, or the write failed. A
    /// caller that has to tell the session where its pack is (the stream transport, which must send
    /// SOMETHING on stdin) uses the return value and falls back to sending the entries when it is
    /// null, so a failed pack costs context rather than the turn.
    /// </summary>
    public static string? Write_OrNull(
        ISupervisionPaths paths,
        IPrintSessionState state,
        string requestId,
        IReadOnlyList<PendingEntry> pending,
        IReadOnlyList<ITurnSource> sources,
        IOrchestrationLog? log = null)
    {
        // A BOOT TURN WRITES NONE: with nothing pending, the role command's own boot sequence is the
        // right thing and a pack describing no traffic is a page of nothing.
        if (pending.Count == 0)
            return null;

        try
        {
            var file = StatePack_Locator.Get_File(paths, state.Role, state.OrchId, state.MemberId);

            StatePack_Writer.Write(file, StatePack_Builder.Build(StatePackInputs_Reader.Read(paths, state, requestId, pending, sources)));

            return file;
        }
        catch (Exception ex)
        {
            log?.Log_Warning(state.OrchId, $"'{state.MemberId}': the state pack could not be written ({ex.Message}) — the session falls back to its boot sequence and to its entries");

            return null;
        }
    }
}

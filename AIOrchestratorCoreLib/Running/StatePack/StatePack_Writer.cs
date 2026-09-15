using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// Replaces the pack — never appends. A pack is the memory of ONE turn; yesterday's pack under
/// today's would be exactly the growing file this stage exists to stop. Atomic, so a session that
/// boots while the bridge writes reads the old pack or the new one, never half of each.
/// </summary>
public static class StatePack_Writer
{
    /// <summary>
    /// THE WHOLE PACK FOR ONE SESSION, in the one place both runners ask for it — read the inputs,
    /// render, write — answering with the file it wrote, or null when it could not write one.
    ///
    /// <para>
    /// GUARDED AS A WHOLE, on top of the reader's own per-section guards: a pack that cannot be
    /// written must never be the reason a turn does not start. The session then finds no pack and
    /// falls back to its boot sequence, which is the behaviour it had before packs existed — and a
    /// wake ticket whose <c>statePackFile</c> is null says exactly that to a terminal session.
    /// </para>
    /// <para>
    /// ONE IMPLEMENTATION, TWO CALLERS: <c>PrintTurnExecutorModel</c> writes it for a bridge-driven
    /// turn it is about to open, and <c>BridgeEngineModel.Sweep_WakeTickets_Async</c> writes it for a
    /// TERMINAL session it is about to ticket (2026-09-15 one-wake-model, Task 11). A second copy of
    /// this three-line body is a second place for the swallow rule and the locator to drift.
    /// </para>
    /// </summary>
    public static string? Write_ForSession_OrNull(
        ISupervisionPaths paths,
        IPrintSessionState state,
        string requestId,
        IReadOnlyList<PendingEntry> pending,
        IReadOnlyList<ITurnSource> sources)
    {
        try
        {
            var packFilePath = StatePack_Locator.Get_File(paths, state.Role, state.OrchId, state.MemberId);

            Write(packFilePath, StatePack_Builder.Build(StatePackInputs_Reader.Read(paths, state, requestId, pending, sources)));

            return packFilePath;
        }
        catch
        {
            // Swallowed by design — see the summary. The session's boot sequence covers the gap.
            return null;
        }
    }

    public static void Write(string packFilePath, string text)
    {
        var folder = Path.GetDirectoryName(packFilePath);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        Atomic_FileWriter.Write_AllText(packFilePath, text);
    }
}

using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>What changed in a ledger between two readings, and whether it is worth a message.</summary>
public sealed record LedgerTransition(IReadOnlyList<string> Finished, IReadOnlyList<string> Started)
{
    public bool IsWorthTelling => Finished.Count > 0 || Started.Count > 0;
}

/// <summary>
/// Turns a ledger into the two events the owner actually wants to hear about: a line FINISHED, and
/// a line STARTED.
///
/// Their ask, 2026-08-20: *"It's quite difficult to follow the sup's or solo's work. I think that I
/// should receive a very short and fast message every time it completes a member of the progress
/// ledger and starts a new one."* Between the half-hourly digest and a full /progress there was
/// nothing that said "this just moved", so following a session meant asking it.
///
/// IT COMPARES TWO READINGS AND REPORTS THE DIFFERENCE, which is what keeps it from becoming the
/// thing this repo spent a whole day removing. A transition happens once, so it is told once: no
/// timer, no cadence, nothing that can fire twice for the same event. The engine's memory of the
/// last reading is the only state involved.
///
/// TOP-LEVEL LINES ONLY. Sub-tasks are the altitude /tasks reports; announcing each one would turn
/// a stage with eleven pieces into eleven messages, which is the waterfall by another name.
///
/// MATCHED ON THE LINE'S TEXT, not its position. A ledger is hand-written and lines get inserted
/// above others all the time; an index-based comparison would report every line below an insertion
/// as having changed.
/// </summary>
public static class LedgerTransition_Detector
{
    public static LedgerTransition Compare(IPlanProgress? previous, IPlanProgress? current)
    {
        if (previous == null || current == null)
            return new LedgerTransition([], []);

        var before = previous.Lines.Where(line => !line.IsSubTask).ToDictionary(line => line.Text, line => line.Marker);

        List<string> finished = [];
        List<string> started = [];

        foreach (var line in current.Lines)
        {
            if (line.IsSubTask)
                continue;

            // A line the previous reading never had is NOT a transition. New work appearing is the
            // supervisor writing its plan, and the owner asked to hear about movement, not authoring.
            if (!before.TryGetValue(line.Text, out var was))
                continue;

            if (was == line.Marker)
                continue;

            if (line.Marker == "x")
                finished.Add(line.Text);
            else if (line.Marker == ">")
                started.Add(line.Text);
        }

        return new LedgerTransition(finished, started);
    }

    /// <summary>
    /// Everything owed is delivered — nothing open, in progress, or blocked. `- [-]` does not hold
    /// it back: a line decided against is resolved, which is the whole reason that marker exists.
    /// </summary>
    public static bool Is_EndOfEndeavour(IPlanProgress? progress)
    {
        if (progress == null || progress.Total <= 0)
            return false;

        return progress.Done == progress.Total;
    }
}

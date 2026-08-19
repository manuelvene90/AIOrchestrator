namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// What a ledger read as the LAST TIME THE OWNER WAS TOLD — the two numbers a delta needs, and
/// deliberately not the whole <see cref="PlanProgress.IPlanProgress"/>.
///
/// It is a snapshot of a MESSAGE, never of the live file. The owner asked for "the difference
/// compared to the previous message" (2026-08-19), so the baseline has to be what was last SENT: a
/// delta against the current file would always be zero, and a delta against the last tick would
/// report a half-hour of work as nothing.
/// </summary>
public sealed record PlanProgressSnapshot(int Done, int Total);

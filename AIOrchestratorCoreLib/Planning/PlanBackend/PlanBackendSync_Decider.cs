using AIOrchestratorCoreLib.Configuration;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// Whether the plan-backend pass should run on this tick at all.
///
/// <para>
/// THE TWO ANSWERS IT EXISTS FOR ARE BOTH LOAD-BEARING CLAIMS. "An installation with no backend
/// configured behaves exactly as it did before" is the property the whole seam is judged on, and it
/// rests on this returning false for the default backend — a claim that was decided by one line inside
/// the engine, where nothing could reach it. And the cadence is the difference between asking somebody
/// else's planning system once a minute and asking it 1,800 times an hour.
/// </para>
/// </summary>
public static class PlanBackendSync_Decider
{
    /// <summary>
    /// How often an external plan backend is asked what was approved upstream. NOT the tick rate: the
    /// bridge ticks every 2 seconds and these calls leave the machine. A request the owner approved is
    /// not urgent to the minute, and a ledger line that closed is reported on the same beat.
    /// </summary>
    public const int SYNC_INTERVAL_SECONDS = 60;

    public static bool Should_Sync(IPlanBackend backend, DateTime lastSyncUtc, DateTime nowUtc)
    {
        // THE DEFAULT PATH RETURNS BEFORE ANY I/O. PlanMdBackend is the null object — it lists nothing
        // and reports nothing — so running the pass for it would spend file probes per orchestration
        // per minute to reach the same nothing.
        if (backend is PlanMdBackend)
            return false;

        return (nowUtc - lastSyncUtc).TotalSeconds >= SYNC_INTERVAL_SECONDS;
    }

    /// <summary>
    /// Whether a newly-loaded backend differs from the one in hand — the reload gate. Settings are a
    /// value, so this is value equality rather than "did the config object change", which is a
    /// different question with a much noisier answer.
    /// </summary>
    public static bool Needs_Reload(bool alreadyLoaded, PlanBackendSettings? loadedFrom, PlanBackendSettings? current)
    {
        return !alreadyLoaded || !Nullable.Equals(loadedFrom, current);
    }
}

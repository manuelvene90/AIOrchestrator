namespace AIOrchestratorCoreLib.Kit.PluginGate;

/// <summary>
/// WHETHER THIS HOST MAY START SESSIONS AT ALL. The bridge does not consult it: a host whose kit is
/// wrong still tails, still mirrors, still answers the owner — that is how they get TOLD. What it
/// gates is the one thing that would be wrong to do quietly, which is handing a session a protocol
/// this host was not built against.
///
/// Two rules meet here and the split is deliberate. Decision 20 — a harness that cannot find what it
/// tests refuses to run — owns the SESSIONS. The comment that has stood in KitAssets_Bootstrapper
/// since it was written, "a bridge that will not start is worse than stale commands", owns the
/// BRIDGE. Neither is weakened: nothing spawns with the wrong kit, and nothing already running is
/// killed for it.
/// </summary>
public interface IPluginGate
{
    PluginVerdicts Verdict { get; }

    /// <summary>Null when spawning is allowed. Non-null is both the reason and the refusal text.</summary>
    string? Refusal { get; }

    bool Spawning_Allowed { get; }

    /// <summary>
    /// Whether the "no verdict was ever taken" warning has already been stated. Read and set by the
    /// launcher so it is said once per host run instead of once per spawn.
    /// </summary>
    bool Unchecked_WasReported { get; set; }

    /// <summary>
    /// Recorded once by the host's startup check, after composition has already built the launcher.
    /// The verdict genuinely is not knowable at construction time — it depends on a Claude home the
    /// composition root is not given — so this is a seam, not a mutable service.
    /// </summary>
    void Record(PluginVerdicts verdict, string? refusal);
}

namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

/// <summary>
/// A request dropped by the GENERAL supervisor (as a .json file under .requests/) asking the app
/// to start a new orchestration. The app is the executor; the general supervisor is the brain.
/// The orchestration id is ALLOCATED BY THE APP (repo-slug-n, incremental) — never requested.
/// </summary>
public interface IStartOrchestrationRequest
{
    /// <summary>The repo the general supervisor resolved (it maps the owner's colloquial phrasing first).</summary>
    string RepoQuery { get; }

    /// <summary>
    /// FULL (supervisor + imp-1) or BASIC (one solo session, no supervisor). Absent means FULL, so
    /// every request written before this field existed keeps working unchanged.
    ///
    /// The capability was built and wired to a UI button but unreachable from the request protocol,
    /// so the owner could not get a basic session by asking the concierge — which is the one route
    /// they actually use from their phone.
    /// </summary>
    bool IsBasic { get; }

    /// <summary>The request file, deleted after processing (success or failure) so it never loops.</summary>
    string SourceFilePath { get; }
}
